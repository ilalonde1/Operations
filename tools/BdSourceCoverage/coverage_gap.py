"""Measure our development-application coverage against every BC municipality.

Vancouver was found by accident. This asks the question once, for all of them:
of the municipalities in the footprint KOR actually works in, which ones do we
hold a development-application feed for, and which do we not?

Population figures are 2021 census, entered here only for the municipalities in
the target regions so the gap list can be ranked by size. A missing feed for
Surrey matters differently than a missing feed for Belcarra.
"""
import io
import json
import os
import re

SP = os.path.dirname(os.path.abspath(__file__))

d = json.load(io.open(os.path.join(SP, "bcmuni.json"), encoding="utf-8"))
names = sorted({f["properties"]["ADMIN_AREA_NAME"] for f in d["features"]})
print("BC municipalities in the provincial dataset: %d" % len(names))


def bare(n):
    n = re.sub(r"^(City|District|Town|Village|Township|Corporation|Resort Municipality|"
               r"District Municipality|The Corporation of)\s+of\s+", "", n)
    n = re.sub(r"^(City|District|Town|Village|Township|Resort Municipality)\s+", "", n)
    return n.strip()


# The regions KOR sells into. Everything else in BC is out of footprint.
REGIONS = {
    "Metro Vancouver": {
        "Vancouver": 662248, "Surrey": 568322, "Burnaby": 249125, "Richmond": 209937,
        "Coquitlam": 148625, "Langley (Township)": 132603, "Delta": 108455,
        "North Vancouver (District)": 88168, "Maple Ridge": 90990,
        "New Westminster": 78916, "Port Coquitlam": 61498, "North Vancouver (City)": 58120,
        "West Vancouver": 44122, "Port Moody": 33535, "Langley (City)": 28963,
        "White Rock": 21939, "Pitt Meadows": 19146, "Bowen Island": 4256,
        "Anmore": 2356, "Lions Bay": 1334, "Belcarra": 692,
    },
    "Fraser Valley": {
        "Abbotsford": 153524, "Chilliwack": 93203, "Mission": 41519,
        "Hope": 6686, "Harrison Hot Springs": 1717, "Kent": 6502,
    },
    "Vancouver Island": {
        "Victoria": 91867, "Nanaimo": 99863, "Saanich": 117735, "Langford": 46584,
        "Campbell River": 35519, "Courtenay": 28420, "Colwood": 18961,
        "Parksville": 13642, "Qualicum Beach": 9111, "Comox": 14806,
        "Esquimalt": 17533, "Oak Bay": 17990, "Central Saatnich": 17385,
        "North Saanich": 12094, "Sidney": 12318, "View Royal": 11575,
        "Sooke": 15086, "Duncan": 5047, "North Cowichan": 31990,
        "Port Alberni": 18259, "Powell River": 13943,
    },
    "Okanagan / Interior": {
        "Kelowna": 144576, "Kamloops": 97902, "Vernon": 44519,
        "West Kelowna": 36078, "Penticton": 36885, "Lake Country": 15817,
        "Salmon Arm": 19432, "Summerland": 12042,
    },
}

# The sources we actually hold, from the live query.
HAVE_APPLICATIONS = {
    "Vancouver", "Coquitlam", "Maple Ridge", "Victoria", "Saanich", "Langford",
    "Colwood", "Campbell River", "Courtenay", "Comox", "Qualicum Beach",
}
PARTIAL = {
    "View Royal": "source exists but DISABLED",
    "Parksville": "manual PDF load, DISABLED",
    "Nanaimo": "'What's Building' page only, not an application feed",
}

total = held = 0
rows = []
for region, munis in REGIONS.items():
    for m, pop in munis.items():
        total += 1
        key = bare(m).split(" (")[0]
        if m in HAVE_APPLICATIONS or key in HAVE_APPLICATIONS:
            held += 1
            status = "HELD"
        elif m in PARTIAL or key in PARTIAL:
            status = "PARTIAL - " + PARTIAL.get(m, PARTIAL.get(key, ""))
        else:
            status = "MISSING"
        rows.append((region, m, pop, status))

print("in-footprint municipalities: %d | application feed held: %d" % (total, held))
print()

for region in REGIONS:
    rr = [r for r in rows if r[0] == region]
    miss = [r for r in rr if r[3] != "HELD"]
    pop_missing = sum(r[2] for r in rr if r[3] == "MISSING")
    print("== %-22s %2d of %2d held | %s people live where we have no feed"
          % (region, len(rr) - len(miss), len(rr), "{:,}".format(pop_missing)))
    for _, m, pop, st in sorted(miss, key=lambda r: -r[2]):
        print("     %-28s %9s  %s" % (m, "{:,}".format(pop), st))
    print()

allmiss = sorted([r for r in rows if r[3] == "MISSING"], key=lambda r: -r[2])
print("TOP 12 GAPS BY POPULATION")
for _, m, pop, _ in allmiss[:12]:
    print("   %-28s %9s" % (m, "{:,}".format(pop)))
print()
print("people in the footprint with NO application feed: %s of %s"
      % ("{:,}".format(sum(r[2] for r in rows if r[3] == "MISSING")),
         "{:,}".format(sum(r[2] for r in rows))))
