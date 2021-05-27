using System;
using System.Threading;
using System.Threading.Tasks;
using Light.GuardClauses;
using LinqToDB.Data;
using Synnotech.DatabaseAbstractions;

namespace Synnotech.Linq2Db.MsSqlServer
{
    /// <summary>
    /// Represents an adapter for the Linq2Db <see cref="DataConnectionTransaction" /> that
    /// implements <see cref="IAsyncTransaction" />. The transaction will be
    /// implicitly rolled back when commit was not called and the transaction is disposed. 
    /// </summary>
    public sealed class Linq2DbTransaction : IAsyncTransaction
    {
        /// <summary>
        /// Initializes a new instance of <see cref="Linq2DbTransaction" />.
        /// </summary>
        /// <param name="dataConnectionTransaction">The data connection transaction that will be wrapped by this instance.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="dataConnectionTransaction"/> is null.</exception>
        public Linq2DbTransaction(DataConnectionTransaction dataConnectionTransaction) =>
            DataConnectionTransaction = dataConnectionTransaction.MustNotBeNull(nameof(dataConnectionTransaction));

        private DataConnectionTransaction DataConnectionTransaction { get; }

        /// <summary>
        /// Gets the value indicating whether <see cref="CommitAsync"/> has already been called.
        /// </summary>
        public bool IsCommitted { get; private set; }

        /// <summary>
        /// Disposes the underlying transaction. It will also be rolled back if
        /// <see cref="CommitAsync"/> was not called up to this point.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (!IsCommitted)
                await DataConnectionTransaction.RollbackAsync();
            await DataConnectionTransaction.DisposeAsync();
        }

        /// <summary>
        /// Disposes the underlying transaction. It will also be rolled back if
        /// <see cref="CommitAsync"/> was not called up to this point.
        /// </summary>
        public void Dispose()
        {
            if (!IsCommitted)
                DataConnectionTransaction.Rollback();
            DataConnectionTransaction.Dispose();
        }

        /// <summary>
        /// Commits all changes to the database.
        /// </summary>
        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await DataConnectionTransaction.CommitAsync(cancellationToken);
            IsCommitted = true;
        }
    }
}