IF OBJECT_ID(N'dbo.Orders', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Orders (
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        Reference uniqueidentifier NOT NULL UNIQUE,
        Sku nvarchar(64) NOT NULL,
        Price decimal(18,2) NOT NULL,
        Currency char(3) NOT NULL
    );
END;
