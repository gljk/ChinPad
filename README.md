# ChinPad

ChinPad is a small user-mode Windows 11 tool for driving the laptop touchpad with the chin. Precision
Touchpad firmware flags large contacts as palms by clearing the HID Confidence bit, and Windows then
ignores them. ChinPad reads the touchpad's raw HID reports through Raw Input (no driver, no admin, no
network) and moves the cursor itself for any contact with Tip Switch on, whatever its Confidence bit
says.

## Download and install

Download the Windows x64 assets from the project's GitHub Releases page (the project page is shown
in **About ChinPad...** in the tray menu). Windows 11 is required; the .NET runtime is included.

- **Portable zip** (`ChinPad-<version>-win-x64.zip`): unzip anywhere and run `ChinPad.exe`.
  To keep settings portable, create `settings.json` containing `{}` next to the exe **before** its
  first start. Without that file, settings and the log live in `%LOCALAPPDATA%\ChinPad`, even when
  the exe came from the zip. Copy an existing settings.json there instead if you want to keep it.
- **Installer** (`ChinPad-<version>-win-x64-setup.exe`): installs just for you in
  `%LOCALAPPDATA%\Programs\ChinPad`, adds a Start-menu shortcut, and offers **Start ChinPad when I
  sign in** and launching the app at the end. It needs no administrator rights and keeps settings
  and the log in `%LOCALAPPDATA%\ChinPad`.

The first start without a settings file opens the first-time setup wizard to measure your chin,
place the buttons and try movement and clicks. You can repeat it from **Settings > First-time setup**.

An unsigned download shows SmartScreen's **Windows protected your PC** warning.
For a copy you chose to download, select **More info**, then **Run anyway**.

To uninstall a zip copy, turn off **Start at login**, choose **Exit** in the tray menu, and delete
the unzipped folder. For an installer copy, exit ChinPad and use **Windows Settings > Apps > Installed
apps > ChinPad > Uninstall**; the uninstaller removes its startup entry and shortcuts and tells you
that `%LOCALAPPDATA%\ChinPad` is kept. Either way, you may keep that data folder for a future install
or delete it yourself to remove saved settings and logs. A portable copy keeps its data in its folder.
If you explicitly created the optional administrator scheduled task, remove it separately using the
command under "Elevated windows: your options" below.

There is no automatic update. Download the new zip, exit ChinPad and replace the exe and accompanying
documents in the same folder, keeping your `settings.json`; for an installed copy, exit and run the
new installer over it. AppData settings are kept too. If you move a zip copy to a different folder,
turn **Start at login** off and on from that copy so it points to the new exe.

## For users

ChinPad lets you move the pointer and click with your chin on a laptop touchpad. It runs quietly in
the background. It needs no administrator rights and never uses the network.

### Start it

1. Put `ChinPad.exe` (one file) in a folder of your choice, for example `Documents\ChinPad`.
2. Double-click it. If Windows warns about an unknown publisher, choose **More info**, then **Run anyway**.
3. A small dot appears in the notification area, bottom right next to the clock. If you do not see it,
   click the `^` arrow there; you can drag the dot onto the taskbar so it stays visible.

**Keep a mouse or the keyboard in reach** while you try ChinPad and its settings. **Ctrl+Alt+P** pauses
and resumes ChinPad at any time (you can choose another key).

### The dot

| Dot | Meaning |
|---|---|
| green | ChinPad is on: your chin moves the pointer |
| grey | paused: the touchpad works as usual, ChinPad does nothing |
| red | something does not work; the settings window and the log say what |

- **Double-click** the dot: the settings window opens.
- **Right-click** the dot: Settings..., Save diagnostic bundle..., Pause, Reload settings, Open settings
  file, Open log, Open README, Start at login, About ChinPad..., Exit.
- Hovering over the dot shows whether ChinPad is on, the last click it made, and "left button locked"
  while Click lock keeps a button pressed.

### Moving and clicking

- Touch the pad with your chin and move it: the pointer follows.
- To click, put your chin in the strip along the edge of the pad nearest you and press the pad down:
  the left half of the strip is a left click, the right half a right click. While the chin is in the
  strip the pointer does not move; slide out of it and the pointer moves again. Pressing the pad with
  the chin anywhere else does not click, so leaning on it by accident does nothing.
- **A second press soon after a click is ignored** (the same button, within half a second), so a chin
  that bounces never opens something by an accidental double-click. To open a file or folder, click it
  once and press Enter, or switch this off in the settings window.
- **Click lock** (off at first; switch it on in the settings window) lets you drag things and select
  text without holding the pad down: keep the pad pressed in the strip until you hear two short beeps,
  then let go. The button stays pressed while you move the pointer with your chin; your next press in
  the strip lets it go (that press does not click). Pausing ChinPad lets it go too.

### The settings window

Double-click the dot (or choose **Settings...** in its menu). On the left you see your touchpad, live:
the click strip in colour and every touch as a dot (green: moving the pointer; light green: in the move
area; yellow: in the click strip; grey: just landed, ChinPad is still deciding; blue: a finger that
Windows handles). A ring shows where you pressed. Below it: what the touch is doing, how many clicks
ChinPad has made, how many of Windows' own clicks it held back, and whether Click lock keeps a button
pressed. On the right are the settings; every change applies at once:

| Setting | What it does |
|---|---|
| Speed | how far the pointer moves for each millimetre the chin moves |
| Smoothing | higher = steadier, but slower to follow |
| Start moving after | the pointer stays still until the chin has moved this far, so a resting chin does not make it jiggle |
| Click strip height | how tall the click strip is (for the usual left/right strip; other layouts are changed in settings.json) |
| In the click strip | "Strip is dead, only clicks" (the default): a chin in the strip never moves the pointer. "Where the chin lands decides": a touch that lands in the strip only clicks, a touch that lands above it moves the pointer, even into the strip |
| Landing time | how long a new touch waits before ChinPad decides "click strip" or "move" |
| Merge touches closer than | some touchpads see a chin as two touches; touches closer than this count as one. 0 = off |
| Click beep | a short sound for each click |
| Click lock | a press held this long (300 to 3000 ms, 600 at first) keeps the button pressed until your next press in the strip: for dragging and selecting text. Off at first |
| Ignore a second click | a second press of the same button this soon after a click (100 to 1500 ms, 500 at first) is ignored completely, so an accidental second press never becomes a double-click. On at first |
| Apply to | which touchpad ChinPad works on: All touchpads (the usual choice) or one of them. Touch a touchpad and its entry shows "reporting now". **Refresh** looks again for touchpads that were plugged in or removed; ChinPad also notices that by itself. If the one you chose is unplugged, ChinPad uses all touchpads until it is back |
| Let Windows handle fingers | checked: fingers work as usual (two-finger scrolling, tapping) and the strip is not dead to fingers. Unchecked (the default): ChinPad handles every touch, fingers too |
| Start at login | start ChinPad when you sign in to Windows |
| Start paused | ChinPad starts paused and waits until you resume it |
| Language / Jezik | choose Automatic (Windows), English or Srpski (latinica). Serbian (Latin script) is included. The window and tray change immediately; no restart is needed |
| Pause key | click the box, then press the keys you want (for example Ctrl+Alt+P); Backspace = no key |

Next to every setting is a round **?**. Point at it to read what the setting does and which way to move
it for a common problem; click it (or Tab to it and press Space) and the explanation stays open until you
click it, press Esc or click another **?**. The **?** above the drawing explains its colours.

Buttons: **Pause / Resume**, **Save diagnostic bundle...**, **Open log**, **Open settings file**,
**Restore defaults...** (asks first and keeps a copy of your settings as `settings.before-defaults.json`),
**First-time setup** (measure your chin and set up the pad again) and **Close**. Closing the window does not stop
ChinPad; Exit in the dot's menu does. Everything works from the keyboard: Tab moves between the
controls (each **?** comes right after its setting), the arrow keys move a slider or choose in the list,
Space opens a **?**, Alt + the underlined letter jumps to most controls, Esc closes an open explanation
and then the window.

### When something does not work: the diagnostic bundle

Right-click the dot and choose **Save diagnostic bundle...** (or use the button in the settings window).
A small window counts down 15 seconds: touch the pad with your chin and with a finger, and press in the
click strip. Then choose where to save the file (the Desktop, unless ChinPad runs in portable mode) and
send that one zip file to the person who helps you. It contains:

- `caps.txt`: what your touchpad says it can report,
- `settings.json`: your settings,
- `chinpad.log`: the last 2000 lines of ChinPad's log,
- `recording.csv`: the 15 seconds of touchpad data,
- `system.txt`: ChinPad and Windows version, your displays and their scaling, Windows' mouse and
  touchpad settings, and ChinPad's counters.

ChinPad never sends anything anywhere: the file stays on your computer until you pass it on. The log and
`system.txt` contain folder names, which include your Windows user name.

### Where the settings live

- Normally in `%LOCALAPPDATA%\ChinPad` (paste that into the address bar of File Explorer): `settings.json`
  and `chinpad.log`. ChinPad creates the folder at the first start.
- **Portable mode**: when a `settings.json` is next to `ChinPad.exe`, ChinPad keeps its settings and log
  in that folder instead (as all versions before 0.4.0 did). To make a copy portable, put a file
  `settings.json` containing `{}` next to the exe; ChinPad fills in every setting at its next start.
- The settings window shows the file in use at the bottom left; the first lines of the log say which mode.

See **Download and install** above for uninstalling or updating either kind of copy.

## For developers

The rest of this file describes how ChinPad works, its settings file in detail, and how it was built and
tested, phase by phase.

## Status

- Phase 1: the diagnostic (`--diag`) and the capabilities dump (`--caps`). They show and log what the
  firmware reports for a finger and for a chin.
- Phase 2: the background mode (`ChinPad.exe` without arguments): tray icon, pause/resume hotkey, the
  chin moves the cursor.
- Phase 3: button zones (the chin clicks by pressing the clickpad while it rests in a zone), Windows'
  own clicks kept out of the way, click beep, Start at login, and a write-up of the options for
  administrator windows (the optional wizard action sets up a scheduled task only when you choose it).
- 0.3.1: a touch is classified once it has settled (`zoneSettleMs`), `zoneMode` (landing or
  position), a taller default strip, and notes on strategy 1 for this laptop.
- 0.4.0 (Phase 4a): the settings window, the diagnostic bundle, merging of a chin reported as two
  contacts (`mergeMm`), and settings in `%LOCALAPPDATA%\ChinPad` unless a settings.json is next to the
  exe (portable mode). See "Phase 4a" below.
- 0.5.0: new defaults (strategy 3, zoneMode position), the touchpad choice ("Apply to") in the settings
  window with hot-plug, ClickLock, blocking of accidental double clicks, and a "?" with a plain-language
  explanation next to every setting. See "0.5.0" below.

- 0.6.0: English and Serbian (Latin) interface, with an immediate language switch in General. See "0.6.0: languages" below.

- 0.7.0: first-time setup wizard, measurements and optional administrator setup.
- 0.8.0: public release packaging, About, version and startup commands. See [RELEASING.md](RELEASING.md).

## Build and publish

Needs the .NET 10 SDK. With the user-local SDK in `%USERPROFILE%\.dotnet`:

```powershell
cd C:\path\to\ChinPad
& "$env:USERPROFILE\.dotnet\dotnet.exe" publish -c Release -o publish --no-restore
```

Run a restore once when preparing a new development machine; release builds use cached packages
with `--no-restore`. The result is a single
self-contained `publish\ChinPad.exe` (no .NET install needed to run it, no elevation).

## Run

```powershell
.\publish\ChinPad.exe                       # background mode: tray icon, the chin moves the cursor
.\publish\ChinPad.exe --seconds 60          # same, exits by itself after 60 s
.\publish\ChinPad.exe --version             # print ChinPad and its version, then exit
.\publish\ChinPad.exe --enable-start-at-login # register this exe at sign-in, then exit
.\publish\ChinPad.exe --disable-start-at-login # remove the sign-in entry, then exit
.\publish\ChinPad.exe --caps                # print the touchpad capabilities dump and exit
.\publish\ChinPad.exe --diag                # live diagnostic in its own console window
.\publish\ChinPad.exe --diag --seconds 30   # same, stops by itself after 30 s
```

`--diag` prints the caps dump (also saved as `diag-caps.txt`), then shows a live table per contact ID
and logs every report to `chinpad-YYYYMMDD-HHMMSS.csv`. Both files go where settings.json is: next to
the exe in portable mode, else `%LOCALAPPDATA%\ChinPad` (see "Where the settings live"). Started from a
terminal it opens its own console window, because the shell does not wait for it. With output
redirected (`| Out-String`, `> file`) it prints one status line per second instead of the table.

Keys in `--diag`:

| Key | Action |
|---|---|
| F | write a `finger` marker row into the CSV |
| C | write a `chin` marker row into the CSV |
| D | rate mode on/off: reports/s and frames/s per touchpad, to check whether raw reports still arrive with the touchpad switched Off in Settings |
| Q or Esc | flush the CSV and quit (Ctrl+C does the same) |

In the live table, yellow rows are contacts with tip on and confidence off (what Windows ignores), and
grey rows are contacts that ended (shown for 1 s with how long they lasted). The `zone` column shows
how ChinPad classifies each touch (`undecided` while it settles, see Phase 3), so the zone layout can be
tried with the chin before using it.

## CSV columns

`t_ms,wallclock,device,report_id,frame,contact_count,scan_time,contact_id,tip,confidence,x,y,w,h,button,age_ms,note,marker,pressure,zone`

- `t_ms`: milliseconds since ChinPad started (monotonic Stopwatch clock).
- `wallclock`: local time of day, HH:mm:ss.fff.
- `device`: touchpad index as listed in `diag-caps.txt` (#0, #1, ...).
- `report_id`: HID report ID of the report.
- `frame`: frame number. Reports that make up one frame (hybrid mode, more contacts than finger collections) share it.
- `contact_count`: Contact Count field of this report (0 in hybrid continuation reports, empty if absent).
- `scan_time`: Scan Time field of this report, in the device's units (usually 100 us).
- `contact_id`, `tip`, `confidence`, `x`, `y`, `w`, `h`: one contact as reported (x/y logical units; w/h empty if the pad has no Width/Height).
- `button`: physical button bits (bit 0 = button 1).
- `age_ms`: ms since this contact ID first appeared; on a `vanished` row, how long it lasted.
- `note`:
  - `confidence-flip`: this contact's confidence changed since its previous report.
  - `vanished`: the contact was down and is missing from a complete frame without a tip-off report.
  - `beyond-count`: a finger slot after Contact Count still carries data (logged, not counted as a contact).
  - `prev-frame-incomplete`: a new frame started before the previous one received all its contacts.
- `marker`: `finger` or `chin` on rows written by the F / C keys, empty otherwise.
- `pressure`: Tip Pressure (usage 0x0D/0x30) of the contact if the pad reports it, empty otherwise.
  The Synaptics pad here has none (the caps dump says `Pressure no` in its summary line); run `--diag`
  on another laptop to see whether its pad has it.
- `zone`: the button zone of the contact's touch, by the background mode's rule and the `zones`,
  `zoneSettleMs` and `zoneMode` in settings.json (see Phase 3): `undecided` on the rows before
  `zoneSettleMs` has passed since the touch's first report, then `left`, `right`, `middle`, `none`, or
  `move` (the move area) by where the touch was at that moment; with `"zoneMode": "position"`, by where
  it is on each row. A press or a lift decides an undecided touch at once. Unlike the background mode it
  ignores ownership: in strategy 2 a finger in a zone shows the zone here although the background mode
  leaves it to Windows. It decides per report, the background mode per frame; on this Synaptics pad each
  report is a whole frame.

There is one row per contact per report, a row with empty contact columns for reports with no contacts,
and one row per vanished contact.

## Phase 2: background mode

Start `publish\ChinPad.exe` (double-click, a shortcut, or from a terminal; no console window opens).
A dot appears in the notification area (it may first land in the overflow `^` menu; drag it onto the
taskbar to keep it visible):

| Icon | Meaning |
|---|---|
| green | active: ChinPad moves the cursor for the contacts it owns (see the strategies) |
| grey | paused: ChinPad only watches; Windows handles the touchpad alone |
| red | something failed: no touchpad found, raw input could not be registered, or the mouse hook (strategy 3 or `hookClicks`) could not be installed. The tooltip says which; `chinpad.log` has the details |

- **Ctrl+Alt+P** pauses and resumes (configurable, see `hotkey`), and so does **Pause** in its
  right-click menu. Up to 0.3.1 a double-click on the icon paused too; since 0.4.0 it opens the settings
  window.
- **Keep a mouse or the keyboard in reach** while trying settings, in case the cursor misbehaves: the
  hotkey or the tray menu turns ChinPad off at once.
- Right-click menu: Settings... (0.4.0), Save diagnostic bundle... (0.4.0), Pause, Reload settings, Open
  settings file, Open log, Open README, Start at login (Phase 3), About ChinPad... (0.8.0), Exit. The tooltip shows the state,
  the last click ChinPad sent and, since 0.5.0, "left button locked" while ClickLock holds a button.
- Only one background instance runs per Windows session; a second start exits at once (noted in the log).
- `--seconds N` makes it exit by itself after N seconds (for tests).

### Strategies (`strategy` in settings.json)

Windows keeps moving the cursor for the contacts it accepts (fingers). ChinPad must not move the cursor
a second time for those, and that is where the three strategies differ:

| | ChinPad moves the cursor for | Use it when |
|---|---|---|
| **1** | every contact (tip on) | the touchpad is switched **Off** in Settings > Bluetooth & devices > Touchpad and raw reports still arrive (check with `--diag`, key `D`). Windows then moves nothing and ChinPad everything. With the touchpad On, fingers would move the cursor twice. |
| **2** | only contacts the firmware flagged as palm (confidence 0), such as the chin; "Let Windows handle fingers" checked in the settings window | fingers stay with Windows (gestures, tapping, everything as before) and the chin is handled by ChinPad. With `claimOnce` the chin stays ChinPad's until it lifts, even across the short confidence blips the firmware sends. The default up to 0.4.0. |
| **3** (default since 0.5.0) | every contact, like 1, plus a low-level mouse hook that drops Windows' own cursor moves while a confident contact (a finger) is down and for 50 ms after; "Let Windows handle fingers" unchecked | 1 is not possible (no raw reports with the touchpad Off). It is what works on this laptop (see below). Costs: Windows' touchpad moves are thrown away (gestures still run), and a real mouse moved while a finger rests on the pad is ignored too. |

Hand-off: ChinPad tracks every contact from its first report, so when a chin's confidence flips to 0
ChinPad continues from where the contact is at that moment; the cursor never jumps back to where the
touch started. One contact drives the cursor at a time: it keeps driving until it lifts, then the
owned contact with the lowest contact ID takes over. Other contacts are ignored, not added up.

**Strategy 1 on this laptop: not available.** With the touchpad switched Off in Settings, this
Synaptics pad stops reporting contacts (at most the bare button bit still comes through), so there is
nothing for ChinPad to move. Strategy 1 therefore cannot be used here; it stays in for laptops whose
pad keeps reporting with the toggle Off. Check on another laptop before relying on it: run `--diag`,
press `D`, switch the touchpad Off and touch the pad. If reports/s stays at 0, strategy 1 cannot work
there either. With strategy 1 set, the log says at start:
`strategy 1: switch the touchpad Off in Settings > Bluetooth & devices > Touchpad so Windows does not
move or click too; hookClicks is then unnecessary`.

**Configuration in use on this laptop (confirmed 2026-10-22):** touchpad On, `"strategy": 3`,
`"zoneMode": "position"`, `"hookClicks": true`, the two default zones in the bottom strip. In this
setup the strip is dead to fingers and chin alike (nothing moves there, a press clicks), the area above
moves the cursor for both, and Windows' own touchpad moves and clicks are dropped by the hook while
anything is on the pad. Windows gestures (two-finger scroll, swipes, tap to click) do not work on the
pad in this mode, and a real mouse is ignored while a touch rests on the pad. Since 0.5.0 strategy 3 and
zoneMode position are the defaults for a new settings.json; files that exist keep their values.

### Settings

`settings.json` is created on first run, in `%LOCALAPPDATA%\ChinPad`, or next to the exe in portable
mode (see "Where the settings live"; up to 0.3.1 always next to the exe). The settings window (0.4.0)
writes this same file. If it lacks keys (for example the Phase 1 file
with only `device`), ChinPad writes it back once with every key and its current value (JSON comments in
the file are not kept when that happens). Unknown keys are ignored and noted in the log. Out-of-range
values are clamped and noted in the log. A file that cannot be read (JSON syntax error, wrong type) is
left untouched and the previous settings stay in use. Changes are applied while ChinPad runs, about
0.3 s after the file is saved (or with Reload settings); `startPaused` only matters at start.

```json
{
  "device": "",
  "strategy": 3,
  "claimOnce": true,
  "speedPxPerMm": 12,
  "acceleration": {
    "enabled": false,
    "exponent": 1.5,
    "thresholdMmPerS": 30
  },
  "deadzoneMm": 0.3,
  "smoothing": 0.3,
  "maxStepPx": 150,
  "moveMode": "relative",
  "hotkey": "Ctrl+Alt+P",
  "startPaused": false,
  "language": "auto",
  "zones": [
    { "action": "left", "x": [0.0, 0.5], "y": [0.72, 1.0] },
    { "action": "right", "x": [0.5, 1.0], "y": [0.72, 1.0] }
  ],
  "zoneSettleMs": 40,
  "zoneMode": "position",
  "pressOutsideZones": "ignore",
  "hookClicks": true,
  "clickBeep": true,
  "mergeMm": 20,
  "clickLock": {
    "enabled": false,
    "holdMs": 600
  },
  "blockDoubleClick": true,
  "doubleClickWindowMs": 500
}
```

| Key | Default | Range | Meaning |
|---|---|---|---|
| `device` | `""` | text | Case-insensitive part of the touchpad's device path, e.g. `"SYNA2BA6"`. Empty = all touchpads, each tracked separately (the Logitech Bolt receiver, which also reports as a touchpad, does no harm). `--diag` and `--caps` print which touchpads this selects. The settings window's "Apply to" list writes the hardware-ID part of one pad's path (`"SYNA2BA6&Col02"`, `"VID_046D&PID_C548&MI_03"`). Since 0.5.0, when no touchpad's path contains it (an unplugged pad), all touchpads are used until it is back, with a line in the log. |
| `strategy` | `3` | 1, 2, 3 | See the table above. Other values fall back to 3. Up to 0.4.0 the default was 2. |
| `claimOnce` | `true` | true/false | Strategy 2: once a contact has reported confidence 0 it stays ChinPad's until it lifts. `false`: only while confidence is 0 (each blip back to 1 stops the cursor for that report and the motion of that report is lost). |
| `speedPxPerMm` | `12` | 0.1 - 200 | Cursor pixels per millimetre of chin travel (Windows' pointer settings come on top in relative mode). The pad is 132 x 77 mm. |
| `acceleration.enabled` | `false` | true/false | Speed-dependent gain on top of `speedPxPerMm`. |
| `acceleration.exponent` | `1.5` | 1 - 4 | Above the threshold the gain is `(speed / threshold) ^ (exponent - 1)`, limited to 1..4. |
| `acceleration.thresholdMmPerS` | `30` | 1 - 1000 | Chin speed in mm/s from which the gain starts to rise. |
| `deadzoneMm` | `0.3` | 0 - 20 | The cursor does not move until the contact has moved this far from where it started (or from where ChinPad took it over); after that every movement counts, including that first stretch. Keeps a resting chin from jittering the cursor. |
| `smoothing` | `0.3` | 0 - 0.9 | Smoothing of the contact position: 0 = off, 0.9 = heavy (smoother but laggier). |
| `maxStepPx` | `150` | 1 - 5000 | Largest cursor step per report (about 7 ms). Guards against firmware position jumps. At the defaults a 4.6 mm jump is 55 px and passes unchanged; with acceleration on it would be up to 220 px and is cut to 150. |
| `moveMode` | `"relative"` | `"relative"`, `"absolute"` | How the cursor is moved, see below. |
| `hotkey` | `"Ctrl+Alt+P"` | text | Pause/resume. Modifiers `Ctrl`, `Alt`, `Shift`, `Win` and one key, joined with `+`: letters, digits, `F1`-`F24`, `Pause`, `Scroll`, `Home`, `Insert`, `Esc`, ... A key without modifier is allowed only if it does not type (e.g. `"Pause"`, `"F13"`). `""` = no hotkey. If another program already uses the combination, the log says so and the tray menu still works. |
| `startPaused` | `false` | true/false | Start in the paused state. |
| `language` | `"auto"` | `"auto"`, `"en"`, `"sr-Latn"` | Interface language; auto uses Serbian Latin for any Serbian Windows display locale, English otherwise. Changed immediately in General; JSON stays invariant. |
| `zones` | bottom 28 % (21.5 mm), left / right half | list | Button zones, see Phase 3. An invalid zone is ignored with a note in the log. |
| `zoneSettleMs` | `40` | 0 - 500 | A new touch is undecided for this many ms after its first report: it does not move the cursor, and it is then classified by where it is at that moment. A press decides at once. `0` = by the first report, as in 0.3.0. See Phase 3. |
| `zoneMode` | `"position"` | `"landing"`, `"position"` | `"landing"`: the classification holds until the touch lifts. `"position"`: a touch is in a zone while it is inside one. See Phase 3. Up to 0.4.0 the default was `"landing"`. |
| `pressOutsideZones` | `"ignore"` | `"ignore"`, `"passthrough"` | A press with no zone touch down, see Phase 3. |
| `hookClicks` | `true` | true/false | Drop Windows' own clicks while ChinPad owns a touch, see Phase 3. |
| `clickBeep` | `true` | true/false | A short beep for each click ChinPad sends. |
| `mergeMm` | `20` | 0 - 60 | Contacts of one touchpad closer than this many mm are one touch (some pads report a chin as two contacts). `0` = off, exactly as 0.3.1. See "Merging split contacts" under Phase 4a. |
| `clickLock.enabled` | `false` | true/false | ClickLock: a zone press held `holdMs` keeps the button down after the release, until the next press. See "0.5.0". |
| `clickLock.holdMs` | `600` | 300 - 3000 | How long (ms) a press has to last before the button locks. |
| `blockDoubleClick` | `true` | true/false | A second press of the same button within `doubleClickWindowMs` after the previous click's down is dropped entirely (nothing is sent). `false` = as 0.4.0. See "0.5.0". |
| `doubleClickWindowMs` | `500` | 100 - 1500, or 0 | The window of `blockDoubleClick`; `0` = Windows' double-click time (Control Panel > Mouse; 500 ms unless changed). |

### Pointer precision and `moveMode`

With `"relative"` (default) ChinPad sends ordinary relative mouse moves, so Windows applies its pointer
speed and, if **Enhance pointer precision** is on (Settings > Bluetooth & devices > Mouse > Additional
mouse settings > Pointer Options), its own acceleration curve on top of ChinPad's. That setting is on
here: a 40 x 25 px step from ChinPad arrived as 56-71 x 34-43 px in tests, depending on timing.
`"absolute"` reads the cursor position, adds ChinPad's step and places the cursor there directly,
which bypasses Windows' ballistics: one pixel from ChinPad is one pixel on screen, and `speedPxPerMm`
and `acceleration` are the only speed settings. Absolute mode was checked pixel-exact on both
monitors (the 200 % laptop panel and the 100 % external one).

### Limitation: administrator windows and UAC

Windows does not let a normal (non-elevated) program move the cursor over windows that run as
administrator (Task Manager, an elevated terminal, some installers) or on the UAC prompt (secure
desktop). Over such windows the chin does nothing, and Windows does not report an error to ChinPad;
fingers keep working through Windows. The hook also does not see input going to those windows, so
Windows' moves (strategy 3) and clicks (`hookClicks`) are not filtered there either. See
"Elevated windows: your options" at the end for what could be done about it, and at what cost.

### Log

`chinpad.log` in the settings folder (tray menu: Open log). It records where settings and log live
(portable mode or `%LOCALAPPDATA%\ChinPad`), start and stop with totals (frames, cursor moves, moves and
clicks dropped by the hook, clicks sent per button, presses ignored, zone and move touches, the merging
counters, SendInput failures), saved diagnostic bundles, the touchpads found and used, the zones of each pad in mm, what the
mouse hook is for, the Start at login state, the hotkey registration result, every settings load or
reload with the values in use and any validation notes, pause/resume, every click ChinPad sends (down
with the pad position in mm, up with the reason and how long it was held), ignored presses (at most one
line a minute, with a count), and errors. Since 0.5.0 also: ClickLock's lock moment (`click left locked
(pressed 602 ms, ...)`) and the press that ends it (`click left up (lock released by a press in the right
zone, ...)`), second presses dropped by blockDoubleClick (at most one line a minute, with a count), and
touchpads plugged in or removed (`touchpads changed (...)`, `touchpad added: ...`, `touchpad removed: ...`,
and the fallback to all touchpads when the one in "device" is gone). New lines are appended; a log larger than 1 MB is emptied at
start.

## Phase 3: button zones and clicks

A clickpad has one physical click under the whole surface. Phase 3 turns parts of the pad into
buttons, so that the chin can click by pressing the pad, and keeps accidental chin presses from
clicking. There is no tap-to-click, no dwell click and no hold-to-click: only a physical press clicks.

### Where a touch settles decides what it is (`zoneSettleMs`, `zoneMode`)

A chin does not land like a fingertip. Its first reported point is where the skin first touches, and
it rolls a few millimetres while it settles: on this pad up to 1.6 mm in the first 50 ms. In 0.3.0 the
first report decided, so a chin aimed at the strip that first touched a few mm above it counted as a
movement touch: the cursor moved and the press was ignored (6 times in a 6-minute test). So:

- **Settling** (`zoneSettleMs`, default 40 ms): for this long after its first report a new touch is
  undecided and does not move the cursor. Then it is classified by where it is at that moment: in a
  zone (the first listed zone that contains that point) or in the move area.
- **A press decides at once**: pressing the pad while a touch is still settling classifies it by where
  it is when the press comes, and the press then counts as usual. A touch that lifts while settling is
  classified by its last position (only for the counters in the log).
- `"zoneSettleMs": 0` classifies by the first report, exactly as 0.3.0 did. With `"zones": []` there is
  nothing to decide and touches move at once.
- The cost: every touch ChinPad moves the cursor for (in strategies 1 and 3 fingers too) starts moving
  only after those 40 ms; the travel during them is not used, and the cursor starts from where the touch
  is then.

What the classification means afterwards depends on `zoneMode`:

**`"landing"`** (the default up to 0.4.0): the classification holds until the touch lifts. This is "the strip does not
move the cursor, it only clicks".

- **Settles in a zone**: it never moves the cursor, even if it slides out of the zone (one chin press
  here drifted 12 mm upward, out of the strip, during 1.4 s). Pressing the pad while it is down clicks
  that zone's button.
- **Settles in the move area**: it moves the cursor, even if it slides into a zone. Pressing the pad
  while it is down does not click (see "Presses outside the zones").
- **Settles in a `"none"` zone** (a dead area): it does nothing at all. No cursor movement, no click,
  and a press while it is down is ignored.
- The chin alone cannot drag (press, move, release): point first, then lift and put the chin down in a
  zone to click. In exchange a press in the strip can never turn into a drag, however far the chin drifts.

**`"position"`** (the default since 0.5.0): after settling, what counts is where the touch is now.

- Inside a zone it does not move the cursor, and a press while it is inside clicks that zone's button.
  Outside every zone it moves the cursor.
- Sliding into a zone stops the cursor; sliding out again moves it on from where the touch is then (no
  jump; the deadzone applies again). So you can point, slide down into the strip and click without
  lifting the chin.
- A button ChinPad holds stays down until the press is released, even if the touch leaves the zone
  meanwhile, and outside the zone the touch moves the cursor: pressing in the strip and drifting out of
  it drags (selects text, moves a window or a file). That is the price of this mode, and also the only
  way to drag with the chin alone.

Which touches all this applies to depends on the strategy:

- **strategy 2** (the default up to 0.4.0): only the touches ChinPad owns, that is the chin once the firmware has
  flagged it as a palm. A finger in a zone stays with Windows (it moves, taps and clicks the way Windows
  decides), so other people's fingers keep working. The palm flag arrives 1 to 3 reports (7 to 20 ms)
  after the chin lands, normally before the 40 ms of settling are over; the two run side by side and the
  touch is classified at the settle moment either way (a finger that is never flagged simply stays
  Windows'). A press in those first milliseconds, before the flag, is Windows' business.
- **strategies 1 and 3** (3 is the default since 0.5.0): every touch, fingers included.

**The default strip** is the bottom 28 % of the pad: 21.5 mm of 76.8 mm on this pad, from 55.3 mm below
the top edge. In 0.3.0 it was 23 % (17.7 mm). Measured here: aiming for the strip, the chin landed
61-71 mm below the top edge; touches meant to move the cursor landed at 41 mm or less. An existing
settings.json keeps its zones (ChinPad only adds missing keys), so to get the taller strip change `0.77`
to `0.72` in both zones, or delete the `zones` key and let ChinPad write the defaults back.

### Clicks

- Press the pad while exactly one zone touch is down: that zone's button goes down. Release the press:
  the button goes up. A double click is two presses (with `blockDoubleClick`, on since 0.5.0, the second
  one is dropped when it comes within `doubleClickWindowMs`; see "0.5.0").
- Lifting the chin while the pad is still pressed releases the button at once. Pause and Exit release
  it too: ChinPad never leaves a button held down. The one exception is ClickLock (0.5.0, off by
  default), which keeps a long press down on purpose until the next press; pause and Exit release it too.
- Sliding out of the zone while pressed does not release the button. In `"zoneMode": "landing"` the
  cursor stays put; in `"position"` it moves, which drags.
- Two zone touches down at once (strategies 1 and 3), or a touch in a `"none"` zone: the press is ignored.
- Feedback: a short beep (`clickBeep`), the last click in the tray tooltip, and every click in
  `chinpad.log` with the pad position in mm.

### Presses outside the zones (`pressOutsideZones`)

- `"ignore"` (default): a press with no zone touch down does nothing. This is the fix for the chin
  pressing the pad by accident while it moves the cursor. Windows sees the physical press too;
  `hookClicks` keeps it from clicking (next section).
- `"passthrough"`: for pads with real, separate buttons. Button 1, 2, 3 of the pad (bit 0, 1, 2 of the
  `button` column in `--diag`) become a left, right, middle click at the cursor.

Either way, ChinPad leaves a press alone when Windows' own click gets through anyway (strategy 2 or 3
with no ChinPad touch on the pad, e.g. only a finger, or with `hookClicks` off): a second click from
ChinPad would make it a double click.

### Changing the layout (`zones`)

`zones` in settings.json is a list. Each zone is `{ "action": ..., "x": [from, to], "y": [from, to] }`:

- `action`: `"left"`, `"right"`, `"middle"`, or `"none"` (dead area).
- `x`: fractions of the pad width, 0 = left edge, 1 = right edge.
- `y`: fractions of the pad height, 0 = top edge (next to the keyboard), 1 = bottom edge (nearest you).
  Check the orientation once with `--diag`: its `zone` column shows how each touch was classified.
- Each range must lie inside 0..1 with from < to; the edges belong to the zone. Where zones overlap,
  the first one in the list wins (the log notes overlaps). A zone with a mistake is ignored and the log
  says why; the rest of the file still loads.
- `"zones": []` turns zones off: every touch moves the cursor, and every press is a press outside the zones.

From millimetres to fractions: `--caps` prints the pad size, for this Synaptics pad
`X ... -> 132.0 mm` and `Y ... -> 76.8 mm`. A fraction is mm divided by that size:

- a 15 mm strip along the bottom edge: `"y": [0.805, 1.0]` (1 - 15 / 76.8 = 0.805);
- the left 40 mm: `"x": [0.0, 0.303]` (40 / 132);
- the default `0.72` is the bottom 21.5 mm (0.28 x 76.8); 0.3.0's `0.77` was 17.7 mm.

Examples:

```json
"zones": [
  { "action": "left",   "x": [0.0, 0.4], "y": [0.72, 1.0] },
  { "action": "middle", "x": [0.4, 0.6], "y": [0.72, 1.0] },
  { "action": "right",  "x": [0.6, 1.0], "y": [0.72, 1.0] },
  { "action": "none",   "x": [0.0, 1.0], "y": [0.0, 0.1] }
]
```

Three buttons along the bottom, and a dead strip along the top edge where the chin may brush the pad.
Changes apply about 0.3 s after saving; a touch already classified keeps its zone. At start and
after each change the log lists the zones of every pad in mm, e.g.
`#1 zone 1 left: x 0.0-66.0 mm, y 55.3-76.8 mm from the top-left corner (66.0 x 21.5 mm)`. When ChinPad
writes the file back (only when keys are missing) it puts each zone on one line, without the extra
spaces of the example.

### Keeping Windows' own clicks out of the way (`hookClicks`)

The press is a real click of the pad, and Windows handles it as well. With the chin on the pad, a zone
press could then click twice (Windows' click and ChinPad's, perhaps different buttons), and an
accidental press would click although ChinPad ignores it. With `hookClicks: true` (default) ChinPad's
low-level mouse hook drops Windows' own left, right and middle button events while ChinPad owns a touch
on the pad (strategy 2: the chin from the moment it is flagged as a palm; strategies 1 and 3: any touch)
and for 100 ms after it lifts. ChinPad's own clicks are marked and always pass. A button press that got
through before the filtering started still gets its release, and a press that was dropped has its
release dropped too, so no button is left stuck. The wheel and the side buttons are never filtered.

Trade-offs:

- While the chin rests on the pad, and for 100 ms after it lifts, clicks of a real mouse (and clicks
  that other programs generate) are dropped too. In strategies 1 and 3 that holds for any touch,
  a resting finger included.
- Nothing is filtered over administrator windows (the hook does not see input going there), and
  ChinPad's own clicks do not reach those windows either; there, a press is handled by Windows alone.
  See "Elevated windows: your options".
- `hookClicks: false`: no filtering. Zone presses then may click twice and accidental presses click.
- The hook runs when `hookClicks` is true or `strategy` is 3; the log says which jobs it does.
  `chinpad.log` reports at exit how many Windows clicks it dropped: on a new pad that number shows
  whether Windows clicks at all for a press with only the chin (a palm) on the pad.

The other route needs no hook: **strategy 1 with the touchpad switched Off** in Settings > Bluetooth &
devices > Touchpad. Windows then ignores the pad completely (no cursor, clicks or gestures, for fingers
too) and ChinPad does everything, zones included. Check first with `--diag` (key D) that raw reports
still arrive with the touchpad Off (on this laptop they do not, see "Strategy 1 on this laptop" under
Strategies). With this route `hookClicks` can be `false`; left on, it would only drop a real mouse's
clicks while a touch rests on the pad.

### Click beep (`clickBeep`)

A beep of about 60 ms for every click ChinPad sends: 1200 Hz for left, 800 Hz for right, 1000 Hz for
middle. It plays through Windows' beep on the default sound device, on a background thread, so it never
delays the cursor. `"clickBeep": false` turns it off. ClickLock's lock moment (0.5.0) is marked by two
short tones (50 ms each, 200 Hz above the button's tone); they play with `clickBeep` off too, because
they say when the press may end.

### Start at login

Tray menu > **Start at login** (a check mark when on, off by default). It writes the value `ChinPad`
under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` with the quoted path of the running
ChinPad.exe; choosing it again removes the value. No administrator rights are needed, and ChinPad then
starts at sign-in as you, not elevated, as with a double-click. If you move ChinPad.exe, switch it off
and on again from the new place (the check mark shows only when the value points at the running exe).
By hand: `reg query HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v ChinPad` shows it,
`reg delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v ChinPad /f` removes it.

**Open README** in the tray menu opens the README.md that `publish` places next to ChinPad.exe; the
item is greyed out when that file is missing.

### Diagnostic additions

- CSV: `pressure` and `zone` columns at the end (see "CSV columns").
- Live table: a `zone` column (`undecided` while a touch settles).
- Caps dump: a `Pressure` line in the finger fields and `Pressure yes/no` in the summary line. The
  Logitech Bolt receiver here declares Tip Pressure; the Synaptics pad does not.

### Tested so far, and not

No one could touch the pad while Phase 3 was built. The zone, press, lift, pause and strategy rules
were checked with synthetic reports built from this Synaptics pad's own descriptor (button bit
included) going through the same code as live reports; the hook and the clicks were checked with real
injected input on a test window (a foreign click is dropped while a chin is "down", ChinPad's click
arrives, no stuck buttons). Not yet seen on the real pad: how the chin presses, whether this pad's
firmware keeps reporting the chin while it presses, and whether Windows itself clicks for a
palm-only press (the hook total at exit answers that).

0.3.1 (settling, `zoneMode`, the 0.72 strip) was also checked with synthetic reports only, through the
same code as live reports: a chin landing 2 mm above the strip and sliding 5 mm into it within 30 ms
clicks and never moves the cursor; the same chin sliding in only after 60 ms is a move touch; a press
at 15 ms decides at once; `zoneSettleMs` 0 passes all the 0.3.0 checks unchanged; no touch moves the
cursor during the 40 ms in any strategy or mode; position mode stops at the strip, clicks inside,
resumes without a jump and keeps a held button while drifting out. Not yet tried with the chin.

## Phase 4a (0.4.0): settings window, diagnostic bundle, merging, settings location

Phase 4 makes ChinPad usable by people who never edit a file. 4a adds the settings window, the
diagnostic bundle, merging of split contacts and a proper settings location; 4b will add a first-run
wizard and 4c the packaging. This laptop's working setup is unchanged: its settings.json sits next to
`publish\ChinPad.exe`, so ChinPad stays in portable mode there with strategy 3, `zoneMode` position,
`hookClicks` and the 0.77 strip; the only new key written into it is `mergeMm` (20).

### Where the settings live (`SettingsPaths`)

At start ChinPad looks for `settings.json` next to the exe. Found: portable mode, settings.json,
chinpad.log, `--diag`'s CSV and diag-caps.txt stay in the exe's folder, and the diagnostic bundle's save
dialog starts there too. Not found: everything goes to `%LOCALAPPDATA%\ChinPad` (created when missing)
and the save dialog starts on the Desktop. If that folder cannot be created, the exe's folder is used
and the log says why. The first lines of chinpad.log name the mode and the folder, for example
`portable mode: settings.json found next to ChinPad.exe, so settings and log are in C:\...\publish`.
README.md is still looked for next to the exe (Open README).

### Merging split contacts (`mergeMm`)

Some pads report a chin as two contacts a few millimetres apart, which up to 0.3.1 broke clicks
("pressed with 2 zone touches down") and made the cursor stop or restart when one of the two lifted.
Contacts of one pad within `mergeMm` (default 20 mm) of each other now count as one touch:

- **Clicks**: for a press, zone contacts within `mergeMm` of each other (also through a chain of them)
  are one zone touch, and the zone of the earliest of them decides the button. Only separate touches
  make a press "pressed with 2 zone touches down". The log line of such a click says
  `..., 2 contacts merged into one touch`.
- **Classification**: a contact that is still settling while a decided contact of its pad lies within
  `mergeMm` takes that contact's classification at once (zone or move) instead of waiting for its own
  `zoneSettleMs`. Of two contacts that land together, the first settles by its position and the
  second follows it in the same frame. With `"zoneMode": "position"` each contact is then classified by
  its own position from the next frame on, as before.
- **Cursor**: when the contact that moves the cursor lifts or vanishes and another contact ChinPad owns
  is within `mergeMm` of where it was, that partner continues in the same frame: from its own position
  (no jump) and without arming the deadzone again. While the driver is down, its partner is ignored like
  any other contact.
- **Held button**: when the contact holding a zone button lifts while a partner is still down, the
  partner keeps the button down until the press is released.
- The log line at stop counts it: `merging (mergeMm 20): 3 contacts merged into a neighbouring touch,
  1 presses with merged zone contacts, 0 held buttons kept by a partner, 2 cursor hand-overs to a partner`.
- Two fingers closer than `mergeMm` are one touch too (in strategies 1 and 3, or for clicks). `--diag`
  does not merge: its zone column shows each contact on its own.
- `"mergeMm": 0` turns all of this off; ChinPad then behaves exactly like 0.3.1 (checked by running the
  same 288,000 frames of random touches through both versions).

### The settings window (`SettingsForm.cs`)

Opened from the tray (Settings... or a double-click); one instance, hidden on Close, disposed at Exit.
It runs on the UI thread with the engines and only does short work per event. It writes settings.json
with `AppSettings.Save` (sliders 150 ms after their last change, check boxes and the radio at once);
the existing file watcher reloads it about 0.3 s later, logged as `settings reloaded (written by the
settings window)`. When the file changes from outside (an editor, Reload settings), the window takes
the new values unless one of its own changes is still waiting. The strip slider only works for the
standard layout (exactly two zones, left over x 0..0.5 and right over 0.5..1, the same y range ending at
1.0); it moves the top edge of both zones and writes it with 4 decimals (25 mm on this pad: 0.6745).
Any other layout shows "Custom layout (edit settings.json)". Strategy 1 shows "Expert: strategy 1 set in
settings.json" and leaves the strategy alone. While the pause-key box has the focus, the current key is
released so that pressing it is recorded rather than pausing ChinPad.

The live drawing polls `BackgroundMode.Live()` about 30 times a second: per pad its geometry and touches
(`PadSnapshot`: ID, position in mm, state), the last press, and the counters. The pad drawn is the one
touched last (before any touch: the one with the most reports), so on this laptop it is the Synaptics
pad rather than the Logitech receiver listed first. Fonts are 11 pt, buttons at least 44 px high at
100 % scaling, and the window rescales when it moves between the 100 % and 200 % screens.

### The diagnostic bundle (`DiagnosticBundle.cs`, `ReportRecorder.cs`)

`ReportRecorder` is the CSV writer that `--diag` used inside DiagMode, factored out unchanged (same
header, same rows, same zone column); `--diag` writes byte-identical rows apart from the clock columns.
The bundle records 15 s into a temporary CSV from the background mode's normal input path (the cursor
keeps working), shows a small always-on-top countdown window that does not take the focus (Cancel stops
it), then asks where to save and writes the zip with `System.IO.Compression`; the temporary folder is
deleted afterwards. The log notes the recording and the saved path.

### Tested so far, and not

Checked with synthetic Synaptics reports through the same code as live input, and with the window
driven on the screen (the user's ChinPad instance and settings untouched): two contacts 8 mm apart in
the left zone press as one LEFT click; the earliest contact's zone wins across the middle; a driver that
lifts or vanishes hands over to its partner 8 mm away with continuous 2 px steps and no jump; a partner
is classified on its first frame; a held button survives the holding contact's lift; contacts 40 mm
apart are not merged; `mergeMm` 0 reproduces 0.3.1; the settings location rules; the strip slider's
mm/fraction round trip; every control of the window writes the file and the app reloads it; the bundle
zip and its five files. Not yet tried with the chin, and not yet on a pad that really splits a chin.

## 0.5.0: new defaults, choosing the touchpad, ClickLock, double-click blocking, "?" help

### New defaults

A new settings.json (and **Restore defaults...**) now has `"strategy": 3` and `"zoneMode": "position"`, the
configuration this laptop runs. An existing settings.json keeps its values: ChinPad only adds the keys it
lacks (here `clickLock`, `blockDoubleClick`, `doubleClickWindowMs`). Only a file that never had `strategy`
or `zoneMode` written in it (none since 0.3.1 wrote every key back) picks up the new values.

### Choosing the touchpad ("Apply to") and hot-plug

The settings window's General section has an **Apply to** list: "All touchpads", then every touchpad found,
built-in ones first (`PadChoice`):

- `Touchpad 1: built-in SYNA2BA6 (132 x 77 mm)` for a path without a vendor ID (an I2C pad; the ACPI
  hardware ID without its `&ColNN` part);
- `Touchpad 2: external Logitech 046D:C548 (117 x 76 mm)` for a USB (`VID_046D&PID_C548`) or Bluetooth
  (`_VID&0002046d_PID&b035`, Bluetooth LE `_Dev_VID&02045e_PID&0b13`) path, with the vendor's name for
  Logitech, Synaptics, ELAN, Microsoft, Apple, Razer and Goodix and the hex IDs only for others. A pad
  that is built in but connected by USB inside the laptop shows as external too.
- The size comes from the pad's report descriptor and is left out when the pad does not report it.
- `, reporting now` is appended to a pad that sent a report in the last 2 s (ChinPad keeps every pad's
  last report time, the ones it does not use included), so touching a pad shows which entry it is. The
  list refreshes twice a second while the window is open; when only the texts change, the entries are
  updated in place and the entry shown stays (also while the open list is browsed with the arrow keys).

Choosing an entry writes `device`: the hardware-ID part of that pad's path (`"SYNA2BA6&Col02"`,
`"VID_046D&PID_C548&MI_03"`), or hardware and instance ID when two identical pads would otherwise match
the same text; "All touchpads" writes `""`. The usual reload applies it. A `device` written by hand that
matches exactly one pad shows that pad's entry; one that matches several or none shows an entry of its own.

Touchpads plugged in or removed: the background mode registers raw input with `RIDEV_DEVNOTIFY` as well
and gets `WM_INPUT_DEVICE_CHANGE` (`GIDC_ARRIVAL` / `GIDC_REMOVAL`; the arrivals Windows reports right
after the registration for pads already known are ignored). 500 ms after the last such message it
enumerates again (`TouchpadSet.Refresh`): a pad that is still there keeps its object and its number (#N
in the log), a new one gets the lowest free number, and the log lists what changed. Then the selection is
applied again; when it gives the same pads as before, nothing is reset (a touch, or a locked button, on
them carries on). When `device` names a pad that is not connected, all touchpads are used until it is
back, with a log line (`no touchpad path contains "..." ...: using all touchpads until it is back`), and
settings.json keeps the choice; the list shows it as `"..." (not connected; all touchpads are used
meanwhile)`. This fallback also applies at start and after a reload; up to 0.4.0 such a `device` turned
the dot red. **Refresh** next to the list does the same by hand at once. `--diag` and `--caps` are
unchanged (no device notifications; `--caps` notes the fallback).

### ClickLock (`clickLock`)

With `"clickLock": { "enabled": true }` a zone press that has lasted `holdMs` (600 ms; 300 - 3000) locks
its button: two short beeps mark the moment, and the button stays down after the physical release and
after the chin lifts, so the chin can move the pointer to drag or select text. The lock is checked at
every frame of any pad and, between frames, by a 50 ms timer that runs only while such a press is held.

- The next press ends it, and only sends the pending up (no new click; blockDoubleClick does not apply
  to it): a press with a touch in a left, right or middle zone of the pad, a press of button 2 or 3 of a
  pad ("the button of another zone" on pads that report one), any press with `"pressOutsideZones":
  "passthrough"`, or a press Windows clicks for itself. A press ChinPad ignores anyway (no zone touch with
  `"ignore"`, a dead-zone touch alone) leaves the lock alone, so an accidental press in the move area
  does not drop what is being dragged.
- Pause, Exit, a change of the touchpads in use and a reload that switches `clickLock` off release it.
  Lifts never do, a merged partner's lift included.
- Only zone buttons lock (not passthrough). The tray tooltip and the settings window show `left button
  locked` meanwhile; the log has the lock and the press that ended it.
- Settings window: **Click lock** check box with a **Hold the button** slider (300 - 3000 ms).

### Ignoring accidental double clicks (`blockDoubleClick`, `doubleClickWindowMs`)

On by default: a press that would send a button down less than `doubleClickWindowMs` (500 ms; 100 -
1500; `0` = Windows' double-click time, `GetDoubleClickTime`) after the previous down of the same button
is dropped entirely: no down, and no up on its release. The window counts from the last click that was
sent, so a third press after the window clicks again. Left, right and middle each have their own; zone
presses and passthrough presses alike. The counter is in the log at stop and in system.txt; the log notes
dropped presses at most once a minute. The cost: no double-click with the chin (click once and press
Enter, or switch it off). Settings window: **Ignore a second click** check box with a **Second click
within** slider (100 - 1500 ms; a 0 written by hand shows as "as Windows (500 ms)").

### The "?" help

Every setting in the settings window (each slider, check box, the radio group, the list and the pause
key box) has a round "?" (36 px at 100 % scaling, a Tab stop right after its setting, no Alt+letter), and
so does the drawing (its colours). Hovering shows the explanation as a tooltip for 30 s; a click, or Space,
opens it in a popup under the "?" that stays until it is clicked, Esc is pressed (the next Esc closes the
window as before) or another "?" is clicked; clicking the same "?" again closes it. The popup is drawn
inside the settings window, so it has the window's scaling, and it never takes the focus. The texts say
what the setting does, what up and down change, and which way to go for a common complaint ("Pointer too
jumpy or shaky: raise Smoothing a little"), without technical words. They are all in `HelpTexts.cs`, keyed
by the settings.json name; the window's other texts are in `SettingsText` (SettingsForm.cs) and the list's
words in `PadChoice.Text`, ready for translation. The old tooltips on the sliders and check boxes are gone:
the "?" carries their text now. Check boxes wrap their text onto a second line when the window is narrow.

### Tested so far, and not

Checked with synthetic Synaptics reports through the same code as live input (the Phase 3 harness, now
140 checks), and with the settings window of a real BackgroundMode in a scratch portable folder whose
engines send their buttons to a recorder instead of Windows (no input reached the desktop): ClickLock
locks at the first frame 600 ms after the down, survives the release, the lift and a partner's lift, lets
the chin drag, and the next press in either zone (or button 2) sends only the up; shorter presses click
normally; pause and switching it off release it; a press in the move area leaves it alone. A second press
within 500 ms sends nothing, a third after the window clicks, window 0 follows `GetDoubleClickTime`, and a
press that ends a lock is not dropped. The labels of the two real pads and of made-up Bluetooth paths; each
entry's `device` selects exactly its pad; the receiver removed and added again through the enumeration
seam and a synthetic `WM_INPUT_DEVICE_CHANGE` (fallback to all touchpads, the list refreshed, Refresh);
"reporting now" with the selection kept while the list is open; every "?", its tooltip and popup (Esc,
click, another "?"). The 0.4.0 checks pin the 0.4.0 defaults (strategy 2, landing, blockDoubleClick off)
and pass unchanged. Not yet tried with the chin, nor with a touchpad really plugged in or removed.

## 0.6.0: languages

The interface includes English and Serbian in Latin script. In the settings window, under **General**,
**Language / Jezik** offers **Automatic (Windows)**, **English** and **Srpski (latinica)**. Switching
changes the open window, help, tray menu and tooltip immediately. Value labels use the chosen language's
number format (for example `0.30` in English and `0,30` in Serbian); the settings file always uses decimal points.

The `language` key in `settings.json` accepts `"auto"` (the default), `"en"` or `"sr-Latn"`:

```json
"language": "auto"
```

`auto` checks `CultureInfo.CurrentUICulture.Name`, the Windows display language. Any name starting with
`sr` selects Serbian Latin, including Serbian Cyrillic locales; all other locales select English.
Missing keys are written back just like other settings. Invalid values fall back to `auto` with a log note.

Text lives in plain C# dictionaries in `Strings.en.cs` and `Strings.sr-Latn.cs` (UTF-8 with BOM).
`L10n.T(key)` falls back to English if a translation is absent and records that key in `L10n.MissingKeys`.
There are no resource assemblies or localization dependencies; the single-file publish still bundles
only English framework resources. App-owned confirmation buttons are translated too. The file picker
is a Windows dialog: its built-in controls follow Windows, while its title and file-type label follow ChinPad.

To add another language, copy `Strings.en.cs` to `Strings.<code>.cs`, rename its dictionary field in the
partial `Strings` class, and translate every value. Keep the keys, `{0}` placeholders (including numeric
formats), line breaks and unique `&` keyboard mnemonics. Save the source as UTF-8 with BOM. Add the code
to validation in `AppSettings.Validated`, selection and culture in `L10n.Apply` / `L10n.T`, and the dropdown
in `SettingsForm`. Extend the UI harness's parity and switching checks, and render the long texts at
100 % and 200 %. `selftest4` checks key and placeholder parity, switching every control back and forth,
source literals, translated dialogs, and layout at both DPI settings. The engine harnesses still pass
53 and 140 checks. Under `--no-real`, the two startup-value checks use an in-memory test seam; they do
not touch HKCU Run. Logs, CSV columns, capabilities/diagnostic console output, diagnostic bundle metadata,
settings keys and values, and this README stay in English for support and compatibility. Product names,
hardware identifiers, file names, units and keyboard key names also keep their standard spelling.

## Elevated windows: your options

Why the chin stops at some windows: Windows keeps programs apart by integrity level. ChinPad runs as
you at the normal (medium) level, so it cannot move the cursor or click in windows of programs that run
as administrator (Task Manager, an elevated terminal, many installers), and its hook does not see input
going there. The UAC prompt ("Do you want to allow this app to make changes to your device?") appears
on a separate secure desktop where no program of yours runs at all. Fingers keep working everywhere
through Windows.

The first-time setup wizard can set up option a. Options b and c are manual; each option needs administrator rights at least once, and none of them
is required. Your account here is an administrator account (UAC asks for confirmation, not for another
account's password), so all three are open to you.

### a) Run ChinPad elevated: a scheduled task at logon

A Task Scheduler task that starts ChinPad at logon with "Run with highest privileges" runs it as
administrator, without a UAC prompt at each start.

- Gains: the chin moves and clicks in administrator windows, and the hook filters there too.
- Does not reach: the UAC prompt's secure desktop, the lock screen, the sign-in screen (see c and d).
- Costs: ChinPad, and any bug in it, runs with full administrator rights the whole time. Whoever can
  change ChinPad.exe or the files next to it gets their code run as administrator at your next logon,
  and `publish` is in your Documents folder, which every program you run can write to. Programs that
  ChinPad opens (Notepad for settings.json, the log, the README) run elevated too.
- Creating the task needs one UAC prompt. Turn **Start at login** off first, otherwise two copies start
  (the second exits, with a note in the log).

Create it in a PowerShell window opened with "Run as administrator" (one UAC prompt):

```powershell
$exe  = "C:\path\to\ChinPad.exe"            # where ChinPad.exe is
$user = "$env:USERDOMAIN\$env:USERNAME"     # your account, for example PCNAME\Name or AzureAD\Name
$action    = New-ScheduledTaskAction -Execute $exe -WorkingDirectory (Split-Path $exe)
$trigger   = New-ScheduledTaskTrigger -AtLogOn -User $user
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName "ChinPad" -Action $action -Trigger $trigger -Principal $principal -Settings $settings
```

The settings line matters on a laptop: by default a task does not start on battery, stops when the
power cord is pulled, and is stopped after 3 days. `-LogonType Interactive` runs it in your session
(on your desktop), only while you are signed in. Start it once without signing out:
`Start-ScheduledTask -TaskName "ChinPad"` (elevated). Check: Task Manager > Details, column
"Elevated" (right-click a column header > Select columns) shows Yes for ChinPad.exe.

Remove it (elevated PowerShell): `Unregister-ScheduledTask -TaskName "ChinPad" -Confirm:$false`.

### b) UIAccess: input to administrator windows without being administrator

Windows lets accessibility tools (on-screen keyboard, Narrator, speech control) send input into
administrator windows without running as administrator. A program gets this "UIAccess" right when
three things hold, checked by Windows at every start: its manifest says `uiAccess="true"`, it is
signed with a certificate the machine trusts (a self-signed one in the local machine's Trusted Root
store is enough), and it lives in a protected folder (C:\Program Files, C:\Program Files (x86) or
C:\Windows\System32).

- Gains: the chin moves and clicks in administrator windows while ChinPad keeps normal rights, so a
  bug in it cannot change system files.
- Does not reach: the UAC secure desktop, the lock and sign-in screens (unless c, for UAC).
- Costs and work (the steps marked admin need an elevated PowerShell, once each):
  1. Settings and log must not live next to the exe, because a normal program cannot write in
     C:\Program Files. Since 0.4.0 they go to %LOCALAPPDATA%\ChinPad unless a settings.json is next to
     the exe, so simply do not put one there. Do not make the Program Files folder writable for your
     account instead: that would let any program put files next to a trusted UIAccess exe.
  2. Manifest: an `app.manifest` containing `<requestedExecutionLevel level="asInvoker" uiAccess="true" />`
     and `<ApplicationManifest>app.manifest</ApplicationManifest>` in ChinPad.csproj, then publish.
  3. A self-signed code-signing certificate, trusted machine-wide:
     ```powershell
     $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=ChinPad local signing" -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(5)
     Export-Certificate -Cert $cert -FilePath "$env:USERPROFILE\chinpad-signing.cer"
     # admin:
     Import-Certificate -FilePath "$env:USERPROFILE\chinpad-signing.cer" -CertStoreLocation Cert:\LocalMachine\Root
     ```
  4. Sign the published exe (no admin; again after every build):
     `Set-AuthenticodeSignature -FilePath .\publish\ChinPad.exe -Certificate $cert -HashAlgorithm SHA256`
     (or `signtool sign /fd SHA256 /sha1 <certificate thumbprint> publish\ChinPad.exe` from the Windows SDK).
  5. Copy it to the protected folder (admin) and run it from there; Start at login then points there:
     `New-Item -ItemType Directory -Force "C:\Program Files\ChinPad"; Copy-Item .\publish\ChinPad.exe, .\publish\README.md "C:\Program Files\ChinPad\"`
  - The certificate in Trusted Root makes this machine trust whatever is signed with it. Its private key
    stays in your user certificate store, where any program running as you could use it. To limit that,
    delete it from `Cert:\CurrentUser\My` after signing (each new build then needs a new certificate,
    and old ones should be removed from Trusted Root, admin:
    `Get-ChildItem Cert:\LocalMachine\Root | Where-Object Subject -eq "CN=ChinPad local signing" | Remove-Item`).
  - When a check fails (unsigned, untrusted, wrong folder), Windows refuses to start the exe, typically
    with "A referral was returned from the server". Check that it works: the chin moves the cursor over
    Task Manager.

### c) UAC prompts on the normal desktop (`PromptOnSecureDesktop` off)

By default the UAC prompt appears on the secure desktop (the screen dims), where no program of yours
can see or touch it, so neither a nor b lets the chin answer it. Windows can show the prompt on the
normal desktop instead (currently it does not: `PromptOnSecureDesktop` is 1 here). Any one of:

- Control Panel > User Accounts > Change User Account Control settings: the third position, "Notify me
  only when apps try to make changes to my computer (do not dim my desktop)";
- Local Security Policy > Local Policies > Security Options > "User Account Control: Switch to the
  secure desktop when prompting for elevation" = Disabled;
- elevated PowerShell: `Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" -Name PromptOnSecureDesktop -Value 0`
  (back to the default: the same with `-Value 1`).

What it allows: a UIAccess ChinPad (option b) can then press the prompt's buttons; this is the case
Windows' UIAccess exception was made for. An elevated ChinPad (option a) probably cannot: the prompt
runs as SYSTEM, one level above administrator, and Windows blocks input from lower levels. Neither has
been tried. (Windows also has a narrower policy made for Remote Assistance, "User Account Control:
Allow UIAccess applications to prompt for elevation without using the secure desktop"; it has not been
tried with ChinPad either.)

What it costs, plainly: the secure desktop is what keeps other programs away from the prompt. With it
off, the prompt is an ordinary window on your desktop. Every program that can send input into it (any
UIAccess program on this machine, and anything that already runs as administrator) could click Yes on
a prompt, and any program could cover the prompt or draw a fake one. That weakens UAC for everything
on this laptop, not only for ChinPad. It is your call: some accessibility setups make this trade, and
security guidance generally advises against it.

### d) Before sign-in, and the lock screen

Nothing of ChinPad runs before you sign in. The sign-in screen and the lock screen (after Win+L, sleep
or a timeout) are on Windows' secure desktop, where only Windows handles the touchpad, and Windows
treats the chin as a palm. None of the options above changes that. Ways around the password screen,
listed as possibilities, not recommendations:

- Windows Hello face recognition (needs an infrared camera; you sign in by looking at the screen) or a
  fingerprint reader; a Hello PIN still has to be typed.
- Automatic sign-in at start-up: anyone who opens the laptop is then signed in as you, and the lock
  screen after sleep still asks unless the sign-in requirement after being away is set to Never
  (Settings > Accounts > Sign-in options). With a work account (Azure AD / Entra ID, as here) automatic
  sign-in is often unavailable or blocked by the organisation.
- The setting "Use my sign-in info to automatically finish setting up after an update" (Settings >
  Accounts > Sign-in options) is unrelated: it signs in once after an update restart to finish setting
  up, then locks the screen.

## 0.7.0: first-time setup

A fresh install (no settings file in the resolved settings folder) opens a maximized setup window.
ChinPad.exe --wizard or Settings > First-time setup opens it again. A second --wizard request
brings setup forward in the running 0.7.0 instance. Closing setup leaves ChinPad in the tray.
Setup is modeless, so --seconds N still stops the app during any step.

Setup measures chin reporting, three settled landings for each button, two comfortable horizontal
sweeps and three seconds of resting jitter. It checks each physical button, then offers three targets
and live speed / strip-height adjustments. Measurement steps advance by themselves; on waiting pages
a two-second contact continues. Lift between holds. Enter continues, Esc goes back, and Tab / Space
operate the large controls. Back from the zone preview repeats both sets of landings. Skip keeps
earlier values for an unfinished measurement. No touchpad: only Finish is offered.

Each produced setting is saved with AppSettings.Save and reloaded immediately through the regular
settings path. ChinPad's cursor and clicks are paused except in Try it; its mouse hook keeps Windows'
own touchpad moves and presses from activating other windows during measurements. The earlier pause
state is restored on exit. Start at login defaults on in the summary and is changed only on Finish.
The Administrator windows expander describes all three README options, with none selected.
Its option-a button first switches off ordinary Start at login, then asks Windows for one elevation
to register the task above for the original interactive user and this executable. It checks that the
task exists; it does not start the task, change UAC policy, install certificates or set up UIAccess.
A cancelled / failed request leaves ordinary Start at login off, and the summary lets you enable it.

WizardLogic has no UI, files, input injection or clock dependency. The host supplies complete frames
in millimetres and monotonic time, and a timer supplies timeouts even when HID is silent.
A useful chin has a continuous reporting run of at least 500 ms during the five-second sample.
The median maximum simultaneous-contact separation plus 10 mm sets mergeMm (maximum 60).
Landings use the first position at least 40 ms after touchdown, with a 150 ms minimum touch followed
by lift. All six in the upper third selects the top strip; otherwise the bottom strip is used.
The furthest extent from that edge plus 8 mm sets strip height, minimum 15 mm, maximum 60% of pad
height. On a pad shorter than 25 mm the movement-area limit takes priority. The split is the midpoint
of the two mean X positions, clamped to 30–70%; reversed means or separation below 10 mm use 50%.
Sweeps are horizontal runs with a reversal of 3 mm or a lift / 700 ms endpoint pause; runs under
5 mm are ignored. The mean span of the two longest runs gives speed = primary-display pixel width
/ (span * 1.15), clamped to 4–60 px/mm. The 95th-percentile distance from the mean resting position
gives deadzone = jitter * 1.2, clamped to 0.2–2 mm, and smoothing 0.5 above 1 mm jitter, otherwise 0.3.
A silent sensor does not count as a continuous hold, landing or resting sample.
The last setup measurements, including confidence samples and flips, are kept locally in
wizard-measurements.json and included in the diagnostic bundle's system.txt.

## 0.8.0: public release

`tools\release.ps1` publishes to a clean temporary staging folder, makes the portable zip and,
when Inno Setup 6 is available, a per-user installer. It includes the README, MIT licence and
changelog and writes SHA256SUMS.txt. See [RELEASING.md](RELEASING.md) for publishing and signing.
The tray's **About ChinPad...** shows the version, licence, settings folder and project page.
`--version`, `--enable-start-at-login` and `--disable-start-at-login` exit without starting input
handling or first-time setup. The startup commands use the same per-user Run value as the tray.
