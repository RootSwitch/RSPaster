# Changelog

## Unreleased

**A 1px line along the top and left of every owner-drawn control.** Each paint
method set `SmoothingMode.AntiAlias` before filling its background, and GDI+
anti-aliases the edges of a filled rectangle: the first row and column came out
at roughly half coverage, letting whatever was underneath show through. The
background fill is now done with smoothing off, and anti-aliasing is switched
on afterwards for the rounded shapes that actually need it.

Measured rather than guessed: filling the checkbox background with pure lime
produced `#008000` on the top row and `#004000` in the corner - exactly 50% and
25% coverage - which is what named anti-aliasing as the cause rather than a
stale repaint.

It also explains why the artifact behaved so strangely. It only appeared on the
first paint of a control, because blending the correct color 50% over itself
gives the correct color, so any later repaint erased it permanently: hovering
made it vanish and nothing would bring it back. And it was far more obvious on
the Paper and Warm palettes, where the half-covered row contrasts hardest
against a light panel.

Affected `InputHost`, `ThemedButton`, `ThemedCheck`, `SpinBox` and
`ThemedScrollBar`.


**Tooltips left stray dark edges behind and flickered at random.** The ToolTip
was a local variable. It is a Component, not a child control, so nothing else
held a reference: it became garbage as soon as the method returned, and
whenever the finalizer happened to run it destroyed the native tooltip window
out from under a tip that might be on screen. That is where the randomness came
from. It is now a field, disposed with the form.

The tooltip was also drawn by the system as a pale box, which on a dark palette
is both jarring and the source of the leftover edges: it is a separate top
level window that covers whatever is beneath it, including neighbouring
controls, so any imperfect repaint on dismissal shows as a thin light line
under a control that has no tooltip of its own. It is now owner-drawn in theme
colors.

**Added `tools/Dpi-Report.cmd`.** A 100% display cannot be used to check a 150%
layout, because text metrics come from the real DPI. This compiles a probe
against the app's own sources, runs it on the scaled display, and prints the
measured geometry with a pass or fail on each label-to-field gap. It reports
whether `SetProcessDPIAware` actually succeeded, so a run that only describes
an unscaled layout says so instead of looking like a pass.

**Note: `Run-From-Source.cmd` cannot be DPI-aware.** `SetProcessDPIAware` has to
be called before the process creates its first window, and the PowerShell host
has already done that. The script path therefore lays out at 1x and lets
Windows stretch the result: correct proportions, but soft at 125% and above.
The exe is the one to use on a scaled display, and the README now says so.


**Field labels overlapped their spin boxes at 150% scaling.** An AutoSize label
reports the stock 100x23 default until it is parented, and the layout runs
before that, so "Start delay (s)" was placed against a width of 100 when it
actually needed 79. The 21px of slack that leaves is invisible at 100% and
turns into an overlap at 150%, because 100 is a fixed pixel value that does not
scale while the text does. Labels are now measured and sized explicitly, the
same way ThemedCheck already sized itself. The same stale default was also
making the labels sit a few pixels high, since Height read 23 instead of 15.

Worth recording how this got through: it was checked at 1x through 2x before
release and passed, because that check scaled `Dpi.Factor` without scaling the
font. A 100% display cannot simulate 150% faithfully - text metrics come from
the real DPI, so scaling the layout alone grows the gaps but not the words, and
scaling a probe font instead grows the words but not the layout. The check now
asserts something scale-independent: each control sits at the previous one's
`Right` plus a fixed gap, so that distance must come out exactly right at every
factor. With the defect put back it reports 29px where 8 was expected, and the
error stays a constant 21px at every scale, which is the signature of a
constant that is not scaling.

**The "seconds" label was never given theme colors**, so it inherited full
brightness text while the two field labels beside it render dimmed.


**Non-ASCII characters were typed as a literal `?`.** `VkKeyScanEx` was declared
without `CharSet`, and `DllImport` defaults to ANSI, so the CLR squeezed each
`char` through the system code page on the way to `VkKeyScanExA`. Anything the
page could not represent arrived as `?`, which resolved to a real key, so the
engine typed a question mark instead of falling through to the unicode path.
Measured on a Windows-1252 machine, the old declaration mapped Greek, CJK,
arrows and checkmarks to the `?` key; the new one reports them unmapped and the
unicode fallback takes over.

**AltGr characters were silently dropped by the consoles this tool exists for.**
Anything needing more than plain Shift fell back to a unicode event, and on
European layouts that is `@ { } [ ] \ | ~` and the euro sign - the characters a
shell command is made of. Hypervisor and IPMI KVM consoles emulate a hardware
keyboard and ignore `KEYEVENTF_UNICODE`, so those characters reached the guest
as nothing at all. Ctrl+Alt pairs are now sent as a real AltGr chord: left
Control plus the extended right Alt, exactly the scancode sequence a physical
AltGr press emits. Verified end to end against a German layout.

**The hotkey could fire while its own modifiers were still held.** With a start
delay of 0, `Ctrl+Alt+V` began injecting before the user's fingers were off the
keys, so the first characters arrived at the target as Ctrl+Alt chords: menu
shortcuts, interrupted commands. Typing now waits for Shift, Ctrl, Alt, Win and
V to be physically up, with a three second cap so a stuck key cannot hang a run.

**Ctrl+V could edit the text while it was being typed.** The paste handler
assigns `SelectedText`, which ignores `ReadOnly`, so the box that is deliberately
locked during a run accepted pastes anyway.

**Choosing a theme reverted every unsaved setting.** The theme menu called
`_settings.Save()`, writing the object as it was at startup, so any delay or
checkbox changed since launch was rolled back. It now goes through
`SaveSettings()`, which reads the controls first.

**Hiding to the tray recreated the window handle.** `ShowInTaskbar` was toggled
on hide and restore, and changing it forces WinForms to destroy and rebuild the
`HWND`, which drops and re-registers the global hotkey and reapplies the dark
title bar for nothing. `Hide()` already clears the taskbar button. Confirmed the
handle now survives a hide.

**Blurry on scaled displays.** The process declared no DPI awareness, so Windows
bitmap-stretched the whole window at 125% and above. It now calls
`SetProcessDPIAware` before the first window and scales every hand-placed
dimension through one factor - awareness without the scaling would have traded
blurry for tiny.

**Controls in the bottom panel were positioned at fixed pixel offsets** and
would collide if a label were reworded or the font metrics differed. Each is now
placed relative to the one before it. Checked for collisions and overflow at
1x through 2x.

**The disabled seconds field showed a system-gray hole on dark themes.** A
disabled WinForms `TextBox` ignores `BackColor`. The child now stays enabled but
read-only, with the border, arrows and text painted dimmed, matching the
half-opacity treatment the suite gives disabled buttons.

**Status text was truncated when running elevated**, because the layout reserved
room for the "Restart as Admin" link even when it was hidden. Note for anyone
making the same change: the fix cannot test `_lnkAdmin.Visible`, because
`Control.Visible` reports *effective* visibility and answers false for every
control while the form is still being built - which lands the status label on
top of the link instead. The intended state is kept in a field.


**Renamed `RSPaster.cmd` to `Run-From-Source.cmd`, and the `.ps1` with it.**
`RSPaster.cmd` sitting beside `RSPaster.exe` read as two variants of the same
thing, when one is the app and the other compiles the app from source on every
launch; `Build-RSPaster.cmd` then looked like a sibling of `RSPaster.cmd` while
doing something else entirely. The three files now say what they do:
`RSPaster.exe` runs it, `Run-From-Source.cmd` runs it without the exe,
`Build-RSPaster.cmd` builds the exe.

The README opens with a "Which file do I run?" table and the measured cost of
each path, since the difference is not obvious from the outside: the exe shows
its window in 0.7 s against 2.8 s and holds 38 MB against 95 MB, because the
source path pays for a compile and a PowerShell host on every launch. It also
appears in Task Manager as `powershell` rather than `RSPaster`. The source path
is kept because it ships no binary, which is what a recipient wary of an
unsigned exe, or a machine that blocks one, actually needs.

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
