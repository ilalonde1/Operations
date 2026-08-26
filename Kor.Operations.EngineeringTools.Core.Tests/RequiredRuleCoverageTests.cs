using System.Collections;
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Every rule the code READS must be a rule the run REQUIRES.
///
/// KorStandards is loaded whole, so a value present there is applied whether or not it is on the
/// required list. That makes a missing key from the list look harmless, and it is not. Three
/// things follow from being off it, and all three were true on 26 August for eight settings:
///
///   * A rule ABSENT from KorStandards falls back to the number in C#, silently, on a production
///     run whose stated contract is that a missing rule stops it.
///   * The report's rule list and the engineer's "Rules in force" sheet are built from that list,
///     so those eight were named nowhere she could see — including the two that decide whether a
///     region of her building is a slab or a hole.
///   * Nothing said so. The suite was green, the model was right, and the gap was found only by
///     comparing three lists by hand: 43 keys read, 35 required, 43 in the database.
///
/// The dictionary handed to ApplyRules here records every key looked up, so this measures what the
/// code does rather than what a list in another file claims about it.
/// </summary>
public class RequiredRuleCoverageTests
{
    private readonly ITestOutputHelper _out;

    public RequiredRuleCoverageTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// A settings dictionary that answers nothing and remembers everything asked of it.
    ///
    /// Answering nothing is deliberate: every accessor then takes its fallback, so the options come
    /// back untouched and no rule can change behaviour during the measurement. All that is under
    /// test is WHICH keys were asked for.
    /// </summary>
    private sealed class RecordingSettings : IReadOnlyDictionary<string, RuleSetting>
    {
        public readonly List<string> Asked = new();

        public bool TryGetValue(string key, out RuleSetting value)
        {
            Asked.Add(key);
            value = default!;
            return false;
        }

        public bool ContainsKey(string key) { Asked.Add(key); return false; }
        public RuleSetting this[string key] { get { Asked.Add(key); throw new KeyNotFoundException(key); } }

        public IEnumerable<string> Keys => Array.Empty<string>();
        public IEnumerable<RuleSetting> Values => Array.Empty<RuleSetting>();
        public int Count => 0;
        public IEnumerator<KeyValuePair<string, RuleSetting>> GetEnumerator()
            => Enumerable.Empty<KeyValuePair<string, RuleSetting>>().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void EveryRuleTheCodeReadsIsOnTheRequiredList()
    {
        var recorder = new RecordingSettings();

        DxfToEtabsService.ApplyRules(new PlanClassificationOptions(), recorder);
        DxfToEtabsService.ApplyRules(new ComposeOptions(), recorder);

        var read = recorder.Asked.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        var required = DxfToEtabsService.RequiredRuleKeys;

        var unrequired = read
            .Where(k => !required.Contains(k, StringComparer.OrdinalIgnoreCase))
            .ToList();

        _out.WriteLine($"{read.Count} rule(s) read by the code, {required.Count} required.");

        Assert.True(unrequired.Count == 0,
            "These rules are read when a model is built but are not on RequiredRuleKeys:\n  " +
            string.Join("\n  ", unrequired) +
            "\n\nSo if KorStandards does not have one, the run does not stop — it quietly uses the " +
            "number in DxfToEtabsService, and the engineer's rule sheet never mentions it. Add each " +
            "to RequiredRuleKeys and to BuiltInRuleValues.");
    }

    /// <summary>
    /// And the other direction, so the list cannot grow a key nothing reads. A rule on the sheet
    /// that changes nothing is worse than one that is absent: an engineer who writes in an answer
    /// is owed a model that moves.
    /// </summary>
    [Fact]
    public void EveryRequiredRuleIsActuallyRead()
    {
        var recorder = new RecordingSettings();

        DxfToEtabsService.ApplyRules(new PlanClassificationOptions(), recorder);
        DxfToEtabsService.ApplyRules(new ComposeOptions(), recorder);

        var read = recorder.Asked.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unread = DxfToEtabsService.RequiredRuleKeys
            .Where(k => !read.Contains(k))
            .ToList();

        Assert.True(unread.Count == 0,
            "These rules are required of KorStandards but nothing reads them:\n  " +
            string.Join("\n  ", unread));
    }
}
