#nullable enable
using System.Configuration;
using System.Data.Odbc;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Kor.Operations.Services
{
    public sealed class DeltekHeadshotProvider
    {
        private readonly string _dsn = ConfigurationManager.AppSettings["Vp.Dsn"] ?? "Deltek";
        private readonly string _user = ConfigurationManager.AppSettings["Vp.User"] ?? "";
        private readonly string _pwd = ConfigurationManager.AppSettings["Vp.Password"] ?? "";

        private string ConnStr => $"DSN={_dsn};UID={_user};PWD={_pwd};";

        // ---- DISPLAY NAME ----
        public async Task<string?> TryGetEmployeeDisplayNameAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;

            return await Task.Run(() =>
            {
                using var conn = new OdbcConnection(ConnStr);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT TOP 1 FirstName, LastName FROM EMMain WHERE EMail = ?";
                cmd.Parameters.Add("email", OdbcType.NVarChar).Value = email;

                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;

                var first = r.IsDBNull(0) ? "" : r.GetString(0).Trim();
                var last = r.IsDBNull(1) ? "" : r.GetString(1).Trim();
                var full = $"{first} {last}".Trim();
                return string.IsNullOrWhiteSpace(full) ? null : full;
            });
        }

        // ---- PHOTO LOOKUP (EMPhoto -> fallback EMMain.EmployeePhoto) ----
        public async Task<BitmapImage?> TryGetByEmailAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;

            return await Task.Run(() =>
            {
                using var conn = new OdbcConnection(ConnStr);
                conn.Open();

                // Prefer EMPhoto.Photo
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT TOP 1 p.Photo
                         FROM EMPhoto p
                         INNER JOIN EMMain m ON m.Employee = p.Employee
                         WHERE m.EMail = ?";
                    cmd.Parameters.Add("email", OdbcType.NVarChar).Value = email;

                    var bytes = ReadBlob(cmd);
                    var bmp = BytesToBitmap(bytes);
                    if (bmp != null) return bmp;
                }

                // Fallback to EMMain.EmployeePhoto
                using (var cmd2 = conn.CreateCommand())
                {
                    cmd2.CommandText = @"SELECT TOP 1 m.EmployeePhoto FROM EMMain m WHERE m.EMail = ?";
                    cmd2.Parameters.Add("email", OdbcType.NVarChar).Value = email;

                    var bytes = ReadBlob(cmd2);
                    return BytesToBitmap(bytes);
                }
            });
        }

        // Optional light check
        public async Task<bool> EmployeeHasPhotoAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            return await Task.Run(() =>
            {
                using var conn = new OdbcConnection(ConnStr);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT TOP 1 1 FROM EMPhoto p INNER JOIN EMMain m ON m.Employee=p.Employee WHERE m.EMail = ?";
                cmd.Parameters.Add("email", OdbcType.NVarChar).Value = email;
                using var r1 = cmd.ExecuteReader();
                if (r1.Read()) return true;

                using var cmd2 = conn.CreateCommand();
                cmd2.CommandText = @"SELECT TOP 1 1 FROM EMMain WHERE EMail = ? AND EmployeePhoto IS NOT NULL";
                cmd2.Parameters.Add("email", OdbcType.NVarChar).Value = email;
                using var r2 = cmd2.ExecuteReader();
                return r2.Read();
            });
        }

        // ---- helpers ----
        private static byte[]? ReadBlob(OdbcCommand cmd)
        {
            var obj = cmd.ExecuteScalar();
            if (obj == null || obj is System.DBNull) return null;
            if (obj is byte[] arr) return arr;
            if (obj is System.Array a)
            {
                var bytes = new byte[a.Length];
                System.Buffer.BlockCopy(a, 0, bytes, 0, bytes.Length);
                return bytes;
            }
            return null;
        }

        private static BitmapImage? BytesToBitmap(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;

            using var ms = new MemoryStream(bytes, writable: false);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
    }
}
