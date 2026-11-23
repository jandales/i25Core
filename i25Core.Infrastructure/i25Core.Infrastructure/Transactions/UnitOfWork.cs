using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using i25Core.Core.Transactions;

namespace i25Core.Infrastructure.Transactions
{
    public sealed class UnitOfWork : IUnitOfWork
    {
        private readonly I25DbContext _db;

        private int _depth;
        private bool _rollbackOnly;
        private bool _committed;
        private IDbContextTransaction? _transaction;
        private List<Func<CancellationToken, Task>>? _afterCommit;

        public UnitOfWork(I25DbContext db) => _db = db;

        public bool InTransaction => _depth > 0;

        public Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
            => RunAsync<object?>(async ct => { await work(ct); return null; }, null, cancellationToken);

        public Task ExecuteAsync(IsolationLevel isolationLevel, Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
            => RunAsync<object?>(async ct => { await work(ct); return null; }, isolationLevel, cancellationToken);

        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
            => RunAsync(work, null, cancellationToken);

        private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, IsolationLevel? level, CancellationToken cancellationToken)
        {
            // Already inside a transaction: join it (a retry, if any, belongs to the outer call).
            if (_depth > 0)
            {
                await using var inner = await BeginAsync(level, cancellationToken);
                var innerResult = await work(cancellationToken);
                await inner.CommitAsync(cancellationToken);
                return innerResult;
            }

            var strategy = _db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async ct =>
            {
                await using var scope = await BeginAsync(level, ct);
                var result = await work(ct);
                await scope.CommitAsync(ct);
                return result;
            }, cancellationToken);
        }

        public async Task<IUnitOfWorkScope> BeginAsync(IsolationLevel? isolationLevel = null, CancellationToken cancellationToken = default)
        {
            if (_depth > 0)
            {
                _depth++;
                return new Scope(this, outermost: false);
            }

            // Someone else opened a transaction on this context. We do not own it, so we do not touch it.
            if (_db.Database.CurrentTransaction != null)
                throw new InvalidOperationException("A database transaction is already open on this context and was not started by IUnitOfWork.");

            _transaction = isolationLevel.HasValue
                ? await _db.Database.BeginTransactionAsync(isolationLevel.Value, cancellationToken)
                : await _db.Database.BeginTransactionAsync(cancellationToken);

            _depth = 1;
            _rollbackOnly = false;
            _committed = false;
            _afterCommit = new List<Func<CancellationToken, Task>>();
            _db.AfterCommit = _afterCommit;      // model events are queued here instead of firing before the commit

            return new Scope(this, outermost: true);
        }

        public void OnCommitted(Func<CancellationToken, Task> callback)
        {
            if (_afterCommit == null)
                callback(CancellationToken.None).GetAwaiter().GetResult();
            else
                _afterCommit.Add(callback);
        }

        private async Task CommitAsync(bool outermost, CancellationToken cancellationToken)
        {
            if (_rollbackOnly)
                throw new InvalidOperationException("A nested unit of work ended without committing, so the whole transaction is rolled back.");

            if (!outermost)
                return;

            await _transaction!.CommitAsync(cancellationToken);
            _committed = true;
        }

        private async ValueTask EndAsync(bool outermost, bool commitCalled)
        {
            if (!outermost)
            {
                _depth--;
                if (!commitCalled)
                    _rollbackOnly = true;
                return;
            }

            var callbacks = _afterCommit;
            var committed = _committed;

            try
            {
                if (!committed)
                {
                    await _transaction!.RollbackAsync();
                    // Entities touched by the failed work no longer match the database.
                    _db.ChangeTracker.Clear();
                }
            }
            finally
            {
                await _transaction!.DisposeAsync();
                _transaction = null;
                _depth = 0;
                _committed = false;
                _afterCommit = null;
                _db.AfterCommit = null;
            }

            // The transaction is gone now, so a callback that saves behaves like any normal save.
            if (committed && callbacks != null)
                foreach (var callback in callbacks)
                    await callback(CancellationToken.None);
        }

        private sealed class Scope : IUnitOfWorkScope
        {
            private readonly UnitOfWork _owner;
            private readonly bool _outermost;
            private bool _commitCalled;
            private bool _disposed;

            public Scope(UnitOfWork owner, bool outermost) { _owner = owner; _outermost = outermost; }

            public async Task CommitAsync(CancellationToken cancellationToken = default)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(IUnitOfWorkScope));
                await _owner.CommitAsync(_outermost, cancellationToken);
                _commitCalled = true;
            }

            public async ValueTask DisposeAsync()
            {
                if (_disposed) return;
                _disposed = true;
                await _owner.EndAsync(_outermost, _commitCalled);
            }
        }
    }
}
