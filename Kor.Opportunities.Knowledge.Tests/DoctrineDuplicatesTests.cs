using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Knowledge.Doctrine;
using Kor.Opportunities.Knowledge.Embedding;
using Xunit;

namespace Kor.Opportunities.Knowledge.Tests;

/// <summary>
/// The doctrine's duplicate check, with a word-bag embedder so the scores are predictable. Covers: a repeat of an accepted,
/// waiting or rejected card is found; a card's own run is not compared with itself on a resend; an amendment is not flagged
/// against the card it amends; retired cards are ignored. Does NOT cover: how the real model scores meaning (calibrated with
/// `doctrine similar` on real cards; numbers in DoctrineDuplicates).
/// </summary>
public sealed class DoctrineDuplicatesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 18, 0, 0, TimeSpan.Zero);

    private static DoctrineCardRow Card(long id, string title, string status, long run = 1) =>
        new(id, "BD", title, "plain " + title, "any", "principle " + title, "r", "p", null, "c", null, run, null, status, Now, null, null);

    private static DoctrineCardDraft Draft(string title, long? amends = null) =>
        new(title, "plain " + title, "any", "principle " + title, "r", "p", null, "c", null, amends);

    [Fact]
    public void ARepeatOfAnyLiveCardIsFound_WhateverItsStatus()
    {
        var existing = new[] { Card(1, "debrief every loss", "accepted"), Card(2, "debrief every loss", "rejected"), Card(3, "debrief every loss", "proposed", run: 9), Card(4, "debrief every loss", "retired") };
        var near = DoctrineDuplicates.Find(new[] { Draft("debrief every loss") }, existing, new WordBag());
        Assert.Equal(new long[] { 1, 2, 3 }, near.Select(m => m.ExistingId).Order());
        Assert.All(near, m => Assert.True(m.Score >= DoctrineDuplicates.RefuseAt));
    }

    [Fact]
    public void AResendIsNotComparedWithItsOwnWaitingCards_AndAnAmendmentNotWithWhatItAmends()
    {
        var existing = new[] { Card(5, "debrief every loss", "proposed", run: 4), Card(6, "debrief every loss", "accepted") };
        Assert.Empty(DoctrineDuplicates.Find(new[] { Draft("debrief every loss", amends: 6) }, existing, new WordBag(), excludeRunId: 4));
    }

    [Fact]
    public void DifferentIdeasAreNotFlagged()
    {
        var existing = new[] { Card(7, "debrief every loss", "accepted") };
        Assert.Empty(DoctrineDuplicates.Find(new[] { Draft("book the quarterly all hands") }, existing, new WordBag()));
    }

    /// <summary>Each word sets a fixed dimension, so equal word sets score 1 and disjoint ones 0.</summary>
    private sealed class WordBag : IKnowledgeEmbedder
    {
        public float[] Embed(string text)
        {
            var v = new float[KnowledgeEmbedder.Dimensions];
            foreach (var w in text.ToLowerInvariant().Split(' ', '.', StringSplitOptions.RemoveEmptyEntries))
                v[(int)((uint)StringComparer.Ordinal.GetHashCode(w) % KnowledgeEmbedder.Dimensions)] += 1;
            return v;
        }
    }
}
