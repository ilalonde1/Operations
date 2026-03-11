using Kor.Operations.Core;          // TransmittalFile
using Kor.Operations.Services;  // HeaderLoader
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace Kor.Operations
{
    public partial class BookmarkNotesWindow : Window
    {
        // Row that backs each line in the grid
        public sealed class BookmarkNoteRow
        {
            public TransmittalFile File { get; set; } = null!;
            public int Index { get; set; }

            public string FileName { get; set; } = string.Empty;
            public string Bookmark { get; set; } = string.Empty;
            public string Note { get; set; } = string.Empty;

            // First row for each file (Index == 0) => header row
            public bool IsHeaderRow => Index == 0;
        }

        public ObservableCollection<BookmarkNoteRow> Items { get; } = new();

        public BookmarkNotesWindow(IEnumerable<BookmarkNoteRow> rows)
        {
            InitializeComponent();
            DataContext = this;

            foreach (var r in rows ?? Enumerable.Empty<BookmarkNoteRow>())
                Items.Add(r);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await HeaderLoader.ApplyAsync(HeaderBar);
            }
            catch
            {
                // non‑fatal; if it fails you just keep the basic header
            }
        }

        public IReadOnlyList<BookmarkNoteRow> GetResults()
            => Items.ToList();

        private void OkBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
