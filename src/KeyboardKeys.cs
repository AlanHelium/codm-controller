using System.Collections.Generic;

namespace CODMControllerProbe;

/// <summary>
/// 键盘键值表：把「键盘上的任意键」翻译成注入所需的 VK + Set-1 扫描码 + 显示名。
/// 注入通道与 U/J/F/H/Tab 一致：PostMessage 直投游戏窗口（wParam=VK，lParam 含扫描码），
/// 回退 SendInput（ScanCode 模式）。扫描码为 Set-1（XT），字母/数字/符号键无扩展前缀，
/// 方向键与右修饰键是扩展键（lParam bit24 / KEYEVENTF_EXTENDEDKEY）。
/// Fn 键由键盘固件处理、不产生标准键码，故不支持映射（表中不含）。
/// </summary>
public sealed record KeyboardKey(ushort Vk, ushort Scan, string Name, bool Extended = false);

public static class KeyboardKeys
{
    // 支持映射的键盘键全集（字母 / 数字 / F1-F12 / 方向键 / 修饰键 / 常用符号与编辑键）。
    // 字母区（Set-1 扫描码）
    public static readonly KeyboardKey A = new(0x41, 0x1E, "A");
    public static readonly KeyboardKey B = new(0x42, 0x30, "B");
    public static readonly KeyboardKey C = new(0x43, 0x2E, "C");
    public static readonly KeyboardKey D = new(0x44, 0x20, "D");
    public static readonly KeyboardKey E = new(0x45, 0x12, "E");
    public static readonly KeyboardKey F = new(0x46, 0x21, "F");
    public static readonly KeyboardKey G = new(0x47, 0x22, "G");
    public static readonly KeyboardKey H = new(0x48, 0x23, "H");
    public static readonly KeyboardKey I = new(0x49, 0x17, "I");
    public static readonly KeyboardKey J = new(0x4A, 0x24, "J");
    public static readonly KeyboardKey K = new(0x4B, 0x25, "K");
    public static readonly KeyboardKey L = new(0x4C, 0x26, "L");
    public static readonly KeyboardKey M = new(0x4D, 0x32, "M");
    public static readonly KeyboardKey N = new(0x4E, 0x31, "N");
    public static readonly KeyboardKey O = new(0x4F, 0x18, "O");
    public static readonly KeyboardKey P = new(0x50, 0x19, "P");
    public static readonly KeyboardKey Q = new(0x51, 0x10, "Q");
    public static readonly KeyboardKey R = new(0x52, 0x13, "R");
    public static readonly KeyboardKey S = new(0x53, 0x1F, "S");
    public static readonly KeyboardKey T = new(0x54, 0x14, "T");
    public static readonly KeyboardKey U = new(0x55, 0x16, "U");
    public static readonly KeyboardKey V = new(0x56, 0x2F, "V");
    public static readonly KeyboardKey W = new(0x57, 0x11, "W");
    public static readonly KeyboardKey X = new(0x58, 0x2D, "X");
    public static readonly KeyboardKey Y = new(0x59, 0x15, "Y");
    public static readonly KeyboardKey Z = new(0x5A, 0x2C, "Z");

    // 数字区（主键盘顶排）
    public static readonly KeyboardKey D0 = new(0x30, 0x0B, "0");
    public static readonly KeyboardKey D1 = new(0x31, 0x02, "1");
    public static readonly KeyboardKey D2 = new(0x32, 0x03, "2");
    public static readonly KeyboardKey D3 = new(0x33, 0x04, "3");
    public static readonly KeyboardKey D4 = new(0x34, 0x05, "4");
    public static readonly KeyboardKey D5 = new(0x35, 0x06, "5");
    public static readonly KeyboardKey D6 = new(0x36, 0x07, "6");
    public static readonly KeyboardKey D7 = new(0x37, 0x08, "7");
    public static readonly KeyboardKey D8 = new(0x38, 0x09, "8");
    public static readonly KeyboardKey D9 = new(0x39, 0x0A, "9");

    // F1-F12
    public static readonly KeyboardKey F1 = new(0x70, 0x3B, "F1");
    public static readonly KeyboardKey F2 = new(0x71, 0x3C, "F2");
    public static readonly KeyboardKey F3 = new(0x72, 0x3D, "F3");
    public static readonly KeyboardKey F4 = new(0x73, 0x3E, "F4");
    public static readonly KeyboardKey F5 = new(0x74, 0x3F, "F5");
    public static readonly KeyboardKey F6 = new(0x75, 0x40, "F6");
    public static readonly KeyboardKey F7 = new(0x76, 0x41, "F7");
    public static readonly KeyboardKey F8 = new(0x77, 0x42, "F8");
    public static readonly KeyboardKey F9 = new(0x78, 0x43, "F9");
    public static readonly KeyboardKey F10 = new(0x79, 0x44, "F10");
    public static readonly KeyboardKey F11 = new(0x7A, 0x57, "F11");
    public static readonly KeyboardKey F12 = new(0x7B, 0x58, "F12");

    // 方向键（扩展键）
    public static readonly KeyboardKey Up = new(0x26, 0x48, "↑", true);
    public static readonly KeyboardKey Down = new(0x28, 0x50, "↓", true);
    public static readonly KeyboardKey Left = new(0x25, 0x4B, "←", true);
    public static readonly KeyboardKey Right = new(0x27, 0x4D, "→", true);

    // 修饰键与系统键
    public static readonly KeyboardKey Tab = new(0x09, 0x0F, "Tab");
    public static readonly KeyboardKey CapsLock = new(0x14, 0x3A, "CapsLock");
    public static readonly KeyboardKey LShift = new(0xA0, 0x2A, "左Shift");
    public static readonly KeyboardKey RShift = new(0xA1, 0x36, "右Shift");
    public static readonly KeyboardKey LCtrl = new(0xA2, 0x1D, "左Ctrl");
    public static readonly KeyboardKey RCtrl = new(0xA3, 0x1D, "右Ctrl", true);
    public static readonly KeyboardKey LAlt = new(0xA4, 0x38, "左Alt");
    public static readonly KeyboardKey RAlt = new(0xA5, 0x38, "右Alt", true);
    public static readonly KeyboardKey LWin = new(0x5B, 0x5B, "Win", true);
    public static readonly KeyboardKey RWin = new(0x5C, 0x5C, "右Win", true);

    // 常用编辑与符号键
    public static readonly KeyboardKey Esc = new(0x1B, 0x01, "Esc");
    public static readonly KeyboardKey Enter = new(0x0D, 0x1C, "Enter");
    public static readonly KeyboardKey Backspace = new(0x08, 0x0E, "Backspace");
    public static readonly KeyboardKey Space = new(0x20, 0x39, "Space");
    public static readonly KeyboardKey Minus = new(0xBD, 0x0C, "-");
    public static readonly KeyboardKey Equals = new(0xBB, 0x0D, "=");
    public static readonly KeyboardKey LBracket = new(0xDB, 0x1A, "[");
    public static readonly KeyboardKey RBracket = new(0xDD, 0x1B, "]");
    public static readonly KeyboardKey Backslash = new(0xDC, 0x2B, "\\");
    public static readonly KeyboardKey Semicolon = new(0xBA, 0x27, ";");
    public static readonly KeyboardKey Quote = new(0xDE, 0x28, "'");
    public static readonly KeyboardKey Grave = new(0xC0, 0x29, "`");
    public static readonly KeyboardKey Comma = new(0xBC, 0x33, ",");
    public static readonly KeyboardKey Period = new(0xBE, 0x34, ".");
    public static readonly KeyboardKey Slash = new(0xBF, 0x35, "/");

    /// <summary>可映射的键盘键全表（顺序即设置窗口展示顺序）。</summary>
    public static readonly IReadOnlyList<KeyboardKey> All = new[]
    {
        // 字母（游戏最常用，排最前）
        A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        // 数字
        D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
        // F1-F12
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
        // 方向键
        Up, Down, Left, Right,
        // 修饰键 / 系统键
        Tab, CapsLock, LShift, RShift, LCtrl, RCtrl, LAlt, RAlt, LWin, RWin,
        // 编辑与符号
        Esc, Enter, Backspace, Space, Minus, Equals, LBracket, RBracket, Backslash,
        Semicolon, Quote, Grave, Comma, Period, Slash,
    };

    /// <summary>按 VK 查键盘键；找不到返回 null（表外键暂不支持映射）。</summary>
    public static KeyboardKey? ByVk(ushort vk)
    {
        if(vk==0x10)return LShift;if(vk==0x11)return LCtrl;if(vk==0x12)return LAlt;
        foreach (var k in All)
            if (k.Vk == vk) return k;
        return null;
    }

    public static KeyboardKey? FromMessage(ushort vk,IntPtr lParam)
    {
        int scan=(int)(((long)lParam>>16)&0xff);bool extended=(((long)lParam>>24)&1)!=0;
        if(vk==0x10)return scan==0x36?RShift:LShift;
        if(vk==0x11)return extended?RCtrl:LCtrl;
        if(vk==0x12)return extended?RAlt:LAlt;
        return ByVk(vk);
    }

    /// <summary>按显示名查键盘键（设置持久化用短名，如 "Q"、"左Ctrl"）。</summary>
    public static KeyboardKey? ByName(string name)
    {
        foreach (var k in All)
            if (k.Name == name) return k;
        return null;
    }
}
