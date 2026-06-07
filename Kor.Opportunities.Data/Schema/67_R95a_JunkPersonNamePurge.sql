SET XACT_ABORT ON;
GO

BEGIN TRAN;

;WITH JunkIntelPeople AS (
    SELECT p.Id, p.NaturalKey
    FROM opportunities.IntelPerson p
    CROSS APPLY (SELECT LTRIM(RTRIM(p.DisplayName)) AS TrimmedName) t
    CROSS APPLY (SELECT LOWER(t.TrimmedName) AS LowerName) l
    WHERE p.DisplayName IS NULL
       OR LTRIM(RTRIM(p.DisplayName)) = N''
       OR LEN(t.TrimmedName) < 4
       OR t.TrimmedName LIKE N'%<%'
       OR t.TrimmedName LIKE N'%>%'
       OR (LEN(t.TrimmedName) >= 3 AND LEFT(t.TrimmedName, 1) = N'[' AND RIGHT(t.TrimmedName, 1) = N']')
       OR l.LowerName LIKE N'unknown%'
       OR l.LowerName IN (N'tbd', N'tba', N'n/a', N'na', N'redacted')
       OR l.LowerName LIKE N'% tbd%'
       OR l.LowerName LIKE N'%tbd %'
       OR l.LowerName LIKE N'% tba %'
       OR l.LowerName LIKE N'%various%'
       OR l.LowerName LIKE N'%not yet%'
       OR l.LowerName LIKE N'%redacted%'
       OR l.LowerName LIKE N'%public servant%'
       OR l.LowerName LIKE N'%official%'
       OR l.LowerName LIKE N'%see notes%'
       OR (t.TrimmedName NOT LIKE N'% %'
           AND t.TrimmedName NOT LIKE N'%' + CHAR(9) + N'%'
           AND t.TrimmedName NOT LIKE N'%' + CHAR(10) + N'%'
           AND t.TrimmedName NOT LIKE N'%' + CHAR(13) + N'%')
)
SELECT Id, NaturalKey
INTO #JunkIntelPeople
FROM JunkIntelPeople;

;WITH JunkProjectKeyPeople AS (
    SELECT k.Id
    FROM opportunities.IntelProjectKeyPerson k
    CROSS APPLY (SELECT LTRIM(RTRIM(k.DisplayName)) AS TrimmedName) t
    CROSS APPLY (SELECT LOWER(t.TrimmedName) AS LowerName) l
    WHERE k.DisplayName IS NULL
       OR LTRIM(RTRIM(k.DisplayName)) = N''
       OR LEN(t.TrimmedName) < 4
       OR t.TrimmedName LIKE N'%<%'
       OR t.TrimmedName LIKE N'%>%'
       OR (LEN(t.TrimmedName) >= 3 AND LEFT(t.TrimmedName, 1) = N'[' AND RIGHT(t.TrimmedName, 1) = N']')
       OR l.LowerName LIKE N'unknown%'
       OR l.LowerName IN (N'tbd', N'tba', N'n/a', N'na', N'redacted')
       OR l.LowerName LIKE N'% tbd%'
       OR l.LowerName LIKE N'%tbd %'
       OR l.LowerName LIKE N'% tba %'
       OR l.LowerName LIKE N'%various%'
       OR l.LowerName LIKE N'%not yet%'
       OR l.LowerName LIKE N'%redacted%'
       OR l.LowerName LIKE N'%public servant%'
       OR l.LowerName LIKE N'%official%'
       OR l.LowerName LIKE N'%see notes%'
       OR (t.TrimmedName NOT LIKE N'% %'
           AND t.TrimmedName NOT LIKE N'%' + CHAR(9) + N'%'
           AND t.TrimmedName NOT LIKE N'%' + CHAR(10) + N'%'
           AND t.TrimmedName NOT LIKE N'%' + CHAR(13) + N'%')
)
SELECT Id
INTO #JunkProjectKeyPeople
FROM JunkProjectKeyPeople;

DECLARE @affiliationsDeleted int;
DECLARE @peopleDeleted int;
DECLARE @projectKeyPeopleDeleted int;

DELETE a
FROM opportunities.IntelPersonAffiliation a
INNER JOIN opportunities.IntelPerson p ON p.Id = a.IntelPersonId
INNER JOIN #JunkIntelPeople j ON j.NaturalKey = p.NaturalKey;
SET @affiliationsDeleted = @@ROWCOUNT;
PRINT 'IntelPersonAffiliation junk-name rows deleted: ' + CONVERT(varchar(20), @affiliationsDeleted);

DELETE p
FROM opportunities.IntelPerson p
INNER JOIN #JunkIntelPeople j ON j.NaturalKey = p.NaturalKey;
SET @peopleDeleted = @@ROWCOUNT;
PRINT 'IntelPerson junk-name rows deleted: ' + CONVERT(varchar(20), @peopleDeleted);

DELETE k
FROM opportunities.IntelProjectKeyPerson k
INNER JOIN #JunkProjectKeyPeople j ON j.Id = k.Id;
SET @projectKeyPeopleDeleted = @@ROWCOUNT;
PRINT 'IntelProjectKeyPerson junk-name rows deleted: ' + CONVERT(varchar(20), @projectKeyPeopleDeleted);

COMMIT TRAN;

PRINT 'Migration 67 R95a junk person-name purge complete.';
GO
