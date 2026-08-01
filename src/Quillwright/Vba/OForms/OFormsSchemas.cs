namespace Quillwright.Vba.OForms;

/// <summary>
/// One table per control saying what each bit of its property mask means and how much room
/// the value takes ([MS-OFORMS] 2.2).
/// </summary>
/// <remarks>
/// <para>
/// The tables are complete in their sizes and deliberately incomplete in their names: every
/// bit is listed, because a bit left out would shift every later property, but only the
/// properties this library surfaces are given a name to record them under. The rest are read
/// and thrown away, which is all that is needed to keep the cursor in step.
/// </para>
/// <para>
/// The order is the order of the bits, lowest first, which is also the order the values are
/// written in each of the three blocks.
/// </para>
/// </remarks>
internal static class OFormsSchemas
{
    /// <summary>Name of the caption property, on the controls that have one.</summary>
    public const string Caption = "Caption";

    /// <summary>Name of the value property, on the six controls that share the MorphData record.</summary>
    public const string Value = "Value";

    /// <summary>Name of the size property, which every visible control stores.</summary>
    public const string Size = "Size";

    /// <summary>Name of the option-button grouping property.</summary>
    public const string GroupName = "GroupName";

    /// <summary>Name of the property that tells the six MorphData controls apart.</summary>
    public const string DisplayStyle = "DisplayStyle";

    /// <summary>Name of the form property holding the size the designer shows.</summary>
    public const string DisplayedSize = "DisplayedSize";

    /// <summary>The boolean bits of a form, one of which says whether a class table follows.</summary>
    public const string BooleanProperties = "BooleanProperties";

    /// <summary>A form's background colour.</summary>
    public const string BackColor = "BackColor";

    /// <summary>A form's foreground colour.</summary>
    public const string ForeColor = "ForeColor";

    /// <summary>A site's own name, which is what the code behind the form refers to it by.</summary>
    public const string Name = "Name";

    /// <summary>A site's identifier, which also names the storage of an embedded parent.</summary>
    public const string Id = "ID";

    /// <summary>Where a site sits inside its parent.</summary>
    public const string Position = "Position";

    /// <summary>A site's place in the tab order.</summary>
    public const string TabIndex = "TabIndex";

    /// <summary>Which control a site holds.</summary>
    public const string ClsidCacheIndex = "ClsidCacheIndex";

    /// <summary>How many bytes of the object stream the site's control occupies.</summary>
    public const string ObjectStreamSize = "ObjectStreamSize";

    /// <summary>The tooltip a site shows.</summary>
    public const string Tooltip = "ControlTipText";

    /// <summary>The cell or field a site is bound to.</summary>
    public const string ControlSource = "ControlSource";

    /// <summary>The range a list site takes its items from.</summary>
    public const string RowSource = "RowSource";

    /// <summary>Which group of option buttons a site belongs to.</summary>
    public const string GroupId = "GroupID";

    /// <summary>The identifiers of the pages of a multi-page control, in the order they are shown.</summary>
    public const string PageIds = "PageIDs";

    /// <summary>A form, a frame or a page ([MS-OFORMS] 2.2.10.1).</summary>
    public static OFormsSchema Form { get; } = new("FormControl", 4,
    [
        OFormsProperty.Flag(),
        OFormsProperty.Data(BackColor, 4),
        OFormsProperty.Data(ForeColor, 4),
        OFormsProperty.Data("NextAvailableID", 4),
        OFormsProperty.Flag(),
        OFormsProperty.Flag(),
        OFormsProperty.Data(BooleanProperties, 4),
        OFormsProperty.Data("BorderStyle", 1),
        OFormsProperty.Data("MousePointer", 1),
        OFormsProperty.Data("ScrollBars", 1),
        OFormsProperty.Pair(DisplayedSize),
        OFormsProperty.Pair("LogicalSize"),
        OFormsProperty.Pair("ScrollPosition"),
        OFormsProperty.Data("GroupCnt", 4),
        OFormsProperty.Flag(),
        new OFormsProperty("MouseIcon", OFormsSlot.Picture),
        OFormsProperty.Data("Cycle", 1),
        OFormsProperty.Data("SpecialEffect", 1),
        OFormsProperty.Data("BorderColor", 4),
        OFormsProperty.Text(Caption),
        new OFormsProperty("Font", OFormsSlot.Font),
        new OFormsProperty("Picture", OFormsSlot.Picture),
        OFormsProperty.Data("Zoom", 4),
        OFormsProperty.Data("PictureAlignment", 1),
        OFormsProperty.Flag("PictureTiling"),
        OFormsProperty.Data("PictureSizeMode", 1),
        OFormsProperty.Data("ShapeCookie", 4),
        OFormsProperty.Data("DrawBuffer", 4),
    ]);

    /// <summary>One embedded control's entry in its parent's form stream ([MS-OFORMS] 2.2.10.12.1).</summary>
    public static OFormsSchema Site { get; } = new("OleSiteConcreteControl", 4,
    [
        OFormsProperty.Text(Name),
        OFormsProperty.Text("Tag"),
        OFormsProperty.Data(Id, 4),
        OFormsProperty.Data("HelpContextID", 4),
        OFormsProperty.Data("BitFlags", 4),
        OFormsProperty.Data(ObjectStreamSize, 4),
        OFormsProperty.Data(TabIndex, 2),
        OFormsProperty.Data(ClsidCacheIndex, 2),
        OFormsProperty.Pair(Position),
        OFormsProperty.Data(GroupId, 2),
        OFormsProperty.Flag(),
        OFormsProperty.Text(Tooltip),
        OFormsProperty.Text("RuntimeLicKey"),
        OFormsProperty.Text(ControlSource),
        OFormsProperty.Text(RowSource),
    ]);

    /// <summary>A push button ([MS-OFORMS] 2.2.1.1).</summary>
    public static OFormsSchema CommandButton { get; } = new("CommandButtonControl", 4,
    [
        OFormsProperty.Data(ForeColor, 4),
        OFormsProperty.Data(BackColor, 4),
        OFormsProperty.Data("VariousPropertyBits", 4),
        OFormsProperty.Text(Caption),
        OFormsProperty.Data("PicturePosition", 4),
        OFormsProperty.Pair(Size),
        OFormsProperty.Data("MousePointer", 1),
        new OFormsProperty("Picture", OFormsSlot.Picture),
        OFormsProperty.Data("Accelerator", 2),
        OFormsProperty.Flag("TakeFocusOnClick"),
        new OFormsProperty("MouseIcon", OFormsSlot.Picture),
    ]);

    /// <summary>A caption beside another control ([MS-OFORMS] 2.2.4.1).</summary>
    public static OFormsSchema Label { get; } = new("LabelControl", 4,
    [
        OFormsProperty.Data(ForeColor, 4),
        OFormsProperty.Data(BackColor, 4),
        OFormsProperty.Data("VariousPropertyBits", 4),
        OFormsProperty.Text(Caption),
        OFormsProperty.Data("PicturePosition", 4),
        OFormsProperty.Pair(Size),
        OFormsProperty.Data("MousePointer", 1),
        OFormsProperty.Data("BorderColor", 4),
        OFormsProperty.Data("BorderStyle", 2),
        OFormsProperty.Data("SpecialEffect", 2),
        new OFormsProperty("Picture", OFormsSlot.Picture),
        OFormsProperty.Data("Accelerator", 2),
        new OFormsProperty("MouseIcon", OFormsSlot.Picture),
    ]);

    /// <summary>A picture ([MS-OFORMS] 2.2.3.1).</summary>
    public static OFormsSchema Image { get; } = new("ImageControl", 4,
    [
        OFormsProperty.Flag(),
        OFormsProperty.Flag(),
        OFormsProperty.Flag("AutoSize"),
        OFormsProperty.Data("BorderColor", 4),
        OFormsProperty.Data(BackColor, 4),
        OFormsProperty.Data("BorderStyle", 1),
        OFormsProperty.Data("MousePointer", 1),
        OFormsProperty.Data("PictureSizeMode", 1),
        OFormsProperty.Data("SpecialEffect", 1),
        OFormsProperty.Pair(Size),
        new OFormsProperty("Picture", OFormsSlot.Picture),
        OFormsProperty.Data("PictureAlignment", 1),
        OFormsProperty.Flag("PictureTiling"),
        OFormsProperty.Data("VariousPropertyBits", 4),
        new OFormsProperty("MouseIcon", OFormsSlot.Picture),
    ]);

    /// <summary>
    /// A text box, list, combo, check box, option button or toggle — six controls with one
    /// record between them ([MS-OFORMS] 2.2.5.1), and the only mask that needs eight bytes.
    /// </summary>
    public static OFormsSchema MorphData { get; } = new("MorphDataControl", 8,
    [
        OFormsProperty.Data("VariousPropertyBits", 4),
        OFormsProperty.Data(BackColor, 4),
        OFormsProperty.Data(ForeColor, 4),
        OFormsProperty.Data("MaxLength", 4),
        OFormsProperty.Data("BorderStyle", 1),
        OFormsProperty.Data("ScrollBars", 1),
        OFormsProperty.Data(DisplayStyle, 1),
        OFormsProperty.Data("MousePointer", 1),
        OFormsProperty.Pair(Size),
        OFormsProperty.Data("PasswordChar", 2),
        OFormsProperty.Data("ListWidth", 4),
        OFormsProperty.Data("BoundColumn", 2),
        OFormsProperty.Data("TextColumn", 2),
        OFormsProperty.Data("ColumnCount", 2),
        OFormsProperty.Data("ListRows", 2),
        OFormsProperty.Data("cColumnInfo", 2),
        OFormsProperty.Data("MatchEntry", 1),
        OFormsProperty.Data("ListStyle", 1),
        OFormsProperty.Data("ShowDropButtonWhen", 1),
        OFormsProperty.Flag(),
        OFormsProperty.Data("DropButtonStyle", 1),
        OFormsProperty.Data("MultiSelect", 1),
        OFormsProperty.Text(Value),
        OFormsProperty.Text(Caption),
        OFormsProperty.Data("PicturePosition", 4),
        OFormsProperty.Data("BorderColor", 4),
        OFormsProperty.Data("SpecialEffect", 4),
        new OFormsProperty("MouseIcon", OFormsSlot.Picture),
        new OFormsProperty("Picture", OFormsSlot.Picture),
        OFormsProperty.Data("Accelerator", 2),
        OFormsProperty.Flag(),
        OFormsProperty.Flag(),
        OFormsProperty.Text(GroupName),
    ]);

    /// <summary>A scroll bar ([MS-OFORMS] 2.2.7.1).</summary>
    public static OFormsSchema ScrollBar { get; } = new("ScrollBarControl", 4,
    [
        OFormsProperty.Data(ForeColor, 4),
        OFormsProperty.Data(BackColor, 4),
        OFormsProperty.Data("VariousPropertyBits", 4),
        OFormsProperty.Pair(Size),
        OFormsProperty.Data("MousePointer", 1),
        OFormsProperty.Data("Min", 4),
        OFormsProperty.Data("Max", 4),
        OFormsProperty.Data(Value, 4),
        OFormsProperty.Flag(),
        OFormsProperty.Data("PrevEnabled", 4),
        OFormsProperty.Data("NextEnabled", 4),
        OFormsProperty.Data("SmallChange", 4),
        OFormsProperty.Data("LargeChange", 4),
        OFormsProperty.Data("Orientation", 4),
        OFormsProperty.Data("ProportionalThumb", 2),
        OFormsProperty.Data("Delay", 4),
        new OFormsProperty("MouseIcon", OFormsSlot.Picture),
    ]);

    /// <summary>A pair of arrows that step a number ([MS-OFORMS] 2.2.8.1).</summary>
    public static OFormsSchema SpinButton { get; } = new("SpinButtonControl", 4,
    [
        OFormsProperty.Data(ForeColor, 4),
        OFormsProperty.Data(BackColor, 4),
        OFormsProperty.Data("VariousPropertyBits", 4),
        OFormsProperty.Pair(Size),
        OFormsProperty.Flag(),
        OFormsProperty.Data("Min", 4),
        OFormsProperty.Data("Max", 4),
        OFormsProperty.Data(Value, 4),
        OFormsProperty.Data("PrevEnabled", 4),
        OFormsProperty.Data("NextEnabled", 4),
        OFormsProperty.Data("SmallChange", 4),
        OFormsProperty.Data("Orientation", 4),
        OFormsProperty.Data("Delay", 4),
        new OFormsProperty("MouseIcon", OFormsSlot.Picture),
        OFormsProperty.Data("MousePointer", 1),
    ]);

    /// <summary>
    /// A row of tabs ([MS-OFORMS] 2.2.9.1), which is also how a multi-page control writes
    /// itself into its parent's object stream.
    /// </summary>
    public static OFormsSchema TabStrip { get; } = new("TabStripControl", 4,
    [
        OFormsProperty.Data("ListIndex", 4),
        OFormsProperty.Data(BackColor, 4),
        OFormsProperty.Data(ForeColor, 4),
        OFormsProperty.Flag(),
        OFormsProperty.Pair(Size),
        OFormsProperty.Array("Items"),
        OFormsProperty.Data("MousePointer", 1),
        OFormsProperty.Flag(),
        OFormsProperty.Data("TabOrientation", 4),
        OFormsProperty.Data("TabStyle", 4),
        OFormsProperty.Flag("MultiRow"),
        OFormsProperty.Data("TabFixedWidth", 4),
        OFormsProperty.Data("TabFixedHeight", 4),
        OFormsProperty.Flag("Tooltips"),
        OFormsProperty.Flag(),
        OFormsProperty.Array("TipStrings"),
        OFormsProperty.Flag(),
        OFormsProperty.Array("TabNames"),
        OFormsProperty.Data("VariousPropertyBits", 4),
        OFormsProperty.Flag("NewVersion"),
        OFormsProperty.Data("TabsAllocated", 4),
        OFormsProperty.Array("Tags"),
        OFormsProperty.Data("TabData", 4),
        OFormsProperty.Array("Accelerators"),
        new OFormsProperty("MouseIcon", OFormsSlot.Picture),
    ]);

    /// <summary>
    /// The record a multi-page control keeps beside its pages ([MS-OFORMS] 2.2.6.1). The array
    /// of page identifiers that follows it is read separately, because it is sized by a value
    /// inside the record rather than by the mask.
    /// </summary>
    public static OFormsSchema MultiPage { get; } = new("MultiPageProperties", 4,
    [
        OFormsProperty.Flag(),
        OFormsProperty.Data("PageCount", 4),
        OFormsProperty.Data(Id, 4),
        OFormsProperty.Flag("Flags"),
    ]);

    /// <summary>One page's entry in the extended stream of its multi-page ([MS-OFORMS] 2.2.6.4.1).</summary>
    public static OFormsSchema Page { get; } = new("PageProperties", 4,
    [
        OFormsProperty.Flag(),
        OFormsProperty.Data("TransitionEffect", 4),
        OFormsProperty.Data("TransitionPeriod", 4),
    ]);
}
