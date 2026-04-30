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
    public sealed class FeeProposalStore : IFeeProposalStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private static readonly string DefaultFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KorOperations",
            "fee-proposals");

        private readonly string _folder;
        private readonly ILogger<FeeProposalStore>? _logger;

        public FeeProposalStore() : this(DefaultFolder) { }

        public FeeProposalStore(string folder, ILogger<FeeProposalStore>? logger = null)
        {
            _folder = folder ?? throw new ArgumentNullException(nameof(folder));
            _logger = logger;
            Directory.CreateDirectory(_folder);
        }

        public async Task SaveAsync(FeeProposal proposal, CancellationToken ct = default)
        {
            proposal.ModifiedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(proposal, JsonOptions);
            await File.WriteAllTextAsync(GetPath(proposal.Id), json, ct).ConfigureAwait(false);
        }

        public async Task<List<FeeProposal>> LoadAllAsync(CancellationToken ct = default)
        {
            var result = new List<FeeProposal>();
            foreach (var file in Directory.GetFiles(_folder, "*.json"))
            {
                try
                {
                    var text = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                    var p = JsonSerializer.Deserialize<FeeProposal>(text, JsonOptions);
                    if (p is not null) { result.Add(p); }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to load proposal from {File}", file);
                }
            }

            return result.OrderByDescending(p => p.ModifiedAt).ToList();
        }

        public async Task<FeeProposal?> LoadByIdAsync(string id, CancellationToken ct = default)
        {
            var path = GetPath(id);
            if (!File.Exists(path)) { return null; }
            try
            {
                var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                return JsonSerializer.Deserialize<FeeProposal>(text, JsonOptions);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load proposal from {File}", path);
                return null;
            }
        }

        public async Task<IReadOnlyList<FeeProposalSummary>> LoadSummariesAsync(CancellationToken ct = default)
        {
            var all = await LoadAllAsync(ct).ConfigureAwait(false);
            return all.Select(p => new FeeProposalSummary(p.Id, p.Name, p.ModifiedAt)).ToList();
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
