;WITH RankedTickets AS
(
    SELECT
        TicketId,
        ROW_NUMBER() OVER
        (
            ORDER BY REVERSE(Notes), TicketId
        ) AS SortRow
    FROM dbo.Ticket
)
SELECT
    MAX(SortRow) AS RowsSorted
FROM RankedTickets
OPTION
(
    MAX_GRANT_PERCENT = 0.1,
    MAXDOP 1,
    USE HINT('DISABLE_ROW_MODE_MEMORY_GRANT_FEEDBACK')
);
