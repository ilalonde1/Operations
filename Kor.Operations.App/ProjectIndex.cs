#nullable enable
#pragma warning disable SA1649
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations
{
    internal sealed class ProjectItem
    {
        public string Name { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
    }

    internal sealed class ProjectIndex
    {
        private readonly string _root;
        private readonly string? _mustContainSubfolder;
        private volatile List<ProjectItem> _items = new();

        public ProjectIndex(string root, string? mustContainSubfolder)
        {
            _root = root;
            _mustContainSubfolder = mustContainSubfolder;
        }

        public async Task BuildIndexAsync()
        {
            var list = await Task.Run(() =>
            {
                var results = new List<ProjectItem>(capacity: 5000);

                foreach (var category in SafeEnumDirs(_root))
                {
                    var catName = Path.GetFileName(category);
                    if (catName.StartsWith("00", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (var proj in SafeEnumDirs(category))
                    {
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

                        results.Add(new ProjectItem { Name = name, Path = proj });
                    }
                }

                results.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
                return results;
            });

            _items = list;
        }

        public Task<IReadOnlyList<ProjectItem>> SearchAsync(string query, int limit, CancellationToken ct)
        {
            return Task.Run<IReadOnlyList<ProjectItem>>(() =>
            {
                ct.ThrowIfCancellationRequested();

                var q = query.Trim();
                if (q.Length == 0 || _items.Count == 0)
                {
                    return Array.Empty<ProjectItem>();
                }

                var tokens = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                return _items
                    .Where(p => tokens.All(t => p.Name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
                    .Take(limit)
                    .ToList();
            }, ct);
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
