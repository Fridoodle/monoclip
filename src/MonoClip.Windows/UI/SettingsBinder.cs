using MonoClip.Core;
namespace MonoClip.Windows.UI;

// The settings the popup edits, as the single source of truth. Each control is bound to one setting: a user edit
// updates Draft, and Push() shows Draft in every bound control, so one setting can have a control on each page.
internal sealed class SettingsBinder(AppSettings start)
{
    readonly List<Action> pushes = []; bool pushing;
    public AppSettings Draft { get; set; } = start;
    // Raised after a user edit (not when Push() fills the controls).
    public event Action? Edited;

    public void Toggle(CheckBox box, Func<AppSettings, bool> get, Func<AppSettings, bool, AppSettings> set) =>
        Bind(() => box.Checked = get(Draft), h => box.CheckedChanged += h, s => set(s, box.Checked));
    public void Number(NumericUpDown number, Func<AppSettings, int> get, Func<AppSettings, int, AppSettings> set) =>
        Bind(() => number.Value = Math.Clamp(get(Draft), number.Minimum, number.Maximum), h => number.ValueChanged += h, s => set(s, (int)number.Value));
    public void Text(TextBox box, Func<AppSettings, string> get, Func<AppSettings, string, AppSettings> set) =>
        Bind(() => box.Text = get(Draft), h => box.TextChanged += h, s => set(s, box.Text));
    public void Steps(MonoSlider slider, Func<AppSettings, int> get, Func<AppSettings, int, AppSettings> set) =>
        Bind(() => slider.Value = get(Draft), h => slider.ValueChanged += h, s => set(s, slider.Value));
    // A drop-down over a fixed list of values (only valid settings reach the popup, so every value is listed).
    public void Choice<T>(ComboBox combo, IReadOnlyList<(string Label, T Value)> options, Func<AppSettings, T> get, Func<AppSettings, T, AppSettings> set)
    {
        combo.Items.AddRange(options.Select(o => (object)o.Label).ToArray());
        Bind(() => combo.SelectedIndex = Math.Max(0, options.ToList().FindIndex(o => EqualityComparer<T>.Default.Equals(o.Value, get(Draft)))),
            h => combo.SelectedIndexChanged += h, s => combo.SelectedIndex < 0 ? s : set(s, options[combo.SelectedIndex].Value));
    }
    public void Volume(VolumeSlider slider, Func<AppSettings, int> get, Func<AppSettings, int, AppSettings> set) =>
        Bind(() => slider.Value = get(Draft), h => slider.ValueChanged += h, s => set(s, slider.Value));
    // A drop-down whose options change (audio devices come and go): Push() reads options() again. A chosen value that is
    // not listed (an unplugged device) stays chosen and is shown as unavailable.
    public void Devices(ComboBox combo, Func<IReadOnlyList<(string Label, string Value)>> options, Func<AppSettings, string> get, Func<AppSettings, string, AppSettings> set)
    {
        var shown = new List<(string Label, string Value)>();
        Bind(() =>
        {
            var current = get(Draft); var next = options().ToList();
            if (!next.Exists(o => o.Value == current)) next.Add(("Unavailable device", current));
            if (!next.SequenceEqual(shown)) { shown = next; combo.BeginUpdate(); combo.Items.Clear(); combo.Items.AddRange(next.Select(o => (object)o.Label).ToArray()); combo.EndUpdate(); }
            combo.SelectedIndex = shown.FindIndex(o => o.Value == current);
        }, h => combo.SelectedIndexChanged += h, s => combo.SelectedIndex < 0 ? s : set(s, shown[combo.SelectedIndex].Value));
    }
    public void Push() { pushing = true; try { foreach (var push in pushes) push(); } finally { pushing = false; } }
    void Bind(Action push, Action<EventHandler> subscribe, Func<AppSettings, AppSettings> edit)
    {
        pushes.Add(push);
        subscribe((_, _) => { if (pushing) return; Draft = edit(Draft); Push(); Edited?.Invoke(); });
    }
}
