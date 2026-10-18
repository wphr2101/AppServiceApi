using Microsoft.Data.SqlClient;

namespace CustomerOperations.Api.Services;

public record Product(int ProductId, string Name, decimal Price, decimal WeightKg, int? StockQuantity = null);

public interface IProductCatalog
{
    Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken);
}

public sealed class ProductCatalogUnavailableException : Exception
{
    public ProductCatalogUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}

public sealed class SqlProductCatalog : IProductCatalog
{
    private readonly IConfiguration _configuration;

    public SqlProductCatalog(IConfiguration configuration) => _configuration = configuration;

    public async Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ProductCatalogUnavailableException("DefaultConnection is not configured.");
        }

        SqlConnectionStringBuilder settings;
        try
        {
            settings = new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = 15 };
        }
        catch (ArgumentException)
        {
            // Do not include a malformed connection string in logs or responses.
            throw new ProductCatalogUnavailableException("DefaultConnection is invalid.");
        }

        try
        {
            await using var connection = new SqlConnection(settings.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "SELECT ProductId, Name, Price, WeightKg, StockQuantity FROM logistics.Products ORDER BY ProductId;",
                connection) { CommandTimeout = 30 };
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var products = new List<Product>();
            while (await reader.ReadAsync(cancellationToken))
            {
                products.Add(new Product(reader.GetInt32(0), reader.GetString(1),
                    reader.GetDecimal(2), reader.GetDecimal(3), reader.GetInt32(4)));
            }
            return products;
        }
        catch (SqlException exception)
        {
            throw new ProductCatalogUnavailableException("Product catalog SQL request failed.", exception);
        }
    }
}
