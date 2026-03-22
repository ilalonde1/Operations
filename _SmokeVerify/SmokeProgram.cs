using System.Xml.Linq;
using Kor.Operations.Data;
using Kor.Operations.Rendering.Brochure;
using Kor.Operations.Rendering.Proposal;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

var appConfigPath = @"C:\VIsual Studio Projects\Operations\Kor.Operations.App\App.config";
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

Directory.CreateDirectory(outputRoot);

var feePdfPath = Path.Combine(outputRoot, $"{fee.Name}.pdf");
var feeDocxPath = Path.Combine(outputRoot, $"{fee.Name}.docx");
var brochurePdfPath = Path.Combine(outputRoot, $"{brochure.Name}.pdf");

await new FeeProposalRenderer(NullLogger<FeeProposalRenderer>.Instance)
    .RenderAsync(fee, staff, feePdfPath, default);

await new FeeProposalDocxRenderer()
    .RenderAsync(fee, staff, feeDocxPath, default);

await new BrochureRenderer(NullLogger<BrochureRenderer>.Instance)
    .RenderAsync(brochure.Content, brochurePdfPath, default);

Console.WriteLine(feePdfPath);
Console.WriteLine(feeDocxPath);
Console.WriteLine(brochurePdfPath);

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
        builder.UserID = envUser;
    if (!string.IsNullOrWhiteSpace(envPassword))
        builder.Password = envPassword;
    return builder.ConnectionString;
}
