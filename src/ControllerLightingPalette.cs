namespace CODMControllerProbe;
internal sealed record ControllerLightingPalette(Color Primary, Color Secondary)
{
    internal static readonly ControllerLightingPalette Default = new(Color.FromArgb(255,140,65), Color.FromArgb(75,130,220));
}
