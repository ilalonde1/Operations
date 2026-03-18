#nullable enable

namespace Kor.Operations.Financials;

public sealed class FactRow
{
    public string Key { get; }
    public string Value { get; }
    public FactRow(string key, string value)
    {
        Key = key ?? string.Empty;
        Value = value ?? string.Empty;
    }
}
