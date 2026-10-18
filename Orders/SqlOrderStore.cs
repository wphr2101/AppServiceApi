using System.Data;
using Microsoft.Data.SqlClient;
using CustomerOperations.Api.Services;
using CustomerOperations.Api.Admin;

namespace CustomerOperations.Api.Orders;

public sealed class SqlOrderStore(IConfiguration configuration, ILogger<SqlOrderStore> logger) : IOrderStore
{
    private async Task<T> Execute<T>(Func<SqlConnection, Task<T>> action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var value = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException();
            var settings = new SqlConnectionStringBuilder(value) { ConnectTimeout = 15 };
            await using var connection = new SqlConnection(settings.ConnectionString);
            await connection.OpenAsync(ct);
            return await action(connection);
        }
        catch (SqlException e)
        {
            logger.LogWarning("Order storage error number {Number}; details suppressed.", e.Number);
            if (e.Number is 207 or 208)
                throw new OrderRejectedException("checkout_not_ready", "Checkout database setup is incomplete. Apply migration 004 before ordering.", 503);
            throw new OrderRejectedException("order_unavailable", "Order service temporarily unavailable. Retry this submission without rebuilding the cart.", 503);
        }
        catch (ArgumentException)
        {
            throw new OrderRejectedException("order_unavailable", "Order service configuration is unavailable.", 503);
        }
    }

    public Task<OrderReceipt> PlaceAsync(int userId, PlaceOrderRequest request, CancellationToken ct)
    {
        request = OrderRules.Normalize(request);
        return Execute(async connection =>
        {
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            await using var customer = new SqlCommand("SELECT c.CustomerId FROM logistics.Customers c JOIN logistics.Users u ON u.UserId=c.UserId WHERE c.UserId=@user AND u.UserType IN ('Customer','Admin');", connection, transaction);
            customer.Parameters.Add("@user", SqlDbType.Int).Value = userId;
            var customerValue = await customer.ExecuteScalarAsync(ct);
            if (customerValue is not int customerId)
                throw new OrderRejectedException("customer_required", "A registered customer account is required.", 403);

            var hash = OrderRules.Hash(request);
            int? existingId = null;
            byte[]? existingHash = null;
            // Lock the request key range until commit so concurrent retries cannot insert twice.
            await using (var existing = new SqlCommand("SELECT OrderId, RequestHash FROM logistics.Orders WITH (UPDLOCK, HOLDLOCK) WHERE CustomerId=@customer AND ClientRequestId=@request;", connection, transaction))
            {
                existing.Parameters.Add("@customer", SqlDbType.Int).Value = customerId;
                existing.Parameters.Add("@request", SqlDbType.UniqueIdentifier).Value = request.RequestId;
                await using var reader = await existing.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct)) { existingId = reader.GetInt32(0); existingHash = reader.IsDBNull(1) ? null : (byte[])reader[1]; }
            }
            if (existingId.HasValue)
            {
                if (existingHash is null || !existingHash.SequenceEqual(hash))
                    throw new OrderRejectedException("request_conflict", "This submission ID has already been used for a different order.", 409);
                var receipt = await Read(connection, transaction, userId, existingId.Value, ct);
                await transaction.CommitAsync(ct);
                return receipt!;
            }

            var products = new List<Product>();
            foreach (var item in request.Items)
            {
                await using var product = new SqlCommand("SELECT ProductId, Name, Price, WeightKg FROM logistics.Products WHERE ProductId=@id;", connection, transaction);
                product.Parameters.Add("@id", SqlDbType.Int).Value = item.ProductId;
                await using var reader = await product.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct)) products.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetDecimal(2), reader.GetDecimal(3)));
            }
            var lines = OrderRules.Price(request, products);
            int orderId;
            await using (var order = new SqlCommand("INSERT INTO logistics.Orders (CustomerId, RecipientName, ShippingAddress, ClientRequestId, RequestHash) OUTPUT INSERTED.OrderId VALUES (@customer, @recipient, @address, @request, @hash);", connection, transaction))
            {
                order.Parameters.Add("@customer", SqlDbType.Int).Value = customerId;
                order.Parameters.Add("@recipient", SqlDbType.NVarChar, 150).Value = request.RecipientName;
                order.Parameters.Add("@address", SqlDbType.NVarChar, 500).Value = request.ShippingAddress;
                order.Parameters.Add("@request", SqlDbType.UniqueIdentifier).Value = request.RequestId;
                order.Parameters.Add("@hash", SqlDbType.Binary, 32).Value = hash;
                orderId = (int)(await order.ExecuteScalarAsync(ct))!;
            }
            foreach (var line in lines)
            {
                await using var item = new SqlCommand("INSERT INTO logistics.OrderItems (OrderId, ProductId, Quantity, UnitPrice) VALUES (@order, @product, @quantity, @price);", connection, transaction);
                item.Parameters.Add("@order", SqlDbType.Int).Value = orderId;
                item.Parameters.Add("@product", SqlDbType.Int).Value = line.ProductId;
                item.Parameters.Add("@quantity", SqlDbType.Int).Value = line.Quantity;
                var price = item.Parameters.Add("@price", SqlDbType.Decimal); price.Precision = 18; price.Scale = 2; price.Value = line.UnitPrice;
                await item.ExecuteNonQueryAsync(ct);
            }
            await InventorySql.Deduct(connection, transaction, orderId, userId,
                lines.Select(l => (l.ProductId, l.Quantity)), ct);
            var result = await Read(connection, transaction, userId, orderId, ct);
            await transaction.CommitAsync(ct);
            return result!;
        }, ct);
    }

    public Task<OrderReceipt?> GetAsync(int userId, int orderId, CancellationToken ct) =>
        Execute(connection => Read(connection, null, userId, orderId, ct), ct);

    public Task<OrderPage> ListAsync(int userId, int? beforeId, CancellationToken ct) =>
        Execute(async connection =>
        {
            // Fetch one extra row to indicate another page without loading the full history.
            await using var command = new SqlCommand("""
                SELECT TOP (21) o.OrderId, o.OrderDate, o.Status,
                    COALESCE(SUM(i.Quantity), 0), COALESCE(SUM(i.Quantity * i.UnitPrice), 0)
                    , o.PaymentStatus
                FROM logistics.Orders o
                JOIN logistics.Customers c ON c.CustomerId=o.CustomerId
                JOIN logistics.Users u ON u.UserId=c.UserId
                LEFT JOIN logistics.OrderItems i ON i.OrderId=o.OrderId
                WHERE c.UserId=@user AND u.UserType IN ('Customer','Admin')
                  AND (@before IS NULL OR o.OrderId < @before)
                GROUP BY o.OrderId, o.OrderDate, o.Status, o.PaymentStatus
                ORDER BY o.OrderId DESC;
                """, connection);
            command.Parameters.Add("@user", SqlDbType.Int).Value = userId;
            command.Parameters.Add("@before", SqlDbType.Int).Value = (object?)beforeId ?? DBNull.Value;
            var items = new List<OrderSummary>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                items.Add(new(reader.GetInt32(0), DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc),
                    reader.GetString(2), reader.GetInt32(3), reader.GetDecimal(4)) { PaymentStatus = reader.GetString(5) });
            return new OrderPage(items.Take(20).ToArray(), items.Count > 20);
        }, ct);

    internal static async Task<OrderReceipt?> Read(SqlConnection connection, SqlTransaction? transaction, int userId, int orderId, CancellationToken ct, bool admin = false)
    {
        await using var command = new SqlCommand("""
            SELECT o.OrderId, o.OrderDate, o.Status, o.RecipientName, o.ShippingAddress,
                   i.ProductId, p.Name, i.Quantity, i.UnitPrice, o.PaymentStatus, o.PaidAt, o.ShippedAt
            FROM logistics.Orders o JOIN logistics.Customers c ON c.CustomerId=o.CustomerId
            JOIN logistics.Users u ON u.UserId=c.UserId
            JOIN logistics.OrderItems i ON i.OrderId=o.OrderId
            JOIN logistics.Products p ON p.ProductId=i.ProductId
            WHERE o.OrderId=@order AND
              ((c.UserId=@user AND u.UserType IN ('Customer','Admin')) OR
               (@admin=1 AND EXISTS(SELECT 1 FROM logistics.Users a WHERE a.UserId=@user AND a.UserType='Admin')))
            ORDER BY i.ProductId;
            """, connection, transaction);
        command.Parameters.Add("@order", SqlDbType.Int).Value = orderId;
        command.Parameters.Add("@user", SqlDbType.Int).Value = userId;
        command.Parameters.Add("@admin", SqlDbType.Bit).Value = admin;
        await using var reader = await command.ExecuteReaderAsync(ct);
        OrderReceipt? result = null;
        var lines = new List<OrderLine>();
        while (await reader.ReadAsync(ct))
        {
            result ??= new(reader.GetInt32(0), DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc), reader.GetString(2), reader.GetString(3), reader.GetString(4), lines)
            {
                PaymentStatus = reader.GetString(9),
                PaidAt = reader.IsDBNull(10) ? null : DateTime.SpecifyKind(reader.GetDateTime(10), DateTimeKind.Utc),
                ShippedAt = reader.IsDBNull(11) ? null : DateTime.SpecifyKind(reader.GetDateTime(11), DateTimeKind.Utc)
            };
            lines.Add(new(reader.GetInt32(5), reader.GetString(6), reader.GetInt32(7), reader.GetDecimal(8)));
        }
        return result;
    }
}
