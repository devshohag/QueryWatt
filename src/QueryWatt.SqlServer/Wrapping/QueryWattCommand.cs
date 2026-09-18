using System.Data;
using System.Data.Common;

namespace QueryWatt.SqlServer.Wrapping;

/// <summary>
/// A pass-through <see cref="DbCommand"/> whose readers are wrapped in
/// <see cref="QueryWattDataReader"/>. It changes nothing about the command itself: same text, same
/// parameters, same execution, same results.
/// </summary>
public sealed class QueryWattCommand : DbCommand
{
    private readonly DbCommand _inner;

    private QueryWattConnection? _connection;

    internal QueryWattCommand(DbCommand inner, QueryWattConnection? connection)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _connection = connection;
    }

    /// <summary>The command this instance delegates to.</summary>
    public DbCommand Inner => _inner;

    /// <summary>Wraps a command, or returns it unchanged if it is already wrapped.</summary>
    /// <param name="command">The provider command to wrap.</param>
    /// <returns>A command whose readers drain before closing.</returns>
    /// <remarks>
    /// Some frameworks — NHibernate among them — create their commands from a driver rather than
    /// from the connection, so they need to wrap the command themselves for a wrapped connection to
    /// be accepted.
    /// </remarks>
    public static QueryWattCommand Wrap(DbCommand command) =>
        command as QueryWattCommand ?? new QueryWattCommand(command, connection: null);

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string CommandText
    {
        get => _inner.CommandText;
        set => _inner.CommandText = value;
    }

    /// <inheritdoc />
    public override int CommandTimeout
    {
        get => _inner.CommandTimeout;
        set => _inner.CommandTimeout = value;
    }

    /// <inheritdoc />
    public override CommandType CommandType
    {
        get => _inner.CommandType;
        set => _inner.CommandType = value;
    }

    /// <inheritdoc />
    public override bool DesignTimeVisible
    {
        get => _inner.DesignTimeVisible;
        set => _inner.DesignTimeVisible = value;
    }

    /// <inheritdoc />
    public override UpdateRowSource UpdatedRowSource
    {
        get => _inner.UpdatedRowSource;
        set => _inner.UpdatedRowSource = value;
    }

    /// <inheritdoc />
    protected override DbConnection? DbConnection
    {
        get => _connection is not null ? _connection : _inner.Connection;
        set
        {
            switch (value)
            {
                case QueryWattConnection wrapped:
                    _connection = wrapped;
                    _inner.Connection = wrapped.Inner;
                    break;
                case null:
                    _connection = null;
                    _inner.Connection = null;
                    break;
                default:
                    _connection = null;
                    _inner.Connection = value;
                    break;
            }
        }
    }

    /// <inheritdoc />
    protected override DbParameterCollection DbParameterCollection => _inner.Parameters;

    /// <inheritdoc />
    protected override DbTransaction? DbTransaction
    {
        get => _inner.Transaction is { } transaction ? QueryWattTransaction.For(transaction, _connection) : null;
        set => _inner.Transaction = value is QueryWattTransaction wrapped ? wrapped.Inner : value;
    }

    /// <inheritdoc />
    public override void Cancel() => _inner.Cancel();

    /// <inheritdoc />
    public override int ExecuteNonQuery() => _inner.ExecuteNonQuery();

    /// <inheritdoc />
    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) =>
        _inner.ExecuteNonQueryAsync(cancellationToken);

    /// <inheritdoc />
    public override object? ExecuteScalar() => _inner.ExecuteScalar();

    /// <inheritdoc />
    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) =>
        _inner.ExecuteScalarAsync(cancellationToken);

    /// <inheritdoc />
    public override void Prepare() => _inner.Prepare();

    /// <inheritdoc />
    public override Task PrepareAsync(CancellationToken cancellationToken = default) =>
        _inner.PrepareAsync(cancellationToken);

    /// <inheritdoc />
    protected override DbParameter CreateDbParameter() => _inner.CreateParameter();

    /// <inheritdoc />
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
        new QueryWattDataReader(_inner.ExecuteReader(behavior));

    /// <inheritdoc />
    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior,
        CancellationToken cancellationToken)
    {
        var reader = await _inner.ExecuteReaderAsync(behavior, cancellationToken).ConfigureAwait(false);
        return new QueryWattDataReader(reader);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
