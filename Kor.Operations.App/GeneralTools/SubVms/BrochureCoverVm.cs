#nullable enable
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Kor.Operations.GeneralTools.SubVms;

public sealed class BrochureCoverVm : INotifyPropertyChanged
{
    private string _templateName = string.Empty;
    private string _coverTitle = string.Empty;
    private string _coverPhotoPath = string.Empty;
    private float _coverPhotoOpacity = 0.85f;
    private int? _coverYear;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string TemplateName
    {
        get => _templateName;
        set => SetField(ref _templateName, value);
    }

    public string CoverTitle
    {
        get => _coverTitle;
        set => SetField(ref _coverTitle, value);
    }

    public string CoverPhotoPath
    {
        get => _coverPhotoPath;
        set => SetField(ref _coverPhotoPath, value);
    }

    public float CoverPhotoOpacity
    {
        get => _coverPhotoOpacity;
        set => SetField(ref _coverPhotoOpacity, value);
    }

    public int? CoverYear
    {
        get => _coverYear;
        set => SetField(ref _coverYear, value);
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
