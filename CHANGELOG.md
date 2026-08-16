# Changelog

## Unreleased

**Added `tools/Make-Dist.ps1`, which packages `dist/RSPaster.zip`.** Handing
the tool to someone meant copying the whole working folder, `.git` and all. The
zip carries the exe, the five sources, both launchers, the build script, README
and LICENSE: enough to run it, and enough to audit or rebuild it. Around 70 KB.

It rebuilds the exe before packaging rather than zipping whatever binary is
lying around, because a distribution whose exe does not match the sources
beside it is worse than one with no exe. A missing file aborts the run and names
itself, and the abort happens before the previous zip is touched, so a failed
run cannot leave a half-built artifact where a good one was.

**Added a delay between lines.** Multi-line command lists sent to a slow or
busy machine lost every line after the first: the next line was typed into a
shell still working on the previous command, so it went nowhere. A checkbox and
a seconds field now hold after each Enter before the next line is typed. The
wait is only taken between lines, never after the last one, where it would just
postpone the finish.

The wait is slept in 100 ms slices rather than one long `Thread.Sleep`, because
a 15 second sleep leaves Esc, Cancel and the hotkey looking dead for the whole
of it. Cancelling a 30 second wait returns immediately. The status bar and the
tray tooltip count the remaining seconds down and name the line about to be
typed, since the window is often hidden while this runs.

**First working version.** A GUI that types multi-line text into the focused
window through `SendInput`, for consoles that ignore the clipboard: hypervisor
VM consoles, VNC, IPMI/BMC KVM, and UAC prompts on the normal desktop. Runs
either from source through PowerShell or as an exe built by the C# compiler
already present in Windows, so there is nothing to install on a jump box.

**LF-only text pasted as a single unreadable line.** A Windows EDIT control
breaks lines on CRLF only, and text copied from a shell, an SSH session or a web
page is usually LF-only. The whole script arrived as one line, which defeats the
point of reviewing a command before sending it. `WM_PASTE` is now intercepted
and the clipboard text normalized to CRLF before insertion; the keystroke engine
normalizes back to `\n`, so only the display changed, never what is sent.

**Scrollbars and title bar stayed system-colored on every dark theme.** Windows
will not recolor an EDIT control's non-client scrollbars: `SetWindowTheme` with
`DarkMode_Explorer` is ignored for them even after the process opts into dark
mode via the undocumented uxtheme ordinals, so a bright white strip sat down the
side of the text box on 20-odd palettes. The stock bars are now switched off and
a themed one is painted in their place, which also lets it match palettes an
OS scrollbar could never reach, like Phosphor and Amber. Word wrap is on, which
removes the need for a horizontal bar at all. The title bar is handled
separately through `DwmSetWindowAttribute`, which does work.

**Checkbox labels rendered clipped.** They were measured before being added to
their parent, so the measurement used `Control.DefaultFont` while the paint used
the wider form font. The font is now set before measuring.

**Spin boxes and the text box opened system-colored.** Both apply their palette
on a theme-changed event, which does not fire for the theme already in force at
startup. They now apply it once at construction too.

### Notes for later

- `NumericUpDown` and `CheckBox` are replaced by owner-drawn equivalents.
  WinForms renders their spin buttons and check glyph in system colors
  regardless of what is set on the control, which reads wrong on most palettes.
- The tray icon is drawn at runtime from `--se-logo-a` / `--se-logo-b` rather
  than shipped as an `.ico`, so it follows the theme and the repo carries no
  binary asset. `Bitmap.GetHicon` handles are destroyed explicitly on each
  switch, or the process bleeds a GDI handle per theme change.
- Palettes are ported from `launchcanvas/public/themes.js` in the same
  overrides-on-defaults shape, so the two files can be diffed when the suite
  palette moves.
