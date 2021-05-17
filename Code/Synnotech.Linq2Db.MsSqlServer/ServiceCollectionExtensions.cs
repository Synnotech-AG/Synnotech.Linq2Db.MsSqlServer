using System;
using System.Diagnostics;
using Light.GuardClauses;
using LinqToDB.Configuration;
using LinqToDB.Data;
using LinqToDB.DataProvider;
using LinqToDB.DataProvider.SqlServer;
using LinqToDB.Mapping;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Synnotech.DatabaseAbstractions;

namespace Synnotech.Linq2Db.MsSqlServer
{
    /// <summary>
    /// Provides extension methods to setup Linq2Db in a DI Container that supports <see cref="IServiceCollection" />.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers several Linq2Db types with the DI container, especially a <see cref="DataConnection" /> (using a transient lifetime by default). The data connection
        /// is instantiated by passing a singleton instance of <see cref="LinqToDbConnectionOptions" /> which is created from <see cref="Linq2DbSettings" />.
        /// The latter is also available as a singleton and retrieved from the <see cref="IConfiguration" /> instance (which should already be registered with the DI container).
        /// Then a <see cref="IDataProvider" /> using Microsoft.Data.SqlClient internally is created and registered as a singleton as well. The <paramref name="createMappings" />
        /// delegate is applied to the mapping schema of the data provider.
        /// </summary>
        /// <param name="services">The collection that is used to register all necessary types with the DI container.</param>
        /// <param name="createMappings">
        /// The delegate that manipulates the mapping schema of the data provider (optional). Alternatively, you could use the Linq2Db attributes to configure
        /// your model classes, but we strongly recommend that you use the Linq2Db <see cref="FluentMappingBuilder" /> to specify how model classes are mapped.
        /// </param>
        /// <param name="dataConnectionLifetime">
        /// The lifetime that is used for the data connection (optional). The default value is <see cref="ServiceLifetime.Transient" />. If you want to, you
        /// can exchange it with <see cref="ServiceLifetime.Scoped" />.
        /// </param>
        /// <param name="registerFactoryDelegateForDataConnection">
        /// The value indicating whether a <see cref="Func{DataConnection}" /> should also be registered with the DI container (optional). The default value is true.
        /// You can set this value to false if you use a proper DI container like LightInject that offers function factories. https://www.lightinject.net/#function-factories
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is null.</exception>
        public static IServiceCollection AddLinq2DbForSqlServer(this IServiceCollection services,
                                                                Action<MappingSchema>? createMappings = null,
                                                                ServiceLifetime dataConnectionLifetime = ServiceLifetime.Transient,
                                                                bool registerFactoryDelegateForDataConnection = true)
        {
            services.MustNotBeNull(nameof(services));

            services.AddSingleton(container => Linq2DbSettings.FromConfiguration(container.GetRequiredService<IConfiguration>()))
                    .AddSingleton(container => CreateSqlServerDataProvider(container.GetRequiredService<Linq2DbSettings>().SqlServerVersion, createMappings))
                    .AddSingleton(container =>
                     {
                         var settings = container.GetRequiredService<Linq2DbSettings>();
                         return CreateLinq2DbConnectionOptions(container.GetRequiredService<IDataProvider>(),
                                                               settings.ConnectionString,
                                                               settings.TraceLevel,
                                                               container.GetService<ILoggerFactory>());
                     })
                    .Add(new ServiceDescriptor(typeof(DataConnection), container => new DataConnection(container.GetRequiredService<LinqToDbConnectionOptions>()), dataConnectionLifetime));
            if (registerFactoryDelegateForDataConnection)
                services.AddSingleton<Func<DataConnection>>(container => container.GetRequiredService<DataConnection>);
            return services;
        }

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
        /// are optional but need to be set together if a level other than <see cref="TraceLevel.Off" /> is used.
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
        /// Registers an <see cref="ISessionFactory{TAbstraction}" /> for the specified session. You can inject this session factory
        /// into client code to resolve your session asynchronously. When resolved, a new data connection is created, a connection to
        /// the target database is opened asynchronously, and a transaction is started. The data connection is then passed to your
        /// custom session type. See <see cref="SessionFactory{TAbstraction,TImplementation,TDataConnection}" /> for details.
        /// <code>
        /// public class MySessionClient
        /// {
        ///     public MySessionClient(ISessionFactory&lt;IMySession> sessionFactory) =>
        ///         SessionFactory = sessionFactory;
        /// 
        ///     // IMySession must derive from IAsyncSession
        ///     private ISessionFactory&lt;IMySession> SessionFactory { get; }
        /// 
        ///     public async Task SomeMethod()
        ///     {
        ///         await using var session = await SessionFactory.OpenSessionAsync();
        ///         // do something useful with your session
        ///     }
        /// }
        /// </code>
        /// </summary>
        /// <typeparam name="TAbstraction">The interface that your session implements. It must implement <see cref="IAsyncSession" />.</typeparam>
        /// <typeparam name="TImplementation">The Linq2Db session implementation that performs the actual database I/O. It must derive from <see cref="AsyncSession{TDataConnection}" />.</typeparam>
        /// <typeparam name="TDataConnection">Your custom data connection subtype that you use in your solution.</typeparam>
        /// <param name="services">The collection that holds all registrations for the DI container.</param>
        /// <param name="factoryLifetime">The lifetime for the session factory. It's usually ok for them to be a singleton.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is null.</exception>
        public static IServiceCollection AddSessionFactoryFor<TAbstraction, TImplementation, TDataConnection>(this IServiceCollection services, ServiceLifetime factoryLifetime = ServiceLifetime.Singleton)
            where TAbstraction : IAsyncSession
            where TImplementation : AsyncSession<TDataConnection>, TAbstraction, new()
            where TDataConnection : DataConnection
        {
            services.MustNotBeNull(nameof(services))
                    .Add(new ServiceDescriptor(typeof(ISessionFactory<TAbstraction>), typeof(SessionFactory<TAbstraction, TImplementation, TDataConnection>), factoryLifetime));
            return services;
        }

        /// <summary>
        /// Registers an <see cref="ISessionFactory{TAbstraction}" /> for the specified session. You can inject this session factory
        /// into client code to resolve your session asynchronously. When resolved, a new data connection is created, a connection to
        /// the target database is opened asynchronously, and a transaction is started. The data connection is then passed to your
        /// custom session type. See <see cref="SessionFactory{TAbstraction,TImplementation,TDataConnection}" /> for details.
        /// <code>
        /// public class MySessionClient
        /// {
        ///     public MySessionClient(ISessionFactory&lt;IMySession> sessionFactory) =>
        ///         SessionFactory = sessionFactory;
        /// 
        ///     // IMySession must derive from IAsyncSession
        ///     private ISessionFactory&lt;IMySession> SessionFactory { get; }
        /// 
        ///     public async Task SomeMethod()
        ///     {
        ///         await using var session = await SessionFactory.OpenSessionAsync();
        ///         // do something useful with your session
        ///     }
        /// }
        /// </code>
        /// </summary>
        /// <typeparam name="TAbstraction">The interface that your session implements. It must implement <see cref="IAsyncSession" />.</typeparam>
        /// <typeparam name="TImplementation">The Linq2Db session implementation that performs the actual database I/O. It must derive from <see cref="AsyncSession{TDataConnection}" />.</typeparam>
        /// <param name="services">The collection that holds all registrations for the DI container.</param>
        /// <param name="factoryLifetime">The lifetime for the session factory. It's usually ok for them to be a singleton.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is null.</exception>
        public static IServiceCollection AddSessionFactoryFor<TAbstraction, TImplementation>(this IServiceCollection services, ServiceLifetime factoryLifetime = ServiceLifetime.Singleton)
            where TAbstraction : IAsyncSession
            where TImplementation : AsyncSession, TAbstraction, new() =>
            services.AddSessionFactoryFor<TAbstraction, TImplementation, DataConnection>(factoryLifetime);
    }
}