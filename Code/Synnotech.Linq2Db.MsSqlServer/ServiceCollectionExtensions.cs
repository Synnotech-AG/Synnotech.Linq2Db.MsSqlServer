using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Light.GuardClauses;
using LinqToDB.Configuration;
using LinqToDB.Data;
using LinqToDB.DataProvider;
using LinqToDB.DataProvider.SqlServer;
using LinqToDB.Mapping;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Synnotech.Linq2Db.MsSqlServer
{
    /// <summary>
    /// Provides extension methods to setup Linq2Db in a DI Container that supports <see cref="IServiceCollection" />.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers several Linq2Db types with the DI container, especially a <see cref="DataConnection" /> with a transient lifetime. The data connection
        /// is instantiated with a singleton <see cref="LinqToDbConnectionOptions" /> which are created from <see cref="Linq2DbSettings" />. The latter are retrieved from
        /// the <see cref="IConfiguration" /> instance (which should already be registered with the DI container) and registered as a singleton.
        /// Then a <see cref="IDataProvider" /> using Microsoft.Data.SqlClient is created and registered as a singleton as well. The <paramref name="createMappings" />
        /// delegate is applied to the mapping schema of the data provider.
        /// </summary>
        /// <param name="services">The collection that is used to register all necessary types with the DI container.</param>
        /// <param name="createMappings">
        /// The delegate that manipulates the mapping schema of the data provider (optional). Alternatively, you could use the Linq2Db attributes to configure
        /// your model classes, but we strongly recommend that you use the Linq2Db <see cref="FluentMappingBuilder" /> to specify how model classes are mapped.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is null.</exception>
        public static IServiceCollection AddLinq2DbForSqlServer(this IServiceCollection services, Action<MappingSchema>? createMappings = null) =>
            services.MustNotBeNull(nameof(services))
                    .AddSingleton(container => Linq2DbSettings.FromConfiguration(container.GetRequiredService<IConfiguration>()))
                    .AddSingleton(container => CreateSqlServerDataProvider(container.GetRequiredService<Linq2DbSettings>().SqlServerVersion, createMappings))
                    .AddSingleton(container =>
                     {
                         var settings = container.GetRequiredService<Linq2DbSettings>();
                         return CreateLinq2DbConnectionOptions(container.GetRequiredService<IDataProvider>(),
                                                               settings.ConnectionString,
                                                               settings.TraceLevel,
                                                               container.GetService<ILoggerFactory>());
                     })
                    .AddTransient(container => new DataConnection(container.GetRequiredService<LinqToDbConnectionOptions>()));

        /// <summary>
        /// Creates an <see cref="IDataProvider" /> that uses Microsoft.Data.SqlClient internally.
        /// </summary>
        /// <param name="sqlServerVersion">The SQL Server version of the target database (optional). Defaults to <see cref="SqlServerVersion.v2017" />.</param>
        /// <param name="createMappings">
        /// The delegate that manipulates the mapping schema of the data provider (optional). Alternatively, you could use the Linq2Db attributes to configure
        /// your model classes, but we strongly recommend that you use the Linq2Db <see cref="FluentMappingBuilder" /> to specify how model classes are mapped.
        /// </param>
        public static IDataProvider CreateSqlServerDataProvider(SqlServerVersion sqlServerVersion = SqlServerVersion.v2017, Action<MappingSchema>? createMappings = null)
        {
            var dataProvider = SqlServerTools.GetDataProvider(sqlServerVersion, SqlServerProvider.MicrosoftDataSqlClient);
            createMappings?.Invoke(dataProvider.MappingSchema);
            return dataProvider;
        }

        /// <summary>
        /// Creates the default <see cref="LinqToDbConnectionOptions" />. <paramref name="traceLevel" /> and <paramref name="loggerFactory" />
        /// are optional but need to be set together if another value than <see cref="TraceLevel.Off" /> is used.
        /// </summary>
        /// <param name="dataProvider">The Linq2Db data provider used to create database-specific queries.</param>
        /// <param name="connectionString">The connection string for the target database.</param>
        /// <param name="traceLevel">The level that is used to log data connection messages (optional). Defaults to <see cref="TraceLevel.Off" />.</param>
        /// <param name="loggerFactory">The logger factory that creates a logger for <see cref="DataConnection" /> when <paramref name="traceLevel" /> is set to a value other than <see cref="TraceLevel.Off" />.</param>
        /// <exception cref="NullReferenceException">Thrown when <paramref name="dataProvider" /> or <paramref name="connectionString" /> are null.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="traceLevel" /> is set to a value other than <see cref="TraceLevel.Off" /> and <paramref name="loggerFactory" /> is null -
        /// or when <paramref name="connectionString" /> is an empty string or contains only white space.
        /// </exception>
        public static LinqToDbConnectionOptions CreateLinq2DbConnectionOptions(IDataProvider dataProvider,
                                                                               string connectionString,
                                                                               TraceLevel traceLevel = TraceLevel.Off,
                                                                               ILoggerFactory? loggerFactory = null)
        {
            dataProvider.MustNotBeNull(nameof(dataProvider));
            connectionString.MustNotBeNullOrWhiteSpace(nameof(connectionString));

            var optionsBuilder = new LinqToDbConnectionOptionsBuilder().UseConnectionString(dataProvider, connectionString)
                                                                       .WithTraceLevel(traceLevel);

            if (traceLevel == TraceLevel.Off)
                return optionsBuilder.Build();

            if (loggerFactory == null)
                throw new ArgumentException($"You must provide a loggerFactory when traceLevel is set to \"{traceLevel}\".", nameof(loggerFactory));

            var logger = loggerFactory.CreateLogger<DataConnection>();
            return optionsBuilder.WithTraceLevel(traceLevel)
                                 .WriteTraceWith(logger.LogLinq2DbMessage)
                                 .Build();
        }

        /// <summary>
        /// Uses an <see cref="ILogger" /> instance to log a Linq2Db data connection trace message.
        /// The different trace levels are mapped to the different log levels.
        /// </summary>
        public static void LogLinq2DbMessage(this ILogger logger, string? message, string? category, TraceLevel traceLevel)
        {
            logger.MustNotBeNull(nameof(logger));
            if (message.IsNullOrWhiteSpace())
                return;

            switch (traceLevel)
            {
                case TraceLevel.Off:
                    break;
                case TraceLevel.Error:
                    logger.LogError(message);
                    break;
                case TraceLevel.Warning:
                    logger.LogWarning(message);
                    break;
                case TraceLevel.Info:
                    logger.LogInformation(message);
                    break;
                case TraceLevel.Verbose:
                    logger.LogDebug(message);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(traceLevel), traceLevel, $"The trace level \"{traceLevel}\" is unknown.");
            }
        }

        /// <summary>
        /// Registers the specified session as a <see cref="Task{TAbstraction}" /> with a transient lifetime. When this dependency is resolved,
        /// the session and a data connection will be instantiated, a transaction will be started asynchronously, and finally the data connection
        /// is injected into the session. In your client code, you should request a Func&lt;Task&lt;TAbstraction>> so that you can instantiate, use, and
        /// afterwards dispose a session:
        /// <code>
        /// public class MySessionClient
        /// {
        ///     public MySessionClient(Func&lt;Task&lt;IMySession>> createSessionAsync) =>
        ///         CreateSessionAsync = createSessionAsync;
        ///
        ///     private Func&lt;Task&lt;IMySession>> CreateSessionAsync { get; }
        ///
        ///     public async Task SomeMethod()
        ///     {
        ///         await using var session = await CreateSessionAsync();
        ///         // do something useful with your session
        ///     }
        /// }
        /// </code>
        /// </summary>
        /// <typeparam name="TAbstraction">The interface that your session implements.</typeparam>
        /// <typeparam name="TImplementation">The Linq2Db session implementation that performs the actual database I/O.</typeparam>
        /// <typeparam name="TDataConnection">Your custom data connection subtype that you use in your solution.</typeparam>
        /// <param name="services">The collection that holds all registrations for the DI container.</param>
        /// <param name="registerFactoryDelegate">
        /// The value indicating whether a factory delegate should be registered as a singleton. The default value is true. You should
        /// set this to false if your DI container can support function factories out of the box, like e.g. LightInject (https://www.lightinject.net/#function-factories).
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
        public static IServiceCollection AddAsyncSession<TAbstraction, TImplementation, TDataConnection>(this IServiceCollection services, bool registerFactoryDelegate = true)
            where TImplementation : AsyncSession, TAbstraction, new()
            where TDataConnection : DataConnection
        {
            services.MustNotBeNull(nameof(services));
            services.AddTransient<Task<TAbstraction>>(async container =>
            {
                var dataConnection = container.GetRequiredService<TDataConnection>();
                var session = new TImplementation();
                await dataConnection.BeginTransactionAsync(session.TransactionLevel);
                session.SetDataConnection(dataConnection);
                return session;
            });
            if (registerFactoryDelegate)
                services.AddSingleton<Func<Task<TAbstraction>>>(container => container.GetRequiredService<Task<TAbstraction>>);

            return services;
        }

        /// <summary>
        /// Registers the specified session as a <see cref="Task{TAbstraction}" /> with a transient lifetime. When this dependency is resolved,
        /// the session and a data connection will be instantiated, a transaction will be started asynchronously, and finally the data connection
        /// is injected into the session. In your client code, you should request a Func&lt;Task&lt;TAbstraction>> so that you can instantiate, use, and
        /// afterwards dispose a session:
        /// <code>
        /// public class MySessionClient
        /// {
        ///     public MySessionClient(Func&lt;Task&lt;IMySession>> createSessionAsync) =>
        ///         CreateSessionAsync = createSessionAsync;
        ///
        ///     private Func&lt;Task&lt;IMySession>> CreateSessionAsync { get; }
        ///
        ///     public async Task SomeMethod()
        ///     {
        ///         await using var session = await CreateSessionAsync();
        ///         // do something useful with your session
        ///     }
        /// }
        /// </code>
        /// </summary>
        /// <typeparam name="TAbstraction">The interface that your session implements.</typeparam>
        /// <typeparam name="TImplementation">The Linq2Db session implementation that performs the actual database I/O.</typeparam>
        /// <param name="services">The collection that holds all registrations for the DI container.</param>
        /// <param name="registerFactoryDelegate">
        /// The value indicating whether a factory delegate should be registered as a singleton. The default value is true. You should
        /// set this to false if your DI container can support function factories out of the box, like e.g. LightInject (https://www.lightinject.net/#function-factories).
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
        public static IServiceCollection AddAsyncSession<TAbstraction, TImplementation>(this IServiceCollection services, bool registerFactoryDelegate = true)
            where TImplementation : AsyncSession, TAbstraction, new() =>
            services.AddAsyncSession<TAbstraction, TImplementation, DataConnection>(registerFactoryDelegate);
    }
}