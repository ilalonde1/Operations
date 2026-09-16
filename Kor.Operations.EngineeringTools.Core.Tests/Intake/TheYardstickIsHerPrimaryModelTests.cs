#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// THE YARDSTICK IS HER PRIMARY MODEL (intake step 101, 2026-09-16). The engineers' review from the corpus found
/// 31098 judged against "31098-01 - 2NDRY ELEMS.EDB": 21 of 22 storeys a metre and more off, 32 of 370 columns
/// within 100 mm - the newest model in the folder, and not the building. Among a job's models the newest whose name
/// carries a primary word (FULL, GRAVITY) is the yardstick; then the newest carrying no secondary word (2NDRY,
/// SECONDARY, CRANE, MASS, CHECK, PRELIM, COPY, SLS); then the newest of the rest.
/// WHAT THIS COVERS: the rank of the three classes by name, whole words, any case ("Full-Model", "2ndry elems",
/// "EQ" as neither); the rows extending the words through the options. WHAT IT DOES NOT: the folder walk itself
/// (OurOwnOutputIsNeverTheYardstickTests); which of two primary models is newer (the file's date).
/// </summary>
public sealed class TheYardstickIsHerPrimaryModelTests
{
    private static int Rank(string name) => CorpusAnalyzer.ModelRank(name, CorpusAnalyzer.DefaultYardstickPrimaryModelWords, CorpusAnalyzer.DefaultYardstickSecondaryModelWords);

    [Fact]
    public void APrimaryModelRanksFirstASecondaryLastAndAnEqModelBetween()
    {
        Assert.Equal(0, Rank("31130-01 Uplands East (FULL_GRAVITY).EDB.e2k"));
        Assert.Equal(0, Rank("31117-01 - W16th gravity model full.e2k"));
        Assert.Equal(2, Rank("31098-01 - 2NDRY ELEMS.e2k"));
        Assert.Equal(2, Rank("31029 - Crane Beam.e2k"));
        Assert.Equal(2, Rank("Bridgeport Hote 2- 3-03-2026 secondary elements.e2k"));
        Assert.Equal(1, Rank("30838-Onyx-EQ-2024-04-18.e2k"));
        Assert.Equal(1, Rank("31202-01 Hotel Circle 2025-12-02.e2k"));
        // a secondary word outranks a primary one in the same name: a check of the full model is a check
        Assert.Equal(2, Rank("31076-Full-Below Grade-L1 Diaphragm Check.e2k"));
    }

    [Fact]
    public void TheWordsAreReadFromTheOptions()
    {
        var d = PdfIntakeOptions.Default;
        Assert.Equal(CorpusAnalyzer.DefaultYardstickPrimaryModelWords, d.YardstickPrimaryModelWords);
        Assert.Equal(CorpusAnalyzer.DefaultYardstickSecondaryModelWords, d.YardstickSecondaryModelWords);
        Assert.Equal(2, CorpusAnalyzer.ModelRank("31000 STAIR MODEL.e2k", d.YardstickPrimaryModelWords, ["STAIR"]));
    }
}
