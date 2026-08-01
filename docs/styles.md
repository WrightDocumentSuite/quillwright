# Styles and numbering

## The catalogue

`document.Styles` holds the document defaults and every named style. Built-in styles are
created on demand rather than up front, which is what Word does too: a fresh document declares
only the handful of styles it uses, and asking for `Heading1` is what brings its definition —
and the definitions it is based on — into the file.

```csharp
document.Styles.GetOrAdd("Heading1");                       // built-in, materialised now
document.Styles.GetOrAdd("TableGrid", StyleKind.Table);
document.Styles.Add(new Style("Callout", StyleKind.Paragraph)
{
    Name = "Callout",
    BasedOn = "Normal",
    IsCustom = true,
    ParagraphFormat = ParagraphFormat.Default with { IndentLeft = Length.FromCentimeters(1) },
    RunFormat = RunFormat.Default with { Italic = true, Color = WordColor.FromRgb(0x555555) },
});
```

The catalogue covers Normal, Heading 1-9, Title, Subtitle, Quote, Intense Quote, List
Paragraph, No Spacing, Caption, Header, Footer, Hyperlink, Followed Hyperlink, Strong,
Emphasis, the footnote, endnote and comment styles, TOC 1-9 and TOC Heading, Table Normal and
Table Grid.

## Resolving what actually applies

`document.Resolver` computes the formatting in force after the whole chain has had its say, in
the order ISO-29500 §17.7.2 specifies:

```
document defaults
  → table style (whole table, then the conditional regions the tblLook switches on)
    → numbering level
      → paragraph style chain, from its root down
        → character style chain
          → direct formatting on the run
```

The numbering level takes part in that chain with its paragraph properties only. Its `w:rPr`
dresses the bullet or the number and nothing else (§17.9.24) — which is why the text of a
bulleted paragraph stays in the body font while its bullet comes from Symbol. Ask for it with
`document.Resolver.ResolveNumberingSymbolFormat(paragraph)`, which answers `null` for a
paragraph that is not in a list.

The numbering layer sits above the paragraph style in that order, but the `w:numPr` naming the
list is just as often *in* that style — a numbered heading carries it there rather than on
every paragraph. The reference is therefore taken from the direct formatting when it states
one and from the style chain otherwise, so such a paragraph gets its level's indents and its
marker's formatting either way.

```csharp
RunFormat effective = document.Resolver.ResolveRunFormat(paragraph.Runs[0]);
ParagraphFormat layout = document.Resolver.ResolveParagraphFormat(paragraph);
```

Paragraph indents can be expressed either in twips (`IndentLeft`, `IndentRight`,
`IndentFirstLine`, `IndentHanging`) or in hundredths of a character
(`IndentLeftCharacters`, `IndentRightCharacters`, `IndentFirstLineCharacters`,
`IndentHangingCharacters`). The character-unit form is not converted to a guessed physical
length: both forms survive in the resolved format, and, as §17.3.1.12 requires, a non-zero
character value supersedes the related twip value. Word's [MS-OI29500] 2.1.44 rule is applied
while resolving styles: a character value of zero clears the value inherited from an earlier
hierarchy level instead of becoming a zero-width character indent.

Bold, italic, caps and the other toggles do not simply overwrite down that chain — they
exclusive-or, which is why bold text inside a bold style comes out unbold. Direct formatting is
the exception and always means what it says. Style chains are walked with a cycle guard, so a
corrupt file cuts the loop instead of hanging.

§17.7.3 names the toggles exhaustively and there are twelve of them: `b`, `bCs`, `caps`,
`emboss`, `i`, `iCs`, `imprint`, `outline`, `shadow`, `smallCaps`, `strike` and `vanish`. The
neighbours that look like they belong are not on the list and overwrite like anything else —
`dstrike` is not a toggle even though `strike` is, and neither are `rtl` or `cs`. That last
pair matters: exclusive-oring them turns a right-to-left run left-to-right as soon as two
levels of the hierarchy both ask for it.

Results are cached per style identifier and dropped when `StyleSheet.Version` changes. Editing
a `Style` object in place does not bump that version, so call `Styles.Invalidate()` afterwards.

## Theme colours

A colour is often not a value but a name — `accent1`, `text2` — so that changing the theme
recolours the document. Resolving one means finding the slot in the theme's colour scheme,
possibly through a mapping the settings give, and then lightening or darkening it:

```csharp
RunFormat effective = document.Resolver.ResolveRunFormat(run);
uint? shown = document.ResolveColor(effective.Color ?? WordColor.Auto);
```

`ResolveColor` returns the literal value of a literal colour, the resolved value of a theme
colour, and `null` for the automatic colour and for a theme slot the document's theme does not
define. `document.Theme` exposes the scheme itself if you want the twelve colours directly.

Two vocabularies meet here and do not quite line up: the theme names its colours in the drawing
layer's terms (`dk1`, `lt1`, `accent1`) while a run names them in the word processor's (`text1`,
`background1`). `w:clrSchemeMapping` in the settings carries the map, and a document may have
swapped them round — which is what makes a "light" background come out dark.

The tint and shade arithmetic works on the lightness of the colour rather than on its channels,
so a tinted red stays red instead of going through grey. Word caches the value it computed in
the same element as the name, which makes every theme colour in a real document a worked
example; checked against the corpus, most agree exactly and none differs by more than one step in any
channel.

## Conditional table formatting

A table style defines formatting for the header row, the total row, the first and last columns,
the banded stripes and the four corner cells. Which of them apply is the table's own
`w:tblLook`, exposed as `TableStyleOptions`:

```csharp
table.Format = table.Format with
{
    StyleId = "GridTable4Accent1",
    StyleOptions = TableStyleOptions.FirstRow | TableStyleOptions.LastRow,
};
```

Without those flags a style's banding and header formatting are defined but never drawn — a
common surprise when a table looks plain despite carrying a style. Regions are applied in the
order the specification gives: bands first, then the edge rows and columns, then the corners,
so a header cell in the first column wins over plain header formatting.

## Numbering

A list has two halves. An `AbstractNumbering` defines the levels; a `NumberingInstance` points
at a definition and is what a paragraph references, which is how two lists can share formatting
but count separately.

```csharp
int bullets = document.Numbering.AddBulletList();     // nine levels, cycling glyphs
int numbers = document.Numbering.AddNumberedList();   // 1. a) i.
int outline = document.Numbering.AddOutlineList();    // 1, 1.1, 1.1.1

Paragraph item = section.AddParagraph("First point", "ListParagraph");
item.Format = item.Format with { NumberingId = bullets, NumberingLevel = 0 };
```

The presets build all nine levels with the indents Word uses, so a list looks right without
further work. `ResolveLevel` follows an instance to its definition and applies any per-instance
override:

```csharp
NumberingLevel? level = document.Numbering.ResolveLevel(bullets, 1);
```

There is a third hop it also follows. An `AbstractNumbering` carrying `w:numStyleLink`
(§17.9.21) declares no levels of its own: it names a numbering style, and that style's own
`w:numId` leads to the definition that does. Word builds a reusable list this way, and a
paragraph pointed at the deferring definition is still in a list — without the hop it resolves
to nothing at all, so it gets no marker, no indents, and is not recognised as a list item by
the PDF or Markdown exporters. A link that leads back to itself is abandoned rather than
followed.

Nothing computes the label text. The numbering definition is round-tripped and resolved so a
consumer can render it; Quillwright does not produce "3.2.1" as a string, because doing so
correctly means tracking counters across the whole document including restarts and overrides.
