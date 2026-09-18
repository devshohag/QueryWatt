using System.Collections;
using System.Data;
using System.Data.Common;
using QueryWatt.Core.Instrumentation;

namespace QueryWatt.SqlServer.Wrapping;

/// <summary>
/// A pass-through <see cref="DbDataReader"/> that advances past the last result set before the
/// reader closes, so the driver raises the trailing <c>STATISTICS IO/TIME</c> messages instead of
/// discarding them.
/// </summary>
/// <remarks>
/// SqlClient reads and throws away whatever is left in the stream when a reader is closed without
/// being drained, and the statistics for a statement arrive after its last result set. That is why
/// Entity Framework Core, NHibernate and hand-written reader loops produce no server metrics under
/// plain <see cref="Capture.SqlClientCapture"/>. This reader closes that gap without touching the
/// application's own reads: every member below delegates, the rows the caller sees are the rows the
/// driver produced, and the drain happens only once the caller is finished.
/// <para>
/// The drain runs only when the caller has already read to the end of a result set. A caller that
/// abandons a reader early keeps that saving: QueryWatt will not read rows the application chose to
/// skip, and the measurement is simply reported without server metrics.
/// </para>
/// </remarks>
public sealed class QueryWattDataReader : DbDataReader
{
    private readonly DbDataReader _inner;

    private bool _exhausted;
    private bool _flushed;

    internal QueryWattDataReader(DbDataReader inner) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>The reader this instance delegates to.</summary>
    public DbDataReader Inner => _inner;

    /// <inheritdoc />
    public override int FieldCount => _inner.FieldCount;

    /// <inheritdoc />
    public override int Depth => _inner.Depth;

    /// <inheritdoc />
    public override bool HasRows => _inner.HasRows;

    /// <inheritdoc />
    public override bool IsClosed => _inner.IsClosed;

    /// <inheritdoc />
    public override int RecordsAffected => _inner.RecordsAffected;

    /// <inheritdoc />
    public override int VisibleFieldCount => _inner.VisibleFieldCount;

    /// <inheritdoc />
    public override object this[int ordinal] => _inner[ordinal];

    /// <inheritdoc />
    public override object this[string name] => _inner[name];

    /// <inheritdoc />
    public override bool Read()
    {
        var read = _inner.Read();
        if (!read)
        {
            _exhausted = true;
        }

        return read;
    }

    /// <inheritdoc />
    public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        var read = await _inner.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!read)
        {
            _exhausted = true;
        }

        return read;
    }

    /// <inheritdoc />
    public override bool NextResult()
    {
        var moved = _inner.NextResult();
        _exhausted = !moved;
        return moved;
    }

    /// <inheritdoc />
    public override async Task<bool> NextResultAsync(CancellationToken cancellationToken)
    {
        var moved = await _inner.NextResultAsync(cancellationToken).ConfigureAwait(false);
        _exhausted = !moved;
        return moved;
    }

    /// <inheritdoc />
    public override void Close()
    {
        Flush();
        _inner.Close();
    }

    /// <inheritdoc />
    public override async Task CloseAsync()
    {
        await FlushAsync().ConfigureAwait(false);
        await _inner.CloseAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await FlushAsync().ConfigureAwait(false);
        await _inner.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override bool GetBoolean(int ordinal) => _inner.GetBoolean(ordinal);

    /// <inheritdoc />
    public override byte GetByte(int ordinal) => _inner.GetByte(ordinal);

    /// <inheritdoc />
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) =>
        _inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);

    /// <inheritdoc />
    public override char GetChar(int ordinal) => _inner.GetChar(ordinal);

    /// <inheritdoc />
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) =>
        _inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);

    /// <inheritdoc />
    public override string GetDataTypeName(int ordinal) => _inner.GetDataTypeName(ordinal);

    /// <inheritdoc />
    public override DateTime GetDateTime(int ordinal) => _inner.GetDateTime(ordinal);

    /// <inheritdoc />
    public override decimal GetDecimal(int ordinal) => _inner.GetDecimal(ordinal);

    /// <inheritdoc />
    public override double GetDouble(int ordinal) => _inner.GetDouble(ordinal);

    /// <inheritdoc />
    public override Type GetFieldType(int ordinal) => _inner.GetFieldType(ordinal);

    /// <inheritdoc />
    public override T GetFieldValue<T>(int ordinal) => _inner.GetFieldValue<T>(ordinal);

    /// <inheritdoc />
    public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken) =>
        _inner.GetFieldValueAsync<T>(ordinal, cancellationToken);

    /// <inheritdoc />
    public override float GetFloat(int ordinal) => _inner.GetFloat(ordinal);

    /// <inheritdoc />
    public override Guid GetGuid(int ordinal) => _inner.GetGuid(ordinal);

    /// <inheritdoc />
    public override short GetInt16(int ordinal) => _inner.GetInt16(ordinal);

    /// <inheritdoc />
    public override int GetInt32(int ordinal) => _inner.GetInt32(ordinal);

    /// <inheritdoc />
    public override long GetInt64(int ordinal) => _inner.GetInt64(ordinal);

    /// <inheritdoc />
    public override string GetName(int ordinal) => _inner.GetName(ordinal);

    /// <inheritdoc />
    public override int GetOrdinal(string name) => _inner.GetOrdinal(name);

    /// <inheritdoc />
    public override string GetString(int ordinal) => _inner.GetString(ordinal);

    /// <inheritdoc />
    public override Stream GetStream(int ordinal) => _inner.GetStream(ordinal);

    /// <inheritdoc />
    public override TextReader GetTextReader(int ordinal) => _inner.GetTextReader(ordinal);

    /// <inheritdoc />
    public override object GetValue(int ordinal) => _inner.GetValue(ordinal);

    /// <inheritdoc />
    public override int GetValues(object[] values) => _inner.GetValues(values);

    /// <inheritdoc />
    public override bool IsDBNull(int ordinal) => _inner.IsDBNull(ordinal);

    /// <inheritdoc />
    public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) =>
        _inner.IsDBNullAsync(ordinal, cancellationToken);

    /// <inheritdoc />
    public override DataTable? GetSchemaTable() => _inner.GetSchemaTable();

    /// <inheritdoc />
    public override Type GetProviderSpecificFieldType(int ordinal) =>
        _inner.GetProviderSpecificFieldType(ordinal);

    /// <inheritdoc />
    public override object GetProviderSpecificValue(int ordinal) =>
        _inner.GetProviderSpecificValue(ordinal);

    /// <inheritdoc />
    public override int GetProviderSpecificValues(object[] values) =>
        _inner.GetProviderSpecificValues(values);

    /// <inheritdoc />
    public override IEnumerator GetEnumerator() => _inner.GetEnumerator();

    /// <inheritdoc />
    protected override DbDataReader GetDbDataReader(int ordinal) => _inner.GetData(ordinal);

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Flush();
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private bool ShouldFlush()
    {
        if (_flushed || _inner.IsClosed)
        {
            return false;
        }

        _flushed = true;

        // Reading on past a result set the caller abandoned would make measurement cost real work,
        // so QueryWatt only finishes a stream the caller already finished.
        return _exhausted
               && Watt.Instrumentation == InstrumentationLevel.Full
               && MeasurementContext.Current is not null;
    }

    private void Flush()
    {
        if (!ShouldFlush())
        {
            return;
        }

        try
        {
            while (_inner.NextResult())
            {
            }
        }
        catch (Exception)
        {
            // Measurement may never change what the application sees, including its exceptions.
        }
    }

    private async ValueTask FlushAsync()
    {
        if (!ShouldFlush())
        {
            return;
        }

        try
        {
            while (await _inner.NextResultAsync().ConfigureAwait(false))
            {
            }
        }
        catch (Exception)
        {
            // See Flush().
        }
    }
}
