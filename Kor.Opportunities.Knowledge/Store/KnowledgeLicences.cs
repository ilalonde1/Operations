namespace Kor.Opportunities.Knowledge.Store;

public static class KnowledgeLicences
{
    public const string OwnedCopyPrivateStudy = "OwnedCopyPrivateStudy", PublicDomain = "PublicDomain",
        CcBy = "CcBy", CcBySa = "CcBySa", CcByNcSa = "CcByNcSa", CcByNcNd = "CcByNcNd",
        Own = "Own", CiteOnly = "CiteOnly";
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
        { OwnedCopyPrivateStudy, PublicDomain, CcBy, CcBySa, CcByNcSa, CcByNcNd, Own, CiteOnly });
}
