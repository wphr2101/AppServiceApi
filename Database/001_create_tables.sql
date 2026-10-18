-- SQL Server / Azure SQL Database.
-- Select the intended development database before executing this script.
-- This script creates the logistics schema if needed and its five tables.
-- It does not create or switch databases, or modify existing dbo tables.
-- PasswordHash must be produced by a vetted password hasher, never plain text.

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF @@TRANCOUNT <> 0
    THROW 50000, 'Run this script outside an existing transaction.', 1;

IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
    THROW 50001, 'Select a development database, not a system database.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Stop on any name collision instead of modifying existing objects.
    IF OBJECT_ID(N'logistics.Users') IS NOT NULL
        OR OBJECT_ID(N'logistics.Customers') IS NOT NULL
        OR OBJECT_ID(N'logistics.Products') IS NOT NULL
        OR OBJECT_ID(N'logistics.Orders') IS NOT NULL
        OR OBJECT_ID(N'logistics.OrderItems') IS NOT NULL
        THROW 50002, 'One or more target objects already exist. No changes were made.', 1;

    -- CREATE SCHEMA must run in its own batch, within this transaction.
    IF SCHEMA_ID(N'logistics') IS NULL
        EXEC(N'CREATE SCHEMA logistics AUTHORIZATION dbo;');

    CREATE TABLE logistics.Users
    (
        UserId INT IDENTITY(1, 1) NOT NULL,
        UserName NVARCHAR(100) COLLATE Latin1_General_100_CI_AS NOT NULL,
        PasswordHash NVARCHAR(512) NOT NULL,
        UserType VARCHAR(20) COLLATE Latin1_General_100_BIN2 NOT NULL
            CONSTRAINT DF_Users_UserType DEFAULT ('Customer'),

        CONSTRAINT PK_Users PRIMARY KEY (UserId),
        CONSTRAINT UQ_Users_UserName UNIQUE (UserName),
        CONSTRAINT CK_Users_UserName CHECK (LEN(LTRIM(RTRIM(UserName))) > 0),
        CONSTRAINT CK_Users_PasswordHash CHECK (LEN(LTRIM(RTRIM(PasswordHash))) > 0),
        CONSTRAINT CK_Users_UserType CHECK (UserType IN ('Customer', 'Staff', 'Admin'))
    );

    CREATE TABLE logistics.Customers
    (
        CustomerId INT IDENTITY(1, 1) NOT NULL,
        UserId INT NOT NULL,
        Name NVARCHAR(150) NOT NULL,
        Email NVARCHAR(254) NOT NULL,
        Phone NVARCHAR(30) NULL,

        CONSTRAINT PK_Customers PRIMARY KEY (CustomerId),
        CONSTRAINT UQ_Customers_UserId UNIQUE (UserId),
        CONSTRAINT FK_Customers_Users FOREIGN KEY (UserId)
            REFERENCES logistics.Users (UserId),
        CONSTRAINT CK_Customers_Name CHECK (LEN(LTRIM(RTRIM(Name))) > 0),
        CONSTRAINT CK_Customers_Email CHECK (LEN(LTRIM(RTRIM(Email))) > 0)
    );

    CREATE TABLE logistics.Products
    (
        ProductId INT IDENTITY(1, 1) NOT NULL,
        Name NVARCHAR(200) NOT NULL,
        Price DECIMAL(18, 2) NOT NULL,
        WeightKg DECIMAL(10, 3) NOT NULL,

        CONSTRAINT PK_Products PRIMARY KEY (ProductId),
        CONSTRAINT CK_Products_Name CHECK (LEN(LTRIM(RTRIM(Name))) > 0),
        CONSTRAINT CK_Products_Price CHECK (Price >= 0),
        CONSTRAINT CK_Products_WeightKg CHECK (WeightKg > 0)
    );

    CREATE TABLE logistics.Orders
    (
        OrderId INT IDENTITY(1, 1) NOT NULL,
        CustomerId INT NOT NULL,
        OrderDate DATETIME2(3) NOT NULL
            CONSTRAINT DF_Orders_OrderDate DEFAULT (SYSUTCDATETIME()),
        Status VARCHAR(20) COLLATE Latin1_General_100_BIN2 NOT NULL
            CONSTRAINT DF_Orders_Status DEFAULT ('Pending'),
        RecipientName NVARCHAR(150) NOT NULL,
        ShippingAddress NVARCHAR(500) NOT NULL,

        CONSTRAINT PK_Orders PRIMARY KEY (OrderId),
        CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId)
            REFERENCES logistics.Customers (CustomerId),
        CONSTRAINT CK_Orders_Status CHECK (Status IN ('Pending', 'Shipped', 'Delivered', 'Cancelled')),
        CONSTRAINT CK_Orders_RecipientName CHECK (LEN(LTRIM(RTRIM(RecipientName))) > 0),
        CONSTRAINT CK_Orders_ShippingAddress CHECK (LEN(LTRIM(RTRIM(ShippingAddress))) > 0)
    );

    CREATE TABLE logistics.OrderItems
    (
        OrderItemId INT IDENTITY(1, 1) NOT NULL,
        OrderId INT NOT NULL,
        ProductId INT NOT NULL,
        Quantity INT NOT NULL,
        UnitPrice DECIMAL(18, 2) NOT NULL,

        CONSTRAINT PK_OrderItems PRIMARY KEY (OrderItemId),
        CONSTRAINT UQ_OrderItems_OrderId_ProductId UNIQUE (OrderId, ProductId),
        CONSTRAINT FK_OrderItems_Orders FOREIGN KEY (OrderId)
            REFERENCES logistics.Orders (OrderId),
        CONSTRAINT FK_OrderItems_Products FOREIGN KEY (ProductId)
            REFERENCES logistics.Products (ProductId),
        CONSTRAINT CK_OrderItems_Quantity CHECK (Quantity > 0),
        CONSTRAINT CK_OrderItems_UnitPrice CHECK (UnitPrice >= 0)
    );

    CREATE INDEX IX_Orders_CustomerId_OrderDate
        ON logistics.Orders (CustomerId, OrderDate DESC);

    CREATE INDEX IX_OrderItems_ProductId
        ON logistics.OrderItems (ProductId);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT DB_NAME() AS DatabaseName, N'Created all five logistics tables.' AS Result;
