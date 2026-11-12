import csv, sys, re, statistics as st
def clock(s): s = int(float(s)); return f"{s//60}:{s%60:02d}"
def med(v): 
    v = sorted(v); return v[(len(v)-1)//2] if v else None
def battles(path):
    out = []
    for line in open(path):
        m = re.search(r"mass (west|east): (\w+) wins after ([\d.]+) s and keeps ([\d.]+)%", line)
        if m: out.append((m.group(2), float(m.group(4))))
    res = []
    for b in range(4):
        w, e = out[2*b], out[2*b+1]
        res.append(f"{b+1}:{w[0][0]}{w[1]:.0f}" + ("" if w == e else f"/{e[0][0]}{e[1]:.0f}"))
    met = re.search(r"battles: (.*)", open(path).read().splitlines()[-1]).group(1)
    return " ".join(res) + " | " + met
def series(path):
    rows = list(csv.DictReader(open(path)))
    lines = []
    for mp in sorted(set(r["map"] for r in rows)):
        R = [r for r in rows if r["map"] == mp]
        M = [r for r in R if r["player"] == "0"]
        L = [float(r["length_s"]) for r in M if r["ended"] == "conquest"]
        mixed = [r for r in R if r["personality"] != r["opponent"]]
        ew = sum(r["result"] == "won" and r["personality"] == "elite" for r in mixed)
        sw = sum(r["result"] == "won" and r["personality"] == "swarm" for r in mixed)
        mir = [r for r in R if r["personality"] == r["opponent"] and r["result"] == "won"]
        s = f"  {mp:10} n{len(M)} len {clock(med(L))} [{clock(min(L))}-{clock(max(L))}] <10:{sum(l<600 for l in L)} 10-15:{sum(600<=l<900 for l in L)} 15-25:{sum(900<=l<=1500 for l in L)} >25:{sum(l>1500 for l in L)} draw:{len(M)-len(L)} | mixed E{ew}:S{sw} of {len(mixed)//2} | mirror start0 {sum(r['start']=='0' for r in mir)}:{sum(r['start']=='1' for r in mir)}"
        lines.append(s)
        for p in ("elite", "swarm"):
            P = [r for r in R if r["personality"] == p]
            def t(col):
                v = [float(r[col]) for r in P if r[col] != ""]
                return f"{clock(med(v))}({len(v)})" if v else "never"
            at = [r for r in P if float(r["at_cap_s"] or 0) > 0]
            lines.append(f"     {p}: e2 {t('epoch_2_s')} e3 {t('epoch_3_s')} e4 {t('epoch_4_s')} all4 {t('every_track_at_cap_s')} attack {t('first_attack_order_s')} atcap {len(at)}/{len(P)} research {sum(sum(float(r[c]) for c in r if c.startswith('research_')) for r in P)/len(P):.0f}")
        fh = [float(r["first_hit_s"]) for r in M if r["first_hit_s"] != ""]
        bh = [float(r["length_s"]) - float(r["loser_behind_from_s"]) for r in M if r["loser_behind_from_s"] != ""]
        lines.append(f"     first hit {clock(med(fh))}; after behind {clock(med(bh))} [{clock(min(bh))}-{clock(max(bh))}]")
    return "\n".join(lines)
if __name__ == "__main__":
    for name in sys.argv[1:]:
        print(f"### {name}")
        try: print("  battles:", battles(f"out/{name}.battles.txt"))
        except Exception as e: print("  battles: ?", e)
        print(series(f"out/{name}.csv"))
