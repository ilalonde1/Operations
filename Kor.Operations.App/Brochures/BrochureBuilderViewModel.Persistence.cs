#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Rendering.Brochure;
using Kor.Operations.Rendering.Brochure.Skins;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.Brochures
{
    public sealed partial class BrochureBuilderViewModel
    {
        private const string OriginalSeedProposalPath = @"Brochures\SeedData\Original.seed.json";
        private const string ExecutiveMinimalSeedId = "98a28b7220614e9cb3a15c15f0ac5c19";
        private const string BoldPortfolioSeedId = "31711db5f0d54c1bb4ac05380d7b8546";
        private const string IslamMarch2026SeedId = "c7f3a192e84b4d5faa0d716b23e9c041";
        private static readonly JsonSerializerOptions SeedProposalJsonOptions = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        public ICommand SaveProposalCommand { get; private set; } = null!;
        public ICommand SaveProposalAsCommand { get; private set; } = null!;
        public ICommand LoadProposalCommand { get; private set; } = null!;
        public ICommand NewProposalCommand { get; private set; } = null!;
        public ICommand SaveContactCommand { get; private set; } = null!;
        public ICommand AddOfficeCommand { get; private set; } = null!;
        public ICommand RemoveOfficeCommand { get; private set; } = null!;

        [MemberNotNull(
            nameof(SaveProposalCommand), nameof(SaveProposalAsCommand), nameof(LoadProposalCommand),
            nameof(NewProposalCommand), nameof(SaveContactCommand), nameof(AddOfficeCommand),
            nameof(RemoveOfficeCommand))]
        private void InitPersistenceCommands()
        {
            SaveProposalCommand = new RelayCommand(ExecSaveProposal);
            SaveProposalAsCommand = new RelayCommand(ExecSaveProposalAs);
            LoadProposalCommand = new RelayCommand(ExecLoadProposal);
            NewProposalCommand = new RelayCommand(ExecNewProposal);
            SaveContactCommand = new RelayCommand(_ =>
            {
                _contactStore.Save(_contactConfig);
                MessageBox.Show("Contact info saved.", "Brochure Builder — Contact Info Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            });
            AddOfficeCommand = new RelayCommand(_ =>
            {
                _contactConfig.Offices.Add(new Kor.Operations.Core.Models.Brochure.BrochureOfficeContact());
                SetDirty();
            });
            RemoveOfficeCommand = new RelayCommand(param =>
            {
                if (param is Kor.Operations.Core.Models.Brochure.BrochureOfficeContact office)
                {
                    _contactConfig.Offices.Remove(office);
                    SetDirty();
                }
            });
        }

        // async-void OK: invoked via RelayCommand(ExecSaveProposal); ICommand.Execute contract is void.
        private async void ExecSaveProposal(object? _)
        {
            if (string.IsNullOrEmpty(ProposalName))
            {
                var nameDialog = new BrochureProposalNameDialog(
                    !string.IsNullOrWhiteSpace(Cover.CoverTitle) ? Cover.CoverTitle : ProposalName)
                {
                    Owner = GetOwnerWindow()
                };
                if (nameDialog.ShowDialog() != true) return;
                ProposalName = nameDialog.ProposalName;
            }

            _proposalId ??= Guid.NewGuid().ToString("N");
            await _proposalStore.SaveAsync(new BrochureProposal
            {
                Id = _proposalId,
                Name = ProposalName,
                Content = BuildBrochureContent()
            });

            MessageBox.Show(
                $"\"{ProposalName}\" saved.",
                "Brochure Builder — Proposal Saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            ClearDirty();
        }

        // async-void OK: invoked via RelayCommand(ExecSaveProposalAs); ICommand.Execute contract is void.
        private async void ExecSaveProposalAs(object? _)
        {
            var nameDialog = new BrochureProposalNameDialog(ProposalName) { Owner = GetOwnerWindow() };
            if (nameDialog.ShowDialog() != true) return;

            ProposalName = nameDialog.ProposalName;
            _proposalId = Guid.NewGuid().ToString("N");

            await _proposalStore.SaveAsync(new BrochureProposal
            {
                Id = _proposalId,
                Name = ProposalName,
                Content = BuildBrochureContent()
            });

            MessageBox.Show(
                $"\"{ProposalName}\" saved.",
                "Brochure Builder — Proposal Saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            ClearDirty();
        }

        private void ExecLoadProposal(object? _)
        {
            if (_isDirty)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Discard and open another proposal?",
                    "Brochure Builder — Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
            }
            var picker = new BrochureProposalPickerWindow(_proposalStore) { Owner = GetOwnerWindow() };
            if (picker.ShowDialog() != true || picker.SelectedProposal is null) return;
            LoadFromProposal(picker.SelectedProposal, picker.IsClone);
        }

        private void ExecNewProposal(object? _)
        {
            if (_isDirty)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Discard and start a new brochure?",
                    "Brochure Builder — Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
            }

            ClearProjectForm();
            Person.ClearForm();
            Overview.ClearSectionForm();
            Overview.ClearOverviewForm();
            Blocks.Clear();
            _selectedSection = null;
            _selectedSectionBlock = null;
            SelectedBlockIndex = -1;
            SelectedProjectIndex = -1;
            SelectedOverviewIndex = -1;
            IsEditingOverview = false;
            PreviewPages.Clear();
            Cover.CoverTitle = string.Empty;
            Cover.CoverPhotoPath = string.Empty;
            Cover.CoverPhotoBytes = Array.Empty<byte>();
            _proposalId = null;
            ProposalName = string.Empty;
            CurrentStep = 1;
            ClearDirty();
            OnPropertyChanged(nameof(SelectedSection));
            OnPropertyChanged(nameof(CanAddProjectToSection));
        }

        private void EnsureSeedProposals()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var existingProposals = await _proposalStore.LoadAllAsync().ConfigureAwait(false);
                    var existingIds = existingProposals.Select(static p => p.Id).ToHashSet();

                    var baseSeed = LoadSeedProposal();
                    if (baseSeed is not null)
                    {
                        if (!existingIds.Contains(baseSeed.Id))
                            await _proposalStore.SaveAsync(baseSeed).ConfigureAwait(false);

                        var execVariant = CreateSeedVariant(baseSeed, ExecutiveMinimalSeedId, "Original - Executive Minimal", "Executive Minimal");
                        if (!existingIds.Contains(execVariant.Id))
                            await _proposalStore.SaveAsync(execVariant).ConfigureAwait(false);

                        var boldVariant = CreateSeedVariant(baseSeed, BoldPortfolioSeedId, "Original - Bold Portfolio", "Bold Portfolio");
                        if (!existingIds.Contains(boldVariant.Id))
                            await _proposalStore.SaveAsync(boldVariant).ConfigureAwait(false);
                    }

                    var islamSeedCutoff = new DateTime(2026, 3, 30);
                    var existingIslamProposal = existingProposals.FirstOrDefault(static p => p.Id == IslamMarch2026SeedId);
                    if (existingIslamProposal is null || existingIslamProposal.ModifiedAt < islamSeedCutoff)
                        await _proposalStore.SaveAsync(BuildIslamSeedProposal()).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Seed proposal initialization failed");
                }
            });
        }

        private static BrochureProposal? LoadSeedProposal()
        {
            var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, OriginalSeedProposalPath);
            if (!System.IO.File.Exists(path)) return null;
            return JsonSerializer.Deserialize<BrochureProposal>(
                System.IO.File.ReadAllText(path), SeedProposalJsonOptions);
        }

        private static byte[]? TryLoadSeedAsset(string relativePath)
        {
            var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null;
        }

        private static BrochureProposal BuildIslamSeedProposal()
        {
            // Personnel headshots
            var photoMarkulin  = TryLoadSeedAsset(@"Brochures\headshot-markulin.jpg");
            var photoDesroches = TryLoadSeedAsset(@"Brochures\headshot-desroches.jpg");
            var photoBeirne    = TryLoadSeedAsset(@"Brochures\headshot-beirne.jpg");
            var photoAlcazar   = TryLoadSeedAsset(@"Brochures\headshot-alcazar.jpg");
            var photoMurtagh   = TryLoadSeedAsset(@"Brochures\headshot-murtagh.jpg");
            var photoShabana   = TryLoadSeedAsset(@"Brochures\headshot-shabana.jpg");

            // Project photos
            var imgParadox           = TryLoadSeedAsset(@"Brochures\project-paradox.jpg");
            var imgStationSquare     = TryLoadSeedAsset(@"Brochures\project-station-square.jpg");
            var imgHalifaxWillingdon = TryLoadSeedAsset(@"Brochures\project-halifax-willingdon.jpg");
            var imgOneBurrard        = TryLoadSeedAsset(@"Brochures\project-one-burrard.jpg");
            var img1011Union         = TryLoadSeedAsset(@"Brochures\project-1011-union.jpg");
            var imgWestPender        = TryLoadSeedAsset(@"Brochures\project-west-pender.jpg");
            var img564Beatty         = TryLoadSeedAsset(@"Brochures\project-564-beatty.jpg");
            var imgTheRoyal          = TryLoadSeedAsset(@"Brochures\project-the-royal.jpg");
            var imgPocoRec           = TryLoadSeedAsset(@"Brochures\project-poco-rec.jpg");
            var imgHouss             = TryLoadSeedAsset(@"Brochures\project-houss.jpg");
            var imgArris             = TryLoadSeedAsset(@"Brochures\project-arris.png");
            var imgGrandeCondos      = TryLoadSeedAsset(@"Brochures\project-grande-condos.jpg");
            var imgRichardsDrake     = TryLoadSeedAsset(@"Brochures\project-richards-drake.jpg");
            var imgParksideWaterfront= TryLoadSeedAsset(@"Brochures\project-parkside-waterfront.jpg");
            var imgParamountPlace    = TryLoadSeedAsset(@"Brochures\project-paramount-place.jpg");
            var imgLangleyEvents     = TryLoadSeedAsset(@"Brochures\project-langley-events.jpg");
            var imgSoloDistrict      = TryLoadSeedAsset(@"Brochures\project-solo-district.jpg");
            var imgMosaicPlace       = TryLoadSeedAsset(@"Brochures\project-mosaic-place.jpg");
            var imgHighline          = TryLoadSeedAsset(@"Brochures\project-highline.jpg");
            var imgRockridge         = TryLoadSeedAsset(@"Brochures\project-rockridge.jpg");
            var imgRogersArena       = TryLoadSeedAsset(@"Brochures\project-rogers-arena.jpg");
            var imgChilliwack        = TryLoadSeedAsset(@"Brochures\project-chilliwack.jpg");
            var imgSovereign         = TryLoadSeedAsset(@"Brochures\project-sovereign.png");
            var imgOfficesBurrard    = TryLoadSeedAsset(@"Brochures\project-offices-burrard.png");
            var imgParkPoint         = TryLoadSeedAsset(@"Brochures\project-park-point.jpg");
            var imgLougheed          = TryLoadSeedAsset(@"Brochures\project-lougheed.png");
            var imgKingsCrossing     = TryLoadSeedAsset(@"Brochures\project-kings-crossing.jpg");
            var img567Clarke         = TryLoadSeedAsset(@"Brochures\project-567-clarke.jpg");
            var imgLindley           = TryLoadSeedAsset(@"Brochures\project-lindley.jpg");
            var img1401UnionSimone   = TryLoadSeedAsset(@"Brochures\project-1401-union-simone.png");
            var imgTheStandard       = TryLoadSeedAsset(@"Brochures\project-the-standard.jpg");
            var imgSouthYards        = TryLoadSeedAsset(@"Brochures\project-south-yards.jpg");
            var imgCentralQuebec     = TryLoadSeedAsset(@"Brochures\project-central-quebec.jpg");
            var imgEtoileGold        = TryLoadSeedAsset(@"Brochures\project-etoile-gold.png");
            var imgJinju             = TryLoadSeedAsset(@"Brochures\project-jinju.png");
            var imgGrandKingGeorge   = TryLoadSeedAsset(@"Brochures\project-grand-king-george.jpg");
            var imgSmithFarrow       = TryLoadSeedAsset(@"Brochures\project-smith-farrow.jpg");
            var imgSoco              = TryLoadSeedAsset(@"Brochures\project-soco.jpg");
            var imgMillenniumSports  = TryLoadSeedAsset(@"Brochures\project-millennium-sports.jpg");
            var imgNeeluBachra       = TryLoadSeedAsset(@"Brochures\project-neelu-bachra.png");
            var imgRadiusCondos      = TryLoadSeedAsset(@"Brochures\project-radius-condos.jpg");
            var imgKensington        = TryLoadSeedAsset(@"Brochures\project-kensington.jpg");
            var imgAzurHotel         = TryLoadSeedAsset(@"Brochures\project-azur-hotel.jpg");

            static Kor.Operations.Core.Models.Brochure.BrochureProject Proj(
                string name, string location, string client, string architect,
                string description, byte[]? photo) => new()
            {
                ProjectName        = name,
                SectionLabel       = location,
                Client             = client,
                Architect          = architect,
                ProjectDescription = description,
                Photos             = photo is { Length: > 0 }
                    ? new List<Kor.Operations.Core.Models.Brochure.BrochurePhoto>
                        { new() { ImageBytes = photo } }
                    : new List<Kor.Operations.Core.Models.Brochure.BrochurePhoto>()
            };

            return new()
            {
                Id = IslamMarch2026SeedId,
                Name = "Original - Islam March 2026",
                CreatedAt = new DateTime(2026, 3, 30, 0, 0, 0, DateTimeKind.Utc),
                ModifiedAt = new DateTime(2026, 3, 30, 0, 0, 0, DateTimeKind.Utc),
                Content = new Kor.Operations.Core.Models.Brochure.BrochureContent
                {
                    TemplateName = "Islam March 2026",
                    SkinId = "islam-march-2026",
                    LayoutTemplateId = "islam-march-2026",
                    CoverTitle = "Corporate Portfolio",
                    CoverPhotoOpacity = 0.4f,
                    ContactConfig = new Kor.Operations.Core.Models.Brochure.BrochureContactConfig
                    {
                        OfficeAddress = "501 - 510 Burrard Street, Vancouver, BC V6C 3A8",
                        CoverContactLines = new List<string>
                        {
                            "Suite 501 - 510 Burrard Street",
                            "Vancouver, BC, V6C3A8",
                            "Office: +1 604 685 9533",
                            "contact@korstructural.com",
                            "www.korstructural.com"
                        },
                        Offices = new System.Collections.ObjectModel.ObservableCollection<Kor.Operations.Core.Models.Brochure.BrochureOfficeContact>
                        {
                            new() { Region = "Vancouver",        Contact = "John Markulin, M.Eng., P.Eng., Struct.Eng., PE, SE", Phone = "(604) 685-9533",  Email = "contact@korstructural.com",   Hours = "9AM to 5PM (Monday to Friday)" },
                            new() { Region = "United States",    Contact = "Jim DesRoches, BASc., P.Eng., PE",                    Phone = "(604) 999-7758",  Email = "jdesroches@korstructural.com", Hours = "9AM to 5PM (Monday to Friday)" },
                            new() { Region = "Vancouver Island", Contact = "Rory Beirne, M.Eng., P.Eng., Struct.Eng.",            Phone = "(778) 652-1895",  Email = "rbeirne@korstructural.com",   Hours = "9AM to 5PM (Monday to Friday)" },
                            new() { Region = "Okanagan",         Contact = "Conor Murtagh, BASc., P.Eng.",                        Phone = "(778) 652-1887",  Email = "cmurtagh@korstructural.com",  Hours = "9AM to 5PM (Monday to Friday)" },
                            new() { Region = "Edmonton",         Contact = "Islam Shabana, Ph.D., P.Eng.",                        Phone = "(825) 459-8092",  Email = "ishabana@korstructural.com",  Hours = "9AM to 5PM (Monday to Friday)" }
                        }
                    },
                    Blocks = new List<Kor.Operations.Core.Models.Brochure.BrochureBlock>
                    {
                        new()
                        {
                            BlockType = Kor.Operations.Core.Models.Brochure.BrochureBlockType.CompanyOverview,
                            PageBreakAfterOverviewIndex = new List<int> { 4 },
                            OverviewSections = new System.Collections.ObjectModel.ObservableCollection<Kor.Operations.Core.Models.Brochure.BrochureOverviewSection>
                            {
                                new() { Heading = "Excellence in Structural Engineering", Body = "Headquartered in Vancouver, B.C., with satellite offices in Kelowna, Nanaimo, and Edmonton, Kor Structural (\u201cKOR\u201d) provides structural engineering, consultation, design, and inspection services throughout B.C. and across North America. We deliver efficient, creative, and buildable solutions for projects of all sizes and types, including residential, commercial, institutional, and light industrial. Our expertise spans all forms of construction, including concrete, structural steel, wood-frame, mass timber, and masonry.\n\nFounded over 25 years ago by three seasoned professional engineers, KOR has grown into a team of 40+ professionals dedicated to client service, quality, and innovative building solutions. While operating as a unified, distributed team, we maintain a strong local presence: our Okanagan office includes three Partners, while a fourth Partner oversees Vancouver Island projects from Nanaimo, along with a senior partner-led team dedicated to U.S. projects. At KOR, we believe every development begins with a strong foundation, combining first-class structural engineering expertise, cutting-edge technological innovation, and an unwavering commitment to your design team." },
                                new() { Heading = "Experience", Body = "KOR\u2019s portfolio spans a wide range of building types, including residential, mixed-use, commercial, office, industrial, institutional, public venues, and renovations. We work closely with clients and industry professionals to integrate architectural requirements into practical, efficient structural solutions. Committed to excellence in customer service, we deliver high-quality projects that meet schedules while realizing client visions.\n\nOver the past 25 years, KOR has designed more than 2,000 projects across Western Canada and the Western U.S., with over 250 high-rise towers among them. These include 24 in California, 3 in Seattle, 3 in Toronto, and 28 in Alberta. In addition, our dedicated team specializing in wood-frame and mass timber construction has successfully delivered over 200 projects of this type, reflecting our versatility and expertise across diverse construction methods." },
                                new() { Heading = "Services", Body = "Structured Engineering is a purposeful marriage of creative design with efficient solutions, rigorous discipline and applied engineering principles and Building Codes. Structured Engineering means structured systems. At KOR, integrated engineering, drafting, and construction teams embrace agile workflow processes. Our efficient design solutions are creative, compliant, and economical.\n\nKOR is constantly striving to improve efficiency and add value to clients. Our aim is to do great work and be easy to work with. Our core competencies include:\n\n\u2022 Structural engineering for a wide range of building materials, including reinforced concrete, structural steel, wood-frame and heavy timber construction, tilt-up construction, and masonry.\n\u2022 Design of high-rise reinforced concrete structures.\n\u2022 Performance-based structural analysis and design.\n\u2022 Mass timber design with a dedicated wood-frame department.\n\u2022 Structural plan check and peer reviews.\n\u2022 Expert opinions and structural assessments." },
                                new() { Heading = "Systems and Organizational Quality Management", Body = "As part of our commitment to delivering excellent service to our clients, we have developed and implemented gold-standard operating systems with clear policies and procedures that drive efficiency across all our projects. Our rigorous quality control practices align with the professional practice requirements set out in the Engineers and Geoscientists BC Quality Management Guidelines and include an Independent Engineering Check of all designs by a professional engineer who was not involved in any aspect of the original design.\n\nAt KOR, we adopt:\n\u2022 An \u2018Organization Quality Management Policy\u2019 structured from the EGBC Quality Management and Professional Practice Guidelines.\n\u2022 Standardized policies and procedures, and well defined and regularly monitored project workflow with rigorous quality control at all stages of Design Development and Construction Review." },
                                new() { Heading = "Technology", Body = "We look ahead to ensure we are prepared to meet tomorrow\u2019s challenges. Technology is a critical enabler, and we leverage both established and leading-edge industry software, including advanced applications of artificial intelligence to support design optimization, coordination, and quality control. We use both AutoCAD and Revit/BIM, among others, to develop our construction documents and coordinate with design teams. To our clients, that means faster drawing development, improved accuracy, and real-time communication and collaboration.\n\nWe utilize state-of-the-art engineering design and drafting tools to develop and document our structural designs, including:\n\u2022 3D Seismic Analysis tools (e.g., ETABS and PERFORM3D Non-Linear Time History).\n\u2022 Finite Element Slab Design tools (e.g., RAM Concept and SAFE).\n\u2022 Space Frame structural analysis tools (e.g., STAAD Pro, RAM Steel, and SAP).\n\u2022 Structure Point Package (e.g., spCol, etc.).\n\u2022 3D & 2D Drafting and Building information Modelling tools (e.g., REVIT, DYNAMO, BIM 360, and AutoCAD).\n\u2022 Various in-house, custom-developed programs.\n\nFurther, we have a Dedicated IT Department with Internal Systems Manager focused on consistency, efficiency, and continual improvement of our systems and methods." },
                                new() { Heading = "People", Body = "At KOR, we don\u2019t aim to the biggest in our field \u2013 we aim to be the best by attracting and retaining first-class talent and being a center of excellence for structural engineering.\n\nWe are a dynamic team of highly experienced professionals and skilled young talent. With 40-plus dedicated professionals on staff, KOR has expertise and experience in all typical building materials, as well as specialists available in a variety of niche fields such as Performance-based design and Peer Reviews." },
                                new() { Heading = "Awards", Body = "KOR has received several industry recognitions over the years, reflecting its commitment to excellence in structural engineering. Among the most notable is the 2024 ACI San Diego Chapter Award for Best Residential Structure, awarded to the Simone Little Italy project\u2014a 36-story high-rise in San Diego\u2019s Little Italy neighborhood. KOR played a key role in delivering this landmark project, overcoming complex challenges such as designing a raft foundation over a major sewer interceptor and implementing a performance-based seismic design that enabled architectural flexibility.\n\nAnother significant recognition is the Multi-Family Residential Award by the American Concrete Institute (ACI) for The Grande in San Diego, a landmark twin-tower project in Downtown San Diego near the bay. Each 40-story, 400 ft. tall tower features a distinctive triangular shape, reinforced concrete construction with post-tensioning, and three levels of underground parking. The project presented unique engineering challenges, including poor soil conditions, high groundwater, and intense seismic considerations." }
                            }
                        },
                        new()
                        {
                            BlockType = Kor.Operations.Core.Models.Brochure.BrochureBlockType.Personnel,
                            PersonnelHeading = "Our Leaders",
                            PersonnelBlurb = "We are a dynamic team of experienced professionals and emerging talent, combining depth of expertise with fresh perspectives. Our growing team brings capabilities across multiple sectors, including engineering, drafting, construction oversight, IT, and operations, with specialized expertise in areas such as earthquake engineering.\nAs we continue to expand, our leadership remains committed to guiding the firm in alignment with our core purpose: delivering the highest quality designs that serve and strengthen the communities we build in.",
                            People = new System.Collections.ObjectModel.ObservableCollection<Kor.Operations.Core.Models.Brochure.BrochurePerson>
                            {
                                new() { Name = "John Markulin",         Credentials = "M.Eng., P.Eng., Struct.Eng., PE, SE",            PhotoBytes = photoMarkulin  ?? Array.Empty<byte>(), Bio = "As Senior Structural Engineer and Managing Principal at KOR, John leads the delivery of innovative structural engineering solutions across some of the firm\u2019s most prominent projects. He is an accomplished engineer with extensive experience in the design of reinforced concrete, precast concrete, cast-in-place and post-tensioned systems, tilt-up construction, structural steel, load-bearing steel stud, wood-frame, and masonry structures. His expertise also spans floor systems, vertical load-resisting systems, and seismic and lateral design.\nWith project experience ranging from San Diego to Arizona to Whistler, John is known for developing project-specific, cost-effective solutions that meet demanding architectural and economic requirements. His leadership has contributed to the successful delivery of a diverse portfolio, including tall towers, mixed-use residential and commercial developments, light industrial facilities, and institutional/public buildings such as ice rinks, sports complexes, healthcare facilities, hotels, office buildings, and LEED-certified projects." },
                                new() { Name = "Jim DesRoches",         Credentials = "BASc., P.Eng., PE",                               PhotoBytes = photoDesroches ?? Array.Empty<byte>(), Bio = "As a Senior Structural Engineer and Principal at KOR, Jim manages the firm\u2019s U.S. projects. Since 1988, he has designed a wide range of developments across Canada and the United States, including over 50 high-rise projects throughout British Columbia, Alberta, Ontario, Washington, Oregon, Nevada, and California.\n\nJim is recognized for his creative structural solutions and strong client focus, approaching every project with a collaborative mindset and a commitment to exceptional service. With more than three decades of experience in quality control management, he leads the development of KOR\u2019s Operational Quality Management Policy and serves as Head of the Systems Department." },
                                new() { Name = "Rory Beirne",           Credentials = "M.Eng., P.Eng., Struct.Eng., C.Eng., MIStructE",  PhotoBytes = photoBeirne    ?? Array.Empty<byte>(), Bio = "Rory is a Senior Structural Engineer and Principal at KOR, bringing over two decades of experience in the construction industry. He leads our Vancouver Island office in Nanaimo, B.C.\n\nWith a unique background as both a Geotechnical and Structural Engineer, Rory possesses a comprehensive skill set that allows him to guide projects from their foundations to innovative structural solutions. His clients value his practical understanding of construction and trust that their projects are managed with care, expertise, and a focus on delivering real value." },
                                new() { Name = "Omar Alcazar Pastrana", Credentials = "M.Sc., P.Eng.",                                   PhotoBytes = photoAlcazar   ?? Array.Empty<byte>(), Bio = "Omar is a Structural Engineer and Associate Principal at KOR, bringing over 12 years of experience in the design and analysis of reinforced concrete buildings. He is licensed to practice in both British Columbia and Mexico.\n\nSpecializing in seismic design, Omar has developed deep expertise in delivering cost-effective, resilient structural solutions that meet rigorous safety standards and building code requirements. Throughout his career, he has successfully led and contributed to the design, analysis, and execution of complex projects across a wide range of building types, from residential developments to commercial high-rise structures.\n\nOmar is highly experienced in the application of advanced seismic design principles, ensuring structures perform efficiently under seismic and wind loading while maintaining structural integrity. In addition to his technical strengths, he brings a comprehensive understanding of construction practices, enabling him to bridge the gap between design and implementation and support efficient project delivery.\n\nHe values strong collaboration among consultants, clients, and trades, and is committed to delivering practical, sustainable, and cost-effective solutions." },
                                new() { Name = "Conor Murtagh",         Credentials = "BASc., P.Eng.",                                   PhotoBytes = photoMurtagh   ?? Array.Empty<byte>(), Bio = "Conor is a Senior Structural Engineer and Associate Principal at KOR, with over 20 years of experience in structural engineering. He has built a strong reputation for excellence in wood design, specializing in both mid-rise buildings and custom homes.\n\nDrawing on a deep understanding of structural principles and a passion for innovative solutions, Conor delivers high-quality designs that prioritize safety, sustainability, and aesthetic appeal. His extensive experience spans complex mid-rise structures and intricately designed custom homes, leveraging expert knowledge of wood materials, engineering codes, and modern construction techniques.\n\nConor is recognized for combining functionality with design, creating spaces that stand the test of time. Known for his meticulous attention to detail, technical proficiency, and collaborative approach, he consistently navigates the unique challenges of each project while ensuring client satisfaction." },
                                new() { Name = "Islam Shabana",         Credentials = "Ph.D., P.Eng.",                                   PhotoBytes = photoShabana   ?? Array.Empty<byte>(), Bio = "Islam is a Senior Structural Engineer with over a decade of experience in the design and delivery of complex high-rise and tall building projects across Canada and internationally. He has played a key role in the structural design of numerous towers in Toronto and Vancouver, contributing to some of the most demanding urban developments in the country. His portfolio includes high-rise residential and mixed-use buildings, along with institutional projects such as universities and schools.\n\nWorking extensively in Alberta, British Columbia, and Ontario, Islam brings a strong understanding of regional design practices, construction methods, and project delivery requirements. His experience spans all phases of design, from early-stage concept development to detailed engineering and construction support, allowing him to deliver efficient, practical solutions that respond to architectural vision while maintaining constructability and performance.\n\nWith a PhD in seismic engineering and involvement as a peer reviewer for leading journals, Islam brings a high level of technical rigor to his work. However, his focus remains firmly rooted in industry practice\u2014developing reliable, well-coordinated designs that meet the needs of clients, consultants, and contractors. He thrives on opportunities to solve complex design challenges through creativity, collaboration, and technical expertise. His ability to move seamlessly between big-picture thinking and detailed engineering ensures that his work considers user experience, client priorities, and long-term impact." }
                            }
                        },
                        new()
                        {
                            BlockType = Kor.Operations.Core.Models.Brochure.BrochureBlockType.Section,
                            Section = new Kor.Operations.Core.Models.Brochure.BrochureSection
                            {
                                Heading = "Featured Large Mixed-Use Commercial & Residential Projects",
                                Blurb = "We have extensive experience with a wide range of building types and have completed over 2,000 projects, including concrete high-rises (over 65 stories), mid- and low-rise concrete buildings, structural steel, and wood-frame construction. Our portfolio spans residential low-rise buildings, office towers, recreational centers, and mixed-use developments. The following highlights some representative examples of the diverse projects we have designed.",
                                Projects = new System.Collections.ObjectModel.ObservableCollection<Kor.Operations.Core.Models.Brochure.BrochureProject>
                                {
                                    Proj("The Paradox Vancouver", "Vancouver (BC, CAN)", "Holborn Group and TA Global", "Arthur Erickson",
                                        "Located in the financial district of downtown Vancouver, the 63-story Paradox Hotel is the second-tallest building in the city and one of its most iconic landmarks. Designed by architect Arthur Erickson and developed by Holborn Group and TA Global, the tower\u2019s twisted hyperbolic paraboloid form presented unique structural engineering challenges. Extensive and iterative planning and design were required to realize the architectural vision while ensuring structural performance. To help control wind-induced motion in the slender, twisting form, the design incorporated a supplementary mass damper system, consisting of two water-filled slosh tanks at the roof, which enhances occupant comfort and structural stability.\n\nThe project includes market residential units from levels 26 to 63 with three different floor configurations and features a nine-level underground parking structure. The Paradox Hotel Vancouver tower is tied with Altus \u2014 another KOR-designed project \u2014 for the second-tallest building in British Columbia, showcasing KOR\u2019s expertise in delivering complex, high-rise structural solutions in challenging urban environments.", imgParadox),

                                    Proj("Station Square Towers", "Burnaby (BC, CAN)", "Anthem Properties & Beedie Living", "Chris Dikeakos Architects",
                                        "Station Square is a large, multi-phase mixed-use development featuring several residential towers integrated with retail and office space. Sites 2 and 3 include 38- and 49-story residential towers atop a podium containing retail and office space, with four levels of underground parking. Site 4, conveniently located near the Metrotown SkyTrain and Metropolis at Metrotown, features a 35-story residential tower with retail and office space above four levels of underground parking.\n\nSite 5 comprises a 41-story residential tower with 334 units, a three-level podium containing 40,000 sq. ft. of office space and 37,000 sq. ft. of commercial space, and a total buildable area of 308,762 sq. ft., supported by five levels of below-grade parking. Site 6 includes a 52-story residential tower with 421 units and a three-level, 102,000 sq. ft. mixed-use podium, with a total buildable area of 408,898 sq. ft. and four levels of below-grade parking.", imgStationSquare),

                                    Proj("Halifax & Willingdon", "Burnaby (BC, CAN)", "Bosa Development", "Chris Dikeakos Architects",
                                        "Located at the intersection of Halifax Street and Willingdon Avenue in North Burnaby, this landmark mixed-use development by Bosa Development is part of the Brentwood West master plan, directly adjacent to the SkyTrain and across from The Amazing Brentwood. The project comprises two towers of approximately 60 and 41 stories above mixed-use podiums, delivering residential ownership, purpose-built rental housing, and about 200,000 sq.ft. of office and commercial space within a highly walkable, transit-oriented urban environment.\n\nThe towers employ efficient lateral systems tailored for slender high-rise performance. Viscoelastic coupling dampers are integrated within coupling beams throughout the height, controlling wind-induced accelerations to improve occupant comfort while maintaining structural efficiency.", imgHalifaxWillingdon),

                                    Proj("One Burrard Street", "Vancouver (BC, CAN)", "Resilience Properties", "IBI Group",
                                        "One Burrard Place is a 55-story mixed-use high-rise combining 444 residential units with three levels of office space, all built atop eight levels of below-grade parking. Strategically located at the gateway to Downtown Vancouver, the tower serves as a prominent architectural landmark, offering residents panoramic views of the city, Burrard Inlet, and the surrounding British Columbia landscape. Its design, high-quality finishes, and thoughtfully planned amenities\u2014including retail, fitness, and common areas\u2014enhance urban living and contribute to the vibrancy of the downtown core.\n\nAt completion, One Burrard Place ranked as the third tallest building in Vancouver, making a notable contribution to the city\u2019s skyline. Designed with sustainability and efficiency in mind, the development achieved LEED Gold certification, reflecting commitment to high-performance building standards and long-term operational efficiency. KOR provided engineering solutions that ensured resilience and constructability, supporting the complex architectural form while creating a landmark development that integrates ambitious design with sustainable urban living.", imgOneBurrard),

                                    Proj("1011 Union Street", "San Diego (CA, US)", "Holland Partner Group", "Carrier Johnson + Culter",
                                        "Located at the intersection of West Broadway and Union Street in downtown San Diego, adjacent to the San Diego Central Courthouse and Hall of Justice, this 38-storey mixed-use tower is a prominent addition to the city\u2019s Civic Core. Designed by Carrier Johnson + Culture and developed by Holland Partner Group, the glass-clad structure features distinctive V-shaped columns at the main entry and integrates residential, office, and retail uses within a cohesive urban development.\n\nThe project presented significant structural challenges, addressed through an extensive and iterative design process, including the use of performance-based seismic design to meet the region\u2019s high seismic demands. This approach enabled efficient system optimization while accommodating the building\u2019s architectural complexity, resulting in a resilient and forward-thinking development that contributes to a vibrant, walkable downtown environment.", img1011Union),

                                    Proj("West Pender Place", "Vancouver (BC, CAN)", "Reliance Holdings", "IBI Group",
                                        "KOR was retained as the structural consultant for West Pender Place, a refined mixed-use development located at 1409 West Pender Street, one of the last remaining development sites in Coal Harbour, Vancouver\u2019s most prestigious waterfront neighborhood. The project comprises a 37-storey tower alongside an 11-storey mid-rise, forming a cohesive and contemporary architectural statement within a district known for its luxury residential towers and proximity to the waterfront, marina, and Stanley Park. The development offers an exclusive residential experience characterized by expansive floor-to-ceiling glazing, high-end finishes, and unobstructed views of the North Shore Mountains and Burrard Inlet.\n\nDesigned to complement its prime location, the project features premium amenities and achieves LEED Silver certification, reflecting a strong commitment to sustainable design while delivering a contemporary landmark in Coal Harbour.", imgWestPender),

                                    Proj("564 Beatty St", "Vancouver (BC, CAN)", "Reliance Properties", "IBI Group & Carscadden Stokes McDonald Architects",
                                        "The redevelopment of 564 Beatty Street exemplifies the integration of heritage preservation with contemporary structural innovation. Developed by Reliance Properties, the project transformed a six-story, 100-year-old brick and heavy timber building into a ten-story structure through the addition of four levels of clear-span office space. The design preserves the original character of the lower floors\u2014with exposed brick and timber\u2014while introducing a modern glass curtain wall above, creating a compelling \u201cold meets new\u201d architectural expression.\n\nThe project required advanced structural solutions, including seismic upgrading of the existing building and careful integration of new construction over the heritage structure, balancing technical performance with the owner\u2019s vision. Its success was recognized with multiple awards, including \u201cBest in Show\u201d and \u201cBest Heritage\u201d from the Urban Development Institute Pacific Region, a Gold Award of Excellence from the Vancouver Regional Construction Association, and the Vancouver Urban Design Award.", img564Beatty),

                                    Proj("The Royal", "Calgary (AB, CAN)", "Bosa Development", "Buttjes Architecture",
                                        "Located at 936 16th Avenue Southwest in Calgary\u2019s Mount Royal Village West, The Royal is a 34-storey condominium tower with approximately 222 residential units and street-level retail, including a premium grocer. KOR Structural served as the structural consultant, delivering solutions that support the tower\u2019s sophisticated design and its role as a vibrant, walkable urban hub.\n\nThe project involved complex high-rise construction with expansive amenity spaces, rooftop terraces, and underground parking. KOR developed efficient structural systems and collaborated closely with the design and construction teams to ensure safety, performance, and seamless integration of residential, retail, and amenity spaces, resulting in a landmark addition to Calgary\u2019s skyline.", imgTheRoyal),

                                    Proj("Port Coquitlam Community Center", "Port Coquitlam (BC, CAN)", "Ventana Construction", "Architecture 49",
                                        "The Port Coquitlam Community Centre (Poco Rec Centre) is a 375,800 sq. ft. fast-track design-build project delivered in collaboration with Ventana, Architecture49, KOR Structural, and the City of Port Coquitlam. The complex integrates the Terry Fox Library, Wilson Lounge, three arenas, an aquatic center, fitness facilities, and a gymnasium, setting a new standard for community-focused infrastructure while meeting the City\u2019s budget requirements.\n\nStructurally, the project incorporates long-span glulam beams and steel trusses with glulam beams framed into wood V-columns and integrated steel bracing to create expansive recreational spaces. Challenging soil conditions required a pile-supported foundation, and KOR optimized the pile layout in coordination with the contractor to balance geotechnical requirements, structural performance, and budget.", imgPocoRec),

                                    Proj("The HOUSS", "Vancouver (BC, CAN)", "Conwest Group of Companies", "Yamamoto Architecture",
                                        "The HOUSS development in Vancouver\u2019s Mount Pleasant neighborhood is a notable example of integrating heritage preservation within a contemporary industrial and commercial project. Designed by Yamamoto Architecture for Conwest Group, the development provides approximately 50,000 sq. ft. of strata office and light industrial space while incorporating the 1901 Coulter House as a central feature. The heritage home is not treated as an afterthought but instead anchors the site, shaping the overall massing and creating a strong connection to the street.\n\nRepurposed as a restaurant, the restored house brings new life and public engagement to the site while preserving its historical character. The surrounding contemporary structure contrasts with and highlights the heritage building through modern materials and reflective fa\u00e7ades, reinforcing connections to the neighborhood\u2019s residential past. The project demonstrates a successful balance between heritage conservation and urban intensification, contributing to the evolving identity of Mount Pleasant.", imgHouss),

                                    Proj("Arris Residences", "Alberta (AB, CAN)", "Bosa Development", "GGA Architecture/Amanat Architecture",
                                        "Located in Calgary\u2019s East Village, Arris Residences is a landmark mixed-use development by Bosa Development that forms a key part of the area\u2019s ongoing urban revitalization. Rising 41 stories above a commercial podium, the project is the tallest residential building in the neighborhood and delivers over 300 residential units, complemented by a second 24-storey tower integrated within the same retail-driven master plan. Positioned at the river\u2019s edge and directly connected to downtown, the development exemplifies high-density urban living anchored by and transit-oriented design.\n\nArris is defined by its integrated mixed-use approach, combining residential towers with over 170,000 sq. ft. of retail and service amenities within the podium, including grocery, dining, and everyday conveniences. The development also features an extensive amenity program\u2014ranging from fitness and wellness facilities to social and workspaces\u2014designed to support a complete live-work-play environment. This integration of residential density, commercial activation, and lifestyle-focused amenities positions Arris as a transformative addition to Calgary\u2019s East Village.", imgArris),

                                    Proj("The Grande Condos", "San Diego (CA, US)", "Bosa Development", "Perkins & Co",
                                        "The Grande in Downtown San Diego is a landmark twin-tower development located near San Diego Bay. Each tower rises 40 stories (400 ft) and features a distinctive triangular form, constructed of reinforced concrete with post-tensioned floor systems and supported by three levels of underground parking. The project combines architectural distinction with high-performance engineering, creating a signature presence in the downtown skyline.\n\nThe site presented unique challenges, including poor soil conditions, a high groundwater table, and proximity to the bay, in addition to rigorous seismic demands. KOR Structural delivered innovative solutions to address these constraints, successfully securing approval from the San Diego Building Department for advanced structural design features. The project\u2019s engineering excellence was recognized with the Multi-Family Residential Award from the American Concrete Institute, highlighting its achievement in complex, high-rise reinforced concrete design.", imgGrandeCondos),

                                    Proj("Richards & Drake", "Vancouver (BC, CAN)", "Bosa Development", "DA Architects + Planners",
                                        "Richards & Drake is a 40-storey mixed-use residential tower in downtown Vancouver, designed by DA Architects + Planners. The project includes over 190 rental units atop a vibrant podium with community and amenity spaces and features a distinctive triangular form that enhances the streetscape while integrating rooftop gardens and outdoor areas for residents.\n\nWe delivered the structural design for Richards & Drake, developing an efficient system to support the tower\u2019s unique geometry and slender floor plates. A tuned mass damper was incorporated to mitigate wind-induced vibrations and improve occupant comfort. Close coordination with the construction team ensured a constructible and well-resolved structure, resulting in a high-performance development that aligns with both architectural intent and urban design objectives.", imgRichardsDrake),

                                    Proj("Parkside at Waterfront", "Calgary (AB, CAN)", "Anthem Properties", "Rafii Architects & IBI Group",
                                        "Waterfront Parkside is a testament to how great communities are built over time, representing a complex, multi-phase structural undertaking along the Bow River in downtown Calgary. Developed by Anthem Properties, this transformative master-planned community will deliver over 1,000 homes across nine buildings, supported by three underground parkade structures, creating a vibrant riverfront destination.\n\nThe development includes three landmark residential towers rising from an integrated mid-rise podium, along with five additional mid-rise buildings constructed over two separate below-grade parkades. Its riverside setting, expansive footprint, terraced design, and phased delivery demanded innovative structural solutions and close collaboration across the consultant team to successfully bring the project to life.", imgParksideWaterfront),

                                    Proj("Paramount Place", "Vancouver (BC, CAN)", "Bosa Development", "Rafii Architects",
                                        "Paramount Place is a 23-storey mixed-use high-rise in Downtown Vancouver, comprising 460 residential units above a podium that accommodates restaurants, retail spaces, and a 9-screen movie theatre. The development includes three levels of underground parking, integrating complex programmatic requirements while maintaining a compact urban footprint. The tower\u2019s vertical and lateral systems were engineered to accommodate slender floor plates and podium-to-tower transitions, ensuring both structural efficiency and constructability.\n\nKor Structural delivered the structural engineering solutions for Paramount Place, addressing the challenges of combining high-rise residential units with large, open commercial and theatre spaces at podium levels. Special attention was given to floor load distribution, amenity integration, and seismic and wind performance, resulting in a robust and efficient system that supports the architectural vision and enhances the functionality and vibrancy of Downtown Vancouver.", imgParamountPlace),

                                    Proj("Langley Events Centre", "Langley (BC, CAN)", "Ventana Construction", "MQN Architects",
                                        "The Langley Events Centre is a 250,000 sq. ft. multiplex facility delivered through a collaborative effort between the Township of Langley, KOR, MQN, and Ventana Construction. The development features a 5,276-seat NHL arena with premium suites, a 2,200-seat triple gymnasium, and a wide range of community amenities including a field house, banquet hall, indoor walking track, and flexible meeting spaces. Shaped through extensive stakeholder engagement, the facility was designed to support a broad spectrum of athletic, educational, and community activities while incorporating sustainable and energy-efficient building strategies.\n\nFrom a structural engineering perspective, the project features a hybrid system integrating glulam tied arches, glulam V-columns, and steel bracing to efficiently achieve long-span, column-free spaces over key program areas. Steel roof framing was utilized in the gymnasiums, while concrete tilt-up panels provide a durable and economical perimeter enclosure. The coordinated use of timber and steel systems, along with concurrent erection strategies, supported an accelerated construction schedule without compromising structural performance.", imgLangleyEvents),

                                    Proj("Solo District", "Burnaby (BC, CAN)", "Appia Development", "Carrier Johnson + Culter",
                                        "The Solo District master-planned community at Lougheed Highway and Willingdon Avenue in Burnaby\u2019s Brentwood neighborhood comprises four high-rise mixed-use towers\u2014Cirrus (42 stories), Stratus (45 stories), Aerius (52 stories), and Altus (49 stories)\u2014all above five levels of underground parking. Developed by Appia Development, the project integrates residential, office, retail, and amenity spaces within a cohesive urban framework. Designed to LEED Gold standards, it emphasizes sustainability, connectivity, and a pedestrian-oriented environment with strong transit access.\n\nSolo District enhances the Greater Vancouver skyline with high-density, sustainable design. Engineered for long-term performance, its resilient structural systems support versatile mixed-use programming, combining architectural quality, environmental responsibility, and functional efficiency in a dynamic urban setting.", imgSoloDistrict),

                                    Proj("Mosaic Place", "Moose Jaw (SK, CAN)", "The City of Moose Jaw", "MQN Architects",
                                        "The Moose Jaw Multiplex, a 208,000-sf state-of-the-art recreational facility, was delivered through a successful collaboration between Ventana, Hockey Capital Corporation (HCC), and the City of Moose Jaw. The complex features a 5,000-seat NHL-size ice surface, an eight-sheet curling rink, and comprehensive hosting facilities. Complementing the ice arenas, the 114,000-sf soccer field house includes a 365-meter indoor running track and a modular soccer field that can be partitioned for multiple user groups, providing versatile programming for the community.\n\nFrom a structural perspective, KOR provided engineering for the multiplex, employing long-span steel framing to achieve large, column-free spaces over the arenas and rinks. The design balanced structural efficiency, constructability, and aesthetic clarity, accommodating the functional demands of both ice and field sports.", imgMosaicPlace),

                                    Proj("Highline", "Burnaby (BC, CAN)", "Thind Properties", "Chris Dikeakos Architects",
                                        "Highline is a 48-storey mixed-use concrete high-rise located at 6511 Sussex Avenue in Burnaby\u2019s Metrotown district, completed in 2023. The development comprises approximately 327 residential units above a multi-level podium that incorporates ground-level retail and office space, with residents enjoying panoramic city views and a robust set of amenities. Situated steps from the Metrotown SkyTrain station and major transit links, the tower contributes to the urban fabric by reinforcing transit-oriented development in one of Metro Vancouver\u2019s busiest hubs.\n\nHighline utilizes a reinforced concrete vertical and floor framing system typical of high-rise residential buildings, optimized for gravity and lateral load resistance in a high seismic zone. The concrete structural frame supports repetitive residential floor plates above a podium that accommodates larger column-free spaces for commercial and amenity uses, requiring careful transfer design at lower levels.", imgHighline),

                                    Proj("Rockridge Canyon Clubhouse", "Princeton (BC, CAN)", "Young Life Canada", "CEI Architecture",
                                        "Situated within the stunning landscape of Princeton, The Rock is a 300-seat performing arts space designed to enhance the creative experiences of Canadian youth. The facility includes expansive lobby areas and lower-level classrooms, with a flexible layout that accommodates various performance types through movable seating arrangements. The clubhouse design harmonizes with its natural surroundings, offering panoramic views of a man-made lake and abundant daylight, while expressive wood elements\u2014columns, beams, and acoustic panels\u2014reflect the camp-inspired aesthetic valued by the client.\n\nKor provided structural engineering services using concrete, steel, and wood systems. Concrete walls and suspended slabs formed the partially daylit basement, while steel and wood framed the floors and roof above. This approach ensured durability, performance, and a design that harmonizes with the surrounding landscape.", imgRockridge),

                                    Proj("Rogers Arena South Tower", "Vancouver (BC, CAN)", "Appia Development", "Chris Dikeakos Architects",
                                        "The Solo District master-planned community at Lougheed Highway and Willingdon Avenue in Burnaby\u2019s Brentwood neighborhood comprises four high-rise mixed-use towers\u2014Cirrus (42 stories), Stratus (45 stories), Aerius (52 stories), and Altus (49 stories)\u2014all constructed above five levels of underground parking. Developed by Appia Development, the project integrates residential, office, retail, and amenity spaces within a cohesive urban framework. Designed to LEED Gold standards, it emphasizes sustainability, connectivity, and a pedestrian-oriented environment with strong access to transit and community amenities.\n\nAt completion, Solo District made a notable contribution to the Greater Vancouver skyline, showcasing high-density development paired with sustainable design. Engineered for long-term performance with efficient and resilient structural systems, the project reflects a strong balance of architectural quality, environmental responsibility, and functional versatility.", imgRogersArena),

                                    Proj("Chilliwack Coliseum", "Chilliwack (BC, CAN)", "Ventana Construction", "MQN Architects",
                                        "The Chilliwack Coliseum is a 144,000 sq. ft. multiplex arena delivered through a successful collaboration between Ventana Construction, MQN Architects, KOR Structural, and the City of Chilliwack. The facility includes a 5,000-seat NHL-size ice surface, a secondary NHL-size rink with 300 seats, and approximately 20,000 sf of community space. Delivered under a design-build-finance-operate model with Hockett Capital Corporation, the project stands as a strong example of a small-scale public-private partnership, supported by clear community approval.\n\nStructurally, the building utilizes long-span curved steel trusses and a steel bracing system to efficiently span large column-free spaces over the rinks. This approach provided an economical and practical solution while maintaining unobstructed sightlines and flexibility within the arena bowl. The integration of these systems reflects a coordinated design strategy that balances structural efficiency with the functional and experiential demands of a modern recreational facility.", imgChilliwack),

                                    Proj("The Sovereign", "Burnaby (BC, CAN)", "Cressey Development", "IBI Group",
                                        "The Sovereign is a 45-storey mixed-use high-rise located at 4501 Kingsway in Burnaby\u2019s Metrotown district, completed in 2014. Developed by Bosa Properties, this landmark tower integrates 202 luxury residential units above a hotel podium and significant retail and commercial space, establishing a dynamic urban presence at one of Burnaby\u2019s busiest intersections. Rising approximately 155.9 m (511 ft), the Sovereign was briefly the tallest building in the city and offers panoramic views toward Downtown Vancouver, the North Shore mountains, and beyond.\n\nKor Structural provided structural engineering design services for this iconic project, addressing the complexities inherent in a tall-form mixed-use tower. The design incorporated robust, high-performance structural systems to support varied programmatic functions \u2014 from retail and hotel spaces in the lower levels to high-rise residential above \u2014 while accommodating large open floor plates and architectural glazing.", imgSovereign),

                                    Proj("The Offices at Burrard Street", "Vancouver (BC, CAN)", "Reliance Properties", "Bing Thom Architects",
                                        "The Offices at Burrard Place is a landmark 13-storey office building located at 1280 Burrard Street, marking one of the most visible corners of Downtown Vancouver\u2019s mixed-use Burrard Place precinct. Designed by the architect Bing Thom, the approximately 140,000 sq. ft. commercial tower features a striking sculptural fa\u00e7ade and generous floor-to-ceiling heights, creating light-filled, flexible workspaces that cater to modern office tenants. Positioned at the southern gateway to the downtown peninsula, the building enhances pedestrian activity at street level and integrates seamlessly with adjacent residential and retail components of the Burrard Place development.\n\nKor Structural provided structural engineering for The Offices at Burrard Place, designing concrete and steel systems to support the architectural form, functional requirements, and seismic compliance, delivering a high-quality, adaptable commercial workspace.", imgOfficesBurrard),

                                    Proj("Park Point", "Calgary (AB, CAN)", "Qualex Landmark", "IBI Group",
                                        "Park Point is a 34-storey residential high-rise located in Calgary\u2019s vibrant Beltline community, developed by Qualex-Landmark and completed in 2018. The tower features approximately 288 condominium homes, ranging from one- to three-bedroom units and live townhomes, all built above three levels of underground parking. Positioned just steps from Central Memorial Park and Haultain Park, the building offers residents convenient access to green spaces, transit, and urban amenities. Its sculptural fa\u00e7ade and vertical articulation create a striking presence on Calgary\u2019s skyline, reflecting a thoughtful balance between architectural expression and functional urban living.\n\nThe tower\u2019s structural design combines concrete and composite framing systems to support high residential loads and long podium spans while maintaining flexible interior layouts. Strategic placement of shear walls and transfer slabs ensured durability, seismic compliance, and constructability, resulting in a high-performance tower that enhances Calgary\u2019s Beltline skyline.", imgParkPoint),

                                    Proj("Lougheed Heights", "Coquitlam (BC, CAN)", "Blue Sky Properties", "Chris Dikeakos Architects",
                                        "Lougheed Heights is a master-planned residential community in Coquitlam, strategically located along the Evergreen SkyTrain Line providing excellent transit access and urban connectivity. The development features a 37-story residential tower with four attached townhomes and a two-story amenity building, a five-story rental building with 57 units, and two additional residential towers rising 29 and 28 stories, all constructed over shared underground parking. In total, the project delivers over 780 residential units, integrating diverse housing types with amenity-rich spaces to create a vibrant, walkable community.\n\nThe project\u2019s structural design combines concrete flat slabs with centrally located shear walls to efficiently support high-rise residential loads, podium and parking levels, and amenity spaces. The integrated approach ensures durability, seismic resilience, and constructability while accommodating complex architectural forms.", imgLougheed),

                                    Proj("Kings Crossing", "Burnaby (BC, CAN)", "Cressey Development", "IBI Group",
                                        "Kings Crossing Development by Cressey is a master-planned mixed-use community at the high-traffic intersection of Kingsway and Edmonds Street in Burnaby\u2019s Edmonds Town Centre. Developed by Cressey Development Group, the project integrates diverse components including a pavilion-inspired office tower, approximately 70,000 sq ft of grocery-anchored retail, and three residential towers rising over multiple stories with nearly 800 condominium homes. Designed to create a vibrant, walkable, and transit-friendly urban district, Kings Crossing contributes to the ongoing transformation of Edmonds into a complete community with convenient access to transit, services, and amenities.\n\nThe structural design for Kings Crossing employs efficient concrete and composite systems to support the high-rise residential towers, retail podiums, and office spaces. By carefully coordinating vertical and lateral load paths, framing strategies, and amenity transitions, Kor Structural delivered solutions that ensured safety, durability, and constructability while bringing the architect\u2019s vision of a vibrant, mixed-use urban district to life.", imgKingsCrossing),

                                    Proj("567 Clarke + Como", "Coquitlam (BC, CAN)", "Marcon Group", "GBL Architects",
                                        "567 Clarke + Como consists of two residential towers in Coquitlam\u2019s Burquitlam neighborhood, rising 49 and 14 stories to create a new landmark within the rapidly growing transit-oriented community. Together the towers accommodate a combined total of 364 residential units along with approximately 200,000 sq ft of street-level commercial space, all positioned over seven levels of underground parking. Positioned adjacent to the Evergreen SkyTrain Line, the development offers residents exceptional connectivity and amenity-rich living, including rooftop lounges, outdoor sports courts, and multiple community gardens that enhance urban lifestyle and landscape integration.\n\nThe structural design for 567 Clarke + Como employs efficient concrete and composite systems to support high-rise residential loads, podium spaces, and sustainable constructability across varied building forms. Thoughtful attention to vertical and lateral load paths, framing, and amenity transitions ensures performance, durability, and alignment with the architectural intent of sculptural massing and a dynamic urban interface.", img567Clarke),

                                    Proj("The Lindley", "San Diego (CA, US)", "Toll Brothers", "JWDA Inc.",
                                        "Located in San Diego\u2019s vibrant Little Italy, The Lindley is a 37-storey residential high-rise comprising over 360 units. Developed by Toll Brothers, the project showcases modern architectural design by JWDA, integrating a dynamic urban presence while complementing the neighborhood\u2019s historic character. The tower contributes to the evolving skyline while enhancing the pedestrian-oriented fabric of this popular urban district.\n\nThe structural design incorporated a performance-based approach as required by ASCE standards for buildings over 240 ft without a moment-resisting frame. This analysis guided the development of an efficient lateral system and overall structural strategy. Careful consideration of vertical and lateral load paths, framing systems, and amenity level transitions ensured a cohesive, durable, and constructible solution.", imgLindley),

                                    Proj("1401 Union Street \u2013 Simone", "San Diego (CA, US)", "Trammel Crow Residential", "JWDA Inc.",
                                        "Located at the intersection of Union Street and West Ash Street in downtown San Diego, this 37-storey residential tower rises prominently just north of the Gaslamp District. The glass-clad structure features 30 levels of residential units with panoramic views of the city and San Diego Bay, supported by four levels of above-grade parking and three levels below grade. Designed by Joseph Wong Design Associates and developed by Trammell Crow Residential, the building also includes a two-story Sky Lounge on the 36th floor with an outdoor pool and premium amenity spaces.\n\nKor Structural provided the structural engineering of the building utilizing a performance-based seismic analysis/design approach to meet the rigorous code criteria for high-rise construction in San Diego. The structural system was designed to efficiently resolve lateral and gravity demands, with clearly articulated load paths and coordinated integration of parking and amenity levels. The result is a resilient, well-coordinated structure that aligns with the architectural vision and contributes to San Diego\u2019s downtown skyline.", img1401UnionSimone),

                                    Proj("The Standard", "Burnaby (BC, CAN)", "Anthem Properties", "GBL Architects",
                                        "This project comprises a 43-storey concrete residential tower with a connected 6-storey mid-rise rental building, delivering 424 market units and 92 below-market rental homes in Burnaby\u2019s Metrotown. Anchoring a transit-oriented corridor near Central Park, the SkyTrain, and Metropolis at Metrotown, the development features modern architectural expression and over 22,000 sq ft of indoor and outdoor amenity space, reinforcing Metrotown as a vibrant urban center.\n\nThe structural design supports the high-rise tower, podium townhouses, and mid-rise wood-framed rental building over four levels of shared below-grade parking, using robust concrete systems and efficient load-resisting frameworks for complex gravity and lateral demands. Careful coordination of vertical load paths, transfer elements, and the interaction between concrete and wood framing delivered a cohesive solution that aligns with the architectural vision while ensuring constructability and long-term performance.", imgTheStandard),

                                    Proj("South Yards", "Burnaby (BC, CAN)", "Anthem Properties", "IBI Group",
                                        "South Yards is an 8.3-acre, master-planned mixed-use community by Anthem Properties in Burnaby\u2019s Brentwood Town Centre. The development features five high-rise residential towers, including two 43-storey towers, alongside three low- to mid-rise buildings, a one-acre central park, and 60,000 sq ft of commercial and retail space. The project delivers approximately 2,567 homes, including market condos, rental suites, and affordable units, creating a vibrant, transit-oriented urban hub.\n\nKOR provided structural engineering for South Yards, where post-tensioned transfer slabs were used to limit the transfer depth between podiums and towers. This strategy efficiently managed the complex vertical load paths while directly impacting the building\u2019s lateral behavior under seismic forces. By controlling the transfer depth, seismic demands on the lateral force-resisting system were better concentrated and managed, resulting in a more resilient and efficient high-rise structure.", imgSouthYards),

                                    Proj("Central at 1618 Quebec Street", "Vancouver (BC, CAN)", "Onni Group", "IBI Group",
                                        "Central at 1618 Quebec Street is a distinctive high-rise in Vancouver\u2019s Southeast False Creek, rising 22 stories with approximately 304 residential units above an integrated commercial and amenity base. Its concrete construction and bold form, including a mid-level Skybridge linking structural blocks, create a striking presence in a dynamic urban neighborhood near the Main Street SkyTrain, Science World, and waterfront.\n\nThe structural design for Central provides a robust concrete framework that accommodates complex geometry and vertical circulation while ensuring constructability. Coordinated lateral and gravity load paths, including the Skybridge, deliver a cohesive solution for residential and commercial spaces. As a standout on False Creek\u2019s skyline, Central exemplifies advanced structural design and contemporary urban living.", imgCentralQuebec),

                                    Proj("\u00c9toile Gold", "Burnaby (BC, CAN)", "Millennium Development", "Chris Dikeakos Architects",
                                        "Etoile Gold is a 47-storey concrete residential tower in Burnaby\u2019s Brentwood neighborhood, comprising approximately 277 units above a multi-level podium. As the final phase of the \u00c9toile master-planned community, the project includes over 35,000 sq ft of amenity space across multiple levels, with a prominent architectural expression that contributes to the Brentwood skyline.\n\nWe carried out the structural design for Etoile Gold, developing a reinforced concrete system tailored to the tower\u2019s height and slender proportions. A sloshing tank damper was incorporated to mitigate wind-induced vibrations and enhance occupant comfort. The design also emphasizes an efficient core-wall system and floor framing strategy, with careful detailing to manage load transfer through amenity levels and podium interfaces.", imgEtoileGold),

                                    Proj("Jinju Condos", "Coquitlam (BC, CAN)", "Onni Group", "IBI Group",
                                        "Jinju is a 42-storey concrete residential tower located at 537 Cottonwood Avenue in Coquitlam\u2019s Burquitlam neighborhood, comprising approximately 467 residential units. The development includes a mix of condominium homes, rental units, and townhomes within the tower, along with a separate 6-storey wood-frame rental building, all positioned above a shared underground parkade. Located steps from Burquitlam SkyTrain Station, the project contributes to the rapid transformation of this transit-oriented urban center.\n\nWe delivered the structural design for Jinju, focusing on the integration of multiple building components within a unified structural system. The design required careful coordination of tower-to-podium transitions and the interface between the concrete tower and the adjacent wood-frame building over the shared parkade. Structural detailing addressed differential movement, load sharing, and sequencing considerations, resulting in a cohesive and constructible solution aligned with the project\u2019s architectural and site constraints.", imgJinju),

                                    Proj("The Grand on King George Boulevard", "Surrey (BC, CAN)", "Allure Ventures Inc.", "IBI Group",
                                        "The Grand on King George is a 46-storey mixed-use residential tower located at 10731 King George Boulevard in Surrey City Centre, comprising approximately 341 condominium units above a multi-level podium with integrated commercial space. Rising as one of the tallest buildings in the area, the tower contributes to the evolving skyline while offering over 23,000 sq ft of indoor and outdoor amenity space distributed across multiple levels.\n\nWe delivered the structural design for The Grand on King George, developing a reinforced concrete system suited to a slender high-rise configuration. A sloshing tank damper was incorporated at the upper levels to mitigate wind-induced vibrations and enhance occupant comfort. The design also addressed tower-to-podium transitions and vertical irregularities associated with the mixed-use base, with optimized floor framing and core systems supporting efficient construction and overall structural performance.", imgGrandKingGeorge),

                                    Proj("Smith and Farrow", "Coquitlam (BC, CAN)", "Boffo Development", "Chris Dikeakos Architects",
                                        "Smith & Farrow is a residential community in West Coquitlam, featuring a 46-storey condominium tower with 340 units, eight townhomes, and a 21-storey rental tower with over 100 units. Located steps from Burquitlam SkyTrain, the development offers extensive indoor and outdoor amenities, including rooftop lounges, fitness facilities, outdoor pool and hot tub, and workshare spaces, creating a vibrant, transit-oriented urban environment.\n\nWe delivered structural engineering for Smith & Farrow, developing a reinforced concrete system tailored to the tower\u2019s height, slender proportions, and mixed-use program. Significant transfer slabs were incorporated to accommodate the change in column layout from the towers to the podium and parkade levels, efficiently resolving complex load paths and vertical transitions. Careful planning of sequencing, constructability, and structural detailing ensured long-term performance while maintaining alignment with the architectural vision and high-rise design objectives.", imgSmithFarrow),

                                    Proj("SOCO Condos", "Coquitlam (BC, CAN)", "Anthem Properties", "IBI Group",
                                        "SOCO is a twin-tower residential development by Anthem Properties located in the Burquitlam neighborhood of Coquitlam. The project comprises two high-rise concrete towers, each approximately 30 stories, delivering a combined total of nearly 700 residential units above a shared podium and underground parking structure. With integrated amenity and street-level retail, SOCO reinforces the area\u2019s transit-oriented growth and provides a strong urban presence next to Burquitlam SkyTrain Station.\n\nFor SOCO, the structural design focused on efficient transfer strategies and core-wall optimization to accommodate changes in column layout between the towers and podium. Large openings near the core for amenities and mechanical access required a complex diaphragm design to ensure lateral load transfer and maintain stiffness under seismic and wind forces. Careful detailing of the podium, amenity, and tower interfaces, along with sequencing strategies, ensured constructability and structural continuity, resulting in a resilient, high-performance structure aligned with the architectural vision.", imgSoco),

                                    Proj("Millennium Sports Centre", "Vancouver (BC, CAN)", "Millennium Sport Facility Society", "FRANCL Architecture",
                                        "The Millennium Sports Centre is a 44,000 sq ft facility in Hilcrest Park, adjacent to Nat Bailey Stadium, housing Vancouver Phoenix Gymnastics and the Pacific Indoor Bowls Club. The sloped site provides ground-level access to both the subterranean Bowls Club and the gymnasium above, with independent entrances and an elevator connection to allow flexible use.\n\nKor Structural developed an efficient hybrid system of glulam beams, steel trusses, and concrete elements to achieve long clear spans and open interior spaces. The design addressed lateral stability, roof support, and multi-level load transfer, while careful detailing ensured constructability, durability, and performance for this versatile, multi-use facility.", imgMillenniumSports),

                                    Proj("Neelu Bachra Centre", "Vancouver (BC, CAN)", "Orca West Development", "Studio One Architecture",
                                        "Neelu Bachra Centre is a mixed-use office and commercial complex located at the corner of West Broadway and Cambie Street in Vancouver, featuring approximately 20,460 sq. ft. of commercial space, 99,500 sq. ft. of office space, and 3,700 sq. ft. of amenity areas, including a landscaped roof garden. Strategically positioned in a rapidly growing urban node, the project supports flexible tenancy and modern workplace layouts while enhancing the streetscape and pedestrian experience.\n\nKor Structural provided structural engineering, developing an efficient floor and lateral system to accommodate open, adaptable interiors and amenity levels. The design addressed column layouts, vertical circulation, and lateral stability, while careful coordination ensured constructability, long-term performance, and seamless integration with the architectural vision.", imgNeeluBachra),

                                    Proj("Radius Condos", "Calgary (AB, CAN)", "Bucci Development", "Wilson Chang Architects & Casola Koppe Architects",
                                        "Radius (Bridges Phase 3) is a 7-storey residential apartment building located at 1009 Centre Avenue NE in Calgary\u2019s Bridgeland neighborhood. The project features a rooftop terrace and urban garden, two and a half levels of below-grade parking, and is LEED Silver certified, reflecting a commitment to sustainable design and energy efficiency. The development provides modern, flexible living spaces in a vibrant urban context while enhancing the streetscape and community amenities.\n\nWe delivered structural engineering, developing an efficient structural system optimized for the building\u2019s height, slender footprint, and mixed-use podium. The design addressed lateral and gravity load demands, column layouts, and integration of the rooftop terrace and parking levels. Careful coordination and detailing ensured constructability, durability, and performance, resulting in a resilient structure that supports both the architectural intent and the LEED Silver sustainability objectives.", imgRadiusCondos),

                                    Proj("The Kensington", "Calgary (AB, CAN)", "Bucci Development", "Casola Koppe Architects",
                                        "Located along 10th Street NW between Sunnyside and Hillhurst in Calgary, this six-story mixed-use building offers commercial and retail space at grade, with two levels of below-grade parking. Its central location provides convenient access to downtown, SAIT, ACAD, U of C, Sunnyside C-Train Station, Foothills Medical Centre, Riley Park, the Bow River Pathway, and the amenities of Kensington Village, making it a vibrant addition to the urban fabric.\n\nKor Structural provided structural engineering, ensuring the building\u2019s design could accommodate flexible commercial spaces, modern amenities, and seamless integration with the surrounding neighborhood. Careful coordination and innovative planning supported efficient construction and a high-quality finished product, resulting in a building that enhances the streetscape and contributes to the vibrancy and connectivity of the Sunnyside and Hillhurst communities.", imgKensington),

                                    Proj("Azur Hotel", "Vancouver (BC, CAN)", "Executive Group Development", "Studio One Architecture",
                                        "AZUR Legacy Collection Hotel is a 13-story luxury boutique hotel located in Downtown Vancouver, designed with a concrete structural system to support a refined urban hospitality experience. The development features a restaurant at street level, a distinctive back-alley valet entrance, and a landscaped rooftop restaurant. Inspired by 1940s Art Deco architecture, the building combines elegant detailing with modern construction, creating a strong architectural identity within a dense city context.\n\nFrom a structural standpoint, the project integrates multiple vertical amenities, including 104 guest rooms and rooftop dining spaces, within a constrained urban footprint. The design required careful coordination to accommodate complex loading conditions, high-end finishes, and experiential features, resulting in a well-balanced solution that supports both architectural intent and functional performance.", imgAzurHotel)
                                }
                            }
                        },
                        new()
                        {
                            BlockType = Kor.Operations.Core.Models.Brochure.BrochureBlockType.ClientList,
                            ClientListHeading = "Our Clients",
                            ClientNames = new System.Collections.ObjectModel.ObservableCollection<string>
                            {
                                "Action Projects Inc.", "Acton Ostry Architects Inc.", "Adera Development Corp.", "Aim Force Development Group Ltd.",
                                "AKA Architecture + Design", "Alabaster Developments Ltd.", "Alan James Architect", "Align Construction Ltd.",
                                "Allaire Properties Inc.", "Allure Ventures Inc.", "Alston Properties Ltd.", "Altea Active", "Alvair Group",
                                "Amachris Corp", "Amacon Developments", "Amanat Architect", "Andrew Cheung Architects Inc.",
                                "Anthem Properties Group Ltd.", "Aoyuan Group", "Aplin & Martin Consultants Ltd.", "Appia Developments Ltd.",
                                "Aquilini Investment Group", "Arcadis IBI Group", "Architecture 49", "Argonne Development",
                                "Arno Matis Architecture", "Ascentia Properties Ltd.", "ATA Architectural Design Ltd.",
                                "Atira Property Management Inc.", "AtLRG Architecture Inc.", "AviSina Properties Ltd.", "Avkon Construction Ltd.",
                                "Axiom Architecture", "Basiala Investment Ltd.", "Beckwith Development Ltd.", "BFA Studio Architects",
                                "Bingham Hill Architects", "Bogner Development Group", "Boldwing Continuum Architects Inc.", "Boffo Properties",
                                "Boniface Oleksiuk Politano Architects", "Bosa Development Corp.", "Bostner Holdings Inc.",
                                "Bucci Developments Ltd.", "Buttjes Architecture Inc.", "Calling Ministries",
                                "Canderel Pacific Management Inc.", "Canlux Development Ltd.", "Canterra Construction Ltd.",
                                "Carrier Johnson+Culture", "Cast Development LLC", "CDM Properties Ltd.", "CEI Architecture",
                                "Century Group", "Chris Dikeakos Architects Inc.", "Christopher Bozyk Architects", "Chunghwa Investment",
                                "Ciccozzi Architecture Inc.", "Cielle Properties", "Coast Development", "Colliers International",
                                "Compass Cohousing Ltd.", "Conwest Group of Companies", "Coromandel Properties Ltd.", "Creus Engineering",
                                "Cristina Oberti Interior Design", "Crystal Consulting Group of Companies", "DA Architects + Planners",
                                "David Stoyko Landscape Architecture", "Desert Properties Inc.", "DF Architecture", "DIALOG", "Domus Homes",
                                "Drift Project Management Ltd.", "DYS Architecture", "Elevate Development Corp", "Embassy Bosa Inc.",
                                "Emerge Modular", "Encore Projects Inc.", "Enduro Construction", "Evantra Developments", "Executive Group",
                                "Farzin Yadegari Architect", "Ferrario Investment Group", "Focus Architecture", "Formosis Architecture",
                                "Francl Architecture Inc.", "Frank Lin Management Ltd.", "GBL Architects Inc.", "Gerry Blonski Architect",
                                "GGA - Architecture", "Greystar Development", "GWA Architecture", "GWL Realty Advisors Inc.",
                                "Harbourview Projects Corp.", "Headwater Projects", "Heatherbrae Builders", "Holborn Properties Ltd.",
                                "Hudson Projects", "Hungerford Properties", "Imani Development Inc.", "Inspira Development Ltd.",
                                "Integral Group", "Intracorp Projects Ltd.", "Isle of Mann Property Group", "ITC Construction Group",
                                "Jadasi Development Corp.", "Jim Pattison Group", "Joe Newell Architect", "Kanin Construction",
                                "Katyal Investment Group Inc.", "Kelson Group", "Kerkhoff Group", "Kevington Building Corp",
                                "Kwan Developments Ltd.", "Landmark Premier Properties", "Landa Global Properties Ltd.",
                                "Larco Investments Ltd.", "Lark Group", "Ledcor Construction Inc.", "Ledingham McAllister",
                                "Listraor Group of Companies", "Local Practice Architecture", "Loon Properties Inc.",
                                "Lorval Development Ltd.", "Lyndan Properties Ltd.", "M2 Architecture Inc.", "M3 Development",
                                "MacLean Homes Ltd.", "Mallen Gowing Berzins Arch", "Mansouri Group", "Maple Leaf Property",
                                "Marcon Group of Companies", "Martinez + Cutri Urban Studio Corporation", "Martini Construction Ltd.",
                                "Maxlite Manufacturing Ltd.", "Medcorp Construction Inc.", "Menzies Development Corporation",
                                "Metal Building Group", "Millennium Group", "Milori Homes", "Minoru Square Development", "Mission Group",
                                "Moli Industries Ltd.", "Mondiale Developments Ltd.", "Montgomery Sisam Architects Inc.", "Morrison Group",
                                "Mortise Group of Companies", "Mosaic Homes", "Multiland Pacific Holdings", "Mundi Construction Ltd.",
                                "Murfey Company", "MVE + Partners", "Nemetz & Associates Ltd.", "Nicola Wealth Real Estate",
                                "Niradia Group of Companies", "Nonni Property Group", "North America Home Finance", "NSDA Architects",
                                "Nu Development Solutions", "Oakfield Consulting Limited", "Ocean Gate Developments",
                                "OctoberNine Capital Inc.", "Oksfeldt Developments", "Onni Contracting Ltd.", "OpenRoad Auto Group",
                                "Orbis Architecture Inc.", "Orca West Developments Ltd.", "OrrMoniz Projects Corp.", "Oviedo Developments",
                                "Pacific Coast Architecture", "Pagliaro Projects Ltd.", "Pennyfarthing Development Corporation",
                                "Peak Towers Development", "Performance Builders Ltd.", "Perkins + Will", "Peterson Group",
                                "Pinnacle International", "Placemaker Group of Companies", "Polygon Development Ltd.", "Pontem Group",
                                "Porte Realty Ltd.", "Primex Investments Ltd.", "Quadra Homes", "QuadReal Property Group",
                                "Quantum Properties Inc.", "Quorum Group", "Qwid Consulting Inc.", "Rafii Architects Inc.",
                                "RaiChu Development Group Ltd.", "RBI Group of Companies", "Redbrick Properties Inc.",
                                "Redekop Ferrario Properties", "RedM Group", "Regent International Developments Ltd.", "Reimer Group",
                                "Ridge North America", "Rize Alliance Properties Ltd.", "RLA Architects Inc.",
                                "Rositch Hemphill Architects RHA", "Rowan Williams Davies & Irwin RWDI", "Ryan Murphy Construction Inc.",
                                "Saadat Enterprises Inc.", "Sacred Waters Developments", "Safari Capital Investments Ltd.",
                                "Sakura Developments", "Sasco Contractors Ltd.", "Shift Architecture", "Siddoo Holdings",
                                "Sightline Properties", "Solaris Properties Inc.", "Solterra Development Corp.", "Spring Properties",
                                "Stantec", "Starlight Developments", "Station One Architects", "Stephen Dalton Architects",
                                "Strand Development Corp.", "Stream Property Partners Inc.", "Studio One Architecture Inc.",
                                "Suffolk Construction", "Sumas Environmental Services Inc.", "Syncor Solutions Ltd.",
                                "Syncra Construction Corp.", "T.Moscone & Bros Landscape Contractors Ltd.", "TANNERHECHT Architecture",
                                "Tangerine Developments Ltd.", "Tannin Developments", "Taylor Kurtz Architecture + Design Inc.",
                                "Terra-Peak Contracting Ltd.", "The Mortgage Group", "Third Space Properties Inc.",
                                "Thind Properties Ltd.", "Three Dog Ventures Ltd.", "TKA+D Architecture + Design Inc.",
                                "TL Housing Solutions Ltd.", "TL Regent Partnership", "TM Crest Homes", "Tobem Projects",
                                "Turnbull Construction Services Inc.", "Unibuild Construction Management Ltd.",
                                "Unicorn Properties Ltd.", "Unimet Investments Ltd.", "Union Gospel Mission", "Urban Arts Architecture",
                                "Urban Coast Developments", "Vancouver Pacific Development Corp.", "VanMar Constructors Inc.",
                                "Ventana Construction Corp.", "Vincon Construction LTD.", "VPAC Construction",
                                "W. T. Leung Architects Inc.", "Wales McLelland Construction", "WA Architects", "Wesbild Holdings Ltd.",
                                "Wesgroup Properties LP", "Westland Corp.", "WestStone Holdings Ltd.", "Westgate Pacific Construction",
                                "Wiebe Group of Companies", "Wilden Group", "Will McKay and Co.", "Yamamoto Architecture",
                                "Yazdi Enterprises Inc.", "Yuan Yung Buddhism Centre", "Yushi Investments Inc.",
                                "Zako Development Inc.", "Zeidler Architecture Inc.", "Zemcore Group Ltd.", "Zen Family Holding Inc."
                            }
                        },
                        new() { BlockType = Kor.Operations.Core.Models.Brochure.BrochureBlockType.Contact }
                    }
                }
            };
        }

        private static BrochureProposal CreateSeedVariant(
            BrochureProposal baseProposal,
            string id,
            string name,
            string templateName,
            string layoutId = "standard-portfolio")
        {
            var clone = JsonSerializer.Deserialize<BrochureProposal>(
                JsonSerializer.Serialize(baseProposal, SeedProposalJsonOptions),
                SeedProposalJsonOptions) ?? new BrochureProposal();
            clone.Id = id;
            clone.Name = name;
            clone.CreatedAt = DateTime.UtcNow;
            clone.ModifiedAt = DateTime.UtcNow;
            clone.Content.TemplateName = templateName;
            clone.Content.SkinId = BrochureSkinRegistry.Resolve(null, templateName).Id;
            clone.Content.LayoutTemplateId = layoutId;
            clone.Content.CoverTitle = $"KOR {templateName}";
            return clone;
        }

        private static string ResolveLayoutTemplateId(string? displayName) =>
            BrochureLayoutTemplateCatalog.Default.All
                .FirstOrDefault(t => t.DisplayName == displayName)?.Id
            ?? "standard-portfolio";

        private BrochureContent BuildBrochureContent() => new()
        {
            TemplateName = Cover.TemplateName,
            SkinId = Cover.SkinId,
            LayoutTemplateId = Cover.LayoutTemplateId,
            CoverTitle = Cover.CoverTitle,
            CoverPhotoPath = Cover.CoverPhotoPath,
            CoverPhotoBytes = Cover.CoverPhotoBytes,
            CoverPhotoOpacity = Cover.CoverPhotoOpacity,
            PrimaryColorOverride = Cover.PrimaryColorOverride,
            AccentColorOverride = Cover.AccentColorOverride,
            ContactConfig = _contactConfig,
            Blocks = Blocks.Select(block => new BrochureBlock
            {
                BlockType = block.BlockType,
                Section = block.BlockType == BrochureBlockType.Section && block.Section is not null
                    ? new BrochureSection
                    {
                        Heading = block.Section.Heading,
                        Blurb = block.Section.Blurb,
                        Projects = new System.Collections.ObjectModel.ObservableCollection<BrochureProject>(block.Section.Projects),
                        PageBreakAfterProjectIndex = block.Section.PageBreakAfterProjectIndex.ToList()
                    }
                    : null,
                People = new System.Collections.ObjectModel.ObservableCollection<BrochurePerson>(block.People),
                PersonnelHeading = block.PersonnelHeading,
                PersonnelBlurb = block.PersonnelBlurb,
                OverviewSections = new System.Collections.ObjectModel.ObservableCollection<BrochureOverviewSection>(block.OverviewSections.Select(static s => new BrochureOverviewSection
                {
                    Heading = s.Heading,
                    Body = s.Body
                })),
                PageBreakAfterOverviewIndex = block.PageBreakAfterOverviewIndex.ToList(),
                ClientListHeading = block.ClientListHeading,
                ClientListPreamble = block.ClientListPreamble,
                ClientNames = new System.Collections.ObjectModel.ObservableCollection<string>(block.ClientNames),
                ClientListNote = block.ClientListNote
            }).ToList()
        };

        private void LoadFromProposal(BrochureProposal proposal, bool asClone)
        {
            var content = proposal.Content;

            ClearProjectForm();
            Person.ClearForm();
            Overview.ClearSectionForm();
            Overview.ClearOverviewForm();
            SelectedBlockIndex = -1;
            SelectedProjectIndex = -1;
            SelectedOverviewIndex = -1;
            IsEditingOverview = false;
            PreviewPages.Clear();

            Blocks.Clear();
            _selectedSection = null;
            _selectedSectionBlock = null;
            OnPropertyChanged(nameof(SelectedSection));
            OnPropertyChanged(nameof(CanAddProjectToSection));

            _suppressSetupPreviewRefresh = true;
            Cover.TemplateName = string.IsNullOrEmpty(content.TemplateName) ? TemplateOptions[0] : content.TemplateName;
            Cover.SkinId = string.IsNullOrEmpty(content.SkinId)
                ? BrochureSkinRegistry.Resolve(null, content.TemplateName).Id
                : content.SkinId;
            Cover.LayoutTemplateId = string.IsNullOrEmpty(content.LayoutTemplateId)
                ? "standard-portfolio"
                : content.LayoutTemplateId;
            OnPropertyChanged(nameof(SelectedSkinDisplayName));
            OnPropertyChanged(nameof(SelectedLayoutDisplayName));
            Cover.CoverTitle = content.CoverTitle;
            Cover.CoverPhotoPath = content.CoverPhotoPath;
            Cover.CoverPhotoBytes = content.CoverPhotoBytes ?? Array.Empty<byte>();
            Cover.CoverPhotoOpacity = content.CoverPhotoOpacity;
            Cover.PrimaryColorOverride = content.PrimaryColorOverride;
            Cover.AccentColorOverride = content.AccentColorOverride;
            _contactConfig = content.ContactConfig ?? _contactStore.Load();
            OnPropertyChanged(nameof(ContactConfig));
            _suppressSetupPreviewRefresh = false;
            QueueSetupPreviewRefresh();

            foreach (var block in content.Blocks)
                Blocks.Add(block);

            _proposalId = asClone ? null : proposal.Id;
            ProposalName = asClone ? proposal.Name + " (Copy)" : proposal.Name;

            CurrentStep = 1;
            ClearDirty();
            WarnAboutMissingPhotos(content);
        }

        private static void WarnAboutMissingPhotos(BrochureContent content)
        {
            var missing = new List<string>();

            if ((content.CoverPhotoBytes?.Length ?? 0) == 0 &&
                !string.IsNullOrWhiteSpace(content.CoverPhotoPath) &&
                !File.Exists(content.CoverPhotoPath))
                missing.Add($"Cover photo: {Path.GetFileName(content.CoverPhotoPath)}");

            foreach (var block in content.Blocks)
            {
                if (block.BlockType == BrochureBlockType.Section && block.Section is not null)
                {
                    foreach (var project in block.Section.Projects)
                    foreach (var photo in project.Photos)
                        if ((photo.ImageBytes?.Length ?? 0) == 0 &&
                            !string.IsNullOrWhiteSpace(photo.FilePath) &&
                            !File.Exists(photo.FilePath))
                            missing.Add($"{project.ProjectName}: {Path.GetFileName(photo.FilePath)}");
                }
                else if (block.BlockType == BrochureBlockType.Personnel)
                {
                    foreach (var person in block.People)
                        if ((person.PhotoBytes?.Length ?? 0) == 0 &&
                            !string.IsNullOrWhiteSpace(person.PhotoPath) &&
                            !File.Exists(person.PhotoPath))
                            missing.Add($"{person.Name}: {Path.GetFileName(person.PhotoPath)}");
                }
            }

            if (missing.Count == 0) return;

            MessageBox.Show(
                "The following photos could not be found and will not appear in the brochure:\n\n"
                + string.Join("\n", missing),
                "Brochure Builder — Missing Photos",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private static string SanitizeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "Brochure" : sanitized;
        }
    }
}


