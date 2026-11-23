using System.Data;

namespace i25Core.Core.Transactions
{
    /// <summary>
    /// Runs several saves as one all-or-nothing unit. Calls can be nested: the outermost call owns the real
    /// database transaction, inner calls join it.
    /// </summary>
    public interface IUnitOfWork
    {
        /// <summary>True while a transaction is open.</summary>
        bool InTransaction { get; }

        /// <summary>
        /// Runs <paramref name="work"/> in a transaction. Commits when it finishes, rolls back when it throws.
        /// Safe with EF's retrying execution strategy: the delegate may run again, so keep it free of side effects
        /// outside the database (use <see cref="OnCommitted"/> for those).
        /// </summary>
        Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default);

        /// <inheritdoc cref="ExecuteAsync(Func{CancellationToken, Task}, CancellationToken)"/>
        Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);

        /// <summary>Same as the plain overload, with an isolation level (applies only to the outermost call).</summary>
        Task ExecuteAsync(IsolationLevel isolationLevel, Func<CancellationToken, Task> work, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a transaction you finish yourself: call <see cref="IUnitOfWorkScope.CommitAsync"/>, or dispose
        /// without committing to roll back. Not usable with a retrying execution strategy; prefer ExecuteAsync.
        /// </summary>
        Task<IUnitOfWorkScope> BeginAsync(IsolationLevel? isolationLevel = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs <paramref name="callback"/> after the outermost transaction commits, and never if it rolls back.
        /// Outside a transaction it runs at once. Use it for things that cannot be undone: queueing a background
        /// job, sending an email, publishing a message.
        /// </summary>
        void OnCommitted(Func<CancellationToken, Task> callback);
    }

    /// <summary>One open (or joined) transaction. Dispose it; commit it first to keep the changes.</summary>
    public interface IUnitOfWorkScope : IAsyncDisposable
    {
        Task CommitAsync(CancellationToken cancellationToken = default);
    }
}
