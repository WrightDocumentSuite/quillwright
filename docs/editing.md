# Editing, search and revisions

## The cursor

`DocumentEditor` keeps the position, the container and the formatting so that building a
document reads like writing one.

```csharp
var editor = new DocumentEditor(document);

editor.WriteHeading("Quarterly report", 1)
      .WriteLine("Revenue grew across every region.")
      .WithFormat(f => f with { Bold = true })
      .WriteLine("Total: 4.2M")
      .ResetFormat();

Table table = editor.InsertTable(3, 2);
editor.MoveTo(table[0, 0]).Write("Region");

editor.MoveToFooter().Write("Page ").CurrentParagraph.AppendPageNumber();
editor.MoveToSection(0).InsertPageBreak();

if (editor.MoveToBookmark("signature"))
    editor.Write("Signed.");
```

`MoveToHeader` and `MoveToFooter` create the part when the section has none.

## Search and replace

Word splits a phrase into runs at every formatting change and at almost every edit, so text a reader
sees as one string is usually several runs. Because a paragraph keeps its text in one buffer,
searching needs no stitching and a replacement that spans a run boundary is an ordinary splice.

```csharp
int replaced = document.Replace("{{Client}}", "Romashka LLC");

document.Replace(@"(\d{2})\.(\d{2})\.(\d{4})", "$3-$2-$1", new SearchOptions { IsRegex = true });

foreach (TextMatch match in document.Find("overdue", new SearchOptions { MatchCase = false }))
    Console.WriteLine($"{match.Value} at {match.Start}");

document.Highlight("draft", f => f with { Highlight = HighlightColor.Yellow });
```

The replacement takes the formatting of the text it stands in for, and a hyperlink or content
control that covered the whole match keeps covering the replacement. By default the search
reaches headers, footers, notes and comments as well as the body; set
`IncludeSecondaryStories = false` to keep to the body.

## Tracked changes

A tracked edit lives in two places: as a wrapper over a stretch of text, and as a marker on the
paragraph mark saying whether the paragraph break itself was added or removed. Resolving both
is what makes accepting a deletion actually rejoin the paragraphs the author meant to merge.

```csharp
if (document.HasRevisions())
{
    int resolved = document.AcceptAllRevisions();
    // or document.RejectAllRevisions();
}
```

Accepting keeps insertions and drops deletions; rejecting does the opposite. Both clear the
markers and turn `Settings.TrackRevisions` off.

### Recording them

```csharp
using (document.TrackChanges("Ada Lovelace"))
{
    document.Replace("draft", "final");
    editor.MoveToEnd().WriteLine("One more clause.");
    paragraph.Delete();
}
```

While the session is open, every edit made through the ordinary editing API leaves a mark
instead of quietly rewriting the text. Nothing new is needed at the call site: `Replace`,
`DocumentEditor`, `InsertText`, `ApplyFormat` and the rest all go through the same paragraph
primitives, so they record by going through them.

What each edit records:

| Edit | What ends up in the file |
| --- | --- |
| Inserting text | The text, wrapped in `w:ins` |
| Deleting text | The text stays where it is, retagged `w:delText` under a `w:del` |
| Replacing text | The deletion first, then the insertion, as Word writes it |
| Changing formatting | The new formatting, with the old recorded in `w:rPrChange` |
| `AddParagraph` | An inserted paragraph mark (`w:pPr/w:rPr/w:ins`) |
| `paragraph.Delete()` | A deleted paragraph mark, and the text marked deleted with it |

Deleting is the interesting half, because nothing is deleted: a reader that has never heard of
tracked changes still sees the text where it was, and accepting the change later is what
actually removes it. Two consequences worth knowing:

- `paragraph.Text` still contains text that has been deleted. `AcceptAllRevisions` is what makes
  the model read the way the author meant.
- Deleting text this same session inserted removes it outright rather than recording an
  insertion and a deletion of the same characters, which would say nothing to anybody. The same
  goes for a paragraph the session added.

Consecutive edits by the same session merge, so typing a word letter by letter is one `w:ins`
and not eight. Identifiers start above the highest one already in the document, so they cannot
collide with an earlier author's.

Disposing the session stops recording and puts `Settings.TrackRevisions` back the way it was —
that setting describes the mode the document is in, not what one tool did to it. Set it
yourself if the document should stay in tracking mode after you are done.

`paragraph.Delete()` also works with no session open, where it simply removes the paragraph.
That is why it exists rather than `Blocks.Remove`: a collection that sometimes does not remove
what you asked it to would be a trap.

## Comments and notes

```csharp
Paragraph paragraph = document.Paragraphs.First();
document.AddComment(paragraph, start: 0, length: 5, "Check this wording.", "Reviewer", "R");
document.AddFootnote(paragraph, "Drafted from the standard template.");
document.AddEndnote(paragraph, "See appendix B.");
```

`AddComment` places the range marks and the reference, creates the styles and adds the body.
The first note added also creates the separator entries Word keeps at the top of the notes part.

The reference is a character of its own, so adding a comment moves every offset past it along
by one. Adding several comments to one paragraph therefore means working backwards through it,
or reading the offsets again after each call.

A comment about text that runs past a paragraph break takes an overload naming both ends. The
opening mark goes into the first paragraph and the closing mark and the reference into the
last, which is how both formats express such a range.

```csharp
document.AddComment(first, 6, second, 6, "Over the break.", "Reviewer", "R");
```

### Replies

A reply is a comment about the same words as the one it answers, joined to it by the threading
part Word 2013 added (`commentsExtended.xml`, [MS-DOCX] 2.5.3.1).

```csharp
Comment question = document.AddComment(paragraph, 4, 5, "Which one?", "Ada", "A");
Comment answer = document.AddReply(question, "That one.", "Grace", "G");
question.IsResolved = true;
```

`AddReply` finds where the comment it answers is anchored and covers the same text, which is
what Word does — each comment in a thread keeps a range and a reference of its own rather than
sharing the parent's. Replying to a reply is allowed and is what Word writes for a conversation
of more than two. `Comment.ParentId` and `Comment.IsResolved` can also be set directly on a
comment made any other way.

The threading part names comments by the paragraph identifier of their last paragraph rather
than by comment id, so identifiers are minted for comments that have none. A new document of
plain comments does not grow a part it never had. Once a loaded package carries the part,
Quillwright regenerates it from the current model even when every reply link and resolved flag
has been cleared; an earlier `done` or parent link therefore cannot reappear on the next save.

Word 2016 added a second part, `commentsIds.xml` ([MS-DOCX] 2.8), holding an identifier for
each comment that survives renumbering — what tells two people editing at once that they are
looking at the same comment. It is read into `Comment.DurableId` and written back for packages
that carried one, with an identifier minted for any comment added since.

### Dates, follow-ups and reactions

Word 2018 added a third part, `commentsExtensible.xml` ([MS-DOCX] 2.10), which names comments
by durable identifier and holds what the comments part has no room for.

```csharp
comment.DateUtc;     // the unambiguous timestamp; Comment.Date is a local wall clock
comment.IsFollowUp;  // `intelligentPlaceholder`: a prompt rather than a remark
```

`Comment.Date` comes from `w:date`, which Word fills with the wall clock the author saw and
then stamps with `Z` regardless of the time zone; `DateUtc` is the same instant said properly.
Setting `DateUtc` or `IsFollowUp` is enough to have the part written, and asking for it brings
`commentsIds.xml` with it, because the two have to agree on every durable identifier. Comments
that have only a `Date` then get a `dateUtc` derived from it.

Reactions ([MS-OREACTXML]) are not modelled. They are kept verbatim inside their comment and
written back where they were, so reacting in Word and editing here do not cancel each other
out. Entries are rebuilt by walking the comments, so one left behind by a comment that is gone
is dropped rather than pointing at nothing.

### Who the authors are

A comment names its author as free text. `people.xml` ([MS-DOCX] 2.5.3.4) says which account
that text stands for, read into `document.People`:

```csharp
foreach (Person person in document.People)
    Console.WriteLine($"{person.Author}: {person.ProviderId}/{person.UserId}");
```

The part is regenerated only for a document that already had one, and only by adding: a
comment author it does not name gets an entry with provider `None`, and nothing is removed,
because a name that looks unused may still be the author of a tracked change.

### Saving to `.doc`

The binary format keeps more of this than it looks. `AtrdExtra` ([MS-DOC] 2.9.5) carries a
comment tree and a date per comment, so replies stay replies and dates come back — to the
minute, since the packed date has no room for seconds. What it has nowhere to put is the
resolved flag, the reactions and the author identities, and those are reported through
`DocWriteOptions.OnWarning` rather than dropped quietly.

## Structural editing

```csharp
table.InsertColumn(2, Length.FromCentimeters(3));
table.RemoveColumn(0);
table.MergeCells(firstRow: 0, firstColumn: 0, rowCount: 2, columnCount: 3);

section.Blocks.Insert(0, new Paragraph("Preamble"));
section.Blocks.Remove(block);

Section second = document.Sections.Add(SectionStart.NextPage);
second.Properties.Orientation = PageOrientation.Landscape;
```

Moving a block between containers re-parents it, so `block.Document` stays true.

## Appending one document to another

`target.Append(source)` copies the whole content of another document to the end of this one —
the assembly step of building a contract from a clause library, or a yearly report from twelve
monthly ones.

```csharp
WordDocument contract = await WordDocument.LoadAsync("frame.docx");
WordDocument annex = await WordDocument.LoadAsync("annex-a.docx");

IReadOnlyList<DocumentWarning> left = contract.Append(annex, new DocumentAppendOptions
{
    StartOnNewPage = true,
});
```

Everything the content leans on comes along, remapped so the two documents stay independent:
styles with their `basedOn`, `next` and `link` chains; numbering as fresh instances, so an
appended list counts from its own start rather than continuing the host's; images into the
target's media, deduplicated; footnotes, endnotes and comments — threading included — under
fresh ids; hyperlinks rebound by URL rather than by relationship id; bookmark ids shifted
clear of the target's. A style the target already defines wins over the source's definition
of the same name, which is what Word does when pasting; the appended text then wears the
host's look, which is normally exactly what assembling under a house template wants.

`KeepSections = true` brings the source's sections across as sections of their own — page
setup, headers and footers copied — instead of flowing the content into the target's last
section.

What cannot come along is what lives in the source *package* rather than in the model: a
chart part, an OLE object, a verbatim fragment that points at a part by relationship id.
Each is left out with a `DocumentWarning` naming it — the alternative, a dangling
relationship id, would make Word offer to repair the file. The source document itself is
never changed.

## Comparing two documents

`DocumentComparer.Compare(original, revised)` produces the third document a lawyer calls a
redline and Word calls Compare: the original, with every difference recorded over it as an
ordinary tracked change. Accepting them all yields the revised text; rejecting them all, the
original — the same two documents the author was choosing between, reachable from one file.

```csharp
ComparisonResult result = DocumentComparer.Compare(original, revised, new DocumentCompareOptions
{
    Author = "Compare",
});

await result.Document.SaveAsync("redline.docx");
Console.WriteLine($"{result.Insertions} insertions, {result.Deletions} deletions");
```

The differences go through the same recording machinery an author's own edits go through, so
the result is ordinary revisions — `w:ins` over what arrived, the departed text held in place
under `w:del`, paragraph marks recorded so an accepted paragraph deletion really joins its
neighbours, and `w:trPr` row marks so an accepted table deletion really removes the rows.
Word opens the result showing the changes, counts them, and accepts or rejects them exactly
as if a reviewer had typed them, which the opt-in oracle tests hold it to.

Blocks are aligned first — longest common subsequence over what each block reads as — and a
changed paragraph is then diffed word by word, with the inserted words carrying the revised
document's formatting and the revised paragraph's pictures copied across. What the revised
content leans on — styles, numbering, images — travels the same way `Append` carries it, and
the same things that cannot cross a package boundary are named in `ComparisonResult.Warnings`.

The comparison is of content, not appearance: text that reads the same is left alone even
where its formatting differs — no `w:rPrChange` is fabricated — and the original's section
setup, headers and footers stay as they were. A table that changed in any way is recorded
whole, as deleted rows beside inserted ones, rather than diffed cell by cell.
