Refresh intelligence on **{PERSON_DISPLAY_NAME}** for KOR Structural's
BD platform. Today is {TODAY_UTC}.

What KOR currently knows:
- Current title: {CURRENT_TITLE}
- Current employer: {CURRENT_EMPLOYER_NAME}
- IntelPerson id: {INTEL_PERSON_ID}

Your job:
1. CONFIRM the person is still at {CURRENT_EMPLOYER_NAME} in the
   stated role. If they've moved, set currentAffiliation.confirmed=false
   and put the move details in person.notes.
2. UPDATE contact fields (email, phone, linkedinUrl) only when you
   find them with high confidence. Do not invent.
3. UPDATE person.notes with a fresh 2-4 sentence summary covering
   role scope, recent moves (last 18 months), decision-making
   influence over structural engineering selection, and any KOR-
   relevant signal.
4. SURFACE recent signals (last 18 months) — leadership changes,
   promotions, public statements about capital plans / structural
   priorities / vendor preferences, conference appearances, etc.
5. PROPOSE concrete KOR actions targeting this individual —
   contact strategy, pursuit angle, timing window. Generic
   firm-level advice does NOT belong here.

Web-search the person's name + employer, LinkedIn, the employer's
news page, and (if a public official) the relevant ministry /
authority press releases.

Output ONLY the JSON object per the system prompt schema. No prose,
no markdown fences, no backticks.
