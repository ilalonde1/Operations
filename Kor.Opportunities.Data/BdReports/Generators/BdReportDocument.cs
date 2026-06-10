#nullable enable
using System.Collections.Generic;

namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// Renderer-agnostic report content model (BD-UI-Plan-2026-06-08 decision 2:
/// one model, two renderers — DocxBuilder and HtmlPreviewBuilder both read
/// this, so the WebView2 preview matches the DOCX one-to-one by construction).
/// Block vocabulary mirrors the PowerShell builders' primitives
/// (H1/H2/H3/P/B/Italic/MakeTable in tools/BdReportBuilders).
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

public sealed record TableBlock(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows) : BdReportBlock;

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

    public BdReportDocumentBuilder Table(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        _blocks.Add(new TableBlock(headers, rows));
        return this;
    }

    public BdReportDocument Build() => new(_title, _blocks);
}
