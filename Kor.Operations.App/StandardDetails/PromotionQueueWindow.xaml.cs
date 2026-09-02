#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace Kor.Operations.StandardDetails;

public partial class PromotionQueueWindow : Window
{
    private readonly StandardDetailsRepository _repo;
    private readonly Func<Task<string>> _processPending;

    internal PromotionQueueWindow(StandardDetailsRepository repo, Func<Task<string>> processPending)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _processPending = processPending ?? throw new ArgumentNullException(nameof(processPending));
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshQueueAsync();
    }

    private async void ProcessPending_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            QueueMessageText.Text = await _processPending();
            await RefreshQueueAsync();
        }
        catch (Exception ex)
        {
            QueueMessageText.Text = "Promotion processing failed.";
            MessageBox.Show(this, ex.Message, "Standard Details - Promotion Queue", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshQueueAsync();
    }

    private async Task RefreshQueueAsync()
    {
        IReadOnlyList<StandardDetailsOutboxRow> rows;
        try
        {
            rows = await _repo.LoadOutboxAsync();
        }
        catch (Exception ex)
        {
            QueueMessageText.Text = "Could not load promotion queue.";
            MessageBox.Show(this, ex.Message, "Standard Details - Promotion Queue", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        OutboxGrid.ItemsSource = rows;
        if (string.Equals(QueueMessageText.Text, "Ready.", StringComparison.Ordinal))
        {
            QueueMessageText.Text = $"Loaded {rows.Count} promotion request(s).";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
