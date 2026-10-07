namespace MonoClip.Windows.UI;

internal sealed class MonoMenuColors : ProfessionalColorTable
{
    public override Color MenuItemSelected => Color.FromArgb(48, 48, 48);
    public override Color MenuItemSelectedGradientBegin => MenuItemSelected;
    public override Color MenuItemSelectedGradientEnd => MenuItemSelected;
    public override Color MenuItemPressedGradientBegin => MenuItemSelected;
    public override Color MenuItemPressedGradientMiddle => MenuItemSelected;
    public override Color MenuItemPressedGradientEnd => MenuItemSelected;
    public override Color MenuItemBorder => Color.FromArgb(96, 96, 96);
    public override Color MenuBorder => Color.FromArgb(48, 48, 48);
    public override Color ToolStripDropDownBackground => Color.FromArgb(16, 16, 16);
    public override Color SeparatorDark => Color.FromArgb(64, 64, 64);
    public override Color SeparatorLight => SeparatorDark;
}
