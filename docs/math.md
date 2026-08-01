# Equations

Office Math (ISO/IEC 29500-1 §22.1) is a vocabulary of 124 elements, most of which say how a
formula is *drawn*: the spacing between operators, where a long equation may break, which font
the italics come from. A handful say what it *means* — this part is a numerator, that one is an
exponent, the sum runs over these limits.

Quillwright models every object the vocabulary declares and keeps the drawing properties
verbatim, which is the same bargain it makes with pictures and drawings. The
[inventory](#the-inventory) below counts exactly which of the 124 elements are which; where the
support sits against the other formats is in [conformance.md](conformance.md).

```csharp
using Quillwright.Model;

foreach (MathObject equation in document.Paragraphs
             .SelectMany(p => p.Objects)
             .Select(o => o.Object)
             .OfType<MathObject>())
{
    Console.WriteLine(equation.GetText());   // x=1/2
}
```

## The tree

A `MathObject` is anchored in a paragraph like a picture, and holds one `MathElement` for each
equation it carries — one for an inline equation, and one per line for a display paragraph that
holds several. `Content` is the first of them. An element is an ordered list of `MathNode`s:

| Node | Element | Reads as |
| --- | --- | --- |
| `MathRun` | `m:r` | its text |
| `MathFraction` | `m:f` | `(a+b)/2` |
| `MathRadical` | `m:rad` | `√(x+1)`, `3√x` |
| `MathScript` | `m:sSub`, `m:sSup`, `m:sSubSup`, `m:sPre` | `a_n`, `e^x`, `x_i^2` |
| `MathNary` | `m:nary` | `∑_(i=1)^n i` |
| `MathDelimiter` | `m:d` | `[x]`, `(a\|b)` |
| `MathFunction` | `m:func` | `sin x` |
| `MathMatrix` | `m:m`, `m:mr` | `(a, b; c, d)` |
| `MathBar` | `m:bar` | its base |
| `MathAccent` | `m:acc` | base plus the mark |
| `MathGroupCharacter` | `m:groupChr` | its base |
| `MathBox` | `m:box` | its base |
| `MathBorderBox` | `m:borderBox` | its base |
| `MathArray` | `m:eqArr` | `x+y=1; x-y=0` |
| `MathLimit` | `m:limLow`, `m:limUpp` | `lim_(n→∞)`, `max^k` |
| `MathPhantom` | `m:phant` | nothing, unless `Show` says otherwise |
| `RawMath` | WordprocessingML inside an equation | whatever text is inside it |

The four script elements differ only in which scripts they carry and which side of the base
they sit on, so one node covers all four and which element to write follows from the tree. The
two limits differ only in which side the limit goes, and are one node for the same reason. A
limit is not a script: a script sits at the corner of its base, a limit squarely under or over
it, and §22.1 keeps them apart for that reason.

`GetText()` writes the tree out as a line, putting brackets round a part only where running it
together would change what it says: `1/2` needs none, `(a+b)/2` does. That is what makes an
equation findable by `document.Replace` and readable in `document.GetText()`, which used to see
nothing but a placeholder. A phantom reads as nothing, because taking up room without being
drawn is exactly what it is for.

## The inventory

All 124 elements of §22.1.2, against what becomes of each. The three groups answer three
different questions: is it modelled, does it survive an edit, and — for the rest — what exactly
is being given up.

| What becomes of it | Count | Elements |
| --- | ---: | --- |
| **Objects**, each a node of the tree | 20 | `acc` `bar` `borderBox` `box` `d` `eqArr` `f` `func` `groupChr` `limLow` `limUpp` `m` `nary` `phant` `r` `rad` `sPre` `sSub` `sSubSup` `sSup` |
| **Structure**, the parts an object is made of | 12 | `deg` `den` `e` `fName` `lim` `mr` `num` `oMath` `oMathPara` `sub` `sup` `t` |
| **Properties with a field of their own** | 23 | `begChr` `chr` `degHide` `endChr` `hideBot` `hideLeft` `hideRight` `hideTop` `jc` `pos` `sepChr` `show` `strikeBLTR` `strikeH` `strikeTLBR` `strikeV` `subHide` `supHide` `transp` `type` `zeroAsc` `zeroDesc` `zeroWid` |
| **Property wrapper elements**, read for what is inside them | 20 | `accPr` `barPr` `borderBoxPr` `boxPr` `dPr` `eqArrPr` `fPr` `funcPr` `groupChrPr` `limLowPr` `limUppPr` `mPr` `naryPr` `oMathParaPr` `phantPr` `radPr` `sPrePr` `sSubPr` `sSubSupPr` `sSupPr` |
| **Carried verbatim**, so they survive a regenerated equation | 4 | `ctrlPr` `rPr` `argPr` `argSz` |
| **Drawing and spacing**, kept only while the equation is untouched | 45 | `aln` `alnScr` `baseJc` `brk` `brkBin` `brkBinSub` `cGp` `cGpRule` `count` `cSp` `defJc` `diff` `dispDef` `grow` `interSp` `intLim` `intraSp` `limLoc` `lit` `lMargin` `mathFont` `mathPr` `maxDist` `mc` `mcJc` `mcPr` `mcs` `naryLim` `noBreak` `nor` `objDist` `opEmu` `plcHide` `postSp` `preSp` `rMargin` `rSp` `rSpRule` `scr` `shp` `smallFrac` `sty` `vertJc` `wrapIndent` `wrapRight` |

Three of those rows deserve a word.

`mathPr` and the settings under it — `mathFont`, `defJc`, `brkBin`, the four margins and the
three spacings — are not part of an equation at all. They live in `settings.xml`, which is
[carried through whole](wordprocessingml-coverage.md), so they are safe by a different mechanism
and appear in the last row only because §22.1 declares them.

Six elements are in the last row and survive anyway when they appear in the one place a run
keeps whole. `aln`, `brk`, `lit`, `nor`, `scr` and `sty` are children of `m:rPr` as well as of a
properties element, and a run's `m:rPr` is kept verbatim in `MathRun.PropertiesXml`. The same
`aln` inside a box's properties is not.

And an argument's own `argPr` is not modelled but is not lost either: an element inside `m:e`
that the tree has no node for becomes a `RawMath` in the argument, at the position it held, and
is written back where it was.

## Round trips

An equation read from a file keeps the markup it arrived as, and is written back byte for byte.
That is the only way the drawing row of the inventory survives.

Editing the tree is an ordinary object-graph edit, so nothing notices it. Say so:

```csharp
var fraction = (MathFraction)equation.Content.Nodes[1];
((MathRun)fraction.Denominator.Nodes[0]).Text = "3";
equation.Invalidate();
```

### What survives an `Invalidate`

After `Invalidate` the markup is regenerated from what the tree holds, and the rule for what
that keeps is one sentence: **everything in the tree, plus the control properties of every
object; nothing else.**

`m:ctrlPr` is the last child of every properties element in the vocabulary, and it carries the
character formatting of the character the object is drawn around — its font, its size, whether
it is italic, whether a line may break there. None of that is structure, so the model does not
interpret it; all of it is visible, so `MathNode.ControlPropertiesXml` carries it and the writer
puts it back where the schema wants it. Losing it is the difference between an italic variable
and an upright one, which is why it is the one exception.

What is legitimately lost is the drawing row above: the alignment of an array, the gaps between
matrix columns, where a long equation is allowed to break, the size of an argument. Those change
how an equation is spaced, not what it says, and a tree that carried them would be a second copy
of the markup rather than a model. An equation a caller built has no original markup and is
always generated, so the same rule applies to it from the start.

## Building one

```csharp
var fraction = new MathFraction();
fraction.Numerator.Nodes.Add(new MathRun("a+b"));
fraction.Denominator.Nodes.Add(new MathRun("2"));

var equation = new MathObject();
equation.Content.Nodes.Add(new MathRun("y="));
equation.Content.Nodes.Add(fraction);

paragraph.AppendObject(equation);
```

Set `IsDisplay` to put the equation on a line of its own (`m:oMathPara`) rather than in the run
of text, `Justification` to say how it sits across that line, and add to `Equations` to put more
than one equation in the same display paragraph:

```csharp
var display = new MathObject { IsDisplay = true, Justification = MathJustification.Left };
display.Content.Nodes.Add(new MathRun("a=1"));
display.Equations.Add(MathElement.Of("b=2"));
```

## What is left alone

- WordprocessingML inside an equation — a tracked insertion, a bookmark, a hyperlink — belongs
  to §17 rather than §22.1 and is kept as the bytes it arrived as.
- An equation in a Strict package names its vocabulary under `purl.oclc.org`. Both spellings
  are read, so a Strict equation is modelled like any other; untouched, it goes back out as the
  bytes it arrived as, still in its own vocabulary.
- Nothing here draws an equation. `Quillwright.Pdf` lays the tree out with real font metrics —
  see [pdf-export.md](pdf-export.md) — and the Markdown exporter writes the linear text.
- A `.doc` has no equations at all. Saving one flattens the equation to its text, with a
  warning — see [doc-export.md](doc-export.md).
