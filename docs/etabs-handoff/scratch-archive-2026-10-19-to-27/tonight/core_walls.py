r"""core_walls.py <e2k_walls output> : keeps the lines whose two ends both fall in Halifax LEVEL 1's core box (x 37500-46500, y 27000-37000 mm).
COVERS: the first four numbers of each line as x1 y1 x2 y2. DOES NOT: headers (frames), openings."""
import re, sys
for line in open(sys.argv[1], encoding="utf-8"):
    nums = [float(v) for p in re.findall(r"\(([^)]*)\)", line) for v in p.split(",")]
    if len(nums) >= 4 and all(37500 <= nums[i] <= 46500 for i in (0, 2)) and all(27000 <= nums[i] <= 37000 for i in (1, 3)):
        print(line.rstrip())
