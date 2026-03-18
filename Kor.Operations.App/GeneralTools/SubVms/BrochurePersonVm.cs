#nullable enable
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Kor.Operations.GeneralTools.SubVms;

public sealed class BrochurePersonVm : INotifyPropertyChanged
{
    private string _personName = string.Empty;
    private string _personCredentials = string.Empty;
    private string _personBio = string.Empty;
    private string _personPhotoPath = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
