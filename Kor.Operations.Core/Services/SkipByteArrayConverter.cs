#nullable enable
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kor.Operations.Core.Services
{
    /// <summary>
    /// Reads byte arrays as empty and writes them as base64. Used when loading a
    /// proposal list where embedded images would balloon memory for no benefit —
    /// see <c>SqlJsonStore</c>'s list options.
    /// </summary>
    public sealed class SkipByteArrayConverter : JsonConverter<byte[]>
    {
        public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Skip();
            return Array.Empty<byte>();
        }

        public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
            => writer.WriteBase64StringValue(value);
    }
}
