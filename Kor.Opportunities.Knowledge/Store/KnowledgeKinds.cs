namespace Kor.Opportunities.Knowledge.Store;

public static class KnowledgeKinds
{
    public const string Book = "Book", Document = "Document", Note = "Note";
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Book, Document, Note });
}
