namespace Quillwright.Vba.OForms;

/// <summary>
/// Turns the two numbers that between them name a control's type into one answer.
/// </summary>
/// <remarks>
/// A site says what it holds with <c>ClsidCacheIndex</c> ([MS-OFORMS] 2.4.5), an index into a
/// fixed table of the controls the format knows. Six of the visible controls share one entry
/// — a text box, a list, a combo, a check box, an option button and a toggle are all a
/// <c>MorphData</c>, told apart only by the <c>DisplayStyle</c> in the control's own record
/// ([MS-OFORMS] 2.5.20.1).
/// </remarks>
internal static class OFormsControlKind
{
    /// <summary>An index of 0x8000 or more names an entry of the form's class table instead.</summary>
    public const int FirstNonCachedIndex = 0x8000;

    /// <summary>What a cached class index stands for.</summary>
    /// <param name="clsidCacheIndex">The index a site carries.</param>
    public static VbaFormControlKind FromCacheIndex(int clsidCacheIndex) => clsidCacheIndex switch
    {
        7 => VbaFormControlKind.Form,
        12 => VbaFormControlKind.Image,
        14 => VbaFormControlKind.Frame,
        15 => VbaFormControlKind.TextBox,
        16 => VbaFormControlKind.SpinButton,
        17 => VbaFormControlKind.CommandButton,
        18 => VbaFormControlKind.TabStrip,
        21 => VbaFormControlKind.Label,
        23 => VbaFormControlKind.TextBox,
        24 => VbaFormControlKind.ListBox,
        25 => VbaFormControlKind.ComboBox,
        26 => VbaFormControlKind.CheckBox,
        27 => VbaFormControlKind.OptionButton,
        28 => VbaFormControlKind.ToggleButton,
        47 => VbaFormControlKind.ScrollBar,
        57 => VbaFormControlKind.MultiPage,
        _ => VbaFormControlKind.Unknown,
    };

    /// <summary>Which of the six controls a <c>MorphData</c> record turned out to be.</summary>
    /// <param name="displayStyle">The <c>DisplayStyle</c> the record carries.</param>
    public static VbaFormControlKind FromDisplayStyle(uint displayStyle) => displayStyle switch
    {
        1 => VbaFormControlKind.TextBox,
        2 => VbaFormControlKind.ListBox,
        3 => VbaFormControlKind.ComboBox,
        4 => VbaFormControlKind.CheckBox,
        5 => VbaFormControlKind.OptionButton,
        6 => VbaFormControlKind.ToggleButton,
        7 => VbaFormControlKind.ComboBox,
        _ => VbaFormControlKind.Unknown,
    };

    /// <summary>Whether a control is persisted as a storage of its own rather than in the object stream.</summary>
    /// <param name="kind">The kind of control.</param>
    public static bool IsParent(VbaFormControlKind kind) =>
        kind is VbaFormControlKind.Form or VbaFormControlKind.Frame or VbaFormControlKind.Page;

    /// <summary>
    /// The record layout a control is written with. The six MorphData controls share one, and
    /// a MultiPage is written as a TabStrip in the object stream of its parent.
    /// </summary>
    /// <param name="kind">The kind of control.</param>
    public static OFormsSchema? SchemaFor(VbaFormControlKind kind) => kind switch
    {
        VbaFormControlKind.CommandButton => OFormsSchemas.CommandButton,
        VbaFormControlKind.Label => OFormsSchemas.Label,
        VbaFormControlKind.Image => OFormsSchemas.Image,
        VbaFormControlKind.ScrollBar => OFormsSchemas.ScrollBar,
        VbaFormControlKind.SpinButton => OFormsSchemas.SpinButton,
        VbaFormControlKind.TabStrip or VbaFormControlKind.MultiPage => OFormsSchemas.TabStrip,
        VbaFormControlKind.TextBox or VbaFormControlKind.ListBox or VbaFormControlKind.ComboBox
            or VbaFormControlKind.CheckBox or VbaFormControlKind.OptionButton or VbaFormControlKind.ToggleButton =>
            OFormsSchemas.MorphData,
        _ => null,
    };
}
