/*
    rows_the_rule_loader_refuses.sql - the KorStandards rows RuleSettings.TryLoad (Operations, aa73cd4a) would REFUSE, which makes
    every rules-required DXF-to-ETABS build stop with "KorStandards rule settings could not be read". Expect 0 rows.

    For operations-ee's applier post-check after every migration that adds a ruling or a convention (2026-10-27: migrations
    131/132/134's units='rule' rows stopped every build until the loader learned to leave them out).

    The loader's rule, row by row (RuleSettings.KindOf):
      - SettingValue NULL                         -> skipped (never refused)
      - SettingUnits 'rule' (any case)            -> a ruling for people: left out, counted, never refused
      - SettingUnits a list unit (layers, names,
        patterns, sheets; any case)               -> refused only when the value is empty or blank
      - anything else (numbers, 'bool', 'regex')  -> accepted when it parses: for 'bool' a yes/no word (true, yes, y, apply, on;
                                                     false, no, n, off; 1, 0); for any unit a number, or a number anywhere in
                                                     the text (the loader takes the first); refused otherwise.
    APPROXIMATION, stated: the loader's "number anywhere" is the regex (?<![\d.])[-+]?\d+(?:\.\d+)? - a digit run not right
    after a digit or a dot; this SQL asks only for a digit anywhere, so a value whose only digits follow a dot (".5" glued to
    text, e.g. "x.5") passes here and is refused by the loader. None such in the store on 2026-10-27; the C# test
    ARulingWrittenForPeopleIsNotASettingTests is the authority.
*/
SELECT v.SettingKey, v.SettingValue, v.SettingUnits, v.Source
  FROM analysis.vw_RuleSetting AS v
 WHERE v.SettingValue IS NOT NULL
   AND LOWER(ISNULL(v.SettingUnits, N'')) <> N'rule'
   AND (
        -- a list with nothing in it
        (LOWER(ISNULL(v.SettingUnits, N'')) IN (N'layers', N'names', N'patterns', N'sheets')
            AND LTRIM(RTRIM(CONVERT(nvarchar(max), v.SettingValue))) = N'')
        OR
        -- a value that must parse and does not
        (LOWER(ISNULL(v.SettingUnits, N'')) NOT IN (N'layers', N'names', N'patterns', N'sheets')
            AND TRY_CONVERT(float, LTRIM(RTRIM(CONVERT(nvarchar(max), v.SettingValue)))) IS NULL
            AND PATINDEX(N'%[0-9]%', CONVERT(nvarchar(max), v.SettingValue)) = 0
            AND NOT (LOWER(ISNULL(v.SettingUnits, N'')) = N'bool'
                     AND (    LOWER(LTRIM(RTRIM(CONVERT(nvarchar(max), v.SettingValue)))) IN (N'true', N'yes', N'y', N'apply', N'false', N'no', N'n')
                          OR  ' ' + LOWER(CONVERT(nvarchar(max), v.SettingValue)) + ' ' LIKE N'%[^a-z]on[^a-z]%'
                          OR  ' ' + LOWER(CONVERT(nvarchar(max), v.SettingValue)) + ' ' LIKE N'%[^a-z]off[^a-z]%'
                          OR  ' ' + LOWER(CONVERT(nvarchar(max), v.SettingValue)) + ' ' LIKE N'%[^a-z]yes[^a-z]%'
                          OR  ' ' + LOWER(CONVERT(nvarchar(max), v.SettingValue)) + ' ' LIKE N'%[^a-z]no[^a-z]%'
                          OR  ' ' + LOWER(CONVERT(nvarchar(max), v.SettingValue)) + ' ' LIKE N'%[^a-z]true[^a-z]%'
                          OR  ' ' + LOWER(CONVERT(nvarchar(max), v.SettingValue)) + ' ' LIKE N'%[^a-z]false[^a-z]%'
                          OR  ' ' + LOWER(CONVERT(nvarchar(max), v.SettingValue)) + ' ' LIKE N'%[^a-z]apply[^a-z]%')))
       )
 ORDER BY v.SettingKey;
