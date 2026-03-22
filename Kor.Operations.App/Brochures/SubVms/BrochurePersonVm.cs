#nullable enable
using System.Collections.Generic;
using Kor.Operations.Core;

namespace Kor.Operations.Brochures.SubVms;

public sealed class BrochurePersonVm : ObservableObject
{
    private string _personName = string.Empty;
    private string _personCredentials = string.Empty;
    private string _personBio = string.Empty;
    private string _personPhotoPath = string.Empty;

    public string PersonName
    {
        get => _personName;
        set => SetField(ref _personName, value);
    }

    public string PersonCredentials
    {
        get => _personCredentials;
        set => SetField(ref _personCredentials, value);
    }

    public string PersonBio
    {
        get => _personBio;
        set => SetField(ref _personBio, value);
    }

    public string PersonPhotoPath
    {
        get => _personPhotoPath;
        set => SetField(ref _personPhotoPath, value);
    }

    public void ClearForm()
    {
        PersonName = string.Empty;
        PersonCredentials = string.Empty;
        PersonBio = string.Empty;
        PersonPhotoPath = string.Empty;
    }

}

