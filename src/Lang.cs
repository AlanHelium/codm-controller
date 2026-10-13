namespace CODMControllerProbe;

/// <summary>
/// 界面语言：0=中文（默认） 1=English。静态当前语言 + 双语选择器。
/// 所有界面文本统一走 Lang.T(zh, en)，切换后由各窗体重新填充文本。
/// </summary>
public static class Lang
{
    /// <summary>当前界面语言：false=中文 true=English。</summary>
    public static bool En { get; set; }

    /// <summary>按当前语言返回文本。</summary>
    public static string T(string zh, string en) => En ? en : zh;

    /// <summary>语言切换按钮显示：当前语言 → 目标语言缩写。</summary>
    public static string SwitchLabel => En ? "中文" : "EN";
}
