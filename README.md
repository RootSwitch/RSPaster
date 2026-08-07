# RSPaster

Types multi-line text into whatever window has focus, as if from a physical
keyboard, for the consoles that will not take a clipboard paste. Built for
hypervisor VM consoles, VNC viewers, IPMI/BMC KVM sessions and UAC prompts,
where reviewing a long command before sending it is most of the work.

![RSPaster](docs/screenshot.png)

No dependencies beyond a stock Windows 10 or 11 install. It uses the .NET
Framework 4.x and the C# compiler that are already on the machine, so there is
nothing to download and no runtime to install.

## Quickstart

**Run it from source, no build:** double-click **`RSPaster.cmd`**. It compiles
the sources in memory through PowerShell and shows the window.

**Or build a standalone exe:** run **`Build-RSPaster.cmd`** once. It finds the
in-box compiler at `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` and
writes `RSPaster.exe`, which you can copy anywhere and run on its own.

Both paths run identical code from the same `.cs` files.

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
| Press Enter at end | off | Appends a newline so the last line is submitted. |
| Clear after typing | off | Wipes the box once typing finishes. |
| Always on top | on | Keeps the window above the console. |
| Unicode mode | off | Sends every character as a Unicode event instead of scancodes. Turn on only if output comes out garbled. |
| Theme | Classic | 30 palettes from the Canvas Suite, grouped Paper / Warm / Cool / Night / Screen. |

The window lives in the tray. Closing it hides it rather than quitting, so the
hotkey keeps working; **Exit** is on the tray icon's right-click menu.

Preferences are kept in `%APPDATA%\RSPaster\settings.ini`, a plain key=value
file you can edit. **The contents of the text box are never written to disk**,
in any form: no settings entry, no recent-items list, no crash backup. It
routinely holds passwords, so it stays in memory only.

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

**Elevation.** Windows blocks a normal process from sending input to a window
running at higher privilege. If the target is elevated and nothing arrives, use
**Restart as Admin** at the bottom of the window. Typing that completes but
reports blocked keystrokes is this, nearly every time.

**The UAC secure desktop cannot be reached.** It is isolated from all user-mode
input injection, so no tool running in your session can type into it. RSPaster
works with UAC only where prompts are configured to appear on the normal
desktop. Running elevated covers ordinary elevated windows, not the secure
desktop.

## Layout

| Path | Purpose |
|---|---|
| `RSPaster.cs` | Main window, tray icon, hotkey, countdown. |
| `KeySender.cs` | Keystroke injection and every P/Invoke. |
| `Themes.cs` | The palette table, ported from the Canvas Suite. |
| `Controls.cs` | Owner-drawn themed controls and the custom scrollbar. |
| `Settings.cs` | Preference load and save. |
| `RSPaster.ps1` / `.cmd` | Run from source. |
| `Build-RSPaster.cmd` | Compile the exe with the in-box compiler. |
| `tools/charcheck.ps1` | Style check: fails on em-dashes and en-dashes. |
