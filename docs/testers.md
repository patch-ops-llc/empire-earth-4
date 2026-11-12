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

During a match, the bar across the top of the window says which player you are, and its button, Leave the match, takes you back to the menu. While units are selected, a panel at the bottom of the window says how many.

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
| Look somewhere else on the map | Click the minimap in the bottom right corner, or hold the left button on it and drag |
| Send the selected units somewhere on the minimap | Right-click the minimap |
| Select one of your units | Click it |
| Select one of your structures | Click it |
| Select several | Hold the left button and drag a box round them |
| Add to the selection | Hold Shift and drag a box. Shift and a click adds a unit, or takes it out if it is already in |
| Clear the selection | Click the ground |
| Send the selected units somewhere | Right-click the ground. On a trackpad that is a click with two fingers, unless you have set it otherwise |
| Train a unit | Click your Capitol, then click a button in the panel at the bottom, or press the key the button names. The first button is Q |
| Cancel an order in the queue | Click it. Backspace cancels the last one |
| Set where new units go | Right-click while the structure that trains them is selected |
| Open or close the research panel | R, or the Research button in the bar at the top. Escape closes it too |
| Start a research | In the research panel, click a bright cell |
| Cancel a research | In the research panel, click Cancel beside its progress bar |
| Show or hide the readout in the corner | F3 |
| Quit | Close the window |

## What you will see

- The host's workers are blue and the other player's are red. Alone, yours are blue. Which side of the map each player starts on is chosen at random each time.
- Selected units have a green ring under them. A right-click leaves a yellow ring that closes on the place you clicked.
- The small map in the bottom right corner shows the whole map: forest in dark green, cliffs in grey, deposits as orange squares, and every unit as a dot in its player's color. The white outline is the part of the map on your screen. Your selected units have a white rim.
- Click the small map and the view jumps there; hold the button and drag, and the view follows. A right-click on it sends the selected units to that place, and a yellow ring closes on the place for a moment. The mouse wheel still zooms while the pointer is on it.
- The small map shows everything, the other player's units too. Nothing is hidden yet.
- Units find their own way round the cliffs and the forest. A unit that needs a way round stands still for a moment while it works one out.
- Nobody can stand on cliffs or among trees. If you send units there, they go to the nearest place they can reach. The yellow ring stays where you clicked.
- Units sent to the same place end up standing inside each other, on one point. They do not make room for each other yet.
- When you play alone, the other player's workers never move. Nothing controls them.
- In a shared match, both players see the same workers in the same places. An order takes a moment longer to start than it does alone, because it travels to the host first.
- If the other player leaves or closes the game, the bar at the top says so and your match goes on without them. If the host leaves, the match ends for both.
- Each player has a Capitol where they start, on the square their workers stand around: a stone hall with a keep and a pointed roof in the player's color. Nobody can stand on it, and units walk round it. Nothing can be built yet.
- The ground a player holds is tinted in the player's color, with a line along its edge. It reaches 16 tiles from the middle of the Capitol. The line follows the sides of the tiles, so it has steps: a tile is either held or it is not. A deposit pays only when all of it is inside, and structures will have to stand inside.
- Click your Capitol to select it. A green line runs round it, and the panel at the bottom of the window names it and its hit points. Nothing can damage a structure yet, so they stay full.
- Structures and units are not selected together. A box takes units only, and a structure leaves the selection as soon as a unit joins it. A right-click with a structure selected sets its rally point: see the next lines.
- With your Capitol selected, the panel at the bottom offers a worker. The button says the price, 50 Materials, shown as "50 M", and the supply it takes. Click it or press Q, and the worker joins the queue under the buttons. You pay when you order. The match starts with 400 Materials and room for 49 more supply.
- The queue holds five orders. The first is the one being made: a green line fills along its lower edge and it counts the seconds down. A worker takes 12 seconds. When it is done, the worker appears beside the Capitol.
- A button that cannot be used is grey and says why: the queue is full, you have too little to pay, or no supply is free. Resting the pointer on it says the same in a sentence.
- Click an order in the queue to cancel it, or press Backspace for the last one. You get the whole price back, also for the one being made.
- Right-click the ground while the Capitol is selected to set its rally point. A flag stands there, with a line to it from the Capitol, while the Capitol is selected. New workers walk to the flag. If the flag stands on a deposit, they go to work there instead.
- If there is nowhere for a new unit to stand, its order says "no room" and waits until there is.

## Research

Press R, or click Research in the bar at the top. The panel shows your six tracks as rows and their nine tiers as columns. The match goes on behind it.

- A filled cell is a tier you have. The bright cell after it is the next tier, with its price: M is Materials and E is Energy. Click it to start the research.
- Point at a row and the right side says what its next tier costs, how long it takes, which units it unlocks and which accents it gives or takes from the units you can build.
- A cell that cannot be started says why: too expensive, with what you lack in orange; slot busy; or out of reach, because no track may be more than two tiers above your epoch.
- While a research runs, its cell fills up, and the top of the panel shows a progress bar with the time left and a Cancel button. Cancelling gives the whole price back. The bar at the top of the window shows the same progress when the panel is closed.
- When a research finishes, a line under the top bar says so and the track's row has one more filled cell.
- You have one research slot, so one research runs at a time.
- You start with 400 Materials and 100 Energy and nothing earns more yet, so a match has room for one research: a tier 2 costs 100 of each. After it, every next tier shows as too expensive.
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
