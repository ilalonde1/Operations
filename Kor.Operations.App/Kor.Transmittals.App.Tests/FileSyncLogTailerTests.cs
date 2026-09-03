#nullable enable
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.FileSync;
using Xunit;

namespace Kor.Operations.App.Tests;

public sealed class FileSyncLogTailerTests
{
    [Fact]
    public async Task ReadNewAsync_returns_lines_appended_while_writer_keeps_log_open()
    {
        var dir = Path.Combine(Path.GetTempPath(), "KorFileSyncLogTailerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "filesync.log");

        try
        {
            await using var writerStream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete);
            await using var writer = new StreamWriter(writerStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true)
            {
                AutoFlush = true,
            };

            await writer.WriteLineAsync(LogLine("First open-handle line"));

            using var tailer = new FileSyncLogTailer(path);
            var firstRead = await tailer.ReadNewAsync(CancellationToken.None);
            Assert.Contains(firstRead, l => l.Message == "First open-handle line");

            await writer.WriteLineAsync(LogLine("Second open-handle line"));

            var secondRead = await tailer.ReadNewAsync(CancellationToken.None);
            Assert.Contains(secondRead, l => l.Message == "Second open-handle line");
            Assert.DoesNotContain(secondRead, l => l.Message == "First open-handle line");
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                /* best-effort temp cleanup */
            }
        }
    }

    private static string LogLine(string message) =>
        $"2026-08-21 02:15:00.000 -07:00 [INF] Kor.Operations.FileSync.Service.Tests {message}";
}
