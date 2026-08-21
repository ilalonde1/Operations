#nullable enable
using System;
using System.Runtime.CompilerServices;
using Kor.Operations.App.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Kor.Operations.App.Tests;

public sealed class UnhandledExceptionPolicyTests
{
    [Fact]
    public void SqlException_maps_to_vpn_sentence_and_can_continue()
    {
        var decision = UnhandledExceptionPolicy.Decide(CreateUninitialized<SqlException>());

        Assert.True(decision.CanContinue);
        Assert.Equal(UserFacingExceptionMapper.DataConnectionMessage, decision.UserMessage);
        Assert.DoesNotContain("SqlException", decision.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", decision.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Fatal_exception_terminates_without_raw_exception_text()
    {
        var decision = UnhandledExceptionPolicy.Decide(new OutOfMemoryException("simulated process exhaustion"));

        Assert.False(decision.CanContinue);
        Assert.Equal(UnhandledExceptionPolicy.FatalMessage, decision.UserMessage);
        Assert.DoesNotContain("OutOfMemoryException", decision.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("simulated process exhaustion", decision.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", decision.UserMessage, StringComparison.Ordinal);
    }

    private static T CreateUninitialized<T>() where T : class =>
        (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
}
