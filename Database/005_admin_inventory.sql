-- Demo inventory and fulfillment. Does not grant roles, mark payments, or seed stock.
SET XACT_ABORT ON;
IF DB_NAME() <> N'nations-db' THROW 50020, 'Select nations-db.', 1;
IF @@TRANCOUNT <> 0 THROW 50021, 'Run outside an existing transaction.', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    IF COL_LENGTH(N'logistics.Products', N'StockQuantity') IS NULL
        ALTER TABLE logistics.Products ADD StockQuantity INT NOT NULL CONSTRAINT DF_Products_Stock DEFAULT (0)
            CONSTRAINT CK_Products_Stock CHECK (StockQuantity BETWEEN 0 AND 1000000);
    IF COL_LENGTH(N'logistics.Orders', N'PaymentStatus') IS NULL
        ALTER TABLE logistics.Orders ADD PaymentStatus VARCHAR(20) NOT NULL CONSTRAINT DF_Orders_Payment DEFAULT ('Unpaid')
            CONSTRAINT CK_Orders_Payment CHECK (PaymentStatus IN ('Unpaid', 'Paid'));
    IF COL_LENGTH(N'logistics.Orders', N'PaidAt') IS NULL
        ALTER TABLE logistics.Orders ADD PaidAt DATETIME2(3) NULL;
    IF COL_LENGTH(N'logistics.Orders', N'ShippedAt') IS NULL
        ALTER TABLE logistics.Orders ADD ShippedAt DATETIME2(3) NULL;
    IF COL_LENGTH(N'logistics.Orders', N'StockDeducted') IS NULL
        ALTER TABLE logistics.Orders ADD StockDeducted BIT NOT NULL CONSTRAINT DF_Orders_StockDeducted DEFAULT (0);
    IF OBJECT_ID(N'logistics.StockMovements', N'U') IS NULL
    BEGIN
        CREATE TABLE logistics.StockMovements (
            MovementId INT IDENTITY PRIMARY KEY,
            ProductId INT NOT NULL REFERENCES logistics.Products(ProductId),
            QuantityChange INT NOT NULL CHECK (QuantityChange <> 0),
            StockAfter INT NOT NULL CHECK (StockAfter BETWEEN 0 AND 1000000),
            Kind VARCHAR(20) NOT NULL CHECK (Kind IN ('Restock', 'Order')),
            Reason NVARCHAR(250) NOT NULL,
            ActorUserId INT NOT NULL REFERENCES logistics.Users(UserId),
            OrderId INT NULL REFERENCES logistics.Orders(OrderId),
            RequestId UNIQUEIDENTIFIER NULL,
            CreatedAt DATETIME2(3) NOT NULL DEFAULT (SYSUTCDATETIME())
        );
        CREATE UNIQUE INDEX UX_Stock_RestockRequest ON logistics.StockMovements(RequestId) WHERE RequestId IS NOT NULL;
        CREATE UNIQUE INDEX UX_Stock_OrderProduct ON logistics.StockMovements(OrderId, ProductId) WHERE OrderId IS NOT NULL;
    END;
    IF OBJECT_ID(N'logistics.OrderEvents', N'U') IS NULL
    BEGIN
        CREATE TABLE logistics.OrderEvents (
            EventId INT IDENTITY PRIMARY KEY,
            OrderId INT NOT NULL REFERENCES logistics.Orders(OrderId),
            EventType VARCHAR(20) NOT NULL CHECK (EventType IN ('Paid', 'Shipped')),
            ActorUserId INT NOT NULL REFERENCES logistics.Users(UserId),
            CreatedAt DATETIME2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT UQ_OrderEvents_OrderType UNIQUE (OrderId, EventType)
        );
    END;
    COMMIT;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK;
    THROW;
END CATCH;
SELECT N'Admin inventory migration complete. No roles granted, payments recorded or stock added.' AS Result;
