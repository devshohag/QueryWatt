using System.Data;
using QueryWatt.Core.Instrumentation;
using Xunit;

namespace QueryWatt.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public sealed class StoredProcedureCaptureTests(SqlServerFixture fixture)
{
    [Fact]
    public void AReadProcedureIsMeasuredAndKeepsItsProcedureIdentity()
    {
        var measured = fixture.Measure("orders.by-customer.procedure", () =>
        {
            using var connection = fixture.OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "dbo.usp_GetOrdersByCustomer";
            command.Parameters.Add("@CustomerId", SqlDbType.UniqueIdentifier).Value =
                TestSchema.PrimaryCustomerId;
            command.Parameters.Add("@PlacedAfter", SqlDbType.DateTime2).Value = TestSchema.SeedEpoch;

            var table = new DataTable();
            using var reader = command.ExecuteReader();
            table.Load(reader);
            return table;
        });

        Assert.Equal(TestSchema.OrdersPerCustomer, measured.Result.Rows.Count);

        var command = MeasurementAssertions.SingleFullyMeasuredCommand(measured.Record);

        Assert.Equal(CommandType.StoredProcedure, command.CommandType);
        Assert.Equal("dbo.usp_GetOrdersByCustomer", command.CommandText);
        Assert.Equal("p[CustomerId:Guid, PlacedAfter:DateTime2]", measured.Record.ScenarioKey);
    }

    [Fact]
    public void AWriteProcedureIsMeasuredOnceAndNeverReExecuted()
    {
        var before = ReadProbeTotal();

        var measured = fixture.Measure("probe.bump.procedure", () =>
        {
            using var connection = fixture.OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "dbo.usp_BumpWriteProbe";
            command.Parameters.Add("@Delta", SqlDbType.Int).Value = 1;

            var affected = command.Parameters.Add("@Affected", SqlDbType.Int);
            affected.Direction = ParameterDirection.Output;

            command.ExecuteNonQuery();
            return (int)affected.Value;
        });

        Assert.Equal(TestSchema.WriteProbeRows, measured.Result);

        var after = ReadProbeTotal();

        // The single most important assertion in the suite: measurement never replays a command
        // that writes, so the data moved by exactly one delta. Contract v2 §5.1.
        Assert.Equal(before + TestSchema.WriteProbeRows, after);

        var command = MeasurementAssertions.SingleFullyMeasuredCommand(measured.Record);

        Assert.Equal(CommandType.StoredProcedure, command.CommandType);
        Assert.Equal(CommandOutcome.Succeeded, command.Outcome);
    }

    private int ReadProbeTotal()
    {
        return fixture.WithoutMeasurement(() =>
        {
            using var connection = fixture.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT SUM(Value) FROM dbo.WriteProbe;";
            return Convert.ToInt32(command.ExecuteScalar());
        });
    }
}
