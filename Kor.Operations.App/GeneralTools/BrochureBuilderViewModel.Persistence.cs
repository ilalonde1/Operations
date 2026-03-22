#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Input;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Rendering.Brochure;
using Kor.Operations.Rendering.Brochure.Skins;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.GeneralTools
{
    public sealed partial class BrochureBuilderViewModel
    {
        private const string OriginalSeedProposalPath = @"GeneralTools\SeedData\Original.seed.json";
        private const string ExecutiveMinimalSeedId = "98a28b7220614e9cb3a15c15f0ac5c19";
        private const string BoldPortfolioSeedId = "31711db5f0d54c1bb4ac05380d7b8546";
        private static readonly JsonSerializerOptions SeedProposalJsonOptions = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        public ICommand SaveProposalCommand { get; private set; } = null!;
        public ICommand SaveProposalAsCommand { get; private set; } = null!;
        public ICommand LoadProposalCommand { get; private set; } = null!;
        public ICommand NewProposalCommand { get; private set; } = null!;
        public ICommand SaveContactCommand { get; private set; } = null!;

        private void InitPersistenceCommands()
        {
            SaveProposalCommand = new RelayCommand(ExecSaveProposal);
            SaveProposalAsCommand = new RelayCommand(ExecSaveProposalAs);
            LoadProposalCommand = new RelayCommand(ExecLoadProposal);
            NewProposalCommand = new RelayCommand(ExecNewProposal);
            SaveContactCommand = new RelayCommand(_ =>
            {
                _contactStore.Save(_contactConfig);
                MessageBox.Show("Contact info saved.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        private void ExecSaveProposal(object? _)
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
            _proposalStore.Save(new BrochureProposal
            {
                Id = _proposalId,
                Name = ProposalName,
                Content = BuildBrochureContent()
            });

            MessageBox.Show(
                $"\"{ProposalName}\" saved.",
                "Proposal Saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            ClearDirty();
        }

        private void ExecSaveProposalAs(object? _)
        {
            var nameDialog = new BrochureProposalNameDialog(ProposalName) { Owner = GetOwnerWindow() };
            if (nameDialog.ShowDialog() != true) return;

            ProposalName = nameDialog.ProposalName;
            _proposalId = Guid.NewGuid().ToString("N");

            _proposalStore.Save(new BrochureProposal
            {
                Id = _proposalId,
                Name = ProposalName,
                Content = BuildBrochureContent()
            });

            MessageBox.Show(
                $"\"{ProposalName}\" saved.",
                "Proposal Saved",
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
                    "Unsaved Changes",
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
                    "Unsaved Changes",
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
            Cover.CoverPhotoPath = null;
            _proposalId = null;
            ProposalName = string.Empty;
            CurrentStep = 1;
            ClearDirty();
            OnPropertyChanged(nameof(SelectedSection));
            OnPropertyChanged(nameof(CanAddProjectToSection));
        }

        private void EnsureSeedProposals()
        {
            try
            {
                var baseSeed = LoadSeedProposal();
                if (baseSeed is null) return;

                var existingIds = _proposalStore.LoadAll().Select(static p => p.Id).ToHashSet();

                if (!existingIds.Contains(baseSeed.Id))
                    _proposalStore.Save(baseSeed);

                var execVariant = CreateSeedVariant(baseSeed, ExecutiveMinimalSeedId, "Original - Executive Minimal", "Executive Minimal");
                if (!existingIds.Contains(execVariant.Id))
                    _proposalStore.Save(execVariant);

                var boldVariant = CreateSeedVariant(baseSeed, BoldPortfolioSeedId, "Original - Bold Portfolio", "Bold Portfolio");
                if (!existingIds.Contains(boldVariant.Id))
                    _proposalStore.Save(boldVariant);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to seed brochure sample proposals.");
            }
        }

        private static BrochureProposal? LoadSeedProposal()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, OriginalSeedProposalPath);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<BrochureProposal>(File.ReadAllText(path), SeedProposalJsonOptions);
        }

        private static BrochureProposal CreateSeedVariant(
            BrochureProposal baseProposal,
            string id,
            string name,
            string templateName)
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
            clone.Content.LayoutTemplateId = "standard-portfolio";
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
                        Projects = block.Section.Projects.ToList(),
                        PageBreakAfterProjectIndex = block.Section.PageBreakAfterProjectIndex.ToList()
                    }
                    : null,
                People = block.People.ToList(),
                PersonnelHeading = block.PersonnelHeading,
                PersonnelBlurb = block.PersonnelBlurb,
                OverviewSections = block.OverviewSections.Select(static s => new BrochureOverviewSection
                {
                    Heading = s.Heading,
                    Body = s.Body
                }).ToList(),
                PageBreakAfterOverviewIndex = block.PageBreakAfterOverviewIndex.ToList()
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

            if (!string.IsNullOrWhiteSpace(content.CoverPhotoPath) && !File.Exists(content.CoverPhotoPath))
                missing.Add($"Cover photo: {Path.GetFileName(content.CoverPhotoPath)}");

            foreach (var block in content.Blocks)
            {
                if (block.BlockType == BrochureBlockType.Section && block.Section is not null)
                {
                    foreach (var project in block.Section.Projects)
                    foreach (var photo in project.Photos)
                        if (!string.IsNullOrWhiteSpace(photo.FilePath) && !File.Exists(photo.FilePath))
                            missing.Add($"{project.ProjectName}: {Path.GetFileName(photo.FilePath)}");
                }
                else if (block.BlockType == BrochureBlockType.Personnel)
                {
                    foreach (var person in block.People)
                        if (!string.IsNullOrWhiteSpace(person.PhotoPath) && !File.Exists(person.PhotoPath))
                            missing.Add($"{person.Name}: {Path.GetFileName(person.PhotoPath)}");
                }
            }

            if (missing.Count == 0) return;

            MessageBox.Show(
                "The following photos could not be found and will not appear in the brochure:\n\n"
                + string.Join("\n", missing),
                "Missing Photos",
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
