#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.BusinessDevelopment.Director;

/// <summary>Paste an owner's reply to a roll-up, see what each numbered line will do, and save only what was understood.</summary>
public partial class ReplyDialog : Window
{
    private readonly IReadOnlyList<RollupItem> _items;
    private readonly IReadOnlyDictionary<long, string> _titles;
    private readonly DateOnly _today;

    public ReplyDialog(string owner, IReadOnlyList<RollupItem> items, IReadOnlyDictionary<long, string> titles, DateOnly today)
    {
        InitializeComponent();
        _items = items;
        _titles = titles;
        _today = today;
        Title = "Record reply from " + owner;
        PromptText.Text = $"{owner}'s answer to the roll-up's {items.Count} numbered items. Each line is logged on its pursuit; "
            + "\"moved\" sets the next step's date, \"done\" clears a due next step, \"dropped\" archives the pursuit with the reason.";
        Refresh();
    }

    /// <summary>The actions Ian confirmed with Save; empty until then.</summary>
    public IReadOnlyList<RollupReplyAction> Actions { get; private set; } = Array.Empty<RollupReplyAction>();

    private (IReadOnlyList<RollupReplyAction> Actions, IReadOnlyList<string> Problems) Planned() =>
        RollupReply.Plan(RollupReply.Parse(ReplyBox.Text, _today), _items, id => _titles.TryGetValue(id, out var t) ? t : "Pursuit #" + id, _today);

    private void Refresh()
    {
        var (actions, problems) = Planned();
        PreviewText.Text = actions.Count == 0 ? "Nothing yet: paste the reply above." : string.Join(Environment.NewLine, actions.Select(a => a.Preview));
        ProblemText.Text = string.IsNullOrWhiteSpace(ReplyBox.Text) ? "" : string.Join(Environment.NewLine, problems);
        SaveButton.IsEnabled = actions.Count > 0;
    }

    private void ReplyBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Actions = Planned().Actions;
        DialogResult = Actions.Count > 0;
    }
}
