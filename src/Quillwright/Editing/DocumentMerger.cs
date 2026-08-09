using Quillwright.Diagnostics;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Editing;

/// <summary>
/// Copies the content of one document into another, carrying everything the content leans on:
/// styles with their chains, numbering with its definitions, images, notes, comments, bookmark
/// ids and header parts — each remapped so the two documents stay independent.
/// </summary>
/// <remarks>
/// <para>
/// The one rule is that the target's own definitions win: a style the target already has keeps
/// its meaning, and the appended content wears it, which is what Word does when pasting.
/// Everything the target lacks is copied over with fresh identifiers.
/// </para>
/// <para>
/// The few things that cannot be carried — a chart, an OLE object, verbatim markup that points
/// into the source package by relationship id — are left behind, each with a warning naming
/// it, because a dangling relationship would make Word repair the file.
/// </para>
/// </remarks>
internal sealed class DocumentMerger
{
    private readonly WordDocument _target;
    private readonly WordDocument _source;
    private readonly List<DocumentWarning> _warnings = [];
    private readonly HashSet<string> _stylesEnsured = new(StringComparer.Ordinal);
    private readonly Dictionary<int, int> _numbering = [];
    private readonly Dictionary<int, int> _footnotes = [];
    private readonly Dictionary<int, int> _endnotes = [];
    private readonly Dictionary<int, int> _comments = [];
    private readonly Dictionary<HeaderFooter, HeaderFooter> _headers = [];
    private readonly int _bookmarkShift;

    private DocumentMerger(WordDocument target, WordDocument source)
    {
        _target = target;
        _source = source;
        _bookmarkShift = MaxBookmarkId(target) + 1;
    }

    /// <summary>Appends the content of a document to another.</summary>
    /// <param name="target">The document that grows.</param>
    /// <param name="source">The document copied from; it is not changed.</param>
    /// <param name="options">How the content arrives.</param>
    public static IReadOnlyList<DocumentWarning> Append(
        WordDocument target, WordDocument source, DocumentAppendOptions options)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        if (ReferenceEquals(target, source))
            throw new ArgumentException("A document cannot be appended to itself; clone it first.", nameof(source));

        var merger = new DocumentMerger(target, source);
        merger.Run(options);
        return merger._warnings;
    }

    /// <summary>
    /// A merger to copy blocks one at a time, for a caller — the comparer — that decides
    /// placement itself. The instance keeps its maps, so two copied blocks that lean on the
    /// same style or list copy it once and agree on the result.
    /// </summary>
    public static DocumentMerger For(WordDocument target, WordDocument source) => new(target, source);

    /// <summary>Copies one block into the target's world without placing it anywhere.</summary>
    public Block? CopyBlock(Block block) => Copy(block);

    /// <summary>Copies one inline object into the target's world.</summary>
    public InlineObject? CopyInlineObject(InlineObject value) => CopyObject(value);

    /// <summary>What could not be carried so far.</summary>
    public IReadOnlyList<DocumentWarning> Warnings => _warnings;

    private void Run(DocumentAppendOptions options)
    {
        if (options.KeepSections)
        {
            foreach (Section sourceSection in _source.Sections)
            {
                var section = new Section { Properties = sourceSection.Properties.Clone() };
                foreach ((Styles.HeaderFooterKind kind, HeaderFooter content) in sourceSection.Headers.Defined)
                    section.Headers[kind] = CopyHeaderFooter(content);
                foreach ((Styles.HeaderFooterKind kind, HeaderFooter content) in sourceSection.Footers.Defined)
                    section.Footers[kind] = CopyHeaderFooter(content);

                _target.Sections.Add(section);
                CopyBlocks(sourceSection.Blocks, section.Blocks);
            }

            return;
        }

        Section last = _target.Sections.Last;
        bool first = true;
        foreach (Section sourceSection in _source.Sections)
        {
            foreach (Block block in sourceSection.Blocks)
            {
                if (Copy(block) is not { } copied)
                    continue;

                if (first && options.StartOnNewPage)
                {
                    if (copied is Paragraph opening)
                        opening.Format = opening.Format with { PageBreakBefore = true };
                    else
                        last.Blocks.Add(new Paragraph { Format = ParagraphFormat.Default with { PageBreakBefore = true } });
                }

                first = false;
                last.Blocks.Add(copied);
            }
        }
    }

    private void CopyBlocks(IEnumerable<Block> from, IList<Block> into)
    {
        foreach (Block block in from)
        {
            if (Copy(block) is { } copied)
                into.Add(copied);
        }
    }

    private Block? Copy(Block block)
    {
        switch (block)
        {
            case Paragraph paragraph:
                return CopyParagraph(paragraph);

            case Table table:
            {
                var copy = (Table)table.Clone();
                FixTable(copy);
                return copy;
            }

            case RawBlock raw when LeansOnPackage(raw.Xml):
                Leave("a preserved block that points into the source package by relationship id");
                return null;

            case RawBlock raw:
                return new RawBlock(raw.Xml);

            case AlternateContentBlock alternate when LeansOnPackage(alternate.Prefix) || LeansOnPackage(alternate.Suffix):
                Leave("a compatibility block whose markup points into the source package");
                return null;

            case AlternateContentBlock alternate:
            {
                var copy = new AlternateContentBlock(alternate.Prefix, alternate.Suffix);
                CopyBlocks(alternate.Blocks, copy.Blocks);
                return copy;
            }

            case BlockContentControl control:
            {
                var copy = new BlockContentControl
                {
                    Tag = control.Tag,
                    Alias = control.Alias,
                    Id = control.Id,
                    PropertiesXml = control.PropertiesXml,
                    EndPropertiesXml = control.EndPropertiesXml,
                };

                CopyBlocks(control.Blocks, copy.Blocks);
                return copy;
            }

            default:
            {
                Block copy = block.Clone();
                if (copy is Paragraph cloned)
                    FixParagraph(cloned);
                return copy;
            }
        }
    }

    private Paragraph CopyParagraph(Paragraph paragraph)
    {
        var copy = (Paragraph)paragraph.Clone();
        FixParagraph(copy);
        return copy;
    }

    private void FixTable(Table table)
    {
        EnsureStyle(table.Format.StyleId);
        foreach (TableRow row in table.Rows)
        {
            foreach (TableCell cell in row.Cells)
            {
                var fixedBlocks = new List<Block>();
                foreach (Block block in cell.Blocks)
                {
                    // The cell's blocks were already cloned by Table.Clone; what each needs is
                    // the same fixing a directly copied block gets, in place.
                    switch (block)
                    {
                        case Paragraph paragraph:
                            FixParagraph(paragraph);
                            fixedBlocks.Add(paragraph);
                            break;

                        case Table nested:
                            FixTable(nested);
                            fixedBlocks.Add(nested);
                            break;

                        case RawBlock raw when LeansOnPackage(raw.Xml):
                            Leave("a preserved block inside a table cell that points into the source package");
                            break;

                        default:
                            fixedBlocks.Add(block);
                            break;
                    }
                }

                if (fixedBlocks.Count != cell.Blocks.Count)
                {
                    cell.Blocks.Clear();
                    foreach (Block block in fixedBlocks)
                        cell.Blocks.Add(block);
                }
            }
        }
    }

    /// <summary>
    /// Points a cloned paragraph at the target document: styles ensured, list ids remapped,
    /// and every shared anchor instance swapped for one of the target's own.
    /// </summary>
    private void FixParagraph(Paragraph paragraph)
    {
        EnsureStyle(paragraph.Format.StyleId);
        if (paragraph.Format.NumberingId is { } listId && listId > 0)
            paragraph.Format = paragraph.Format with { NumberingId = EnsureNumbering(listId) };

        paragraph.RewriteRunFormats(format =>
        {
            EnsureStyle(format.StyleId);
            return format;
        });

        paragraph.RewriteAnchors(CopyObject, CopyMark, CopyRange);
    }

    private InlineObject? CopyObject(InlineObject value)
    {
        switch (value)
        {
            case NoteReference note:
            {
                if (CopyNote(note.Id, note.IsEndnote) is not { } id)
                {
                    Leave(note.IsEndnote ? "an endnote reference with no endnote behind it" : "a footnote reference with no footnote behind it");
                    return null;
                }

                return new NoteReference { IsEndnote = note.IsEndnote, Id = id, CustomMark = note.CustomMark };
            }

            case CommentReference comment:
            {
                if (CopyComment(comment.Id) is not { } id)
                    return null;

                return new CommentReference { Id = id };
            }

            case Picture picture:
            {
                _target.Media.Add(picture.Image);
                return new Picture
                {
                    Image = picture.Image,
                    Width = picture.Width,
                    Height = picture.Height,
                    Name = picture.Name,
                    Description = picture.Description,
                    IsInline = picture.IsInline,
                    Anchor = picture.Anchor,
                    OriginalXml = picture.OriginalXml,

                    // Dirty makes the writer regenerate or rewrite the markup, which is what
                    // repoints the image relationship at the target's own media part.
                    IsDirty = true,
                };
            }

            case Shape shape:
                return CopyShape(shape);

            case ChartFrame:
                Leave("a chart, whose part lives in the source package");
                return null;

            case AlternateContent alternate:
            {
                if (LeansOnPackage(alternate.Prefix) || LeansOnPackage(alternate.Suffix))
                {
                    Leave("a compatibility fragment whose markup points into the source package");
                    return null;
                }

                return CopyObject(alternate.Content) is { } inner
                    ? new AlternateContent(alternate.Prefix, inner, alternate.Suffix)
                    : null;
            }

            case RawInline raw when LeansOnPackage(raw.Xml):
                Leave("a preserved fragment that points into the source package by relationship id");
                return null;

            case RawInline raw:
                return new RawInline(raw.Xml, raw.IsRunChild);

            case FieldCharacter field:
                return new FieldCharacter
                {
                    Kind = field.Kind,
                    Locked = field.Locked,
                    Dirty = field.Dirty,
                    FormFieldXml = field.FormFieldXml,
                };

            case MathObject math when math.OriginalXml is { } markup:
                // Re-reading the markup gives the target a tree of its own; sharing one tree
                // between two documents would let an edit in either show up in both.
                return Formats.OfficeMathReader.Parse(markup) ?? value;

            default:
                return value;
        }
    }

    private Shape? CopyShape(Shape shape)
    {
        foreach (string fragment in shape.Fragments)
        {
            if (LeansOnPackage(fragment))
            {
                Leave("a shape whose markup points into the source package by relationship id");
                return null;
            }
        }

        var content = new TextBox();
        CopyBlocks(shape.Content.Blocks, content.Blocks);

        return new Shape([.. shape.Fragments], content)
        {
            Width = shape.Width,
            Height = shape.Height,
            IsInline = shape.IsInline,
            Anchor = shape.Anchor,
            Fill = shape.Fill,
            Direction = shape.Direction,
            Outline = shape.Outline,
            IsLine = shape.IsLine,
        };
    }

    private InlineMark? CopyMark(InlineMark mark) => mark switch
    {
        BookmarkStart start => new BookmarkStart
        {
            Id = start.Id + _bookmarkShift,
            Name = start.Name,
            ColumnFirst = start.ColumnFirst,
            ColumnLast = start.ColumnLast,
        },
        BookmarkEnd end => new BookmarkEnd { Id = end.Id + _bookmarkShift },
        CommentRangeStart start => CopyComment(start.Id) is { } id ? new CommentRangeStart { Id = id } : null,
        CommentRangeEnd end => CopyComment(end.Id) is { } id ? new CommentRangeEnd { Id = id } : null,
        RawMark raw => new RawMark(raw.Xml),
        _ => mark,
    };

    private InlineRange? CopyRange(InlineRange range) => range switch
    {
        Hyperlink link => new Hyperlink
        {
            // The relationship id stays behind: it names a relationship of the source part,
            // and the target's writer makes its own from the URL.
            Url = link.Url,
            Anchor = link.Anchor,
            Tooltip = link.Tooltip,
            TargetFrame = link.TargetFrame,
            AddToHistory = link.AddToHistory,
        },
        SimpleField field => new SimpleField
        {
            Instruction = field.Instruction,
            Dirty = field.Dirty,
            Locked = field.Locked,
            Attributes = field.Attributes,
            DataXml = field.DataXml,
        },
        Revision revision => new Revision
        {
            Kind = revision.Kind,
            Id = revision.Id,
            Author = revision.Author,
            Date = revision.Date,
            MoveName = revision.MoveName,
            Attributes = revision.Attributes,
        },
        InlineContentControl control => new InlineContentControl
        {
            Tag = control.Tag,
            Alias = control.Alias,
            Id = control.Id,
            PropertiesXml = control.PropertiesXml,
            EndPropertiesXml = control.EndPropertiesXml,
        },
        _ => range,
    };

    /// <summary>Copies a referenced note and answers the id it has in the target.</summary>
    private int? CopyNote(int sourceId, bool isEndnote)
    {
        Dictionary<int, int> map = isEndnote ? _endnotes : _footnotes;
        if (map.TryGetValue(sourceId, out int mapped))
            return mapped;

        List<Note> sourceList = isEndnote ? _source.EndnoteList : _source.FootnoteList;
        if (sourceList.FirstOrDefault(note => note.Id == sourceId) is not { } note)
            return null;

        List<Note> targetList = isEndnote ? _target.EndnoteList : _target.FootnoteList;
        int id = targetList.Count == 0 ? 1 : Math.Max(1, targetList.Max(static n => n.Id) + 1);
        var copy = new Note(_target, isEndnote) { Id = id, Kind = note.Kind };
        map[sourceId] = id;
        targetList.Add(copy);
        CopyBlocks(note.Blocks, copy.Blocks);
        return id;
    }

    /// <summary>Copies a referenced comment — its parents first — and answers its new id.</summary>
    private int? CopyComment(int sourceId)
    {
        if (_comments.TryGetValue(sourceId, out int mapped))
            return mapped;

        if (_source.CommentList.FirstOrDefault(comment => comment.Id == sourceId) is not { } comment)
        {
            Leave("a comment mark with no comment behind it");
            return null;
        }

        int? parent = comment.ParentId is { } parentId ? CopyComment(parentId) : null;
        int id = _target.CommentList.Count == 0 ? 1 : _target.CommentList.Max(static c => c.Id) + 1;
        var copy = new Comment(_target)
        {
            Id = id,
            Author = comment.Author,
            Initials = comment.Initials,
            Date = comment.Date,
            DateUtc = comment.DateUtc,
            IsFollowUp = comment.IsFollowUp,
            ParentId = parent,
            IsResolved = comment.IsResolved,
        };

        _comments[sourceId] = id;
        _target.CommentList.Add(copy);
        CopyBlocks(comment.Blocks, copy.Blocks);
        return id;
    }

    private HeaderFooter CopyHeaderFooter(HeaderFooter content)
    {
        if (_headers.TryGetValue(content, out HeaderFooter? copied))
            return copied;

        var copy = new HeaderFooter(_target, content.IsFooter);
        _headers[content] = copy;
        _target.RegisterHeaderFooter(copy);
        CopyBlocks(content.Blocks, copy.Blocks);
        return copy;
    }

    /// <summary>
    /// Makes sure a style the content wears exists in the target, copying it — and the chain
    /// it stands on — from the source when it does not. A style the target already has wins.
    /// </summary>
    private void EnsureStyle(string? id)
    {
        if (id is null || !_stylesEnsured.Add(id))
            return;

        if (_target.Styles.Find(id) is not null || _source.Styles.Find(id) is not { } style)
            return;

        var copy = new Style(id, style.Kind)
        {
            Name = style.Name,
            Aliases = style.Aliases,
            BasedOn = style.BasedOn,
            NextStyle = style.NextStyle,
            LinkedStyle = style.LinkedStyle,
            IsDefault = false,
            IsCustom = style.IsCustom,
            Priority = style.Priority,
            SemiHidden = style.SemiHidden,
            UnhideWhenUsed = style.UnhideWhenUsed,
            QuickFormat = style.QuickFormat,
            Locked = style.Locked,
            AutoRedefine = style.AutoRedefine,
            Hidden = style.Hidden,
            Personal = style.Personal,
            PersonalCompose = style.PersonalCompose,
            PersonalReply = style.PersonalReply,
            ParagraphFormat = style.ParagraphFormat,
            RunFormat = style.RunFormat,
            TableFormat = style.TableFormat,
            RowFormat = style.RowFormat,
            CellFormat = style.CellFormat,
            NumberingId = style.NumberingId is { } styleList ? EnsureNumbering(styleList) : null,
        };

        if (copy.ParagraphFormat.NumberingId is { } attached && attached > 0)
            copy.ParagraphFormat = copy.ParagraphFormat with { NumberingId = EnsureNumbering(attached) };

        foreach (ConditionalTableStyle conditional in style.ConditionalFormats)
        {
            copy.ConditionalFormats.Add(new ConditionalTableStyle
            {
                Region = conditional.Region,
                ParagraphFormat = conditional.ParagraphFormat,
                RunFormat = conditional.RunFormat,
                TableFormat = conditional.TableFormat,
                RowFormat = conditional.RowFormat,
                CellFormat = conditional.CellFormat,
            });
        }

        _target.Styles.Add(copy);
        EnsureStyle(style.BasedOn);
        EnsureStyle(style.NextStyle);
        EnsureStyle(style.LinkedStyle);
    }

    /// <summary>Copies a numbering instance with its definition and answers the target's id.</summary>
    private int EnsureNumbering(int sourceId)
    {
        if (_numbering.TryGetValue(sourceId, out int mapped))
            return mapped;

        NumberingInstance? instance = _source.Numbering.Instances.FirstOrDefault(i => i.Id == sourceId);
        AbstractNumbering? definition = instance is null
            ? null
            : _source.Numbering.Definitions.FirstOrDefault(d => d.Id == instance.AbstractId);

        if (instance is null || definition is null)
        {
            // The source itself dangles; the id is carried as it was, exactly as a load would.
            _numbering[sourceId] = sourceId;
            return sourceId;
        }

        var definitionCopy = new AbstractNumbering
        {
            Id = _target.Numbering.Definitions.Count == 0 ? 0 : _target.Numbering.Definitions.Max(static d => d.Id) + 1,
            MultiLevelType = definition.MultiLevelType,
            NumberingStyleLink = definition.NumberingStyleLink,
            StyleLink = definition.StyleLink,
            NsidXml = definition.NsidXml,
            TemplateXml = definition.TemplateXml,
            NameXml = definition.NameXml,
            Attributes = definition.Attributes,
        };

        foreach (NumberingLevel level in definition.Levels)
        {
            NumberingLevel levelCopy = level.Clone();
            EnsureStyle(levelCopy.StyleId);
            definitionCopy.Levels.Add(levelCopy);
        }

        var instanceCopy = new NumberingInstance
        {
            Id = _target.Numbering.Instances.Count == 0 ? 1 : _target.Numbering.Instances.Max(static i => i.Id) + 1,
            AbstractId = definitionCopy.Id,
        };

        foreach (NumberingLevelOverride over in instance.Overrides)
        {
            instanceCopy.Overrides.Add(new NumberingLevelOverride
            {
                Level = over.Level,
                StartOverride = over.StartOverride,
                Definition = over.Definition?.Clone(),
            });
        }

        _target.Numbering.Definitions.Add(definitionCopy);
        _target.Numbering.Instances.Add(instanceCopy);
        _numbering[sourceId] = instanceCopy.Id;

        EnsureStyle(definition.NumberingStyleLink);
        EnsureStyle(definition.StyleLink);
        return instanceCopy.Id;
    }

    /// <summary>Whether verbatim markup names anything of the package it came from.</summary>
    private static bool LeansOnPackage(string xml) =>
        xml.Contains("r:id=", StringComparison.Ordinal) ||
        xml.Contains("r:embed=", StringComparison.Ordinal) ||
        xml.Contains("r:link=", StringComparison.Ordinal) ||
        xml.Contains("o:relid=", StringComparison.Ordinal);

    private void Leave(string what) =>
        _warnings.Add(new DocumentWarning(WarningCode.NotCarried, $"Appending left behind {what}."));

    private static int MaxBookmarkId(WordDocument document)
    {
        int max = 0;
        foreach (Paragraph paragraph in document.Paragraphs)
        {
            foreach ((_, InlineMark mark) in paragraph.Marks)
            {
                if (mark is BookmarkStart start && start.Id > max)
                    max = start.Id;
                else if (mark is BookmarkEnd end && end.Id > max)
                    max = end.Id;
            }
        }

        return max;
    }
}
