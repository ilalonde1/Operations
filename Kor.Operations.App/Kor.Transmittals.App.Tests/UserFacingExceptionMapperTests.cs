#nullable enable
using System;
using System.Data.Odbc;
using System.IO;
using System.Runtime.CompilerServices;
using Kor.Operations.App.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kor.Operations.App.Tests;

public sealed class UserFacingExceptionMapperTests
{
    [Fact]
    public void SqlException_maps_to_vpn_connection_sentence()
    {
        var message = UserFacingExceptionMapper.Map(CreateUninitialized<SqlException>());

        Assert.Equal(UserFacingExceptionMapper.DataConnectionMessage, message);
        Assert.DoesNotContain("SqlException", message, StringComparison.Ordinal);
        Assert.DoesNotContain("IDeltekClientContextService", message, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadAsync", message, StringComparison.Ordinal);
    }

    [Fact]
    public void OdbcException_maps_to_vpn_connection_sentence()
    {
        var message = UserFacingExceptionMapper.Map(CreateUninitialized<OdbcException>());

        Assert.Equal(UserFacingExceptionMapper.DataConnectionMessage, message);
        Assert.DoesNotContain("OdbcException", message, StringComparison.Ordinal);
        Assert.DoesNotContain("IDeltekClientContextService", message, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadAsync", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deltek_client_not_found_message_contains_no_implementation_detail()
    {
        var message = UserFacingExceptionMapper.DeltekClientNotFoundMessage;

        Assert.DoesNotContain("IDeltekClientContextService", message, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadAsync", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Clendor", message, StringComparison.Ordinal);
        Assert.DoesNotContain("ODBC", message, StringComparison.Ordinal);
    }

    [Fact]
    public void MapAndLog_passes_original_exception_to_logger()
    {
        var logger = new CapturingLogger();
        var original = new InvalidOperationException(
            "Outer failure",
            CreateUninitialized<OdbcException>());

        _ = UserFacingExceptionMapper.MapAndLog(logger, original, "Detailed log entry {Id}", 123);

        Assert.Same(original, logger.Exception);
        Assert.Equal(LogLevel.Warning, logger.Level);
    }

    [Fact]
    public void Org_dossier_uses_mapper_instead_of_raw_exception_text()
    {
        var source = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "Kor.Operations.App",
            "Opportunities",
            "OrgDossierViewModel.cs"));

        Assert.Contains("UserFacingExceptionMapper.DeltekClientNotFoundMessage", source, StringComparison.Ordinal);
        Assert.Contains("UserFacingExceptionMapper.MapAndLog", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IDeltekClientContextService.LoadAsync returned null", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ErrorMessage: ex.GetType().Name + \": \" + ex.Message", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StatusMessage = $\"Load failed: {ex.GetType().Name}: {ex.Message}\"", source, StringComparison.Ordinal);
    }

    private static T CreateUninitialized<T>() where T : class =>
        (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.App")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }

    private sealed class CapturingLogger : ILogger
    {
        public LogLevel Level { get; private set; }
        public Exception? Exception { get; private set; }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Level = logLevel;
            Exception = exception;
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
