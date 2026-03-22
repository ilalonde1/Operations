#nullable enable
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using Kor.Operations.Core;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.Brochures.SubVms;

public sealed class BrochureProjectVm : ObservableObject
{
    private string _projectName = string.Empty;
    private string _projectDescription = string.Empty;
    private string _client = string.Empty;
    private string _architect = string.Empty;
    private ObservableCollection<BrochurePhoto> _photos = new();

    public string ProjectName
    {
        get => _projectName;
        set => SetField(ref _projectName, value);
    }

    public string ProjectDescription
    {
        get => _projectDescription;
        set => SetField(ref _projectDescription, value);
    }

    public string Client
    {
        get => _client;
        set => SetField(ref _client, value);
    }

    public string Architect
    {
        get => _architect;
        set => SetField(ref _architect, value);
    }

    public ObservableCollection<BrochurePhoto> Photos
    {
        get => _photos;
        set => SetField(ref _photos, value);
    }

    public bool ValidateForm()
    {
        if (string.IsNullOrWhiteSpace(ProjectName))
        {
            MessageBox.Show(
                "Project name is required.",
                "Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(ProjectDescription))
        {
            MessageBox.Show(
                "Project description is required.",
                "Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    public void ClearForm()
    {
        ProjectName = string.Empty;
        ProjectDescription = string.Empty;
        Client = string.Empty;
        Architect = string.Empty;
        Photos = new ObservableCollection<BrochurePhoto>();
    }

}

