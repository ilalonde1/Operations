namespace Kor.Opportunities.Knowledge.Readers;

public sealed class ReaderPicker(string? calibrePath = null, IEpubConverter? converter = null)
{
    public static IReadOnlyList<string> Extensions { get; } = Array.AsReadOnly(
        new[] { ".epub", ".pdf", ".html", ".htm", ".txt", ".md", ".mobi", ".azw3" });

    public IReader Pick(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".epub" => new EpubReader(),
        ".pdf" => new PdfTextReader(),
        ".html" or ".htm" => new HtmlReader(),
        ".txt" or ".md" => new TextReader(),
        ".mobi" or ".azw3" => new CalibreConverter(calibrePath, converter),
        var extension => throw new NotSupportedException($"No reader for extension '{extension}'."),
    };
}
