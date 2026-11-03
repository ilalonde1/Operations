using System.Text.RegularExpressions;

namespace Kor.Operations.Framework.Capture;

/// <summary>
/// Mistake 138: a credential VALUE never reaches the brain, and never reaches a session. The F7 import carried real
/// passwords written in prose ("the actual working password for X is ...") into brain.Memory, because the only check
/// was <see cref="Redact"/>'s key-shaped markers (Password=, sk-ant-). This classifier finds the value itself:
/// <list type="bullet">
/// <item><b>kv</b> - <c>password= pwd= secret= client_secret= apikey= api_key= token= accountkey= ...</c> with no space
/// before '=' (a connection string's <c>;Password=x;</c> included), any value of 4+ characters that is not a placeholder.</item>
/// <item><b>prose</b> - a credential word (password, passphrase, pwd, pw, secret, creds, credential(s), api/access/account/
/// secret/private key, token) then within 60 characters on the same line a separator (is, are, was, were, set to,
/// changed to, ':', '=', '-&gt;', '=&gt;'), and in the next three words a token of 6+ characters of 3+ character classes
/// (lower, upper, digit, other), or 16+ characters with a digit or symbol and Shannon entropy of 3.5 bits or more.</item>
/// <item><b>prefix</b> - sk-ant-, ghp_/gho_/ghu_/ghs_/ghr_, github_pat_, xoxb-/xoxp-..., AKIA/ASIA + 16.</item>
/// <item><b>azure</b> - an Azure AD client secret's shape: 34-40 of [A-Za-z0-9_~.-] holding a '~', a letter and a digit.</item>
/// <item><b>bearer</b> - Bearer + 16 or more token characters. <b>cli</b> - <c>sqlcmd|osql|bcp ... -P value</c>.</item>
/// </list>
/// NOT a value (never refused): a rule ABOUT a credential ("never print the sa password"), an env var NAME
/// (KOR_DB_PASSWORD), a placeholder (&lt;pw&gt;, {pw}, $PW, %PW%, $env:PW, ****, xxxx, ..., [REDACTED...], ChangeMe and other
/// placeholder words without a digit), a GUID, hex (a sha), a path or URL, a file name, a dotted identifier or a call
/// (<c>args.Password</c>, <c>GetPassword()</c>).
/// Same-class values it does NOT catch: a password of under 6 characters or of fewer than 3 classes written in prose
/// ("the password is summer2026"); a value after a word it does not know ("the passcode is ..."); a value in a dotted or
/// file-name shape ("Summer.Time"); a value split over two lines; a credential with no word near it at all.
/// </summary>
public static class SecretGuard
{
    /// <summary>One value found: where it is (0-based), how long, and which rule found it. Never the value.</summary>
    public sealed record Hit(int Index, int Length, string Rule);

    private const RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex Kv = new(
        @"(?<![A-Za-z0-9_])(?:password|passwd|pwd|secret|client_secret|clientsecret|client-secret|apikey|api_key|api-key|x-api-key|" +
        @"token|access_token|refresh_token|auth_token|accountkey|sharedaccesskey|sas_token|private_key)=" +
        @"(?<v>""[^""\n]*""|'[^'\n]*'|[^\s;""'&,)<>\]]+)", I);

    private static readonly Regex ProseWord = new(
        // a whole word: not inside an identifier (KOR_APOLLO_APIKEY is a NAME - live brain #683, 3 Nov)
        @"(?<![A-Za-z0-9_])(?:passwords?|passwd|passphrase|pwd|pw|secrets?|client[ _-]secret|creds|credentials?|" +
        @"api[ _-]?key|access[ _-]?key|account[ _-]?key|secret[ _-]?key|private[ _-]?key|tokens?)(?![A-Za-z0-9_])", I);

    private static readonly Regex Separator = new(
        @"\s(?:is|are|was|were|set to|reset to|changed to)\s+|\s*(?::|=|->|=>|→)\s*", I);

    private static readonly Regex Prefix = new(
        @"(?<![A-Za-z0-9])(?:sk-ant-(?<v>[A-Za-z0-9_\-]{10,})|gh[pousr]_(?<v>[A-Za-z0-9]{20,})|github_pat_(?<v>[A-Za-z0-9_]{20,})|" +
        @"xox[abposr]-(?<v>[A-Za-z0-9\-]{10,}))|(?<![A-Z0-9])(?:AKIA|ASIA)(?<v>[0-9A-Z]{16})(?![A-Z0-9])", RegexOptions.CultureInvariant);

    private static readonly Regex Azure = new(@"(?<![A-Za-z0-9_~.\-])[A-Za-z0-9_~.\-]{34,40}(?![A-Za-z0-9_~.\-])", RegexOptions.CultureInvariant);

    private static readonly Regex Bearer = new(@"\bBearer\s+(?<v>[A-Za-z0-9._~+/=\-]{16,})", I);

    private static readonly Regex CliP = new(@"\b(?:sqlcmd|osql|bcp)(?:\.exe)?\b[^\n]*?\s(?-i:-P)\s*(?<v>""[^""\n]+""|'[^'\n]+'|[^\s""'\-][^\s""']*)", I);

    private static readonly string[] PlaceholderWords =
    {
        "changeme", "password", "passwd", "secret", "example", "placeholder", "redacted", "hidden", "your", "dummy", "sample",
        "fake", "todo", "tbd", "none", "null", "empty", "required", "unset", "notset", "xxx", "foo", "bar", "value", "masked",
    };

    /// <summary>Every credential value in the text, in order. Empty = none found.</summary>
    public static IReadOnlyList<Hit> Find(string? text)
    {
        var hits = new List<Hit>();
        if (string.IsNullOrEmpty(text)) return hits;
        foreach (Match m in Kv.Matches(text))
        {
            var (i, l) = Unquote(m.Groups["v"]);
            var v = text.Substring(i, l);
            if (l >= 4 && !IsPlaceholder(v) && !IsEnvName(v) && !IsCode(v)) hits.Add(new Hit(i, l, "kv"));
        }
        foreach (Match m in ProseWord.Matches(text)) Prose(text, m.Index + m.Length, hits);
        foreach (Match m in Prefix.Matches(text))
            if (!IsPlaceholder(m.Groups["v"].Value)) hits.Add(new Hit(m.Index, m.Length, "prefix"));
        foreach (Match m in Azure.Matches(text))
        {
            var v = m.Value;
            if (v.Contains('~') && v.Any(char.IsLetter) && v.Any(char.IsDigit) && Classes(v) >= 3) hits.Add(new Hit(m.Index, m.Length, "azure"));
        }
        foreach (Match m in Bearer.Matches(text))
            if (!IsPlaceholder(m.Groups["v"].Value)) hits.Add(new Hit(m.Groups["v"].Index, m.Groups["v"].Length, "bearer"));
        foreach (Match m in CliP.Matches(text))
        {
            var (i, l) = Unquote(m.Groups["v"]);
            var v = text.Substring(i, l);
            if (l >= 1 && !IsPlaceholder(v) && !IsEnvName(v)) hits.Add(new Hit(i, l, "cli"));
        }
        return Merge(hits);
    }

    /// <summary>
    /// For tuning on real rows WITHOUT showing a value: per hit, the rule, the credential word and separator that led to it
    /// (prose), which word after the separator, its length, character-class count and entropy. Never the value's text.
    /// </summary>
    public static IReadOnlyList<string> Explain(string? text)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;
        foreach (var h in Find(text))
        {
            var v = text.Substring(h.Index, h.Length);
            var why = "";
            if (h.Rule == "prose")
            {
                var before = text[..h.Index];
                var kw = ProseWord.Matches(before).LastOrDefault();
                if (kw is not null)
                {
                    var sep = Separator.Match(text, kw.Index + kw.Length, h.Index - kw.Index - kw.Length);
                    var gapWords = sep.Success ? text[(sep.Index + sep.Length)..h.Index].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length : -1;
                    // up to 4 words BEFORE the credential word: context, never the value (which comes after it)
                    var lineStart = before.LastIndexOf('\n', Math.Max(0, kw.Index - 1)) + 1;
                    var lead = string.Join(' ', text[lineStart..kw.Index].Split(' ', StringSplitOptions.RemoveEmptyEntries).TakeLast(4));
                    why = $" context='{lead}' word='{kw.Value}' sep='{(sep.Success ? sep.Value.Trim() : "?")}' wordsBefore={gapWords} gapChars={(sep.Success ? sep.Index - kw.Index - kw.Length : -1)}";
                }
            }
            // the shape: each run of one class collapsed to one mark (A upper, a lower, 9 digit, # other) - no character of it
            var shape = new System.Text.StringBuilder();
            foreach (var ch in v)
            {
                var k = char.IsUpper(ch) ? 'A' : char.IsLower(ch) ? 'a' : char.IsDigit(ch) ? '9' : '#';
                if (shape.Length == 0 || shape[^1] != k) shape.Append(k);
            }
            lines.Add($"{h.Rule}{why} len={h.Length} classes={Classes(v)} entropy={Entropy(v):0.00} shape={shape}");
        }
        return lines;
    }

    /// <summary>True when the text holds a credential value.</summary>
    public static bool Holds(string? text) => Find(text).Count > 0;

    /// <summary>
    /// The text with each value replaced by <c>[REDACTED len=N]</c> (kor start renders through this, so a value that ever
    /// reached the DB never reaches a session). Null stays null.
    /// </summary>
    public static string? Mask(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var hits = Find(text);
        if (hits.Count == 0) return text;
        var sb = new System.Text.StringBuilder(text);
        foreach (var h in hits.OrderByDescending(h => h.Index))
            sb.Remove(h.Index, h.Length).Insert(h.Index, $"[REDACTED len={h.Length}]");
        return sb.ToString();
    }

    /// <summary>
    /// <see cref="Mask"/> for a text that may be JSON (an event's Detail): when it parses, each string VALUE is masked and
    /// the JSON written again, so a mask never cuts an escape or a quote; otherwise the text is masked as text.
    /// </summary>
    public static string? MaskJsonOrText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var t = text.TrimStart();
        if (t.Length > 0 && t[0] is '{' or '[')
        {
            try
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(text);
                if (node is not null && MaskNode(node) is var changed && changed) return node.ToJsonString(GitCapture.Json);
                if (node is not null) return text;
            }
            catch (System.Text.Json.JsonException) { }
        }
        return Mask(text);
    }

    private static bool MaskNode(System.Text.Json.Nodes.JsonNode node)
    {
        var changed = false;
        switch (node)
        {
            case System.Text.Json.Nodes.JsonObject o:
                foreach (var key in o.Select(p => p.Key).ToList())
                {
                    var v = o[key];
                    if (v is System.Text.Json.Nodes.JsonValue jv && jv.TryGetValue<string>(out var s))
                    {
                        var m = Mask(s);
                        if (m != s) { o[key] = m; changed = true; }
                    }
                    else if (v is not null) changed |= MaskNode(v);
                }
                break;
            case System.Text.Json.Nodes.JsonArray a:
                for (var i = 0; i < a.Count; i++)
                {
                    var v = a[i];
                    if (v is System.Text.Json.Nodes.JsonValue jv && jv.TryGetValue<string>(out var s))
                    {
                        var m = Mask(s);
                        if (m != s) { a[i] = m; changed = true; }
                    }
                    else if (v is not null) changed |= MaskNode(v);
                }
                break;
        }
        return changed;
    }

    /// <summary>
    /// Refuses a write whose <paramref name="what"/> (e.g. "body") holds a credential value: ONE plain line naming the
    /// rule, the length and where - never the value. Nothing is written by a caller that calls this first.
    /// </summary>
    public static void Refuse(string? text, string what)
    {
        var hits = Find(text);
        if (hits.Count == 0) return;
        var h = hits[0];
        throw new SecretRefusedException(
            $"refused, nothing written: the {what} holds what looks like a credential value ({h.Rule} rule, {h.Length} characters at " +
            $"character {h.Index}{(hits.Count > 1 ? $", and {hits.Count - 1} more" : "")}) - write where it lives (an env var NAME, the vault), never the value (mistake 138)");
    }

    /// <summary>
    /// For oversight.GuardRefusal.Matched: masked, then every marker CK_GuardRefusal_NoSecret refuses un-redacted
    /// (password= pwd= accountkey= sk-ant- "bearer ") is followed by [redacted] as Framework tools/guard_log.py does, then
    /// cut to <paramref name="max"/>.
    /// </summary>
    public static string ForGuardLog(string? text, int max = 400)
    {
        var s = Mask(text) ?? "";
        s = Regex.Replace(s, @"(password|pwd|accountkey)\s*=\s*('[^']*'|""[^""]*""|[^;\s]*)", "$1=[redacted]", I);
        s = Regex.Replace(s, @"sk-ant-\S*", "sk-ant-[redacted]", I);
        s = Regex.Replace(s, @"bearer[ \t]+\S*", "Bearer [redacted]", I);
        s = s.Length <= max ? s : s[..(max - 3)] + "...";
        // a cut can leave a marker at the very end followed by nothing - the constraint needs a following non-'[' char,
        // so that is safe; a cut that splits "[redacted]" after a marker is not: re-run the marker pass on the cut text
        return Regex.Replace(s, @"(password|pwd|accountkey)=(?!\[)", "$1=[redacted]", I);
    }

    // ---- prose ---------------------------------------------------------------------------------------------------------
    private static void Prose(string text, int afterWord, List<Hit> hits)
    {
        var lineEnd = text.IndexOf('\n', afterWord);
        if (lineEnd < 0) lineEnd = text.Length;
        var windowEnd = Math.Min(lineEnd, afterWord + 60);
        var sep = Separator.Match(text, afterWord, lineEnd - afterWord);
        if (!sep.Success || sep.Index > windowEnd) return;
        // the FIRST word after the separator is the value; a "user / password" pair (a lone '/' between) also offers the
        // word after it. Any other later word is prose, not the value (tuned on the live brain 3 Nov: three words read
        // ordinary sentences as values).
        var i = sep.Index + sep.Length;
        var words = new List<(int Start, int Length)>();
        while (i < lineEnd && words.Count < 3)
        {
            while (i < lineEnd && char.IsWhiteSpace(text[i])) i++;
            var start = i;
            while (i < lineEnd && !char.IsWhiteSpace(text[i])) i++;
            if (i > start) words.Add((start, i - start));
        }
        var offer = new List<(int Start, int Length)>();
        if (words.Count > 0) offer.Add(words[0]);
        if (words.Count == 3 && text.Substring(words[1].Start, words[1].Length) is "/" or "|" or "&") offer.Add(words[2]);
        foreach (var (start, length) in offer)
        {
            var (ts, tl) = Trim(text, start, length);
            if (tl > 0 && IsProseValue(text.Substring(ts, tl))) { hits.Add(new Hit(ts, tl, "prose")); return; }
        }
    }

    private static (int Start, int Length) Trim(string text, int start, int length)
    {
        var s = start;
        var e = start + length;
        // quotes, brackets and markdown emphasis (**x**, _x_) around a word are not part of it
        while (s < e && "\"'`([{<*_".Contains(text[s])) s++;
        while (e > s && "\"'`.,;:)]}>!?*_\\".Contains(text[e - 1])) e--;
        return (s, e - s);
    }

    private static bool IsProseValue(string v)
    {
        if (IsPlaceholder(v) || IsEnvName(v) || IsCode(v) || IsGuid(v) || IsHex(v) || IsPathOrUrl(v) || IsFileName(v)
            || IsWordChain(v) || IsAccountOrHost(v)) return false;
        var classes = Classes(v);
        if (v.Length >= 6 && classes >= 3) return true;
        // 16+ characters of high entropy: random-looking (3.8 bits a character or more), with a digit or a symbol
        return v.Length >= 16 && classes >= 2 && v.Any(c => char.IsDigit(c) || !char.IsLetterOrDigit(c)) && Entropy(v) >= 3.8;
    }

    /// <summary>Words joined by - _ ' : (per-user, Read-Only, KOR_Opportunities, Section:Key): language or a config key,
    /// not a value; and a quantity with a unit (25_000t, 1.5k, ~3,000ms).</summary>
    private static bool IsWordChain(string v) =>
        Regex.IsMatch(v, @"\A[A-Za-z]{2,}(?:[-_':]+[A-Za-z]{2,})+\z")          // per-user, Mcp__Basic__User, Section:Key
        || Regex.IsMatch(v, @"\A[~≈]?[\d.,_]+[a-zA-Z]{0,3}\z")                 // 25_000t, 1.5k
        || Regex.IsMatch(v, @"\A\d+[-+x×/][a-z]+\z")                          // 2-gram, 3+words
        || v.StartsWith("--", StringComparison.Ordinal);                       // a CLI flag or a CSS custom property

    /// <summary>DOMAIN\user, SERVER\INSTANCE, user@host, host:port: who or where, not the secret.</summary>
    private static bool IsAccountOrHost(string v) =>
        Regex.IsMatch(v, @"\A[\w.\-]+\\[\w.$\-]+\z") || Regex.IsMatch(v, @"\A[\w.\-+]+@[\w\-]+(?:\.[\w\-]+)+\z")
        || Regex.IsMatch(v, @"\A[A-Za-z][\w.\-]*:\d{2,5}\z");

    // ---- shapes that are never a value ----------------------------------------------------------------------------------
    internal static bool IsPlaceholder(string v)
    {
        v = v.Trim().Trim('"', '\'', '`');
        if (v.Length == 0) return true;
        // a variable or template slot, whole: <pw> {pw} ${PW} $(cmd) $PW $env:PW %PW% [REDACTED ...]
        if (Regex.IsMatch(v, @"\A(?:<[^<>]*>|\{[^{}]*\}|\$\{[^}]*\}|\$\(.*\)|\$[A-Za-z_][A-Za-z0-9_:]*|%[A-Za-z_][A-Za-z0-9_]*%|\[[^\]]*\]?)\z")) return true;
        if (v.StartsWith("env:", StringComparison.OrdinalIgnoreCase)) return true;
        if (v.Contains("...") || v.Contains('…')) return true;
        if (v.All(c => c is '*' or 'x' or 'X' or '.' or '#' or '•' or '_' or '-' or '?')) return true;
        if (v.Distinct().Count() <= 2) return true;   // aaaa, abab: not a credential anyone uses
        if (!v.Any(char.IsDigit) && v.All(char.IsLetter))
        {
            var low = v.ToLowerInvariant();
            if (PlaceholderWords.Any(w => low.Contains(w))) return true;
        }
        return false;
    }

    /// <summary>An env var NAME (KOR_DB_PASSWORD, SQLCMDPASSWORD): capitals, digits and '_', at most two digits.</summary>
    private static bool IsEnvName(string v) => Regex.IsMatch(v, @"\A[A-Z][A-Z0-9_]{2,}\z") && v.Count(char.IsDigit) <= 2;

    private static bool IsCode(string v) =>
        Regex.IsMatch(v, @"\A[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+\z")   // args.Password, os.environ
        || Regex.IsMatch(v, @"\A[A-Za-z_][\w.]*\(") || v.Contains("[\"") || v.Contains("['");

    private static bool IsGuid(string v) => Guid.TryParse(v, out _);

    private static bool IsHex(string v) => Regex.IsMatch(v, @"\A(?:0x)?[0-9a-fA-F]{7,}\z");

    private static bool IsPathOrUrl(string v) =>
        Regex.IsMatch(v, @"\A(?:[A-Za-z]:[\\/]|\\\\|/|\./|\.\./|~/|[A-Za-z][\w+.-]*://)") || v.Count(c => c is '/' or '\\') >= 2;

    private static bool IsFileName(string v) =>
        Regex.IsMatch(v, @"\A[\w.\-]+\.(?:ps1|psm1|py|cs|csproj|sql|json|md|txt|exe|dll|config|xml|ya?ml|csv|log|bat|cmd|sh|pdf|dxf|e2k|rvt|html?|js|ts)\z", I);

    internal static int Classes(string v) =>
        (v.Any(char.IsLower) ? 1 : 0) + (v.Any(char.IsUpper) ? 1 : 0) + (v.Any(char.IsDigit) ? 1 : 0)
        + (v.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c)) ? 1 : 0);

    internal static double Entropy(string v)
    {
        if (v.Length == 0) return 0;
        return -v.GroupBy(c => c).Select(g => (double)g.Count() / v.Length).Sum(p => p * Math.Log2(p));
    }

    private static (int Index, int Length) Unquote(Group g)
    {
        var v = g.Value;
        return v.Length >= 2 && (v[0] == '"' || v[0] == '\'') && v[^1] == v[0] ? (g.Index + 1, g.Length - 2) : (g.Index, g.Length);
    }

    private static List<Hit> Merge(List<Hit> hits)
    {
        // overlapping hits: the more exact rule wins (a key=value or a known shape over prose, which reads to whitespace and
        // would take "x;Encrypt=true" with it), then the earlier, then the longer; the loser is dropped, never merged in
        static int Rank(string rule) => rule == "prose" ? 1 : 0;
        var kept = new List<Hit>();
        foreach (var h in hits.Where(h => h.Length > 0).OrderBy(h => Rank(h.Rule)).ThenBy(h => h.Index).ThenByDescending(h => h.Length))
            if (!kept.Any(k => h.Index < k.Index + k.Length && k.Index < h.Index + h.Length)) kept.Add(h);
        return kept.OrderBy(h => h.Index).ToList();
    }
}

/// <summary>A brain write refused because it holds a credential value (mistake 138). The message never carries the value.</summary>
public sealed class SecretRefusedException(string message) : InvalidOperationException(message);
