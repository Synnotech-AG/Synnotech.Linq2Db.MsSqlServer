using System;
using FluentAssertions;
using Xunit;

namespace Synnotech.Linq2Db.MsSqlServer.Tests
{
    public static class AsyncReadOnlySessionTests
    {
        [Fact]
        public static void MustImplementIDisposable() =>
            typeof(AsyncReadOnlySession<>).Should().Implement<IDisposable>();

        [Fact]
        public static void MustImplementIAsyncDisposable() =>
            typeof(AsyncReadOnlySession<>).Should().Implement<IAsyncDisposable>();
    }
}