#nullable enable
using System.Collections.Generic;

namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// Renderer-agnostic report content model (BD-UI-Plan-2026-06-08 decision 2:
/// one model, two renderers — DocxBuilder and HtmlPreviewBuilder both read
/// this, so the WebView2 preview matches the DOCX one-to-one by construction).
/// Block vocabulary mirrors the PowerShell builders' primitives
/// (H1/H2/H3/P/B/Italic/MakeTable in tools/BdReportBuilders) plus the
/// 2026-06-12 UX-pass summary primitives (KPI strip, verdict chips).
/// </summary>
public sealed record BdReportDocument(string Title, IReadOnlyList<BdReportBlock> Blocks);

public abstract record BdReportBlock;

/// <summary>Heading level 1-3, mapped to native Word Heading styles (nav-pane friendly).</summary>
public sealed record HeadingBlock(int Level, string Text) : BdReportBlock;

public sealed record ParagraphBlock(string Text) : BdReportBlock;

/// <summary>Bold label + regular value on one line (the PS builders' B primitive).</summary>
public sealed record LabelValueBlock(string Label, string Value) : BdReportBlock;

/// <summary>Small italic note, 9pt (the PS builders' Italic primitive).</summary>
public sealed record ItalicNoteBlock(string Text) : BdReportBlock;

/// <summary>Per-column horizontal alignment for TableBlock.</summary>
public enum ColumnAlignment
{
    Left,
    Right,
    Center,
}

public sealed record TableBlock(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<ColumnAlignment>? Alignments = null) : BdReportBlock;

/// <summary>
/// Semantic tone for chips and KPI accents. The color mapping lives in
/// KorReportStyles so both renderers stay on the dashboard's verdict palette.
/// </summary>
public enum ChipTone
{
    Urgent,
    Positive,
    Caution,
    Info,
    Neutral,
}

public sealed record ChipItem(string Text, ChipTone Tone);

/// <summary>A row of status chips (verdict pills) — partner-skimmable state at a glance.</summary>
public sealed record ChipRowBlock(IReadOnlyList<ChipItem> Chips) : BdReportBlock;

/// <summary>One KPI callout: big value + small uppercase label, optional tone accent.</summary>
public sealed record KpiItem(string Value, string Label, ChipTone? Tone = null);

/// <summary>
/// The summary strip: a single row of KPI callout cards at the top of a
/// report so a partner gets the story from the first screen.
/// </summary>
public sealed record KpiStripBlock(IReadOnlyList<KpiItem> Items) : BdReportBlock;

/// <summary>Maps honing verdicts onto chip tones (dashboard pill palette parity).</summary>
public static class ChipTones
{
    public static ChipTone ForVerdict(string? verdict) => verdict switch
    {
        BdVerdicts.PursueUrgent => ChipTone.Urgent,
        BdVerdicts.Pursue => ChipTone.Positive,
        BdVerdicts.Monitor => ChipTone.Caution,
        BdVerdicts.Discover => ChipTone.Info,
        _ => ChipTone.Neutral,
    };
}

/// <summary>Fluent composer matching the PS builders' authoring style.</summary>
public sealed class BdReportDocumentBuilder
{
    private readonly string _title;
    private readonly List<BdReportBlock> _blocks = new();

    public BdReportDocumentBuilder(string title)
    {
        _title = title;
        _blocks.Add(new HeadingBlock(1, title));
    }

    public BdReportDocumentBuilder H2(string text)
    {
        _blocks.Add(new HeadingBlock(2, text));
        return this;
    }

    public BdReportDocumentBuilder H3(string text)
    {
        _blocks.Add(new HeadingBlock(3, text));
        return this;
    }

    public BdReportDocumentBuilder P(string text)
    {
        _blocks.Add(new ParagraphBlock(text));
        return this;
    }

    public BdReportDocumentBuilder B(string label, string value)
    {
        _blocks.Add(new LabelValueBlock(label, value));
        return this;
    }

    public BdReportDocumentBuilder Italic(string text)
    {
        _blocks.Add(new ItalicNoteBlock(text));
        return this;
    }

    public BdReportDocumentBuilder Table(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyList<ColumnAlignment>? alignments = null)
    {
        _blocks.Add(new TableBlock(headers, rows, alignments));
        return this;
    }

    public BdReportDocumentBuilder Kpis(params KpiItem[] items)
    {
        _blocks.Add(new KpiStripBlock(items));
        return this;
    }

    public BdReportDocumentBuilder Chips(params ChipItem[] chips)
    {
        _blocks.Add(new ChipRowBlock(chips));
        return this;
    }

    public BdReportDocument Build() => new(_title, _blocks);
}
