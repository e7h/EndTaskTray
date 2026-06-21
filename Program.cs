using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Forms;

namespace EndTaskTray
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayAppContext());
        }
    }

    internal sealed class TrayAppContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly MouseHook _hook;
        private readonly Form _hiddenForm;

        public TrayAppContext()
        {
            _hiddenForm = new Form
            {
                ShowInTaskbar = false,
                WindowState = FormWindowState.Minimized,
                FormBorderStyle = FormBorderStyle.FixedToolWindow,
                Opacity = 0
            };
            _ = _hiddenForm.Handle;

            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Shield,
                Visible = true,
                Text = "End Task On Taskbar - Middle-click any icon to end it"
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("About", null, (_, _) => ShowAbout());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => ExitApp());
            _trayIcon.ContextMenuStrip = menu;

            _trayIcon.ShowBalloonTip(4000, "End Task is running",
                "Middle-click any running app icon on the taskbar to end it instantly.",
                ToolTipIcon.Info);

            _hook = new MouseHook();
            _hook.MiddleClickOnTaskbar += point =>
            {
                _hiddenForm.BeginInvoke(new Action(() => HandleMiddleClick(point)));
            };
            _hook.Start();
        }

        private void HandleMiddleClick(Point screenPoint)
        {
            try
            {
                var wpfPoint = new System.Windows.Point(screenPoint.X, screenPoint.Y);
                AutomationElement element = AutomationElement.FromPoint(wpfPoint);
                if (element == null) return;

                AutomationElement current = element;
                int depth = 0;
                while (current != null
                       && current.Current.ControlType != ControlType.Button
                       && depth < 6)
                {
                    current = TreeWalker.ControlViewWalker.GetParent(current);
                    depth++;
                }
                if (current == null) current = element;

                string title = current.Current.Name;
                if (string.IsNullOrWhiteSpace(title)) return;

                IntPtr hwnd = NativeMethods.FindTopWindowByTitle(title);
                if (hwnd == IntPtr.Zero)
                {
                    _trayIcon.ShowBalloonTip(2500, "Window not found",
                        $"Couldn't identify the app associated with \"{title}\".", ToolTipIcon.Warning);
                    return;
                }

                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return;

                using Process proc = Process.GetProcessById((int)pid);
                string procName = proc.ProcessName;

                proc.Kill(entireProcessTree: true);
                _trayIcon.ShowBalloonTip(1500, "Ended", $"Ended \"{title}\" ({procName}.exe).", ToolTipIcon.Info);
            }
            catch (Win32Exception)
            {
                MessageBox.Show(
                    "Couldn't end the task - it requires Administrator privileges.\nTry running this tool as Administrator.",
                    "Insufficient permissions", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private static void ShowAbout()
        {
            MessageBox.Show(
				"EndTaskTray\n\n" +
				"Task Manager? Ain't nobody got time for that.\n\n" +
				"By : Slay",
				"About",
				MessageBoxButtons.OK,
				MessageBoxIcon.Information);
        }

        private void ExitApp()
        {
            _hook.Stop();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _hiddenForm.Dispose();
            Application.Exit();
        }
    }

    internal sealed class MouseHook
    {
        public event Action<Point> MiddleClickOnTaskbar;

        private const int WhMouseLl = 14;
        private const int WmMbuttondown = 0x0207;

        private IntPtr _hookId = IntPtr.Zero;
        private NativeMethods.LowLevelMouseProc _proc;

        public void Start()
        {
            _proc = HookCallback;
            _hookId = NativeMethods.SetMouseHook(_proc);
        }

        public void Stop()
        {
            if (_hookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WmMbuttondown)
            {
                var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                var screenPoint = new Point(hookStruct.pt.x, hookStruct.pt.y);

                if (NativeMethods.IsPointOverRunningAppsArea(screenPoint))
                {
                    MiddleClickOnTaskbar?.Invoke(screenPoint);
                    return (IntPtr)1;
                }
            }

            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
    }

    internal static class NativeMethods
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        public static IntPtr SetMouseHook(LowLevelMouseProc proc)
        {
            using Process curProcess = Process.GetCurrentProcess();
            using ProcessModule curModule = curProcess.MainModule;
            return SetWindowsHookEx(14, proc, GetModuleHandle(curModule.ModuleName), 0);
        }

        public static bool IsPointOverRunningAppsArea(Point pt)
        {
            foreach (string trayClassName in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
            {
                IntPtr trayHwnd = IntPtr.Zero;
                while ((trayHwnd = FindWindowEx(IntPtr.Zero, trayHwnd, trayClassName, null)) != IntPtr.Zero)
                {
                    IntPtr listHwnd = FindTaskListControl(trayHwnd);
                    if (listHwnd != IntPtr.Zero
                        && GetWindowRect(listHwnd, out RECT rect)
                        && pt.X >= rect.Left && pt.X <= rect.Right
                        && pt.Y >= rect.Top && pt.Y <= rect.Bottom)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static IntPtr FindTaskListControl(IntPtr trayHwnd)
        {
            IntPtr listHwnd = FindWindowEx(trayHwnd, IntPtr.Zero, "MSTaskListWClass", null);
            if (listHwnd != IntPtr.Zero) return listHwnd;

            IntPtr rebar = FindWindowEx(trayHwnd, IntPtr.Zero, "ReBarWindow32", null);
            if (rebar != IntPtr.Zero)
            {
                listHwnd = FindWindowEx(rebar, IntPtr.Zero, "MSTaskListWClass", null);
                if (listHwnd != IntPtr.Zero) return listHwnd;
            }

            IntPtr found = IntPtr.Zero;
            EnumChildWindows(trayHwnd, (hWnd, _) =>
            {
                var sb = new StringBuilder(256);
                GetClassName(hWnd, sb, sb.Capacity);
                if (sb.ToString() == "MSTaskListWClass")
                {
                    found = hWnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        public static IntPtr FindTopWindowByTitle(string title)
        {
            IntPtr found = IntPtr.Zero;

            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd)) return true;

                int len = GetWindowTextLength(hWnd);
                if (len == 0) return true;

                var sb = new StringBuilder(len + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                string text = sb.ToString();

                if (text.Equals(title, StringComparison.OrdinalIgnoreCase)
                    || text.Contains(title, StringComparison.OrdinalIgnoreCase)
                    || title.Contains(text, StringComparison.OrdinalIgnoreCase))
                {
                    found = hWnd;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return found;
        }

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}