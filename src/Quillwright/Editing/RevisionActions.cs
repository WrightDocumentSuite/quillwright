using Quillwright.Model;

namespace Quillwright.Editing;

/// <summary>
/// Recording, accepting and rejecting tracked changes.
/// </summary>
/// <remarks>
/// A tracked edit lives in two places: as a wrapper over a stretch of text, and as a marker
/// on the paragraph mark that says whether the paragraph break itself was added or removed.
/// Resolving both is what makes accepting a deletion actually rejoin the paragraphs the
/// author meant to merge.
/// </remarks>
public static class RevisionActions
{
    /// <summary>
    /// Starts recording edits as tracked changes instead of applying them outright.
    /// </summary>
    /// <param name="document">Document to record into.</param>
    /// <param name="author">Who the changes are attributed to.</param>
    /// <param name="date">When they are recorded as made; defaults to now.</param>
    /// <returns>The session; disposing it stops recording.</returns>
    /// <exception cref="InvalidOperationException">The document is already recording.</exception>
    /// <example>
    /// <code>
    /// using (document.TrackChanges("Ada Lovelace"))
    ///     document.Replace("draft", "final");
    /// </code>
    /// </example>
    public static RevisionTracking TrackChanges(this WordDocument document, string author, DateTimeOffset? date = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(author);
        if (document.ActiveTracking is not null)
            throw new InvalidOperationException("The document is already recording tracked changes.");

        var tracking = new RevisionTracking(document, author, date ?? DateTimeOffset.UtcNow);
        document.ActiveTracking = tracking;
        document.Settings.TrackRevisions = true;
        return tracking;
    }

    /// <summary>
    /// Removes a paragraph, or records its removal when the document is recording changes.
    /// </summary>
    /// <param name="paragraph">The paragraph to remove.</param>
    /// <returns><see langword="true"/> when the paragraph was in a container.</returns>
    /// <remarks>
    /// A recorded removal leaves the paragraph where it is: its text is marked deleted and so
    /// is the paragraph mark, which is what tells a reader the break itself went and the two
    /// paragraphs became one. A paragraph this same session added goes for real, because
    /// recording that it came and went says nothing to anybody.
    /// </remarks>
    public static bool Delete(this Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        if (paragraph.Parent is not { } container)
            return false;

        if (paragraph.Document?.ActiveTracking is not { } tracking)
            return container.Blocks.Remove(paragraph);

        if (tracking.WasAdded(paragraph))
            return container.Blocks.Remove(paragraph);

        RevisionRecorder.Replace(paragraph, tracking, 0, paragraph.TextLength, default, null);
        paragraph.MarkFormat = paragraph.MarkFormat with
        {
            MarkRevisionXml = RevisionRecorder.Mark(RevisionKind.Deleted, tracking),
        };

        return true;
    }

    /// <summary>Applies every tracked change in the document and clears the markers.</summary>
    /// <param name="document">Document to change.</param>
    /// <returns>How many revisions were resolved.</returns>
    public static int AcceptAllRevisions(this WordDocument document) => Resolve(document, accept: true);

    /// <summary>Undoes every tracked change in the document and clears the markers.</summary>
    /// <param name="document">Document to change.</param>
    /// <returns>How many revisions were resolved.</returns>
    public static int RejectAllRevisions(this WordDocument document) => Resolve(document, accept: false);

    /// <summary>Whether the document holds any tracked change.</summary>
    /// <param name="document">Document to inspect.</param>
    public static bool HasRevisions(this WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.AllContainers
            .SelectMany(static container => container.Blocks.Paragraphs)
            .Any(static paragraph =>
                paragraph.Ranges.Any(static entry => entry.Range is Revision) ||
                paragraph.MarkFormat.MarkRevisionXml is not null);
    }

    private static int Resolve(WordDocument document, bool accept)
    {
        ArgumentNullException.ThrowIfNull(document);
        int resolved = 0;

        // Resolving is not itself an edit anybody wants recorded, so a session in progress
        // stands aside while the marks it made are applied or undone.
        RevisionTracking? tracking = document.ActiveTracking;
        document.ActiveTracking = null;
        try
        {
            foreach (BlockContainer container in document.AllContainers.ToArray())
                resolved += ResolveContainer(container, accept);
        }
        finally
        {
            document.ActiveTracking = tracking;
        }

        document.Settings.TrackRevisions = false;
        return resolved;
    }

    private static int ResolveContainer(BlockContainer container, bool accept)
    {
        int resolved = 0;
        foreach (Paragraph paragraph in container.Blocks.Paragraphs.ToArray())
            resolved += ResolveParagraph(paragraph, accept);

        resolved += MergeParagraphs(container, accept);

        foreach (Table table in container.Blocks.OfType<Table>().ToArray())
            resolved += ResolveTable(container, table, accept);

        return resolved;
    }

    /// <summary>
    /// Resolves the row marks of a table (<c>w:trPr/w:ins</c>, <c>w:trPr/w:del</c>): an
    /// accepted deletion or a rejected insertion takes the row out, and a table left with no
    /// rows goes with them, because a table of nothing is not what either author had.
    /// </summary>
    private static int ResolveTable(BlockContainer container, Table table, bool accept)
    {
        int resolved = 0;
        for (int i = table.Rows.Count - 1; i >= 0; i--)
        {
            TableRow row = table.Rows[i];
            if (row.Format.DeletedXml is not null)
            {
                resolved++;
                if (accept)
                {
                    table.Rows.RemoveAt(i);
                    continue;
                }

                row.Format = row.Format with { DeletedXml = null };
            }

            if (row.Format.InsertedXml is not null)
            {
                resolved++;
                if (!accept)
                {
                    table.Rows.RemoveAt(i);
                    continue;
                }

                row.Format = row.Format with { InsertedXml = null };
            }
        }

        if (table.Rows.Count == 0)
            container.Blocks.Remove(table);

        return resolved;
    }

    private static int ResolveParagraph(Paragraph paragraph, bool accept)
    {
        int resolved = 0;

        // Removals are applied from the end so earlier offsets stay valid.
        foreach ((int start, int length, InlineRange range) in paragraph.Ranges.OrderByDescending(static entry => entry.Start).ToArray())
        {
            if (range is not Revision revision)
                continue;

            resolved++;
            bool removeText = revision.Kind switch
            {
                RevisionKind.Deleted => accept,
                RevisionKind.MovedFrom => accept,
                _ => !accept,
            };

            paragraph.RemoveRange(range);
            if (removeText && length > 0)
            {
                paragraph.RemoveText(start, length);
                continue;
            }

            // Text that was marked deleted and is now staying has to stop reading as deleted:
            // a run that still says w:delText outside a w:del is a file Word refuses to open.
            if (length > 0 && revision.Kind is RevisionKind.Deleted or RevisionKind.MovedFrom)
                paragraph.SetDeletedRuns(start, length, deleted: false);
        }

        return resolved;
    }

    /// <summary>
    /// A paragraph whose mark went merges into the one after it: a deleted mark merges when
    /// the deletion is accepted — the author pressed Delete at the end of a line — and an
    /// inserted mark merges when the insertion is rejected, because rejecting the paragraph
    /// the author added has to take the break away along with the words.
    /// </summary>
    private static int MergeParagraphs(BlockContainer container, bool accept)
    {
        int merged = 0;
        for (int i = container.Blocks.Count - 1; i >= 0; i--)
        {
            if (container.Blocks[i] is not Paragraph paragraph || paragraph.MarkFormat.MarkRevisionXml is null)
                continue;

            bool breakGoes = accept ? IsDeletedMark(paragraph) : IsInsertedMark(paragraph);
            paragraph.MarkFormat = paragraph.MarkFormat with { MarkRevisionXml = null };
            merged++;

            if (!breakGoes || i + 1 >= container.Blocks.Count || container.Blocks[i + 1] is not Paragraph next)
                continue;

            paragraph.AppendText(next.Text, next.RunCount > 0 ? next.FormatAt(0) : null);
            paragraph.MarkFormat = next.MarkFormat;
            container.Blocks.RemoveAt(i + 1);
        }

        return merged;
    }

    private static bool IsDeletedMark(Paragraph paragraph) =>
        paragraph.MarkFormat.MarkRevisionXml is { } xml && xml.Contains("<w:del", StringComparison.Ordinal);

    private static bool IsInsertedMark(Paragraph paragraph) =>
        paragraph.MarkFormat.MarkRevisionXml is { } xml && xml.Contains("<w:ins", StringComparison.Ordinal);
}
