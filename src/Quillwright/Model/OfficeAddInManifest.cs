namespace Quillwright.Model;

/// <summary>Which of the three kinds of add-in a manifest describes ([MS-OWEMXML] 2.1.1).</summary>
public enum OfficeAddInKind
{
    /// <summary>The manifest declares no <c>xsi:type</c>, or one this library does not know.</summary>
    Unknown,

    /// <summary>An add-in placed in the content of a document (2.2.24).</summary>
    ContentApp,

    /// <summary>An add-in shown in a task pane beside a document (2.2.29).</summary>
    TaskPaneApp,

    /// <summary>An add-in shown against a mail item (2.2.32).</summary>
    MailApp,
}

/// <summary>One locale's wording of a setting ([MS-OWEMXML] 2.2.1).</summary>
/// <param name="Locale">Culture name the wording is for, such as <c>en-US</c>.</param>
/// <param name="Value">What the setting says in that culture.</param>
public readonly record struct LocaleOverride(string Locale, string Value);

/// <summary>
/// A setting written once for the manifest's default locale and again for any other
/// ([MS-OWEMXML] 2.2.5).
/// </summary>
public sealed class LocaleAwareValue
{
    /// <summary>What the setting says in the locale <c>DefaultLocale</c> names.</summary>
    public string? DefaultValue { get; init; }

    /// <summary>The other locales it was written for, in the order the manifest lists them.</summary>
    public IReadOnlyList<LocaleOverride> Overrides { get; init; } = [];

    /// <summary>
    /// What the setting says in one culture, falling back to <see cref="DefaultValue"/>.
    /// </summary>
    /// <param name="locale">Culture name to look for, matched case-insensitively and in full.</param>
    /// <remarks>
    /// The match is exact: asking for <c>en-GB</c> will not fall back to an <c>en-US</c>
    /// override. Deciding that two cultures are near enough is the caller's business, and it is
    /// not something the specification lays down.
    /// </remarks>
    public string? For(string locale)
    {
        foreach (LocaleOverride wording in Overrides)
        {
            if (string.Equals(wording.Locale, locale, StringComparison.OrdinalIgnoreCase))
                return wording.Value;
        }

        return DefaultValue;
    }
}

/// <summary>
/// A page the add-in loads, and the surface it is the page for ([MS-OWEMXML] 2.2.9 to 2.2.13).
/// </summary>
/// <remarks>
/// A mail add-in gives a different page to the desktop, the tablet and the phone, and a 1.1
/// manifest gives a different one again per form. Each is kept with the path it was found at
/// so they cannot be mistaken for one another.
/// </remarks>
public sealed class OfficeAddInSourceLocation
{
    /// <summary>
    /// Where in the manifest this page was declared, as a path of element names below the root
    /// — <c>DefaultSettings</c>, <c>PhoneSettings</c>, <c>FormSettings/Form[ItemRead]/DesktopSettings</c>.
    /// </summary>
    public required string Context { get; init; }

    /// <summary>The address of the page, per locale.</summary>
    public required LocaleAwareValue Url { get; init; }

    /// <summary>Width in pixels the add-in asks for, when the surface takes one.</summary>
    public int? RequestedWidth { get; init; }

    /// <summary>Height in pixels the add-in asks for, when the surface takes one.</summary>
    public int? RequestedHeight { get; init; }
}

/// <summary>
/// A <c>VersionOverrides</c> subtree, kept as the markup it is ([MS-OWEMXML] 2.1.3 to 2.1.6).
/// </summary>
/// <remarks>
/// There are four of these vocabularies and this library models none of them. Handing back the
/// markup with the namespace that identifies it lets a caller that does understand one parse it,
/// and stops this reader from pretending to.
/// </remarks>
public sealed class OfficeAddInVersionOverrides
{
    /// <summary>The namespace that says which override vocabulary this is.</summary>
    public required string Namespace { get; init; }

    /// <summary>The subtree, namespace declarations included, exactly as it was written.</summary>
    public required string Markup { get; init; }
}

/// <summary>
/// What an Office add-in manifest says about itself ([MS-OWEMXML]).
/// </summary>
/// <remarks>
/// <para>
/// A deliberate subset: the elements the two base namespaces share, which is enough to say what
/// an add-in is called, who wrote it, where it loads from and what it is asking for. The
/// vocabularies layered on top — the activation rules of a mail add-in, the dictionary settings
/// of a task pane, everything inside <c>VersionOverrides</c> — are not modelled, and the
/// overrides are handed back as markup rather than silently dropped.
/// </para>
/// <para>
/// The manifest is not part of a document. It is distributed through an add-in catalogue, so no
/// package contains one and nothing here can find one for you; see
/// <c>WordDocument.WebExtensions</c> for the half of an add-in a document does carry.
/// </para>
/// </remarks>
public sealed class OfficeAddInManifest
{
    /// <summary>Which of the two base namespaces the manifest is written in.</summary>
    public required string Namespace { get; init; }

    /// <summary>Which kind of add-in the root element's <c>xsi:type</c> declares.</summary>
    public OfficeAddInKind Kind { get; init; }

    /// <summary>That <c>xsi:type</c> as it was written, so an unknown one is still readable.</summary>
    public string? DeclaredType { get; init; }

    /// <summary>The identifier the add-in is known by (2.3.5).</summary>
    public string? Id { get; init; }

    /// <summary>Which version of the add-in this manifest describes (2.3.8).</summary>
    public string? Version { get; init; }

    /// <summary>Who wrote it.</summary>
    public string? ProviderName { get; init; }

    /// <summary>The culture the manifest's unqualified wording is in (2.3.7).</summary>
    public string? DefaultLocale { get; init; }

    /// <summary>The short name the add-in is shown under.</summary>
    public LocaleAwareValue? DisplayName { get; init; }

    /// <summary>The longer description of what it does.</summary>
    public LocaleAwareValue? Description { get; init; }

    /// <summary>
    /// The applications it activates in, as the 1.1 namespace names them (2.2.40) — kept as
    /// written, so a host nobody has heard of yet is still reported.
    /// </summary>
    public IReadOnlyList<string> Hosts { get; init; } = [];

    /// <summary>The 1.0 way of saying the same thing (2.2.23), likewise kept as written.</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>The permission level it asks for (2.3.18), as written.</summary>
    public string? Permissions { get; init; }

    /// <summary>Every page the manifest declares, one per surface.</summary>
    public IReadOnlyList<OfficeAddInSourceLocation> SourceLocations { get; init; } = [];

    /// <summary>The override subtrees, unparsed.</summary>
    public IReadOnlyList<OfficeAddInVersionOverrides> VersionOverrides { get; init; } = [];
}
