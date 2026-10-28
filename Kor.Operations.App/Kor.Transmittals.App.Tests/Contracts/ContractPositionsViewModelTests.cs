#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Contracts;
using Kor.Operations.Core.Models.Contracts;
using Kor.Operations.Core.Services;
using Moq;
using Xunit;

namespace Kor.Operations.App.Tests.Contracts;

/// <summary>The Positions view model without a window: the query its filters build, the status line, the path a
/// row opens, and the row's short forms. Covers the mapping; does not cover the SQL (SqlContractClauseStore is read
/// against the live view when the window is driven) nor the grid's rendering (ContractsScreensRenderTests).</summary>
public sealed class ContractPositionsViewModelTests
{
    [Fact]
    public void Default_filter_asks_for_limitation_of_liability_any_client_all_documents()
    {
        var vm = new ContractPositionsViewModel(Mock.Of<IContractClauseStore>());
        var q = vm.BuildQuery();
        Assert.Equal("limitation_of_liability", q.Topic);
        Assert.Null(q.ClientName);
        Assert.Null(q.Text);
        Assert.False(q.SignedOnly);
    }

    [Fact]
    public void Any_topic_and_any_client_become_null_and_text_is_trimmed()
    {
        var vm = new ContractPositionsViewModel(Mock.Of<IContractClauseStore>())
        {
            SelectedTopic = ContractPositionsViewModel.AnyTopic,
            SelectedClient = ContractPositionsViewModel.AnyClient,
            SearchText = "  subrogation  ",
            SignedOnly = true,
        };
        var q = vm.BuildQuery();
        Assert.Null(q.Topic);
        Assert.Null(q.ClientName);
        Assert.Equal("subrogation", q.Text);
        Assert.True(q.SignedOnly);
    }

    [Fact]
    public async Task Search_fills_rows_selects_the_first_and_describes_the_counts()
    {
        var store = new Mock<IContractClauseStore>();
        store.Setup(s => s.SearchAsync(It.IsAny<ContractPositionQuery>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(ContractsScreensRenderTests.Fixture());
        var vm = new ContractPositionsViewModel(store.Object);

        await vm.SearchAsync();

        Assert.Equal(5, vm.Rows.Count);
        Assert.Same(vm.Rows[0], vm.SelectedRow);
        Assert.True(vm.HasSelection);
        Assert.False(vm.HasNoSelection);
        // 5 clauses, 4 documents (two from the Wesgroup PSA), 3 named clients (the Fraser Health pursuit has none), 2 signed.
        Assert.Equal("5 clauses from 4 documents across 3 clients, 2 in signed agreements.", vm.Status);
    }

    [Fact]
    public async Task A_failing_store_leaves_the_rows_alone_and_says_so()
    {
        var store = new Mock<IContractClauseStore>();
        store.Setup(s => s.SearchAsync(It.IsAny<ContractPositionQuery>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("the view is missing"));
        var vm = new ContractPositionsViewModel(store.Object);

        await vm.SearchAsync();

        Assert.Empty(vm.Rows);
        Assert.StartsWith("Search failed:", vm.Status);
        Assert.Contains("the view is missing", vm.Status);
        Assert.True(vm.CanSearch);
    }

    [Theory]
    [InlineData("Projects/Projects/03 Residential/31180-01 (7376 Halifax St Burnaby)/01 General/Proposals & Contracts/x.pdf",
                @"\\Kor-fs01\Projects\Projects\03 Residential\31180-01 (7376 Halifax St Burnaby)\01 General\Proposals & Contracts\x.pdf")]
    [InlineData("Management/Project Opportunities/Pending/4. Fraser Health Authority/7-AIBC-Contract-8C 2021 070921.pdf",
                @"\\Kor-fs01\Management\Project Opportunities\Pending\4. Fraser Health Authority\7-AIBC-Contract-8C 2021 070921.pdf")]
    [InlineData("Archives/old.pdf", @"\\Kor-fs01\E$\Archives\old.pdf")]
    public void Library_paths_open_from_the_share_the_folder_is_published_on(string stored, string expected)
    {
        Assert.Equal(expected, ContractPositionsViewModel.ToUncPath(stored));
    }

    [Fact]
    public void Row_short_forms_are_what_the_grid_shows()
    {
        var p = ContractsScreensRenderTests.Fixture()[4]; // RLA, an email attachment, 44 words
        var row = new ContractPositionRow(p);
        Assert.Equal("RLA Architects Inc.", row.Client);
        Assert.Equal("2026", row.Year);
        Assert.Equal("Latest on file", row.Status);
        Assert.Equal("Limitation of liability", row.TopicLabels);
        Assert.Equal("FW_ RAIC Doc 9.msg › EPI-RAIC-Doc9-agreement with KOR.pdf", row.SourceLabel);
        Assert.True(row.Preview.Length <= 160, $"preview is {row.Preview.Length} chars");
        Assert.StartsWith("5. Limitation of Liability.", row.Preview);

        var pursuit = new ContractPositionRow(ContractsScreensRenderTests.Fixture()[3]);
        Assert.Equal("Limitation of liability, Limitation period", pursuit.TopicLabels);
    }

    [Fact]
    public async Task Copies_of_the_same_clause_are_folded_and_counted()
    {
        var fixture = ContractsScreensRenderTests.Fixture();
        // A scan of the same PSA, read with the section number lost and the clause cut short, listed FIRST by the store.
        var scan = fixture[0] with { FilePath = "Projects/Projects/.../a scan of the same PSA.pdf", ClauseId = 99, Section = "6", Words = 49,
                                      ClauseText = string.Join(" ", fixture[0].ClauseText.Split(' ').Take(49)) + "\nJM\nDocuSign Envelope ID: 08E5EBF3" };
        var store = new Mock<IContractClauseStore>();
        store.Setup(s => s.SearchAsync(It.IsAny<ContractPositionQuery>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<ContractPosition> { scan, fixture[0], fixture[1] });
        var vm = new ContractPositionsViewModel(store.Object);

        await vm.SearchAsync();

        Assert.Equal(2, vm.Rows.Count);                      // the scan is the same 9.1 and is folded into the full copy
        Assert.Equal(107, vm.Rows[0].Words);                 // the fullest copy wins, in the scan's (first) position
        Assert.Equal("9.1", vm.Rows[0].Section);
        Assert.EndsWith("1 further copy of the same clauses folded.", vm.Status);
        Assert.DoesNotContain("DocuSign", ContractPositionRow.Readable(scan.ClauseText));
    }

    [Fact]
    public void A_pursuit_without_a_job_is_labelled_by_its_folder()
    {
        var row = new ContractPositionRow(ContractsScreensRenderTests.Fixture()[3]);
        Assert.Equal("4. Fraser Health Authority (pursuit)", row.Client);
    }

    [Fact]
    public void Readable_joins_mid_sentence_breaks_and_keeps_paragraphs()
    {
        var raw = "9.1 Limitation of Liability. Notwithstanding any other\nprovision, the cap applies.\n\n9.2 Consequential Damages. Neither\nparty is liable.";
        Assert.Equal("9.1 Limitation of Liability. Notwithstanding any other provision, the cap applies.\n\n9.2 Consequential Damages. Neither party is liable.",
            ContractPositionRow.Readable(raw));
    }

    [Fact]
    public void Describe_distinguishes_an_empty_library_from_an_empty_filter()
    {
        var none = new List<ContractPosition>();
        Assert.Equal("Nothing on file.", ContractPositionsViewModel.Describe(none, new ContractPositionQuery(null, null, null, false)));
        Assert.Equal("Nothing on file for that filter.", ContractPositionsViewModel.Describe(none, new ContractPositionQuery("indemnity", null, null, false)));
    }
}
