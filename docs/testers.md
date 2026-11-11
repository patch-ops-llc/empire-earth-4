# Running a test build

This is an early build of a hobby real-time strategy prototype, and the first one you can do something in. You have six workers on a map with forest and a ridge of cliffs, and you can select them and send them round it. There is nothing to build and nobody to fight yet. Its job for now is to show that the game starts on your machine, and that selecting units and moving them works.

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
| Quit | Close the window |

## What you will see

- Your workers are blue and the other player's are red. Which side of the map you start on is chosen at random each time.
- Selected units have a green ring under them. A right-click leaves a yellow ring that closes on the place they are going to.
- Units find their own way round the cliffs and the forest. A unit that needs a way round stands still for a moment while it works one out.
- Nobody can stand on cliffs or among trees. If you send units there, they go to the nearest place they can reach, and the yellow ring jumps to it.
- Units sent to the same place end up standing inside each other, on one point. They do not make room for each other yet.
- The other player's workers never move. Nothing controls them yet.
- There is no Capitol and nothing to build.

## What to know before multiplayer arrives

Multiplayer is not in this build. When it arrives, every player's machine will run the whole game and they will exchange only commands. That has three limits, accepted for games among friends:

- A modified copy of the game can show what should be hidden: the fog of war and an opponent's tracks.
- A player who drops out cannot rejoin the match.
- A slow machine or connection on the host's side slows the match for everyone.

## Reporting a problem

Send this to whoever gave you the build:

- The name of the zip file. It carries the version.
- Your operating system and its version, and the model of your machine or graphics card.
- What you did, what you expected and what happened. A screenshot helps.
- The log file `godot.log`. It is in a folder named after the game:
  - Windows: `%APPDATA%\Godot\app_userdata\`
  - macOS: `~/Library/Application Support/Godot/app_userdata/`

  Inside that folder, open `logs`.
