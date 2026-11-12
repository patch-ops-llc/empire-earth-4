# Running a test build

This is an early build of a hobby real-time strategy prototype, and the first one two people can play together. Each player has six workers on a map with forest and a ridge of cliffs, and can select them and send them round it. There is nothing to build yet, and no match can train a fighting unit, so for now a development aid makes them from nothing: see Trying a fight below. Its job for now is to show that the game starts on your machine, that selecting units, moving them and fighting with them works, and that two machines can share one match.

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
| Select one of your units | Click it |
| Select several | Hold the left button and drag a box round them |
| Add to the selection | Hold Shift and drag a box. Shift and a click adds a unit, or takes it out if it is already in |
| Clear the selection | Click the ground |
| Send the selected units somewhere | Right-click the ground. On a trackpad that is a click with two fingers, unless you have set it otherwise |
| Show or hide the readout in the corner | F3 |
| Attack a unit of the other player | Right-click it |
| Go somewhere and fight whatever is met on the way | Hold Ctrl and right-click the place |
| Stop | X, or the Stop button in the panel at the bottom |
| Development aid: make units where the pointer is | G for yours. Alone, H for the other player's. See Trying a fight |
| Quit | Close the window |

## What you will see

- The host's workers are blue and the other player's are red. Alone, yours are blue. Which side of the map each player starts on is chosen at random each time.
- Selected units have a green ring under them. A right-click leaves a yellow ring that closes on the place you clicked.
- Units find their own way round the cliffs and the forest. A unit that needs a way round stands still for a moment while it works one out.
- Nobody can stand on cliffs or among trees. If you send units there, they go to the nearest place they can reach. The yellow ring stays where you clicked.
- Units sent to the same place end up standing inside each other, on one point. They do not make room for each other yet.
- When you play alone, the other player's workers never move. Nothing controls them.
- In a shared match, both players see the same workers in the same places. An order takes a moment longer to start than it does alone, because it travels to the host first.
- If the other player leaves or closes the game, the bar at the top says so and your match goes on without them. If the host leaves, the match ends for both.
- There is no Capitol and nothing to build.

## What to know about multiplayer

Every player's machine runs the whole game and they exchange only commands. That has three limits, accepted for games among friends:

- A modified copy of the game can show what should be hidden: the fog of war and an opponent's tracks.
- A player who drops out cannot rejoin the match.
- A slow machine or connection on the host's side slows the match for everyone.

This build also has a development switch on. No match can train a fighting unit yet, so the game accepts an order that makes units from nothing, to try out fights. The development aid described under Trying a fight sends it. It is switched off again once units can be trained.

The two games check each other all the time. If they ever disagree about the state of the match, both stop and say so. That is a fault in the game, and its files are what finds it: see the next section.

## Trying a fight

No match can train a fighting unit yet. Until one can, this build has a development aid that makes them from nothing, free of charge. It is not part of the game and goes away once units can be trained. Its panel has an orange border and sits at the top left of a match.

1. In the panel, choose a unit and how many to make at a time. There are four lines of units, each in four epochs. Line infantry shoots. Guards fight hand to hand. Cavalry fights hand to hand and is larger. Artillery shoots far and cannot shoot at what stands close to it. Its shells do not yet hit what stands round the target.
2. Point at open ground and press G. Your units appear there.
3. Alone, point somewhere else and press H. Units of the other player appear there, and nothing but their own reflexes controls them. In a shared match H makes nothing: each player makes their own units with G.

What you will see:

- All units still share one stand-in shape, so each fighting unit carries a pale mark on top that says how it fights: a cross for hand to hand, a ball for shooting, a ring for artillery. Cavalry's cross is larger. Workers carry no mark.
- Units fight on their own when an enemy comes near, and go back to where they stood afterwards.
- A right-click on an enemy leaves a red ring, and the selected units go after that unit. A right-click with Ctrl held leaves an orange ring, and the units go there and fight what they meet. A plain right-click on the ground moves them, and on that walk they do not fight, even when attacked.
- A unit that has lost hit points has a bar above it: green, yellow below half and red below a quarter. The pale part of a bar is what the unit has just lost. A selected unit shows its bar even when unhurt, and the panel at the bottom gives the number when one unit is selected.
- A shot is a pale streak in its owner's color, and a shell of artillery is thicker. A unit that is hit flashes white. A shot whose target died or got away before it arrived ends in a grey puff.
- A unit that dies sinks into the ground and leaves a ring in its owner's color that spreads and fades.
- Workers can be attacked and do not fight back.
- If an order cannot be carried out, a line of text on screen says why.

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
