#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.Core.Services
{
    public sealed class FeeProposalStore
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

        public FeeProposalStore() : this(DefaultFolder) { }

        public FeeProposalStore(string folder)
        {
            _folder = folder ?? throw new ArgumentNullException(nameof(folder));
            Directory.CreateDirectory(_folder);
        }

        public void Save(FeeProposal proposal)
        {
            proposal.ModifiedAt = DateTime.UtcNow;
            File.WriteAllText(GetPath(proposal.Id), JsonSerializer.Serialize(proposal, JsonOptions));
        }

        public List<FeeProposal> LoadAll()
        {
            var result = new List<FeeProposal>();
            foreach (var file in Directory.GetFiles(_folder, "*.json"))
            {
                try
                {
                    var p = JsonSerializer.Deserialize<FeeProposal>(File.ReadAllText(file), JsonOptions);
                    if (p is not null) result.Add(p);
                }
                catch { }
            }

            return result.OrderByDescending(p => p.ModifiedAt).ToList();
        }

        public void Delete(string id)
        {
            var path = GetPath(id);
            if (File.Exists(path)) File.Delete(path);
        }

        private string GetPath(string id) => Path.Combine(_folder, $"{id}.json");
    }
}
