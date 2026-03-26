#nullable enable
using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;

namespace Kor.Operations
{
    internal sealed class SingleInstanceGuard : IDisposable
    {
        private const string MutexName = "Kor.Transmittals.SingleInstance.Mutex";
        private const string PipeName = "Kor.Transmittals.NamedPipe";
        private readonly Mutex? _mutex;

        internal SingleInstanceGuard(string[] args)
        {
            ParticipatesInSingleInstance = !IsDedicatedShortLivedMode(args);
            if (!ParticipatesInSingleInstance)
            {
                IsFirstInstance = true;
                return;
            }

            _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out var isNew);
            IsFirstInstance = isNew;
        }

        internal bool ParticipatesInSingleInstance { get; }

        public bool IsFirstInstance { get; }

        public void ForwardArgsAndExit(string[] args)
        {
            if (args.Length == 0)
                return;

            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(8000);

                using var writer = new StreamWriter(client, Encoding.UTF8, 1024, leaveOpen: false)
                {
                    AutoFlush = true
                };

                writer.WriteLine(string.Join("|", args));
            }
            catch
            {
                // best-effort only; never crash on startup
            }
        }

        public void Dispose()
        {
            try
            {
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
            }
            catch
            {
                // ignore
            }
        }

        private static bool IsDedicatedShortLivedMode(string[] args)
        {
            var pickerMode = args.Any(a => string.Equals(a, CliArgs.FilePicker, StringComparison.OrdinalIgnoreCase));
            var fileEmailsMode = args.Any(a => a.StartsWith(CliArgs.FileEmails, StringComparison.OrdinalIgnoreCase));
            var quickTransferMode = args.Any(a => a.StartsWith(CliArgs.QuickTransfer, StringComparison.OrdinalIgnoreCase));
            return pickerMode || fileEmailsMode || quickTransferMode;
        }
    }
}
