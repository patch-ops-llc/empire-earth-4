# When a match stops because the games disagree

This page is for testers. It says what to send when a match ends with this message:

> The match was stopped because the players' games no longer agreed on what had happened in it. This is a fault in the game, not something you did.

## What happened

In a match with someone else, every player's machine runs the whole game, and the games check each other about once a second. If they ever differ, even by one unit standing a hair to the side, the match cannot go on, so every game stops at the same moment and says so. Nobody did anything wrong, and nothing is wrong with your machine or your connection. It is a fault in the game, and it is one of the faults we most want to hear about.

## What to send

**One file from each player of the match.** Your game has already saved it. The message on screen names it, and the button **Show the file in its folder** under the message opens the folder with the file selected.

1. Send the file, as it is, to whoever gave you the build. Do not rename it or unpack it.
2. Ask the other player to send theirs. Each game saves its own, and they are not the same. One file alone seldom shows which game went wrong.
3. Add the usual: the name of the zip file your build came in, and a line about what was going on in the match when it stopped.

That is all. You do not need the replay, a screenshot or the log file for this: what is needed from them is in the file.

## Where the file is

If you have left the page with the message, the file is still there. It is in the folder `matches`:

- Windows: `%APPDATA%\Godot\app_userdata\empire-earth-4\matches`
- macOS: `~/Library/Application Support/Godot/app_userdata/empire-earth-4/matches`

On Windows, paste that line into the address bar of an Explorer window. On macOS, choose Go, then Go to Folder in the Finder and paste it there.

The file's name ends in `.desync.zip` and starts with the time the match began, then the map and your number as a player: `2026-11-12_19-30-05_ridge_p0.desync.zip` is the host's and `..._p1.desync.zip` the other player's. It is small, from a few kilobytes to a few megabytes.

## What is in the file

It is an ordinary zip file, and you may look inside. It holds:

- the replay of the match: the orders every player gave, and nothing else about the players;
- the state of the match at a few moments in its last seconds;
- which version of the game you have, and what it ran on: the name and version of your operating system, the kind of processor, and the language your system is set to.

It holds nothing else from your machine: not your name, not the machine's name, and no folder or file names.

## If the message names no file

Then the game could not save it, and the message says why, most often a full disk. Send what the message does name, and the log file `godot.log`, which [testers.md](testers.md) tells you how to find.

## For developers

`desync-diff` in `src/Tools` reads the files, two against each other or one against a run on your own machine. [determinism.md](determinism.md) describes the file and the tool under "Finding a desync".
