#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Core.PrimeConsultant;
using Kor.Opportunities.Data.Awards;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kor.Opportunities.Data.Opportunities;

/// <summary>
/// SQL Server implementation of <see cref="IOpportunityStore"/>. Pure ADO,
/// no EF — matches repo convention. Connection string is injected as a
/// plain string so this project stays free of host-specific Options classes.
/// </summary>
public sealed class SqlOpportunityStore : IOpportunityStore
{
    private readonly CanonicalOrgResolver? _canonicalResolver;
    private readonly ILogger<SqlOpportunityStore> _logger;

    private const int CommandTimeoutSeconds = 30;

    /// <summary>
    /// Canonical SELECT column list. Used by both read queries and OUTPUT
    /// clauses. Order matches <see cref="MapReader"/>; do not reorder one
    /// without the other.
    /// </summary>
    private const string AllColumns = @"
Id, OpportunityKey, Name,
BuyerName, BuyerType, DeltekClientId, DeltekContactId, BuyerContactName, BuyerContactEmail, BuyerContactPhone,
ProjectAddress, ProjectCity, ProjectProvince, ProjectPostalCode, ProjectLatitude, ProjectLongitude,
Discipline, ConstructionType, ProjectCategory,
EstimatedValue, EstimatedValueCurrency, RfpReleaseDate, SubmissionDeadlineUtc,
Status, IdentifiedAtUtc, ReviewingSinceUtc, QualifiedAtUtc, PursuingSinceUtc, ProposalSubmittedAtUtc, OutcomeAtUtc,
OwnerStaffId, ReviewerStaffIds,
RelevanceScore, RelevanceTier,
WonLostOutcome, OutcomeReason, WonProjectWbs1, AwardedValue,
IsPrimeConsultantRfp, PrimeConfidence, PrimeDisciplineType, PrimeProjectSector, PrimeLikelyType,
PrimeKorSectorMatch, PrimeKorLocationMatch, PrimeClassifiedAtUtc,
CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy,
RowVersion";

    private readonly string _connectionString;

    public SqlOpportunityStore(
        string connectionString,
        CanonicalOrgResolver? canonicalResolver = null,
        ILogger<SqlOpportunityStore>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
        _canonicalResolver = canonicalResolver;
        _logger = logger ?? NullLogger<SqlOpportunityStore>.Instance;
    }

    public async Task<IReadOnlyList<Opportunity>> ListAsync(CancellationToken ct, int maxRows = 5000, bool includeClosed = true, bool includeNonPrime = true)
    {
        var cappedRows = Math.Clamp(maxRows, 1, 5000);
        var conditions = new List<string>();
        if (!includeClosed)
        {
            conditions.Add("Status NOT IN (6, 7)");
        }

        if (!includeNonPrime)
        {
            conditions.Add("IsPrimeConsultantRfp = 1");
        }

        var whereClause = conditions.Count == 0
            ? string.Empty
            : $"{Environment.NewLine}WHERE {string.Join(" AND ", conditions)}";

        var sql = $@"
SELECT TOP (@max) {AllColumns}
FROM opportunities.Opportunities{whereClause}
ORDER BY UpdatedAtUtc DESC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@max", SqlDbType.Int).Value = cappedRows;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<Opportunity>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(MapReader(reader));
        }

        return rows;
    }

    public async Task<Opportunity?> GetByIdAsync(long id, CancellationToken ct)
    {
        var sql = $@"
SELECT {AllColumns}
FROM opportunities.Opportunities
WHERE Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapReader(reader) : null;
    }

    public async Task<Opportunity?> GetByKeyAsync(string opportunityKey, CancellationToken ct)
    {
        var sql = $@"
SELECT {AllColumns}
FROM opportunities.Opportunities
WHERE OpportunityKey = @key;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@key", SqlDbType.NVarChar, 64).Value = opportunityKey;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapReader(reader) : null;
    }

    public async Task<long?> GetBuyerCanonicalOrgIdAsync(long opportunityId, CancellationToken ct)
    {
        // Lean single-column read (plan 1.4): BuyerCanonicalOrgId lives on the
        // table (migration 22) but deliberately not on the domain model.
        const string sql = @"
SELECT BuyerCanonicalOrgId
FROM opportunities.Opportunities
WHERE Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = opportunityId;
        var scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return scalar is long l ? l : null;
    }

    public async Task<IReadOnlyList<OpportunityDuplicateCandidate>> FindPossibleDuplicatesAsync(
        string buyerName, string name, string? city, string? excludeKey, int take, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(buyerName))
        {
            return Array.Empty<OpportunityDuplicateCandidate>();
        }

        // Read-only buyer resolution — PURE find, never writes (adversarial
        // review 2026-07-07: the old ResolveAsync(allowCreate:false) still wrote
        // UnclassifiedAlias rows on every debounce tick). "City of Vancouver"
        // and "Vancouver (City of)" resolve to the same id, so a re-entry of an
        // ingested RFP scores as the same buyer even against NULL-FK rows.
        long? typedBuyerId = null;
        if (_canonicalResolver is not null && !string.IsNullOrWhiteSpace(buyerName))
        {
            try
            {
                typedBuyerId = await _canonicalResolver
                    .FindExistingAsync(buyerName, OrgAliasSources.OpportunitiesBuyer, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Duplicate-guard buyer resolve failed for '{Buyer}'.", buyerName);
            }
        }

        // Candidate set is bounded by NAME TOKENS, not by buyer (review fixes):
        //   - catches dups whose existing row has a NULL BuyerCanonicalOrgId
        //     (freshly-ingested rows — the ones most likely re-entered);
        //   - keeps a coarse buyer (thousands of opps) from being scored whole
        //     on every 500ms tick;
        //   - INCLUDES closed Won/Lost opps so re-entering a completed RFP is
        //     flagged (the dialog shows its status).
        // The top few significant title words (≥4 chars) OR'd; TOP 500 by
        // recency backstops a very common token. sameBuyer is computed app-side
        // (canonical id OR normalized raw-name equality) for scoring.
        var tokens = SignificantTokens(name, max: 3);
        var likeParams = new List<string>();
        var likeClauses = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            likeClauses.Add($"o.Name LIKE @t{i} ESCAPE N'\\'");
            likeParams.Add("%" + EscapeLike(tokens[i]) + "%");
        }

        string where;
        if (likeClauses.Count > 0)
        {
            where = "(" + string.Join(" OR ", likeClauses) + ")";
        }
        else if (typedBuyerId.HasValue)
        {
            // No usable title token (very short name): fall back to this buyer's
            // opps, still bounded by TOP 500 recency.
            where = "o.BuyerCanonicalOrgId = @buyerId";
        }
        else
        {
            return Array.Empty<OpportunityDuplicateCandidate>();
        }

        var sql = $@"
SELECT TOP 500
       o.OpportunityKey, o.Name, o.BuyerName, o.Status, o.ProjectCity, o.BuyerCanonicalOrgId,
       CASE WHEN EXISTS (SELECT 1 FROM opportunities.CrmEngagements e WHERE e.OpportunityId = o.Id) THEN 1 ELSE 0 END AS HasPursuit
FROM opportunities.Opportunities o
WHERE {where}
ORDER BY o.UpdatedAtUtc DESC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        for (var i = 0; i < likeParams.Count; i++)
        {
            cmd.Parameters.Add($"@t{i}", SqlDbType.NVarChar, 400).Value = likeParams[i];
        }
        if (likeClauses.Count == 0 && typedBuyerId.HasValue)
        {
            cmd.Parameters.Add("@buyerId", SqlDbType.BigInt).Value = typedBuyerId.Value;
        }

        var typedBuyerNorm = CanonicalOrgResolver.NormalizeName(CanonicalOrgResolver.StripIntakeNoise(buyerName ?? string.Empty));
        var results = new List<OpportunityDuplicateCandidate>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var candKey = reader.GetString(0);
                if (!string.IsNullOrEmpty(excludeKey) && string.Equals(candKey, excludeKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candName = reader.GetString(1);
                var candBuyer = reader.GetString(2);
                var candBuyerId = reader.IsDBNull(5) ? (long?)null : reader.GetInt64(5);

                var sameBuyer = (typedBuyerId.HasValue && candBuyerId == typedBuyerId)
                    || (typedBuyerNorm.Length >= 3
                        && CanonicalOrgResolver.NormalizeName(CanonicalOrgResolver.StripIntakeNoise(candBuyer)) == typedBuyerNorm);

                var score = OpportunityDuplicateScorer.NameSimilarity(name, candName);
                var confidence = OpportunityDuplicateScorer.Classify(score, sameBuyer);
                if (confidence == DuplicateConfidence.None)
                {
                    continue;
                }

                results.Add(new OpportunityDuplicateCandidate(
                    candKey, candName, candBuyer, reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetInt32(6) == 1, sameBuyer, score, confidence));
            }
        }

        return results
            .OrderByDescending(r => r.Confidence)
            .ThenByDescending(r => r.NameScore)
            .Take(Math.Max(1, take))
            .ToList();
    }

    /// <summary>The <paramref name="max"/> longest distinct name words (len ≥ 4)
    /// for the candidate LIKE prefilter — the most selective title tokens.</summary>
    private static List<string> SignificantTokens(string? name, int max)
    {
        if (string.IsNullOrWhiteSpace(name)) return new List<string>();
        var words = new List<string>();
        var word = new System.Text.StringBuilder();
        void Flush()
        {
            if (word.Length >= 4) words.Add(word.ToString());
            word.Clear();
        }

        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch)) word.Append(char.ToLowerInvariant(ch));
            else Flush();
        }
        Flush();

        return words
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(w => w.Length)
            .Take(max)
            .ToList();
    }

    private static string EscapeLike(string s)
        => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    public async Task<Opportunity> InsertAsync(Opportunity opportunity, string actorDisplay, CancellationToken ct)
    {
        var classifiedOpportunity = WithPrimeClassification(opportunity);
        // CreatedAtUtc / UpdatedAtUtc fall back to SQL defaults so the timestamps are
        // server-side and atomic with the insert. We set CreatedBy / UpdatedBy explicitly
        // so we capture the WPF user, not the SQL login (suser_sname() = 'opportunities_app').
        var sql = $@"
INSERT INTO opportunities.Opportunities
    (OpportunityKey, Name,
     BuyerName, BuyerType, DeltekClientId, DeltekContactId, BuyerContactName, BuyerContactEmail, BuyerContactPhone,
     ProjectAddress, ProjectCity, ProjectProvince, ProjectPostalCode, ProjectLatitude, ProjectLongitude,
     Discipline, ConstructionType, ProjectCategory,
     EstimatedValue, EstimatedValueCurrency, RfpReleaseDate, SubmissionDeadlineUtc,
     Status, IdentifiedAtUtc, ReviewingSinceUtc, QualifiedAtUtc, PursuingSinceUtc, ProposalSubmittedAtUtc, OutcomeAtUtc,
     OwnerStaffId, ReviewerStaffIds,
     RelevanceScore, RelevanceTier,
     WonLostOutcome, OutcomeReason, WonProjectWbs1, AwardedValue,
     IsPrimeConsultantRfp, PrimeConfidence, PrimeDisciplineType, PrimeProjectSector, PrimeLikelyType,
     PrimeKorSectorMatch, PrimeKorLocationMatch, PrimeClassifiedAtUtc,
     CreatedBy, UpdatedBy)
OUTPUT {OutputInsertedColumns()}
VALUES
    (@key, @name,
     @buyer, @buyerType, @deltekId, @deltekContactId, @buyerContactName, @buyerContactEmail, @buyerContactPhone,
     @addr, @city, @prov, @postal, @lat, @lng,
     @disc, @ctype, @pcat,
     @value, @ccy, @rfpDate, @deadline,
     @status, @identifiedAt, @reviewingAt, @qualifiedAt, @pursuingAt, @submittedAt, @outcomeAt,
     @ownerId, @reviewerIds,
     @score, @tier,
     @outcome, @outcomeReason, @wbs1, @awarded,
     @isPrime, @primeConfidence, @primeDisciplineType, @primeProjectSector, @primeLikelyType,
     @primeKorSectorMatch, @primeKorLocationMatch, @primeClassifiedAt,
     @actor, @actor);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        BindOpportunityParams(cmd, classifiedOpportunity);
        cmd.Parameters.Add("@actor", SqlDbType.NVarChar, 150).Value = actorDisplay;

        Opportunity inserted;
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                throw new InvalidOperationException("INSERT did not return a row. This should be impossible.");
            }
            inserted = MapReader(reader);
        }

        var freshRvAfterInsert = await ResolveBuyerCanonicalAsync(inserted.Id, inserted.RowVersion, opportunity.OpportunityKey, opportunity.BuyerName, ct).ConfigureAwait(false);
        return freshRvAfterInsert is null ? inserted : inserted with { RowVersion = freshRvAfterInsert };
    }

    public async Task<Opportunity> UpdateAsync(Opportunity opportunity, string actorDisplay, CancellationToken ct)
    {
        var classifiedOpportunity = WithPrimeClassification(opportunity);
        var sql = $@"
UPDATE opportunities.Opportunities
SET OpportunityKey = @key,
    Name = @name,
    BuyerName = @buyer, BuyerType = @buyerType, DeltekClientId = @deltekId,
    DeltekContactId = @deltekContactId, BuyerContactName = @buyerContactName, BuyerContactEmail = @buyerContactEmail, BuyerContactPhone = @buyerContactPhone,
    ProjectAddress = @addr, ProjectCity = @city, ProjectProvince = @prov, ProjectPostalCode = @postal,
    ProjectLatitude = @lat, ProjectLongitude = @lng,
    Discipline = @disc, ConstructionType = @ctype, ProjectCategory = @pcat,
    EstimatedValue = @value, EstimatedValueCurrency = @ccy, RfpReleaseDate = @rfpDate, SubmissionDeadlineUtc = @deadline,
    Status = @status,
    IdentifiedAtUtc = @identifiedAt,
    ReviewingSinceUtc = @reviewingAt, QualifiedAtUtc = @qualifiedAt, PursuingSinceUtc = @pursuingAt,
    ProposalSubmittedAtUtc = @submittedAt, OutcomeAtUtc = @outcomeAt,
    OwnerStaffId = @ownerId, ReviewerStaffIds = @reviewerIds,
    RelevanceScore = @score, RelevanceTier = @tier,
    WonLostOutcome = @outcome, OutcomeReason = @outcomeReason, WonProjectWbs1 = @wbs1, AwardedValue = @awarded,
    IsPrimeConsultantRfp = @isPrime,
    PrimeConfidence = @primeConfidence,
    PrimeDisciplineType = @primeDisciplineType,
    PrimeProjectSector = @primeProjectSector,
    PrimeLikelyType = @primeLikelyType,
    PrimeKorSectorMatch = @primeKorSectorMatch,
    PrimeKorLocationMatch = @primeKorLocationMatch,
    PrimeClassifiedAtUtc = @primeClassifiedAt,
    UpdatedAtUtc = sysdatetimeoffset(), UpdatedBy = @actor
OUTPUT {OutputInsertedColumns()}
WHERE Id = @id AND RowVersion = @rv;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        BindOpportunityParams(cmd, classifiedOpportunity);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = opportunity.Id;
        cmd.Parameters.Add("@rv", SqlDbType.Binary, 8).Value = opportunity.RowVersion;
        cmd.Parameters.Add("@actor", SqlDbType.NVarChar, 150).Value = actorDisplay;

        Opportunity updated;
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                throw new OpportunityConcurrencyException(opportunity.Id);
            }
            updated = MapReader(reader);
        }

        var freshRvAfterUpdate = await ResolveBuyerCanonicalAsync(updated.Id, updated.RowVersion, opportunity.OpportunityKey, opportunity.BuyerName, ct).ConfigureAwait(false);
        return freshRvAfterUpdate is null ? updated : updated with { RowVersion = freshRvAfterUpdate };
    }

    /// <summary>
    /// Best-effort canonical resolution at insert/update time (Round 9c).
    /// Returns the row's NEW RowVersion when it updated the row, else null —
    /// the secondary UPDATE bumps the rowversion behind the record the caller
    /// is about to return, and handing back a stale token made the very next
    /// save throw a phantom OpportunityConcurrencyException (audit 2026-07-01 M1).
    /// The UPDATE is guarded on the rowversion the caller just observed so a
    /// concurrent writer between the two statements is never masked: the guard
    /// misses, null returns, the caller keeps its stale token, and the next save
    /// raises the concurrency conflict it should.
    /// </summary>
    private async Task<byte[]?> ResolveBuyerCanonicalAsync(long opportunityId, byte[] observedRowVersion, string sourceKey, string? buyerName, CancellationToken ct)
    {
        if (_canonicalResolver is null || string.IsNullOrWhiteSpace(buyerName)) return null;
        try
        {
            var buyerId = await _canonicalResolver.ResolveOpportunityBuyerAsync(buyerName, ct).ConfigureAwait(false);
            if (!buyerId.HasValue) return null;

            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync(ct).ConfigureAwait(false);
            await using var upd = new SqlCommand(
                "UPDATE opportunities.Opportunities SET BuyerCanonicalOrgId = @b OUTPUT inserted.RowVersion WHERE Id = @id AND RowVersion = @rv;", con)
            { CommandTimeout = CommandTimeoutSeconds };
            upd.Parameters.Add("@id", SqlDbType.BigInt).Value = opportunityId;
            upd.Parameters.Add("@b", SqlDbType.BigInt).Value = buyerId.Value;
            upd.Parameters.Add("@rv", SqlDbType.Binary, 8).Value = observedRowVersion;
            return await upd.ExecuteScalarAsync(ct).ConfigureAwait(false) as byte[];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Canonical resolution failed for buyer '{BuyerName}' on opportunity {SourceKey}",
                buyerName,
                sourceKey);
            return null;
        }
    }

    public async Task<Opportunity> ChangeStatusAsync(
        long id,
        OpportunityStatus newStatus,
        byte[] expectedRowVersion,
        string actorDisplay,
        CancellationToken ct)
    {
        // Map the target status to the milestone column we should stamp. ISNULL keeps
        // the first transition's timestamp if the user yo-yos a status - the original
        // milestone is preserved.
        var milestoneColumn = newStatus switch
        {
            OpportunityStatus.New => "IdentifiedAtUtc",            // legacy column name retained
            OpportunityStatus.Pursuing => "PursuingSinceUtc",
            OpportunityStatus.Submitted => "ProposalSubmittedAtUtc", // legacy column name retained
            OpportunityStatus.Won
                or OpportunityStatus.Lost => "OutcomeAtUtc",
            _ => throw new ArgumentOutOfRangeException(nameof(newStatus), newStatus, "Unknown status."),
        };

        // milestoneColumn is sourced from a closed enum switch, never user input -
        // safe to interpolate into SQL.
        var sql = $@"
UPDATE opportunities.Opportunities
SET Status = @status,
    {milestoneColumn} = ISNULL({milestoneColumn}, sysdatetimeoffset()),
    UpdatedAtUtc = sysdatetimeoffset(),
    UpdatedBy = @actor
OUTPUT {OutputInsertedColumns()}
WHERE Id = @id AND RowVersion = @rv;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@status", SqlDbType.Int).Value = (int)newStatus;
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        cmd.Parameters.Add("@rv", SqlDbType.Binary, 8).Value = expectedRowVersion;
        cmd.Parameters.Add("@actor", SqlDbType.NVarChar, 150).Value = actorDisplay;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new OpportunityConcurrencyException(id);
        }

        return MapReader(reader);
    }

    /// <summary>
    /// Builds the OUTPUT clause's column list. Matches <see cref="AllColumns"/>
    /// exactly, but each column is prefixed with <c>INSERTED.</c> per T-SQL
    /// OUTPUT semantics.
    /// </summary>
    private static string OutputInsertedColumns()
    {
        var columnNames = AllColumns
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(", ", Array.ConvertAll(columnNames, c => $"INSERTED.{c}"));
    }

    private static void BindOpportunityParams(SqlCommand cmd, Opportunity o)
    {
        cmd.Parameters.Add("@key", SqlDbType.NVarChar, 64).Value = o.OpportunityKey;
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 400).Value = o.Name;
        cmd.Parameters.Add("@buyer", SqlDbType.NVarChar, 300).Value = o.BuyerName;
        cmd.Parameters.Add("@buyerType", SqlDbType.Int).Value = (int)o.BuyerType;
        cmd.Parameters.Add("@deltekId", SqlDbType.NVarChar, 50).Value = (object?)o.DeltekClientId ?? DBNull.Value;
        cmd.Parameters.Add("@deltekContactId", SqlDbType.NVarChar, 32).Value = (object?)o.DeltekContactId ?? DBNull.Value;
        cmd.Parameters.Add("@buyerContactName", SqlDbType.NVarChar, 120).Value = (object?)o.BuyerContactName ?? DBNull.Value;
        cmd.Parameters.Add("@buyerContactEmail", SqlDbType.NVarChar, 255).Value = (object?)o.BuyerContactEmail ?? DBNull.Value;
        cmd.Parameters.Add("@buyerContactPhone", SqlDbType.NVarChar, 40).Value = (object?)o.BuyerContactPhone ?? DBNull.Value;

        cmd.Parameters.Add("@addr", SqlDbType.NVarChar, 500).Value = (object?)o.ProjectAddress ?? DBNull.Value;
        cmd.Parameters.Add("@city", SqlDbType.NVarChar, 150).Value = (object?)o.ProjectCity ?? DBNull.Value;
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 20).Value = (object?)o.ProjectProvince ?? DBNull.Value;
        cmd.Parameters.Add("@postal", SqlDbType.NVarChar, 20).Value = (object?)o.ProjectPostalCode ?? DBNull.Value;
        AddDecimal(cmd, "@lat", precision: 9, scale: 6, value: o.ProjectLatitude);
        AddDecimal(cmd, "@lng", precision: 9, scale: 6, value: o.ProjectLongitude);

        cmd.Parameters.Add("@disc", SqlDbType.Int).Value = (int)o.Discipline;
        cmd.Parameters.Add("@ctype", SqlDbType.NVarChar, 100).Value = (object?)o.ConstructionType ?? DBNull.Value;
        cmd.Parameters.Add("@pcat", SqlDbType.NVarChar, 100).Value = (object?)o.ProjectCategory ?? DBNull.Value;

        AddDecimal(cmd, "@value", precision: 18, scale: 2, value: o.EstimatedValue);
        cmd.Parameters.Add("@ccy", SqlDbType.NVarChar, 3).Value = string.IsNullOrEmpty(o.EstimatedValueCurrency) ? "CAD" : o.EstimatedValueCurrency;
        cmd.Parameters.Add("@rfpDate", SqlDbType.Date).Value = o.RfpReleaseDate.HasValue
            ? (object)o.RfpReleaseDate.Value.ToDateTime(TimeOnly.MinValue)
            : DBNull.Value;
        cmd.Parameters.Add("@deadline", SqlDbType.DateTimeOffset).Value = (object?)o.SubmissionDeadlineUtc ?? DBNull.Value;

        cmd.Parameters.Add("@status", SqlDbType.Int).Value = (int)o.Status;
        cmd.Parameters.Add("@identifiedAt", SqlDbType.DateTimeOffset).Value = o.IdentifiedAtUtc;
        cmd.Parameters.Add("@reviewingAt", SqlDbType.DateTimeOffset).Value = (object?)o.ReviewingSinceUtc ?? DBNull.Value;
        cmd.Parameters.Add("@qualifiedAt", SqlDbType.DateTimeOffset).Value = (object?)o.QualifiedAtUtc ?? DBNull.Value;
        cmd.Parameters.Add("@pursuingAt", SqlDbType.DateTimeOffset).Value = (object?)o.PursuingSinceUtc ?? DBNull.Value;
        cmd.Parameters.Add("@submittedAt", SqlDbType.DateTimeOffset).Value = (object?)o.ProposalSubmittedAtUtc ?? DBNull.Value;
        cmd.Parameters.Add("@outcomeAt", SqlDbType.DateTimeOffset).Value = (object?)o.OutcomeAtUtc ?? DBNull.Value;

        cmd.Parameters.Add("@ownerId", SqlDbType.NVarChar, 150).Value = (object?)o.OwnerStaffId ?? DBNull.Value;
        cmd.Parameters.Add("@reviewerIds", SqlDbType.NVarChar, 500).Value = (object?)o.ReviewerStaffIds ?? DBNull.Value;

        AddDecimal(cmd, "@score", precision: 10, scale: 4, value: o.RelevanceScore);
        cmd.Parameters.Add("@tier", SqlDbType.Int).Value = o.RelevanceTier.HasValue ? (object)(int)o.RelevanceTier.Value : DBNull.Value;

        cmd.Parameters.Add("@outcome", SqlDbType.Int).Value = o.WonLostOutcome.HasValue ? (object)(int)o.WonLostOutcome.Value : DBNull.Value;
        cmd.Parameters.Add("@outcomeReason", SqlDbType.NVarChar, 2000).Value = (object?)o.OutcomeReason ?? DBNull.Value;
        cmd.Parameters.Add("@wbs1", SqlDbType.NVarChar, 50).Value = (object?)o.WonProjectWbs1 ?? DBNull.Value;
        AddDecimal(cmd, "@awarded", precision: 18, scale: 2, value: o.AwardedValue);

        cmd.Parameters.Add("@isPrime", SqlDbType.Bit).Value = o.IsPrimeConsultantRfp.HasValue ? (object)o.IsPrimeConsultantRfp.Value : DBNull.Value;
        AddDecimal(cmd, "@primeConfidence", precision: 4, scale: 3, value: o.PrimeConfidence);
        cmd.Parameters.Add("@primeDisciplineType", SqlDbType.NVarChar, 40).Value = (object?)o.PrimeDisciplineType ?? DBNull.Value;
        cmd.Parameters.Add("@primeProjectSector", SqlDbType.NVarChar, 40).Value = (object?)o.PrimeProjectSector ?? DBNull.Value;
        cmd.Parameters.Add("@primeLikelyType", SqlDbType.NVarChar, 20).Value = (object?)o.PrimeLikelyType ?? DBNull.Value;
        cmd.Parameters.Add("@primeKorSectorMatch", SqlDbType.Bit).Value = o.PrimeKorSectorMatch.HasValue ? (object)o.PrimeKorSectorMatch.Value : DBNull.Value;
        cmd.Parameters.Add("@primeKorLocationMatch", SqlDbType.Bit).Value = o.PrimeKorLocationMatch.HasValue ? (object)o.PrimeKorLocationMatch.Value : DBNull.Value;
        cmd.Parameters.Add("@primeClassifiedAt", SqlDbType.DateTimeOffset).Value = (object?)o.PrimeClassifiedAtUtc ?? DBNull.Value;
    }

    /// <summary>
    /// SqlClient defaults <c>SqlDbType.Decimal</c> parameters to precision 18 / scale 0,
    /// which silently truncates fractional digits. Always set Precision/Scale to match
    /// the column's declared shape.
    /// </summary>
    private static void AddDecimal(SqlCommand cmd, string name, byte precision, byte scale, decimal? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
        p.Precision = precision;
        p.Scale = scale;
        p.Value = (object?)value ?? DBNull.Value;
    }

    /// <summary>
    /// Maps a single row from <see cref="AllColumns"/> order to <see cref="Opportunity"/>.
    /// Column ordinals are hard-coded against <see cref="AllColumns"/>; if you reorder
    /// that constant you must reorder this method to match.
    /// </summary>
    private static Opportunity MapReader(SqlDataReader r) => new()
    {
        Id = r.GetInt64(0),
        OpportunityKey = r.GetString(1),
        Name = r.GetString(2),

        BuyerName = r.GetString(3),
        BuyerType = (BuyerType)r.GetInt32(4),
        DeltekClientId = r.IsDBNull(5) ? null : r.GetString(5),
        DeltekContactId = r.IsDBNull(6) ? null : r.GetString(6),
        BuyerContactName = r.IsDBNull(7) ? null : r.GetString(7),
        BuyerContactEmail = r.IsDBNull(8) ? null : r.GetString(8),
        BuyerContactPhone = r.IsDBNull(9) ? null : r.GetString(9),

        ProjectAddress = r.IsDBNull(10) ? null : r.GetString(10),
        ProjectCity = r.IsDBNull(11) ? null : r.GetString(11),
        ProjectProvince = r.IsDBNull(12) ? null : r.GetString(12),
        ProjectPostalCode = r.IsDBNull(13) ? null : r.GetString(13),
        ProjectLatitude = r.IsDBNull(14) ? null : r.GetDecimal(14),
        ProjectLongitude = r.IsDBNull(15) ? null : r.GetDecimal(15),

        Discipline = (OpportunityDiscipline)r.GetInt32(16),
        ConstructionType = r.IsDBNull(17) ? null : r.GetString(17),
        ProjectCategory = r.IsDBNull(18) ? null : r.GetString(18),

        EstimatedValue = r.IsDBNull(19) ? null : r.GetDecimal(19),
        EstimatedValueCurrency = r.GetString(20),
        RfpReleaseDate = r.IsDBNull(21) ? null : DateOnly.FromDateTime(r.GetDateTime(21)),
        SubmissionDeadlineUtc = r.IsDBNull(22) ? null : r.GetDateTimeOffset(22),

        Status = (OpportunityStatus)r.GetInt32(23),
        IdentifiedAtUtc = r.GetDateTimeOffset(24),
        ReviewingSinceUtc = r.IsDBNull(25) ? null : r.GetDateTimeOffset(25),
        QualifiedAtUtc = r.IsDBNull(26) ? null : r.GetDateTimeOffset(26),
        PursuingSinceUtc = r.IsDBNull(27) ? null : r.GetDateTimeOffset(27),
        ProposalSubmittedAtUtc = r.IsDBNull(28) ? null : r.GetDateTimeOffset(28),
        OutcomeAtUtc = r.IsDBNull(29) ? null : r.GetDateTimeOffset(29),

        OwnerStaffId = r.IsDBNull(30) ? null : r.GetString(30),
        ReviewerStaffIds = r.IsDBNull(31) ? null : r.GetString(31),

        RelevanceScore = r.IsDBNull(32) ? null : r.GetDecimal(32),
        RelevanceTier = r.IsDBNull(33) ? null : (RelevanceTier?)r.GetInt32(33),

        WonLostOutcome = r.IsDBNull(34) ? null : (WonLostOutcome?)r.GetInt32(34),
        OutcomeReason = r.IsDBNull(35) ? null : r.GetString(35),
        WonProjectWbs1 = r.IsDBNull(36) ? null : r.GetString(36),
        AwardedValue = r.IsDBNull(37) ? null : r.GetDecimal(37),

        IsPrimeConsultantRfp = r.IsDBNull(38) ? null : r.GetBoolean(38),
        PrimeConfidence = r.IsDBNull(39) ? null : r.GetDecimal(39),
        PrimeDisciplineType = r.IsDBNull(40) ? null : r.GetString(40),
        PrimeProjectSector = r.IsDBNull(41) ? null : r.GetString(41),
        PrimeLikelyType = r.IsDBNull(42) ? null : r.GetString(42),
        PrimeKorSectorMatch = r.IsDBNull(43) ? null : r.GetBoolean(43),
        PrimeKorLocationMatch = r.IsDBNull(44) ? null : r.GetBoolean(44),
        PrimeClassifiedAtUtc = r.IsDBNull(45) ? null : r.GetDateTimeOffset(45),

        CreatedAtUtc = r.GetDateTimeOffset(46),
        CreatedBy = r.GetString(47),
        UpdatedAtUtc = r.GetDateTimeOffset(48),
        UpdatedBy = r.GetString(49),

        RowVersion = (byte[])r.GetValue(50),
    };

    private static Opportunity WithPrimeClassification(Opportunity opportunity)
    {
        var c = PrimeConsultantClassifier.Classify(opportunity);
        return opportunity with
        {
            IsPrimeConsultantRfp = c.IsPrimeConsultantRfp,
            PrimeConfidence = c.Confidence,
            PrimeDisciplineType = c.DisciplineType,
            PrimeProjectSector = c.ProjectSector,
            PrimeLikelyType = c.LikelyPrimeType,
            PrimeKorSectorMatch = c.KorSectorMatch,
            PrimeKorLocationMatch = c.KorLocationMatch,
            PrimeClassifiedAtUtc = DateTimeOffset.UtcNow,
        };
    }
}
