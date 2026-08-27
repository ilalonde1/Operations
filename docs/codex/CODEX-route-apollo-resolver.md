# Codex: Route BdApolloEnrich person identity through usp_ResolveOrCreateIntelPerson

## Goal

`tools/BdApolloEnrich/Program.cs` currently creates `IntelPerson` rows with a
name-only key (`PKey = SHA1(normalize(name))`, then `MERGE opportunities.IntelPerson
ON T.NaturalKey = S.PKey`). That diverges from the shared resolver proc's identity
(email → linkedin → name+org) and mints duplicate people. Resolve every contact's
`PersonId` through `opportunities.usp_ResolveOrCreateIntelPerson` instead, and delete
the name-only person MERGE. Leave the affiliation path alone.

## Pattern to follow

Mirror `Kor.Opportunities.Data/Intel/IntelPersistenceService.cs` →
`ResolveOrCreatePersonAsync` (the proc-call shape: `CommandType.StoredProcedure`,
typed parameters, `@personId` OUTPUT, `Convert.ToInt64`).

## Changes

### 1. `tools/BdApolloEnrich/Program.cs`

**a) In the `foreach (var c in contacts)` loop (~line 204)**, resolve the PersonId via
the proc first, then carry it into the `#c` INSERT. Replace the loop body with:

```csharp
foreach (var c in contacts)
{
    long personId;
    await using (var rp = new SqlCommand("opportunities.usp_ResolveOrCreateIntelPerson", con, tx)
        { CommandType = CommandType.StoredProcedure })
    {
        rp.Parameters.Add("@displayName", SqlDbType.NVarChar, 400).Value = c.DisplayName;
        rp.Parameters.Add("@email", SqlDbType.NVarChar, 400).Value = (object?)c.Email ?? DBNull.Value;
        rp.Parameters.Add("@linkedinUrl", SqlDbType.NVarChar, 800).Value = (object?)c.Linkedin ?? DBNull.Value;
        rp.Parameters.Add("@orgId", SqlDbType.BigInt).Value = c.OrgId;
        rp.Parameters.Add("@sourceProviderName", SqlDbType.NVarChar, 200).Value = provider;
        rp.Parameters.Add("@emailSource", SqlDbType.NVarChar, 50).Value =
            string.IsNullOrWhiteSpace(c.Email) ? (object)DBNull.Value : "Apollo";
        rp.Parameters.Add("@emailConfidence", SqlDbType.Int).Value = c.EmailConfidence;
        var pidOut = rp.Parameters.Add("@personId", SqlDbType.BigInt);
        pidOut.Direction = ParameterDirection.Output;
        await rp.ExecuteNonQueryAsync();
        personId = Convert.ToInt64(pidOut.Value);
    }

    await using var ins = new SqlCommand(
        "INSERT INTO #c (OrgId, Person, Title, Email, Linkedin, Conf, PersonId) VALUES (@o,@p,@t,@e,@l,@cf,@pid)", con, tx);
    ins.Parameters.AddWithValue("@o", c.OrgId);
    ins.Parameters.AddWithValue("@p", c.DisplayName);
    ins.Parameters.AddWithValue("@t", (object?)c.Title ?? DBNull.Value);
    ins.Parameters.AddWithValue("@e", (object?)c.Email ?? DBNull.Value);
    ins.Parameters.AddWithValue("@l", (object?)c.Linkedin ?? DBNull.Value);
    ins.Parameters.AddWithValue("@cf", c.EmailConfidence);
    ins.Parameters.AddWithValue("@pid", personId);
    await ins.ExecuteNonQueryAsync();
}
```

**b) In the `merge` SQL string (~line 219), DELETE the name-only person identity** —
the proc owns it now. Remove these three pieces:

- the `UPDATE #c SET PKey = CONVERT(CHAR(40), HASHBYTES('SHA1', ...))` statement (~line 228);
- the entire `MERGE opportunities.IntelPerson WITH (HOLDLOCK) AS T ... ;` block (~lines 239–256);
- the `UPDATE c SET c.PersonId = p.Id FROM #c c JOIN opportunities.IntelPerson p ON p.NaturalKey = c.PKey;` statement (~line 258).

**c) Keep the rest of the `merge` SQL:** the `NormTitle` part of the `UPDATE #c` (AffKey
needs it), the `@enr` `CanonicalOrgEnrichment` MERGE, the `AffKey` UPDATE, and the
`MERGE opportunities.IntelPersonAffiliation` block — all unchanged. `#c.PersonId` is now
populated at insert time (change **a**), so the affiliation MERGE keys off the proc's id.

**d) Fix the final count SELECT (~line 274)** — `PKey` no longer exists; count people by id:

```sql
SELECT (SELECT COUNT(DISTINCT PersonId) FROM #c), (SELECT COUNT(DISTINCT AffKey) FROM #c), (SELECT COUNT(*) FROM @enr);
```

## Constraints

- Do NOT run dotnet build or dotnet test
- Do NOT change `BdContactEnrich` or any other file
- Keep every command on the existing `con` / `tx` transaction
- Preserve the returned `(persons, affs, orgs)` tuple shape
- `CommandType`, `ParameterDirection`, and `SqlDbType` are in `System.Data` — add
  `using System.Data;` if it is not already imported
