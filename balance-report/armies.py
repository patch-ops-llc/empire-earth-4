import csv, sys
from collections import defaultdict
def med(v):
    v = sorted(v); return v[(len(v)-1)//2] if v else None
for name in sys.argv[1:]:
    sam = list(csv.DictReader(open(f"out/{name}.samples.csv")))
    res = list(csv.DictReader(open(f"out/{name}.csv")))
    mixed = {(r["match"], r["player"]): r for r in res if r["personality"] != r["opponent"]}
    for mp in ("ridge", "switchback"):
        print(f"## {name} {mp}: mixed matches, median per personality at a time (n still running): workers / units / army value / supply used/cap / stock M+E")
        by = defaultdict(lambda: defaultdict(list))
        for s in sam:
            if s["map"] != mp or (s["match"], s["player"]) not in mixed: continue
            by[int(s["time_s"])][s["personality"]].append(s)
        for t in (180, 240, 300, 330, 360, 390, 420, 480, 540, 600, 720, 900):
            if t not in by: continue
            out = []
            for p in ("swarm", "elite"):
                S = by[t][p]
                out.append(f'{p[0]} n{len(S):2} w{med([int(x["workers"]) for x in S]):>2} u{med([int(x["units"]) for x in S]):>2} v{med([int(x["army_value"]) for x in S]):>5} s{med([int(x["supply_used"]) for x in S]):>3}/{med([int(x["supply_cap"]) for x in S]):<3} $ {med([int(x["materials"])+int(x["energy"]) for x in S]):>4}')
            print(f"  {t//60}:{t%60:02d}  " + "   |   ".join(out))
