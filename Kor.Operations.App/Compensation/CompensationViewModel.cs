#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.Compensation;

public sealed class CompensationViewModel : INotifyPropertyChanged
{
    private readonly CompensationService _service;
    private readonly ILogger<CompensationViewModel>? _log;

    private bool _isLoading;
    private EmployeeCompensationRow? _selectedRow;
    private CompensationFirmContext? _firm;

    public CompensationViewModel(CompensationService service, ILogger<CompensationViewModel>? log = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _log = log;
        Employees = new ObservableCollection<EmployeeCompensationRow>();

        EmployeesView = new CollectionViewSource { Source = Employees }.View;
        EmployeesView.Filter = o => o is EmployeeCompensationRow r && !r.IsPartner;
    }

    public ObservableCollection<EmployeeCompensationRow> Employees { get; }
    public ICollectionView EmployeesView { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotLoading));
        }
    }

    public bool IsNotLoading => !_isLoading;

    public EmployeeCompensationRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(_selectedRow, value)) return;
            _selectedRow = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => _selectedRow != null;

    public CompensationFirmContext? Firm
    {
        get => _firm;
        private set
        {
            if (ReferenceEquals(_firm, value)) return;
            _firm = value;
            OnPropertyChanged();
        }
    }

    public void ResetSelectedRow()
    {
        _selectedRow?.ResetOverrides();
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        Employees.Clear();
        SelectedRow = null;
        Firm = null;

        try
        {
            var bundle = await _service.LoadAsync(ct).ConfigureAwait(true);
            foreach (var row in bundle.Rows)
                Employees.Add(row);

            Firm = bundle.Firm;
            EmployeesView.Refresh();
            SelectedRow = bundle.Rows.Count > 0 ? bundle.Rows[0] : null;

            _log?.LogInformation("Compensation: loaded {Count} active staff; firm pool {Pool:C0}.",
                bundle.Rows.Count, bundle.Firm.FirmBonusPool);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Compensation: load failed.");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
