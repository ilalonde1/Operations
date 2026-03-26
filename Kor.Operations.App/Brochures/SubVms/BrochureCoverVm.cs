#nullable enable
using System.Collections.Generic;
using Kor.Operations.Core;

namespace Kor.Operations.Brochures.SubVms;

public sealed class BrochureCoverVm : ObservableObject
{
    private string _templateName = string.Empty;
    private string? _skinId;
    private string? _layoutTemplateId;
    private string _coverTitle = string.Empty;
    private string _coverPhotoPath = string.Empty;
    private byte[] _coverPhotoBytes = System.Array.Empty<byte>();
    private float _coverPhotoOpacity = 0.85f;
    private string _primaryColorOverride = string.Empty;
    private string _accentColorOverride = string.Empty;

    public Action? OnChanged { get; set; }

    public string TemplateName
    {
        get => _templateName;
        set
        {
            if (SetField(ref _templateName, value))
                OnChanged?.Invoke();
        }
    }

    public string? SkinId
    {
        get => _skinId;
        set
        {
            if (SetField(ref _skinId, value))
                OnChanged?.Invoke();
        }
    }

    public string? LayoutTemplateId
    {
        get => _layoutTemplateId;
        set
        {
            if (SetField(ref _layoutTemplateId, value))
                OnChanged?.Invoke();
        }
    }

    public string CoverTitle
    {
        get => _coverTitle;
        set
        {
            if (SetField(ref _coverTitle, value))
                OnChanged?.Invoke();
        }
    }

    public string CoverPhotoPath
    {
        get => _coverPhotoPath;
        set
        {
            if (SetField(ref _coverPhotoPath, value))
            {
                OnPropertyChanged(nameof(HasCoverPhoto));
                OnChanged?.Invoke();
            }
        }
    }

    public byte[] CoverPhotoBytes
    {
        get => _coverPhotoBytes;
        set
        {
            if (SetField(ref _coverPhotoBytes, value))
            {
                OnPropertyChanged(nameof(HasCoverPhoto));
                OnChanged?.Invoke();
            }
        }
    }

    public float CoverPhotoOpacity
    {
        get => _coverPhotoOpacity;
        set
        {
            if (SetField(ref _coverPhotoOpacity, value))
                OnChanged?.Invoke();
        }
    }

    public string PrimaryColorOverride
    {
        get => _primaryColorOverride;
        set
        {
            if (SetField(ref _primaryColorOverride, value))
                OnChanged?.Invoke();
        }
    }

    public string AccentColorOverride
    {
        get => _accentColorOverride;
        set
        {
            if (SetField(ref _accentColorOverride, value))
                OnChanged?.Invoke();
        }
    }

    public bool HasCoverPhoto => !string.IsNullOrWhiteSpace(CoverPhotoPath) || CoverPhotoBytes.Length > 0;

}

