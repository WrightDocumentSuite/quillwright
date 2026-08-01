using Quillwright.Model;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// The order blocks are placed in, and the groups of them that have to stay together.
/// </summary>
internal sealed partial class PageComposer
{
    /// <summary>
    /// Places a run of blocks one after another, keeping together the ones that asked to stay with
    /// what follows them. Each block is measured against the column it starts in.
    /// </summary>
    private void Flow(IEnumerable<Block> blocks)
    {
        List<Block> list = [.. Expand(blocks)];
        ParagraphBox? previous = null;

        for (int index = 0; index < list.Count;)
        {
            if (list[index] is not Paragraph)
            {
                PlaceOther(list[index]);
                previous = null;
                index++;
                continue;
            }

            List<ParagraphBox> group = Group(list, index);
            _reel?.AddRange(group);
            if (group.Count > 1)
                MakeRoomFor(group);

            foreach (ParagraphBox box in group)
            {
                PlaceParagraph(box, previous);
                previous = box;
            }

            index += group.Count;
        }
    }

    /// <summary>
    /// Measures the run of paragraphs that has to stay together: the one at <paramref name="index"/>
    /// and everything the chain of "keep with next" drags along behind it.
    /// </summary>
    private List<ParagraphBox> Group(List<Block> blocks, int index)
    {
        // A chain long enough to fill a page cannot be kept together anyway, so it is cut off
        // rather than measured in full — a document where every paragraph keeps with the next
        // would otherwise be laid out entirely before the first line was placed.
        const int Longest = 64;

        List<ParagraphBox> group = [];
        while (index < blocks.Count && blocks[index] is Paragraph paragraph && group.Count < Longest)
        {
            ParagraphBox box = _layouter.Layout(paragraph, CurrentWidth);
            group.Add(box);
            index++;

            if (!box.KeepWithNext)
                break;
        }

        return group;
    }

    /// <summary>Opens a column when a group that would fit in one does not fit in the room left.</summary>
    private void MakeRoomFor(List<ParagraphBox> group)
    {
        double total = 0;
        for (int i = 0; i < group.Count; i++)
            total += group[i].ContentHeight + (i == 0 ? 0 : group[i].SpacingBefore) + group[i].SpacingAfter;

        PageGeometry geometry = Current.Geometry;
        double bottom = Math.Min(NoteAwareBottom, _balanceBottom ?? double.MaxValue);
        if (_hasContent && _cursor + total > bottom && total <= geometry.ContentHeight)
            NewColumn();
    }

    /// <summary>
    /// Flattens the blocks a wrapper holds — a content control, or the branch of a compatibility
    /// block this version selected — which are laid out exactly as if the wrapper were not there.
    /// </summary>
    private static IEnumerable<Block> Expand(IEnumerable<Block> blocks)
    {
        foreach (Block block in blocks)
        {
            IEnumerable<Block>? inner = block switch
            {
                BlockContentControl control => control.Blocks,
                AlternateContentBlock alternate => alternate.Blocks,
                _ => null,
            };

            if (inner is null)
            {
                yield return block;
                continue;
            }

            foreach (Block nested in Expand(inner))
                yield return nested;
        }
    }
}
