using System.Buffers.Binary;
namespace CODMControllerProbe;

public enum SonyModel { Ds4, DualSense, Edge }
public readonly record struct SonyFrame(XInputNative.XInputGamepad Gamepad, bool Touchpad,
    bool GyroValid, short GyroX, short GyroY, short GyroZ);

// Linux hid-playstation.c: select by model, transport and report ID, never idle values.
public static class SonyReportParser
{
    public static bool TryParse(ReadOnlySpan<byte> report, SonyModel model, bool bluetooth, out SonyFrame frame)
    {
        frame = default;
        if (report.IsEmpty) return false;
        bool ds4 = model == SonyModel.Ds4;
        int offset;
        bool minimal = false;
        switch (report[0])
        {
            case 1:
                offset = 1;
                minimal = bluetooth || report.Length == 10;
                if (report.Length < (ds4 || minimal ? 10 : 11)) return false;
                break;
            case 0x11 when ds4:
                offset = 3;
                if (report.Length != 78 || !ValidBluetoothCrc(report)) return false;
                break;
            case 0x31 when !ds4:
                offset = 2;
                if (report.Length != 78 || !ValidBluetoothCrc(report)) return false;
                break;
            default: return false;
        }
        bool ds4Layout = ds4 || minimal;
        var data = report[offset..];
        int buttons = ds4Layout ? 4 : 7, triggers = ds4Layout ? 7 : 4;
        if (data.Length <= Math.Max(buttons + 2, triggers + 1)) return false;
        byte face = data[buttons], other = data[buttons + 1];
        ushort mask = (face & 15) switch { 0 => 1, 1 => 9, 2 => 8, 3 => 10,
            4 => 2, 5 => 6, 6 => 4, 7 => 5, _ => 0 };
        if ((face & 0x10) != 0) mask |= 0x4000;
        if ((face & 0x20) != 0) mask |= 0x1000;
        if ((face & 0x40) != 0) mask |= 0x2000;
        if ((face & 0x80) != 0) mask |= 0x8000;
        if ((other & 1) != 0) mask |= 0x0100;
        if ((other & 2) != 0) mask |= 0x0200;
        if ((other & 0x10) != 0) mask |= 0x0020;
        if ((other & 0x20) != 0) mask |= 0x0010;
        if ((other & 0x40) != 0) mask |= 0x0040;
        if ((other & 0x80) != 0) mask |= 0x0080;
        if ((data[buttons + 2] & 1) != 0) mask |= 0x0400;
        // 静止噪声死区：DSE/DualSense 实测静止最大径向偏移约 1388（满量程 32767 的 4.2%）、
        // DS4 约 576（1.8%）。径向幅度低于阈值 → 摇杆归零，防止静止时视角/方向漂移。
        // （此前摇杆原样透传，DSE 静止偏移经反死区放大后在游戏里表现为缓慢漂移。）
        short ax = Axis(data[0]), ay = Axis(data[1], true);
        short rx = Axis(data[2]), ry = Axis(data[3], true);
        int deadzone = model == SonyModel.Ds4 ? 800 : 1600;
        if (Magnitude(ax, ay) < deadzone) { ax = 0; ay = 0; }
        if (Magnitude(rx, ry) < deadzone) { rx = 0; ry = 0; }
        var pad = new XInputNative.XInputGamepad {
            wButtons = mask, bLeftTrigger = data[triggers], bRightTrigger = data[triggers + 1],
            sThumbLX = ax, sThumbLY = ay,
            sThumbRX = rx, sThumbRY = ry
        };
        int gyro = ds4Layout ? 12 : 15;
        bool gyroValid = !minimal && data.Length >= gyro + 6;
        frame = new(pad, (data[buttons + 2] & 2) != 0, gyroValid,
            gyroValid ? BinaryPrimitives.ReadInt16LittleEndian(data[gyro..]) : (short)0,
            gyroValid ? BinaryPrimitives.ReadInt16LittleEndian(data[(gyro + 2)..]) : (short)0,
            gyroValid ? BinaryPrimitives.ReadInt16LittleEndian(data[(gyro + 4)..]) : (short)0);
        return true;
    }
    private static int Magnitude(short x, short y)
        => (int)Math.Sqrt((long)x * x + (long)y * y);

    public static short Axis(byte value, bool invert = false)
    {
        int d = value - 128;
        int v = d < 0 ? d * 256 : (int)Math.Round(d * 32767.0 / 127);
        if (invert) v = v == -32768 ? 32767 : v == 32767 ? -32768 : -v;
        return (short)v;
    }
    public static uint BluetoothCrc(ReadOnlySpan<byte> payload)
    {
        uint crc = 0xffffffff;
        void Update(byte value) {
            crc ^= value;
            for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        Update(0xa1);
        foreach (byte value in payload) Update(value);
        return ~crc;
    }
    private static bool ValidBluetoothCrc(ReadOnlySpan<byte> report)
        => BluetoothCrc(report[..74]) == BinaryPrimitives.ReadUInt32LittleEndian(report[74..]);
}
