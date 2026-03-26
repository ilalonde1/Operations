#nullable enable
using Kor.Operations.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations.Services
{
    public sealed class ProjectSearchService : IProjectSearchService
    {
        private readonly PreferencesRepository _preferencesRepository;
        private readonly string _root;
        private readonly string? _mustContainSubfolder;
        private volatile List<ProjectSearchResult> _items = new();

        public ProjectSearchService(
            PreferencesRepository preferencesRepository,
            string root,
            string? mustContainSubfolder)
        {
            _preferencesRepository = preferencesRepository ?? throw new ArgumentNullException(nameof(preferencesRepository));
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _mustContainSubfolder = mustContainSubfolder;
        }

        public async Task BuildIndexAsync(CancellationToken ct = default)
        {
            var list = await Task.Run(() =>
            {
                var results = new List<ProjectSearchResult>(capacity: 5000);

                foreach (var category in SafeEnumDirs(_root))
                {
                    ct.ThrowIfCancellationRequested();

                    var catName = Path.GetFileName(category);
                    if (catName.StartsWith("00", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (var proj in SafeEnumDirs(category))
                    {
                        ct.ThrowIfCancellationRequested();

                        if (!string.IsNullOrWhiteSpace(_mustContainSubfolder))
                        {
                            var child = Path.Combine(proj, _mustContainSubfolder);
                            if (!Directory.Exists(child))
                            {
                                continue;
                            }
                        }

                        var name = Path.GetFileName(proj);
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            continue;
                        }

                        results.Add(new ProjectSearchResult(name, proj));
                    }
                }

                results.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
                return results;
            }, ct).ConfigureAwait(false);

            _items = list;
        }

        public async Task<IReadOnlyList<ProjectSearchResult>> SearchAsync(string query, int limit, CancellationToken ct)
        {
            var trimmed = query?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
            {
                return Array.Empty<ProjectSearchResult>();
            }

            var indexTask = Task.Run<IReadOnlyList<ProjectSearchResult>>(() =>
            {
                ct.ThrowIfCancellationRequested();

                var tokens = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                return _items
                    .Where(p => tokens.All(t => p.Name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
                    .Take(limit)
                    .ToList();
            }, ct);

            List<(string ProjectNo, string? ProjectName)> recentProjects;
            try
            {
                recentProjects = await _preferencesRepository.SearchProjectsAsync(trimmed, limit).ConfigureAwait(false);
            }
            catch
            {
                recentProjects = new List<(string ProjectNo, string? ProjectName)>();
            }

            var indexed = await indexTask.ConfigureAwait(false);
            var combined = recentProjects
                .Select(p => new ProjectSearchResult(FormatProjectName(p.ProjectNo, p.ProjectName), null))
                .Concat(indexed)
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(limit)
                .ToList();

            return combined;
        }

        public ProjectSelection Resolve(ProjectSearchResult project)
        {
            if (project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }

            ParseProject(project.Name, out var projectNumber, out var projectName);

            return new ProjectSelection(
                project.Name,
                !string.IsNullOrWhiteSpace(project.Path) && Directory.Exists(project.Path) ? project.Path : null,
                projectNumber ?? project.Name,
                projectName ?? project.Name);
        }

        private static string FormatProjectName(string projectNo, string? projectName)
        {
            if (string.IsNullOrWhiteSpace(projectName))
            {
                return projectNo;
            }

            return $"{projectNo} ({projectName.Trim()})";
        }

        private static void ParseProject(string folderName, out string? projNo, out string? projName)
        {
            projNo = null;
            projName = null;

            var numEnd = folderName.IndexOfAny(new[] { ' ', '(' });
            projNo = numEnd > 0 ? folderName[..numEnd].Trim() : folderName.Trim();

            var open = folderName.IndexOf('(');
            var close = folderName.LastIndexOf(')');
            if (open >= 0 && close > open)
            {
                projName = folderName.Substring(open + 1, close - open - 1).Trim();
            }
        }

        private static IEnumerable<string> SafeEnumDirs(string path)
        {
            try
            {
                return Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}
