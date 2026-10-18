-- Demo grocery products. Prices are USD per package; weights are illustrative.
-- Existing products with the same name are preserved, never updated.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'nations-db'
    THROW 50010, 'Run only in the intended nations-db database.', 1;
IF @@TRANCOUNT <> 0
    THROW 50011, 'Run outside an existing transaction.', 1;
IF OBJECT_ID(N'logistics.Products', N'U') IS NULL
    THROW 50012, 'The logistics.Products table is missing.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO logistics.Products (Name, Price, WeightKg)
    SELECT seed.Name, seed.Price, seed.WeightKg
    FROM (VALUES
        (N'Apples - 1 kg bag',       CAST(3.99 AS DECIMAL(18,2)), CAST(1.000 AS DECIMAL(10,3))),
        (N'Whole Milk - 1 L',        2.49, 1.030),
        (N'White Bread - 500 g',     2.99, 0.500),
        (N'Eggs - 12 pack',          4.29, 0.720),
        (N'Rice - 2 kg bag',         6.99, 2.000)
    ) AS seed(Name, Price, WeightKg)
    WHERE NOT EXISTS (
        SELECT 1 FROM logistics.Products WITH (UPDLOCK, HOLDLOCK)
        WHERE Name = seed.Name
    );
    DECLARE @InsertedCount INT = @@ROWCOUNT;
    COMMIT TRANSACTION;
    SELECT @InsertedCount AS InsertedCount;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
