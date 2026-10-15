namespace Kor.Opportunities.Knowledge.Brief;

public enum DirectorSection { ThisWeek, Pursuits, Accounts, Market, Asks }

/// <summary>Questions for the session writer, with the meaning searches that supply evidence.</summary>
public static class DirectorQuestions
{
    public static string Question(DirectorSection section) => section switch
    {
        DirectorSection.ThisWeek => "What one or two actions this week would move the most across people, pursuits and accounts?",
        DirectorSection.Pursuits => "What has moved, what has not, and which pursuit should be decided on first?",
        DirectorSection.Accounts => "Which Deltek clients should we touch this month and why, given last touch and live pursuits?",
        DirectorSection.Market => "Which signals, deadlines or forward-pipeline rows deserve a conversation this week?",
        DirectorSection.Asks => "Which open asks are overdue or misdirected, and what should we ask next?",
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };

    public static string ClaimsQuery(DirectorSection section) => section switch
    {
        DirectorSection.ThisWeek => "how engineers in a professional firm should prioritize business development time",
        DirectorSection.Pursuits => "when a professional firm should qualify, advance or stop a stalled pursuit",
        DirectorSection.Accounts => "when and how a professional firm should re-contact a dormant client",
        DirectorSection.Market => "how a professional firm turns early market signals and planned projects into client conversations",
        DirectorSection.Asks => "how to assign bounded business development actions and follow up on overdue commitments",
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };
}
