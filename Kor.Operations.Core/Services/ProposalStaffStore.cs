#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.Core.Services
{
    public sealed class ProposalStaffStore
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

        public ProposalStaffStore() : this(DefaultPath) { }

        public ProposalStaffStore(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        }

        public List<ProposalStaffMember> LoadAll()
        {
            if (!File.Exists(_path)) return new List<ProposalStaffMember>();
            try
            {
                return JsonSerializer.Deserialize<List<ProposalStaffMember>>(
                    File.ReadAllText(_path), JsonOptions)
                    ?? new List<ProposalStaffMember>();
            }
            catch { return new List<ProposalStaffMember>(); }
        }

        public void SaveAll(List<ProposalStaffMember> staff)
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(staff, JsonOptions));
        }
    }
}
