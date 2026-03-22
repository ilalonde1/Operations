#nullable enable
using System.Collections.Generic;
using Kor.Operations.Core.Models.Proposal;
using Kor.Operations.Core.Services;

namespace Kor.Operations.App.FeeProposal
{
    internal static class ProposalLibrarySeed
    {
        public static void EnsureSeeded(IProposalBlockLibraryStore store)
        {
            var existingNames = new HashSet<string>(
                store.LoadAll().ConvertAll(t => t.Name),
                System.StringComparer.OrdinalIgnoreCase);

            foreach (var t in BuildTemplates())
                if (!existingNames.Contains(t.Name))
                    store.Save(t);
        }

        private static List<ProposalBlockTemplate> BuildTemplates()
        {
            return new List<ProposalBlockTemplate>
            {
                new()
                {
                    Name = "Company - Standard",
                    Category = "Company",
                    BlockType = ProposalBlockType.Company,
                    Content = new FeeProposalBlock
                    {
                        BlockType = ProposalBlockType.Company,
                        TemplateName = "Company - Standard",
                        Company = new CompanyBlockContent
                        {
                            Heading = "Our Company",
                            Sections = new()
                            {
                                new CompanySection
                                {
                                    Title = "The Firm",
                                    Body = "Kor Structural (\"KOR\") is a Vancouver based Structural Engineering firm with satellite offices in Kelowna and Nanaimo. For over 25 years, we've provided expert consultation, design, and field review services throughout British Columbia and across North America.\n\nWith a portfolio of over 2,000 projects across Canada and the United States, we specialize in delivering efficient, creative, and buildable Structural solutions. Our expertise covers all building types from tall towers (up to 65 storeys), mixed-use commercial and residential projects, light industrial warehouses, institutional/public facilities, sports facilities, extended care facilities, hotels, office buildings, and more.\n\nOur team of Professional Engineers are licensed in provinces across Canada and in multiple U.S. States, including Washington, California, Arizona, and Texas.\n\nFounded by three seasoned Professional Engineers, Kor has grown to a team of 40+ professionals driven by a commitment to superior client service, innovative design, and quality execution.",
                                },
                                new CompanySection
                                {
                                    Title = "Our Services",
                                    Body = "At Kor, Structural Engineering is a seamless blend of creative design and practical solutions where we bring innovative structures to life through collaboration and precision. Our expertise includes:\n\nResidential / Commercial: High-rise reinforced concrete structures, Mid-rise multifamily buildings (wood and concrete), Structural steel buildings, Commercial developments and mixed-use projects, Office buildings and hotels.\n\nCommunity, Cultural, and Industrial: Community and cultural buildings, Healthcare facilities and social housing, Sport facilities and arenas, Warehouses and light industrial facilities.\n\nRenovations & Retrofits: Renovations, retrofits, and structural upgrades, Analysis and design of unique or complex structural systems.\n\nPerformance-Based Design: Seismic performance-based analysis and design.",
                                },
                            },
                        },
                    },
                },
                new()
                {
                    Name = "Scope - British Columbia",
                    Category = "Scope",
                    BlockType = ProposalBlockType.Scope,
                    Content = new FeeProposalBlock
                    {
                        BlockType = ProposalBlockType.Scope,
                        TemplateName = "Scope - British Columbia",
                        Scope = new ScopeBlockContent
                        {
                            Heading = "Scope of Structural Services",
                            CadPlatform = "Revit/BIM LOD 300",
                            Jurisdiction = "British Columbia",
                            Narrative = "Our scope of services includes detailed coordination with the Architect and other consultants, as well as developing the project. We will assist the Architect in completing Schematic Design Documents along with providing local technical expertise and participate in local engineering/sub-consultant coordination in support of this phase of the work.",
                            IncludedServices = new()
                            {
                                new ScopeItem { Text = "All structural engineering design and specifications and working drawings for all structural elements of the project. Working drawings will be done in Revit/BIM LOD 300." },
                                new ScopeItem { Text = "Professional Letter of Assurance (Schedule B) as per the 2025 Vancouver Building Bylaw to the City of Vancouver in support of the Building Permit application." },
                                new ScopeItem { Text = "Attendance at design meetings as requested by the Architect and/or Client." },
                                new ScopeItem { Text = "Site visits by qualified personnel required by the building authority, with such frequency as is necessary to observe the various stages of structural work, and to ascertain that it is being done in general conformance with the structural drawings and Code requirements." },
                                new ScopeItem { Text = "Review of structural shop drawings." },
                                new ScopeItem { Text = "Review of testing reports from testing and inspection agencies to determine compliance with the structural drawings and Code requirements." },
                                new ScopeItem { Text = "Responding to Request for Information (RFI) and issuing Structural Site Instructions (SSI) and details to the Contractor during construction." },
                                new ScopeItem { Text = "Professional Letter of Assurance (Schedule C-B) in support of project completion." },
                            },
                        },
                    },
                },
                new()
                {
                    Name = "Scope - Alberta",
                    Category = "Scope",
                    BlockType = ProposalBlockType.Scope,
                    Content = new FeeProposalBlock
                    {
                        BlockType = ProposalBlockType.Scope,
                        TemplateName = "Scope - Alberta",
                        Scope = new ScopeBlockContent
                        {
                            Heading = "Scope of Structural Services",
                            CadPlatform = "Revit/BIM LOD 300",
                            Jurisdiction = "Alberta",
                            Narrative = "Our scope of services includes detailed coordination with the Architect and other consultants, as well as developing the project in accordance with the Alberta Building Code.",
                            IncludedServices = new()
                            {
                                new ScopeItem { Text = "All structural engineering design and specifications and working drawings for all structural elements of the project. Working drawings will be done in Revit/BIM LOD 300 or AutoCAD." },
                                new ScopeItem { Text = "Professional Schedules (A-2 and Schedule B-2) as per the Alberta Building Code (ABC) and National Building Code - 2019 Alberta Edition (NBC-2019 AE)." },
                                new ScopeItem { Text = "Attendance at design meetings as requested by the Architect and/or Client." },
                                new ScopeItem { Text = "Site visits by qualified personnel required by the building authority." },
                                new ScopeItem { Text = "Review of structural shop drawings." },
                                new ScopeItem { Text = "Review of testing reports from testing and inspection agencies." },
                                new ScopeItem { Text = "Responding to Request for Information (RFI) and issuing Structural Site Instructions (SSI)." },
                                new ScopeItem { Text = "Professional Schedule (C-2) in support of project completion." },
                            },
                        },
                    },
                },
                new()
                {
                    Name = "Excluded Services - Standard",
                    Category = "ExcludedServices",
                    BlockType = ProposalBlockType.ExcludedServices,
                    Content = new FeeProposalBlock
                    {
                        BlockType = ProposalBlockType.ExcludedServices,
                        TemplateName = "Excluded Services - Standard",
                        ExcludedServices = new ExcludedServicesBlockContent
                        {
                            ExcludedItems = new()
                            {
                                "Subsurface soil investigation and report, and inspection of the geotechnical aspects of foundation construction; sampling, testing, and reporting on materials used in construction; and retaining specialist consultants such as geotechnical engineers and testing engineers.",
                                "Design and construction review of concrete formwork and design changes to accommodate temporary construction conditions.",
                                "Design and supervision of excavation shoring and underpinning of adjacent buildings.",
                                "Design and construction review of any temporary construction conditions.",
                                "Design changes to accommodate temporary construction conditions and review of design effects on the structural design due to temporary conditions.",
                                "Design and supervision of non-structural components. Normal current practice is that non-structural components are designed and certified by subtrades' engineers.",
                                "Services outside the original scope of work outlined above will be billed on an hourly rates basis in addition to the proposed fixed fees.",
                                "Normal disbursements, which include courier costs, plotting of other consultants' drawings, and printing, etc. We do not markup disbursements.",
                                "Government tax on professional fees.",
                            },
                        },
                    },
                },
                new()
                {
                    Name = "Approval to Proceed - Standard",
                    Category = "ApprovalToProceed",
                    BlockType = ProposalBlockType.ApprovalToProceed,
                    Content = new FeeProposalBlock
                    {
                        BlockType = ProposalBlockType.ApprovalToProceed,
                        TemplateName = "Approval to Proceed - Standard",
                        ApprovalToProceed = new ApprovalToProceedBlockContent(),
                    },
                },
                new()
                {
                    Name = "References - Standard",
                    Category = "References",
                    BlockType = ProposalBlockType.References,
                    Content = new FeeProposalBlock
                    {
                        BlockType = ProposalBlockType.References,
                        TemplateName = "References - Standard",
                        References = new ReferencesBlockContent
                        {
                            ClientNames = new()
                            {
                                "Allure Ventures", "Amacon Developments", "Anthem Properties Group",
                                "Aoyuan Group", "Appia Development", "Aquilini Investments",
                                "Atira Development Society", "Bastion Development", "BC Housing",
                                "Beedie Development", "Blue Sky Properties", "Boffo Developments",
                                "Bold Properties", "Bonnis Development", "Bosa Development",
                                "Bosa Properties", "Bosa Ventures", "Bucci Developments",
                                "Cast Development LLC", "Carrier Sekani Family Services", "Cielle Properties",
                                "Cressey Development Group", "Darwin Properties", "Domus Homes",
                                "Embassy Bosa", "Executive Group", "Formwerks",
                                "Boutique Properties", "Holborn Developments", "Holland Partner Group",
                                "Hudson Projects", "Imani Developments", "Intergulf Development",
                                "Intracorp Homes", "Indigenous Services Canadac", "Jim Pattison Developments",
                                "Kevington Building Corp", "Kekinow Native Housing Society", "Larco Investments",
                                "Ledcor Group", "Lu'ma Development Management", "Lyndan Properties",
                                "Marcon Group of Companies", "Millennium Group", "Mondiale Developments",
                                "Mosaic Homes", "Musqueam Capital Corporation", "NSDA Architects",
                                "Oakridge LP", "Onni Group", "Pennyfarthing Developments",
                                "Peterson Group", "Pinnacle International", "Polygon Homes",
                                "Porte Realty", "Redekop Development", "Reliance Properties",
                                "Solterra Development", "Square Nine Development", "Strand Development",
                                "Thind Properties", "TL Housing Solutions", "Townline Homes",
                                "VanMar Constructors", "Ventana Construction", "Vertex Developments",
                                "Wesgroup Properties", "Westbank First Nation", "Westland",
                                "WestStone Properties", "Wiebe Group of Companies",
                            },
                        },
                    },
                },
                new()
                {
                    Name = "Rates Table - Standard 2025",
                    Category = "RatesTable",
                    BlockType = ProposalBlockType.RatesTable,
                    Content = new FeeProposalBlock
                    {
                        BlockType = ProposalBlockType.RatesTable,
                        TemplateName = "Rates Table - Standard 2025",
                        RatesTable = new RatesTableBlockContent
                        {
                            EffectiveDate = "May 1, 2025",
                        },
                    },
                },
                new()
                {
                    Name = "Signature Page - Standard",
                    Category = "SignaturePage",
                    BlockType = ProposalBlockType.SignaturePage,
                    Content = new FeeProposalBlock
                    {
                        BlockType = ProposalBlockType.SignaturePage,
                        TemplateName = "Signature Page - Standard",
                        SignaturePage = new SignaturePageBlockContent
                        {
                            IncludeRatesAppendix = true,
                        },
                    },
                },
                new()
                {
                    Name = "Introduction - Standard",
                    Category = "Introduction",
                    BlockType = ProposalBlockType.Introduction,
                    Content = new FeeProposalBlock
                    {
                        BlockType = ProposalBlockType.Introduction,
                        TemplateName = "Introduction - Standard",
                        Introduction = new IntroductionBlockContent
                        {
                            CloserText = "Thank you for the opportunity to present you with this proposal and we look forward to hearing from you. Please do not hesitate to contact us with any questions.",
                        },
                    },
                },
                new()
                {
                    Name = "Personnel - Standard",
                    Category = "Personnel",
                    BlockType = ProposalBlockType.Personnel,
                    Content = new FeeProposalBlock
                    {
                        BlockType = ProposalBlockType.Personnel,
                        TemplateName = "Personnel - Standard",
                        Personnel = new PersonnelBlockContent(),
                    },
                },
                new()
                {
                    Name = "Fee Table - Standard",
                    Category = "FeeTable",
                    BlockType = ProposalBlockType.FeeTable,
                    Content = new FeeProposalBlock
                    {
                        BlockType = ProposalBlockType.FeeTable,
                        TemplateName = "Fee Table - Standard",
                        FeeTable = new FeeTableBlockContent(),
                    },
                },
                new()
                {
                    Name = "Project Description - Standard",
                    Category = "ProjectDescription",
                    BlockType = ProposalBlockType.ProjectDescription,
                    Content = new FeeProposalBlock
                    {
                        BlockType = ProposalBlockType.ProjectDescription,
                        TemplateName = "Project Description - Standard",
                        ProjectDescription = new ProjectDescriptionBlockContent
                        {
                            Preamble = "Our proposed fees are based on the following assumptions:",
                        },
                    },
                },
            };
        }
    }
}
