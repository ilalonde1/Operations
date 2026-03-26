#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.Core.Services
{
    public sealed class BrochureContactStore : IBrochureContactStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KorOperations",
            "contact.json");

        public BrochureContactConfig Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    return JsonSerializer.Deserialize<BrochureContactConfig>(json, JsonOptions)
                        ?? new BrochureContactConfig();
                }
            }
            catch
            {
            }

            return new BrochureContactConfig();
        }

        public void Save(BrochureContactConfig config)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(config, JsonOptions));
        }
    }
}
