#nullable enable
using System.Collections.Generic;
using Kor.Operations.Core.Models.Proposal;
using Kor.Operations.Core.Services;

namespace Kor.Operations.App.FeeProposal
{
    internal static class ProposalStaffSeed
    {
        public static void EnsureSeeded(IProposalStaffStore store)
        {
            if (store.LoadAll().Count > 0)
                return;

            store.SaveAll(new List<ProposalStaffMember>
            {
                new() { Id = "6e041b48c8374e14bd46a27c8f6282d9", FullName = "John Markulin",         Credentials = "M.Eng., P.Eng., Struct.Eng., PE, SE",          Title = "", PhotoPath = @"Brochures\SeedData\OriginalAssets\image8.jpeg" },
                new() { Id = "a3f1d2e8b4c54f6a9d0e7b1c2f3a4d5e", FullName = "Omar Alcazar Pastrana", Credentials = "M.A.Sc., P.Eng.",                              Title = "Senior Structural Engineer, Associate Principal" },
                new() { Id = "b7c2e4f1a3d54e8b9f0a1c2d3e4f5a6b", FullName = "Islam Shabana",         Credentials = "Ph.D., P.Eng.",                                Title = "Senior Structural Engineer" },
                new() { Id = "c9d3f5a2b4e64f7a8b0c1d2e3f4a5b6c", FullName = "Andr\u00e9a Neuviale",  Credentials = "M.Eng., P.Eng.",                               Title = "Structural Engineer" },
                new() { Id = "d1e4a6b3c5f74e8b9a0b1c2d3e4f5a6d", FullName = "Simon Szarkiewicz",     Credentials = "",                                             Title = "Senior Structural BIM / CAD Manager" },
                new() { Id = "e2f5b7c4d6a84f9a0b1c2d3e4f5a6b7e", FullName = "Michael Mousa",         Credentials = "",                                             Title = "Senior Structural BIM / CAD Technologist" },
                new() { Id = "f3a6c8d5e7b94f0a1b2c3d4e5f6a7b8f", FullName = "Lindsay Finnigan",      Credentials = "",                                             Title = "Senior Structural BIM / CAD Technologist" },
                new() { Id = "a4b7d9e6f8c04f1a2b3c4d5e6f7a8b9a", FullName = "Chris Ford",            Credentials = "",                                             Title = "Structural BIM / CAD Technologist" },
                new() { Id = "b5c8e0f7a9d14f2a3b4c5d6e7f8a9b0c", FullName = "Kevin Wurmlinger",      Credentials = "P.Eng., Struct.Eng., CEng, MIStructE",         Title = "", PhotoPath = @"Brochures\SeedData\OriginalAssets\image11.jpeg" },
                new() { Id = "c6d9f1a8b0e24f3a4b5c6d7e8f9a0b1d", FullName = "Jim Deroches",          Credentials = "BASc., P.Eng., PE",                            Title = "", PhotoPath = @"Brochures\SeedData\OriginalAssets\image9.jpeg" },
                new() { Id = "d7e0a2b9c1f34f4a5b6c7d8e9f0a1b2e", FullName = "Rory Beirne",           Credentials = "M.Eng., P.Eng., Struct.Eng., C.Eng., MIStruct", Title = "", PhotoPath = @"Brochures\SeedData\OriginalAssets\image10.jpeg" },
                new() { Id = "e8f1b3c0d2a44f5a6b7c8d9e0f1a2b3f", FullName = "Mark Bakhtavar",        Credentials = "MASc., P.Eng.",                                Title = "" },
                new() { Id = "f9a2c4d1e3b54f6a7b8c9d0e1f2a3b4a", FullName = "Jason Stuart",          Credentials = "",                                             Title = "Senior Structural Designer", PhotoPath = @"Brochures\SeedData\OriginalAssets\image12.jpeg" },
            });
        }
    }
}
