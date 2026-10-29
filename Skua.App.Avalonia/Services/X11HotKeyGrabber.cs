using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Skua.App.Avalonia.Services;

/// <summary>
/// Linux hotkeys. The game runs in the player's own X11 window inside Bonxie's, and while it has the keyboard
/// focus Avalonia never sees a key press. XGrabKey on Bonxie's window makes the X server send the hotkeys to us
/// whenever the focus is in that window or in any window inside it (the game included), and to nobody else;
/// other programs keep their keys.
/// </summary>
internal sealed class X11HotKeyGrabber : IDisposable
{
    public readonly record struct Gesture(Key Key, KeyModifiers Modifiers);

    private readonly Action<Gesture> _onPressed;
    private readonly object _lock = new();
    private IntPtr _window;
    private Gesture[] _gestures = [];
    private bool _changed;
    private volatile bool _disposed;
    private Thread? _thread;

    public X11HotKeyGrabber(Action<Gesture> onPressed)
    {
        _onPressed = onPressed;
    }

    /// <summary>Grabs <paramref name="gestures"/> on the X11 window <paramref name="window"/>, replacing the previous grabs.</summary>
    public void Update(IntPtr window, IEnumerable<Gesture> gestures)
    {
        lock (_lock)
        {
            _window = window;
            _gestures = gestures.ToArray();
            _changed = true;
        }

        if (_thread is null && !_disposed)
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "X11 hotkeys" };
            _thread.Start();
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }

    // Every X11 call happens on this thread, on a connection of its own (Avalonia's isn't shared).
    private void Run()
    {
        IntPtr display;
        try
        {
            InstallErrorHandler();
            display = XOpenDisplay(IntPtr.Zero);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"X11 hotkeys unavailable: {ex.Message}");
            return;
        }

        if (display == IntPtr.Zero)
        {
            System.Diagnostics.Trace.WriteLine("X11 hotkeys unavailable: can't open the display.");
            return;
        }

        lock (s_ownDisplays)
            s_ownDisplays.Add(display);

        List<(int Keycode, uint Modifiers, Gesture Gesture)> grabs = [];
        IntPtr grabWindow = IntPtr.Zero;
        IntPtr xevent = Marshal.AllocHGlobal(XEventSize);
        try
        {
            while (!_disposed)
            {
                bool changed;
                IntPtr window;
                Gesture[] gestures;
                lock (_lock)
                {
                    changed = _changed;
                    _changed = false;
                    window = _window;
                    gestures = _gestures;
                }

                if (changed)
                {
                    Ungrab(display, grabWindow, grabs);
                    grabWindow = window;
                    if (window != IntPtr.Zero)
                    {
                        foreach (Gesture gesture in gestures)
                        {
                            nuint keysym = ToKeysym(gesture.Key);
                            if (keysym == 0)
                                continue;

                            int keycode = XKeysymToKeycode(display, keysym);
                            if (keycode == 0)
                                continue;

                            uint modifiers = ToX11Modifiers(gesture.Modifiers);
                            // Caps Lock and Num Lock change the state too; grab every combination so they don't matter.
                            foreach (uint locks in LockCombinations)
                                XGrabKey(display, keycode, modifiers | locks, window, true, GrabModeAsync, GrabModeAsync);
                            grabs.Add((keycode, modifiers, gesture));
                        }
                    }
                    XFlush(display);
                }

                while (XPending(display) > 0)
                {
                    XNextEvent(display, xevent);
                    int type = Marshal.ReadInt32(xevent);
                    if (type == MappingNotify)
                    {
                        // The keymap changed (another layout, a keyboard plugged in): keycodes may differ now.
                        XRefreshKeyboardMapping(xevent);
                        lock (_lock)
                            _changed = true;
                        continue;
                    }

                    if (type != KeyPress)
                        continue;

                    uint state = (uint)Marshal.ReadInt32(xevent, XKeyEventStateOffset) & (ShiftMask | ControlMask | Mod1Mask);
                    int keycode = Marshal.ReadInt32(xevent, XKeyEventKeycodeOffset);
                    foreach ((int Keycode, uint Modifiers, Gesture Gesture) grab in grabs)
                    {
                        if (grab.Keycode == keycode && grab.Modifiers == state)
                        {
                            _onPressed(grab.Gesture);
                            break;
                        }
                    }
                }

                Thread.Sleep(20);
            }

            Ungrab(display, grabWindow, grabs);
            XFlush(display);
        }
        catch (Exception ex)
        {
            // An exception on this thread would end the process; lose the hotkeys instead.
            System.Diagnostics.Trace.WriteLine($"X11 hotkeys stopped: {ex}");
        }
        finally
        {
            Marshal.FreeHGlobal(xevent);
            // Close first: closing syncs, and errors it reports (e.g. BadWindow if Bonxie's window is already
            // gone) must still be recognized as ours.
            XCloseDisplay(display);
            lock (s_ownDisplays)
                s_ownDisplays.Remove(display);
        }
    }

    private static void Ungrab(IntPtr display, IntPtr window, List<(int Keycode, uint Modifiers, Gesture Gesture)> grabs)
    {
        if (window != IntPtr.Zero)
        {
            foreach ((int Keycode, uint Modifiers, Gesture Gesture) grab in grabs)
            {
                foreach (uint locks in LockCombinations)
                    XUngrabKey(display, grab.Keycode, grab.Modifiers | locks, window);
            }
        }
        grabs.Clear();
    }

    private static uint ToX11Modifiers(KeyModifiers modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(KeyModifiers.Shift))
            result |= ShiftMask;
        if (modifiers.HasFlag(KeyModifiers.Control))
            result |= ControlMask;
        if (modifiers.HasFlag(KeyModifiers.Alt))
            result |= Mod1Mask;
        return result;
    }

    // X11 keysyms (X11/keysymdef.h) for the keys a hotkey can use.
    private static nuint ToKeysym(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
            return (nuint)('a' + (key - Key.A));
        if (key is >= Key.D0 and <= Key.D9)
            return (nuint)('0' + (key - Key.D0));
        if (key is >= Key.NumPad0 and <= Key.NumPad9)
            return (nuint)(0xFFB0 + (key - Key.NumPad0));
        if (key is >= Key.F1 and <= Key.F24)
            return (nuint)(0xFFBE + (key - Key.F1));

        return key switch
        {
            Key.Enter => 0xFF0D,
            Key.Tab => 0xFF09,
            Key.Escape => 0xFF1B,
            Key.Space => 0x20,
            Key.Back => 0xFF08,
            Key.Delete => 0xFFFF,
            Key.Insert => 0xFF63,
            Key.Home => 0xFF50,
            Key.End => 0xFF57,
            Key.PageUp => 0xFF55,
            Key.PageDown => 0xFF56,
            Key.Left => 0xFF51,
            Key.Up => 0xFF52,
            Key.Right => 0xFF53,
            Key.Down => 0xFF54,
            Key.Multiply => 0xFFAA,
            Key.Add => 0xFFAB,
            Key.Subtract => 0xFFAD,
            Key.Decimal => 0xFFAE,
            Key.Divide => 0xFFAF,
            Key.OemTilde => 0x60,
            Key.OemMinus => 0x2D,
            Key.OemPlus => 0x3D,
            Key.OemOpenBrackets => 0x5B,
            Key.OemCloseBrackets => 0x5D,
            Key.OemSemicolon => 0x3B,
            Key.OemQuotes => 0x27,
            Key.OemComma => 0x2C,
            Key.OemPeriod => 0x2E,
            Key.OemQuestion => 0x2F,
            Key.OemPipe => 0x5C,
            _ => 0
        };
    }

    // Xlib's default error handler ends the process, and a grab another program already holds fails with BadAccess.
    // Errors on our own connection are ignored; any other goes to the handler that was there before (Avalonia's).
    private static readonly HashSet<IntPtr> s_ownDisplays = [];
    private static XErrorHandler? s_errorHandler;
    private static IntPtr s_previousErrorHandler;

    private static void InstallErrorHandler()
    {
        lock (s_ownDisplays)
        {
            if (s_errorHandler is not null)
                return;

            s_errorHandler = OnXError;
            s_previousErrorHandler = XSetErrorHandler(s_errorHandler);
        }
    }

    private static int OnXError(IntPtr display, IntPtr errorEvent)
    {
        lock (s_ownDisplays)
        {
            if (s_ownDisplays.Contains(display))
                return 0;
        }

        return s_previousErrorHandler != IntPtr.Zero
            ? Marshal.GetDelegateForFunctionPointer<XErrorHandler>(s_previousErrorHandler)(display, errorEvent)
            : 0;
    }

    private const int KeyPress = 2;
    private const int MappingNotify = 34;
    private const int GrabModeAsync = 1;
    private const uint ShiftMask = 1 << 0;
    private const uint LockMask = 1 << 1;
    private const uint ControlMask = 1 << 2;
    private const uint Mod1Mask = 1 << 3;
    private const uint Mod2Mask = 1 << 4;
    private static readonly uint[] LockCombinations = [0, LockMask, Mod2Mask, LockMask | Mod2Mask];

    // XEvent is a union of 24 longs; in XKeyEvent (LP64) state and keycode sit at bytes 80 and 84.
    private const int XEventSize = 24 * 8;
    private const int XKeyEventStateOffset = 80;
    private const int XKeyEventKeycodeOffset = 84;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    private const string LibX11 = "libX11.so.6";

    [DllImport(LibX11)]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport(LibX11)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(LibX11)]
    private static extern IntPtr XSetErrorHandler(XErrorHandler handler);

    [DllImport(LibX11)]
    private static extern byte XKeysymToKeycode(IntPtr display, nuint keysym);

    [DllImport(LibX11)]
    private static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow, bool ownerEvents, int pointerMode, int keyboardMode);

    [DllImport(LibX11)]
    private static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow);

    [DllImport(LibX11)]
    private static extern int XPending(IntPtr display);

    [DllImport(LibX11)]
    private static extern int XNextEvent(IntPtr display, IntPtr xevent);

    [DllImport(LibX11)]
    private static extern int XFlush(IntPtr display);

    [DllImport(LibX11)]
    private static extern int XRefreshKeyboardMapping(IntPtr mappingEvent);
}
