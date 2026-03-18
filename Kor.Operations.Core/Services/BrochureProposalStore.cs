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
    /// <summary>
    /// Stores brochure proposals as JSON files in the user's application data folder.
    /// </summary>
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

        /// <summary>
        /// Initializes a new instance of the <see cref="BrochureProposalStore"/> class.
        /// </summary>
        public BrochureProposalStore()
        {
            Directory.CreateDirectory(ProposalsFolder);
        }

        /// <summary>
        /// Saves the supplied brochure proposal to disk.
        /// </summary>
        /// <param name="proposal">The proposal to persist.</param>
        public void Save(BrochureProposal proposal)
        {
            proposal.ModifiedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(proposal, JsonOptions);
            File.WriteAllText(GetPath(proposal.Id), json);
        }

        /// <summary>
        /// Loads all brochure proposals from disk.
        /// </summary>
        /// <returns>The stored brochure proposals ordered by modification time.</returns>
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

        /// <summary>
        /// Deletes the brochure proposal with the specified identifier.
        /// </summary>
        /// <param name="id">The proposal identifier.</param>
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
