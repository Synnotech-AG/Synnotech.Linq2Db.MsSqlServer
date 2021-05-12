﻿using System.Threading.Tasks;
using FluentAssertions;
using LinqToDB;
using Microsoft.Extensions.DependencyInjection;
using Synnotech.DatabaseAbstractions;
using Xunit;

namespace Synnotech.Linq2Db.MsSqlServer.Tests
{
    public sealed class AsyncSessionTests : BaseMsSqlIntegrationTest
    {
        [Fact]
        public static void MustImplementIAsyncSession() =>
            typeof(AsyncSession<>).Should().Implement<IAsyncSession>();

        [SkippableFact]
        public async Task LoadAndUpdateData()
        {
            SkipTestIfNecessary();

            var container = PrepareContainer().AddAsyncSession<IEmployeeSession, EmployeeSession>()
                                              .BuildServiceProvider();

            const string newName = "Margaret Doe";
            await using (var session = await container.GetRequiredService<Task<IEmployeeSession>>())
            {
                var noLongerJohn = await session.GetEmployeeAsync(1);
                noLongerJohn.Name = newName;
                await session.UpdateEmployeeAsync(noLongerJohn);
                await session.SaveChangesAsync();
            }

            await using (var session = await container.GetRequiredService<Task<IEmployeeSession>>())
            {
                var margaret = await session.GetEmployeeAsync(1);
                margaret.Name.Should().Be(newName);
            }
        }

        private interface IEmployeeSession : IAsyncSession
        {
            Task<Employee> GetEmployeeAsync(int id);

            Task UpdateEmployeeAsync(Employee employee);
        }

        private sealed class EmployeeSession : AsyncSession, IEmployeeSession
        {
            public Task<Employee> GetEmployeeAsync(int id) =>
                DataConnection.GetTable<Employee>().FirstAsync(e => e.Id == id);

            public Task UpdateEmployeeAsync(Employee employee) =>
                DataConnection.UpdateAsync(employee);
        }
    }
}