#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations.App.FileSync;

// Polls a Serilog rolling-file path and yields newly-appended lines.
//
// Why polling instead of FileSystemWatcher? FSW on a remote SMB share fires
// inconsistently and misses lots of write events. We poll by opening the file
// and reading Stream.Length; FileInfo metadata can be stale while the service
// is actively appending on another handle.
//
// Open mode is FileShare.ReadWrite|Delete so the service can keep writing
// (Serilog opens with the same share flags) and roll the file to a new day
// without us holding it open.
public sealed class FileSyncLogTailer : IDisposable
{
    private readonly object _gate = new();
    private string _path;
    private long _lastLength;
    private FileSyncLogLine? _lastEntry;

    public FileSyncLogTailer(string path)
    {
        _path = path;
        _lastLength = 0;
        _lastEntry = null;
    }

    public string Path
    {
        get { lock (_gate) return _path; }
    }

    // Switch to a different file (e.g. user picked a different host or date,
    // or the rolling file rolled at midnight). Resets the tail position so
    // the next ReadAsync returns the whole new file from the start.
    public void Retarget(string newPath)
    {
        lock (_gate)
        {
            _path = newPath;
            _lastLength = 0;
            _lastEntry = null;
        }
    }

    // Reads any newly-appended bytes since the last call, parses them, and
    // returns the new log entries. Returns an empty list if nothing changed
    // or the file doesn't exist (yet).
    public Task<IReadOnlyList<FileSyncLogLine>> ReadNewAsync(CancellationToken ct)
    {
        return Task.Run<IReadOnlyList<FileSyncLogLine>>(() =>
        {
            string path;
            long lastLen;
            FileSyncLogLine? carry;
            lock (_gate) { path = _path; lastLen = _lastLength; carry = _lastEntry; }

            if (!File.Exists(path))
                return Array.Empty<FileSyncLogLine>();

            using var fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var current = fs.Length;

            // File shrank -> rolled (Serilog rolling file or operator wiped
            // it). Restart from byte 0 so we don't lose context.
            if (current < lastLen)
                lastLen = 0;

            if (current == lastLen)
                return Array.Empty<FileSyncLogLine>();

            var newLines = new List<FileSyncLogLine>();
            fs.Seek(lastLen, SeekOrigin.Begin);
            using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
            FileSyncLogParser.ParseInto(sr, newLines, ref carry);

            lock (_gate)
            {
                _lastLength = current;
                _lastEntry = carry;
            }

            ct.ThrowIfCancellationRequested();
            return newLines;
        }, ct);
    }

    public void Dispose() { /* nothing to dispose; FileStreams are scoped per call */ }
}
