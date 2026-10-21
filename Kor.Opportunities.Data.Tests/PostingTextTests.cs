using Kor.Opportunities.Data.Ingestion;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

// Pins what the RFP window shows as "What the posting says". Covers tags, escaped tags, entities and blank lines in the
// description text; it does not cover a description that is a whole HTML page (scripts, styles), which no feed sends today.
public sealed class PostingTextTests
{
    [Fact]
    public void A_CivicInfo_description_reads_as_text_not_markup()
    {
        // Verbatim from opportunity 39521 (CivicInfoBC_All), as Ian saw it on 2026-10-20.
        const string raw = "Request for Proposals<br/>Posting expires: Tuesday, October 20, 2026.<br>Location: Vernon<br><br>To post a bid or tender, please e-mail <a href='mailto:bids@civicinfo.bc.ca'>bids@civicinfo.bc.ca</a>.";
        Assert.Equal("Request for Proposals\nPosting expires: Tuesday, October 20, 2026.\nLocation: Vernon\nTo post a bid or tender, please e-mail bids@civicinfo.bc.ca.",
            PostingText.ToPlain(raw));
    }

    [Theory]
    [InlineData("REQUEST FOR PROPOSALS (RFP) FOR ENGINEERING CONSULTANT SERVICES FOR EDMONTON – JOHN E BROWNLEE BUILDING",
        "Request for Proposals (RFP) for Engineering Consultant Services for Edmonton – John E Brownlee Building")]
    [InlineData("PRE-QUALIFICATION REQUEST (“PQR”) 021771 For ARCHITECTURAL SERVICES Ministry of Infrastructure",
        "Pre-Qualification Request (“PQR”) 021771 for Architectural Services Ministry of Infrastructure")]
    [InlineData("Prime Consultant - SE02 Second-Floor Patio Renovation", "Prime Consultant - SE02 Second-Floor Patio Renovation")]
    [InlineData("MDB - New CAT II Accommodations Buildings", "MDB - New CAT II Accommodations Buildings")]
    public void Capitalised_tender_titles_read_in_normal_case(string raw, string shown)
        => Assert.Equal(shown, PostingText.DisplayTitle(raw));

    [Fact]
    public void Escaped_markup_and_entities_are_decoded_and_plain_text_is_left_alone()
    {
        Assert.Equal("Roof & envelope\nPhase 2", PostingText.ToPlain("Roof &amp; envelope&lt;br/&gt;Phase 2"));
        Assert.Equal("Seismic upgrade of the gym.", PostingText.ToPlain("  Seismic upgrade of the gym.  "));
        Assert.Equal("", PostingText.ToPlain(null));
    }
}
