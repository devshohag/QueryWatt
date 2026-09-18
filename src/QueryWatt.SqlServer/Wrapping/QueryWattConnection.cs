using System.Data;
using System.Data.Common;

namespace QueryWatt.SqlServer.Wrapping;

/// <summary>
/// Wraps the application's own <see cref="DbConnection"/> so that the readers it produces finish
/// their result streams before closing. Everything else — the connection string, the pool, the
/// transactions, the commands, the results — is the provider's, untouched.
/// </summary>
/// <remarks>
/// This is the one line a developer adds to make Entity Framework Core, NHibernate and
/// hand-written reader loops fully measurable:
/// <code>
/// using var connection = QueryWattConnection.Wrap(new SqlConnection(connectionString));
/// </code>
/// It is only ever needed alongside Full instrumentation, which is a development and test setting.
/// With instrumentation off the wrapper costs one extra virtual call per member and changes no
/// behaviour at all, so it is safe to leave in place.
/// </remarks>
public sealed class QueryWattConnection : DbConnection
{
    private readonly DbConnection _inner;
    private readonly bool _ownsInner;

    /// <summary>Wraps <paramref name="inner"/>, which this connection then owns and disposes.</summary>
    /// <param name="inner">The provider connection to delegate to.</param>
    public QueryWattConnection(DbConnection inner)
        : this(inner, ownsInner: true)
    {
    }

    private QueryWattConnection(DbConnection inner, bool ownsInner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _ownsInner = ownsInner;
        _inner.StateChange += OnInnerStateChange;
    }

    /// <summary>The connection this instance delegates to.</summary>
    public DbConnection Inner => _inner;

    /// <summary>Wraps a connection, or returns it unchanged if it is already wrapped.</summary>
    /// <param name="connection">The provider connection to wrap.</param>
    /// <returns>A connection whose readers drain before closing.</returns>
    public static QueryWattConnection Wrap(DbConnection connection) =>
        connection as QueryWattConnection ?? new QueryWattConnection(connection);

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ConnectionString
    {
        get => _inner.ConnectionString;
        set => _inner.ConnectionString = value!;
    }

    /// <inheritdoc />
    public override int ConnectionTimeout => _inner.ConnectionTimeout;

    /// <inheritdoc />
    public override string Database => _inner.Database;

    /// <inheritdoc />
    public override string DataSource => _inner.DataSource;

    /// <inheritdoc />
    public override string ServerVersion => _inner.ServerVersion;

    /// <inheritdoc />
    public override ConnectionState State => _inner.State;

    /// <inheritdoc />
    public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);

    /// <inheritdoc />
    public override Task ChangeDatabaseAsync(string databaseName, CancellationToken cancellationToken = default) =>
        _inner.ChangeDatabaseAsync(databaseName, cancellationToken);

    /// <inheritdoc />
    public override void Open() => _inner.Open();

    /// <inheritdoc />
    public override Task OpenAsync(CancellationToken cancellationToken) => _inner.OpenAsync(cancellationToken);

    /// <inheritdoc />
    public override void Close() => _inner.Close();

    /// <inheritdoc />
    public override Task CloseAsync() => _inner.CloseAsync();

    /// <inheritdoc />
    public override DataTable GetSchema() => _inner.GetSchema();

    /// <inheritdoc />
    public override DataTable GetSchema(string collectionName) => _inner.GetSchema(collectionName);

    /// <inheritdoc />
    public override DataTable GetSchema(string collectionName, string?[] restrictionValues) =>
        _inner.GetSchema(collectionName, restrictionValues);

    /// <inheritdoc />
    public override void EnlistTransaction(System.Transactions.Transaction? transaction) =>
        _inner.EnlistTransaction(transaction);

    /// <inheritdoc />
    protected override DbCommand CreateDbCommand() => new QueryWattCommand(_inner.CreateCommand(), this);

    /// <inheritdoc />
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        QueryWattTransaction.For(_inner.BeginTransaction(isolationLevel), this);

    /// <inheritdoc />
    protected override async ValueTask<DbTransaction> BeginDbTransactionAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
    {
        var transaction = await _inner
            .BeginTransactionAsync(isolationLevel, cancellationToken)
            .ConfigureAwait(false);

        return QueryWattTransaction.For(transaction, this);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.StateChange -= OnInnerStateChange;

            if (_ownsInner)
            {
                _inner.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        _inner.StateChange -= OnInnerStateChange;

        if (_ownsInner)
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }

    private void OnInnerStateChange(object sender, StateChangeEventArgs args) => OnStateChange(args);
}
