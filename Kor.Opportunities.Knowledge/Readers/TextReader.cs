using System.Text;

namespace Kor.Opportunities.Knowledge.Readers;

public sealed class TextReader : IReader
{
    public async Task<IReadOnlyList<Chunk>> ReadAsync(string path, CancellationToken ct = default)
        => Chunking.Split(await File.ReadAllTextAsync(path, Encoding.UTF8, ct))
            .Select(text => new Chunk(null, text)).ToArray();
}
