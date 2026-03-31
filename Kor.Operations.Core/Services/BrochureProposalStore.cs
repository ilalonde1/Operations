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
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        // Same options but with a converter that skips all byte[] values — used by LoadAll()
        // so we don't deserialize megabytes of base64 image data just to show a name list.
        private static readonly JsonSerializerOptions JsonOptionsNoImages = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(), new SkipByteArrayConverter() },
        };

        private static readonly string DefaultProposalsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KorOperations",
            "proposals");

        private readonly string _proposalsFolder;

        /// <summary>
        /// Initializes a new instance of the <see cref="BrochureProposalStore"/> class.
        /// </summary>
        public BrochureProposalStore()
            : this(DefaultProposalsFolder)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BrochureProposalStore"/> class.
        /// </summary>
        /// <param name="proposalsFolder">The folder that stores brochure proposal JSON files.</param>
        public BrochureProposalStore(string proposalsFolder)
        {
            _proposalsFolder = proposalsFolder ?? throw new ArgumentNullException(nameof(proposalsFolder));
            Directory.CreateDirectory(_proposalsFolder);
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
        /// Loads all brochure proposals from disk with image bytes stripped for performance.
        /// Use <see cref="Load"/> to retrieve a single proposal with full image data.
        /// </summary>
        /// <returns>The stored brochure proposals ordered by modification time (no embedded images).</returns>
        public List<BrochureProposal> LoadAll()
        {
            var result = new List<BrochureProposal>();

            foreach (var file in Directory.GetFiles(_proposalsFolder, "*.json"))
            {
                try
                {
                    var proposal = JsonSerializer.Deserialize<BrochureProposal>(
                        File.ReadAllText(file), JsonOptionsNoImages);
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

        /// <inheritdoc/>
        public BrochureProposal? Load(string id)
        {
            var path = GetPath(id);
            if (!File.Exists(path))
                return null;

            try
            {
                return JsonSerializer.Deserialize<BrochureProposal>(
                    File.ReadAllText(path), JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Deletes the brochure proposal with the specified identifier.
        /// </summary>
        /// <param name="id">The proposal identifier.</param>
        public void Delete(string id)
        {
            var path = GetPath(id);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private string GetPath(string id) =>
            Path.Combine(_proposalsFolder, $"{id}.json");
    }

    /// <summary>
    /// Reads and discards byte[] JSON values (base64 strings or null) without allocating image data.
    /// Used by LoadAll() so we never deserialize megabytes of embedded images for the picker list.
    /// </summary>
    public sealed class SkipByteArrayConverter : JsonConverter<byte[]>
    {
        public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Skip();
            return Array.Empty<byte>();
        }

        public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
            => writer.WriteBase64StringValue(value);
    }
}
