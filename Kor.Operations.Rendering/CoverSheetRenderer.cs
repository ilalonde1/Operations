using Kor.Operations.Core; 
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
namespace Kor.Operations.Rendering
{
    public static class CoverSheetRenderer
    {
        private const string BrandNavy = "#3F5364";
        private const string BrandOrange = "#FF5B35";

        private const string FooterLine1 =
            "KOR Structural • 501 - 510 Burrard Street, Vancouver, BC V6C 3A8";
        private const string FooterLine2 =
            "contact@korstructural.com • www.korstructural.com";

        // for diagnostics
        public static string? LastLogoSource { get; private set; }

        private static readonly string? _brandFontFamily;

        static CoverSheetRenderer()
        {
            try
            {
                QuestPDF.Settings.License = LicenseType.Community;
                _brandFontFamily = TryRegisterMulishFontsSafely();
            }
            catch (Exception ex)
            {
                // Best-effort log so we can see any future failures on non-dev machines
                try
                {
                    var logPath = Path.Combine(AppContext.BaseDirectory, "CoverSheetRenderer.error.txt");

                    var lines = new[]
                    {
                        $"Timestamp: {DateTime.UtcNow:O}",
                        $"Exception: {ex}",
                        "",
                        "=== Environment snapshot ===",
                        $"AppContext.BaseDirectory: {AppContext.BaseDirectory}",
                        $"Environment.CurrentDirectory: {Environment.CurrentDirectory}",
                        $"Process.MainModule: {System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName}",
                        $"OS: {Environment.OSVersion}",
                        $".NET Runtime: {RuntimeInformation.FrameworkDescription}",
                        "",
                        "Env vars:",
                        $"  TEMP={Environment.GetEnvironmentVariable("TEMP")}",
                        $"  TMP={Environment.GetEnvironmentVariable("TMP")}",
                        $"  LOCALAPPDATA={Environment.GetEnvironmentVariable("LOCALAPPDATA")}",
                        $"  APPDATA={Environment.GetEnvironmentVariable("APPDATA")}",
                    };

                    File.WriteAllLines(logPath, lines);
                }
                catch
                {
                    // swallow logging issues
                }

                // preserve original behaviour (type initializer still fails)
                throw;
            }
        }

        public static async Task RenderAsync(string outputPath, Transmittal header, IReadOnlyList<TransmittalFile> files)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path is required.", nameof(outputPath));
            if (header is null) throw new ArgumentNullException(nameof(header));
            if (files is null) throw new ArgumentNullException(nameof(files));

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            await Task.Run(() =>
            {
                var (logoBytes, logoSrc) = TryLoadLogoBytes();
                LastLogoSource = logoSrc;

                var document = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.Letter);
                        page.Margin(36);

                        // Standardize to 11pt everywhere
                        page.DefaultTextStyle(s =>
                        {
                            var t = s.FontSize(11);
                            if (!string.IsNullOrWhiteSpace(_brandFontFamily))
                                t = t.FontFamily(_brandFontFamily);
                            return t;
                        });

                        page.Header().Element(c => BuildHeader(c, header, logoBytes));

                        page.Content().Element(c =>
                            c.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(16)
                             .Element(inner => BuildBody(inner, header, files)));

                        page.Footer().Element(c => BuildFooter(c));
                    });
                });

                using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                document.GeneratePdf(fs);
                fs.Flush(true);
            });
        }

        // ======================  Layout  ======================

        private static void BuildHeader(IContainer c, Transmittal h, byte[]? logoBytes)
        {
            c.Column(col =>
            {
                col.Item()
                   .Background(BrandNavy)
                   .Padding(16)
                   .Row(row =>
                   {
                       row.RelativeItem().Row(r2 =>
                       {
                           if (logoBytes is not null && logoBytes.Length > 0)
                               r2.ConstantItem(56).PaddingRight(12).AlignMiddle().Image(logoBytes);

                           r2.RelativeItem().AlignMiddle().Column(text =>
                           {
                               text.Item().Text("KOR Transmittals")
                                   .FontSize(18).SemiBold().FontColor(Colors.White);

                               var displayNo = AdjustTransmittalNoForPacific(h.TransmittalNo);
                               text.Item().Text(displayNo)
                                   .FontSize(10).Light().FontColor(Colors.White);
                           });
                       });

                       row.ConstantItem(260).AlignRight().Column(meta =>
                       {
                           meta.Item().Text(t =>
                           {
                               t.Span("Date ").Light().FontColor(Colors.White);
                               t.Span(GetPacificDate(h).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                                .SemiBold().FontColor(Colors.White);
                           });
                       });
                   });

                col.Item().PaddingTop(10).Row(r =>
                {
                    r.RelativeItem();
                    r.ConstantItem(80).Height(3).Background(BrandOrange);
                });
            });
        }

        private static void BuildBody(IContainer c, Transmittal h, IReadOnlyList<TransmittalFile> files)
        {
            // NEW: populate bookmarks only for Site Instructions, PDFs only.
            if (ShouldExtractBookmarks(h))
            {
                foreach (var f in files)
                {
                    // Prefer explicit FileName, fall back to LocalPath for extension detection
                    var nameOrPath = !string.IsNullOrWhiteSpace(f.FileName)
                        ? f.FileName
                        : f.LocalPath;

                    if (string.IsNullOrWhiteSpace(nameOrPath))
                        continue;

                    var ext = Path.GetExtension(nameOrPath);
                    if (!string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var localPath = f.LocalPath;
                    if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
                        continue;

                    var bookmarks = PdfBookmarkExtractor.TryGetBookmarks(localPath);
                    if (bookmarks.Count > 0)
                    {
                        // Uses existing property on TransmittalFile; safe to overwrite in-memory only.
                        f.PdfBookmarks = bookmarks;
                    }
                }
            }

            c.Column(col =>
            {
                col.Item().Element(e => BuildIntro(e, h));

                if (!string.IsNullOrWhiteSpace(h.Remarks))
                {
                    col.Item().PaddingTop(12).Text("Remarks").SemiBold();
                    col.Item().Text(h.Remarks);
                }

                var fromLine = BuildFromLine(h);
                if (!string.IsNullOrWhiteSpace(fromLine))
                {
                    col.Item().PaddingTop(12).Element(e => SectionHeader(e, "From"));
                    col.Item().Text(fromLine);
                }

                var recipients = ResolveToCc(h);
                var toList = recipients.To;
                var ccList = recipients.Cc;

                if (toList.Count > 0)
                {
                    col.Item().PaddingTop(12).Element(e => SectionHeader(e, "To"));
                    col.Item().Column(ccol =>
                    {
                        foreach (var addr in toList)
                            ccol.Item().Text(addr);
                    });
                }

                if (ccList.Count > 0)
                {
                    col.Item().PaddingTop(12).Element(e => SectionHeader(e, "CC"));
                    col.Item().Column(ccol =>
                    {
                        foreach (var addr in ccList)
                            ccol.Item().Text(addr);
                    });
                }

                if (files.Count > 0)
                {
                    col.Item().PaddingTop(14).Table(t =>
                    {
                        t.ColumnsDefinition(cd =>
                        {
                            cd.RelativeColumn(7); // content
                            cd.RelativeColumn(1); // size (narrower)
                        });

                        t.Header(hd =>
                        {
                            hd.Cell().Element(HeaderCellTight).Text("Transmitted Files").SemiBold();
                            hd.Cell().Element(HeaderCellTight).AlignRight().Text("Size").SemiBold();
                        });

                        foreach (var f in files)
                        {
                            var name = string.IsNullOrWhiteSpace(f.FileName)
                                ? Path.GetFileName(f.LocalPath)
                                : f.FileName;

                            // UPDATED: file name + optional bookmarks in a stacked column
                            t.Cell().Element(BodyCell).Column(colFiles =>
                            {
                                // File name (original behaviour)
                                colFiles.Item().Text(name ?? string.Empty);

                                // Bookmarks + optional notes (only for Site Instructions PDFs)
                                // Bookmarks + optional notes (only for Site Instructions PDFs)
                                if (f.PdfBookmarks != null &&
                                    f.PdfBookmarks.Count > 0 &&
                                    ShouldExtractBookmarks(h))
                                {
                                    colFiles.Item()
                                        .PaddingTop(2)
                                        .Text(text =>
                                        {
                                            // Show a reasonable number so the cover sheet doesn't explode
                                            var bookmarksToShow = f.PdfBookmarks;

                                            // Notes list aligns by index with PdfBookmarks
                                            var notes = f.PdfBookmarkNotes ?? new List<string>();

                                            for (int i = 0; i < bookmarksToShow.Count; i++)
                                            {
                                                var bm = bookmarksToShow[i];
                                                string? note = (i < notes.Count) ? notes[i] : null;

                                                // 1) Bullet line with the bookmark title
                                                text.Line("• " + bm).FontSize(9);

                                                // 2) Optional note on its own line, indented and smaller
                                                if (!string.IsNullOrWhiteSpace(note))
                                                {
                                                    text.Line("   " + note)
                                                        .FontSize(8)
                                                        .Italic()
                                                        .FontColor(Colors.Grey.Darken2);
                                                }
                                            }
                                        });
                                }

                            });

                            // Size column – unchanged
                            t.Cell().Element(BodyCell).AlignRight().Text(FormatBytes(f.SizeBytes));
                        }
                    });
                }
            });
        }


        private static void BuildFooter(IContainer c)
        {
            c.Column(col =>
            {
                col.Item()
                   .BorderTop(1).BorderColor(Colors.Grey.Lighten2)
                   .Row(r =>
                   {
                       r.ConstantItem(80).Height(3).Background(BrandOrange);
                       r.RelativeItem();
                   });

                col.Item()
                   .DefaultTextStyle(s => s.FontSize(9).Light().FontColor(Colors.Grey.Darken1))
                   .Row(row =>
                   {
                       row.RelativeItem().Column(left =>
                       {
                           left.Item().Text(FooterLine1);
                           left.Item().Text(FooterLine2);
                       });

                       row.ConstantItem(110)
                          .AlignRight()
                          .Text(t =>
                          {
                              t.Span("Page ");
                              t.CurrentPageNumber();
                              t.Span(" / ");
                              t.TotalPages();
                          });
                   });
            });
        }

        // ======================  Blocks  ======================

        private static void BuildIntro(IContainer c, Transmittal h)
        {
            c.Text(t =>
            {
                t.Span("Subject: ").SemiBold();
                t.Span(string.IsNullOrWhiteSpace(h.Subject) ? "-" : h.Subject);
                t.Line("");

                var projLine = string.Join(" · ",
                    new[] { Safe(h.ProjectNumber), Safe(h.ProjectName) }
                        .Where(x => !string.IsNullOrWhiteSpace(x) && x != "-"));
                t.Span("Project: ").SemiBold();
                t.Span(string.IsNullOrWhiteSpace(projLine) ? "-" : projLine);
                t.Line("");

                t.Span("Date: ").SemiBold();
                var pacificDate = GetPacificDate(h); // handles DST
                t.Span(pacificDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                t.Line("");

                t.Span("Send Via: ").SemiBold();
                var sendViaStr = string.IsNullOrWhiteSpace(h.SendVia) ? "SharePoint" : h.SendVia;
                t.Span(sendViaStr);
                t.Line("");

                if (!string.IsNullOrWhiteSpace(h.Purpose))
                {
                    t.Span("Purpose: ").SemiBold();
                    t.Span(h.Purpose);
                }
            });
        }

        // ======================  Helpers ======================

        private static void SectionHeader(IContainer c, string label) =>
            c.Background(Colors.Grey.Lighten3)
             .PaddingVertical(4)
             .PaddingHorizontal(6)
             .BorderBottom(1).BorderColor(Colors.Grey.Medium)
             .Text(label).SemiBold();

        private static IContainer HeaderCellTight(IContainer c) =>
            c.Background(Colors.Grey.Lighten3)
             .PaddingVertical(4)
             .PaddingHorizontal(6)
             .BorderBottom(1).BorderColor(Colors.Grey.Medium);

        private static IContainer BodyCell(IContainer c) =>
            c.Padding(6).BorderBottom(1).BorderColor(Colors.Grey.Lighten3);

        private static string Safe(string? s) =>
            string.IsNullOrWhiteSpace(s) ? "-" : s.Trim();

        private static string FormatBytes(long bytes)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return $"{v:0.##} {u[i]}";
        }

        private static string BuildFromLine(Transmittal h)
        {
            var name = Safe(h.FromName);
            var email = Safe(h.FromEmail);

            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(email))
                return $"{name} <{email}>";
            if (!string.IsNullOrWhiteSpace(name))
                return name;
            if (!string.IsNullOrWhiteSpace(email))
                return email;

            return string.Empty;
        }

        private static (List<string> To, List<string> Cc) ResolveToCc(Transmittal h)
        {
            var to = new List<string>();
            var cc = new List<string>();

            if (h.ToRecipients is { Count: > 0 })
            {
                to.AddRange(h.ToRecipients.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
            }

            if (h.CcRecipients is { Count: > 0 })
            {
                cc.AddRange(h.CcRecipients.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
            }

            if (to.Count > 0 || cc.Count > 0)
                return (to, cc);

            if (h.Recipients is { Count: > 0 })
            {
                foreach (var r in h.Recipients)
                {
                    var addr = !string.IsNullOrWhiteSpace(r.Email) ? r.Email : (r.DisplayName ?? "-");
                    if (string.IsNullOrWhiteSpace(addr)) continue;

                    if (r.IsCc) cc.Add(addr);
                    else to.Add(addr);
                }
            }

            return (to, cc);
        }

        // Bookmark gating helper – keeps behaviour scoped to Site Instructions only.
        private static bool ShouldExtractBookmarks(Transmittal h)
        {
            var purpose = h.Purpose;
            if (string.IsNullOrWhiteSpace(purpose))
                return false;

            // You can add more aliases here if you ever rename the purpose.
            return purpose.Trim().Equals("Site Instructions", StringComparison.OrdinalIgnoreCase);
        }

        // ---- Logo loader ----

        private static (byte[]? bytes, string? source) TryLoadLogoBytes()
        {
            try
            {
                var p = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png");
                if (File.Exists(p))
                    return (File.ReadAllBytes(p), $"File: {p}");
            }
            catch { }

            try
            {
                var asm = typeof(CoverSheetRenderer).Assembly;
                var name = asm.GetManifestResourceNames().FirstOrDefault(rn =>
                    rn.EndsWith(".logo.png", StringComparison.OrdinalIgnoreCase) ||
                    rn.EndsWith("Assets.logo.png", StringComparison.OrdinalIgnoreCase));

                if (name != null)
                {
                    using var s = asm.GetManifestResourceStream(name);
                    if (s == null)
                        return (null, null);

                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    return (ms.ToArray(), $"Embedded Resource: {name}");
                }
            }
            catch { }

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string? name = asm.GetManifestResourceNames().FirstOrDefault(rn =>
                        rn.EndsWith(".logo.png", StringComparison.OrdinalIgnoreCase) ||
                        rn.EndsWith("Assets.logo.png", StringComparison.OrdinalIgnoreCase));

                    if (name is null) continue;

                    using var s = asm.GetManifestResourceStream(name);
                    if (s is null) continue;

                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    return (ms.ToArray(), $"Embedded Resource: {name}");
                }
            }
            catch { }

            return (null, null);
        }

        private static string? TryRegisterMulishFontsSafely()
        {
            bool any = false;

            void TryScan(string? root)
            {
                if (string.IsNullOrWhiteSpace(root)) return;

                foreach (var rel in new[] { Path.Combine("Assets", "Fonts"), "Assets" })
                {
                    var dir = Path.Combine(root, rel);
                    if (!Directory.Exists(dir)) continue;

                    foreach (var path in Directory.EnumerateFiles(dir, "*.ttf", SearchOption.TopDirectoryOnly))
                    {
                        var file = Path.GetFileName(path);
                        if (!file.Contains("Mulish", StringComparison.OrdinalIgnoreCase) &&
                            !file.StartsWith("Muli", StringComparison.OrdinalIgnoreCase))
                            continue;

                        try
                        {
                            using var fs = File.OpenRead(path);
                            FontManager.RegisterFont(fs);
                            any = true;
                        }
                        catch { }
                    }
                }
            }

            try { TryScan(AppContext.BaseDirectory); } catch { }
            try { TryScan(Path.GetDirectoryName(typeof(CoverSheetRenderer).Assembly.Location)); } catch { }

            return any ? "Mulish" : null;
        }

        // ======================  Pacific time helpers  ======================

        private static readonly TimeZoneInfo Pacific = GetPacificTimeZoneSafe();

        private static TimeZoneInfo GetPacificTimeZoneSafe()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
                else
                    return TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
            }
            catch
            {
                return TimeZoneInfo.Local;
            }
        }

        private static DateTime GetPacificDate(Transmittal h)
        {
            DateTime utc = h.DateUtc.HasValue ? h.DateUtc.Value : DateTime.UtcNow;
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, Pacific);
            return local.Date;
        }

        private static string AdjustTransmittalNoForPacific(string? transmittalNo)
        {
            if (string.IsNullOrWhiteSpace(transmittalNo))
                return "-";

            var m = Regex.Match(transmittalNo, @"-(\d{8})-(\d{6})$");
            if (!m.Success) return transmittalNo;

            var yyyymmdd = m.Groups[1].Value;
            var hhmmss = m.Groups[2].Value;

            if (!DateTime.TryParseExact(
                    yyyymmdd + hhmmss,
                    "yyyyMMddHHmmss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var utcStamp))
                return transmittalNo;

            var pacific = TimeZoneInfo.ConvertTimeFromUtc(utcStamp, Pacific);
            var localStamp = pacific.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

            return Regex.Replace(transmittalNo, @"-(\d{8})-(\d{6})$", "-" + localStamp);
        }
    }
}
