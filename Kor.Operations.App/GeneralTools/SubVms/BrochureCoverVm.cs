#nullable enable
using System.Collections.Generic;
using Kor.Operations.Core;

namespace Kor.Operations.GeneralTools.SubVms;

public sealed class BrochureCoverVm : ObservableObject
{
    private string _templateName = string.Empty;
    private string _coverTitle = string.Empty;
    private string _coverPhotoPath = string.Empty;
    private float _coverPhotoOpacity = 0.85f;
    private int? _coverYear;

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

}
