# Macros

```csharp
WordDocument document = await WordDocument.LoadAsync("report.docm");

foreach (VbaModule module in document.Macros?.Modules ?? [])
{
    Console.WriteLine($"{module.Name} ({module.Kind})");
    Console.WriteLine(module.Code);
}
```

`WordDocument.Macros` is `null` when the document carries no VBA project. When it does, every
module comes back with its name, what it is attached to, and its source as text.

Legacy documents work the same way, through `Quillwright.Doc`:

```csharp
WordDocument document = await DocReader.LoadAsync("report.doc");
string listing = document.Macros?.ToSourceListing() ?? "no macros";
```

## Where macros live

Word stores a VBA project identically in both of its formats, which is why one reader serves
both. The project is a small compound file ([MS-CFB]) laid out like this:

```
PROJECT            a plain text listing naming each module and what it belongs to
PROJECTwm          the same names again, paired with their UTF-16 spellings
VBA/dir            the module table: names, stream names, and where each source begins
VBA/_VBA_PROJECT   a cache of compiled state, opaque and not read here
VBA/<Module>       one stream per module
```

In a `.docm` or `.dotm` that compound file is the package part `word/vbaProject.bin`. In a
`.doc` it is a storage named `Macros` inside the document's own compound file. Everything below
the top level is the same, so extraction differs only in where it starts looking.

## How the source is stored

A module stream does not begin with its source. It begins with a cache of compiled state whose
length is not recorded in the stream itself — only the `dir` stream knows, in a record giving
the offset of the text. Past that offset the source is held in a *compressed container*
([MS-OVBA] 2.4.1), which is not deflate but a format of its own: a signature byte, then chunks
that each expand to at most 4096 bytes, each chunk a run of literal bytes and two-byte
references pointing back at what has already been produced.

The awkward part is that a reference does not have a fixed shape. How many of its sixteen bits
hold the offset and how many hold the length depends on how far into the current chunk the
decoder has got, so that early references, which cannot point far back, spend fewer bits on the
offset and more on the length. A reference may also reach into the bytes it is itself producing,
which is how a repeated run is expressed. Both details reset at every chunk boundary, which is
why the tests use a module long enough to need several chunks.

A chunk the compressor could not shrink is stored whole instead, and padded out to 4096 bytes
with zeroes that the format cannot tell apart from content — so a container can yield more than
went into it ([MS-OVBA] 2.4.1.3.10). Padding is dropped from the end of a module's source, since
no VBA source ends in null characters.

The `dir` stream is a flat run of records — an identifier, a length, that many bytes — with one
exception, the version record, which carries two bytes the length does not cover. Everything
else is framed by its own length, including the reference records describing type libraries, and
those the reader has no use for are stepped over without being understood.

## What you get

| Member | What it holds |
| --- | --- |
| `VbaProject.Name` | The name the project goes by in the editor, usually `Project` |
| `VbaProject.CodePage` | The code page its single-byte text was written in |
| `VbaProject.Modules` | Every module, in the order the project declares them |
| `VbaProject.References` | The external libraries the project depends on |
| `VbaProject.Protection` | Whether the project was locked, and by whom |
| `VbaProject.ToSourceListing()` | All the source as one listing, with a header per module |
| `VbaModule.Name` | The name the editor shows |
| `VbaModule.Kind` | `Procedural`, `Document`, `Class` or `Form` |
| `VbaModule.Code` | The source, as stored |
| `VbaModule.IsEmpty` | Whether there is anything beyond the attribute preamble |
| `VbaModule.Description` | The description the author gave the module, if any |
| `VbaModule.IsReadOnly`, `IsPrivate` | The two flags a module can carry |
| `VbaModule.Designer` | For a form, its design-time caption and size, and the controls on it |
| `VbaReference.Name`, `Kind`, `Libid` | What is referenced, and how it resolves |
| `VbaReference.OriginalLibid` | For a control reference, the library the generated type library came from |
| `VbaProtection.IsPasswordCorrect(…)` | Whether a candidate is the password the project was locked with |
| `VbaProtection.IsVisible` | Whether the project shows in the editor |

`Code` is the source exactly as stored, which means it keeps the `Attribute` lines the editor
hides. That is the same text a `.bas` export contains, so it can be imported back into the
editor unchanged. It also means the preamble is not evidence of anything: Word gives every
document a `ThisDocument` module whether or not a line was ever written in it, and `IsEmpty` is
what tells a document that merely could run macros from one that does.

## User forms

A form module is two things: the code, which reads like any other module, and a storage beside
it holding what the form looks like. The outer frame is written in plain text; everything on
the form is written in a binary format of its own ([MS-OFORMS]), which `Designer.Controls`
decodes.

```csharp
VbaDesigner? designer = document.Macros!.Modules.Single(m => m.Name == "Launcher").Designer;
foreach (VbaFormControlSite control in designer!.Controls!.AllControls)
    Console.WriteLine($"{control.Kind,-14} {control.Name,-14} {control.Caption} = {control.Value}");

// CommandButton  GoButton       Go =
// Label          TitleLabel     Who is asking? =
// TextBox        NameBox         = Ada
// CheckBox       AgreeBox       Agree = 1
// Frame          GroupFrame     Grouped =
// Label          InnerLabel     Inside the frame =
```

`Controls` walks the form's direct children; `AllControls` walks everything, descending into
frames and pages. Each `VbaFormControlSite` gives the name the code uses to refer to it, its kind,
where it sits and how big it is, its place in the tab order, its tooltip, and — for the
controls that have them — its caption, its value and the group of option buttons it belongs to.
A frame, a page or a multi-page also carries a `Child` holding what is inside it.

Positions and sizes are in hundredths of a millimetre in the file and come back as `Length`, so
`control.Left.Points` reads in the units the designer shows.

Two things are worth knowing about how this is stored, because they explain what the reader
can and cannot recover. Six of the visible controls — text box, list, combo, check box, option
button and toggle — share a single record and are told apart only by one byte inside it, so a
control whose record will not parse comes back as whatever kind its container claimed. And a
control that can hold others is not stored beside the ones around it at all: it gets a storage
of its own, named after its identifier, which is why a frame's caption and size are read from
there rather than from the site that places it.

Nothing in the format is self-describing — each property is found by counting from the last —
so a record that will not parse is caught and left with only what its container said about it,
rather than taking the rest of the form down with it.

## References

A project names the libraries its code binds to, and for working out what a document can reach
that list is often more telling than the source. A reference to `Scripting` means the file
system is in play; a reference to another project means a second file has to be present for the
code to run at all.

```csharp
foreach (VbaReference reference in document.Macros!.References)
    Console.WriteLine($"{reference.Kind,-10} {reference.Name,-12} {reference.Description}");

// Registered  stdole       OLE Automation
// Project     Normal       Normal
// Registered  Scripting    Microsoft Scripting Runtime
// Control     MSForms      Microsoft Forms 2.0 Object Library
```

A `Control` reference is what a user form drags in behind it. Its record is stored in two
halves, and the identifier in the first half is a placeholder of all zeroes — the real one is in
the second, which is where `Libid` comes from.

There is usually a third identifier, and for a control it is the one that matters. Word does not
bind a form to the registered library directly; it generates a type library of its own and
caches it, so `Libid` names a `.exd` file under the temporary directory of whichever machine
last saved the document, with a class identifier minted for that machine. The library it was
generated from is recorded beside it ([MS-OVBA] 2.3.4.2.2.4) and comes back as `OriginalLibid`:

```
Libid          *\G{64CE520E-…}#2.0#0#C:\Users\…\Temp\VBE\MSForms.exd#Microsoft Forms 2.0 …
OriginalLibid  *\G{0D452EE1-E08F-101A-852E-02608C4D0BB4}#2.0#0#C:\Windows\system32\FM20.DLL#…
```

Only a control reference has one; everywhere else `OriginalLibid` is `null`.

## Protection

`CMG`, `DPB` and `GC` in the `PROJECT` stream record whether the project was locked, by whom,
and whether it shows in the editor at all. All three are obfuscated with a byte cipher described
in the specification ([MS-OVBA] 2.4.3), which `Protection` unwinds.

```csharp
VbaProtection protection = document.Macros!.Protection;
if (protection.IsProtected)
    Console.WriteLine($"locked (password: {protection.HasPassword}, visible: {protection.IsVisible})");
```

`Password` is usually `null` even when `HasPassword` is true, because the normal thing to store
is a hash and a hash does not come back. Some files keep the password as text instead, and those
give it up.

A hash cannot be reversed, but it can be checked against. The stored structure keeps the random
key the password was hashed with ([MS-OVBA] 2.4.4), so a candidate can be put through the same
steps and the results compared:

```csharp
if (document.Macros!.Protection.IsPasswordCorrect("letmein"))
    Console.WriteLine("that is the password");
```

That works whichever way the file stores the password, and returns `false` when there is none.
It is a check, not a search: nothing here will look for a password you do not already have.

Locking a project in the editor also hides it, and the format requires the two to agree — a
project with `IsVisible` false is always `IsEditorProtected` as well.

None of this guards the source — see below.

## Reading only

Macros are decoded, never modelled and never rebuilt. Saving a `.docx` copies the project part
through byte for byte along with its content type and its relationship, so what `Macros` reports
and what a saved file runs cannot drift apart — and no edit made through this API could change
the saved macros, because there is no such edit.

Saving to `.doc` is the exception: the binary writer does not write a VBA project at all, so
macros are lost. That is reported through `DocWriteOptions.OnWarning` rather than happening
quietly.

| Format | Read | Preserved on save |
| --- | --- | --- |
| `.docm`, `.dotm` | Yes | Yes, byte for byte |
| `.doc` | Yes | No, with a warning |

Some of the format is read past as well, and it is worth saying which parts. The compiled-state
cache is not interpreted, so nothing here reports on p-code. A form's controls are decoded, but
only as far as what they are and what the designer put in them: the pictures, fonts and colour
details of each control are stepped over rather than read, and the list a combo box or a list
box was filled with at run time was never in the file to begin with. The contents hashes
([MS-OVBA] 2.4.2) are not computed either, so a document's digital signature over its macros is
not verified here. `PROJECTwm` is skipped because it maps
module names between their two spellings and `dir` already carries both, and `PROJECTlk` because
it holds ActiveX licence keys that say nothing about what the code does. The performance caches
Word may leave in `__SRP_*` streams are ignored, which is what the specification asks of a
reader.

## Text encoding

A project records the code page its text was written in, and legacy code pages are not among the
encodings a .NET process knows by default. Rather than register a provider for the whole
process, which is not a library's decision to make, the code page provider is asked directly. If
even that has nothing for the code page in question, the text is read as Latin-1, which keeps
ASCII intact instead of throwing.

## A note on passwords

A password on a VBA project does not hide its source. The password guards the editor: the flag
that asks for it and the hash it is checked against sit in the `PROJECT` stream, while the
source sits beside them compressed but not encrypted. A locked project therefore reads exactly
like an unlocked one. This is a property of the format, not a weakness in this code, and it is
worth knowing in both directions — it is what makes auditing an untrusted document possible, and
it is why a password is not a way to keep macro source private.

The claim is not taken on faith: one of the fixtures is a project Word locked, and a test reads
its modules out with their names, kinds and text, exactly as it does for the unlocked ones.

## Verification

Four kinds of check, because each catches what the others cannot.

**The specification's own byte arrays.** [MS-OVBA] prints worked examples with both sides
shown, and those are the one check here that owes nothing to Word, to a fixture, or to any code
of ours. Section 3.2 gives three compressed containers with their expanded text, covering a
chunk stored whole, a mixture of literals and back-references, and a single reference that
overlaps the bytes it is producing. Section 3.1.6 gives three obfuscated protection values with
their decrypted contents, which is what pins the cipher. Section 3.1.2 gives a reference array
with every field offset printed, which is what settles the framing of a control reference.

**Records built from the layouts.** Word writes a narrow subset of what the format allows, so
some records cannot be reached through a document at all: a reference that leaves out its name,
a module described only in the project's code page. Those are assembled by hand from the record
layouts in 2.3.4.2 and read back, which is the only way to exercise them.

**Fixtures Word wrote.** `tests/fixtures/build-fixtures.ps1` builds two documents through Word
automation, so the compressed source being decoded is Microsoft's own output. One module is 21 KB
of repetitive procedures, which forces the container into several chunks. The second document
carries a user form, which obliges Word to write a control reference — the record whose framing
the specification states least plainly — and which carries one of every control the layout
reader knows, each at a different place and size, plus a frame with two controls of its own. No
two numbers on that form are the same, so a property read from the wrong offset comes back
visibly wrong rather than plausibly right. Each document is saved as both `.docm` and `.doc` in
one session, and a test requires the two formats to yield identical source and identical
layout. The script takes `-Only forms` or `-Only macros` so one pair can be rebuilt without
disturbing the other.

Regenerating the fixtures needs *Trust access to the VBA project object model* turned on for the
duration; the script's header says how, and it should be turned back off afterwards.

A third fixture, `macros-locked`, is a project Word locked with the password `123`. That one the
script cannot build, because Word will not set a project password through automation — which is
exactly why it was worth having by hand. It is what establishes that the reserved byte, the
bit-field, the key, the digest and the terminator of a stored hash are where this code looks for
them, rather than only that the arithmetic over them is right.

**Word itself, on the way out.** An oracle test loads a fixture, saves it through Quillwright,
opens the result in Word and asks whether it still has a VB project — because copying the bytes
through is only half the claim, and a part that lost its content type or its relationship would
look whole here and open without macros there.

```powershell
$env:QUILLWRIGHT_WORD_ORACLE = "1"
dotnet test --filter "Category=word-oracle"
```

The specification was worth reading closely, and worth reading twice.

The first pass turned up a real defect: the extended half of a control reference was being
over-read by twenty bytes, on the assumption that its length field stopped short of the type
library and cookie that trail it. It does not, as the worked example in 3.1.2 shows numerically.
The consequence was not a missing reference but a missing project — the record framing
desynchronised from that point, so a document with a user form yielded no modules at all. That
is why the second fixture exists.

The second pass found nothing that framing depends on, but it did find a record being skipped
that had something to say. A control reference is wrapped in one naming the library it was
generated from (2.3.4.2.2.4), and dropping that wrapper left `MSForms` identified by a path into
a temporary directory on somebody else's machine. Word writes the wrapper, so the fixture had
been carrying the answer all along. Three smaller things came out of the same pass: the name
inside a control reference could carry over onto a following reference that had none of its own,
a module described only in the project's code page reported no description, and the zero padding
of a stored chunk could reach the end of a module's source.

The same pass implemented password checking, and left one thing unproven: the layout of the
bit-field over the stored hash was read off the diagram in 2.4.4.1 and confirmed only by putting
a structure built here back through the reader, which cannot catch an offset that is wrong in
both directions. A locked document then settled it — the reader takes `123` from a hash Word
wrote, and refuses everything else.
