#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.Core.Services
{
    public sealed class BrochureProposalStore : IBrochureProposalStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

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

        public BrochureProposalStore()
            : this(DefaultProposalsFolder)
        {
        }

        public BrochureProposalStore(string proposalsFolder)
        {
            _proposalsFolder = proposalsFolder ?? throw new ArgumentNullException(nameof(proposalsFolder));
            Directory.CreateDirectory(_proposalsFolder);
        }

        public async Task SaveAsync(BrochureProposal proposal, CancellationToken ct = default)
        {
            proposal.ModifiedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(proposal, JsonOptions);
            await File.WriteAllTextAsync(GetPath(proposal.Id), json, ct).ConfigureAwait(false);
        }

        public async Task<List<BrochureProposal>> LoadAllAsync(CancellationToken ct = default)
        {
            var result = new List<BrochureProposal>();

            foreach (var file in Directory.GetFiles(_proposalsFolder, "*.json"))
            {
                try
                {
                    var text = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                    var proposal = JsonSerializer.Deserialize<BrochureProposal>(text, JsonOptionsNoImages);
                    if (proposal is not null)
                        result.Add(proposal);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    // Skip corrupt/unreadable files — the picker shows fewer
                    // proposals rather than crashing. The file remains on disk
                    // for manual recovery.
                }
            }

            return result.OrderByDescending(static p => p.ModifiedAt).ToList();
        }

        public async Task<BrochureProposal?> LoadAsync(string id, CancellationToken ct = default)
        {
            var path = GetPath(id);
            if (!File.Exists(path))
                return null;

            try
            {
                var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                return JsonSerializer.Deserialize<BrochureProposal>(text, JsonOptions);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                // File exists but is corrupt/unreadable — return null so the
                // caller sees "not found" rather than crashing. The file
                // remains on disk for manual inspection.
                return null;
            }
        }

        public Task DeleteAsync(string id, CancellationToken ct = default)
        {
            var path = GetPath(id);
            if (File.Exists(path))
                File.Delete(path);
            return Task.CompletedTask;
        }

        private string GetPath(string id) =>
            Path.Combine(_proposalsFolder, $"{id}.json");
    }

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
