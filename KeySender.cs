// Keystroke injection. Everything that talks to user32 lives here.
//
// C# 5 only (in-box csc).

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace RSPaster
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        // All three members overlay at offset 0; INPUT is sequential so the
        // union lands at the right offset on both x86 and x64.
        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_UNICODE = 0x0004;

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        // CharSet.Unicode is load-bearing: DllImport defaults to ANSI, which
        // marshals this char through the system code page. A character outside
        // it (Greek, box drawing, checkmarks) became '?', which VkKeyScanExA
        // happily resolved to the question-mark key - so instead of the unicode
        // fallback the target received a literal '?'.
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern short VkKeyScanEx(char ch, IntPtr dwhkl);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

        [DllImport("user32.dll")]
        public static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Icon handles from Bitmap.GetHicon() are not owned by the Icon wrapper
        // and leak a GDI handle per theme switch unless destroyed explicitly.
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        // The two bits of chrome WinForms cannot recolor: a TextBox's scrollbars
        // and the window title bar. Both are in-box on Win 10 1809+ / Win 11.
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // Scrollbars are the one piece that needs undocumented uxtheme ordinals:
        // SetWindowTheme("DarkMode_Explorer") alone is ignored until the process
        // has opted into dark mode. These are exported by ordinal only and are
        // absent before Win10 1809, so every call site swallows the lookup
        // failure and settles for light scrollbars.
        [DllImport("uxtheme.dll", EntryPoint = "#135")]
        public static extern int SetPreferredAppMode(int mode);

        [DllImport("uxtheme.dll", EntryPoint = "#133")]
        public static extern bool AllowDarkModeForWindow(IntPtr hwnd, bool allow);

        public const int WM_THEMECHANGED = 0x031A;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public const uint RDW_INVALIDATE = 0x0001;
        public const uint RDW_FRAME = 0x0400;
        public const uint RDW_UPDATENOW = 0x0100;

        [DllImport("user32.dll")]
        public static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprc, IntPtr hrgn, uint flags);
    }

    public class TypeResult
    {
        public int KeysSent;
        public int Blocked;
        public bool Cancelled;
    }

    public class TypeOptions
    {
        public int PerKeyDelayMs = 15;
        // Pause after each Enter before the next line is typed, for consoles
        // where a command needs time to finish before the next can be entered.
        public int LineDelayMs;
        public bool UnicodeMode;

        public Func<bool> Cancelled;
        public Action<int, int> Progress;          // characters done, total
        public Action<int, int, int> LineWait;     // ms remaining, next line, total lines
    }

    public static class KeySender
    {
        const ushort VK_SHIFT = 0x10;
        const ushort VK_CONTROL = 0x11;
        const ushort VK_MENU = 0x12;
        const ushort VK_RETURN = 0x0D;
        const ushort VK_TAB = 0x09;
        const ushort SCAN_LSHIFT = 0x2A;
        const ushort SCAN_LCONTROL = 0x1D;
        const ushort SCAN_RALT = 0x38;   // with the extended bit: right Alt / AltGr

        // Types 'text' into the focused window. Newlines become Enter, tabs
        // become Tab. In scancode mode each character is mapped through the
        // target window's keyboard layout to a virtual key + scancode, which is
        // what VM/IPMI/VNC consoles listen for; characters needing AltGr or
        // absent from the layout fall back to a KEYEVENTF_UNICODE event. In
        // unicode mode every character is sent as a unicode event.
        public static TypeResult TypeText(string text, TypeOptions o)
        {
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            IntPtr hkl = ForegroundLayout();
            TypeResult result = new TypeResult();
            int total = text.Length;

            Func<bool> cancelled = o.Cancelled;

            // With a start delay of 0 and the hotkey, the user's fingers are
            // still on Ctrl+Alt+V when typing begins, so the first injected
            // keys arrive at the target as Ctrl+Alt chords - shortcuts, killed
            // commands. Hold until the physical keys are up before sending.
            if (!WaitForKeysUp(cancelled))
            {
                result.Cancelled = true;
                return result;
            }
            Action<int, int> progress = o.Progress;
            bool unicodeMode = o.UnicodeMode;
            int perKeyDelayMs = o.PerKeyDelayMs;

            // A trailing newline is the "press Enter at end" one and does not
            // start another line, so it must not inflate the count.
            int totalLines = 1;
            for (int i = 0; i < total; i++)
                if (text[i] == '\n' && i < total - 1) totalLines++;
            int lineNo = 1;

            for (int i = 0; i < total; i++)
            {
                if (cancelled != null && cancelled())
                {
                    result.Cancelled = true;
                    break;
                }

                char c = text[i];
                bool ok;
                if (c == '\n')
                {
                    ok = SendVk(VK_RETURN, false, hkl);
                }
                else if (c == '\t')
                {
                    ok = SendVk(VK_TAB, false, hkl);
                }
                else if (unicodeMode)
                {
                    ok = SendUnicode(c);
                }
                else
                {
                    short vks = Native.VkKeyScanEx(c, hkl);
                    if (vks == -1)
                    {
                        ok = SendUnicode(c);
                    }
                    else
                    {
                        int shiftState = (vks >> 8) & 0xFF;
                        // Bit 0 = Shift, bit 1 = Ctrl, bit 2 = Alt.
                        if ((shiftState & ~1) == 0)
                        {
                            ok = SendVk((ushort)(vks & 0xFF), shiftState == 1, hkl);
                        }
                        else if ((shiftState & 6) == 6)
                        {
                            // Ctrl+Alt together is AltGr: on European layouts
                            // that is @ { } [ ] \ | ~ and more. Sent as a real
                            // AltGr scancode chord, because the KVM consoles
                            // this tool exists for ignore unicode events.
                            ok = SendAltGr((ushort)(vks & 0xFF), (shiftState & 1) != 0, hkl);
                        }
                        else
                        {
                            ok = SendUnicode(c);
                        }
                    }
                }

                if (ok) result.KeysSent++; else result.Blocked++;

                if (progress != null && (i % 20 == 0 || i == total - 1))
                    progress(i + 1, total);
                if (perKeyDelayMs > 0)
                    Thread.Sleep(perKeyDelayMs);

                // Hold after the Enter that ends a line, but not after the last
                // one: nothing follows it, so waiting only delays the finish.
                if (c == '\n' && o.LineDelayMs > 0 && i < total - 1)
                {
                    lineNo++;
                    if (!Wait(o.LineDelayMs, cancelled, o.LineWait, lineNo, totalLines))
                    {
                        result.Cancelled = true;
                        break;
                    }
                }
            }
            return result;
        }

        // Sleeps in short slices so Cancel stays responsive: a line delay is
        // measured in seconds, and one long Thread.Sleep would leave Esc and
        // the hotkey looking dead for the whole of it. Returns false if the run
        // was cancelled while waiting.
        static bool Wait(int totalMs, Func<bool> cancelled,
                         Action<int, int, int> tick, int lineNo, int totalLines)
        {
            const int SLICE = 100;
            int waited = 0;
            while (waited < totalMs)
            {
                if (cancelled != null && cancelled()) return false;
                if (tick != null && waited % 1000 == 0)
                    tick(totalMs - waited, lineNo, totalLines);
                int slice = Math.Min(SLICE, totalMs - waited);
                Thread.Sleep(slice);
                waited += slice;
            }
            return true;
        }

        static bool SendVk(ushort vk, bool shift, IntPtr hkl)
        {
            ushort scan = (ushort)Native.MapVirtualKeyEx(vk, 0 /* MAPVK_VK_TO_VSC */, hkl);
            List<Native.INPUT> events = new List<Native.INPUT>(4);
            if (shift) events.Add(KeyEvent(VK_SHIFT, SCAN_LSHIFT, false));
            events.Add(KeyEvent(vk, scan, false));
            events.Add(KeyEvent(vk, scan, true));
            if (shift) events.Add(KeyEvent(VK_SHIFT, SCAN_LSHIFT, true));
            return Send(events.ToArray());
        }

        // A physical AltGr press emits LControl (0x1D) followed by the extended
        // right Alt (E0 0x38); this mirrors that exactly, so a console that
        // forwards raw scancodes hands the guest the same chord a real keyboard
        // would have produced.
        static bool SendAltGr(ushort vk, bool shift, IntPtr hkl)
        {
            ushort scan = (ushort)Native.MapVirtualKeyEx(vk, 0 /* MAPVK_VK_TO_VSC */, hkl);
            List<Native.INPUT> events = new List<Native.INPUT>(8);
            events.Add(KeyEvent(VK_CONTROL, SCAN_LCONTROL, false));
            events.Add(KeyEvent(VK_MENU, SCAN_RALT, false, true));
            if (shift) events.Add(KeyEvent(VK_SHIFT, SCAN_LSHIFT, false));
            events.Add(KeyEvent(vk, scan, false));
            events.Add(KeyEvent(vk, scan, true));
            if (shift) events.Add(KeyEvent(VK_SHIFT, SCAN_LSHIFT, true));
            events.Add(KeyEvent(VK_MENU, SCAN_RALT, true, true));
            events.Add(KeyEvent(VK_CONTROL, SCAN_LCONTROL, true));
            return Send(events.ToArray());
        }

        // Blocks until Shift, Ctrl, Alt, Win and V are all physically up, so a
        // hotkey trigger with no start delay cannot mix the user's own held
        // modifiers into the injected stream. The timeout covers keys that
        // report stuck (some KVM passthroughs): typing anyway beats hanging.
        static bool WaitForKeysUp(Func<bool> cancelled)
        {
            int[] keys = { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0x56 };
            const int TIMEOUT_MS = 3000;
            const int SLICE = 30;
            int waited = 0;
            while (waited < TIMEOUT_MS)
            {
                if (cancelled != null && cancelled()) return false;
                bool held = false;
                for (int i = 0; i < keys.Length; i++)
                {
                    if ((Native.GetAsyncKeyState(keys[i]) & 0x8000) != 0) { held = true; break; }
                }
                if (!held) return true;
                Thread.Sleep(SLICE);
                waited += SLICE;
            }
            return true;
        }

        static bool SendUnicode(char c)
        {
            Native.INPUT[] events = new Native.INPUT[2];
            events[0] = UnicodeEvent(c, false);
            events[1] = UnicodeEvent(c, true);
            return Send(events);
        }

        static Native.INPUT KeyEvent(ushort vk, ushort scan, bool up)
        {
            return KeyEvent(vk, scan, up, false);
        }

        static Native.INPUT KeyEvent(ushort vk, ushort scan, bool up, bool extended)
        {
            Native.INPUT input = new Native.INPUT();
            input.type = Native.INPUT_KEYBOARD;
            input.U.ki.wVk = vk;
            input.U.ki.wScan = scan;
            input.U.ki.dwFlags = (up ? Native.KEYEVENTF_KEYUP : 0)
                               | (extended ? Native.KEYEVENTF_EXTENDEDKEY : 0);
            return input;
        }

        static Native.INPUT UnicodeEvent(char c, bool up)
        {
            Native.INPUT input = new Native.INPUT();
            input.type = Native.INPUT_KEYBOARD;
            input.U.ki.wVk = 0;
            input.U.ki.wScan = c;
            input.U.ki.dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0);
            return input;
        }

        static bool Send(Native.INPUT[] events)
        {
            uint sent = Native.SendInput((uint)events.Length, events,
                                         Marshal.SizeOf(typeof(Native.INPUT)));
            return sent == (uint)events.Length;
        }

        static IntPtr ForegroundLayout()
        {
            uint pid;
            uint tid = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
            return Native.GetKeyboardLayout(tid);
        }
    }
}
