-- Illustrative produce prices in USD per package, not live market prices.
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
        (N'Oranges - 1 kg bag',       CAST(3.49 AS DECIMAL(18,2)), CAST(1.000 AS DECIMAL(10,3))),
        (N'Strawberries - 454 g box',  4.99, 0.454),
        (N'Bananas - 1 kg bunch',     1.99, 1.000),
        (N'Kiwi - 6 pack',            4.49, 0.450),
        (N'Lettuce - 1 head',         1.79, 0.500),
        (N'Carrots - 1 kg bag',       1.99, 1.000)
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
