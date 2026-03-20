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
                RefreshPhases();
                RefreshNotes();
            };
        }

        private FeeTableBlockContent? Model => DataContext as FeeTableBlockContent;

        private void RefreshPhases()
        {
            PhasesList.ItemsSource = null;
            PhasesList.ItemsSource = Model?.Phases;
            PhasesList.Items.Refresh();
        }

        private void RefreshNotes()
        {
            NotesList.ItemsSource = null;
            NotesList.ItemsSource = Model?.AdditionalNotes;
        }

        private void AddPhase_Click(object sender, RoutedEventArgs e)
        {
            Model?.Phases.Add(new FeePhaseRow { PhaseName = "New Phase" });
            RefreshPhases();
            PhasesList.SelectedIndex = Model!.Phases.Count - 1;
        }

        private void RemovePhase_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null || PhasesList.SelectedItem is not FeePhaseRow selected)
                return;

            Model.Phases.Remove(selected);
            RefreshPhases();
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
            if (PhasesList.SelectedItem is not FeePhaseRow selected)
                return;

            selected.PhaseName = PhaseNameText.Text;
            PhasesList.Items.Refresh();
        }

        private void PhaseFeeText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (PhasesList.SelectedItem is not FeePhaseRow selected)
                return;

            if (decimal.TryParse(PhaseFeeText.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                selected.Fee = value;

            PhasesList.Items.Refresh();
        }

        private void AddNote_Click(object sender, RoutedEventArgs e)
        {
            Model?.AdditionalNotes.Add("New note");
            RefreshNotes();
            NotesList.SelectedIndex = Model!.AdditionalNotes.Count - 1;
        }

        private void RemoveNote_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null || NotesList.SelectedItem is not string selected)
                return;

            Model.AdditionalNotes.Remove(selected);
            RefreshNotes();
            SelectedNoteText.Text = string.Empty;
        }

        private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedNoteText.Text = NotesList.SelectedItem as string ?? string.Empty;
        }

        private void SelectedNoteText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Model is null || NotesList.SelectedIndex < 0)
                return;

            Model.AdditionalNotes[NotesList.SelectedIndex] = SelectedNoteText.Text;
            RefreshNotes();
            NotesList.SelectedIndex = System.Math.Min(NotesList.SelectedIndex, Model.AdditionalNotes.Count - 1);
        }
    }
}
