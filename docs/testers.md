# Running a test build

This is an early build of a hobby real-time strategy prototype. There is nothing to play yet: the build opens a window with a ground grid and a camera you can move. Its job for now is to show that the game starts on your machine.

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
| Zoom | Mouse wheel, or the = and - keys |
| Show or hide the readout in the corner | F3 |
| Quit | Close the window |

Pinch and two-finger scroll on a trackpad should zoom and move the camera as well. Nobody has tried that yet, so say whether it works.

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
