using Avalonia.Controls;
using Avalonia.Platform;
using System;

namespace Skua.App.Avalonia.Controls;

#if IS_WINDOWS
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

/// <summary>
/// Hosts the game player's window. The player (another process) is started as a child of <see cref="WindowHandle"/>,
/// a holder created once and moved between native parents, so the game keeps running when Avalonia recreates the
/// native control (e.g. when the window moves between tabs).
/// </summary>
public class GameHost : NativeControlHost
{
    private readonly Holder _holder = new() { Dock = WinForms.DockStyle.Fill, BackColor = System.Drawing.Color.FromArgb(20, 20, 20) };
    private WinForms.Panel? _panel;

    public nint WindowHandle => _holder.Handle;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _panel = new WinForms.Panel { Dock = WinForms.DockStyle.Fill };
        _panel.Controls.Add(_holder);
        return new PlatformHandle(_panel.Handle, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (_panel is not null)
        {
            _panel.Controls.Clear();
            _panel.Dispose();
            _panel = null;
        }
    }

    /// <summary>Keeps the player's window filling the holder, from when it is created on.</summary>
    private sealed class Holder : WinForms.Control
    {
        private const int WM_PARENTNOTIFY = 0x0210;
        private const int WM_CREATE = 0x0001;
        private const uint GW_CHILD = 5;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")]
        private static extern nint GetWindow(nint hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        protected override void WndProc(ref WinForms.Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PARENTNOTIFY && (m.WParam.ToInt64() & 0xFFFF) == WM_CREATE)
                FitPlayer();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            FitPlayer();
        }

        private void FitPlayer()
        {
            nint player = GetWindow(Handle, GW_CHILD);
            if (player != 0)
                SetWindowPos(player, 0, 0, 0, Width, Height, SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }
}
#else
using global::Avalonia.Threading;
using System.Runtime.InteropServices;

/// <summary>
/// Hosts the game player's window on X11 (Linux). The player (another process) is started as a child of
/// <see cref="WindowHandle"/>, an X11 window created once on Bonxie's own display connection. Avalonia places that
/// window in the game area and, when it recreates the native control, parks it instead of destroying it, so the game
/// keeps running. Elsewhere (e.g. macOS) the handle is 0 and the player opens a window of its own.
/// </summary>
public class GameHost : NativeControlHost
{
    private static nint _display;
    private nint _holder;
    private DispatcherTimer? _fitTimer;

    public nint WindowHandle
    {
        get
        {
            if (_holder == 0 && OperatingSystem.IsLinux())
            {
                try
                {
                    // Kept open for the app's lifetime: the server destroys a connection's windows when it closes.
                    if (_display == 0)
                        _display = XOpenDisplay(0);
                    if (_display != 0)
                    {
                        _holder = XCreateSimpleWindow(_display, XDefaultRootWindow(_display), 0, 0, 1, 1, 0, 0, 0x141414);
                        XFlush(_display);
                    }
                }
                catch (DllNotFoundException)
                {
                    // No libX11 (Wayland-only system): the player opens a window of its own.
                }
            }
            return _holder;
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (WindowHandle == 0)
            return base.CreateNativeControlCore(parent);

        _fitTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => FitPlayer());
        _fitTimer.Start();
        return new PlatformHandle(_holder, "XID");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        // Avalonia has already parked our window; destroying it would destroy the player's window with it.
        if (control.Handle != _holder)
            base.DestroyNativeControlCore(control);
        _fitTimer?.Stop();
    }

    // ponytail: polls the sizes every 200 ms; listen for ConfigureNotify/CreateNotify on the holder if this ever matters.
    private void FitPlayer()
    {
        if (XGetGeometry(_display, _holder, out _, out _, out _, out uint width, out uint height, out _, out _) == 0)
            return;
        if (XQueryTree(_display, _holder, out _, out _, out nint children, out uint count) == 0 || children == 0)
            return;
        nint player = count > 0 ? Marshal.ReadIntPtr(children) : 0;
        XFree(children);
        if (player == 0 || XGetGeometry(_display, player, out _, out _, out _, out uint playerWidth, out uint playerHeight, out _, out _) == 0)
            return;
        if (playerWidth != width || playerHeight != height)
        {
            XMoveResizeWindow(_display, player, 0, 0, width, height);
            XFlush(_display);
        }
    }

    private const string LibX11 = "libX11.so.6";

    [DllImport(LibX11)]
    private static extern nint XOpenDisplay(nint name);

    [DllImport(LibX11)]
    private static extern nint XDefaultRootWindow(nint display);

    [DllImport(LibX11)]
    private static extern nint XCreateSimpleWindow(nint display, nint parent, int x, int y, uint width, uint height, uint borderWidth, nuint border, nuint background);

    [DllImport(LibX11)]
    private static extern int XGetGeometry(nint display, nint drawable, out nint root, out int x, out int y, out uint width, out uint height, out uint borderWidth, out uint depth);

    [DllImport(LibX11)]
    private static extern int XQueryTree(nint display, nint window, out nint root, out nint parent, out nint children, out uint count);

    [DllImport(LibX11)]
    private static extern int XMoveResizeWindow(nint display, nint window, int x, int y, uint width, uint height);

    [DllImport(LibX11)]
    private static extern int XFree(nint data);

    [DllImport(LibX11)]
    private static extern int XFlush(nint display);
}
#endif
