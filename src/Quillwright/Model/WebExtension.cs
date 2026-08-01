namespace Quillwright.Model;

/// <summary>One setting a web extension stores in the document.</summary>
/// <param name="Name">Name the add-in knows the setting by.</param>
/// <param name="Value">What it is set to.</param>
public readonly record struct WebExtensionProperty(string Name, string? Value);

/// <summary>Where a task pane sits and how large it is.</summary>
public sealed class TaskPaneSettings
{
    /// <summary>Which edge the pane is docked to, as the document spells it.</summary>
    public string? DockState { get; init; }

    /// <summary>Whether the pane is shown when the document opens.</summary>
    public bool IsVisible { get; init; }

    /// <summary>Width of the pane, in the units the document stores.</summary>
    public int? Width { get; init; }

    /// <summary>Which row of panes this one sits in.</summary>
    public int? Row { get; init; }
}

/// <summary>
/// A web extension the document carries — an Office add-in and the state it saved with the
/// file ([MS-OWEXML]).
/// </summary>
/// <remarks>
/// <para>
/// Read only: the extension parts are copied through untouched on save, so a document with an
/// add-in in it keeps the add-in. What is added here is a typed view, so a caller can tell
/// which add-ins a document will try to load and what it saved for them, without parsing the
/// parts itself.
/// </para>
/// <para>
/// This is the in-document half of the story. What the add-in <em>is</em> — its display name,
/// its start page, the permissions it asks for — lives in a manifest ([MS-OWEMXML]) that is
/// distributed through a catalogue and is never written into the document, so none of it can
/// be recovered from the package. <see cref="Quillwright.IO.OfficeAddInManifestReader"/> reads
/// such a manifest when you have one to hand.
/// </para>
/// </remarks>
public sealed class WebExtension
{
    /// <summary>Absolute name of the part describing the extension.</summary>
    public required string PartPath { get; init; }

    /// <summary>Identifier of this instance of the extension in this document.</summary>
    public string? Id { get; init; }

    /// <summary>Identifier the store knows the add-in by.</summary>
    public string? StoreId { get; init; }

    /// <summary>Which version of the add-in the document was saved against.</summary>
    public string? Version { get; init; }

    /// <summary>The store the add-in came from.</summary>
    public string? Store { get; init; }

    /// <summary>What kind of store that is — the public catalogue, a file share, a registry key.</summary>
    public string? StoreType { get; init; }

    /// <summary>The state the add-in saved with the document.</summary>
    public IReadOnlyList<WebExtensionProperty> Properties { get; init; } = [];

    /// <summary>Where the extension's task pane sits, when the document shows it in one.</summary>
    public TaskPaneSettings? TaskPane { get; init; }
}
