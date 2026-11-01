using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Plotter.Canvas.Interop
{
    /// <summary>One display as Windows sees it, in physical pixels.</summary>
    internal sealed class DisplayMonitor
    {
        internal IntPtr Handle;
        internal int Index;
        internal string DeviceName;
        internal bool IsPrimary;
        internal NativeMethods.RECT Bounds;
        internal NativeMethods.RECT WorkArea;

        internal int Width
        {
            get { return Bounds.Right - Bounds.Left; }
        }

        internal int Height
        {
            get { return Bounds.Bottom - Bounds.Top; }
        }

        /// <summary>"Display 2 (primary), 2560 x 1440"</summary>
        internal string Label
        {
            get
            {
                return string.Format(CultureInfo.InvariantCulture, "Display {0}{1}, {2} x {3}",
                    Index + 1, IsPrimary ? " (primary)" : string.Empty, Width, Height);
            }
        }
    }

    /// <summary>
    /// Monitor enumeration and window placement through Win32, in physical pixels, so that moving
    /// a window between monitors with different DPI scaling is exact (WPF positions are DPI
    /// dependent and ambiguous across monitors).
    /// </summary>
    internal static class DisplayMonitors
    {
        internal static List<DisplayMonitor> GetAll()
        {
            var result = new List<DisplayMonitor>();
            NativeMethods.MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
            {
                var info = new NativeMethods.MONITORINFOEX();
                info.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));
                if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
                {
                    result.Add(new DisplayMonitor
                    {
                        Handle = hMonitor,
                        DeviceName = info.szDevice,
                        IsPrimary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
                        Bounds = info.rcMonitor,
                        WorkArea = info.rcWork,
                    });
                }
                return true;
            };
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            GC.KeepAlive(callback);

            // Number displays left to right, then top to bottom (what the Windows display settings show is not available here).
            result.Sort((a, b) => a.Bounds.Left != b.Bounds.Left ? a.Bounds.Left.CompareTo(b.Bounds.Left) : a.Bounds.Top.CompareTo(b.Bounds.Top));
            for (var i = 0; i < result.Count; i++)
            {
                result[i].Index = i;
            }
            return result;
        }

        /// <summary>The display that shows most of the window.</summary>
        internal static DisplayMonitor FromWindow(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }
            var handle = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            foreach (var monitor in GetAll())
            {
                if (monitor.Handle == handle)
                {
                    return monitor;
                }
            }
            return null;
        }

        /// <summary>
        /// The display for a new canvas window: a different one than the window that hosts
        /// Dynamo, preferring the largest; the Dynamo display when there is only one.
        /// </summary>
        internal static DisplayMonitor PickSecondary(Window dynamoWindow)
        {
            var all = GetAll();
            if (all.Count == 0)
            {
                return null;
            }
            var host = dynamoWindow != null ? FromWindow(dynamoWindow) : null;
            DisplayMonitor best = null;
            foreach (var monitor in all)
            {
                if (host != null && monitor.Handle == host.Handle)
                {
                    continue;
                }
                if (best == null || (long)monitor.Width * monitor.Height > (long)best.Width * best.Height)
                {
                    best = monitor;
                }
            }
            return best ?? host ?? all[0];
        }

        /// <summary>
        /// Moves the window onto the display's work area (normal state, inset by 5 percent), then
        /// maximizes it when asked. Call after the window has a handle.
        /// </summary>
        internal static void MoveTo(Window window, DisplayMonitor monitor, bool maximize)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || monitor == null)
            {
                return;
            }
            if (window.WindowState != WindowState.Normal)
            {
                window.WindowState = WindowState.Normal;
            }
            var work = monitor.WorkArea;
            var insetX = (work.Right - work.Left) / 20;
            var insetY = (work.Bottom - work.Top) / 20;
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
                work.Left + insetX, work.Top + insetY,
                work.Right - work.Left - 2 * insetX, work.Bottom - work.Top - 2 * insetY,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            if (maximize)
            {
                window.WindowState = WindowState.Maximized;
            }
        }

        /// <summary>Win32 placement of the window as "left,top,right,bottom,showCmd" (restore bounds).</summary>
        internal static string GetPlacement(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }
            var placement = new NativeMethods.WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf(typeof(NativeMethods.WINDOWPLACEMENT));
            if (!NativeMethods.GetWindowPlacement(hwnd, ref placement))
            {
                return null;
            }
            var r = placement.rcNormalPosition;
            return string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}", r.Left, r.Top, r.Right, r.Bottom, placement.showCmd);
        }

        /// <summary>
        /// Restores a placement saved by <see cref="GetPlacement"/> when its rectangle is still on a
        /// connected display. Call from SourceInitialized (handle exists, window not shown yet).
        /// </summary>
        internal static bool TryRestorePlacement(Window window, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            var parts = text.Split(',');
            if (parts.Length != 5)
            {
                return false;
            }
            var values = new int[5];
            for (var i = 0; i < 5; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                {
                    return false;
                }
            }
            var rect = new NativeMethods.RECT { Left = values[0], Top = values[1], Right = values[2], Bottom = values[3] };
            if (rect.Right - rect.Left < 100 || rect.Bottom - rect.Top < 100)
            {
                return false;
            }
            if (NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONULL) == IntPtr.Zero)
            {
                return false; // that display is gone
            }

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }
            var placement = new NativeMethods.WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf(typeof(NativeMethods.WINDOWPLACEMENT));
            placement.flags = 0;
            placement.showCmd = values[4] == NativeMethods.SW_SHOWMAXIMIZED ? NativeMethods.SW_SHOWMAXIMIZED : NativeMethods.SW_SHOWNORMAL;
            placement.ptMinPosition = new NativeMethods.POINT { X = -1, Y = -1 };
            placement.ptMaxPosition = new NativeMethods.POINT { X = -1, Y = -1 };
            placement.rcNormalPosition = rect;
            return NativeMethods.SetWindowPlacement(hwnd, ref placement);
        }
    }

    internal static class NativeMethods
    {
        internal const int MONITORINFOF_PRIMARY = 0x00000001;
        internal const int MONITOR_DEFAULTTONULL = 0x00000000;
        internal const int MONITOR_DEFAULTTONEAREST = 0x00000002;
        internal const int SW_SHOWNORMAL = 1;
        internal const int SW_SHOWMAXIMIZED = 3;
        internal const uint SWP_NOZORDER = 0x0004;
        internal const uint SWP_NOACTIVATE = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT rcNormalPosition;
        }

        internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("user32.dll")]
        internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

        [DllImport("user32.dll")]
        internal static extern IntPtr MonitorFromRect(ref RECT lprc, int dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);
    }
}
