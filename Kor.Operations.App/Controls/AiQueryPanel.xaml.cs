#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kor.Operations.Services;

namespace Kor.Operations.Controls
{
    public partial class AiQueryPanel : UserControl
    {
        private AppAiService? _aiService;
        private IAiContextProvider? _localProvider;
        private readonly List<(string Role, string Content)> _history = new();

        public AiQueryPanel()
        {
            InitializeComponent();
        }

        internal void Initialize(AppAiService aiService, IAiContextProvider? localProvider = null)
        {
            _aiService = aiService;
            _localProvider = localProvider;
        }

        private void QuestionBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AskBtn_Click(sender, e);
        }

        private async void AskBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_aiService == null || !_aiService.IsConfigured) return;

            var question = QuestionBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(question)) return;

            AskBtn.IsEnabled = false;
            QuestionBox.Text = "";
            ResponseText.Text = "Thinking...";
            ResponseText.Visibility = Visibility.Visible;

            try
            {
                var localContext = _localProvider?.BuildLocalContext();
                _history.Add(("user", question));
                var response = await _aiService.AskAsync(_history, localContext);
                _history.Add(("assistant", response));

                while (_history.Count > 12) _history.RemoveAt(0);

                ResponseText.Text = response;
            }
            catch (Exception ex)
            {
                ResponseText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                AskBtn.IsEnabled = true;
            }
        }
    }
}
