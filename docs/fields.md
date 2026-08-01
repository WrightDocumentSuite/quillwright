# Fields

A field is two things stored side by side: an *instruction* that says how to work out a value,
and the *result* an application last worked out. Nothing in the file format makes the two agree.
Word recomputes the result when it feels like it — on open, on print, on F9 — and until then
what a reader sees is a cache that may be years old.

Quillwright reads both halves, parses the instruction, and recomputes the results that follow
from the document alone.

```csharp
using Quillwright.Editing;

int updated = document.UpdateFields();
```

## Two ways of writing the same thing

The format has two forms and Word writes whichever it feels like: a sequence of five things in
the text — a begin character, the instruction, a separator, the cached result, an end character
— or one `w:fldSimple` element with the instruction as an attribute. `document.Fields()` hands
back both as the same `Field`, in the order they appear, so nothing at the call site has to
care. `Field.IsSimple` says which the file used, if you do.

A `.doc` file has only the character form, so when one is read here a simple field is
represented as that five-character sequence rather than as `w:fldSimple`.

## Reading an instruction

```csharp
FieldInstruction instruction = FieldInstruction.Parse(field.Instruction);

instruction.Name;            // "DATE"
instruction.Arguments;       // positional arguments, unquoted
instruction.DatePicture;     // the \@ switch argument
instruction.Has("h");        // whether \h is present
```

The quoting rules are the field's own (ISO/IEC 29500-1 §17.16.1). Double quotes group an
argument that contains spaces; inside them `\"` is a quote and `\\` is a backslash; outside them
a backslash begins a switch, with no space allowed after it. So
`INCLUDETEXT "E:\\ReadMe.txt"` has one argument and no switches, while `DATE \@ "dd.MM.yyyy"`
has no arguments and one switch.

Whether a switch takes an argument is decided per field by §17.16.5, which this does not model.
The token after a switch is taken as its argument unless that token is itself a switch — which
is what every field in the specification actually does.

## What updates, and what does not

| Field | What it becomes |
| --- | --- |
| `= expression` | The value, by the grammar of §17.16.3 |
| `DATE`, `TIME` | Now, or `FieldUpdateOptions.Now` |
| `CREATEDATE`, `SAVEDATE` | `document.Properties.Created` and `Modified` |
| `AUTHOR`, `TITLE`, `SUBJECT`, `KEYWORDS`, `COMMENTS`, `LASTSAVEDBY` | The core property, or the argument when one overrides it |
| `DOCPROPERTY` | A built-in category of §17.16.1, or a custom property |
| `NUMWORDS`, `NUMCHARS`, `NUMPAGES` | The count `docProps/app.xml` caches |
| `FILENAME` | `FieldUpdateOptions.FileName`, which the document does not know |
| `IF` | One of its two results, by comparing the operands |
| `REF`, a bare bookmark name | The text the bookmark covers |
| `QUOTE` | Its argument |
| `SET` | Nothing; the value lives in the bookmark |
| `SEQ` | Its position among the fields naming the same series |
| `STYLEREF` | The nearest body paragraph carrying the named style |
| `DOCVARIABLE` | `document.Settings.Variables[name]` |
| `USERNAME`, `USERINITIALS`, `USERADDRESS` | `FieldUpdateOptions`, since they belong to the application |

Everything else — `PAGE`, `PAGEREF`, `TOC`, `INDEX`, the mail-merge family — needs either a
layout or data this library does not have. Those keep the result they arrived with and are
marked dirty (`w:fldChar/@w:dirty`), which is the format's own way of telling the next consumer
to recompute them. `UpdateFields` returns how many fields it actually recomputed, so the
difference from `document.Fields().Count()` is the number left to Word.

The one place a layout does exist is rendering: `Quillwright.Pdf` computes `PAGE`, `NUMPAGES`,
`SECTIONPAGES` and `PAGEREF` against its own pagination while drawing — which is what puts real
page numbers on a Word-built table of contents — without touching what the document stores.
See [pdf-export.md](pdf-export.md).

### Sequences and style references

`SEQ Figure` numbers the captions of one series, and what it counts is nowhere in the file: the
number is the field's position among the ones naming the same series, so working it out means
walking the document. `\r N` restarts the count, `\c` repeats the last one without advancing,
`\h` hides the result, and `\*` renumbers it like any other numeric result. `\s`, which
restarts the count at each heading, needs the heading numbering and is left dirty.

`STYLEREF "Heading 1"` quotes the nearest paragraph carrying a style, named either by its
identifier or by its name. Word answers it with the nearest one *on the page*, which is why the
field is mostly used in headers — and why one in a header cannot be answered here at all. In
the body the nearest one in the text is the same paragraph, so that is what this reads, looking
backwards first and forwards only if nothing is above. A switch asking for a page or a number
(`\l`, `\n`, `\p`, `\r`, `\w`) is left dirty.

### Document variables

A document variable is a named value the document carries for its own use (`w:docVars`). Unlike
a custom property, it is invisible to a reader and is set by a macro — templates use them to
keep runtime state that macros populate.

```csharp
document.Settings.Variables["Region"] = "North";
foreach (string name in document.Settings.Variables.Names)
    Console.WriteLine($"{name} = {document.Settings.Variables[name]}");
```

## Formulas

The `=` field is an arithmetic expression over constants, bookmarks, table cells and the
functions of §17.16.3.4.

```csharp
paragraph.AppendField("=SUM(ABOVE)", "0");
paragraph.UpdateFields();
```

Operators, in the precedence §17.16.3.3 gives them: unary `-`, then `^`, then `*` and `/`, then
the postfix `%`, then `+` and `-`, then `=`, `<>`, `<`, `<=`, `>` and `>=`, which yield 1 or 0.
Every term is a real number, so `=1/3` is `0.3333333333` and not zero.

Functions: `ABS`, `AND`, `AVERAGE`, `COUNT`, `DEFINED`, `FALSE`, `IF`, `INT`, `MAX`, `MIN`,
`MOD`, `NOT`, `OR`, `PRODUCT`, `ROUND`, `SIGN`, `SUM`, `TRUE`. `MOD` keeps the sign of the
dividend, as §17.16.3.4 requires: `MOD(-21,5)` is `-1`, not `4`.

A name that is not a function is a bookmark, and inside a table it may be a cell instead.
Cell references work as §17.16.3.5 describes them: `A1`, the range `A1:B3`, a whole column
`B:B`, a whole row `1:1`, and the directions `ABOVE`, `BELOW`, `LEFT` and `RIGHT`, which run
away from the cell holding the formula and stop at the first cell that holds no number.

The letter is a column of the table's *grid*, not a count of the cells in the row. The two
part company as soon as a cell spans more than one column: after a two-column merge in row 1,
the next cell along is `C1` rather than `B1`, and a spanning cell is counted once by a
direction or a range rather than once per column it covers.

## Formatting switches

`\#` formats the value with a numeric picture (§17.16.4.2). A picture is positional: each item
stands for one place of the result, and the digits are fitted into it from the radix point
outwards.

| Field | Result |
| --- | --- |
| `=4+5 \# 00.00` | `09.00` |
| `=9+6 \# $###` | `$ 15` |
| `=2456800 \# $#,###,###` | `$2,456,800` |
| `=111053+111439 \# x##` | `492` |
| `=0-1250.5 \# "$#,##0.00;($#,##0.00)"` | `($1,250.50)` |

`\@` is a date picture (§17.16.4.1). The picture items are the ones .NET uses, except that Word
spells out AM/PM explicitly — `AM/PM` and `A/P` rather than `tt` and `t` — and any other letter is
taken literally rather than as a format specifier.

`\*` recases a text result (`Upper`, `Lower`, `Caps`, `FirstCap`) or renumbers a numeric one
(`Arabic`, `ROMAN`, `roman`, `ALPHABETIC`, `alphabetic`, `Ordinal`, `Hex`). `MERGEFORMAT` and
`CHARFORMAT` say what run formatting to keep rather than what text to produce, and are left to
the consumer: the formatting of a field result is not rewritten.

## Reproducible updates

```csharp
document.UpdateFields(new FieldUpdateOptions
{
    Now = new DateTime(2026, 1, 31),
    Culture = CultureInfo.GetCultureInfo("en-GB"),
    FileName = "report.docx",
});
```

## One thing to watch

A `Field` is a view onto offsets in a paragraph. Writing a result of a different length moves
every offset after it, so a `Field` value read before an update points at the wrong place after
one. Read it again from `paragraph.Fields()`, or call `paragraph.UpdateFields()` and
`document.UpdateFields()`, which take the list again for each field they touch.
