using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace QueryWatt.SqlServer.Wrapping;

/// <summary>
/// A pass-through <see cref="DbTransaction"/> so that a transaction begun on a
/// <see cref="QueryWattConnection"/> reports that connection as its own, the way callers expect.
/// </summary>
public sealed class QueryWattTransaction : DbTransaction
{
    private static readonly ConditionalWeakTable<DbTransaction, QueryWattTransaction> Wrappers = new();

    private readonly DbTransaction _inner;
    private readonly QueryWattConnection? _connection;

    private QueryWattTransaction(DbTransaction inner, QueryWattConnection? connection)
    {
        _inner = inner;
        _connection = connection;
    }

    /// <summary>The transaction this instance delegates to.</summary>
    public DbTransaction Inner => _inner;

    /// <inheritdoc />
    public override IsolationLevel IsolationLevel => _inner.IsolationLevel;

    /// <inheritdoc />
    protected override DbConnection? DbConnection => _connection ?? _inner.Connection;

    /// <inheritdoc />
    public override void Commit() => _inner.Commit();

    /// <inheritdoc />
    public override Task CommitAsync(CancellationToken cancellationToken = default) =>
        _inner.CommitAsync(cancellationToken);

    /// <inheritdoc />
    public override void Rollback() => _inner.Rollback();

    /// <inheritdoc />
    public override Task RollbackAsync(CancellationToken cancellationToken = default) =>
        _inner.RollbackAsync(cancellationToken);

    /// <inheritdoc />
    public override void Save(string savepointName) => _inner.Save(savepointName);

    /// <inheritdoc />
    public override Task SaveAsync(string savepointName, CancellationToken cancellationToken = default) =>
        _inner.SaveAsync(savepointName, cancellationToken);

    /// <inheritdoc />
    public override void Release(string savepointName) => _inner.Release(savepointName);

    /// <inheritdoc />
    public override Task ReleaseAsync(string savepointName, CancellationToken cancellationToken = default) =>
        _inner.ReleaseAsync(savepointName, cancellationToken);

    /// <inheritdoc />
    public override void Rollback(string savepointName) => _inner.Rollback(savepointName);

    /// <inheritdoc />
    public override Task RollbackAsync(string savepointName, CancellationToken cancellationToken = default) =>
        _inner.RollbackAsync(savepointName, cancellationToken);

    /// <inheritdoc />
    public override bool SupportsSavepoints => _inner.SupportsSavepoints;

    internal static QueryWattTransaction For(DbTransaction inner, QueryWattConnection? connection) =>
        Wrappers.GetValue(inner, key => new QueryWattTransaction(key, connection));

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override ValueTask DisposeAsync() => _inner.DisposeAsync();
}
