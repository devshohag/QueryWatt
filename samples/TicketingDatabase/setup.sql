USE master;
GO

IF DB_ID(N'QueryWattSample') IS NULL
BEGIN
    CREATE DATABASE QueryWattSample;
END;
GO

USE QueryWattSample;
GO

DROP TABLE IF EXISTS dbo.Ticket;
GO

CREATE TABLE dbo.Ticket
(
    TicketId INT NOT NULL,
    CustomerId INT NOT NULL,
    StatusCode TINYINT NOT NULL,
    CreatedUtc DATETIME2(0) NOT NULL,
    Notes CHAR(200) NOT NULL,
    CONSTRAINT PK_Ticket PRIMARY KEY CLUSTERED (TicketId)
);
GO

;WITH Numbers AS
(
    SELECT TOP (50000)
        CONVERT(INT, ROW_NUMBER() OVER (ORDER BY first_object.object_id, second_object.object_id)) AS Number
    FROM sys.all_objects AS first_object
    CROSS JOIN sys.all_objects AS second_object
)
INSERT INTO dbo.Ticket
(
    TicketId,
    CustomerId,
    StatusCode,
    CreatedUtc,
    Notes
)
SELECT
    Number,
    ((Number - 1) % 1000) + 1,
    Number % 5,
    DATEADD(MINUTE, Number, CONVERT(DATETIME2(0), '2026-01-01T00:00:00')),
    CONVERT(CHAR(200), CONCAT('Synthetic ticket ', Number))
FROM Numbers;
GO

CREATE NONCLUSTERED INDEX IX_Ticket_CustomerId
    ON dbo.Ticket (CustomerId)
    INCLUDE (StatusCode, CreatedUtc);
GO

UPDATE STATISTICS dbo.Ticket WITH FULLSCAN;
GO
