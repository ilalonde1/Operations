#nullable enable
#pragma warning disable SA1649
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kor.Operations.App.Options;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.App.Email;

internal sealed class ProjectEntry
{
    public string FullPath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

internal sealed class ProjectFolderCatalogService
{
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<ProjectFolderCatalogService> _logger;

    public ProjectFolderCatalogService(StorageOptions storageOptions, ILogger<ProjectFolderCatalogService> logger)
    {
        _storageOptions = storageOptions ?? throw new ArgumentNullException(nameof(storageOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    internal string ProjectsRoot => GetRequiredProjectsRoot();

    public List<ProjectEntry> LoadProjects()
    {
        var allProjects = new List<ProjectEntry>();
        var projectsRoot = GetRequiredProjectsRoot();

        if (!Directory.Exists(projectsRoot))
            throw new DirectoryNotFoundException(projectsRoot);

        var categories = Directory.GetDirectories(projectsRoot);

        foreach (var category in categories)
        {
            string[] subfolders;
            try
            {
                subfolders = Directory.GetDirectories(category);
            }
            catch
            {
                continue;
            }

            foreach (var folder in subfolders)
            {
                var name = Path.GetFileName(folder);
                if (string.IsNullOrEmpty(name) || name.Length < 8)
                    continue;

                var prefix = name.Substring(0, Math.Min(5, name.Length));
                if (prefix.IndexOf('x', StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                var codePart = name.Substring(0, 8);
                if (!codePart.Contains("-"))
                    continue;

                var entry = new ProjectEntry
                {
                    FullPath = folder,
                    DisplayName = name,
                    Code = codePart
                };

                allProjects.Add(entry);
            }
        }

        allProjects.Sort((a, b) =>
            string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

        return allProjects;
    }

    public List<ProjectEntry> ApplyProjectFilter(IEnumerable<ProjectEntry> allProjects, string term)
    {
        IEnumerable<ProjectEntry> source = allProjects;

        if (!string.IsNullOrWhiteSpace(term))
        {
            string t = term.Trim();
            string lower = t.ToLowerInvariant();

            source = source.Where(p =>
                (!string.IsNullOrEmpty(p.DisplayName) &&
                    p.DisplayName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrEmpty(p.Code) &&
                    p.Code.StartsWith(t, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(p.DisplayName) &&
                    p.DisplayName.ToLowerInvariant().Contains(lower)));
        }

        return source.ToList();
    }

    private string GetRequiredProjectsRoot() => !string.IsNullOrWhiteSpace(_storageOptions.ProjectsRoot) ? _storageOptions.ProjectsRoot.Trim() : throw new InvalidOperationException("App.config appSetting 'ProjectsRoot' is missing or empty.");
}
