import sys
p = sys.argv[1]
s = open(p, encoding='utf-8').read()
start = s.index('        // ── WHOLE-BUILDING: add COLUMN + WALL concrete from the schedule sheets')
end = s.index('            // ── SCHEDULE-FIRST COLUMNS')
new = (
    '        // ── WHOLE-BUILDING: COLUMN + WALL concrete ─────────────────────────────────────────────────────────\n'
    '        // The column and shear-wall SCHEDULE reads were vision only and were removed with it (2026-10-20, C1),\n'
    '        // so colSched stays empty and the engine\'s gray-fill column and wall rows are the priced source on every\n'
    '        // floor; the composition below is unchanged and, with no schedule rows, changes nothing.\n'
    '        try\n'
    '        {\n'
    '            var tkDig = DrawingDigestBuilder.Build(args[1], tkFirst, tkLast);\n'
    '            double typicalStoreyIn = tkStoreyIn;   // typical fallback; per-level heights below when supplied\n'
    '            double wallCuYd = 0; int wallSheets = 0, wallMarks = 0;\n'
    '            var colSched = new List<(int Page, string? Tower, ScheduleTakeoff.ScheduleResult Res)>();\n'
    '\n'
)
assert s.count('        // ── WHOLE-BUILDING: add COLUMN + WALL concrete from the schedule sheets') == 1
s = s[:start] + new + s[end:]
open(p, 'w', encoding='utf-8', newline='').write(s)
print('cut', end - start, 'chars')
