using System.Xml.Linq;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Data;
using Kor.Operations.Rendering.Brochure;
using Kor.Operations.Rendering.Proposal;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Infrastructure;

var appConfigPath = @"C:\VIsual Studio Projects\Operations\Kor.Operations.App\App.config";
var brochurePdfSourcePath = @"C:\VIsual Studio Projects\Operations\Kor.Operations.App\Brochures\Kor_Structural_Corporate_Portfolio_2025-03-17.pdf";
var popplerBinPath = @"C:\poppler\Library\bin";
var feeName = "Master Fee Proposal Template KW";
var brochureName = "Kor_Structural_Corporate_Portfolio_2025-03-17";
var outputRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
    "KorOperationsSmoke");

var connectionString = ResolveConnectionString(appConfigPath);
var staffStore = new SqlProposalStaffStore(connectionString);
var feeStore = new SqlFeeProposalStore(connectionString);
var brochureStore = new SqlBrochureProposalStore(connectionString);

var staff = staffStore.LoadAll();
var fee = feeStore.LoadAll().First(x => string.Equals(x.Name, feeName, StringComparison.OrdinalIgnoreCase));
var brochure = brochureStore.LoadAll().First(x => string.Equals(x.Name, brochureName, StringComparison.OrdinalIgnoreCase));

RepairSuspiciousBrochurePhotos(brochure, brochurePdfSourcePath, popplerBinPath);
brochureStore.Save(brochure);

Directory.CreateDirectory(outputRoot);

var feePdfPath = Path.Combine(outputRoot, $"{fee.Name}.pdf");
var feeDocxPath = Path.Combine(outputRoot, $"{fee.Name}.docx");
var brochurePdfPath = Path.Combine(outputRoot, $"{brochure.Name}.pdf");

await new FeeProposalRenderer(NullLogger<FeeProposalRenderer>.Instance)
    .RenderAsync(fee, staff, feePdfPath, default);

await new FeeProposalDocxRenderer()
    .RenderAsync(fee, staff, feeDocxPath, default);

var invalidProjectPhotos = new List<string>();
foreach (var block in brochure.Content.Blocks)
{
    if (block.Section is null)
        continue;

    foreach (var project in block.Section.Projects)
    {
        for (var i = 0; i < project.Photos.Count; i++)
        {
            var photo = project.Photos[i];
            if (photo.ImageBytes is not { Length: > 0 })
                continue;

            try
            {
                Image.FromBinaryData(photo.ImageBytes);
            }
            catch
            {
                invalidProjectPhotos.Add($"{project.ProjectName} [photo {i + 1}]");
            }
        }
    }
}

if (invalidProjectPhotos.Count == 0)
{
    await new BrochureRenderer(NullLogger<BrochureRenderer>.Instance)
        .RenderAsync(brochure.Content, brochurePdfPath, default);
}
else
{
    Console.WriteLine("INVALID_BROCHURE_IMAGES");
    foreach (var item in invalidProjectPhotos)
        Console.WriteLine(item);
}

Console.WriteLine(feePdfPath);
Console.WriteLine(feeDocxPath);
Console.WriteLine(brochurePdfPath);

static void RepairSuspiciousBrochurePhotos(BrochureProposal brochure, string brochurePdfSourcePath, string popplerBinPath)
{
    foreach (var block in brochure.Content.Blocks)
    {
        if (block.Section is null)
            continue;

        foreach (var project in block.Section.Projects)
        {
            if (!LooksCorrupted(project))
                continue;

            var page = FindProjectPage(brochurePdfSourcePath, popplerBinPath, project.ProjectName);
            if (page <= 0)
                throw new InvalidOperationException($"Could not locate project page for '{project.ProjectName}'.");

            var images = GetPageImages(brochurePdfSourcePath, popplerBinPath, page);
            if (images.Count == 0)
                throw new InvalidOperationException($"No images extracted for '{project.ProjectName}' on page {page}.");

            project.Photos = images
                .Select(x => new BrochurePhoto { ImageBytes = x })
                .ToList();
        }
    }
}

static bool LooksCorrupted(BrochureProject project) =>
    project.Photos.Count > 20 ||
    project.Photos.Any(x => (x.ImageBytes?.Length ?? 0) < 100);

static int FindProjectPage(string pdfPath, string popplerBinPath, string projectName)
{
    for (var page = 1; page <= 80; page++)
    {
        var text = RunTool(
            Path.Combine(popplerBinPath, "pdftotext.exe"),
            $"-layout -f {page} -l {page} \"{pdfPath}\" -");
        if (text.Contains(projectName, StringComparison.OrdinalIgnoreCase))
            return page;
    }

    return -1;
}

static List<byte[]> GetPageImages(string pdfPath, string popplerBinPath, int page)
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "KorOperationsSmoke", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    try
    {
        var prefix = Path.Combine(tempRoot, "img");
        RunTool(
            Path.Combine(popplerBinPath, "pdfimages.exe"),
            $"-f {page} -l {page} -all \"{pdfPath}\" \"{prefix}\"");

        return Directory.GetFiles(tempRoot)
            .Where(x =>
                x.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                x.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                x.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllBytes)
            .Where(x => x.Length > 8 * 1024)
            .ToList();
    }
    finally
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, true);
    }
}

static string RunTool(string exePath, string arguments)
{
    using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
        FileName = exePath,
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    }) ?? throw new InvalidOperationException($"Failed to start {exePath}.");

    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"{Path.GetFileName(exePath)} failed: {stderr}");
    return stdout;
}

static string ResolveConnectionString(string appConfigPath)
{
    var doc = XDocument.Load(appConfigPath);
    var node = doc.Descendants("add")
        .First(x => string.Equals((string?)x.Attribute("name"), "KorTransmittalsDb", StringComparison.OrdinalIgnoreCase));
    var cs = (string?)node.Attribute("connectionString");
    if (string.IsNullOrWhiteSpace(cs))
        throw new InvalidOperationException("KorTransmittalsDb connection string not found.");

    var builder = new SqlConnectionStringBuilder(cs);
    var envUser = Environment.GetEnvironmentVariable("KOR_DB_USER");
    var envPassword = Environment.GetEnvironmentVariable("KOR_DB_PASSWORD");
    if (!string.IsNullOrWhiteSpace(envUser))
        builder["User ID"] = envUser;
    if (!string.IsNullOrWhiteSpace(envPassword))
        builder.Password = envPassword;
    return builder.ConnectionString;
}
