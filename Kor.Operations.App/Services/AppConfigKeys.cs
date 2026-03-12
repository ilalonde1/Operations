#nullable enable
namespace Kor.Operations.Services
{
    internal static class AppConfigKeys
    {
        public const string DefaultFromDomain = "DefaultFromDomain";
        public const string DefaultFromEmail = "DefaultFromEmail";
        public const string DeltekOdbcDsn = "DeltekOdbcDsn";
        public const string FinancialsPnLGlFlipSign = "Financials.PnL.GlFlipSign";
        public const string GraphClientId = "Graph.ClientId";
        public const string GraphDriveId = "Graph.DriveId";
        public const string GraphTenantId = "Graph.TenantId";
        public const string ProjectsRoot = "ProjectsRoot";
        public const string RedirectorBaseUrl = "RedirectorBaseUrl";
        public const string StandardDetailsFileStorageRootPath = "StandardDetails.FileStorageRootPath";
        public const string UserUpnOverride = "UserUpnOverride";
        public const string VpDsn = "Vp.Dsn";
        public const string VpPassword = "Vp.Password";
        public const string VpUser = "Vp.User";

        internal static class ConnectionStrings
        {
            public const string KorEmailIndex = "KorEmailIndex";
            public const string KorTransmittals = "KorTransmittals";
            public const string KorTransmittalsDb = "KorTransmittalsDb";
        }
    }
}
