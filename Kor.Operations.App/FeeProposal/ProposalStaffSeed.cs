#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.Core.Models.Proposal;
using Kor.Operations.Core.Services;

namespace Kor.Operations.App.FeeProposal
{
    internal static class ProposalStaffSeed
    {
        public static void EnsureSeeded(IProposalStaffStore store)
        {
            var existing = store.LoadAll();
            var byName = existing.ToDictionary(s => s.FullName, StringComparer.OrdinalIgnoreCase);

            var seeds = BuildSeedList();
            var changed = false;

            foreach (var seed in seeds)
            {
                if (byName.TryGetValue(seed.FullName, out var found))
                {
                    // Update fields that may have been added since original seed — preserve existing Id
                    var dirty = false;
                    if (string.IsNullOrWhiteSpace(found.Credentials) && !string.IsNullOrWhiteSpace(seed.Credentials)) { found.Credentials = seed.Credentials; dirty = true; }
                    if (string.IsNullOrWhiteSpace(found.Title)       && !string.IsNullOrWhiteSpace(seed.Title))       { found.Title       = seed.Title;       dirty = true; }
                    if (string.IsNullOrWhiteSpace(found.Email)       && !string.IsNullOrWhiteSpace(seed.Email))       { found.Email       = seed.Email;       dirty = true; }
                    if (string.IsNullOrWhiteSpace(found.Phone)       && !string.IsNullOrWhiteSpace(seed.Phone))       { found.Phone       = seed.Phone;       dirty = true; }
                    if (string.IsNullOrWhiteSpace(found.Bio)         && !string.IsNullOrWhiteSpace(seed.Bio))         { found.Bio         = seed.Bio;         dirty = true; }
                    if (string.IsNullOrWhiteSpace(found.PhotoPath)   && !string.IsNullOrWhiteSpace(seed.PhotoPath))   { found.PhotoPath   = seed.PhotoPath;   dirty = true; }
                    if (dirty) changed = true;
                }
                else
                {
                    existing.Add(seed);
                    changed = true;
                }
            }

            if (changed)
                store.SaveAll(existing);
        }

        private static List<ProposalStaffMember> BuildSeedList() => new()
        {
            new() { FullName = "John Markulin",        Credentials = "M.Eng., P.Eng., Struct.Eng., PE, SE", Title = "Managing Principal",            PhotoPath = @"Brochures\SeedData\OriginalAssets\image8.jpeg" },
            new() { FullName = "Omar Alcazar Pastrana", Credentials = "M.Eng., P.Eng.",                     Title = "Principal" },
            new() { FullName = "Islam Shabana",         Credentials = "Ph.D., P.Eng.",                      Title = "Principal" },
            new() { FullName = "Andréa Neuviale",       Credentials = "M.Eng., P.Eng.",                     Title = "Associate Principal" },
            new() { FullName = "Simon Szarkiewicz",     Credentials = "M.Eng., P.Eng.",                     Title = "Associate Principal" },
            new() { FullName = "Michael Mousa",         Credentials = "M.Eng., P.Eng.",                     Title = "Senior Structural Engineer" },
            new() { FullName = "Lindsay Finnigan",      Credentials = "M.Eng., P.Eng.",                     Title = "Senior Structural Engineer" },
            new() { FullName = "Chris Ford",            Credentials = "P.Eng.",                             Title = "Structural Engineer" },
            new() { FullName = "Kevin Wurmlinger",      Credentials = "P.Eng.",                             Title = "Structural Engineer",            PhotoPath = @"Brochures\SeedData\OriginalAssets\image11.jpeg" },
            new() { FullName = "Jim Deroches",          Credentials = "P.Eng.",                             Title = "Structural Engineer",            PhotoPath = @"Brochures\SeedData\OriginalAssets\image9.jpeg" },
            new() { FullName = "Rory Beirne",           Credentials = "P.Eng.",                             Title = "Structural Engineer",            PhotoPath = @"Brochures\SeedData\OriginalAssets\image10.jpeg" },
            new() { FullName = "Mark Bakhtavar",        Credentials = "P.Eng.",                             Title = "Structural Engineer" },
            new() { FullName = "Jason Stuart",          Credentials = "P.Eng.",                             Title = "Structural Engineer",            PhotoPath = @"Brochures\SeedData\OriginalAssets\image12.jpeg" },
        };
    }
}
