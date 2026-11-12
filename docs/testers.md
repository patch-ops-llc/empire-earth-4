# Running a test build

This is an early build of a hobby real-time strategy prototype, and the first one two people can play together. Each player has six workers on a map with forest and a ridge of cliffs, and can select them and send them round it. There is nothing to build and nobody to fight yet. Its job for now is to show that the game starts on your machine, that selecting units and moving them works, and that two machines can share one match.

The builds are not signed, because signing costs money and this is a hobby project. Windows and macOS both warn about that the first time you start the game. The steps below get past the warning.

## What you need

- Windows 11 on a 64-bit Intel or AMD processor, or
- macOS 15 or later, on an Intel or Apple silicon Mac.

Nothing else has to be installed.

## Getting the build

Whoever invited you can send you the zip file. To fetch it yourself:

1. Sign in to GitHub. Downloads do not work when signed out.
2. Open the repository's Actions tab and choose the Export workflow.
3. Open the newest run with a green tick.
4. Under Artifacts, download the file whose name starts with `Game-windows-x86_64` or `Game-macos-universal`. The rest of the name is the version.

GitHub deletes the files of a run after a while, 90 days unless the repository says otherwise.

## Windows

1. Right-click the zip file and choose Extract All. The game does not run from inside the zip.
2. Open the extracted folder and start `Game.exe`. Keep `Game.exe`, `Game.pck` and the `data_Game_windows_x86_64` folder together.
3. Windows shows "Windows protected your PC". Click More info, then Run anyway.

## macOS

1. Double-click the zip file to unpack it.
2. Drag the app into the Applications folder. Do this before the first start: otherwise macOS runs the app from a hidden temporary copy, which can stop it from starting.
3. Open the app. macOS refuses and says it cannot check the app. Click Done.
4. Open System Settings, then Privacy & Security, and scroll down to Security. Next to the message about the app, click Open Anyway and confirm. The button is there for about an hour after the refused start.

If the button does not appear, open Terminal instead. Type `xattr -dr com.apple.quarantine` followed by a space, drag the app from the Applications folder onto the Terminal window, and press Return. Then open the app again.

## Starting a match

The game opens on a menu with three choices.

- **Play alone** starts a match at once, on your machine only. Nothing is opened to the network, so the system asks no question about connections, and no second copy of the game or second player is needed. The other player's workers are there, and nothing controls them.
- **Host a match** opens the match for one other player and waits. See the next section.
- **Join** joins a match someone else hosts. Type their address into the box first.

During a match, the bar across the top of the window shows on its left what you have: Materials, Energy and supply, then your epoch and what is in research. On its right it says which player you are, and its button, Leave, takes you back to the menu. In a wide window it also says how the match is played: alone, hosted or joined. While units are selected, a panel at the bottom of the window says how many.

## Playing with someone else

Both players need the same build. The name of the zip file carries the version, and the menu shows it under the title: the two must be the same. The Windows and the macOS zip of one run of the Export workflow are the same version and play together.

One player hosts:

1. Click Host a match. The port can stay as it is.
2. The first time, the system may ask whether the game may accept connections from the network. On Windows the question comes from Windows Defender Firewall: tick private networks and click Allow access. On macOS it asks whether the application may accept incoming network connections: click Allow. If you refuse, nobody can join.
3. The game shows this machine's address on its network, something like `192.168.1.20`. Tell the other player. If it shows two, the one that starts like the other player's own address is the right one.
4. When the other player has joined, the page says Player 2: ready. Click Start the match.

The other player joins:

1. Type the host's address into the box beside Join, and click Join. If the host changed the port, add a colon and the port: `192.168.1.20:24650`.
2. The page says when you are in and waiting for the host to start.

This is meant for two machines on the same network, such as the same home Wi-Fi. Across the internet the host has to forward UDP port 24642 on their router to their machine and give the other player their public address. That has not been tried.

If joining fails, the menu says why in orange:

- **No game answered.** The address or the port is wrong, the host is not on its waiting page, or a firewall on the host's machine or network is in the way.
- **No free seat, or the match has already started.** A match has two players, and nobody can join once it runs.
- **The host runs another version.** The two builds are not the same. Both players download the zips of one run.

## Controls

| Action | Input |
| --- | --- |
| Move the camera | W, A, S, D or the arrow keys, or push the pointer against an edge of the window |
| Zoom | Mouse wheel, the = and - keys, or a pinch or two-finger scroll on a trackpad |
| Select one of your units | Click it |
| Select several | Hold the left button and drag a box round them |
| Add to the selection | Hold Shift and drag a box. Shift and a click adds a unit, or takes it out if it is already in |
| Clear the selection | Click the ground |
| See what another player's unit is | Click it. A pale ring marks it and the panel at the bottom describes it. It cannot be given orders |
| Send the selected workers to work at a deposit | Right-click the deposit |
| Send the selected units somewhere | Right-click the ground. On a trackpad that is a click with two fingers, unless you have set it otherwise |
| Open or close the research panel | R, or the Research button in the bar at the top. Escape closes it too |
| Start a research | In the research panel, click a bright cell |
| Cancel a research | In the research panel, click Cancel beside its progress bar |
| Show or hide the readout in the corner | F3 |
| Quit | Close the window |

## Resources and workers

The brown blocks on the map are deposits. Select workers and right-click a deposit, and they walk to it, stand beside it and earn Materials for you. A yellow ring closes round the deposit you clicked.

- Each worker at a deposit earns 45 Materials a minute, paid a little every second from one second after it arrives. Your six workers at one deposit earn 4.5 a second.
- A deposit has room for eight workers, so all six of yours fit at one.
- A deposit pays only inside your territory, which for now is the ground round your start: the two deposits nearest your workers. Workers sent to any other deposit stand there and earn nothing, and a line under the top bar says so. Nothing can claim more territory yet.
- A worker stops earning when you send it somewhere else. Send it back to a deposit and it starts again.
- Nobody can walk through a deposit.
- Materials and Energy are shown as whole numbers. Supply is what you use out of what you may use: each worker uses 1.
- Nothing earns Energy yet. Research spends both: see Research below.

## What you will see

- The host's workers are blue and the other player's are red. Alone, yours are blue. Which side of the map each player starts on is chosen at random each time.
- Selected units have a green ring under them. A right-click leaves a yellow ring that closes on the place you clicked.
- Units find their own way round the cliffs and the forest. A unit that needs a way round stands still for a moment while it works one out.
- Nobody can stand on cliffs or among trees. If you send units there, they go to the nearest place they can reach. The yellow ring stays where you clicked.
- Units sent to the same place end up standing inside each other, on one point. They do not make room for each other yet.
- When you play alone, the other player's workers never move. Nothing controls them.
- In a shared match, both players see the same workers in the same places. An order takes a moment longer to start than it does alone, because it travels to the host first.
- If the other player leaves or closes the game, a line under the top bar says why, the bar says from then on that they are out, and your match goes on without them. If the host leaves, the match ends for both.
- There is no Capitol and nothing to build.

## What your units are

Select units and the panel at the bottom of the window says what they are.

- **One unit** is shown in detail: its name, its line and epoch, its hit points as a bar and as numbers, and a card for each thing your research changes. A card names the track behind it in the track's color, the tier the unit fights with, and how far that tier is from the unit's own epoch: `Firepower 2 +1` is one tier ahead. Damage is given for a second of fighting, speed in tiles a second. Under the cards are its range or reach, how often it strikes and how far it sees.
- **Accents** are listed under that with what each does. A track one tier ahead of a unit's epoch gives the unit that track's accent at level I, two tiers ahead at level II. A unit carries two at most, from its two tracks furthest ahead, and the panel says so when a third track is ahead.
- **Several units** are summed up, one row for each kind: how many, their hit points together, the Firepower (FP), Protection (PR) and Mobility (MO) tiers they fight with, and their accents. Point at an accent to read what it does.
- **A worker** shows its hit points, what it is doing, its speed and sight. A worker has no tiers and no accents.
- **Another player's unit**, when you click it, shows its hit points, its Firepower, Protection and Mobility tiers and its accents. Its price and supply are the owner's to know.
- Units with accents carry small square badges over their heads, in the color of the track, with one white mark for level I and two for level II. Every player's units show them.
- When a player's epoch rises, a banner under the top bar says which epoch they reached. Your own is larger and says how high your tracks may now go.
- Only workers start a match and nothing in the game makes fighting units yet, so for now the panel has workers to show. The cards, tiers, accents and badges appear once fighting units can be placed or built.
- A unit made before a Protection or Mobility research keeps the hit points and speed it was made with for now, though its card already shows the new tier. Damage, range and accents follow the research at once.
- No match can raise an epoch yet: it takes three tracks at a tier, and the Energy you start with pays for one research.

## Research

Press R, or click Research in the bar at the top. The panel shows your six tracks as rows and their nine tiers as columns. The match goes on behind it.

- A filled cell is a tier you have. The bright cell after it is the next tier, with its price: M is Materials and E is Energy. Click it to start the research.
- Point at a row and the right side says what its next tier costs, how long it takes, which units it unlocks and which accents it gives or takes from the units you can build.
- A cell that cannot be started says why: too expensive, with what you lack in orange; slot busy; or out of reach, because no track may be more than two tiers above your epoch.
- While a research runs, its cell fills up, and the top of the panel shows a progress bar with the time left and a Cancel button. Cancelling gives the whole price back. The bar at the top of the window shows the same progress when the panel is closed: the track and its tier, a bar and the time left.
- When a research finishes, a line under the top bar says so and the track's row has one more filled cell.
- You have one research slot, so one research runs at a time.
- You start with 400 Materials and 100 Energy. Workers at deposits earn more Materials, but nothing earns Energy yet, so a match still has room for one research: a tier 2 costs 100 of each. After it you have no Energy, and every next tier shows as too expensive however many Materials you have.
- Tiers 5 to 9 are closed. A match stops at epoch 4 for now.

## What to know about multiplayer

Every player's machine runs the whole game and they exchange only commands. That has three limits, accepted for games among friends:

- A modified copy of the game can show what should be hidden: the fog of war and an opponent's tracks.
- A player who drops out cannot rejoin the match.
- A slow machine or connection on the host's side slows the match for everyone.

This build also has a development switch on. No match can train a fighting unit yet, so the game accepts an order that makes units from nothing, for either player, to try out fights. Nothing in the game sends that order yet. It is switched off again once units can be trained.

The two games check each other all the time. If they ever disagree about the state of the match, both stop and say so. That is a fault in the game, and its files are what finds it: see the next section.

## The files of a match

Every match writes a replay, a small file with the orders both players gave. The page that ends a match names it. The files are in a folder `matches`:

- Windows: `%APPDATA%\Godot\app_userdata\empire-earth-4\matches`
- macOS: `~/Library/Application Support/Godot/app_userdata/empire-earth-4/matches`

A file is named after the time the match started, the map and the player: `2026-11-11_19-30-05_ridge_p0.replay` is the host's and `..._p1.replay` the other player's. When a match stops because the two games disagree, a second file with the same name and the ending `.snapshot` appears beside each.

## Reporting a problem

Send this to whoever gave you the build:

- The name of the zip file. It carries the version.
- Your operating system and its version, and the model of your machine or graphics card.
- What you did, what you expected and what happened. A screenshot helps.
- For a problem in a match, its replay from the `matches` folder. If the match stopped because the two games disagreed, the `.replay` and the `.snapshot` file from both players: four files.
- The log file `godot.log`. It is in a folder named after the game:
  - Windows: `%APPDATA%\Godot\app_userdata\`
  - macOS: `~/Library/Application Support/Godot/app_userdata/`

  Inside that folder, open `logs`.
