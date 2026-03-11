using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;

namespace Kor.Operations.Financials
{
    public partial class ProfitLossReportWindow : Window
    {
        private readonly ProfitLossReportViewModel _vm = new();

        public ProfitLossReportWindow()
        {
            InitializeComponent();
            DataContext = _vm;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshAsync(forceRefresh: false).ConfigureAwait(true);
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAsync(forceRefresh: true).ConfigureAwait(true);
        }

        private async Task RefreshAsync(bool forceRefresh)
        {
            _vm.ClearMessages();
            _vm.CanRefresh = false;
            _vm.StatusMessage = "Loading P&L...";

            try
            {
                var svc = new ProfitLossReportService();
                var rows = await svc.LoadAsync(forceRefresh, cancelToken: default).ConfigureAwait(true);

                _vm.Rows.Clear();
                foreach (var r in rows)
                    _vm.Rows.Add(r);

                _vm.StatusMessage = _vm.Rows.Count == 0 ? "No rows returned." : $"Loaded {_vm.Rows.Count:N0} rows.";
            }
            catch (Exception ex)
            {
                _vm.SetError($"Unable to load P&L.\n{ex.Message}");
                _vm.StatusMessage = "Error.";
            }
            finally
            {
                _vm.CanRefresh = true;
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
    }

    internal sealed class ProfitLossReportViewModel : INotifyPropertyChanged
    {
        private string _statusMessage = "";
        private string _errorMessage = "";
        private Visibility _errorVisibility = Visibility.Collapsed;
        private bool _canRefresh = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ProfitLossReportRow> Rows { get; } = new();

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value ?? ""; OnPropertyChanged(); }
        }

        public bool CanRefresh
        {
            get => _canRefresh;
            set { _canRefresh = value; OnPropertyChanged(); }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set { _errorMessage = value ?? ""; OnPropertyChanged(); }
        }

        public Visibility ErrorVisibility
        {
            get => _errorVisibility;
            private set { _errorVisibility = value; OnPropertyChanged(); }
        }

        public void ClearMessages()
        {
            ErrorMessage = "";
            ErrorVisibility = Visibility.Collapsed;
        }

        public void SetError(string message)
        {
            ErrorMessage = message ?? "";
            ErrorVisibility = string.IsNullOrWhiteSpace(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    internal enum ProfitLossValueKind
    {
        Currency,
        Percent,
        Number,
        Text
    }

    internal sealed class ProfitLossReportRow
    {
        public string Section { get; init; } = "";
        public string LineItem { get; init; } = "";

        public ProfitLossValueKind ValueKind { get; init; } = ProfitLossValueKind.Currency;

        // Use Amount for numeric kinds; use Text for non-numeric.
        public decimal? Amount { get; init; }
        public string? Text { get; init; }

        public string ValueDisplay
        {
            get
            {
                if (ValueKind == ProfitLossValueKind.Text)
                    return Text ?? "";

                if (Amount is not decimal a)
                    return "";

                return ValueKind switch
                {
                    ProfitLossValueKind.Currency => a.ToString("C0", CultureInfo.CurrentCulture),
                    ProfitLossValueKind.Percent => a.ToString("P1", CultureInfo.CurrentCulture),
                    ProfitLossValueKind.Number => a.ToString("N2", CultureInfo.CurrentCulture),
                    _ => a.ToString(CultureInfo.CurrentCulture)
                };
            }
        }
    }
}
