#nullable enable
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Kor.Operations.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations.App.FeeProposal
{
    public partial class ProposalContactPickerDialog : Window
    {
        private readonly VantagepointRepository _repo;
        public VantagepointRepository.ContactRow? SelectedContact { get; private set; }

        public ProposalContactPickerDialog()
        {
            InitializeComponent();
            _repo = Kor.Operations.Services.AppServices.Get<VantagepointRepository>();
        }

        private async void SearchBtn_Click(object sender, RoutedEventArgs e)
        {
            var q = SearchBox.Text?.Trim();
            if (string.IsNullOrEmpty(q) || q.Length < 2) return;
            var rows = await _repo.SearchPeopleAsync(q, 200);
            ResultsList.ItemsSource = rows
                .Where(r => !string.IsNullOrWhiteSpace(r.Email))
                .OrderBy(r => r.Name)
                .ToList();
        }

        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                SearchBtn_Click(sender, new RoutedEventArgs());
                await System.Threading.Tasks.Task.CompletedTask;
            }
        }

        private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultsList.SelectedItem is VantagepointRepository.ContactRow row)
            {
                SelectedContact = row;
                DialogResult = true;
            }
        }

        private void OkBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsList.SelectedItem is VantagepointRepository.ContactRow row)
            {
                SelectedContact = row;
                DialogResult = true;
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
