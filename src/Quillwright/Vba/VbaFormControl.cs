using System.Globalization;
using Quillwright.Primitives;

namespace Quillwright.Vba;

/// <summary>What kind of control a form holds.</summary>
public enum VbaFormControlKind : byte
{
    /// <summary>A control this version does not recognise, or one supplied by another vendor.</summary>
    Unknown = 0,

    /// <summary>The form itself.</summary>
    Form,

    /// <summary>A box that groups other controls.</summary>
    Frame,

    /// <summary>One page of a multi-page control.</summary>
    Page,

    /// <summary>A stack of pages with tabs along one edge.</summary>
    MultiPage,

    /// <summary>A row of tabs with no pages of its own.</summary>
    TabStrip,

    /// <summary>Text the user cannot edit.</summary>
    Label,

    /// <summary>A box the user types into.</summary>
    TextBox,

    /// <summary>A list of values to choose from.</summary>
    ListBox,

    /// <summary>A text box with a list attached.</summary>
    ComboBox,

    /// <summary>A box that is ticked or not.</summary>
    CheckBox,

    /// <summary>One of a group of mutually exclusive choices.</summary>
    OptionButton,

    /// <summary>A button that stays pressed.</summary>
    ToggleButton,

    /// <summary>A button that runs code when clicked.</summary>
    CommandButton,

    /// <summary>A picture.</summary>
    Image,

    /// <summary>A bar that scrolls a value between two bounds.</summary>
    ScrollBar,

    /// <summary>A pair of arrows that step a value.</summary>
    SpinButton,
}

/// <summary>
/// A control that can hold other controls: the form itself, a frame, or one page of a
/// multi-page ([MS-OFORMS] 2.1.2.1).
/// </summary>
/// <remarks>
/// Each of these is a storage of its own in the project, holding a stream that describes the
/// container and another holding the controls inside it. Reading is one way: the layout is
/// decoded for inspection and never written back.
/// </remarks>
public sealed class VbaFormControl
{
    private readonly List<VbaFormControlSite> _controls;

    internal VbaFormControl(VbaFormControlKind kind, List<VbaFormControlSite> controls)
    {
        Kind = kind;
        _controls = controls;
    }

    /// <summary>Whether this is the form, a frame or a page.</summary>
    public VbaFormControlKind Kind { get; }

    /// <summary>The title the container shows, when it has one.</summary>
    public string? Caption { get; internal init; }

    /// <summary>How wide the designer left the container.</summary>
    public Length Width { get; internal init; }

    /// <summary>How tall the designer left the container.</summary>
    public Length Height { get; internal init; }

    /// <summary>
    /// The background colour as an <c>OLE_COLOR</c> ([MS-OFORMS] 2.4.9): three colour bytes and
    /// a fourth saying whether they are a colour or an index into a palette.
    /// </summary>
    public uint? BackColor { get; internal init; }

    /// <summary>The foreground colour, in the same form as <see cref="BackColor"/>.</summary>
    public uint? ForeColor { get; internal init; }

    /// <summary>The controls inside, in the order the form stores them.</summary>
    public IReadOnlyList<VbaFormControlSite> Controls => _controls;

    /// <summary>Every control inside, including those nested in frames and pages.</summary>
    public IEnumerable<VbaFormControlSite> AllControls
    {
        get
        {
            foreach (VbaFormControlSite site in Controls)
            {
                yield return site;
                if (site.Child is not { } child)
                    continue;

                foreach (VbaFormControlSite nested in child.AllControls)
                    yield return nested;
            }
        }
    }

    /// <summary>Puts the controls into the order a multi-page shows its pages in.</summary>
    /// <param name="order">The controls, in the order they should appear.</param>
    internal void Reorder(IEnumerable<VbaFormControlSite> order)
    {
        var reordered = order.ToList();
        _controls.Clear();
        _controls.AddRange(reordered);
    }

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Kind} \"{Caption}\" with {Controls.Count} control(s)");
}

/// <summary>
/// One control placed on a form ([MS-OFORMS] 2.2.10.12): where it sits, what it is, and what
/// the designer put in it.
/// </summary>
/// <remarks>
/// A control is described in two places at once. Its parent's form stream says where it is,
/// what it is called and what type it has; the control's own record, in the object stream
/// beside it or in a storage of its own, says what it looks like and what it holds. Both are
/// read into this one object.
/// </remarks>
public sealed class VbaFormControlSite
{
    internal VbaFormControlSite(string name, VbaFormControlKind kind)
    {
        Name = name;
        Kind = kind;
    }

    /// <summary>The name the code behind the form refers to the control by.</summary>
    public string Name { get; }

    /// <summary>What kind of control this is.</summary>
    public VbaFormControlKind Kind { get; internal set; }

    /// <summary>
    /// The identifier the form gave the control, which also names the storage of a control
    /// that holds others.
    /// </summary>
    public int Id { get; internal init; }

    /// <summary>How far the control sits from the left edge of its container.</summary>
    public Length Left { get; internal init; }

    /// <summary>How far the control sits below the top edge of its container.</summary>
    public Length Top { get; internal init; }

    /// <summary>How wide the control is.</summary>
    public Length Width { get; internal set; }

    /// <summary>How tall the control is.</summary>
    public Length Height { get; internal set; }

    /// <summary>Where the control comes in the tab order of its container.</summary>
    public int TabIndex { get; internal init; }

    /// <summary>Which group of option buttons the control belongs to, or zero for none.</summary>
    public int GroupId { get; internal init; }

    /// <summary>The text shown when the pointer rests on the control.</summary>
    public string? Tooltip { get; internal init; }

    /// <summary>The cell or field the control reads and writes, for a control that is bound.</summary>
    public string? ControlSource { get; internal init; }

    /// <summary>Where a list control takes its items from.</summary>
    public string? RowSource { get; internal init; }

    /// <summary>The text written on the control, for the kinds that carry one.</summary>
    public string? Caption { get; internal set; }

    /// <summary>
    /// What the control holds: the text of a box, the ticked state of a check box as
    /// <c>"0"</c> or <c>"1"</c>, or the position of a scroll bar.
    /// </summary>
    public string? Value { get; internal set; }

    /// <summary>Which set of option buttons the control is exclusive within.</summary>
    public string? GroupName { get; internal set; }

    /// <summary>The controls inside, when this one is a frame, a page or a multi-page.</summary>
    public VbaFormControl? Child { get; internal set; }

    /// <summary>How many bytes of the object stream this control's own record occupies.</summary>
    internal int ObjectStreamSize { get; init; }

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Kind} {Name} at {Left.Points:0.#},{Top.Points:0.#}pt");
}
