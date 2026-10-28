r"""six.py : from framingover.out, the storeys given only by framing-over sheets where the engineer's model has a plate."""
import os, re

job = None
n = 0
for line in open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "framingover.out"), encoding="utf-8"):
    if re.match(r"^\d{5}-\d\d:", line): job = line.split(":")[0]; continue
    m = re.match(r"\s+(\S+)\s+(\S+)\s+slabs\s+\d+\s+ours (.+?)\s+hers (.+?)\s+other sheets of \S+: (\S+)\s+\| (.*)", line)
    if m and m.group(4) not in ("none", "-") and m.group(5) == "none":
        n += 1
        print(job, m.group(1), m.group(2), "| ours", m.group(3), "| hers", m.group(4)[:70], "|", m.group(6)[:60])
print(n, "rows")
