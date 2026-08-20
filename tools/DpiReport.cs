// Prints RSPaster's real layout geometry at whatever DPI the machine is set
// to. A 100% display cannot simulate 150%, because text metrics come from the
// real DPI, so this exists to be run on the scaled display itself.
//
// Built and run by tools\Dpi-Report.cmd. Not part of the shipped app.
//
// C# 5 only (in-box csc).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RSPaster
{
    public static class DpiReport
    {
        [STAThread]
        public static void Main()
        {
            bool aware = false;
            try { aware = Native.SetProcessDPIAware(); }
            catch (EntryPointNotFoundException) { }
            Dpi.Init();

            Console.WriteLine("RSPaster DPI report");
            Console.WriteLine("===================");
            Console.WriteLine("SetProcessDPIAware returned : {0}{1}", aware,
                aware ? "" : "  (already set, or refused - see note at the end)");
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                Console.WriteLine("screen DPI                  : {0} ({1}% scaling)",
                    g.DpiX, Math.Round(g.DpiX / 96.0 * 100));
            Console.WriteLine("Dpi.Factor                    : {0}", Dpi.Factor);
            Console.WriteLine("work area                     : {0}", Screen.PrimaryScreen.WorkingArea);

            using (MainForm form = new MainForm())
            {
                using (Font f = new Font("Segoe UI", 9F))
                    Console.WriteLine("9pt Segoe UI renders at       : {0} px tall", f.Height);
                Console.WriteLine("client size                   : {0}", form.ClientSize);
                Console.WriteLine();

                Control bottom = FindBottom(form);
                if (bottom == null) { Console.WriteLine("could not locate the bottom panel"); return; }

                Console.WriteLine("control                 left  width  right");
                Console.WriteLine("---------------------  -----  -----  -----");
                List<Control> ordered = new List<Control>();
                foreach (Control c in bottom.Controls) ordered.Add(c);
                // Sort by vertical centre, not Top: labels are centred against
                // their field, so their Top differs from the field's by design.
                ordered.Sort(delegate(Control a, Control b)
                {
                    int ca = a.Top + a.Height / 2, cb = b.Top + b.Height / 2;
                    if (Math.Abs(ca - cb) > Dpi.S(12)) return ca.CompareTo(cb);
                    return a.Left.CompareTo(b.Left);
                });
                foreach (Control c in ordered)
                {
                    string name = c.Text;
                    if (string.IsNullOrEmpty(name)) name = (c is SpinBox) ? "<spin>" : "<control>";
                    Console.WriteLine("{0,-21}  {1,5}  {2,5}  {3,5}", Trim(name, 21), c.Left, c.Width, c.Right);
                }

                Console.WriteLine();
                Console.WriteLine("gaps that matter (expected {0} px between a label and its field):", Dpi.S(8));
                int problems = 0;
                for (int i = 1; i < ordered.Count; i++)
                {
                    Control prev = ordered[i - 1], cur = ordered[i];
                    int cp = prev.Top + prev.Height / 2, cc = cur.Top + cur.Height / 2;
                    if (Math.Abs(cp - cc) > Dpi.S(12)) continue;     // different row
                    if (!(prev is Label) && !(prev is ThemedCheck)) continue;
                    if (!(cur is SpinBox)) continue;
                    int gap = cur.Left - prev.Right;
                    bool ok = gap == Dpi.S(8);
                    if (!ok) problems++;
                    Console.WriteLine("  {0,-21} -> field : {1,4} px   {2}",
                        Trim(prev.Text, 21), gap, ok ? "ok" : "WRONG, expected " + Dpi.S(8));
                }
                Console.WriteLine();
                Console.WriteLine(problems == 0
                    ? "RESULT: layout is correct at this scale."
                    : "RESULT: " + problems + " gap(s) wrong at this scale.");
            }

            Console.WriteLine();
            Console.WriteLine("If SetProcessDPIAware returned False, this was run inside a host that");
            Console.WriteLine("had already created a window (PowerShell does). The report then");
            Console.WriteLine("describes an unscaled 1x layout, not what RSPaster.exe does.");
            Console.WriteLine();
            Console.Write("Press Enter to close.");
            Console.ReadLine();
        }

        static string Trim(string s, int n)
        {
            if (s == null) return "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + ".";
        }

        static Control FindBottom(Control root)
        {
            foreach (Control c in root.Controls)
            {
                foreach (Control d in c.Controls)
                    foreach (Control e in d.Controls)
                        if (e.Text == "Always on top") return d;
                Control deeper = FindBottom(c);
                if (deeper != null) return deeper;
            }
            return null;
        }
    }
}
