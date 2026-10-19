#nullable enable
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Operations.App.Opportunities;
using Kor.Operations.Services;
using Kor.Opportunities.Data.BdReports.Generators;

namespace Kor.Operations
{
    /// <summary>
    /// Opens a kor:// deep link (mpi / org / person) in the running app — the same
    /// detail surfaces the report preview and dashboard drill-downs use. Used when
    /// a link is clicked from OUTSIDE the app: the OS launches (or forwards the URL
    /// to) the app via the registered kor:// scheme. Must be called on the UI thread.
    /// </summary>
    internal static class KorDeepLink
    {
        public static async Task OpenAsync(string url)
        {
            // A move answered from the 6 a.m. email: show the Director, then record (it asks why for a skip).
            if (KorReportLinks.TryParseMove(url, out var hourId, out var rank, out var result))
            {
                var director = Application.Current?.Windows
                                   .OfType<Kor.Operations.App.BusinessDevelopment.Director.BdDirectorWindow>()
                                   .FirstOrDefault(w => w.IsVisible)
                               ?? AppServices.Get<Kor.Operations.App.BusinessDevelopment.Director.BdDirectorWindow>();
                director.Show();
                if (director.WindowState == WindowState.Minimized)
                {
                    director.WindowState = WindowState.Normal;
                }

                director.Activate();
                await director.RecordMoveFromLinkAsync(hourId, rank, result).ConfigureAwait(true);
                return;
            }

            if (!KorReportLinks.TryParse(url, out var kind, out var id))
            {
                return;
            }

            var owner = Application.Current?.MainWindow;
            Window? opened = null;

            // An opportunity has no standalone window — the registry inside the
            // BD workspace IS its listing. Reuse a visible workspace when there
            // is one, the same "one door" rule OpenPursuitInWorkspace follows.
            if (kind == KorReportLinks.OpportunityKind)
            {
                var workspace = Application.Current?.Windows
                                    .OfType<BdWorkspaceWindow>()
                                    .FirstOrDefault(w => w.IsVisible)
                                ?? AppServices.Get<BdWorkspaceWindow>();

                workspace.NavigateToOpportunity(id);
                workspace.Show();
                if (workspace.WindowState == WindowState.Minimized)
                {
                    // Activate() alone does not restore a minimized window.
                    workspace.WindowState = WindowState.Normal;
                }

                workspace.Activate();
                return;
            }

            if (kind == KorReportLinks.PursuitKind)
            {
                var workspace = Application.Current?.Windows
                                    .OfType<BdWorkspaceWindow>()
                                    .FirstOrDefault(w => w.IsVisible)
                                ?? AppServices.Get<BdWorkspaceWindow>();
                workspace.NavigateToPursuit(id);
                workspace.Show();
                if (workspace.WindowState == WindowState.Minimized)
                {
                    workspace.WindowState = WindowState.Normal;
                }

                workspace.Activate();
                return;
            }

            if (kind == KorReportLinks.DirectorKind)
            {
                var director = Application.Current?.Windows
                                   .OfType<Kor.Operations.App.BusinessDevelopment.Director.BdDirectorWindow>()
                                   .FirstOrDefault(w => w.IsVisible)
                               ?? AppServices.Get<Kor.Operations.App.BusinessDevelopment.Director.BdDirectorWindow>();
                director.ShowView(id);
                director.Show();
                if (director.WindowState == WindowState.Minimized)
                {
                    director.WindowState = WindowState.Normal;
                }

                director.Activate();
                return;
            }

            switch (kind)
            {
                case KorReportLinks.MpiKind:
                    var briefVm = AppServices.Get<PursuitBriefViewModel>();
                    await briefVm.LoadAsync(id, CancellationToken.None).ConfigureAwait(true);
                    opened = new PursuitBriefWindow(briefVm);
                    break;

                case KorReportLinks.OrgKind:
                    opened = new OrgDossierWindow(AppServices.Get<OrgDossierViewModel>(), id);
                    break;

                case KorReportLinks.PersonKind:
                    var personVm = AppServices.Get<PersonDossierViewModel>();
                    await personVm.LoadAsync(id, CancellationToken.None).ConfigureAwait(true);
                    opened = new PersonDossierWindow(personVm);
                    break;
            }

            if (opened is null)
            {
                return;
            }

            if (owner is not null && owner.IsLoaded && !ReferenceEquals(owner, opened))
            {
                opened.Owner = owner;
            }

            opened.Show();
            opened.Activate();
        }
    }
}
