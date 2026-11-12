#!/bin/bash
# series.sh <name> [<data folder>]: the acceptance battles and the series of 160 matches with
# the data in out/data/<name> (or the folder given), on the build of the branch that is checked
# out. Writes out/<name>.battles.txt, .txt, .csv and .samples.csv, and prints the short lines of
# summary.py. Run from the repository root after: dotnet build -c Release src/Tools
set -e
name=$1; data=${2:-out/data/$name}
tools="dotnet run --no-build -c Release --project src/Tools --"
mkdir -p out
$tools datacheck "$data" > "out/$name.datacheck.txt"
$tools battles --data "$data" > "out/$name.battles.txt"
$tools ai-series --data "$data" --delays 1,2,3,4,5,6,7,8,9,10 --minutes 40 --csv "out/$name.csv" --samples "out/$name.samples.csv" > "out/$name.txt"
python3 balance-report/summary.py "$name"
