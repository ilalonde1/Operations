#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations
{
    internal sealed class Debouncer
    {
        private readonly TimeSpan _delay;
        private int _version;

        public Debouncer(TimeSpan delay) => _delay = delay;

        public void Run(Func<Task> action)
        {
            var current = Interlocked.Increment(ref _version);
            _ = Task.Run(async () =>
            {
                await Task.Delay(_delay).ConfigureAwait(false);
                if (current != _version) return;
                await action().ConfigureAwait(false);
            });
        }
    }
}
