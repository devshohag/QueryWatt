namespace QueryWatt.IntegrationTests;

/// <summary>
/// A small, deterministic schema and data set. Row counts are fixed so a measurement taken twice
/// is comparable — the same reason the CLI runner verifies its seed before measuring.
/// </summary>
internal static class TestSchema
{
    public static readonly Guid PrimaryCustomerId =
        Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    public static readonly Guid SecondaryCustomerId =
        Guid.Parse("6b29fc40-ca47-1067-b31d-00dd010662da");

    public const int OrdersPerCustomer = 25;
    public const int LinesPerOrder = 3;
    public const int ProductCount = 40;
    public const int WriteProbeRows = 10;

    public static readonly DateTime SeedEpoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public const string Script = """
        CREATE TABLE dbo.Customer
        (
            CustomerId  uniqueidentifier NOT NULL CONSTRAINT PK_Customer PRIMARY KEY,
            DisplayName nvarchar(200)    NOT NULL,
            Email       nvarchar(256)    NOT NULL,
            IsActive    bit              NOT NULL
        );

        CREATE UNIQUE INDEX UQ_Customer_Email ON dbo.Customer (Email);

        CREATE TABLE dbo.[Order]
        (
            OrderId        bigint IDENTITY(1, 1) CONSTRAINT PK_Order PRIMARY KEY,
            CustomerId     uniqueidentifier NOT NULL,
            OrderNumber    nvarchar(40)     NOT NULL,
            PlacedOn       datetime2        NOT NULL,
            TotalAmount    decimal(18, 2)   NOT NULL,
            StatusId       int              NOT NULL,
            PromisedShipOn datetime2        NULL
        );

        CREATE INDEX IX_Order_Customer_PlacedOn
            ON dbo.[Order] (CustomerId, PlacedOn)
            INCLUDE (OrderNumber, TotalAmount, StatusId, PromisedShipOn);

        CREATE TABLE dbo.OrderLine
        (
            OrderLineId bigint IDENTITY(1, 1) CONSTRAINT PK_OrderLine PRIMARY KEY,
            OrderId     bigint         NOT NULL,
            Sku         nvarchar(40)   NOT NULL,
            Quantity    int            NOT NULL,
            UnitPrice   decimal(18, 2) NOT NULL
        );

        CREATE INDEX IX_OrderLine_Order ON dbo.OrderLine (OrderId);

        CREATE TABLE dbo.Product
        (
            ProductId int IDENTITY(1, 1) CONSTRAINT PK_Product PRIMARY KEY,
            Sku       nvarchar(40)   NOT NULL,
            Name      nvarchar(200)  NOT NULL,
            ListPrice decimal(18, 2) NOT NULL,
            IsActive  bit            NOT NULL
        );

        CREATE INDEX IX_Product_Name ON dbo.Product (Name) INCLUDE (Sku, ListPrice);

        CREATE TABLE dbo.WriteProbe
        (
            WriteProbeId int IDENTITY(1, 1) CONSTRAINT PK_WriteProbe PRIMARY KEY,
            Value        int NOT NULL
        );
        """;

    public const string Procedures = """
        CREATE PROCEDURE dbo.usp_GetOrdersByCustomer
            @CustomerId  uniqueidentifier,
            @PlacedAfter datetime2
        AS
        BEGIN
            SELECT o.OrderId, o.OrderNumber, o.PlacedOn, o.TotalAmount, o.StatusId
            FROM   dbo.[Order] o
            WHERE  o.CustomerId = @CustomerId
              AND  o.PlacedOn  >= @PlacedAfter
            ORDER BY o.PlacedOn DESC;
        END;
        """;

    public const string WriteProcedure = """
        CREATE PROCEDURE dbo.usp_BumpWriteProbe
            @Delta    int,
            @Affected int OUTPUT
        AS
        BEGIN
            UPDATE dbo.WriteProbe SET Value = Value + @Delta;
            SET @Affected = @@ROWCOUNT;
        END;
        """;

    /// <summary>
    /// A table big enough that an index seek and a table scan cost visibly different amounts.
    /// </summary>
    /// <remarks>
    /// The other tables here hold tens of rows, which is right for proving that a measurement is
    /// captured at all — but on tens of rows every plan fits in a couple of pages, so a real
    /// regression is indistinguishable from noise. Guard tests need a table where the difference is
    /// unmistakable, which is also why QueryWatt refuses to judge queries below a floor of reads.
    /// </remarks>
    public const string GuardProbe = """
        IF OBJECT_ID('dbo.GuardProbe', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.GuardProbe
            (
                GuardProbeId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_GuardProbe PRIMARY KEY,
                Bucket       int           NOT NULL,
                Payload      nvarchar(200) NOT NULL
            );

            WITH numbers AS
            (
                SELECT TOP (20000)
                       ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
                FROM   sys.all_objects a
                CROSS JOIN sys.all_objects b
            )
            INSERT INTO dbo.GuardProbe (Bucket, Payload)
            SELECT n % 200, REPLICATE(N'x', 100)
            FROM   numbers;

            CREATE NONCLUSTERED INDEX IX_GuardProbe_Bucket
                ON dbo.GuardProbe (Bucket) INCLUDE (Payload);
        END
        """;

    public const string Seed = """
        INSERT INTO dbo.Customer (CustomerId, DisplayName, Email, IsActive)
        VALUES (@PrimaryCustomerId,   N'Northwind Retail', N'orders@northwind.test',  1),
               (@SecondaryCustomerId, N'Seaside Supply',   N'buying@seaside.test',    1);

        DECLARE @orderIndex int = 0;
        WHILE @orderIndex < @OrdersPerCustomer
        BEGIN
            INSERT INTO dbo.[Order]
                (CustomerId, OrderNumber, PlacedOn, TotalAmount, StatusId, PromisedShipOn)
            VALUES
                (@PrimaryCustomerId,
                 CONCAT(N'NW-', RIGHT(N'0000' + CAST(@orderIndex AS nvarchar(4)), 4)),
                 DATEADD(day, @orderIndex, @SeedEpoch),
                 100.00 + @orderIndex,
                 CASE WHEN @orderIndex % 3 = 0 THEN 1 ELSE 2 END,
                 DATEADD(day, @orderIndex + 5, @SeedEpoch)),
                (@SecondaryCustomerId,
                 CONCAT(N'SS-', RIGHT(N'0000' + CAST(@orderIndex AS nvarchar(4)), 4)),
                 DATEADD(day, @orderIndex, @SeedEpoch),
                 250.00 + @orderIndex,
                 2,
                 NULL);

            SET @orderIndex = @orderIndex + 1;
        END;

        INSERT INTO dbo.OrderLine (OrderId, Sku, Quantity, UnitPrice)
        SELECT o.OrderId,
               CONCAT(N'SKU-', RIGHT(N'000' + CAST(line.n AS nvarchar(3)), 3)),
               line.n,
               9.99 * line.n
        FROM   dbo.[Order] o
        CROSS JOIN (SELECT 1 AS n UNION ALL SELECT 2 UNION ALL SELECT 3) AS line;

        DECLARE @productIndex int = 0;
        WHILE @productIndex < @ProductCount
        BEGIN
            INSERT INTO dbo.Product (Sku, Name, ListPrice, IsActive)
            VALUES (CONCAT(N'SKU-', RIGHT(N'000' + CAST(@productIndex AS nvarchar(3)), 3)),
                    CONCAT(N'Widget ', RIGHT(N'000' + CAST(@productIndex AS nvarchar(3)), 3)),
                    19.99 + @productIndex,
                    1);
            SET @productIndex = @productIndex + 1;
        END;

        DECLARE @probeIndex int = 0;
        WHILE @probeIndex < @WriteProbeRows
        BEGIN
            INSERT INTO dbo.WriteProbe (Value) VALUES (@probeIndex);
            SET @probeIndex = @probeIndex + 1;
        END;
        """;
}
