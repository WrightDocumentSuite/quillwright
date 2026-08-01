namespace Quillwright.IO;

/// <summary>Who is editing one region of a shared document ([MS-WORDLFF] 2.4.3.7).</summary>
public sealed class CoAuthoringLock
{
    /// <summary>Identifier of the region.</summary>
    public required uint Id { get; init; }

    /// <summary>Identifier of the author on the machine they are editing from.</summary>
    public string? OwnerId { get; init; }

    /// <summary>The author's name, as they would like it shown.</summary>
    public string? OwnerName { get; init; }

    /// <summary>The account the author is signed in as.</summary>
    public string? OwnerUserName { get; init; }

    /// <summary>The author's email address, when the lock records one.</summary>
    public string? OwnerEmailAddress { get; init; }

    /// <summary>The address instant messaging would reach the author at.</summary>
    public string? OwnerSipAddress { get; init; }

    /// <summary>Identifiers of the paragraphs the region covers (<c>w14:paraId</c>).</summary>
    public IReadOnlyList<uint> Paragraphs { get; init; } = [];
}

/// <summary>An author whose details changed while the document was open ([MS-WORDLFF] 2.4.3.6).</summary>
public sealed class CoAuthoringLockOwner
{
    /// <summary>Identifier of the author on the machine they are editing from.</summary>
    public string? OwnerId { get; init; }

    /// <summary>The author's name, as they would like it shown.</summary>
    public string? OwnerName { get; init; }

    /// <summary>The account the author is signed in as.</summary>
    public string? OwnerUserName { get; init; }

    /// <summary>The author's email address, when the record carries one.</summary>
    public string? OwnerEmailAddress { get; init; }

    /// <summary>The address instant messaging would reach the author at.</summary>
    public string? OwnerSipAddress { get; init; }
}

/// <summary>
/// A region identifier nobody holds any more, and when it was given up
/// ([MS-WORDLFF] 2.4.3.3).
/// </summary>
/// <param name="Id">Identifier of the region.</param>
/// <param name="TimeStamp">
/// When the author withdrew, or <see langword="null"/> when the file states a time that is not
/// a schema <c>dateTime</c>. The identifier still counts as withdrawn either way.
/// </param>
public readonly record struct CoAuthoringDeletedLock(uint Id, DateTimeOffset? TimeStamp);

/// <summary>A request to renumber a document's identifiers ([MS-WORDLFF] 2.4.3.10).</summary>
/// <remarks>
/// Read only. Carrying the renumbering out — walking the parts in the order 2.4.3.10 lists and
/// reassigning <c>paraId</c>, <c>anchorId</c> and <c>editId</c> — is not implemented, because
/// it only means anything to a client that is also speaking the co-authoring protocol.
/// </remarks>
public sealed class CoAuthoringSync
{
    /// <summary>Identifier the document is to take, and the first identifier of the renumbering.</summary>
    public required uint DocumentId { get; init; }

    /// <summary>The identifier that follows the last one the renumbering used.</summary>
    public required uint NextId { get; init; }

    /// <summary>The base revision this request applies to.</summary>
    public string? RevisionId { get; init; }
}

/// <summary>
/// Everything one lock file records ([MS-WORDLFF] 2.4.3.1, <c>CT_CALocks</c>).
/// </summary>
/// <remarks>
/// <para>
/// Three of the nine child categories are locks: <see cref="Locks"/> is what the server has
/// accepted, while <see cref="UncommittedLocks"/> and <see cref="EphemeralLocks"/> are states a
/// client keeps to itself and never sends over the primary metadata channel. The rest is
/// bookkeeping about identifiers: which are spent, which may be reused and when, and whose
/// author details have changed.
/// </para>
/// <para>
/// Each collection is in the order the file lists it in. This is a structural view of the file
/// and nothing more: it is not applied to a <c>WordDocument</c>, and none of it is authored.
/// </para>
/// </remarks>
public sealed class CoAuthoringLocks
{
    private IReadOnlyList<CoAuthoringLock>? _effective;

    /// <summary>The renumbering request the file carries, when it carries one.</summary>
    public CoAuthoringSync? Sync { get; init; }

    /// <summary>Regions the server has accepted, deleted identifiers included.</summary>
    public IReadOnlyList<CoAuthoringLock> Locks { get; init; } = [];

    /// <summary>Regions a client is holding but has not sent.</summary>
    public IReadOnlyList<CoAuthoringLock> UncommittedLocks { get; init; } = [];

    /// <summary>Regions a client is holding for as long as the cursor is in them.</summary>
    public IReadOnlyList<CoAuthoringLock> EphemeralLocks { get; init; } = [];

    /// <summary>Identifiers that have been given up and must not be used for a region.</summary>
    public IReadOnlyList<CoAuthoringDeletedLock> DeletedLocks { get; init; } = [];

    /// <summary>
    /// The earliest withdrawal a <see cref="DeletedLocks"/> entry has to be kept for; anything
    /// older may be reused.
    /// </summary>
    public DateTimeOffset? IdPruneTime { get; init; }

    /// <summary>Identifiers a client has decided to stop using.</summary>
    public IReadOnlyList<uint> AutoDeletableLocks { get; init; } = [];

    /// <summary>Identifiers that stop being usable after the next save.</summary>
    public IReadOnlyList<uint> MakePlaceholder { get; init; } = [];

    /// <summary>Authors whose details changed since the file was last written.</summary>
    public IReadOnlyList<CoAuthoringLockOwner> UserInfoChanges { get; init; } = [];

    /// <summary>
    /// The committed regions that are actually held: <see cref="Locks"/> less the ones whose
    /// identifier has been given up, which 2.4.3.9 says MUST be ignored.
    /// </summary>
    public IReadOnlyList<CoAuthoringLock> Effective => _effective ??= WithoutDeleted();

    private IReadOnlyList<CoAuthoringLock> WithoutDeleted()
    {
        if (DeletedLocks.Count == 0)
            return Locks;

        HashSet<uint> withdrawn = [.. DeletedLocks.Select(static entry => entry.Id)];
        return [.. Locks.Where(held => !withdrawn.Contains(held.Id))];
    }
}
