# Running a test build

This is an early build of a hobby real-time strategy prototype, and the first one two people can play together. Each player has six workers on a map with forest and a ridge of cliffs, and can select them and send them round it. Workers can build structures, a Generator among them for Energy: see Building below. No match can train a fighting unit yet, so for now a development aid makes them from nothing: see Trying a fight below. Its job for now is to show that the game starts on your machine, that selecting units, moving them and fighting with them works, and that two machines can share one match.

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
| Look somewhere else on the map | Click the minimap in the bottom right corner, or hold the left button on it and drag |
| Send the selected units somewhere on the minimap | Right-click the minimap |
| Select one of your units | Click it |
| Select one of your structures | Click it |
| Select several | Hold the left button and drag a box round them |
| Add to the selection | Hold Shift and drag a box. Shift and a click adds a unit, or takes it out if it is already in |
| Clear the selection | Click the ground |
| See what another player's unit is | Click it. A pale ring marks it and the panel at the bottom describes it. It cannot be given orders |
| Send the selected workers to work at a deposit | Right-click the deposit |
| Send the selected workers to work at a Generator of yours | Right-click the Generator, once it is finished |
| Open or close the build menu | B, or the Build button in the panel at the bottom, with a worker selected |
| Choose a structure in the build menu | Click it, or press the key beside its name: C Capitol, O Outpost, E Generator, B Barracks, K Works, Y Academy, T Tower |
| Place the chosen structure | Click the ground. Hold Shift to go on placing the same structure |
| Go back from placing to the menu, and from the menu to the match | Right-click or Escape |
| Cancel a build order or a structure that is not finished | Select the worker or the structure and press Delete, or the Cancel build button in the panel at the bottom |
| Send the selected units somewhere | Right-click the ground. On a trackpad that is a click with two fingers, unless you have set it otherwise |
| Open or close the research panel | R, or the Research button in the bar at the top. Escape closes it too |
| Start a research | In the research panel, click a bright cell |
| Cancel a research | In the research panel, click Cancel beside its progress bar |
| Show or hide the readout in the corner | F3 |
| Attack a unit of the other player | Right-click it |
| Go somewhere and fight whatever is met on the way | Hold Ctrl and right-click the place |
| Stop | X, or the Stop button in the panel at the bottom |
| Development aid: make units where the pointer is | G for yours. Alone, H for the other player's. See Trying a fight |
| Development aid: lift the fog of war from your screen, and lay it back | F, alone, or the button in the orange panel "Development aid: fog of war". See The fog of war |
| Quit | Close the window |

## Resources and workers

The brown blocks on the map are deposits. Select workers and right-click a deposit, and they walk to it, stand beside it and earn Materials for you. A yellow ring closes round the deposit you clicked.

- Each worker at a deposit earns 45 Materials a minute, paid a little every second from one second after it arrives. Your six workers at one deposit earn 4.5 a second.
- A deposit has room for eight workers, so all six of yours fit at one.
- A deposit pays only inside your territory, which for now is the ground round your start: the two deposits nearest your workers. Workers sent to any other deposit stand there and earn nothing, and a line under the top bar says so. Nothing can claim more territory yet.
- A worker stops earning when you send it somewhere else. Send it back to a deposit and it starts again.
- Nobody can walk through a deposit.
- Materials and Energy are shown as whole numbers. Supply is what you use out of what you may use: each worker uses 1.
- Energy comes from workers at a Generator, which you build: see Building below. Research spends both: see Research below.

## What you will see

- The host's workers are blue and the other player's are red. Alone, yours are blue. Which side of the map each player starts on is chosen at random each time.
- Selected units have a green ring under them. A right-click leaves a yellow ring that closes on the place you clicked.
- The small map in the bottom right corner shows what you know of the whole map: forest in dark green, cliffs in grey, deposits as orange squares, every structure you know of as a larger square in its player's color, and every unit you can see as a dot in that color. The white outline is the part of the map on your screen. Your selected units have a white rim.
- Click the small map and the view jumps there; hold the button and drag, and the view follows. A right-click on it sends the selected units to that place, and a yellow ring closes on the place for a moment. The mouse wheel still zooms while the pointer is on it.
- You see the map through your own units, in the world and on the small map alike: see The fog of war below.
- Units find their own way round the cliffs and the forest. A unit that needs a way round stands still for a moment while it works one out.
- Nobody can stand on cliffs or among trees. If you send units there, they go to the nearest place they can reach. The yellow ring stays where you clicked.
- Units sent to the same place end up standing inside each other, on one point. They do not make room for each other yet.
- When you play alone, the other player's workers never move. Nothing controls them.
- In a shared match, both players see the same workers in the same places. An order takes a moment longer to start than it does alone, because it travels to the host first.
- If the other player leaves or closes the game, a line under the top bar says why, the bar says from then on that they are out, and your match goes on without them. If the host leaves, the match ends for both.
- Each player has a Capitol where they start, on the square their workers stand around: a stone hall with a keep and a pointed roof in the player's color. Nobody can stand on it, and units walk round it.
- The ground a player holds is tinted in the player's color, with a line along its edge. It reaches 16 tiles from the middle of the Capitol. The line follows the sides of the tiles, so it has steps: a tile is either held or it is not. A deposit pays only when all of it is inside, and every structure but a Capitol and an Outpost has to stand inside.
- Click your Capitol to select it. A green line runs round it, and the panel at the bottom of the window names it and its hit points. Nothing can damage a structure yet, so they stay full.
- Structures and units are not selected together. A box takes units only, and a structure leaves the selection as soon as a unit joins it. A selected structure takes no orders yet, so a right-click does nothing while one is selected.

## The fog of war

You are shown only what your own units see. Each unit sees a circle round itself: six tiles for a worker, eight for most fighting units, ten for cavalry. Nothing blocks sight, not forest and not cliffs.

- **Ground you have never seen is dark and empty.** Its forest and cliffs are not drawn, and the small map is black there. At the start that is nearly the whole map.
- **Ground you have seen and do not see now is dimmed.** You see its forest and cliffs as they are, and none of the other player's units on it.
- **A deposit is shown once you have seen it.** Until then it is not on your screen and not on the small map, and a click where it lies finds only the ground. A deposit you have seen and do not see now is dimmed.
- **Ground you see now is bright.** The other player's units are drawn only there. So are their shots and everything that happens to them: bars, flashes, deaths. A unit that walks out of what you see is gone from your screen at once.
- **Your own units are always shown.** When one of them is shot at from ground you do not see, you see the shots arrive over the ground you do see, and your unit being hit, and not who fired.
- **Another player's structures are shown once you have seen them, and then as you last saw them.** A structure is seen when any tile under it is. Until then it is not on your screen and not on the small map, and a click where it stands finds only the ground. Once it is out of your sight again it stays where you saw it, greyed and dimmed, in the world and on the small map: that is what you remember, not what is there. It may have changed since, or be gone, and you learn that when you see its tiles again. A structure that was still building itself when you last saw it stays as far built as it was then, with its bar, though it may long be finished.
- **Another player's territory is shown with its Capitol.** Its tint and its line appear when you have seen the Capitol, and stay for as long as you remember it. Your own territory is always shown, also where its edge runs over ground you have not seen yet.
- **You cannot give an attack order on a unit you do not see**, since it is not on your screen to click. When a unit you ordered attacked walks out of sight, your units walk to where it was last seen and stop there.
- The edge of what you see moves in steps of a tile a few times a second, and the screen smooths that.
- In a shared match each player has their own picture. What you see is not what the other player sees.

**The development switch.** A tester alone has to see the other player's units, so this build has a second development aid, in an orange panel under the first: "Development aid: fog of war". Its button, or the F key, lifts the fog from your screen: the whole map and every unit, structure and territory on it are shown, as they are now. Press it again to lay the fog back. It is not part of the game, and it changes only what is drawn. Your units still see only what they see: they do not shoot at a unit outside their sight, and a right-click on such a unit sends them walking there, with a line of text that says so. The build menu also goes by what you know and not by what the switch shows. In a shared match the switch is not offered, and the panel says so. Like the aid that makes units, it goes away once units can be trained.

## Building

Select one or more workers and press B, or click Build in the panel at the bottom. The build menu opens above that panel and lists the seven structures a worker places.

- Each entry has the structure's name, the key that chooses it and its price: M is Materials and E is Energy. Point at an entry and the line under the entries says what it costs, how many tiles it takes, how long it builds itself and whether it must stand in your territory.
- An entry is dark when you cannot order the structure now. Its price is orange when you lack the resources, and the line under the entries says how much you lack. An entry says `Limit 2 of 2` when you have as many as you may have: you may have two Academies. A structure that waits for a research says which.
- Choose a structure and its shape follows the pointer. Green is a place it can go. Red is a place it cannot: the red tiles are the ones in the way, because they are outside your territory, not open ground, or under a deposit or a structure. The top line of the menu says which.
- The shape goes by what you know, as the order does. A structure of another player's that you have never seen is not marked red, and one you remember is, also when it is gone by now. If something you did not know of is in the way, the worker finds out when it arrives, and the whole price comes back.
- A Capitol or an Outpost can only go on ground you have seen: on ground that is still dark the shape is red, and the top line says that you have not seen all of the ground there. The other structures stand in your territory, and can also go on a part of it you have not seen yet.
- Amber is a warning, for a Capitol or an Outpost only: its territory would overlap that of an enemy's Capitol or Outpost you see now. You can still place it there, but the worker will be turned away when it arrives unless the enemy's Capitol or Outpost is gone by then.
- Click to place it. You pay the whole price at once. The nearest selected worker walks to the site, which is drawn as a pale shape of the structure with a line round its tiles. Only you see your sites.
- When the worker arrives it lays the foundation and goes back to what it was doing. The structure then builds itself: nobody has to stay. It fills from the ground up, and a blue bar over it shows how far it is. Select it and the panel at the bottom says how long it has left.
- A foundation is in the way like a finished structure and has all its hit points from the start, but it does nothing until it is finished.
- Hold Shift while you click to place the same structure again. Each order takes another of the selected workers, so select as many workers as you want sites.
- Cancel with Delete, or the Cancel build button. For a worker on its way, you get the whole price back. For a structure that is not finished, you get three quarters back and it is taken away.
- If the worker cannot lay the foundation when it arrives, because something stands in the way or the rules no longer allow it, a line under the top bar says why and the whole price comes back.
- What the structures do so far: an Outpost claims the ground 12 tiles round it once it is finished, so that you can build and work deposits there. A Generator is where workers earn Energy: select workers and right-click a finished Generator. Each earns 30 Energy a minute, and a Generator has room for four. An Academy gives you a second research at a time. A second Capitol claims ground like the first. The Barracks, the Works and the Tower can be built but do nothing yet: no match can train units yet.
- A worker that was working at a deposit or a Generator goes back to it once the foundation is laid. A worker that had no work stays beside the site.

## What your units are

Select units and the panel at the bottom of the window says what they are.

- **One unit** is shown in detail: its name, its line and epoch, at the right its hit points as a bar and as numbers, and a card for each thing your research changes. A card names the track behind it in the track's color, the tier the unit fights with, and how far that tier is from the unit's own epoch: `Firepower 2 +1` is one tier ahead. Damage is given for a second of fighting, speed in tiles a second. Under the cards are its range or reach, how often it strikes and how far it sees.
- **Accents** are listed under that with what each does. A track one tier ahead of a unit's epoch gives the unit that track's accent at level I, two tiers ahead at level II. A unit carries two at most, from its two tracks furthest ahead, and the panel says so when a third track is ahead.
- **Several units** are summed up, one row for each kind: how many, their hit points together, the Firepower (FP), Protection (PR) and Mobility (MO) tiers they fight with, and their accents. Point at an accent to read what it does.
- **A worker** shows its hit points, what it is doing, its speed and sight. A worker has no tiers and no accents.
- **Another player's unit**, when you click it, shows its hit points, its Firepower, Protection and Mobility tiers and its accents. Its price and supply are the owner's to know. The panel lets the unit go when it walks out of your sight.
- Units with accents carry small square badges over their heads, in the color of the track, with one white mark for level I and two for level II. A unit shows two at most, the stronger on the left. They sit just over the place of a unit's hit point bar.
- The colors are those of the research grid: red for Firepower, blue for Protection, green for Mobility, orange for Manufacturing, purple for Logistics.
- Zoomed far out there is no room for the marks, and the badges become small boxes that you count: one box for level I, two boxes stacked for level II. So a red stack of two beside one blue box is Firepower II and Protection I.
- Your own units always show their badges. Another player's units show theirs while you see the unit, and that is all the badges tell you about the other player's research: scout to read them.
- When a research finishes, the badges of the units you already have change at once. On an older unit a new accent can push out one it carried, and its badge goes with it.
- When a player's epoch rises, a banner under the top bar says which epoch they reached. Your own is larger and says how high your tracks may now go.
- Only workers start a match. Fighting units, which have the cards, tiers, accents and badges, come from the development aid for now: see Trying a fight.
- A unit made before a Protection or Mobility research keeps the hit points and speed it was made with for now, though its card already shows the new tier. Damage, range and accents follow the research at once.
- An epoch rises when three tracks have reached its tier. The Energy you start with pays for one research, so the rest has to come from a Generator.

## Research

Press R, or click Research in the bar at the top. The panel shows your six tracks as rows and their nine tiers as columns. The match goes on behind it.

- A filled cell is a tier you have. The bright cell after it is the next tier, with its price: M is Materials and E is Energy. Click it to start the research.
- Point at a row and the right side says what its next tier costs, how long it takes, which units it unlocks and which accents it gives or takes from the units you can build.
- A cell that cannot be started says why: too expensive, with what you lack in orange; slot busy; or out of reach, because no track may be more than two tiers above your epoch.
- While a research runs, its cell fills up, and the top of the panel shows a progress bar with the time left and a Cancel button. Cancelling gives the whole price back. The bar at the top of the window shows the same progress when the panel is closed: the track and its tier, a bar and the time left.
- When a research finishes, a line under the top bar says so and the track's row has one more filled cell.
- You have one research slot, so one research runs at a time.
- You start with 400 Materials and 100 Energy. Workers at deposits earn more Materials, and workers at a Generator earn Energy: build one early if you mean to research more than once, since a tier 2 costs 100 of each.
- Tiers 5 to 9 are closed. A match stops at epoch 4 for now.

## What to know about multiplayer

Every player's machine runs the whole game and they exchange only commands. That has three limits, accepted for games among friends:

- A modified copy of the game can show what should be hidden: the fog of war and an opponent's tracks.
- A player who drops out cannot rejoin the match.
- A slow machine or connection on the host's side slows the match for everyone.

This build also has a development switch on. No match can train a fighting unit yet, so the game accepts an order that makes units from nothing, to try out fights. The development aid described under Trying a fight sends it. It is switched off again once units can be trained.

The two games check each other all the time. If they ever disagree about the state of the match, both stop and say so. That is a fault in the game, and its files are what finds it: see the next section.

## Trying a fight

No match can train a fighting unit yet. Until one can, this build has a development aid that makes them from nothing, free of charge. It is not part of the game and goes away once units can be trained. Its panel has an orange border and sits at the left edge of the window.

1. In the panel, choose a unit and how many to make at a time. There are four lines of units, each in four epochs. Line infantry shoots. Guards fight hand to hand. Cavalry fights hand to hand and is larger. Artillery shoots far and cannot shoot at what stands close to it. Its shells do not yet hit what stands round the target.
2. Point at open ground and press G. Your units appear there.
3. Alone, point somewhere else and press H. Units of the other player appear there, and nothing but their own reflexes controls them. In a shared match H makes nothing: each player makes their own units with G.
4. Units of the other player made on ground you do not see are there and are not shown: see The fog of war. Make them close to your own units, or press F to lift the fog from your screen first.

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
