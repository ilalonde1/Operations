using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Core.Models.Proposal;
using Kor.Operations.Core.Services;
using Kor.Operations.Data;
using Kor.Operations.Rendering.Brochure;
using Kor.Operations.Rendering.Proposal;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

internal sealed class ImportRunner
{
    private const int SchemaCommandTimeoutSeconds = 120;

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = ImportOptions.Parse(args);
            options.Validate();

            Console.WriteLine("Resolving DB connection...");
            var connectionString = ResolveConnectionString(options.AppConfigPath);

            Console.WriteLine("Ensuring ProposalStaff photo schema...");
            await EnsureProposalStaffPhotoSchemaAsync(connectionString);

            using var brochureReader = new PdfDocumentReader(options.BrochurePdfPath, options.PopplerBinPath);
            var staffStore = new SqlProposalStaffStore(connectionString);
            var blockStore = new SqlProposalBlockLibraryStore(connectionString);
            var feeStore = new SqlFeeProposalStore(connectionString);
            var brochureStore = new SqlBrochureProposalStore(connectionString);

            Console.WriteLine("Importing shared staff roster...");
            var staffByName = await MergeAndSaveStaffAsync(staffStore, BuildImportedStaff(brochureReader));

            Console.WriteLine("Importing fee proposal example and reusable templates...");
            await ImportFeeProposalArtifactsAsync(blockStore, feeStore, staffByName, options.FeeDocxPath);

            Console.WriteLine("Importing brochure proposal with embedded project/staff photos...");
            await ImportBrochureProposalAsync(brochureStore, brochureReader, staffByName, options.BrochurePdfPath);

            Console.WriteLine("Running render smoke tests from DB...");
            await SmokeTestRenderAsync(feeStore, brochureStore, staffStore, options.FeeDocxPath, options.BrochurePdfPath);

            Console.WriteLine("Import complete.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static string ResolveConnectionString(string appConfigPath)
    {
        var doc = XDocument.Load(appConfigPath);
        var node = doc.Descendants("add")
            .FirstOrDefault(x => string.Equals((string?)x.Attribute("name"), "KorTransmittalsDb", StringComparison.OrdinalIgnoreCase));
        var connectionString = (string?)node?.Attribute("connectionString");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("KorTransmittalsDb connection string not found in App.config.");

        var builder = new SqlConnectionStringBuilder(connectionString);
        var envUser = Environment.GetEnvironmentVariable("KOR_DB_USER");
        var envPassword = Environment.GetEnvironmentVariable("KOR_DB_PASSWORD");
        if (!string.IsNullOrWhiteSpace(envUser))
            builder.UserID = envUser;
        if (!string.IsNullOrWhiteSpace(envPassword))
            builder.Password = envPassword;
        return builder.ConnectionString;
    }

    private static async Task EnsureProposalStaffPhotoSchemaAsync(string connectionString)
    {
        const string sql = @"
IF OBJECT_ID('dbo.ProposalStaff', 'U') IS NULL
BEGIN
    THROW 50000, 'dbo.ProposalStaff does not exist.', 1;
END;

IF COL_LENGTH('dbo.ProposalStaff', 'PhotoBytes') IS NULL
BEGIN
    ALTER TABLE dbo.ProposalStaff ADD PhotoBytes VARBINARY(MAX) NULL;
END;";

        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SchemaCommandTimeoutSeconds };
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<Dictionary<string, ProposalStaffMember>> MergeAndSaveStaffAsync(IProposalStaffStore store, IEnumerable<ProposalStaffMember> imported)
    {
        var existing = await store.LoadAllAsync().ConfigureAwait(false);
        var byName = existing.ToDictionary(x => x.FullName, StringComparer.OrdinalIgnoreCase);

        foreach (var incoming in imported)
        {
            if (byName.TryGetValue(incoming.FullName, out var current))
            {
                current.Credentials = Prefer(current.Credentials, incoming.Credentials);
                current.Title = Prefer(current.Title, incoming.Title);
                current.Email = Prefer(current.Email, incoming.Email);
                current.Phone = Prefer(current.Phone, incoming.Phone);
                current.Bio = Prefer(current.Bio, incoming.Bio);
                current.PhotoPath = Prefer(current.PhotoPath, incoming.PhotoPath);
                if ((current.PhotoBytes?.Length ?? 0) == 0 && incoming.PhotoBytes is { Length: > 0 } photo)
                    current.PhotoBytes = photo;
            }
            else
            {
                existing.Add(incoming);
                byName[incoming.FullName] = incoming;
            }
        }

        await store.SaveAllAsync(existing.OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase).ToList()).ConfigureAwait(false);
        return existing.ToDictionary(x => x.FullName, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task SmokeTestRenderAsync(
        IFeeProposalStore feeStore,
        IBrochureProposalStore brochureStore,
        IProposalStaffStore staffStore,
        string feeDocxPath,
        string brochurePdfPath)
    {
        var smokeRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "KorOperationsSmoke");
        Directory.CreateDirectory(smokeRoot);

        var feeName = Path.GetFileNameWithoutExtension(feeDocxPath);
        var brochureName = Path.GetFileNameWithoutExtension(brochurePdfPath);

        var feeProposal = (await feeStore.LoadAllAsync().ConfigureAwait(false))
            .FirstOrDefault(x => string.Equals(x.Name, feeName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Fee proposal '{feeName}' not found in DB.");

        var brochureProposal = (await brochureStore.LoadAllAsync().ConfigureAwait(false))
            .FirstOrDefault(x => string.Equals(x.Name, brochureName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Brochure proposal '{brochureName}' not found in DB.");

        var staff = await staffStore.LoadAllAsync().ConfigureAwait(false);

        var feePdfPath = Path.Combine(smokeRoot, $"{feeProposal.Name}.pdf");
        var feeDocxOutputPath = Path.Combine(smokeRoot, $"{feeProposal.Name}.docx");
        var brochurePdfOutputPath = Path.Combine(smokeRoot, $"{brochureProposal.Name}.pdf");

        var feePdfRenderer = new FeeProposalRenderer(NullLogger<FeeProposalRenderer>.Instance);
        var feeDocxRenderer = new FeeProposalDocxRenderer();
        var brochureRenderer = new BrochureRenderer(NullLogger<BrochureRenderer>.Instance);

        await feePdfRenderer.RenderAsync(feeProposal, staff, feePdfPath, default);
        await feeDocxRenderer.RenderAsync(feeProposal, staff, feeDocxOutputPath, default);
        await brochureRenderer.RenderAsync(brochureProposal.Content, brochurePdfOutputPath, default);

        Console.WriteLine($"Smoke output: {feePdfPath}");
        Console.WriteLine($"Smoke output: {feeDocxOutputPath}");
        Console.WriteLine($"Smoke output: {brochurePdfOutputPath}");
    }

    private static async Task ImportFeeProposalArtifactsAsync(
        IProposalBlockLibraryStore blockStore,
        IFeeProposalStore feeStore,
        IReadOnlyDictionary<string, ProposalStaffMember> staffByName,
        string feeDocxPath)
    {
        var paragraphs = ReadDocxParagraphs(feeDocxPath);
        var feeProposal = BuildFeeProposal(paragraphs, staffByName);

        var allFeeProposals = await feeStore.LoadAllAsync().ConfigureAwait(false);
        var existingProposal = allFeeProposals
            .FirstOrDefault(x => string.Equals(x.Name, feeProposal.Name, StringComparison.OrdinalIgnoreCase));
        if (existingProposal is not null)
            feeProposal.Id = existingProposal.Id;
        await feeStore.SaveAsync(feeProposal).ConfigureAwait(false);

        var existingTemplates = await blockStore.LoadAllAsync().ConfigureAwait(false);
        foreach (var template in BuildProposalTemplates(feeProposal))
        {
            var existingTemplate = existingTemplates
                .FirstOrDefault(x => string.Equals(x.Name, template.Name, StringComparison.OrdinalIgnoreCase));
            if (existingTemplate is not null)
                template.Id = existingTemplate.Id;
            await blockStore.SaveAsync(template).ConfigureAwait(false);
        }
    }

    private static async Task ImportBrochureProposalAsync(
        IBrochureProposalStore brochureStore,
        PdfDocumentReader brochureReader,
        IReadOnlyDictionary<string, ProposalStaffMember> staffByName,
        string brochurePdfPath)
    {
        var proposal = BuildBrochureProposal(brochureReader, staffByName, brochurePdfPath);
        var existing = (await brochureStore.LoadAllAsync().ConfigureAwait(false))
            .FirstOrDefault(x => string.Equals(x.Name, proposal.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            proposal.Id = existing.Id;
        await brochureStore.SaveAsync(proposal).ConfigureAwait(false);
    }

    private static IEnumerable<ProposalStaffMember> BuildImportedStaff(PdfDocumentReader reader)
    {
        var contactPage = reader.GetPageText(2);
        var staff = new List<ProposalStaffMember>
        {
            BuildStaffMember(reader, 5, "John Markulin", "Jim DesRoches", "M.Eng., P.Eng., Struct.Eng., PE, SE", "Managing Principal, Senior Structural Engineer"),
            BuildStaffMember(reader, 5, "Jim DesRoches", null, "BASc., P.Eng., PE", "Principal, Senior Structural Engineer", imageIndex: 1),
            BuildStaffMember(reader, 6, "Jeremy Atkinson", "Rory Beirne", "M.Sc., P.Eng., PE", "Principal, Senior Structural Engineer"),
            BuildStaffMember(reader, 6, "Rory Beirne", null, "M.Eng., P.Eng., Struct.Eng.", "Principal, Senior Structural Engineer", imageIndex: 1),
            BuildStaffMember(reader, 7, "Jason Stuart", "Kevin Wurmlinger", string.Empty, "Principal, Senior Structural Designer"),
            BuildStaffMember(reader, 7, "Kevin Wurmlinger", null, "P.Eng., Struct.Eng., CEng, MIStructE", "Principal, Senior Structural Engineer", imageIndex: 1),
            BuildStaffMember(reader, 8, "Omar Alcazar Pastrana", "Conor Murtagh", "M.A.Sc., P.Eng.", "Associate Principal, Structural Engineer"),
            BuildStaffMember(reader, 8, "Conor Murtagh", null, "B.A.Sc., P.Eng.", "Associate Principal, Senior Structural Engineer", imageIndex: 1),
            BuildStaffMember(reader, 9, "John Bryson", null, string.Empty, "Senior Structural Consultant, Management Advisor"),
            new() { FullName = "Islam Shabana", Credentials = "Ph.D., P.Eng.", Title = "Senior Structural Engineer" },
            new() { FullName = "Andréa Neuviale", Credentials = "M.Eng., P.Eng.", Title = "Structural Engineer" },
            new() { FullName = "Simon Szarkiewicz", Title = "Senior Structural BIM / CAD Manager" },
            new() { FullName = "Michael Mousa", Title = "Senior Structural BIM / CAD Technologist" },
            new() { FullName = "Lindsay Finnigan", Title = "Senior Structural BIM / CAD Technologist" },
            new() { FullName = "Chris Ford", Title = "Structural BIM / CAD Technologist" },
        };

        ApplyContact(staff, "John Markulin", contactPage, "Vancouver");
        ApplyContact(staff, "Jim DesRoches", contactPage, "United States");
        ApplyContact(staff, "Rory Beirne", contactPage, "Vancouver Island");
        ApplyContact(staff, "Jeremy Atkinson", contactPage, "Okanagan");
        ApplyFeeContact(staff, "Kevin Wurmlinger", "kevin@korstructural.com", "604-612-0507");

        return staff;
    }

    private static ProposalStaffMember BuildStaffMember(
        PdfDocumentReader reader,
        int page,
        string name,
        string? nextName,
        string credentials,
        string title,
        int imageIndex = 0)
    {
        var images = reader.GetPageImages(page);
        return new ProposalStaffMember
        {
            FullName = name,
            Credentials = credentials,
            Title = title,
            Bio = ExtractBio(reader.GetPageText(page), name, nextName),
            PhotoBytes = imageIndex < images.Count ? images[imageIndex] : Array.Empty<byte>()
        };
    }

    private static void ApplyContact(List<ProposalStaffMember> staff, string name, string pageText, string region)
    {
        var member = staff.First(x => string.Equals(x.FullName, name, StringComparison.OrdinalIgnoreCase));
        var match = Regex.Match(
            pageText,
            $"{Regex.Escape(region)}\\s+(?<contact>.+?)\\s+T\\s+(?<phone>.+?)\\s+E\\s+(?<email>.+?)\\s+H\\s+(?<hours>.+?)(?:\\s{{2,}}|$)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!match.Success)
            return;

        member.Phone = NormalizePdfField(match.Groups["phone"].Value);
        member.Email = NormalizePdfField(match.Groups["email"].Value);
    }

    private static void ApplyFeeContact(List<ProposalStaffMember> staff, string name, string email, string phone)
    {
        var member = staff.First(x => string.Equals(x.FullName, name, StringComparison.OrdinalIgnoreCase));
        member.Email = Prefer(member.Email, email);
        member.Phone = Prefer(member.Phone, phone);
    }

    private static string ExtractBio(string pageText, string name, string? nextName)
    {
        var lines = CleanPdfLines(pageText);
        var start = lines.FindIndex(x => x.StartsWith(name, StringComparison.OrdinalIgnoreCase));
        if (start < 0)
            return string.Empty;

        var end = nextName is null
            ? lines.Count
            : lines.FindIndex(start + 1, x => x.StartsWith(nextName, StringComparison.OrdinalIgnoreCase));
        if (end < 0)
            end = lines.Count;

        return string.Join(" ", lines.Skip(start + 1).Take(end - start - 1)).Trim();
    }

    private static FeeProposal BuildFeeProposal(IReadOnlyList<string> paragraphs, IReadOnlyDictionary<string, ProposalStaffMember> staffByName)
    {
        var projectDescriptionLines = Slice(paragraphs, "Project Description", "Proposed Fees").Skip(1).ToList();
        var excludedLines = Slice(paragraphs, "Excluded Services", "Approval to Proceed").Skip(1).ToList();
        var additionalStaff = Slice(paragraphs, "Additional staff dedicated to this project will include:", "All personnel listed have extensive experience with similar projects.")
            .Skip(1)
            .Select(ParseStaffName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        return new FeeProposal
        {
            Name = "Master Fee Proposal Template KW",
            Blocks = new List<FeeProposalBlock>
            {
                new()
                {
                    TemplateName = "Cover - Imported Master KW",
                    BlockType = ProposalBlockType.Cover,
                    Cover = new CoverBlockContent
                    {
                        ProjectName = "Two-Storey Industrial Development",
                        ProjectAddress = "10860 124th Street, Surrey, B.C.",
                        ClientCompany = "Unibuild Construction Management Ltd.",
                        ClientAddress = "#202-8433 132 Street Surrey, B.C., V3W 4N8",
                        AttentionName = "Jaspreet Kuar",
                        AttentionTitle = "Development Project Coordinator",
                        AttentionEmail = "office@unibuild.ca",
                        CcName = "Vikas Mehta",
                        CcTitle = "Managing Director",
                        CcEmail = "vm@unibuild.ca",
                        PreparerStaffId = GetStaffId(staffByName, "Kevin Wurmlinger"),
                        ProposalDate = "September 22, 2025",
                        Jurisdiction = "British Columbia",
                    }
                },
                new()
                {
                    TemplateName = "Introduction - Imported Master KW",
                    BlockType = ProposalBlockType.Introduction,
                    Introduction = new IntroductionBlockContent
                    {
                        SalutationName = "Jaspreet",
                        ProjectAddress = "10860 124th Street, Surrey B.C.",
                        DrawingReference = "Matthew Cheng Architects preliminary Architectural drawings dated September 10, 2025, provided with the request for proposal email received September 18, 2025.",
                        CloserText = "Thank you for the opportunity to present you with this proposal and we look forward to hearing from you. Please do not hesitate to contact us with any questions.",
                        SignatoryStaffId = GetStaffId(staffByName, "Kevin Wurmlinger")
                    }
                },
                new()
                {
                    TemplateName = "Company - Imported Master KW",
                    BlockType = ProposalBlockType.Company,
                    Company = new CompanyBlockContent
                    {
                        Heading = "Our Company",
                        Sections = new ObservableCollection<CompanySection>
                        {
                            new() { Title = "The Firm", Body = string.Join(" ", Slice(paragraphs, "The Firm", "Our Services").Skip(1)) },
                            new() { Title = "Our Services", Body = string.Join(" ", Slice(paragraphs, "Our Services", "Project Personnel and Experience").Skip(1)) }
                        }
                    }
                },
                new()
                {
                    TemplateName = "Personnel - Imported Master KW",
                    BlockType = ProposalBlockType.Personnel,
                    Personnel = new PersonnelBlockContent
                    {
                        LeadStaffId = GetStaffId(staffByName, "Kevin Wurmlinger"),
                        SupportingStaffId = GetStaffId(staffByName, "John Markulin"),
                        CollaborationNote = paragraphs.First(x => x.StartsWith("Kevin Wurmlinger and John Markulin will collaborate", StringComparison.Ordinal)),
                        AdditionalStaffIds = new ObservableCollection<string>(additionalStaff.Select(x => GetStaffId(staffByName, x)).Where(x => !string.IsNullOrWhiteSpace(x)))
                    }
                },
                new()
                {
                    TemplateName = "References - Imported Master KW",
                    BlockType = ProposalBlockType.References,
                    References = new ReferencesBlockContent
                    {
                        Preamble = paragraphs.First(x => x.StartsWith("Many of our best clients include", StringComparison.Ordinal)),
                        ProjectSpecificNote = paragraphs.First(x => x.StartsWith("Kor Structural has an excellent working relationship with TKA+D Architecture", StringComparison.Ordinal)),
                    }
                },
                new()
                {
                    TemplateName = "Project Description - Imported Master KW",
                    BlockType = ProposalBlockType.ProjectDescription,
                    ProjectDescription = new ProjectDescriptionBlockContent
                    {
                        Preamble = "Our proposed fees are based on the following assumptions:",
                        AssumptionBullets = new ObservableCollection<string>(projectDescriptionLines)
                    }
                },
                new()
                {
                    TemplateName = "Fee Table - Imported Master KW",
                    BlockType = ProposalBlockType.FeeTable,
                    FeeTable = new FeeTableBlockContent
                    {
                        AdditionalNotes = new ObservableCollection<string>(Slice(paragraphs, "† Additional field reviews over the above specified limit will be billed at a rate of $350 per visit, covering up to 2.0 hours, at which point hourly rates will apply.", "Scope of Structural Services")),
                        DisbursementsAllowance = 1500m,
                        FieldReviewVisits = 0
                    }
                },
                new()
                {
                    TemplateName = "Scope - Imported Master KW",
                    BlockType = ProposalBlockType.Scope,
                    Scope = new ScopeBlockContent
                    {
                        Narrative = paragraphs.First(x => x.StartsWith("Our scope of services includes detailed coordination", StringComparison.Ordinal)),
                        CadPlatform = "Revit/BIM LOD 300 or AutoCAD",
                        Jurisdiction = "British Columbia",
                        IncludedServices = new ObservableCollection<ScopeItem>(Slice(paragraphs, "The foregoing proposed fees are for normal basic structural engineering services as follows:", "Please note that the field review services outlined above does not make us guarantors of the contractor's work.").Skip(1).Select(x => new ScopeItem { Text = x }))
                    }
                },
                new()
                {
                    TemplateName = "Excluded Services - Imported Master KW",
                    BlockType = ProposalBlockType.ExcludedServices,
                    ExcludedServices = new ExcludedServicesBlockContent
                    {
                        ExcludedItems = new ObservableCollection<string>(excludedLines)
                    }
                },
                new()
                {
                    TemplateName = "Approval to Proceed - Imported Master KW",
                    BlockType = ProposalBlockType.ApprovalToProceed,
                    ApprovalToProceed = new ApprovalToProceedBlockContent
                    {
                        IntroParagraph = paragraphs.First(x => x.StartsWith("We will proceed with our work upon receipt", StringComparison.Ordinal)),
                        InvoicingParagraph = paragraphs.First(x => x.StartsWith("We propose to invoice on a monthly basis.", StringComparison.Ordinal))
                    }
                },
                new()
                {
                    TemplateName = "Signature Page - Imported Master KW",
                    BlockType = ProposalBlockType.SignaturePage,
                    SignaturePage = new SignaturePageBlockContent
                    {
                        ClosingParagraph = paragraphs.First(x => x.StartsWith("We trust the foregoing proposal meets your expectations.", StringComparison.Ordinal)),
                        SignatoryStaffIds = new ObservableCollection<string>(new[] { GetStaffId(staffByName, "Kevin Wurmlinger"), GetStaffId(staffByName, "John Markulin") }.Where(x => !string.IsNullOrWhiteSpace(x))),
                        ClientCompanyName = "Unibuild Construction Management Ltd.",
                        IncludeRatesAppendix = true
                    }
                },
                new()
                {
                    TemplateName = "Rates Table - Imported Master KW",
                    BlockType = ProposalBlockType.RatesTable,
                    RatesTable = new RatesTableBlockContent
                    {
                        EffectiveDate = "May 1, 2025"
                    }
                }
            }
        };
    }

    private static IEnumerable<ProposalBlockTemplate> BuildProposalTemplates(FeeProposal proposal)
    {
        foreach (var block in proposal.Blocks.Where(x => x.BlockType != ProposalBlockType.Cover))
        {
            yield return new ProposalBlockTemplate
            {
                Name = block.TemplateName,
                Category = block.BlockType.ToString(),
                BlockType = block.BlockType,
                Content = block
            };
        }
    }

    private static BrochureProposal BuildBrochureProposal(
        PdfDocumentReader reader,
        IReadOnlyDictionary<string, ProposalStaffMember> staffByName,
        string brochurePdfPath)
    {
        var sectionSpecs = new[]
        {
            new SectionSpec("FEATURED LARGE MIXED-USE COMMERCIAL & RESIDENTIAL PROJECTS - CANADA", 10, 27),
            new SectionSpec("LOW-RISE WOOD-FRAME RESIDENTIAL PROJECTS", 28, 40),
            new SectionSpec("CROSS LAMINATED TIMBER (CLT) PROJECTS", 41, 42),
            new SectionSpec("FEATURED LARGE MIXED-USE COMMERCIAL & RESIDENTIAL PROJECTS - USA", 43, 54),
        };

        var blocks = new List<BrochureBlock>
        {
            new()
            {
                BlockType = BrochureBlockType.CompanyOverview,
                OverviewSections = new ObservableCollection<BrochureOverviewSection>(BuildOverviewSections(reader))
            },
            new()
            {
                BlockType = BrochureBlockType.Personnel,
                PersonnelHeading = "People",
                PersonnelBlurb = "Selected principals, associates, and senior technical staff featured in the 2025 KOR structural portfolio.",
                People = new ObservableCollection<BrochurePerson>(BuildBrochurePeople(staffByName))
            }
        };

        foreach (var spec in sectionSpecs)
        {
            blocks.Add(new BrochureBlock
            {
                BlockType = BrochureBlockType.Section,
                Section = BuildBrochureSection(reader, spec)
            });
        }

        blocks.Add(new BrochureBlock { BlockType = BrochureBlockType.Contact });

        return new BrochureProposal
        {
            Name = Path.GetFileNameWithoutExtension(brochurePdfPath),
            Content = new BrochureContent
            {
                TemplateName = "Corporate Portfolio",
                SkinId = "corporate-profile",
                LayoutTemplateId = "standard-portfolio",
                CoverTitle = "KOR Structural Corporate Portfolio 2025",
                CoverPhotoBytes = reader.GetPageImages(1).OrderByDescending(x => x.Length).FirstOrDefault() ?? Array.Empty<byte>(),
                Blocks = blocks,
                ContactConfig = new BrochureContactConfig
                {
                    OfficeAddress = "501-510 Burrard Street, Vancouver, BC, V6C 3A8",
                    Offices = new ObservableCollection<BrochureOfficeContact>
                    {
                        new() { Region = "Vancouver", Contact = "John Markulin, M.Eng., P.Eng., Struct.Eng., PE, SE", Phone = "(604) 685-9533", Email = "contact@korstructural.com", Hours = "9AM to 5PM (Monday to Friday)" },
                        new() { Region = "United States", Contact = "Jim DesRoches, BASc., P.Eng., PE", Phone = "(604) 999-7758", Email = "jdesroches@korstructural.com", Hours = "9AM to 5PM (Monday to Friday)" },
                        new() { Region = "Vancouver Island", Contact = "Rory Beirne, M.Eng., P.Eng., Struct.Eng.", Phone = "(778) 652-1895", Email = "rbeirne@korstructural.com", Hours = "9AM to 5PM (Monday to Friday)" },
                        new() { Region = "Okanagan", Contact = "Jeremy Atkinson, M.Sc., P.Eng., PE", Phone = "(778) 652-1897", Email = "jatkinson@korstructural.com", Hours = "9AM to 5PM (Monday to Friday)" },
                    }
                }
            }
        };
    }

    private static List<BrochurePerson> BuildBrochurePeople(IReadOnlyDictionary<string, ProposalStaffMember> staffByName)
    {
        var names = new[]
        {
            "John Markulin",
            "Jim DesRoches",
            "Jeremy Atkinson",
            "Rory Beirne",
            "Jason Stuart",
            "Kevin Wurmlinger",
            "Omar Alcazar Pastrana",
            "Conor Murtagh",
            "John Bryson",
        };

        return names
            .Where(staffByName.ContainsKey)
            .Select(name => staffByName[name])
            .Select(staff => new BrochurePerson
            {
                Name = staff.FullName,
                Credentials = staff.Credentials,
                Bio = staff.Bio,
                PhotoBytes = staff.PhotoBytes
            })
            .ToList();
    }

    private static List<BrochureOverviewSection> BuildOverviewSections(PdfDocumentReader reader)
    {
        var page3 = reader.GetPageText(3);
        var page4 = reader.GetPageText(4);
        var page55 = reader.GetPageText(55);
        return new List<BrochureOverviewSection>
        {
            new() { Heading = "Excellence in Structural Engineering", Body = ExtractTextBetween(page3, "EXCELLENCE IN STRUCTURAL ENGINEERING", "EXPERIENCE") },
            new() { Heading = "Experience", Body = ExtractTextBetween(page3, "EXPERIENCE", "SERVICES") },
            new() { Heading = "Services", Body = ExtractTextBetween(page3, "SERVICES", "501-510 Burrard Street") },
            new() { Heading = "Systems and Organizational Quality Management", Body = ExtractTextBetween(page4, "SYSTEMS AND ORGANIZATIONAL QUALITY MANAGEMENT", "TECHNOLOGY") },
            new() { Heading = "Technology", Body = ExtractTextBetween(page4, "TECHNOLOGY", "PEOPLE") },
            new() { Heading = "People", Body = ExtractTextBetween(page4, "PEOPLE", "501-510 Burrard Street") },
            new() { Heading = "Clients Include", Body = ExtractTextBetween(page55, "CLIENTS INCLUDE", "501-510 Burrard Street") },
        };
    }

    private static BrochureSection BuildBrochureSection(PdfDocumentReader reader, SectionSpec spec)
    {
        var projects = new List<BrochureProject>();
        string blurb = string.Empty;

        for (var page = spec.StartPage; page <= spec.EndPage; page++)
        {
            var lines = CleanPdfLines(reader.GetPageText(page));
            var titleIndexes = FindProjectTitleIndexes(lines);
            if (titleIndexes.Count == 0)
                continue;

            if (page == spec.StartPage)
            {
                var headingIndex = lines.FindIndex(x => string.Equals(x, spec.Heading, StringComparison.OrdinalIgnoreCase));
                if (headingIndex >= 0 && titleIndexes[0] > headingIndex)
                    blurb = string.Join(" ", lines.Skip(headingIndex + 1).Take(titleIndexes[0] - headingIndex - 1)).Trim();
            }

            var pageProjects = ParseProjectsFromPage(lines, titleIndexes);
            AssignPageImages(pageProjects, reader.GetPageImages(page));
            projects.AddRange(pageProjects.Select(x =>
            {
                x.SectionLabel = spec.Heading;
                return x;
            }));
        }

        return new BrochureSection
        {
            Heading = spec.Heading,
            Blurb = blurb,
            Projects = new ObservableCollection<BrochureProject>(projects)
        };
    }

    private static List<BrochureProject> ParseProjectsFromPage(List<string> lines, List<int> titleIndexes)
    {
        var projects = new List<BrochureProject>();
        for (var i = 0; i < titleIndexes.Count; i++)
        {
            var start = titleIndexes[i];
            var end = i + 1 < titleIndexes.Count ? titleIndexes[i + 1] : lines.Count;
            var segment = lines.Skip(start).Take(end - start).ToList();
            if (segment.Count == 0)
                continue;

            var project = new BrochureProject { ProjectName = segment[0] };
            var clientIndex = FindClientMarkerIndex(segment);
            var architectIndex = FindArchitectMarkerIndex(segment);
            var descriptionEnd = new[] { clientIndex, architectIndex }.Where(x => x > 0).DefaultIfEmpty(segment.Count).Min();
            project.ProjectDescription = string.Join(" ", segment.Skip(1).Take(descriptionEnd - 1)).Trim();

            if (clientIndex >= 0 && architectIndex >= 0 && clientIndex < architectIndex)
            {
                project.Client = ReadField(segment, clientIndex, architectIndex);
                project.Architect = ReadField(segment, architectIndex, segment.Count);
            }
            else if (clientIndex >= 0)
            {
                var value = ReadField(segment, clientIndex, segment.Count);
                project.Client = value;
                if (IsCombinedClientArchitectMarker(segment[clientIndex]))
                    project.Architect = value;
            }
            else if (architectIndex >= 0)
            {
                project.Architect = ReadField(segment, architectIndex, segment.Count);
            }

            projects.Add(project);
        }
        return projects;
    }

    private static void AssignPageImages(List<BrochureProject> projects, List<byte[]> images)
    {
        if (projects.Count == 0 || images.Count == 0)
            return;

        if (projects.Count == 1)
        {
            projects[0].Photos.AddRange(images.Select(x => new BrochurePhoto { ImageBytes = x }));
            return;
        }

        var chunkSize = (int)Math.Ceiling(images.Count / (double)projects.Count);
        for (var i = 0; i < projects.Count; i++)
        {
            var assigned = images.Skip(i * chunkSize).Take(chunkSize).ToList();
            if (assigned.Count == 0 && i < images.Count)
                assigned.Add(images[i]);
            projects[i].Photos.AddRange(assigned.Select(x => new BrochurePhoto { ImageBytes = x }));
        }
    }

    private static IReadOnlyList<string> ReadDocxParagraphs(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidOperationException("DOCX document.xml not found.");
        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        return doc.Descendants(w + "p")
            .Select(p => string.Concat(p.Descendants(w + "t").Select(t => (string?)t ?? string.Empty)).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static List<string> Slice(IReadOnlyList<string> paragraphs, string startMarker, string endMarker)
    {
        var start = paragraphs.ToList().FindIndex(x => string.Equals(x, startMarker, StringComparison.Ordinal));
        if (start < 0)
            return new List<string>();
        var end = paragraphs.ToList().FindIndex(start + 1, x => string.Equals(x, endMarker, StringComparison.Ordinal));
        if (end < 0)
            end = paragraphs.Count;
        return paragraphs.Skip(start).Take(end - start).ToList();
    }

    private static string ParseStaffName(string line)
    {
        var commaIndex = line.IndexOf(',');
        return commaIndex > 0 ? line[..commaIndex].Trim() : line.Trim();
    }

    private static string GetStaffId(IReadOnlyDictionary<string, ProposalStaffMember> staffByName, string name) =>
        staffByName.TryGetValue(name, out var staff) ? staff.Id : string.Empty;

    private static string Prefer(string current, string incoming) =>
        string.IsNullOrWhiteSpace(current) ? incoming : current;

    private static string NormalizePdfField(string value) =>
        Regex.Replace(value ?? string.Empty, "\\s+", " ").Trim();

    private static string ExtractTextBetween(string text, string start, string end)
    {
        var startIndex = text.IndexOf(start, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
            return string.Empty;
        startIndex += start.Length;
        var endIndex = text.IndexOf(end, startIndex, StringComparison.OrdinalIgnoreCase);
        if (endIndex < 0)
            endIndex = text.Length;
        return Regex.Replace(text[startIndex..endIndex], "\\s+", " ").Trim();
    }

    private static List<string> CleanPdfLines(string text) =>
        text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Regex.Replace(x, "\\s+", " ").Trim())
            .Where(x =>
                !string.IsNullOrWhiteSpace(x) &&
                !string.Equals(x, "KOR Structural Portfolio - 2025", StringComparison.OrdinalIgnoreCase) &&
                !x.EndsWith("of 57", StringComparison.OrdinalIgnoreCase) &&
                !x.StartsWith("501-510 Burrard Street", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static List<int> FindProjectTitleIndexes(List<string> lines)
    {
        var indexes = new List<int>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (!IsLikelyProjectTitle(line))
                continue;
            var nextMarkerIndex = lines.FindIndex(i + 1, x =>
                IsClientMarker(x) || IsArchitectMarker(x));
            if (nextMarkerIndex > i)
                indexes.Add(i);
        }
        return indexes;
    }

    private static bool IsLikelyProjectTitle(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;
        if (!line.Contains(','))
            return false;
        if (IsClientMarker(line) || IsArchitectMarker(line))
            return false;
        if (line.StartsWith("Located", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("This ", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("The following", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("We have", StringComparison.OrdinalIgnoreCase))
            return false;
        if (line.Contains(':'))
            return false;
        if (line.Length < 15 || line.Length > 140)
            return false;

        var upperLetters = line.Count(char.IsUpper);
        var letters = line.Count(char.IsLetter);
        if (letters == 0)
            return false;

        return upperLetters >= 8 || (upperLetters / (double)letters) >= 0.28;
    }

    private static int FindClientMarkerIndex(List<string> segment) =>
        segment.FindIndex(IsClientMarker);

    private static int FindArchitectMarkerIndex(List<string> segment) =>
        segment.FindIndex(IsArchitectMarker);

    private static bool IsClientMarker(string line) =>
        line.StartsWith("Client", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Developer", StringComparison.OrdinalIgnoreCase);

    private static bool IsArchitectMarker(string line) =>
        line.StartsWith("Architect", StringComparison.OrdinalIgnoreCase) ||
        IsCombinedClientArchitectMarker(line);

    private static bool IsCombinedClientArchitectMarker(string line) =>
        line.StartsWith("Client/Architect", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Client & Architect", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Client And Architect", StringComparison.OrdinalIgnoreCase);

    private static string ReadField(List<string> segment, int markerIndex, int endExclusive)
    {
        if (markerIndex < 0 || markerIndex >= segment.Count)
            return string.Empty;

        var collected = new List<string>();
        var markerLine = segment[markerIndex];
        var colonIndex = markerLine.IndexOf(':');
        var inline = colonIndex >= 0 ? markerLine[(colonIndex + 1)..].Trim() : string.Empty;
        if (!string.IsNullOrWhiteSpace(inline) && !inline.Equals("Client", StringComparison.OrdinalIgnoreCase) && !inline.Equals("Architect", StringComparison.OrdinalIgnoreCase))
            collected.Add(inline);

        for (var i = markerIndex + 1; i < endExclusive; i++)
        {
            var line = segment[i];
            if (IsClientMarker(line) || IsArchitectMarker(line))
                break;
            collected.Add(line);
        }

        return string.Join(" ", collected.Distinct(StringComparer.OrdinalIgnoreCase)).Trim();
    }

    private sealed record SectionSpec(string Heading, int StartPage, int EndPage);
}

internal sealed class PdfDocumentReader : IDisposable
{
    private readonly string _pdfPath;
    private readonly string _pdftotextPath;
    private readonly string _pdfimagesPath;
    private readonly string _tempRoot;
    private readonly Dictionary<int, string> _pageText = new();
    private readonly Dictionary<int, List<byte[]>> _pageImages = new();

    public PdfDocumentReader(string pdfPath, string popplerBinPath)
    {
        _pdfPath = pdfPath;
        _pdftotextPath = Path.Combine(popplerBinPath, "pdftotext.exe");
        _pdfimagesPath = Path.Combine(popplerBinPath, "pdfimages.exe");
        _tempRoot = Path.Combine(Path.GetTempPath(), "KorOperationsImport", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public string GetPageText(int page)
    {
        if (_pageText.TryGetValue(page, out var cached))
            return cached;

        var output = RunTool(_pdftotextPath, $"-layout -f {page} -l {page} \"{_pdfPath}\" -");
        _pageText[page] = output;
        return output;
    }

    public List<byte[]> GetPageImages(int page)
    {
        if (_pageImages.TryGetValue(page, out var cached))
            return cached;

        var pageDir = Path.Combine(_tempRoot, $"page-{page:D2}");
        Directory.CreateDirectory(pageDir);
        var prefix = Path.Combine(pageDir, "img");
        RunTool(_pdfimagesPath, $"-f {page} -l {page} -all \"{_pdfPath}\" \"{prefix}\"");
        var images = Directory.GetFiles(pageDir)
            .Where(x => x.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllBytes)
            .Where(x => x.Length > 8 * 1024)
            .ToList();
        _pageImages[page] = images;
        return images;
    }

    private static string RunTool(string exePath, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {exePath}.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(exePath)} failed: {stderr}");
        return stdout;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}

internal sealed class ImportOptions
{
    public string AppConfigPath { get; private set; } = @"C:\VIsual Studio Projects\Operations\Kor.Operations.App\App.config";
    public string FeeDocxPath { get; private set; } = @"C:\VIsual Studio Projects\Operations\Kor.Operations.App\FeeProposal\Master Fee Proposal Template KW.docx";
    public string BrochurePdfPath { get; private set; } = @"C:\VIsual Studio Projects\Operations\Kor.Operations.App\Brochures\Kor_Structural_Corporate_Portfolio_2025-03-17.pdf";
    public string PopplerBinPath { get; private set; } = @"C:\poppler\Library\bin";

    public static ImportOptions Parse(string[] args)
    {
        var options = new ImportOptions();
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length)
                throw new ArgumentException($"Missing value for argument '{args[i]}'.");

            switch (args[i])
            {
                case "--app-config":
                    options.AppConfigPath = args[i + 1];
                    break;
                case "--fee-docx":
                    options.FeeDocxPath = args[i + 1];
                    break;
                case "--brochure-pdf":
                    options.BrochurePdfPath = args[i + 1];
                    break;
                case "--poppler-bin":
                    options.PopplerBinPath = args[i + 1];
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }
        return options;
    }

    public void Validate()
    {
        if (!File.Exists(AppConfigPath))
            throw new FileNotFoundException("App.config not found.", AppConfigPath);
        if (!File.Exists(FeeDocxPath))
            throw new FileNotFoundException("Fee proposal DOCX not found.", FeeDocxPath);
        if (!File.Exists(BrochurePdfPath))
            throw new FileNotFoundException("Brochure PDF not found.", BrochurePdfPath);
        if (!File.Exists(Path.Combine(PopplerBinPath, "pdftotext.exe")))
            throw new FileNotFoundException("pdftotext.exe not found.", Path.Combine(PopplerBinPath, "pdftotext.exe"));
        if (!File.Exists(Path.Combine(PopplerBinPath, "pdfimages.exe")))
            throw new FileNotFoundException("pdfimages.exe not found.", Path.Combine(PopplerBinPath, "pdfimages.exe"));
    }
}
