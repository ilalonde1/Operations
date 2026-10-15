namespace Kor.Opportunities.Knowledge.Readers;

public static class TokenEstimate
{
    /// <summary>JoeBrain's chars / 4 estimate, rounded up: a budget, not a tokenizer count.</summary>
    public static int Of(string text) => (text.Length + 3) / 4;
}
