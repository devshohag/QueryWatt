using System.Collections;
using System.Data.Common;
using QueryWatt.Core.Instrumentation;
using QueryWatt.SqlServer.Wrapping;
using Xunit;

namespace QueryWatt.UnitTests;

[Collection(InstrumentationCollection.Name)]
public sealed class QueryWattDataReaderTests : IDisposable
{
    public void Dispose() => Watt.Reset();

    [Fact]
    public void TheReaderPassesRowsThroughUnchanged()
    {
        var inner = new FakeDataReader(rows: 3);
        using var reader = new QueryWattDataReader(inner);

        var rows = 0;
        while (reader.Read())
        {
            rows++;
        }

        Assert.Equal(3, rows);
        Assert.Equal(1, reader.FieldCount);
    }

    [Fact]
    public void ADrainedReaderFinishesItsStreamSoTheServerCanReport()
    {
        Watt.Instrumentation = InstrumentationLevel.Full;

        var inner = new FakeDataReader(rows: 2);

        using (var scope = Watt.Measure("wrapper.drained"))
        {
            using (var reader = new QueryWattDataReader(inner))
            {
                while (reader.Read())
                {
                }
            }

            scope.Complete();
        }

        Assert.True(inner.NextResultCalls > 0);
    }

    [Fact]
    public void AReaderTheCallerAbandonedIsNotDrained()
    {
        Watt.Instrumentation = InstrumentationLevel.Full;

        var inner = new FakeDataReader(rows: 1_000);

        using (var scope = Watt.Measure("wrapper.abandoned"))
        {
            using (var reader = new QueryWattDataReader(inner))
            {
                reader.Read();
            }

            scope.Complete();
        }

        // Measurement never makes the application pay for rows it chose not to read.
        Assert.Equal(0, inner.NextResultCalls);
    }

    [Fact]
    public void NothingIsDrainedWhileInstrumentationIsOff()
    {
        Watt.Instrumentation = InstrumentationLevel.Off;

        var inner = new FakeDataReader(rows: 2);

        using (var reader = new QueryWattDataReader(inner))
        {
            while (reader.Read())
            {
            }
        }

        Assert.Equal(0, inner.NextResultCalls);
    }

    private sealed class FakeDataReader(int rows) : DbDataReader
    {
        private int _read;
        private bool _closed;

        public int NextResultCalls { get; private set; }

        public override int FieldCount => 1;

        public override int Depth => 0;

        public override bool HasRows => rows > 0;

        public override bool IsClosed => _closed;

        public override int RecordsAffected => -1;

        public override object this[int ordinal] => _read;

        public override object this[string name] => _read;

        public override bool Read() => _read++ < rows;

        public override bool NextResult()
        {
            NextResultCalls++;
            return false;
        }

        public override void Close() => _closed = true;

        public override bool GetBoolean(int ordinal) => false;

        public override byte GetByte(int ordinal) => 0;

        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;

        public override char GetChar(int ordinal) => '\0';

        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;

        public override string GetDataTypeName(int ordinal) => "int";

        public override DateTime GetDateTime(int ordinal) => DateTime.UnixEpoch;

        public override decimal GetDecimal(int ordinal) => 0m;

        public override double GetDouble(int ordinal) => 0d;

        public override Type GetFieldType(int ordinal) => typeof(int);

        public override float GetFloat(int ordinal) => 0f;

        public override Guid GetGuid(int ordinal) => Guid.Empty;

        public override short GetInt16(int ordinal) => 0;

        public override int GetInt32(int ordinal) => _read;

        public override long GetInt64(int ordinal) => _read;

        public override string GetName(int ordinal) => "value";

        public override int GetOrdinal(string name) => 0;

        public override string GetString(int ordinal) => _read.ToString();

        public override object GetValue(int ordinal) => _read;

        public override int GetValues(object[] values) => 0;

        public override bool IsDBNull(int ordinal) => false;

        public override IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
    }
}
