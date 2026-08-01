#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kor.Operations.Services;

namespace Kor.Operations.Controls
{
    public partial class AiQueryPanel : UserControl
    {
        private AppAiService? _aiService;
        private IAiContextProvider? _localProvider;
        private readonly List<(string Role, string Content)> _history = new();

        // Tool-use mode (optional). When all three are non-null, the panel uses
        // AskWithToolsAsync instead of AskAsync — enabling Claude to actuate
        // changes in the host window through declared tools.
        private IReadOnlyList<AiTool>? _tools;
        private AiToolDispatcher? _toolDispatcher;
        private string? _systemPromptOverride;

        // Last raw assistant response — kept so Copy hands the user the
        // unrendered Markdown they actually saw, not a flattened TextBlock.Text.
        private string _lastResponseText = "";

        public AiQueryPanel()
        {
            InitializeComponent();
        }

        internal void Initialize(AppAiService aiService, IAiContextProvider? localProvider = null)
        {
            _aiService = aiService;
            _localProvider = localProvider;
        }

        /// <summary>
        /// Tool-use-capable initialization. When this overload is used, the panel
        /// calls <see cref="AppAiService.AskWithToolsAsync"/> on every Ask, letting
        /// Claude invoke the provided tools via the dispatcher. The
        /// <paramref name="systemPrompt"/> fully replaces the default firm-wide
        /// system prompt (context should already be embedded).
        /// </summary>
        internal void InitializeWithTools(
            AppAiService aiService,
            IAiContextProvider localProvider,
            IReadOnlyList<AiTool> tools,
            AiToolDispatcher dispatcher,
            string systemPrompt)
        {
            _aiService = aiService;
            _localProvider = localProvider;
            _tools = tools;
            _toolDispatcher = dispatcher;
            _systemPromptOverride = systemPrompt;
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

            // Spinner + transient "Thinking..." placeholder. Spinner is the
            // real activity signal during 8-17s /ask calls — the placeholder
            // text just covers the empty stack until the first response token.
            BusyBar.Visibility = Visibility.Visible;
            ResponseStack.Children.Clear();
            ResponseStack.Children.Add(new TextBlock
            {
                Text = "Thinking…",
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("Text.Secondary"),
                FontSize = 11.5,
            });
            ResponseContainer.Visibility = Visibility.Visible;

            try
            {
                var localContext = _localProvider?.BuildLocalContext();
                _history.Add(("user", question));

                string response;
                if (_tools is not null && _toolDispatcher is not null && _systemPromptOverride is not null)
                {
                    // Build a fresh system prompt each turn so Claude always sees
                    // the current state of the host (geometry, overrides, etc.).
                    var prompt = BuildSystemPromptForThisTurn();
                    var result = await _aiService.AskWithToolsAsync(
                        _history, _tools, _toolDispatcher, prompt);
                    response = result.ToolCallsExecuted > 0
                        ? (string.IsNullOrWhiteSpace(result.Text)
                            ? $"Done — {result.ToolCallsExecuted} change(s) applied."
                            : $"{result.Text}\n\n({result.ToolCallsExecuted} change(s) applied.)")
                        : result.Text;
                }
                else
                {
                    response = await _aiService.AskAsync(_history, localContext);
                }

                _history.Add(("assistant", response));

                while (_history.Count > 12) _history.RemoveAt(0);

                _lastResponseText = response ?? "";
                RenderResponse(_lastResponseText);
            }
            catch (Exception ex)
            {
                _lastResponseText = $"Error: {ex.Message}";
                RenderResponse(_lastResponseText);
            }
            finally
            {
                AskBtn.IsEnabled = true;
                BusyBar.Visibility = Visibility.Collapsed;
            }
        }

        private void RenderResponse(string markdown)
        {
            MarkdownPresenter.Render(
                markdown,
                ResponseStack,
                textBrush: (Brush)FindResource("Text.Primary"),
                codeBackground: (Brush)FindResource("Surface.Subtle"),
                codeBorder: (Brush)FindResource("Panel.Border"));
        }

        private string BuildSystemPromptForThisTurn()
        {
            // Caller supplied a base system prompt; append the latest provider
            // snapshot so Claude sees current state on every turn.
            var basePrompt = _systemPromptOverride ?? string.Empty;
            var context = _localProvider?.BuildContext();
            if (string.IsNullOrWhiteSpace(context)) return basePrompt;
            return basePrompt + "\n\nCURRENT STATE:\n" + context;
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            ResponseStack.Children.Clear();
            _lastResponseText = "";
            ResponseContainer.Visibility = Visibility.Collapsed;
            _history.Clear();
            QuestionBox.Text = "";
        }

        private void CopyBtn_Click(object sender, RoutedEventArgs e)
        {
            // Copy the original Markdown — what the user actually sees rendered
            // — rather than reconstructing text from the rendered TextBlocks.
            var text = _lastResponseText;
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                Clipboard.SetText(text);
                var original = CopyBtn.Content;
                CopyBtn.Content = "Copied";
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                timer.Tick += (_, __) => { CopyBtn.Content = original; timer.Stop(); };
                timer.Start();
            }
            catch { /* clipboard occasionally locked by other apps; ignore */ }
        }
    }
}
