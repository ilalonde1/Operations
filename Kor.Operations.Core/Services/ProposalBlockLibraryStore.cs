#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Models.Proposal;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.Core.Services
{
    public sealed class ProposalBlockLibraryStore : IProposalBlockLibraryStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private static readonly string DefaultFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KorOperations",
            "proposal-library");

        private readonly string _folder;
        private readonly ILogger<ProposalBlockLibraryStore>? _logger;

        public ProposalBlockLibraryStore() : this(DefaultFolder) { }

        public ProposalBlockLibraryStore(string folder, ILogger<ProposalBlockLibraryStore>? logger = null)
        {
            _folder = folder ?? throw new ArgumentNullException(nameof(folder));
            _logger = logger;
            Directory.CreateDirectory(_folder);
        }

        public async Task SaveAsync(ProposalBlockTemplate template, CancellationToken ct = default)
        {
            template.ModifiedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(template, JsonOptions);
            await File.WriteAllTextAsync(GetPath(template.Id), json, ct).ConfigureAwait(false);
        }

        public async Task<List<ProposalBlockTemplate>> LoadAllAsync(CancellationToken ct = default)
        {
            var result = new List<ProposalBlockTemplate>();
            foreach (var file in Directory.GetFiles(_folder, "*.json"))
            {
                try
                {
                    var text = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                    var t = JsonSerializer.Deserialize<ProposalBlockTemplate>(text, JsonOptions);
                    if (t is not null) { result.Add(t); }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to load proposal block template from {File}", file);
                }
            }

            return result.OrderBy(t => t.Category).ThenBy(t => t.Name).ToList();
        }

        public Task DeleteAsync(string id, CancellationToken ct = default)
        {
            var path = GetPath(id);
            if (File.Exists(path)) { File.Delete(path); }
            return Task.CompletedTask;
        }

        private string GetPath(string id) => Path.Combine(_folder, $"{id}.json");
    }
}
