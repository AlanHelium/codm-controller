using System.Collections.Generic;

namespace CODMControllerProbe;

/// <summary>键位映射：把 XInput 键位（A/B/X/Y/LB/RB/LT/RT）绑定到游戏动作。</summary>
public sealed class KeyMap
{
    public int Jump = 0x1000;   // 默认 A
    public int Crouch = 0x2000; // 默认 B
    public int Reload = 0x4000; // 默认 X
    public int Switch = 0x8000; // 默认 Y
    public int Aim = LT;        // 瞄准（默认 LT/L2，可在键位修正中重绑）
    public int Shoot = RT;      // 射击（默认 RT/R2，可在键位修正中重绑）
    public int Tactical = 0x0100; // 战术道具（默认 LB/L1，可在键位修正中重绑）
    public int Lethal = 0x0200;   // 杀伤道具（默认 RB/R1，可在键位修正中重绑）
    public int MeleePing = 0x0080; // 近战/标点（默认 R3——DS 系 R3 原生即近战+标点同一键，可在键位修正中重绑到其它键）

    // 虚拟扩展位：LT/RT 扳机（XInput 的 wButtons 不含扳机，用高 16 位表示，与按钮位不冲突）
    public const int LT = 0x10000;
    public const int RT = 0x20000;

    public int ActionBinding(int index)=>index switch {0=>Jump,1=>Crouch,2=>Reload,3=>Switch,4=>Aim,5=>Shoot,6=>Tactical,7=>Lethal,8=>MeleePing,_=>throw new ArgumentOutOfRangeException(nameof(index))};
    private void SetActionBinding(int index,int bind)
    {
        switch(index){case 0:Jump=bind;break;case 1:Crouch=bind;break;case 2:Reload=bind;break;case 3:Switch=bind;break;
            case 4:Aim=bind;break;case 5:Shoot=bind;break;case 6:Tactical=bind;break;case 7:Lethal=bind;break;case 8:MeleePing=bind;break;default:throw new ArgumentOutOfRangeException(nameof(index));}
    }
    public void AssignAction(int index,int bind)
    {
        int previous=ActionBinding(index);if(previous==bind)return;
        for(int i=0;i<9;i++)if(i!=index&&ActionBinding(i)==bind)SetActionBinding(i,previous);
        SetActionBinding(index,bind);
    }
    public bool UsesSource(int source)=>((Jump|Crouch|Reload|Switch|Aim|Shoot|Tactical|Lethal|MeleePing)&source)!=0;
    public bool NormalizeConflicts()
    {
        int[] defaults={0x1000,0x2000,0x4000,0x8000,LT,RT,0x0100,0x0200,0x0080};
        var groups=Enumerable.Range(0,9).GroupBy(ActionBinding).Where(g=>g.Count()>1).Select(g=>g.ToArray()).ToArray();
        foreach(var group in groups)
        {
            int winner=group.FirstOrDefault(i=>ActionBinding(i)!=defaults[i],group[0]);
            foreach(int index in group.Where(i=>i!=winner))
            {
                var used=Enumerable.Range(0,9).Select(ActionBinding).ToHashSet();
                int replacement=!used.Contains(defaults[winner])?defaults[winner]:!used.Contains(defaults[index])?defaults[index]:defaults.First(d=>!used.Contains(d));
                SetActionBinding(index,replacement);
            }
        }
        return groups.Length>0;
    }

    /// <summary>任意绑定值是否命中当前输入：按钮位直接比对，扳机虚拟位按阈值（&gt;10）判定。</summary>
    public static bool Hit(int bind, ushort wButtons, byte lt, byte rt)
        => (bind & wButtons) != 0
        || ((bind & LT) != 0 && lt > 10)
        || ((bind & RT) != 0 && rt > 10);

    /// <summary>绑定值 → 可读键名（动作绑定 / 键位修正显示共用）。支持 方向键/视图/Option/L3/R3/A/B/X/Y/LB/RB/LT/RT。</summary>
    public static string BitName(int bit) => bit switch
    {
        0x0001 => "D-Up",
        0x0002 => "D-Down",
        0x0004 => "D-Left",
        0x0008 => "D-Right",
        0x0010 => "Option",
        0x0020 => "View",
        0x0040 => "L3",
        0x0080 => "R3",
        0x0100 => "LB",
        0x0200 => "RB",
        0x0400 => "Logo",
        0x1000 => "A",
        0x2000 => "B",
        0x4000 => "X",
        0x8000 => "Y",
        LT => "LT",
        RT => "RT",
        _ => "?",
    };

    /// <summary>组合掩码显示名：逐位拆出按钮位名称，按 + 拼接（如 "LB+RB"）；0 或未知位显示 "?"。</summary>
    public static string ComboName(ushort mask)
    {
        if (mask == 0) return "?";
        var names = new List<string>();
        for (int bit = 0x0001; bit <= 0x8000; bit <<= 1)
            if ((mask & bit) != 0) names.Add(BitName(bit));
        return names.Count > 0 ? string.Join("+", names) : "?";
    }
}
