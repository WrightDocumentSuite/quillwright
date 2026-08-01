# The document model

```
WordDocument
├── Sections            page setup, headers and footers, blocks
│   └── Blocks          Paragraph | Table | BlockContentControl | RawBlock
│       └── Table → Rows → Cells → Blocks   (a cell holds blocks, so tables nest)
├── Styles              the style catalogue and the resolver
├── Numbering           list definitions and instances
├── Comments            comment bodies; the anchors live in the paragraphs
├── Footnotes/Endnotes  note bodies; the references live in the paragraphs
├── Media               the images, stored once and shared
├── Charts              what each chart draws, from the numbers it caches
├── Signatures          who signed the package, and whether it has changed since
├── WebExtensions       the add-ins the document will try to load
├── Settings            the settings part, as its elements in order
└── Properties          title, author, dates
```

## Paragraphs

A paragraph is one text buffer with everything else anchored to offsets in it. Four kinds of
thing can be anchored:

| | Width | Examples |
| --- | --- | --- |
| **Runs** | cover the whole buffer | character formatting |
| **Objects** | exactly one character | picture, break, footnote reference, field boundary, symbol |
| **Marks** | zero | bookmark start and end, comment range start and end |
| **Ranges** | any stretch | hyperlink, tracked insertion or deletion, inline content control |

Objects occupy a character so that every offset stays meaningful: a tab is `\t`, a break is
`\n`, a non-breaking hyphen is `U+2011`, and everything else is `U+FFFC`. `paragraph.Text`
returns the buffer as it is; `paragraph.GetText()` drops the placeholders that have no textual
meaning, which is what text extraction wants.

```csharp
var paragraph = new Paragraph();
paragraph.AppendText("See the ");
int start = paragraph.TextLength;
paragraph.AppendText("terms", RunFormat.Default with { StyleId = "Hyperlink" });
paragraph.AddRange(new Hyperlink { Url = "https://example.com/terms" }, start, paragraph.TextLength - start);
paragraph.AppendText(" before signing.");
paragraph.AddMark(new BookmarkStart { Id = 1, Name = "terms" }, start);
paragraph.AddMark(new BookmarkEnd { Id = 1 }, paragraph.TextLength);
```

## Runs are views, not objects

`paragraph.Runs` yields `Run`, a struct holding the paragraph and an index. Enumerating
allocates nothing, and writing through the view edits the paragraph in place.

```csharp
foreach (Run run in paragraph.Runs)
{
    if (run.Text.Contains("overdue", StringComparison.OrdinalIgnoreCase))
        run.SetFormat(f => f with { Bold = true });
}
```

## Editing text moves the anchors with it

`ReplaceText` is the single primitive; `InsertText` and `RemoveText` are it with one side
empty. It keeps everything consistent:

- Runs are split and merged so the result has no redundant boundaries.
- Objects inside the replaced stretch are removed; those after it shift.
- Marks inside it collapse to the edges — opening marks to the start, closing marks to the end
  of the replacement — so a bookmark keeps surrounding the text that replaced its content.
- Ranges keep their outer edges. Replacing exactly the extent of a hyperlink leaves the
  hyperlink covering the replacement, which is what makes `{{placeholder}}` inside a link work.

```csharp
paragraph.ReplaceText(start, length, "new text");
paragraph.InsertText(0, "Note: ");
paragraph.ApplyFormat(6, 4, f => f with { Bold = true });   // splits runs at the edges
```

Content spliced at a range's leading edge lands inside it; content appended at its trailing
edge lands outside. That rule is what makes filling a content control keep the control while
typing after a hyperlink does not extend the link.

## Blocks and containers

Anything that holds blocks is a `BlockContainer`: a section, a table cell, a header or footer,
a footnote, a comment, a block-level content control. They all offer `AddParagraph`,
`AddTable`, `Blocks` and `GetText`.

`document.AllContainers` walks every one of them, including headers, footers, notes, comments
and nested cells. That is what search, replace and templating iterate, which is why they reach
a placeholder in a footer.

## Fields

A field in the file is a sequence — a begin character, instruction runs, a separator, the
cached result, an end character — not an element. `Field` is a view over that sequence:

```csharp
paragraph.AppendPageNumber();
paragraph.AppendField("REF bookmark \\h", "see above");

foreach (Field field in document.Fields())
{
    if (field.Name == "PAGE")
        field.SetResult("7");
}
```

`document.UpdateFields()` recomputes the ones that follow from the document alone and leaves
the rest dirty for the consumer to build; `FieldInstruction.Parse` gives the instruction as a
name, arguments and switches. See [fields.md](fields.md).

## Equations

An `m:oMath` becomes a `MathObject` anchored in the paragraph, holding a tree of fractions,
radicals, scripts, sums, delimiters and matrices, with anything outside that set kept verbatim
as a `RawMath`. `GetText()` writes the tree out as a line, so an equation is part of the
document's text rather than a hole in it. See [math.md](math.md).

## Images

An `ImageData` holds the encoded bytes and the size sniffed from the header, and is shared by
every picture that shows it, so a logo on forty pages is one package part.

```csharp
ImageData logo = await ImageData.FromFileAsync("logo.png");
paragraph.AppendPicture(logo, Length.FromCentimeters(4));
```

A picture read from a file keeps its original markup and is written back verbatim, so cropping
and the effects the model does not represent survive. Markup is regenerated only for a picture
that was created or changed.

A picture that does not flow with the text carries a `PictureAnchor` saying what its position is
measured from, how far, what the text does about it and how close it may come — the four wrap
distances of the anchor. Both of the ways a document can state that are read — the modern drawing
and the VML an older reader falls back to — so a document converted out of `.doc`, which has only
the second, places its pictures as accurately as if Word had saved the document.

```csharp
if (picture.Anchor is { } anchor)
    Console.WriteLine($"{anchor.OffsetX} from the {anchor.HorizontalFrom}, wrapping {anchor.Wrapping}");
```

A shape with words in it — a text box, a callout — is a `Shape` whose `Content` is an ordinary
container. The shape around those words is kept as the bytes it arrived as, and what can be read
off those bytes is offered as properties: `Width`, `Height`, `IsInline`, `Anchor`, `Fill` and
`Outline`. They have no setters on purpose. They are a reading of the markup rather than a second
copy of it, so a renderer can draw the shape where it belongs while the bytes written back stay
the bytes that were read.

## Notes

`document.Footnotes` and `document.Endnotes` hold the note bodies, each an ordinary container, and
the references to them are objects anchored in the text. The first two entries of each list are the
separators Word keeps there rather than notes anybody wrote.

How the notes are printed and numbered is a `NoteProperties`, read off the `w:footnotePr` and
`w:endnotePr` the document keeps verbatim — from `document.Settings.Footnotes` and `.Endnotes`, or
from `section.Properties.FootnoteProperties` when a section overrides them. Like the geometry of a
shape, it has no setters: it is a reading of what the document says rather than a second copy of it.

```csharp
NoteProperties notes = document.Settings.Footnotes;
Console.WriteLine($"{notes.Position}, numbered {notes.NumberFormat} from {notes.Start}");
```

## Cloning

`Clone()` on a block, row or cell returns an independent copy that is not attached anywhere.
Cloning a section shares its headers and footers rather than duplicating them, which matches
what Word does when a section is split.
