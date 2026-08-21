#nullable enable
using System.Configuration;
using System.Windows;
using Kor.Operations.Services;

namespace Kor.Operations.App;

public static class DemoClientPresentMode
{
    public static bool ClientPresent { get; } =
        bool.TryParse(ConfigurationManager.AppSettings[AppConfigKeys.DemoClientPresent], out var clientPresent)
        && clientPresent;

    public static Visibility InternalOnlyVisibility =>
        ClientPresent ? Visibility.Collapsed : Visibility.Visible;
}
