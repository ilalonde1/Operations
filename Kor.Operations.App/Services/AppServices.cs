#nullable enable
using System;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations.Services;

internal static class AppServices
{
    private static IServiceProvider? _provider;

    internal static void Initialize(IServiceProvider provider)
        => _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    internal static T Get<T>() where T : notnull
        => (_provider ?? throw new InvalidOperationException("AppServices not initialized."))
            .GetRequiredService<T>();

    internal static T? GetOptional<T>() where T : class
        => _provider?.GetService<T>();
}
