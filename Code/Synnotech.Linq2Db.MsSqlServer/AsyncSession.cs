using System;
using System.Threading.Tasks;
using Light.GuardClauses;
using LinqToDB.Data;
using Synnotech.DatabaseAbstractions;

namespace Synnotech.Linq2Db.MsSqlServer
{
    /// <summary>
    /// Represents an asynchronous session to MS SQL Server via a Linq2Db data connection.
    /// This session wraps a transaction which should be already started before the <see cref="DataConnection"/>
    /// is passed to this constructor. Calling <see cref="SaveChangesAsync" /> will commit the transaction.
    /// Disposing the session will implicitly roll-back the transaction if SaveChangesAsync was not called beforehand.
    /// Beware: you must not derive from this class and introduce other references to disposable objects.
    /// Only the <see cref="DataConnection"/> will be disposed.
    /// </summary>
    /// <typeparam name="TDataConnection">Your database context type that derives from <see cref="DataConnection"/>.</typeparam>
    public abstract class AsyncSession<TDataConnection> : IAsyncSession
        where TDataConnection : DataConnection
    {
        /// <summary>
        /// Initializes a new instance of <see cref="AsyncSession{TDataConnection}" />.
        /// </summary>
        /// <param name="dataConnection">The Linq2Db data connection used for database access.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="dataConnection"/> is null.</exception>
        protected AsyncSession(TDataConnection dataConnection)
        {
            DataConnection = dataConnection.MustNotBeNull(nameof(dataConnection));
            Check.InvalidOperation(dataConnection.Transaction == null, "A transaction must have been started before the data connection is passed to AsyncSession");
        }

        /// <summary>
        /// Gets the Linq2Db data connection.
        /// </summary>
        protected TDataConnection DataConnection { get; }

        /// <summary>
        /// Gets the value indicating whether <see cref="SaveChangesAsync" /> has been called.
        /// </summary>
        protected bool IsCommitted { get; private set; }

        /// <summary>
        /// Disposes the Linq2Db data connection. If <see cref="SaveChangesAsync"/> has not been called,
        /// then the internal transaction will be rolled back.
        /// </summary>
        public void Dispose()
        {
            if (!IsCommitted)
                DataConnection.RollbackTransaction();
            DataConnection.Dispose();
        }

        /// <summary>
        /// Disposes the Linq2Db data connection. If <see cref="SaveChangesAsync"/> has not been called,
        /// then the internal transaction will be rolled back.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (!IsCommitted)
                await DataConnection.RollbackTransactionAsync();
            await DataConnection.DisposeAsync();
        }

        /// <summary>
        /// Commits the internal transaction.
        /// </summary>
        public async Task SaveChangesAsync()
        {
            await DataConnection.CommitTransactionAsync();
            IsCommitted = true;
        }
    }

    /// <summary>
    /// Represents an asynchronous session to MS SQL Server via a Linq2Db data connection.
    /// This session wraps a transaction which should be already started before the <see cref="DataConnection"/>
    /// is passed to this constructor. Calling SaveChangesAsync will commit the transaction.
    /// Disposing the session will implicitly roll-back the transaction if SaveChangesAsync was not called beforehand.
    /// Beware: you must not derive from this class and introduce other references to disposable objects.
    /// Only the <see cref="DataConnection"/> will be disposed.
    /// </summary>
    public abstract class AsyncSession : AsyncSession<DataConnection>
    {
        /// <summary>
        /// Initializes a new instance of <see cref="AsyncSession{TDataConnection}" />.
        /// </summary>
        /// <param name="dataConnection">The Linq2Db data connection used for database access.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="dataConnection"/> is null.</exception>
        protected AsyncSession(DataConnection dataConnection) : base(dataConnection) { }
    }
}