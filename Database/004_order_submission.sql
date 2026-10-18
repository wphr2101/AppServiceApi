-- Add durable retry protection to existing orders. Does not create orders or change old orders.
SET XACT_ABORT ON;
IF DB_NAME() <> N'nations-db'
    THROW 50010, 'Select nations-db before running this migration.', 1;
IF OBJECT_ID(N'logistics.Orders', N'U') IS NULL
    THROW 50011, 'The logistics.Orders table is required.', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    IF COL_LENGTH(N'logistics.Orders', N'ClientRequestId') IS NULL
        ALTER TABLE logistics.Orders ADD ClientRequestId UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH(N'logistics.Orders', N'RequestHash') IS NULL
        ALTER TABLE logistics.Orders ADD RequestHash BINARY(32) NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'logistics.Orders') AND name = N'UX_Orders_Customer_Request')
        EXEC(N'CREATE UNIQUE INDEX UX_Orders_Customer_Request ON logistics.Orders (CustomerId, ClientRequestId) WHERE ClientRequestId IS NOT NULL;');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
SELECT N'Order submission migration complete. No orders created.' AS Result;
