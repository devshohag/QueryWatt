SELECT
    TicketId,
    StatusCode,
    CreatedUtc
FROM dbo.Ticket
WHERE CustomerId = @CustomerId;
