using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Templates;

/// <summary>Carries the counters and the model while a template is being filled.</summary>
internal sealed class RenderState
{
    private readonly WordDocument _document;
    private readonly object _model;
    private readonly ITemplateBinder _binder;
    private readonly SortedSet<string> _unresolved = new(StringComparer.Ordinal);
    private int _filled;
    private int _repeated;
    private int _removed;

    public RenderState(WordDocument document, object model, ITemplateBinder binder)
    {
        _document = document;
        _model = model;
        _binder = binder;
    }

    public TemplateResult ToResult() => new(_filled, _repeated, _removed, [.. _unresolved]);

    /// <summary>
    /// Duplicates the regions bound to a collection: a table row whose placeholders are
    /// dotted with the collection name, or a block content control tagged <c>rows:</c>.
    /// </summary>
    public void ExpandRepeats(BlockContainer container)
    {
        foreach (Block block in container.Blocks.ToArray())
        {
            switch (block)
            {
                case Table table:
                    ExpandTable(table);
                    break;
                case BlockContentControl { Tag: { } tag } control
                    when tag.StartsWith(TemplateAnchors.RowsTagPrefix, StringComparison.OrdinalIgnoreCase):
                    ExpandBlockRegion(container, control, TemplateAnchors.StructuralName(tag));
                    break;
            }
        }
    }

    /// <summary>Keeps or drops the regions bound to a condition.</summary>
    public void ResolveConditions(BlockContainer container)
    {
        foreach (Block block in container.Blocks.ToArray())
        {
            if (block is not BlockContentControl { Tag: { } tag } control ||
                !tag.StartsWith(TemplateAnchors.ConditionTagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string name = TemplateAnchors.StructuralName(tag);
            bool keep = _binder.TryGetCondition(_model, name, out bool value) ? value : Unresolved(name, fallback: true);

            int index = container.Blocks.IndexOf(control);
            container.Blocks.RemoveAt(index);
            if (keep)
            {
                foreach (Block inner in control.Blocks.ToArray())
                    container.Blocks.Insert(index++, inner);
            }
            else
            {
                _removed++;
            }
        }
    }

    /// <summary>Fills every remaining anchor from a model.</summary>
    public void FillValues(BlockContainer container, object model, ITemplateBinder binder)
    {
        foreach (Paragraph paragraph in container.Blocks.Paragraphs.ToArray())
            FillParagraph(paragraph, model, binder);
    }

    /// <summary>Fills the anchors of one paragraph, working backwards so offsets stay valid.</summary>
    public void FillParagraph(Paragraph paragraph, object model, ITemplateBinder binder)
    {
        TemplateAnchor[] anchors = [.. TemplateAnchors.Find(paragraph)];
        for (int i = anchors.Length - 1; i >= 0; i--)
        {
            TemplateAnchor anchor = anchors[i];
            string name = TemplateAnchors.MemberOf(anchor.Name);

            if (binder.TryGetImage(model, name, out TemplateImage image))
            {
                InsertPicture(anchor, image);
                _filled++;
                continue;
            }

            if (!binder.TryGetText(model, name, out string? value))
            {
                _ = Unresolved(anchor.Name, fallback: true);
                continue;
            }

            paragraph.ReplaceText(anchor.Start, anchor.Length, value ?? string.Empty);
            _filled++;
        }
    }

    private void InsertPicture(TemplateAnchor anchor, TemplateImage image)
    {
        Paragraph paragraph = anchor.Paragraph;
        RunFormat format = paragraph.FormatAtOffset(anchor.Start);
        paragraph.ReplaceText(anchor.Start, anchor.Length, string.Empty);

        var picture = new Picture
        {
            Image = image.Image,
            Width = image.Width ?? image.Image.NaturalWidth,
            Height = image.Height ?? image.Image.NaturalHeight,
            IsDirty = true,
        };

        paragraph.InsertObject(anchor.Start, picture, format);
        _document.Media.Add(image.Image);
    }

    private void ExpandTable(Table table)
    {
        foreach (TableRow row in table.Rows.ToArray())
        {
            if (CollectionNameIn(row) is not { } name)
                continue;

            if (!_binder.TryGetRows(_model, name, out TemplateRows rows))
            {
                _ = Unresolved(name, fallback: true);
                continue;
            }

            int index = table.Rows.IndexOf(row);
            table.Rows.RemoveAt(index);
            foreach (object item in rows.Items)
            {
                TableRow clone = row.Clone();
                table.Rows.Insert(index++, clone);
                foreach (TableCell cell in clone.Cells)
                    FillValues(cell, item, rows.Binder);
                _repeated++;
            }
        }
    }

    private void ExpandBlockRegion(BlockContainer container, BlockContentControl control, string name)
    {
        if (!_binder.TryGetRows(_model, name, out TemplateRows rows))
        {
            _ = Unresolved(name, fallback: true);
            return;
        }

        int index = container.Blocks.IndexOf(control);
        container.Blocks.RemoveAt(index);

        foreach (object item in rows.Items)
        {
            foreach (Block template in control.Blocks)
            {
                Block clone = template.Clone();
                container.Blocks.Insert(index++, clone);
                if (clone is Paragraph paragraph)
                    FillParagraph(paragraph, item, rows.Binder);
                else if (clone is Table table)
                    FillTable(table, item, rows.Binder);
            }

            _repeated++;
        }
    }

    private void FillTable(Table table, object model, ITemplateBinder binder)
    {
        foreach (TableRow row in table.Rows)
        {
            foreach (TableCell cell in row.Cells)
                FillValues(cell, model, binder);
        }
    }

    /// <summary>The collection a row belongs to, taken from the first dotted placeholder in it.</summary>
    private static string? CollectionNameIn(TableRow row)
    {
        foreach (TableCell cell in row.Cells)
        {
            foreach (Paragraph paragraph in cell.Blocks.Paragraphs)
            {
                foreach (TemplateAnchor anchor in TemplateAnchors.Find(paragraph))
                {
                    if (TemplateAnchors.CollectionOf(anchor.Name) is { } name)
                        return name;
                }
            }
        }

        return null;
    }

    private bool Unresolved(string name, bool fallback)
    {
        _unresolved.Add(name);
        return fallback;
    }
}
