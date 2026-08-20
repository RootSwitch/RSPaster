# RSPaster - Type Into Consoles That Will Not Paste

> Hypervisor consoles, VNC viewers and IPMI KVM sessions ignore your clipboard.
> RSPaster types the text instead, one real keystroke at a time, after a
> countdown long enough to put focus where you need it.

![Four RSPaster windows in four themes: a command list ready to send on Classic,
the grouped theme picker open on Nocturne, a run paused between lines on Canvas,
and a countdown running on Phosphor](docs/hero-quadrants.png)

Some consoles simply have no paste. You end up retyping a four-line command by
hand into a KVM session, at 3am, with a typo waiting at the end of it. RSPaster
is the small window you paste into instead: read the commands back, set a delay,
then let it type them for you.

No dependencies beyond a stock Windows 10 or 11 install. It uses the .NET
Framework 4.x and the C# compiler that are already on the machine, so there is
nothing to download, no runtime to install, and no network access at any point.

## What it does

| | |
|---|---|
| **Types instead of pasting** | Real `SendInput` keystrokes with per-character scancodes, which is what VM, IPMI and VNC consoles actually listen for. |
| **Waits between lines** | Optional pause after each Enter, for machines too slow or busy to accept the next command yet. |
| **Waits for you** | A countdown before it starts, and a global hotkey so you can focus the console first and never take focus away. |
| **Cancels instantly** | Esc, the hotkey, or the button. Even mid-way through a 30 second line delay. |
| **Handles awkward characters** | AltGr characters on European layouts are sent as real AltGr chords, not unicode events the consoles would drop. |
| **Hides the text** | One checkbox turns the box into dots, for a password on a shared screen. Pasting still works while hidden. |
| **Keeps nothing** | The text box never touches disk. Not settings, not a recent list, not a crash backup. |
| **Looks like the suite** | 30 palettes from the Canvas Suite design language, including the title bar and scrollbar. |

## Which file do I run?

| File | Job |
|---|---|
| **`RSPaster.exe`** | **The app. Use this one.** |
| `Run-From-Source.cmd` | Runs the same app without the exe, compiling the sources on launch. |
| `Build-RSPaster.cmd` | Not a launcher. Builds `RSPaster.exe` from the sources. |

`Run-From-Source.cmd` and `RSPaster.exe` are not two programs. They are the same
program, either compiled fresh on every launch or compiled once ahead of time.
Identical behavior, identical features. What differs is the cost:

| | `RSPaster.exe` | `Run-From-Source.cmd` |
|---|---|---|
| Window appears in | 0.7 s | 2.8 s |
| Memory | 38 MB | 95 MB |
| Shows in Task Manager as | `RSPaster` | `powershell` |
| Needs | nothing | the five `.cs` files beside it |
| SmartScreen and antivirus | can flag an unsigned exe | ships no binary |
| Display scaling above 100% | sharp | soft, see below |

Prefer the exe. The source path is there for handing the tool to someone who
will not run an unknown binary, for machines that block unsigned ones, and for
editing a `.cs` file and seeing the change without a build step.

On a display scaled above 100%, use the exe. DPI awareness has to be declared
before a process opens its first window, and the PowerShell host has already
opened one, so the script path lays out at 1x and lets Windows stretch the
result. The proportions stay right but the window is soft. To check the scaled
layout on your own display, run `tools\Dpi-Report.cmd`.

(`Run-From-Source.ps1` does the actual work; the `.cmd` is only a
double-clickable wrapper, because Windows opens a `.ps1` in an editor rather
than running it.)

## Quickstart

If the zip came with `RSPaster.exe`, run it. Nothing to install.

To build the exe yourself, run **`Build-RSPaster.cmd`** once. It finds the
in-box compiler at `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` and
writes `RSPaster.exe` beside the sources. To skip the binary entirely,
double-click **`Run-From-Source.cmd`**.

**To hand it to someone else:** run `tools\Make-Dist.ps1`. It rebuilds the exe
and writes `dist\RSPaster.zip` holding only what is needed to run or rebuild,
about 70 KB. Unzip it anywhere and run `RSPaster.exe`, or double-click
`Run-From-Source.cmd` to run without the binary at all. Nothing is
installed and nothing is written outside `%APPDATA%\RSPaster`, so uninstalling
is deleting the folder.

Then:

1. Paste your text into the box.
2. Set the **start delay** (time to focus the target) and the **key delay**
   (pause between keystrokes).
3. Either click **Type after delay** and focus the target during the countdown,
   or focus the target first and press **Ctrl+Alt+V**. The hotkey deliberately
   does not pull focus back, so the keys land where you are looking.
4. **Esc**, the hotkey again, or **Cancel** stops it mid-stream.

## Options

| Option | Default | What it does |
|---|---|---|
| Start delay | 3 s | Countdown before typing begins, so you can focus the target. |
| Key delay | 15 ms | Pause between keystrokes. Raise it to 30 - 50 ms if a slow BMC drops characters. |
| Delay between lines | off, 15 s | Waits after each Enter before typing the next line, for machines where a command needs time to finish before the next can be entered. |
| Press Enter at end | off | Appends a newline so the last line is submitted. |
| Hide text | off | Shows the text as dots. Paste still works and replaces the box; untick to edit by hand. |
| Clear after typing | off | Wipes the box once typing finishes, hidden text included. |
| Always on top | on | Keeps the window above the console. |
| Unicode mode | off | Sends every character as a Unicode event instead of scancodes. Turn on only if output comes out garbled. |
| Theme | Classic | 30 palettes from the Canvas Suite, grouped Paper / Warm / Cool / Night / Screen. |

The window lives in the tray. Closing it hides it rather than quitting, so the
hotkey keeps working; **Exit** is on the tray icon's right-click menu.

Preferences are kept in `%APPDATA%\RSPaster\settings.ini`, a plain key=value
file you can edit. **The contents of the text box are never written to disk**,
in any form: no settings entry, no recent-items list, no crash backup. It
routinely holds passwords, so it stays in memory only.

**Sending a password.** Tick **Hide text** and the box shows dots instead of
characters. Pasting still works while hidden and replaces the whole box, so the
usual flow never puts the secret on screen at all: tick Hide text, paste from
your password manager, send it. Untick to edit by hand. What gets typed is the
real text either way, and **Clear after typing** wipes the hidden copy too.

The box is read-only while hidden. Editing a mask means mapping every caret
move and selection back onto the string underneath, and getting that subtly
wrong on a password is worse than not offering it.

## How it works, and where it stops

Keystrokes are injected with the Win32 `SendInput` API. By default each
character is mapped through the *target window's* keyboard layout into a virtual
key plus scancode, which is what VM, IPMI and VNC consoles actually listen for.
Characters that need AltGr, or that are absent from the layout, fall back to a
Unicode event automatically.

**Keystrokes go wherever focus is.** The countdown exists so you can put focus
in the right place. In hypervisor and IPMI viewers, click into the console
screen area first: their surrounding toolbars do not forward keys to the guest.

**Raise the key delay over slow links.** BMC and IPMI KVM consoles drop
keystrokes sent faster than they can process. Missing characters almost always
mean the key delay is too low.

**Key delay and line delay solve different problems.** Key delay is about the
console keeping up with the typing. Line delay is about the *machine* keeping
up with the work: on a slow or busy box, a command has to finish before the
next one can be entered, and without a pause the following line is typed into a
shell that is not ready and is simply lost. Set it to comfortably more than the
slowest command in the list. The status bar counts the wait down, and Esc, the
hotkey and Cancel all stay responsive throughout, so an over-long delay costs
nothing but the time you choose to let run.

**Elevation.** Windows blocks a normal process from sending input to a window
running at higher privilege. If the target is elevated and nothing arrives, use
**Restart as Admin** at the bottom of the window. Typing that completes but
reports blocked keystrokes is this, nearly every time.

**The UAC secure desktop cannot be reached.** It is isolated from all user-mode
input injection, so no tool running in your session can type into it. RSPaster
works with UAC only where prompts are configured to appear on the normal
desktop. Running elevated covers ordinary elevated windows, not the secure
desktop.

See [DEPLOY.md](DEPLOY.md) for putting it on a machine: clearing the web mark
that makes a downloaded exe look broken, what it writes and where, running it
elevated, and what to expect on a locked-down or shared box.

## Layout

| Path | Purpose |
|---|---|
| `RSPaster.cs` | Main window, tray icon, hotkey, countdown. |
| `KeySender.cs` | Keystroke injection and every P/Invoke. |
| `Themes.cs` | The palette table, ported from the Canvas Suite. |
| `Controls.cs` | Owner-drawn themed controls and the custom scrollbar. |
| `Settings.cs` | Preference load and save. |
| `Run-From-Source.cmd` / `.ps1` | Run without the exe, compiling on launch. |
| `Build-RSPaster.cmd` | Compile the exe with the in-box compiler. |
| `favicon.svg` | The mark. Static hex, shared family ground, not themed. |
| `DEPLOY.md` | Putting it on a machine, and what Windows does to a download. |
| `docs/src/` | HTML sources for the hero and social images, plus the real captures they use. |
| `tools/charcheck.ps1` | Style check: fails on em-dashes, en-dashes, and British spellings. |
| `tools/Make-Dist.ps1` | Builds `dist/RSPaster.zip`, the run-or-rebuild file set. |
| `tools/Dpi-Report.cmd` | Prints the measured layout at the display's real scaling. |
| `tools/Render-Png.ps1` | Renders an HTML source to a PNG at an exact size. |
