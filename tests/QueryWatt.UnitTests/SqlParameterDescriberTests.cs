using System.Data.Common;
using System.Data;
using Microsoft.Data.SqlClient;
using QueryWatt.SqlServer.Capture;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class SqlParameterDescriberTests
{
    [Fact]
    public void Describe_CapturesShapeAndNeverTheValue()
    {
        using var command = new SqlCommand("SELECT 1");
        command.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.UniqueIdentifier)
        {
            Value = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301")
        });
        command.Parameters.Add(new SqlParameter("@Keyword", SqlDbType.NVarChar, 100)
        {
            Value = "widget"
        });

        var described = SqlParameterDescriber.Describe(command);

        Assert.Equal(2, described.Count);

        var customerId = described[0];
        Assert.Equal("@CustomerId", customerId.Name);
        Assert.Equal(DbType.Guid, customerId.Type);
        Assert.False(customerId.IsNull);

        var keyword = described[1];
        Assert.Equal(DbType.String, keyword.Type);
        Assert.Equal(100, keyword.Size);

        // The record carries no member that could hold a value.
        Assert.DoesNotContain(
            typeof(QueryWatt.Core.Instrumentation.CommandParameterInfo).GetProperties(),
            property => property.Name.Contains("Value", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Describe_MarksNullAndDbNullAsNull()
    {
        using var command = new SqlCommand("SELECT 1");
        command.Parameters.Add(new SqlParameter("@CategoryId", SqlDbType.Int) { Value = DBNull.Value });
        command.Parameters.Add(new SqlParameter("@From", SqlDbType.DateTime2) { Value = null });

        var described = SqlParameterDescriber.Describe(command);

        Assert.All(described, parameter => Assert.True(parameter.IsNull));
    }

    [Fact]
    public void Describe_KeepsPrecisionAndScaleWhenSet()
    {
        using var command = new SqlCommand("SELECT 1");
        command.Parameters.Add(new SqlParameter("@Amount", SqlDbType.Decimal)
        {
            Precision = 18,
            Scale = 2,
            Value = 10.5m
        });

        var described = Assert.Single(SqlParameterDescriber.Describe(command));

        Assert.Equal((byte)18, described.Precision);
        Assert.Equal((byte)2, described.Scale);
    }

    [Fact]
    public void Describe_HandlesNoParametersAndNoCommand()
    {
        using var command = new SqlCommand("SELECT 1");

        Assert.Empty(SqlParameterDescriber.Describe(command));
        Assert.Empty(SqlParameterDescriber.Describe((DbCommand?)null));
    }
}
