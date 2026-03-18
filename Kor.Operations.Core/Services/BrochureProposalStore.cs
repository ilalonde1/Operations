#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.Core.Services
{
    public sealed class BrochureProposalStore : IBrochureProposalStore
    {
        private static readonly string ProposalsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KorOperations", "proposals");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public BrochureProposalStore()
        {
            Directory.CreateDirectory(ProposalsFolder);
        }

        public void Save(BrochureProposal proposal)
        {
            proposal.ModifiedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(proposal, JsonOptions);
            File.WriteAllText(GetPath(proposal.Id), json);
        }

        public List<BrochureProposal> LoadAll()
        {
            var result = new List<BrochureProposal>();

            foreach (var file in Directory.GetFiles(ProposalsFolder, "*.json"))
            {
                try
                {
                    var proposal = JsonSerializer.Deserialize<BrochureProposal>(
                        File.ReadAllText(file), JsonOptions);
                    if (proposal is not null)
                        result.Add(proposal);
                }
                catch
                {
                    // skip corrupt files
                }
            }

            return result.OrderByDescending(static p => p.ModifiedAt).ToList();
        }

        public void Delete(string id)
        {
            var path = GetPath(id);
            if (File.Exists(path))
                File.Delete(path);
        }

        private static string GetPath(string id) =>
            Path.Combine(ProposalsFolder, $"{id}.json");
    }
}
