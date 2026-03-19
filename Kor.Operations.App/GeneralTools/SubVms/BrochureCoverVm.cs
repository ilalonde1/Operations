#nullable enable
using System.Collections.Generic;
using Kor.Operations.Core;

namespace Kor.Operations.GeneralTools.SubVms;

public sealed class BrochureCoverVm : ObservableObject
{
    private string _templateName = string.Empty;
    private string? _skinId;
    private string? _layoutTemplateId;
    private string _coverTitle = string.Empty;
    private string _coverPhotoPath = string.Empty;
    private float _coverPhotoOpacity = 0.85f;
    private string _primaryColorOverride = string.Empty;
    private string _accentColorOverride = string.Empty;

    public string TemplateName
    {
        get => _templateName;
        set => SetField(ref _templateName, value);
    }

    public string? SkinId
    {
        get => _skinId;
        set => SetField(ref _skinId, value);
    }

    public string? LayoutTemplateId
    {
        get => _layoutTemplateId;
        set => SetField(ref _layoutTemplateId, value);
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

    public string PrimaryColorOverride
    {
        get => _primaryColorOverride;
        set => SetField(ref _primaryColorOverride, value);
    }

    public string AccentColorOverride
    {
        get => _accentColorOverride;
        set => SetField(ref _accentColorOverride, value);
    }

}
