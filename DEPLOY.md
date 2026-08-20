# Deploying RSPaster

There is no installer and no service. Deployment is copying a folder onto the
machine you work from and running an exe. What follows is the part that is not
obvious: the things Windows does to a downloaded binary, what to expect on a
shared jump box, and the limits that no amount of configuration will move.

## Requirements

- Windows 10 (1809 or later) or Windows 11.
- Nothing else. RSPaster uses the .NET Framework 4.x that ships with Windows,
  and builds with the C# compiler that ships with it. There is no runtime to
  install, no NuGet restore, and no network access at any point.

The two undocumented dark-mode calls it makes are looked up by ordinal and the
failure is swallowed, so on a build that lacks them you get light scrollbars
rather than a crash.

## Getting it onto a machine

**From the release zip.** Unzip anywhere the user can write. `%LOCALAPPDATA%`
or a tools folder both work; it does not need Program Files, and putting it
there only means it needs elevation to update itself.

**From source.** Copy the repo folder and run `Build-RSPaster.cmd` once. This
matters on a locked-down machine: nothing was downloaded, so nothing carries a
web mark, and the person running it can read every line first.

## The thing that will bite: mark of the web

A zip downloaded through a browser is tagged with a zone identifier, and that
tag survives extraction onto every file inside it. The symptom is not always an
obvious block - sometimes SmartScreen shows "Windows protected your PC", and
sometimes the exe simply does nothing on double-click.

Clear it on the zip **before** extracting, so the tag is not copied onto each
extracted file:

```powershell
Unblock-File .\RSPaster.zip
```

If it is already extracted, clear the folder:

```powershell
Get-ChildItem .\RSPaster -Recurse | Unblock-File
```

The binary is unsigned. Code signing needs a certificate this project does not
have, so SmartScreen will warn on a fresh download until enough people run it.
If that is unacceptable in your environment, use the source path instead:
double-click `Run-From-Source.cmd` and no binary is involved at all.

## Where it keeps things

| Path | What | Notes |
|---|---|---|
| `%APPDATA%\RSPaster\settings.ini` | Delays, checkbox states, theme, window size | Plain key=value, safe to edit or delete |
| nowhere else | | |

The contents of the text box are **never** written to disk, in any form: no
settings entry, no recent-items list, no crash backup. It routinely holds
passwords, so it stays in memory only. That is worth knowing before you approve
the tool for a team.

Uninstalling is deleting the folder and, if you care, that one settings file.
Nothing is written to the registry, no service is registered, and nothing is
added to startup.

On a machine with roaming profiles, `%APPDATA%` roams, so settings follow the
user between machines. Window size is stored in physical pixels, so a size
saved at 100% scaling is rejected as too small at 150% and the defaults apply.
That is deliberate, and it means the only symptom of moving between differently
scaled machines is a window that reverts to its default size.

## Running it elevated

Windows blocks a normal-privilege process from sending input to a window that
belongs to a higher-privilege one. If your target is an elevated app, RSPaster
must be elevated too, or the keystrokes are silently discarded. The status bar
says so when it happens: typing completes but reports blocked keystrokes.

Use the **Restart as Admin** link at the bottom of the window rather than
launching elevated by habit. An elevated RSPaster can only send to elevated
targets and normal ones equally, but it also means a global hotkey registered by
an elevated process, which some environments audit.

## Shared and locked-down machines

- **Per-user, not per-machine.** Two users on the same box each get their own
  settings and their own hotkey registration. There is nothing machine-wide to
  configure.
- **The hotkey is first-come.** `Ctrl+Alt+V` is registered at startup. If
  another app already owns it, RSPaster says so in the status bar and the top
  bar, and the button keeps working. It does not fight for the key.
- **AppLocker and similar.** The exe is unsigned and lives in a user-writable
  directory, which is exactly what a default AppLocker policy blocks. The
  source path runs through `powershell.exe` with `-ExecutionPolicy Bypass`,
  which many managed environments also block. If both are blocked, that is the
  policy working as intended; get the exe allowlisted by hash rather than
  working around it.

## Limits that configuration will not move

**The UAC secure desktop cannot be reached.** It is isolated from all user-mode
input injection, so nothing running in your session can type into it. RSPaster
works with UAC prompts only where they are configured to appear on the normal
desktop. Running elevated covers ordinary elevated windows, not the secure
desktop.

**Keystrokes go wherever focus is.** There is no targeting. The countdown is the
mechanism for putting focus in the right place, and the global hotkey exists so
you can focus the target first and never take it away.

**Consoles drop keys that arrive too fast.** BMC and IPMI KVM sessions are the
usual offenders. Missing characters almost always mean the key delay is too low;
30 to 50 ms is a reasonable starting point over a slow link.

## Checking a scaled display

On a display scaled above 100%, use `RSPaster.exe`. `Run-From-Source.cmd`
cannot declare DPI awareness, because that has to happen before a process opens
its first window and the PowerShell host has already opened one; that path lays
out at 1x and lets Windows stretch the result, which is correct in proportion
but soft.

To confirm the layout on your own display rather than trusting a screenshot:

```
tools\Dpi-Report.cmd
```

It compiles a probe against the app's own sources, measures the real geometry at
whatever scaling is in force, and prints a pass or fail per control gap. It also
reports whether DPI awareness was actually achieved, so a run that only
describes an unscaled layout says so instead of looking like a pass.
