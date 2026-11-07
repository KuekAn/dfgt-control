# DFGT Control

A small replacement for the wheel part of Logitech Gaming Software, made for the
Logitech Driving Force GT. Nothing needs to be installed: run `DFGTControl.exe`.
Only one copy runs at a time; opening it again brings the running one to the front.

This is an unofficial tool and is not made or supported by Logitech.

## What it does

- **Live settings.** The operating range (40–900°) and the centering spring go to the
  wheel the moment you move a slider, even while a game is running.
- **Settings apply on their own.** They're applied when the app starts, whenever the
  wheel is plugged in, and (optionally) re-sent every few seconds in case a game or
  the Logitech driver resets them. You don't have to launch games from this app.
  Turn on **Options → Start with Windows** so it runs quietly in the tray.
- **Pedals & deadzones tab.**
  - **Deadzones** for steering (around the centre and at each end) and for each pedal
    (at the start and at the end). The meters at the top show them as grey zones, and
    their percentage is the result after the deadzone.
  - **Invert** each pedal on its own, in case it reads 100% when released.
  - Deadzones always apply to the meters and the virtual PS4
    controller. Tick **Also apply them in games** to save them in the wheel's
    DirectInput calibration in Windows. Most racing sims read the wheel this way, and
    it keeps working without this app running. Restart a game to pick up changes.
    Games get the pedal start/end and steering end deadzones; for a steering centre
    deadzone, use the game's own setting. Unticking puts the calibration back to
    normal.
- **Dark mode.** **Options → Appearance**: Match Windows (default), Light or Dark.
  Everything follows it, including the title bar, the key dialog and the tray menu.
- **Buttons tab.** One table with a row per button, showing its name, its keyboard
  keys and its PS4 controller button. The row lights up while you press the button.
  - **Keyboard keys** (double-click a row): one key (`F5`), a combination
    (`Ctrl+Shift+F5`) or a sequence (`Ctrl+C, Ctrl+V`). The modes are **Hold** (keys
    stay down while the button is held), **Tap** (pressed once) and **Toggle** (press
    to hold, press again to release).
  - **Button names:** choose *Symbols* (Cross, Square…) or *Original* (Button 1,
    Button 2…). Tick **Show the other name in grey** to see both side by side.

## PlayStation button symbols in games (virtual PS4 controller)

Tick **PS4 controller** on the Buttons tab, and the wheel's buttons also appear in
Windows as a virtual DualShock 4. Games that support PlayStation controllers then
show ✕ □ ○ △, L1, R2, Options… instead of "Button 5".

- This needs the free **ViGEmBus** driver, installed once:
  <https://github.com/nefarius/ViGEmBus/releases/tag/v1.22.0>.
- In the game's control settings, assign actions by pressing wheel buttons. Keep
  steering and pedals on the wheel.
- In the table, pick which PS4 button each wheel button sends. **Wheel only** sends
  nothing, so the game sees just "Button N". **Reset PS4 buttons to default** puts
  back the standard mapping.

## Good to know

- Games always see the wheel's own button too. Assign each action in a game to only
  one of: the wheel button, its keys, or its PS4 controller button.
- If a game runs as administrator, Windows blocks keys sent from a normal program. In
  that case, run DFGT Control as administrator too.
- Force-feedback strength and effects are still set inside each game. This app only
  controls the range and the centering spring.
- If Logitech Gaming Software is running, it may override the range when its own
  profile switches. Keep "Re-send these settings" on, or close LGS. The Logitech
  *driver* should stay installed.
- **Options → Save button names to Windows** saves the names as the Buttons tab
  shows them: symbols, or Button 1, Button 2… if you picked Original. Many games
  ignore these names, though.

## Files

- `src/` — source code (C#).
- `build.cmd` — builds `DFGTControl.exe` using the C# compiler built into Windows.
- `assets/app.ico` — optional app icon, not included in this repository. If it's
  there, `build.cmd` uses it for the exe, the window and the tray. If not, the app
  draws a simple wheel icon.
- Settings are stored in `%APPDATA%\DFGTControl\settings.ini`.
