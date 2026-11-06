#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.BusinessDevelopment.Director;
using Kor.Operations.App.Crm;
using Kor.Opportunities.Data.Crm;
using Moq;
using Xunit;

namespace Kor.Transmittals.App.Tests.BusinessDevelopment;

/// <summary>
/// The pursuit card's documents and the warning on stale questions (Ian, 6 Nov 2026).
/// Covers: the card's wording from the row's counts, the Today banner across a lead's filter, and what Save all writes.
/// Not covered: BoardSql's counts against the database, or the buttons being clicked (BdScreensRenderTests draws them).
/// Same-class fault it would miss: BoardSql counting the wrong files, since the row here is built by hand.
/// </summary>
public sealed class PursuitCardDocumentsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 21, 40, 0, TimeSpan.Zero);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kor-doc-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static TenderBoardRow Row(int docs = 6, int since = 0, bool running = false, bool apc = false, string owner = "ishabana@korstructural.com") =>
        new(1163, "Peace Regional Pool Structural Engineering Services", "Town of Peace River", 1, owner, "Islam Shabana", Now.AddDays(7), null, null, "decide",
            "Can KOR staff the visits?", new[] { new RfpQuestion("Q1", "Why", null), new RfpQuestion("Q2", "Why", null) }, null, Array.Empty<RfpAction>(),
            "tbell@peaceriver.ca", running, 41438, Now, docs, since, apc);

    private static DirectorCard Card(TenderBoardRow row) => new(row, "ishabana@korstructural.com", Now);

    [Fact]
    public void TheQuestionsButtonSaysItEmails()
    {
        Assert.Equal("Email 2 questions", Card(Row()).PrimaryAction);
    }

    [Fact]
    public void TheDocumentsButtonCountsAndHidesWhenThereAreNone()
    {
        Assert.Equal("Documents (6)", Card(Row()).DocumentsLabel);
        Assert.True(Card(Row()).HasDocuments);
        Assert.False(Card(Row(docs: 0)).HasDocuments);
    }

    [Fact]
    public void DocumentsAfterTheReviewAreSaidOnTheCard()
    {
        Assert.False(Card(Row()).HasNewDocuments);
        Assert.Equal("", Card(Row()).NewDocumentsNote);
        Assert.StartsWith("2 documents arrived after Claude's review", Card(Row(since: 2)).NewDocumentsNote, StringComparison.Ordinal);
        Assert.Contains("do not cover them.", Card(Row(since: 2)).NewDocumentsNote, StringComparison.Ordinal);
        Assert.Contains("do not cover it.", Card(Row(since: 1)).NewDocumentsNote, StringComparison.Ordinal);
        Assert.Contains("Claude is reading them now.", Card(Row(since: 2, running: true)).NewDocumentsNote, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunningReviewOffersNoSecondRead()
    {
        Assert.False(Card(Row(since: 2, running: true)).CanReRead);
        Assert.False(Card(Row(since: 0)).CanReRead);
    }

    [Fact]
    public void TheBannerCountsEveryApcTenderEvenOnALeadsOwnView()
    {
        var home = new BdDirectorHomeViewModel();
        var board = new[] { Row(apc: true), Row(apc: true, owner: "rbeirne@korstructural.com") };
        home.Load(board, Array.Empty<BdAsk>(), Array.Empty<BdDirectorPersonRow>(), Array.Empty<DirectorPerson>(), "ishabana@korstructural.com", "Islam Shabana", Now, ownOnly: true);
        Assert.True(home.HasApcSignInProblem);
        Assert.Contains("on 2 open tenders", home.ApcSignInProblem, StringComparison.Ordinal);

        home.Load(new[] { Row() }, Array.Empty<BdAsk>(), Array.Empty<BdDirectorPersonRow>(), Array.Empty<DirectorPerson>(), "ishabana@korstructural.com", "Islam Shabana", Now);
        Assert.False(home.HasApcSignInProblem);
    }

    private static (Mock<IPursuitFileStore> Store, PursuitFile File) Stored(long id, string name, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var file = new PursuitFile(id, 1163, 41438, name, null, SHA256.HashData(bytes), bytes.Length, "application/pdf", Now, "apc", HasContent: true);
        var store = new Mock<IPursuitFileStore>();
        store.Setup(s => s.OpenContentAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream(bytes));
        return (store, file);
    }

    [Fact]
    public async Task SaveAllWritesTheFileAndSavingTwiceWritesNothing()
    {
        var (store, file) = Stored(1, "RFP.pdf", "the rfp");
        var first = await PursuitDocumentSaver.SaveAllAsync(store.Object, new[] { file }, _folder, null, CancellationToken.None);
        Assert.Equal(1, first.Saved);
        Assert.Equal("the rfp", await File.ReadAllTextAsync(Path.Combine(_folder, "RFP.pdf")));

        var second = await PursuitDocumentSaver.SaveAllAsync(store.Object, new[] { file }, _folder, null, CancellationToken.None);
        Assert.Equal((0, 1), (second.Saved, second.AlreadyThere));
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task SaveAllNeverOverwritesADifferentFileOfTheSameName()
    {
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(Path.Combine(_folder, "RFP.pdf"), "somebody's own edit");
        var (store, file) = Stored(1, "RFP.pdf", "the rfp");

        var result = await PursuitDocumentSaver.SaveAllAsync(store.Object, new[] { file }, _folder, null, CancellationToken.None);

        Assert.Equal(1, result.Saved);
        Assert.Equal("somebody's own edit", await File.ReadAllTextAsync(Path.Combine(_folder, "RFP.pdf")));
        Assert.Equal("the rfp", await File.ReadAllTextAsync(Path.Combine(_folder, "RFP (2).pdf")));
    }

    [Fact]
    public async Task AFileWithNoStoredContentIsNamedNotSkippedSilently()
    {
        var legacy = new PursuitFile(2, 1163, 41438, "Old.pdf", @"\\share\Old.pdf", null, 10, null, Now, "someone");
        var result = await PursuitDocumentSaver.SaveAllAsync(Mock.Of<IPursuitFileStore>(), new List<PursuitFile> { legacy }, _folder, null, CancellationToken.None);
        Assert.Equal("Old.pdf", result.NotSaved.Single());
        Assert.Equal(0, result.Saved);
    }
}
