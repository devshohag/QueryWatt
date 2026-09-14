SELECT
    TicketId,
    StatusCode,
    CreatedUtc
FROM dbo.Ticket
WHERE CustomerId + 0 = @CustomerId;
