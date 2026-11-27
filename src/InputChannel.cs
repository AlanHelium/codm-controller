using System.Runtime.InteropServices;
using System.Text;

namespace CODMControllerProbe;

/// <summary>
/// 键盘注入方式。游戏版本更新可能改变对输入的处理（过滤系统注入、只认渲染子窗口、
/// 只认扫描码等），这里提供五种可切换通道，由用户在游戏内逐个实测，能触发的那一方式保留。
/// 0 窗口消息 · 主窗口（原有默认） / 1 窗口消息 · 渲染子窗口 / 2 系统模拟 · 扫描码 /
/// 3 系统模拟 · 虚拟键 / 4 双通道（窗口消息 + 系统模拟）
/// </summary>
public static class InputChannel
{
    public const int Count = 5;

    public static bool Valid(int value) => value >= 0 && value < Count;

    public static string Name(int value) => value switch
    {
        1 => Lang.T("窗口消息 · 渲染子窗口", "Window message · child window"),
        2 => Lang.T("系统模拟 · 扫描码", "System input · scan code"),
        3 => Lang.T("系统模拟 · 虚拟键", "System input · virtual key"),
        4 => Lang.T("双通道 · 消息 + 系统", "Dual · message + system"),
        _ => Lang.T("窗口消息 · 主窗口", "Window message · main window"),
    };

    public static string Hint(int value) => value switch
    {
        1 => Lang.T("把按键投给游戏进程里最大的可见子窗口。模拟器或多窗口引擎的画面窗口常不是主窗口，主窗口方式失效时先试这个。",
            "Posts to the largest visible child window of the game process. Emulators and multi-window engines often render into a child window; try this first when the main-window method stops working."),
        2 => Lang.T("用系统级按键事件模拟真实键盘，带扫描码。部分引擎只接受这类输入；会带注入标记，游戏若过滤则无效。需要游戏在前台。",
            "Sends system-level key events with scan codes, like a real keyboard. Some engines accept only this; it carries the injected flag, so a game that filters injected input will ignore it. The game must be in the foreground."),
        3 => Lang.T("系统级按键事件，但只带虚拟键码、不带扫描码。适合只检查键码的引擎。需要游戏在前台。",
            "System-level key events with the virtual-key code only. For engines that check key codes rather than scan codes. The game must be in the foreground."),
        4 => Lang.T("同时发窗口消息和系统按键事件，命中率最高。少数情况下一次按键会被识别两次；如果其它方式能用，就别用这个。",
            "Sends both window messages and system key events for the highest hit rate. A key may be recognized twice in rare cases; if another method works, do not use this one."),
        _ => Lang.T("默认方式。直接向游戏窗口投递按键消息，不触发系统注入标记。",
            "Default. Posts key messages straight to the game window without system injection flags."),
    };
}

/// <summary>
/// 实际注入实现。窗口消息走 PostMessage；系统模拟走 SendInput。
/// 系统模拟只在游戏处于前台时发送——否则按键会落到桌面或其它程序上。
/// </summary>
public static class KeyInjection
{
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    private static int _channel;

    /// <summary>当前注入方式（0~4）。写入前已做范围校验。</summary>
    public static int Channel
    {
        get => Volatile.Read(ref _channel);
        set => Volatile.Write(ref _channel, Valid(value) ? value : 0);
    }

    private static bool Valid(int value) => value >= 0 && value < InputChannel.Count;

    /// <summary>按当前方式发送一次按键。返回 true 表示这一路投递成功。</summary>
    public static bool Send(GameKeyTarget target, GameKey key, bool down, bool repeat = false)
    {
        if (target.Handle == IntPtr.Zero || !IsAlive(target)) return false;
        if (LooksLikeOwnWindow(target.Handle)) return false;
        key = key.Canonical;
        switch (Channel)
        {
            case 1: return Post(ChildWindow(target), key, down, repeat);
            case 2: return SystemKey(target, key, down, scanCode: true);
            case 3: return SystemKey(target, key, down, scanCode: false);
            case 4: return Post(target.Handle, key, down, repeat) | SystemKey(target, key, down, scanCode: true);
            default: return Post(target.Handle, key, down, repeat);
        }
    }

    /// <summary>拦截被误认成游戏窗口的本软件界面，避免按键打回自己。</summary>
    private static bool LooksLikeOwnWindow(IntPtr handle)
    {
        var title = new StringBuilder(256);
        GetWindowText(handle, title, 256);
        string text = title.ToString();
        return text.Contains("手柄转换", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ControllerProbe", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Controller Bridge", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Post(IntPtr handle, GameKey key, bool down, bool repeat)
    {
        if (handle == IntPtr.Zero) return false;
        return PostMessage(handle, down ? WM_KEYDOWN : WM_KEYUP, (UIntPtr)key.Vk,
            (IntPtr)(long)KeyMessage.Flags(key, down, repeat));
    }

    // ---- 渲染子窗口：缓存句柄，窗口重建或失效时重算 ----

    private static IntPtr _childParent, _childCache;

    private static IntPtr ChildWindow(GameKeyTarget target)
    {
        if (_childParent == target.Handle && _childCache != IntPtr.Zero && IsWindow(_childCache)) return _childCache;
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;
        EnumChildWindows(target.Handle, (handle, _) =>
        {
            if (!IsWindowVisible(handle)) return true;
            GetWindowThreadProcessId(handle, out uint pid);
            if (pid != target.ProcessId) return true;
            if (!GetWindowRect(handle, out RECT rect)) return true;
            long area = (long)Math.Max(0, rect.Right - rect.Left) * Math.Max(0, rect.Bottom - rect.Top);
            if (area > bestArea) { bestArea = area; best = handle; }
            return true;
        }, IntPtr.Zero);
        if (best == IntPtr.Zero)
        {
            // 部分模拟器把渲染窗口做成同进程的兄弟顶层窗口，而不是子窗口。
            EnumWindows((handle, _) =>
            {
                if (handle == target.Handle || !IsWindowVisible(handle)) return true;
                GetWindowThreadProcessId(handle, out uint pid);
                if (pid != target.ProcessId) return true;
                if (!GetWindowRect(handle, out RECT rect)) return true;
                long area = (long)Math.Max(0, rect.Right - rect.Left) * Math.Max(0, rect.Bottom - rect.Top);
                if (area > bestArea) { bestArea = area; best = handle; }
                return true;
            }, IntPtr.Zero);
        }
        _childParent = target.Handle;
        _childCache = best;
        return best != IntPtr.Zero ? best : target.Handle;
    }

    // ---- 系统模拟 ----

    private static bool SystemKey(GameKeyTarget target, GameKey key, bool down, bool scanCode)
    {
        if (!IsForeground(target)) return false;
        uint flags = down ? 0u : KEYEVENTF_KEYUP;
        if (key.Extended) flags |= KEYEVENTF_EXTENDEDKEY;
        ushort vk = 0, scan = 0;
        if (scanCode) { flags |= KEYEVENTF_SCANCODE; scan = key.Scan; }
        else vk = key.Vk;
        INPUT[] inputs =
        {
            new() { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } } },
        };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == 1;
    }

    private static bool IsForeground(GameKeyTarget target)
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        GetWindowThreadProcessId(foreground, out uint pid);
        return pid == target.ProcessId;
    }

    private static bool IsAlive(GameKeyTarget target)
    {
        if (target.Handle == IntPtr.Zero || !IsWindow(target.Handle)) return false;
        GetWindowThreadProcessId(target.Handle, out uint pid);
        return pid == target.ProcessId && pid != (uint)Environment.ProcessId && pid != 0;
    }

    // ---- Win32 ----

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi; // INPUT must include the largest union member: 40 bytes on x64.
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
