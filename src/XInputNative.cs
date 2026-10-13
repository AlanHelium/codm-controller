using System.Runtime.InteropServices;

namespace CODMControllerProbe;

/// <summary>
/// XInput (xinput1_4.dll) P/Invoke —— 读取 Xbox 360 / Xbox One / 第三方 XInput 协议手柄。
/// </summary>
public static class XInputNative
{
    private const string Dll = "xinput1_4.dll";

    public const int ERROR_SUCCESS = 0;
    public const int ERROR_DEVICE_NOT_CONNECTED = 1167;
    public const uint XINPUT_FLAG_GAMEPAD = 0x1;

    [Flags]
    public enum GamepadButtons : ushort
    {
        DPadUp = 0x0001,
        DPadDown = 0x0002,
        DPadLeft = 0x0004,
        DPadRight = 0x0008,
        Start = 0x0010,
        Back = 0x0020,
        LeftThumb = 0x0040,
        RightThumb = 0x0080,
        LeftShoulder = 0x0100,
        RightShoulder = 0x0200,
        A = 0x1000,
        B = 0x2000,
        X = 0x4000,
        Y = 0x8000,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputGamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputState
    {
        public uint dwPacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputVibration
    {
        public ushort wLeftMotorSpeed;
        public ushort wRightMotorSpeed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputCapabilities
    {
        public byte Type;
        public byte SubType;
        public ushort Flags;
        public XInputGamepad Gamepad;
        public XInputVibration Vibration;
    }

    [DllImport(Dll)]
    public static extern int XInputGetState(int dwUserIndex, out XInputState pState);

    [DllImport(Dll)]
    public static extern int XInputGetCapabilities(int dwUserIndex, uint dwFlags, out XInputCapabilities pCapabilities);

    [DllImport(Dll)]
    public static extern int XInputSetState(int dwUserIndex, ref XInputVibration pVibration);

    /// <summary>把 SubType 转成可读名称。</summary>
    public static string SubTypeName(byte subType) => subType switch
    {
        0x01 => "Gamepad",
        0x02 => "Wheel",
        0x03 => "Arcade Stick",
        0x04 => "Flight Stick",
        0x05 => "Dance Pad",
        0x06 => "Guitar",
        0x07 => "Guitar Alt",
        0x08 => "Drum Kit",
        0x09 => "Guitar Bass",
        0x0A => "Arcade Pad",
        0x0B => "Guitar Alt 2",
        0x0C => "Guitar Bass 2",
        0x11 => "Xbox One Gamepad",
        0x12 => "Xbox One Wheel",
        0x13 => "Xbox One Elite",
        0x14 => "Xbox One Arcade Stick",
        0x15 => "Xbox One Flight Stick",
        0x21 => "Xbox Series X|S Gamepad",
        0x23 => "Xbox Series X|S Elite",
        _ => $"SubType 0x{subType:X2}",
    };
}
