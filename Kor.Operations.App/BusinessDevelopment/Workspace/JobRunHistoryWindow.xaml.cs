#nullable enable
using System;
using System.Windows;
using Kor.Operations.Services;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public partial class JobRunHistoryWindow : Window
{
    private bool _aiRegistered;

    public JobRunHistoryWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // BD-Audit-2026-06-09 M16: let the AI assistant see the open job history.
        // The VM is assigned via the DataContext initializer in AdminViewModel's
        // ShowHistory, so it is available by the time Loaded fires.
        if (!_aiRegistered && DataContext is JobRunHistoryViewModel vm)
        {
            AppServices.Get<AppAiContextBuilder>().Register(vm);
            _aiRegistered = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_aiRegistered && DataContext is JobRunHistoryViewModel vm)
        {
            AppServices.Get<AppAiContextBuilder>().Unregister(vm);
            _aiRegistered = false;
        }

        base.OnClosed(e);
    }
}
