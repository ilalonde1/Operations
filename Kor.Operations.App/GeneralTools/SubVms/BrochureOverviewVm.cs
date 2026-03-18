#nullable enable
using System.Collections.Generic;
using Kor.Operations.Core;

namespace Kor.Operations.GeneralTools.SubVms;

public sealed class BrochureOverviewVm : ObservableObject
{
    private string _overviewHeading = string.Empty;
    private string _overviewBody = string.Empty;
    private string _sectionHeading = string.Empty;
    private string _sectionBlurb = string.Empty;

    public string OverviewHeading
    {
        get => _overviewHeading;
        set => SetField(ref _overviewHeading, value);
    }

    public string OverviewBody
    {
        get => _overviewBody;
        set => SetField(ref _overviewBody, value);
    }

    public string SectionHeading
    {
        get => _sectionHeading;
        set => SetField(ref _sectionHeading, value);
    }

    public string SectionBlurb
    {
        get => _sectionBlurb;
        set => SetField(ref _sectionBlurb, value);
    }

    public void ClearOverviewForm()
    {
        OverviewHeading = string.Empty;
        OverviewBody = string.Empty;
    }

    public void ClearSectionForm()
    {
        SectionHeading = string.Empty;
        SectionBlurb = string.Empty;
    }

}
