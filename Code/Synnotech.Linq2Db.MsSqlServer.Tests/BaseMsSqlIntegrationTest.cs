using System.Threading.Tasks;
using Light.GuardClauses;
using Light.GuardClauses.Exceptions;
using Microsoft.Extensions.Configuration;
using Synnotech.MsSqlServer;
using Synnotech.Xunit;
using Xunit;

namespace Synnotech.Linq2Db.MsSqlServer.Tests
{
    public abstract class BaseMsSqlIntegrationTest : IAsyncLifetime
    {
        private bool AreIntegrationTestsEnabled => TestSettings.Configuration.GetValue<bool>(nameof(AreIntegrationTestsEnabled));

        private string ConnectionString
        {
            get
            {
                var connectionString = TestSettings.Configuration["connectionString"];
                if (connectionString.IsNullOrWhiteSpace())
                    throw new InvalidConfigurationException("You must set \"connectionString\" when \"areIntegrationTestsEnabled\" is set to true in testsettings.");
                return connectionString;
            }
        }

        public async Task InitializeAsync()
        {
            if (!AreIntegrationTestsEnabled)
                return;

            await Database.DropAndCreateDatabaseAsync(ConnectionString);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        protected void SkipTestIfNecessary() => Skip.IfNot(AreIntegrationTestsEnabled);
    }
}