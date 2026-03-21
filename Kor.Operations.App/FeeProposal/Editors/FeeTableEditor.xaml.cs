#nullable enable
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.App.FeeProposal.Editors
{
    public partial class FeeTableEditor : UserControl
    {
        public FeeTableEditor()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                PhasesList.ItemsSource = Model?.Phases;
                NotesList.ItemsSource = Model?.AdditionalNotes;
            };
        }

        private FeeTableBlockContent? Model => DataContext as FeeTableBlockContent;

        private void AddPhase_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null) return;
            Model.Phases.Add(new FeePhaseRow { PhaseName = "New Phase" });
            PhasesList.Items.Refresh();
            PhasesList.SelectedIndex = Model.Phases.Count - 1;
        }

        private void RemovePhase_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null || PhasesList.SelectedItem is not FeePhaseRow selected)
                return;

            Model.Phases.Remove(selected);
            PhasesList.Items.Refresh();
        }

        private void PhasesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PhasesList.SelectedItem is FeePhaseRow selected)
            {
                PhaseNameText.Text = selected.PhaseName;
                PhaseFeeText.Text = selected.Fee.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                PhaseNameText.Text = string.Empty;
                PhaseFeeText.Text = string.Empty;
            }
        }

        private void PhaseNameText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Model is null)
                return;

            var selectedIndex = PhasesList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= Model.Phases.Count)
                return;

            Model.Phases[selectedIndex].PhaseName = PhaseNameText.Text;
            PhasesList.Items.Refresh();
        }

        private void PhaseFeeText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Model is null)
                return;

            var selectedIndex = PhasesList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= Model.Phases.Count)
                return;

            if (decimal.TryParse(PhaseFeeText.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                Model.Phases[selectedIndex].Fee = value;

            PhasesList.Items.Refresh();
        }

        private void AddNote_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null) return;
            Model.AdditionalNotes.Add("New note");
            NotesList.Items.Refresh();
            NotesList.SelectedIndex = Model.AdditionalNotes.Count - 1;
        }

        private void RemoveNote_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null || NotesList.SelectedItem is not string selected)
                return;

            Model.AdditionalNotes.Remove(selected);
            NotesList.Items.Refresh();
            SelectedNoteText.Text = string.Empty;
        }

        private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedNoteText.Text = NotesList.SelectedItem as string ?? string.Empty;
        }

        private void SelectedNoteText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Model is null)
                return;

            var selectedIndex = NotesList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= Model.AdditionalNotes.Count)
                return;

            Model.AdditionalNotes[selectedIndex] = SelectedNoteText.Text;
            NotesList.Items.Refresh();
        }
    }
}
