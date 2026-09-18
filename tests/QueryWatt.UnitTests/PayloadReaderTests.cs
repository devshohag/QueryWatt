using System.Data.Common;
using Microsoft.Data.SqlClient;
using QueryWatt.SqlServer.Capture;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class PayloadReaderTests
{
    [Fact]
    public void APayload_YieldsItsCommandAndOperationId()
    {
        using var command = new SqlCommand("SELECT 1");
        var operationId = Guid.NewGuid();

        var payload = new { OperationId = operationId, Command = command };

        Assert.Same(command, PayloadReader.Read<DbCommand>(payload, "Command"));
        Assert.Equal(operationId, PayloadReader.ReadOperationId(payload));
    }

    [Fact]
    public void APayloadWithoutTheMembers_YieldsNothingInsteadOfThrowing()
    {
        var payload = new { Unrelated = 1 };

        Assert.Null(PayloadReader.Read<DbCommand>(payload, "Command"));
        Assert.Equal(Guid.Empty, PayloadReader.ReadOperationId(payload));
    }

    [Fact]
    public void ANullPayload_YieldsNothing()
    {
        Assert.Null(PayloadReader.Read<DbCommand>(null, "Command"));
        Assert.Equal(Guid.Empty, PayloadReader.ReadOperationId(null));
    }

    [Fact]
    public void AMemberOfTheWrongType_YieldsNothing()
    {
        var payload = new { Command = "not a command" };

        Assert.Null(PayloadReader.Read<DbCommand>(payload, "Command"));
    }
}
