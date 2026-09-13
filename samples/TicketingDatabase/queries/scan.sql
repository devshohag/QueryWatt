SELECT
    TicketId,
    CustomerId,
    StatusCode,
    CreatedUtc
FROM dbo.Ticket
WHERE CONVERT(DATE, CreatedUtc) = CONVERT(DATE, '2026-01-15');
