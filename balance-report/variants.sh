#!/bin/bash
# Makes the data folders the balance report measured, under out/data/. Run from the repository
# root. "two-maps" is game/data with the test map switchback added and no number changed; every
# other folder is two-maps with the one edit its line names. Nothing under game/data is touched.
set -e
out=out/data
rm -rf "$out"; mkdir -p "$out"
cp -r game/data "$out/two-maps"
cp tests/golden/data/maps/switchback.jsonc "$out/two-maps/maps/"

variant() {
  name=$1; shift
  cp -r "$out/two-maps" "$out/$name"
  while [ $# -gt 0 ]; do
    file="$out/$name/$1"; before=$(md5sum < "$file"); sed -i -E "$2" "$file"
    [ "$before" != "$(md5sum < "$file")" ] || { echo "variants.sh: the edit of $1 for $name changed nothing" >&2; exit 1; }
    shift 2
  done
}

estimate() { variant "$1" ai/swarm.jsonc "s/\"assumed_enemy_value\": 400/\"assumed_enemy_value\": $2/" ai/elite.jsonc "s/\"assumed_enemy_value\": 400/\"assumed_enemy_value\": $2/"; }
workers='s/"epoch": 1, "workers": 18/"epoch": 1, "workers": W1/; s/"epoch": 2, "workers": 26/"epoch": 2, "workers": W2/; s/"epoch": 3, "workers": 32/"epoch": 3, "workers": W3/'
swarm_workers() { echo "$workers" | sed "s/W1/$1/; s/W2/$2/; s/W3/$3/"; }

# The computer's estimate of an enemy army it has not seen (both personalities), 400 in the data.
for value in 800 1200 1600 2400 3000 3600 4800; do estimate aev$value $value; done
estimate peace 1000000
# The Capitol's hit points, 2,000 in the data.
variant cap4000 structures/capitol.jsonc 's/"hp": 2000/"hp": 4000/'
# The design's four levers (3.1): supply per Logistics tier, the quality price, the blast share
# of Overgunned II, the Manufacturing cost step.
variant sup20 rules.jsonc 's/"per_logistics_tier": 15/"per_logistics_tier": 20/'
variant q125 rules.jsonc 's/"quality_cost": "1.15"/"quality_cost": "1.25"/'
variant splash02 accents.jsonc 's/"share": "0.3"/"share": "0.2"/'
variant man085 rules.jsonc 's/"manufacturing_cost": "0.90"/"manufacturing_cost": "0.85"/'
variant man080 rules.jsonc 's/"manufacturing_cost": "0.90"/"manufacturing_cost": "0.80"/'
# The design's first lever for battle 3: the range of Line infantry, 5 in the data.
for range in 4 3 2; do variant range$range lines/line_infantry.jsonc "s/\"range\": 5, \"projectile_speed\"/\"range\": $range, \"projectile_speed\"/"; done
# What Overgunned II's splash reaches: its radius, 1 in the data, and the spacing of the two
# infantry lines, 1 in the data.
for radius in 1.02 1.05 1.1 1.25 1.5; do
  variant "splash$(echo "$radius" | tr -d . | sed 's/^11$/110/; s/^15$/150/')" accents.jsonc "s/\"splash\": \{ \"share\": \"0.3\", \"radius\": 1 \}/\"splash\": { \"share\": \"0.3\", \"radius\": \"$radius\" }/"
done
variant spacing09 lines/line_infantry.jsonc 's/"spacing": "1"/"spacing": "0.9"/' lines/guard.jsonc 's/"spacing": "1"/"spacing": "0.9"/'
# The time of a tier 4 research, 75 seconds in the data.
variant t4s105 research.jsonc 's/("tier": 4, .*"seconds": )75/\1105/'
# The price of a tier 4 research, 225 Materials and 350 Energy in the data, times 1.25.
variant t4x125 research.jsonc 's/"tier": 4, "cost": \{ "materials": 225, "energy": 350 \}/"tier": 4, "cost": { "materials": 281, "energy": 438 }/'
# The workers the Swarm personality keeps per epoch, 18, 26 and 32 in the data.
variant swworkers ai/swarm.jsonc "$(swarm_workers 16 22 28)"
variant sww17 ai/swarm.jsonc "$(swarm_workers 17 24 30)"
# Two at once.
variant aev2400man085 ai/swarm.jsonc 's/"assumed_enemy_value": 400/"assumed_enemy_value": 2400/' ai/elite.jsonc 's/"assumed_enemy_value": 400/"assumed_enemy_value": 2400/' rules.jsonc 's/"manufacturing_cost": "0.90"/"manufacturing_cost": "0.85"/'
variant sww17aev2400 ai/swarm.jsonc "$(swarm_workers 17 24 30); s/\"assumed_enemy_value\": 400/\"assumed_enemy_value\": 2400/" ai/elite.jsonc 's/"assumed_enemy_value": 400/"assumed_enemy_value": 2400/'
# A personality for measuring only: every track weighs the same, and it attacks only on what
# its scout has seen or at its supply cap. For the design's times "for a player who advances evenly".
variant even
cp balance-report/even.jsonc "$out/even/ai/even.jsonc"
for v in even-t4x125:'s/"tier": 4, "cost": \{ "materials": 225, "energy": 350 \}/"tier": 4, "cost": { "materials": 281, "energy": 438 }/' \
         even-t4x150:'s/"tier": 4, "cost": \{ "materials": 225, "energy": 350 \}/"tier": 4, "cost": { "materials": 338, "energy": 525 }/' \
         even-t4s90:'s/("tier": 4, .*"seconds": )75/\190/' \
         even-t4s105:'s/("tier": 4, .*"seconds": )75/\1105/'; do
  variant "${v%%:*}" research.jsonc "${v#*:}"
  cp balance-report/even.jsonc "$out/${v%%:*}/ai/even.jsonc"
done
# The even personality with the workers of Elite (16, 22, 28) and of Swarm (18, 26, 32).
for v in even-w16:16:22:28 even-w18:18:26:32; do
  IFS=: read -r name w1 w2 w3 <<< "$v"
  variant "$name"
  sed -E "s/\"epoch\": 1, \"workers\": 17/\"epoch\": 1, \"workers\": $w1/; s/\"epoch\": 2, \"workers\": 24/\"epoch\": 2, \"workers\": $w2/; s/\"epoch\": 3, \"workers\": 30/\"epoch\": 3, \"workers\": $w3/" balance-report/even.jsonc > "$out/$name/ai/even.jsonc"
done
ls "$out"
