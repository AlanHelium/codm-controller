namespace CODMControllerProbe;

public sealed record HoldOptions(bool Prone, int ProneSource, bool Interact, int InteractSource,
    bool Nuke, int TacticalSource, int LethalSource);

public partial class MainForm
{
    private HoldOptions ReadHoldOptions() => new(_enableFastProne, _map.Crouch,
        _enableFastInteract, _interactKey, _enableNukeCombo, _map.Tactical, _map.Lethal);

    private IEnumerable<string> HoldUses(int source, string except = "")
    {
        string key = KeyMap.BitName(source);
        if (except != "prone" && _enableFastProne && source == _map.Crouch)
            yield return key + " · 长按快速趴下（Ctrl）";
        if (except != "interact" && _enableFastInteract && source == _interactKey)
            yield return key + " · 长按快速交互（F）";
        if (except != "nuke" && _enableNukeCombo && (source == _map.Tactical || source == _map.Lethal))
            yield return key + " · 长按核弹组合（J）";
    }

    private void ShowBindingNotice(IEnumerable<string> uses, string instruction)
    {
        var form = new Form { Text = "按键已被占用", Font = Assets.Ui(10) };
        var body = UiLayout.Stack(18); form.Controls.Add(body);
        UiLayout.Add(body, UiLayout.Text("按键已被占用", true));
        foreach (string use in uses.Distinct()) UiLayout.Add(body, UiLayout.Text(use));
        UiLayout.Add(body, UiLayout.Text(instruction));
        UiLayout.Action(body, "知道了，保留当前设置", () => form.Close(), true);
        lock (_keyboardBindingGate) { _mappingConflictActive = true; ResetMappingTransient(); }
        form.FormClosed += (_, _) => { lock (_keyboardBindingGate) { _mappingConflictActive = false; ResetMappingTransient(); } };
        MountStudioForm(form, keepParentOnClose: true);
    }

    private bool AllowNonHoldMapping(IEnumerable<int> sources)
    {
        var uses = sources.Distinct().SelectMany(s => HoldUses(s)).Distinct().ToArray();
        if (uses.Length == 0) return true;
        ShowBindingNotice(uses, "请先到「光标与交互 → 长按与快捷交互」取消勾选对应功能，再设置此按键。当前绑定未更改。");
        return false;
    }

    private bool SetHoldEnabled(string kind, bool enabled)
    {
        int[] sources = kind switch { "prone" => new[] { _map.Crouch }, "interact" => new[] { _interactKey }, _ => new[] { _map.Tactical, _map.Lethal } };
        if (enabled)
        {
            var conflicts = sources.SelectMany(s => HoldUses(s, kind)
                .Concat(SourceOverrideName(s) is { Length: > 0 } owner ? new[] { KeyMap.BitName(s) + " · " + owner } : Array.Empty<string>())
                .Concat(!_experimentalCursorEnabled && ((_comboMask & s) != 0) ? new[] { KeyMap.BitName(s) + " · 光标组合" } : Array.Empty<string>())
                .Concat(_enableRun && s == 0x0040 ? new[] { "L3 · 奔跑切换" } : Array.Empty<string>())).Distinct().ToArray();
            if (conflicts.Length > 0) { ShowBindingNotice(conflicts, "请先关闭对应长按功能或移除已有映射，再启用此长按功能。游戏原生短按动作可以保留。"); return false; }
        }
        MappingEdited(() => { if (kind == "prone") _enableFastProne = enabled; else if (kind == "interact") _enableFastInteract = enabled; else _enableNukeCombo = enabled; });
        RefreshRemapViews(); return true;
    }

    private void SetInteractSource(int source)
    {
        if (source == _interactKey) return;
        if (_enableFastInteract) { ShowBindingNotice(new[] { KeyMap.BitName(_interactKey) + " · 长按快速交互" }, "请先取消勾选「长按快速交互」，再更改触发键。"); RefreshRemapViews(); return; }
        if (!AllowNonHoldMapping(new[] { source })) { RefreshRemapViews(); return; }
        MappingEdited(() => _interactKey = source); RefreshRemapViews();
    }

    private void SetCursorCombo(ushort mask)
    {
        if (mask == _comboMask) return;
        if (!AllowNonHoldMapping(MappingPolicy.PadSources.Where(s => (s & mask) != 0))) { RefreshRemapViews(); return; }
        MappingEdited(() => _comboMask = mask); RefreshRemapViews();
    }

    private void NormalizeLegacyHoldOwnership()
    {
        bool Claimed(int source) => SourceOverrideName(source).Length > 0
            || (!_experimentalCursorEnabled && (_comboMask & source) != 0)
            || (_enableRun && source == 0x0040);
        if (Claimed(_map.Crouch)) _enableFastProne = false;
        if (Claimed(_interactKey) || (_enableFastProne && _interactKey == _map.Crouch)) _enableFastInteract = false;
        if (Claimed(_map.Tactical) || Claimed(_map.Lethal)
            || (_enableFastProne && (_map.Crouch == _map.Tactical || _map.Crouch == _map.Lethal))
            || (_enableFastInteract && (_interactKey == _map.Tactical || _interactKey == _map.Lethal))) _enableNukeCombo = false;
    }
}
