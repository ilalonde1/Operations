#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kor.Operations.App.Contracts;
using Kor.Operations.Core.Models.Contracts;
using Kor.Operations.Core.Services;
using Moq;
using Xunit;

namespace Kor.Operations.App.Tests.Contracts;

/// <summary>
/// Renders the Contracts window (Home → Contracts, the Positions screen) to PNGs a person opens and
/// LOOKS at (%TEMP%\kor-contracts-screens): a result set with a clause selected, the same window on
/// "signed only", and the empty state. The pattern is NetworkOpsWindowsRenderTests'.
///
/// WHAT IT COVERS: the window constructs on its REAL view model fed fixture positions through a mocked
/// store; every StaticResource it names resolves; the grid's star column and the detail pane lay out
/// non-blank at the window's size; the status line carries the counts.
/// WHAT IT DOES NOT COVER: whether it looks GOOD (open the PNGs); anything behind Loaded (never shown:
/// the client list and first search are not run here, the fixture is pushed in through SearchAsync);
/// the real library's text lengths; clicking "Open the source folder". A same-class fault it would
/// NOT catch: a wrong ORDER BY in the store, because the fixture is hand-ordered.
/// </summary>
[Trait("Speed", "Slow")]
public sealed class ContractsScreensRenderTests
{
    [Fact]
    public void Contracts_window_builds_and_renders()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kor-contracts-screens");
        Directory.CreateDirectory(dir);
        Exception? failure = null;
        var written = 0;

        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null) _ = new Application();
                Application.Current!.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Kor.Operations.App;component/Themes/KorTheme.xaml", UriKind.Absolute),
                });

                var store = new Mock<IContractClauseStore>();
                store.Setup(s => s.ListClientsAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(Fixture().Select(p => p.ClientName!).Distinct().OrderBy(c => c).ToList());
                store.Setup(s => s.SearchAsync(It.IsAny<ContractPositionQuery>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync((ContractPositionQuery q, CancellationToken _) =>
                         Fixture().Where(p => !q.SignedOnly || p.IsSigned).ToList());

                var vm = new ContractPositionsViewModel(store.Object);
                vm.LoadClientsAsync().GetAwaiter().GetResult();
                vm.SearchAsync().GetAwaiter().GetResult();
                Assert.True(vm.Rows.Count == Fixture().Count, $"EXPECTED {Fixture().Count} rows; ACTUAL {vm.Rows.Count}");
                Assert.NotNull(vm.SelectedRow);
                written += Render(new ContractsWindow(vm), Path.Combine(dir, "positions.png"));

                vm.SignedOnly = true;
                vm.SearchAsync().GetAwaiter().GetResult();
                Assert.True(vm.Rows.All(r => r.Status == "Signed"), "Signed-only search returned an unsigned row.");
                written += Render(new ContractsWindow(vm), Path.Combine(dir, "positions-signed-only.png"));

                var empty = new Mock<IContractClauseStore>();
                empty.Setup(s => s.SearchAsync(It.IsAny<ContractPositionQuery>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<ContractPosition>());
                var emptyVm = new ContractPositionsViewModel(empty.Object);
                emptyVm.SearchAsync().GetAwaiter().GetResult();
                Assert.Equal("Nothing on file for that filter.", emptyVm.Status);
                written += Render(new ContractsWindow(emptyVm), Path.Combine(dir, "positions-empty.png"));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromMinutes(2)), "Rendering the Contracts window did not finish within two minutes.");
        Assert.True(failure is null, $"The Contracts window failed to build or render: {failure}");
        Assert.True(written == 3, $"EXPECTED 3 renders; ACTUAL {written}");
    }

    /// <summary>Real positions from the 2026-10-28 library, shortened, in the store's order (signed first, then client, job).</summary>
    internal static List<ContractPosition> Fixture() =>
    [
        new("30950-01", "30950-01", "Wesgroup Properties Ltd.", "West 59th & Cambie", "client", "executed", true, new DateTime(2020, 12, 4),
            "Projects/Projects/03 Residential/30950-01 (W 59th & Cambie)/01 General/Proposals & Contracts/30950-01 Wesgroup 2020-11-18 - Kor Structural - PSA Fully Executed.pdf", "",
            1, "9.1", "Limitation of Liability", "limitation_of_liability", 107,
            "9.1 Limitation of Liability. Notwithstanding any other provision in this Agreement, but subject to Sections 9.2 and 9.3, the Consultant's maximum aggregate liability will be limited to an amount equal to any insurance proceeds received, recoverable or claimable under any insurance policy obtained and maintained or required to be obtained and maintained under this Agreement, up to the minimum required amount of the applicable policy under this Agreement."),
        new("30950-01", "30950-01", "Wesgroup Properties Ltd.", "West 59th & Cambie", "client", "executed", true, new DateTime(2020, 12, 4),
            "Projects/Projects/03 Residential/30950-01 (W 59th & Cambie)/01 General/Proposals & Contracts/30950-01 Wesgroup 2020-11-18 - Kor Structural - PSA Fully Executed.pdf", "",
            2, "9.3", "Exceptions to Limitation of Liability", "limitation_of_liability", 210,
            "9.3 Exceptions to Limitation of Liability. Notwithstanding any other provision in this Agreement, the limits on the Consultant's liability under Section 9.1 will not apply to or limit the Consultant's responsibility and liability for, and the Consultant will be fully liable for: (a) Claims relating to or arising out of the gross negligence, recklessness or willful, fraudulent, criminal or intentional misconduct on the part of the Consultant; (b) third party Claims relating to personal injury, including death, property damage, or infringement of intellectual property rights; (c) breach of confidentiality; and (d) breach of any laws."),
        new("31180-01", "31180-01", "Starlight Developments", "7360 Halifax Street", "client", "draft", true, new DateTime(2026, 5, 8),
            "Projects/Projects/03 Residential/31180-01 (7376 Halifax St Burnaby)/01 General/Proposals & Contracts/7360 Halifax Street - KOR Structural Service Agreement - May 8 2026 KOR.pdf", "",
            3, "8.4", "Amount of Damages", "limitation_of_liability", 112,
            "8.4 Amount of Damages. To the maximum extent permitted under applicable law, the cumulative liability of either party arising out of or in any way connected to this Agreement shall be limited as follows: (a) with respect to Claims for which insurance is to be provided, the insurance limits required pursuant to Section 8.1 hereof; and (b) with respect to Claims for which insurance is not required and/or does not apply pursuant to Section 8.1 hereof, the lesser of two times the Price and $500,000.00."),
        new("OPP:4. Fraser Health Authority", null, null, null, "client", "draft", true, new DateTime(2025, 2, 3),
            "Management/Project Opportunities/Pending/4. Fraser Health Authority/7-AIBC-Contract-8C 2021 070921.pdf", "",
            4, "5", "5. Limitation of Liability", "limitation_of_liability|limitation_period", 94,
            "5. Limitation of Liability: claims which the Client has or hereafter may have against the Consultant in any way arising out of or related to the Consultant's duties and responsibilities pursuant to this Contract, shall be limited to the lesser amount of the Consultant's fee or $250,000. No claim may be brought against the Consultant in contract or tort more than 2 years after the services were completed."),
        new("31226-01", "31226-01", "RLA Architects Inc.", "EPI", "client", "draft", true, new DateTime(2026, 6, 17),
            "Projects/Projects/03 Residential/31226-01/01 General/Proposals & Contracts/FW_ RAIC Doc 9.msg", "EPI-RAIC-Doc9-agreement with KOR.pdf",
            5, "5", "5. Limitation of Liability", "limitation_of_liability", 44,
            "5. Limitation of Liability. To the fullest extent permitted by law, the Consultant's total aggregate liability arising out of the services shall be limited to the greater of the Consultant's professional liability insurance limits, or the Consultant's total fee under this Agreement."),
    ];

    private static int Render(Window window, string path)
    {
        var root = window.Content as FrameworkElement
                   ?? throw new InvalidOperationException($"{window.GetType().Name} has no FrameworkElement content to render.");
        var size = new Size(window.Width, window.Height);
        for (var pass = 0; pass < 2; pass++)
        {
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();
            DrainDispatcher();
        }
        var target = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        target.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using (var stream = File.Create(path)) encoder.Save(stream);
        Assert.True(new FileInfo(path).Length > 10_000, $"{path} is suspiciously small: the window may have rendered blank.");
        return 1;
    }

    private static void DrainDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
