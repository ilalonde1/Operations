# Codex: Add `--kind org-classify` to BdQueueDrainIngest

## Goal

Add a new `--kind org-classify` mode to `tools/BdQueueDrainIngest/Program.cs`
that reads classify-{id}.json output files from the `classify-unknown-orgs`
drain and UPDATEs `opportunities.CanonicalOrg.Kind` where Kind is still
'Unknown'.

## Pattern to follow

Mirror the `org-name-repair` case (`case "org-name-repair":`) at line ~751
of `tools/BdQueueDrainIngest/Program.cs`. Same structure: parse the envelope,
extract fields, validate, run the UPDATE, move to processed/.

## Changes

### 1. `tools/BdQueueDrainIngest/Program.cs`

**a) Add `org-classify` to the --kind validation check** (line ~29):
```csharp
if (kind is not ("people" or "orgs" or "ab-projects" or "proponents" or "org-name-repair" or "org-classify"))
```

**b) Add `org-classify` to the `idPattern` switch** (line ~97):
```csharp
"org-classify"    => new Regex(@"^classify-(\d+)\.json$", RegexOptions.IgnoreCase),
```

**c) Add `org-classify` to the `expectedEnvelopeKind` switch** (line ~107):
```csharp
"org-classify"    => "org-classify",
```

**d) Add `org-classify` to the `orgProviderWhitelist`** — add `"OrgClassify"`:
```csharp
var orgProviderWhitelist = new[] { "FirmNarrative", "FirmNarrativeHoning", "OrgClassify" };
```

**e) Add the `case "org-classify":` block** in the switch at line ~497,
AFTER the existing `case "orgs":` block and BEFORE `case "ab-projects":`:

```csharp
case "org-classify":
    {
        // Reads classify-{id}.json from the classify-unknown-orgs drain.
        // Validates canonicalOrgId echo + resolvedKind whitelist + confidence >= 0.75.
        // UPDATEs CanonicalOrg.Kind only when the row is still Unknown and not retired.
        long? classifyEchoedId = null;
        string? classifyDisplayName = null, resolvedKind = null;
        double classifyConfidence = 0;
        using (var cdoc = JsonDocument.Parse(briefJson))
        {
            var croot = cdoc.RootElement;
            if (croot.TryGetProperty("canonicalOrgId", out var cid) && cid.TryGetInt64(out var cidVal))
                classifyEchoedId = cidVal;
            if (croot.TryGetProperty("displayName", out var cdn) && cdn.ValueKind == JsonValueKind.String)
                classifyDisplayName = cdn.GetString();
            if (croot.TryGetProperty("resolvedKind", out var rk) && rk.ValueKind == JsonValueKind.String)
                resolvedKind = rk.GetString()?.Trim();
            if (croot.TryGetProperty("confidence", out var cf) && cf.ValueKind == JsonValueKind.Number)
                classifyConfidence = cf.GetDouble();
        }

        if (classifyEchoedId != id)
        {
            log.LogWarning("Skipping {Name}: payload canonicalOrgId={Echoed} does not match filename id={Id}.", name, classifyEchoedId, id);
            skipped++;
            continue;
        }

        var validKinds = new[] { "Architect", "Buyer", "GC", "Competitor", "Developer", "KorClient" };
        if (resolvedKind is null || !validKinds.Contains(resolvedKind, StringComparer.OrdinalIgnoreCase))
        {
            log.LogWarning("Skipping {Name}: resolvedKind '{Kind}' is not in the allowed list [{Valid}].", name, resolvedKind, string.Join(", ", validKinds));
            skipped++;
            continue;
        }

        if (classifyConfidence < 0.75)
        {
            log.LogWarning("Skipping {Name}: confidence {Conf:0.##} below 0.75 threshold.", name, classifyConfidence);
            skipped++;
            continue;
        }

        await using var kcon = new Microsoft.Data.SqlClient.SqlConnection(cs);
        await kcon.OpenAsync().ConfigureAwait(false);
        await using var kcmd = new Microsoft.Data.SqlClient.SqlCommand(
            @"UPDATE opportunities.CanonicalOrg
              SET Kind = @kind, UpdatedAtUtc = SYSDATETIMEOFFSET()
              WHERE Id = @id AND Kind = N'Unknown' AND RetiredAtUtc IS NULL;
              SELECT @@ROWCOUNT;", kcon);
        kcmd.Parameters.AddWithValue("@kind", resolvedKind);
        kcmd.Parameters.AddWithValue("@id", id);
        var rowsAffected = (int)(await kcmd.ExecuteScalarAsync().ConfigureAwait(false))!;
        if (rowsAffected == 0)
        {
            log.LogWarning("{Name}: no update applied for Id={Id} — org may already be classified, retired, or missing.", name, id);
            skipped++;
            continue;
        }
        log.LogInformation("{Name}: CanonicalOrg Id={Id} '{Display}' → Kind={Kind} (confidence={Conf:0.##})", name, id, classifyDisplayName, resolvedKind, classifyConfidence);
        ok++;
    }
    break;
```

## Constraints

- Do NOT run dotnet build or dotnet test
- Do NOT change the existing `orgs` case
- Do NOT change any other file
- The resolvedKind comparison should be case-insensitive but the DB write
  should use the exact canonical casing from `validKinds` — use
  `validKinds.First(k => string.Equals(k, resolvedKind, StringComparison.OrdinalIgnoreCase))`
  as the value passed to `@kind`
