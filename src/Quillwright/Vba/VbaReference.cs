namespace Quillwright.Vba;

/// <summary>What kind of thing a project reference points at.</summary>
public enum VbaReferenceKind
{
    /// <summary>A registered Automation type library, named by its identifier.</summary>
    Registered,

    /// <summary>Another VBA project, named by the path of the file holding it.</summary>
    Project,

    /// <summary>The type library of an ActiveX control, which a user form always brings with it.</summary>
    Control,
}

/// <summary>
/// One external library a VBA project depends on ([MS-OVBA] 2.3.4.2.2).
/// </summary>
/// <remarks>
/// Worth reading when the question is what a document's macros can reach. A reference to
/// <c>Scripting</c> means the file system is in play, one to a project means another workbook or
/// document has to be present for the code to run at all.
/// </remarks>
/// <param name="name">The name the editor shows for the reference.</param>
/// <param name="libid">The identifier the reference resolves through.</param>
/// <param name="kind">What kind of thing is referenced.</param>
public sealed class VbaReference(string name, string libid, VbaReferenceKind kind)
{
    /// <summary>The name the editor shows, such as <c>stdole</c> or <c>MSForms</c>.</summary>
    public string Name { get; } = name;

    /// <summary>
    /// The identifier the reference resolves through. For a library this carries its class
    /// identifier, version and path; for a project, the path of the file holding it.
    /// </summary>
    public string Libid { get; } = libid;

    /// <summary>What kind of thing is referenced.</summary>
    public VbaReferenceKind Kind { get; } = kind;

    /// <summary>
    /// For a control whose type library was generated from a registered one, the identifier of
    /// the registered library ([MS-OVBA] 2.3.4.2.2.4). Worth having because <see cref="Libid"/>
    /// then names a generated cache file in a temporary directory rather than the library
    /// itself. <see langword="null"/> when the reference names no original.
    /// </summary>
    public string? OriginalLibid { get; init; }

    /// <summary>
    /// The human-readable tail of the identifier, which for a registered library is its
    /// description — <c>OLE Automation</c>, <c>Microsoft Office 16.0 Object Library</c>.
    /// </summary>
    public string Description
    {
        get
        {
            int last = Libid.LastIndexOf('#');
            return last >= 0 && last < Libid.Length - 1 ? Libid[(last + 1)..] : Name;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} ({Kind})";
}
