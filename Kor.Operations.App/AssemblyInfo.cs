#nullable enable
using System.Reflection;
using System.Windows;
using System.Runtime.Versioning;

// This project sets GenerateAssemblyInfo=false, so the SDK does NOT synthesise version
// attributes from <Version> in the csproj -- they must live here or FileVersion stays 0.0.0.0
// (which the app's startup log reports). Date-based: YYYY.M.D. Bump on a meaningful app change.
[assembly: AssemblyVersion("2026.10.4")]
[assembly: AssemblyFileVersion("2026.10.4")]
[assembly: AssemblyInformationalVersion("2026.10.4")]

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Kor.Operations.EngineeringTools.Tests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Kor.Operations.App.Tests")]

// WPF app: Windows-only by definition (net8.0-windows). This annotation prevents noisy
// platform analyzer warnings (CA1416) for Windows-only APIs used throughout the app.
[assembly: SupportedOSPlatform("windows7.0")]

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
