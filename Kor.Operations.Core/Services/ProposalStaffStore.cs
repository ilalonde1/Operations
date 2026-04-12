#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Models.Proposal;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.Core.Services
{
    public sealed class ProposalStaffStore : IProposalStaffStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
        };

        private static readonly string DefaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KorOperations",
            "proposal-staff.json");

        private readonly string _path;
        private readonly ILogger<ProposalStaffStore>? _logger;

        public ProposalStaffStore() : this(DefaultPath) { }

        public ProposalStaffStore(string path, ILogger<ProposalStaffStore>? logger = null)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _logger = logger;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        }

        public async Task<List<ProposalStaffMember>> LoadAllAsync(CancellationToken ct = default)
        {
            if (!File.Exists(_path)) { return new List<ProposalStaffMember>(); }
            try
            {
                var text = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
                return JsonSerializer.Deserialize<List<ProposalStaffMember>>(text, JsonOptions)
                    ?? new List<ProposalStaffMember>();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load proposal staff from {File}", _path);
                return new List<ProposalStaffMember>();
            }
        }

        public async Task SaveAllAsync(List<ProposalStaffMember> staff, CancellationToken ct = default)
        {
            var json = JsonSerializer.Serialize(staff, JsonOptions);
            await File.WriteAllTextAsync(_path, json, ct).ConfigureAwait(false);
        }
    }
}
