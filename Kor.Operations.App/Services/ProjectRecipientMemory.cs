#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Kor.Operations.Services
{
    /// <summary>
    /// Persists per-user preferences: recent projects + default recipients per project.
    /// </summary>
    public sealed class ProjectRecipientMemory
    {
        private readonly string _userUpn;
        private readonly string _storePath;
        private Prefs _prefs;

        public ProjectRecipientMemory(string userUpn)
        {
            _userUpn = userUpn.Trim().ToLowerInvariant();
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "Kor.Transmittals");
            Directory.CreateDirectory(dir);
            _storePath = Path.Combine(dir, "prefs.json");
            _prefs = LoadAll();
            if (!_prefs.Users.TryGetValue(_userUpn, out _))
                _prefs.Users[_userUpn] = new UserPrefs();
        }

        // ---------- Public API ----------

        public IReadOnlyList<string> GetRecentProjects(int max = 6)
        {
            var u = _prefs.Users[_userUpn];
            return u.RecentProjects.Take(max).ToList();
        }

        public IReadOnlyCollection<string> GetDefaultRecipients(string projectKey)
        {
            projectKey = NormalizeProjectKey(projectKey);
            var u = _prefs.Users[_userUpn];
            if (u.Projects.TryGetValue(projectKey, out var p))
                return p.DefaultRecipients;
            return Array.Empty<string>();
        }

        public void TouchProject(string projectKey)
        {
            projectKey = NormalizeProjectKey(projectKey);
            var u = _prefs.Users[_userUpn];

            // Move to front of MRU list
            u.RecentProjects.RemoveAll(p => string.Equals(p, projectKey, StringComparison.OrdinalIgnoreCase));
            u.RecentProjects.Insert(0, projectKey);
            if (u.RecentProjects.Count > 20) u.RecentProjects.RemoveRange(20, u.RecentProjects.Count - 20);

            if (!u.Projects.ContainsKey(projectKey))
                u.Projects[projectKey] = new ProjectPrefs();

            u.Projects[projectKey].LastUsedUtc = DateTime.UtcNow;
            SaveAll();
        }

        /// <summary>
        /// Learn recipients actually used for this project (e.g., To + CC).
        /// </summary>
        public void LearnRecipients(string projectKey, IEnumerable<string> recipients, int capPerProject = 25)
        {
            projectKey = NormalizeProjectKey(projectKey);
            var u = _prefs.Users[_userUpn];
            if (!u.Projects.TryGetValue(projectKey, out var p))
                u.Projects[projectKey] = p = new ProjectPrefs();

            foreach (var addr in recipients.Where(IsLikelyEmail))
                p.DefaultRecipients.Add(addr.Trim());

            // keep the set bounded
            if (p.DefaultRecipients.Count > capPerProject)
            {
                // crude trim by last used (we’re not scoring yet)
                p.DefaultRecipients = p.DefaultRecipients.Take(capPerProject).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            p.LastUsedUtc = DateTime.UtcNow;
            SaveAll();
        }

        // ---------- Helpers ----------

        private static bool IsLikelyEmail(string s) => !string.IsNullOrWhiteSpace(s) && s.Contains('@');

        public static string NormalizeProjectKey(string projectNo, string? projectName = null)
        {
            var n = (projectNo ?? "").Trim();
            var pn = (projectName ?? "").Trim();
            return string.IsNullOrEmpty(pn) ? n : $"{n} - {pn}";
        }

        private string NormalizeProjectKey(string key) => (key ?? "").Trim();

        private Prefs LoadAll()
        {
            try
            {
                if (File.Exists(_storePath))
                {
                    var json = File.ReadAllText(_storePath);
                    var loaded = JsonSerializer.Deserialize<Prefs>(json, JsonOptions);
                    if (loaded != null) return loaded;
                }
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to load recipient memory from {Path}.", _storePath); }
            return new Prefs();
        }

        private void SaveAll()
        {
            try
            {
                var json = JsonSerializer.Serialize(_prefs, JsonOptions);
                File.WriteAllText(_storePath, json);
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to save recipient memory to {Path}.", _storePath); }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // ---------- Models ----------

        private sealed class Prefs
        {
            public Dictionary<string, UserPrefs> Users { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class UserPrefs
        {
            public List<string> RecentProjects { get; set; } = new();
            public Dictionary<string, ProjectPrefs> Projects { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class ProjectPrefs
        {
            public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;
            public HashSet<string> DefaultRecipients { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
