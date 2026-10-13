using System.Diagnostics;
using System.Text.Json;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets.DualShock4;
namespace CODMControllerProbe;

// Explicit diagnostic entry point: no UI, settings saves, game messages or driver installs.
public static class RepairHardwareCheck
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    public static void HoldForRecoveryTest(string reportPath)
    {
        var before = HidHideManager.TakeSnapshot();
        try {
            if (!before.Success) throw new IOException(before.Error);
            if (!HidHideManager.RecoverPreviousSession(out _, out var error)) throw new IOException(error);
            using var client = new ViGEmClient();
            var pad = client.CreateDualShock4Controller();
            pad.Connect(); pad.ResetReport(); pad.SubmitReport();
            IsolationProbe.StartWatchdog();
            var ids = HidEnum.GetGamepadInstanceIds();
            if (HidHideManager.HideDevices(ids) == 0) throw new IOException(HidHideManager.LastError);
            if (!SonyHidPad.Open()) throw new IOException(SonyHidPad.LastProbeInfo);
            var result = IsolationProbe.Verify(ids);
            if (!result.Verified) throw new IOException(result.Message);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Ready = true, Pid = Environment.ProcessId, Before = before, Isolation = result }, Json));
            while (true) Thread.Sleep(500);
        } catch (Exception ex) {
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Ready = false, Error = ex.ToString() }, Json));
        } finally { SonyHidPad.Close(); HidHideManager.Restore(); }
    }
    public static void Run(string reportPath)
    {
        var report = new Dictionary<string, object?>();
        var before = HidHideManager.TakeSnapshot();
        report["Before"] = before;
        bool success = false;
        try {
            if (!before.Success) throw new IOException(before.Error);
            if (!HidHideManager.RecoverPreviousSession(out _, out var recoveryError)) throw new IOException(recoveryError);
            var devices = DeviceCatalog.Enumerate();
            var sony = devices.Where(SonyHidPad.Supported).ToList();
            report["PhysicalSonyDevices"] = sony;
            if (sony.Count == 0) throw new IOException("没有在场实体 Sony 手柄");
            if (!SonyHidPad.Open()) throw new IOException(SonyHidPad.LastProbeInfo);
            report["Model"] = SonyHidPad.KindName();
            report["Raw"] = SonyHidPad.LastReportHex;
            report["ReadBeforeHide"] = SonyHidPad.Read(out var raw);
            report["Input"] = raw;
            using var client = new ViGEmClient();
            var pad = client.CreateDualShock4Controller();
            pad.AutoSubmitReport = false;
            pad.Connect(); pad.ResetReport(); pad.SubmitReport();
            try {
                var ids = HidEnum.GetGamepadInstanceIds();
                report["HiddenIds"] = ids;
                if (!ids.Contains(SonyHidPad.DeviceId, StringComparer.OrdinalIgnoreCase)) throw new IOException("实体 Sony 接口漏入隐藏名单");
                report["VirtualExcluded"] = DeviceCatalog.Enumerate().Where(d => d.Virtual).All(d => !ids.Contains(d.Id, StringComparer.OrdinalIgnoreCase));
                IsolationProbe.StartWatchdog();
                if (HidHideManager.HideDevices(ids) == 0) throw new IOException(HidHideManager.LastError);
                report["Applied"] = HidHideManager.TakeSnapshot();
                var isolation = IsolationProbe.Verify(ids);
                report["Isolation"] = isolation;
                if (!isolation.Verified) throw new IOException(isolation.Message);
                // Verify whitelisting by closing the physical handle and reopening while hiding is ON.
                SonyHidPad.Close();
                if (!SonyHidPad.Open()) throw new IOException("隐藏开启时白名单进程无法重开实体：" + SonyHidPad.LastProbeInfo);
                report["ReopenedWhileHidden"] = true;
                long frames = SonyHidPad.FramesReceived;
                Thread.Sleep(200);
                report["FreshReportsWhileHidden"] = SonyHidPad.FramesReceived > frames && SonyHidPad.Read(out _);
                if (!(bool)report["FreshReportsWhileHidden"]!) throw new IOException("隐藏后未持续收到实体报告");
                // Submit a synthetic swapped shoulder input to the isolated virtual pad and read the actual HID output.
                var swap = ShoulderTriggerSwap.Apply(0x0100, 0, 0, true);
                pad.SetSliderValue(DualShock4Slider.LeftTrigger, swap.LeftTrigger);
                pad.SetButtonState(DualShock4Button.TriggerLeft, swap.LeftTrigger > 10);
                pad.SetButtonState(DualShock4Button.ShoulderLeft, (swap.Buttons & 0x0100) != 0);
                pad.SubmitReport();
                var output = IsolationProbe.Verify(ids);
                report["PhysicalL1VirtualOutput"] = output;
                var hex = output.Access.First(a => a.Virtual && a.ReportHex.Length > 0).ReportHex;
                bool parsed = SonyReportParser.TryParse(Convert.FromHexString(hex), SonyModel.Ds4, false, out var frame);
                report["VirtualSwapCorrect"] = parsed && frame.Gamepad.bLeftTrigger == 255 && (frame.Gamepad.wButtons & 0x0100) == 0;
                if (!(bool)report["VirtualSwapCorrect"]!) throw new IOException("虚拟 DS4 的实际报告没有正确交换 L1/L2");
                pad.ResetReport(); pad.SubmitReport();
                success = true;
            } finally { pad.ResetReport(); pad.SubmitReport(); pad.Disconnect(); }
        }
        catch (Exception ex) { report["Error"] = ex.ToString(); }
        finally {
            SonyHidPad.Close();
            report["RestoreSucceeded"] = HidHideManager.Restore();
            var after = HidHideManager.TakeSnapshot();
            report["After"] = after;
            report["RestoredExactly"] = after.Success && before.Success && before.Active == after.Active &&
                before.Inverse == after.Inverse &&
                before.Blacklist.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(after.Blacklist) &&
                before.Whitelist.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(after.Whitelist);
            report["Passed"] = success && (bool)report["RestoreSucceeded"]! && (bool)report["RestoredExactly"]!;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, Json));
        }
    }
}
public readonly record struct SwappedInput(ushort Buttons, byte LeftTrigger, byte RightTrigger);
public static class ShoulderTriggerSwap
{
    public static SwappedInput Apply(ushort buttons, byte left, byte right, bool enabled)
    {
        if (!enabled) return new(buttons, left, right);
        ushort swapped = (ushort)(buttons & ~0x0300);
        if (left > 10) swapped |= 0x0100;
        if (right > 10) swapped |= 0x0200;
        return new(swapped, (buttons & 0x0100) != 0 ? (byte)255 : (byte)0,
            (buttons & 0x0200) != 0 ? (byte)255 : (byte)0);
    }
}

