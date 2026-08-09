using System.Globalization;
using Quillwright.Diagnostics;
using Quillwright.Model;
using Quillwright.Primitives;

namespace Quillwright.Doc;

/// <summary>
/// Turns the shape anchors of the text into the shapes they stand for, so far as this reader
/// models them: a text box becomes a text box, a picture frame becomes a floating picture,
/// and everything else says it was left behind.
/// </summary>
internal static partial class DocConverter
{
    private const string UnconvertedShape =
        "A floating drawing is anchored in the text but stored as a shape this reader does not convert.";

    /// <summary>
    /// The shape anchored at a character position, when it is one whose content comes across.
    /// </summary>
    /// <param name="context">The file being read.</param>
    /// <param name="position">Absolute character position of the anchor character.</param>
    private static InlineObject? ShapeAt(DocReadContext context, int position)
    {
        DocStory story = StoryOf(context, position);
        if (story.Shapes.At(position - story.Start) is not { } anchor)
            return Missing(context, WarningCode.PreservedVerbatim, UnconvertedShape);

        OfficeArtShape? drawing = context.Drawings.ById(anchor.ShapeId);
        if (story.Boxes.For(anchor.ShapeId) is { } range && context.ClaimTextbox(anchor.ShapeId))
            return TextBox(context, anchor, drawing, story.TextStart + range.Start, story.TextStart + range.End);

        return FloatingPicture(context, anchor)
            ?? Lettering(context, anchor, drawing)
            ?? Missing(context, WarningCode.PreservedVerbatim, Unconverted(drawing));
    }

    /// <summary>
    /// A shape drawn as lettering rather than as text — WordArt — keeps its words in a property
    /// of its own ([MS-ODRAW] 2.3.22.1) and nowhere else, so a reader that skips the shape loses
    /// them completely. The words come across in a box; the lettering does not.
    /// </summary>
    private static Shape? Lettering(DocReadContext context, DocShapeAnchor anchor, OfficeArtShape? drawing)
    {
        if (drawing?.Appearance.GeometryText is not { Length: > 0 } text)
            return null;

        var content = new Model.TextBox();
        foreach (string line in text.Split('\n'))
            content.AddParagraph(line);

        context.Warn(
            WarningCode.PreservedVerbatim,
            "The words of a WordArt shape were kept as ordinary text; the lettering they were drawn with was not.");

        return Framed(anchor, drawing, content);
    }

    /// <summary>What a shape that did not come across was, for a warning that says which.</summary>
    private static string Unconverted(OfficeArtShape? drawing) => drawing is { } shape
        ? $"A floating drawing — {shape.TypeName} — is anchored in the text, and this reader converts only pictures, text boxes and lettering."
        : UnconvertedShape;

    /// <summary>
    /// The picture a floating shape displays, when it displays one. The drawing itself holds
    /// no image: it names a place in the store the whole document shares ([MS-ODRAW] 2.3.23.5),
    /// and the rectangle the anchor gives is the size it is drawn at.
    /// </summary>
    /// <remarks>
    /// Where the shape sits and how the text wraps around it comes across with it, out of the
    /// flag word of the anchor. What does not is the shape's own decoration — its border, its
    /// fill, its effects — which lives in a drawing this reader does not translate.
    /// </remarks>
    private static Picture? FloatingPicture(DocReadContext context, DocShapeAnchor anchor)
    {
        if (context.Drawings.ById(anchor.ShapeId) is not { IsPicture: true } shape ||
            context.Blips.For(shape) is not { } image)
            return null;

        context.Images.Add(image);
        return new Picture
        {
            Image = image,
            Width = anchor.Width > 0 ? Length.FromTwips(anchor.Width) : image.NaturalWidth,
            Height = anchor.Height > 0 ? Length.FromTwips(anchor.Height) : image.NaturalHeight,
            IsInline = false,
            Anchor = Position(anchor, shape.Position),
        };
    }

    /// <summary>
    /// Where the shape sits. The text's anchor gives the rectangle and how the text flows
    /// round it ([MS-DOC] 2.9.253); the drawing gives the finer answer to the same question —
    /// whether the shape is offset by a distance or lined up with an edge
    /// ([MS-ODRAW] 2.3.4.19 to 2.3.4.22). A watermark is centred on the page rather than
    /// placed at a distance from it, so a reader that has only the rectangle puts every one of
    /// them in the top left corner.
    /// </summary>
    private static PictureAnchor Position(DocShapeAnchor anchor, ShapePosition placement) => new()
    {
        OffsetX = Length.FromTwips(anchor.Left),
        OffsetY = Length.FromTwips(anchor.Top),
        HorizontalFrom = placement.RelativeToHorizontal switch
        {
            1 => AnchorOrigin.Margin,
            2 => AnchorOrigin.Page,
            3 => AnchorOrigin.Column,
            4 => AnchorOrigin.Character,
            _ => anchor.HorizontalOrigin switch
            {
                1 => AnchorOrigin.Page,
                2 => AnchorOrigin.Column,
                _ => AnchorOrigin.Margin,
            },
        },
        VerticalFrom = placement.RelativeToVertical switch
        {
            1 => AnchorOrigin.Margin,
            2 => AnchorOrigin.Page,
            3 => AnchorOrigin.Paragraph,
            4 => AnchorOrigin.Line,
            _ => anchor.VerticalOrigin switch
            {
                1 => AnchorOrigin.Page,
                2 => AnchorOrigin.Paragraph,
                _ => AnchorOrigin.Margin,
            },
        },
        HorizontalAlignment = Alignment(placement.Horizontal),
        VerticalAlignment = Alignment(placement.Vertical),

        // Wrapping 3 means the text takes no notice of the shape at all, which is the one
        // case where whether it is in front or behind decides what a reader sees.
        Wrapping = anchor.Wrapping switch
        {
            1 => TextWrapping.TopAndBottom,
            3 => TextWrapping.None,
            4 => TextWrapping.Tight,
            5 => TextWrapping.Through,
            _ => TextWrapping.Square,
        },
        Sides = anchor.WrappingSides switch
        {
            1 => WrapSides.Left,
            2 => WrapSides.Right,
            3 => WrapSides.Largest,
            _ => WrapSides.Both,
        },
        BehindText = anchor.Wrapping == 3 && anchor.BehindText,
    };

    /// <summary>Which edge the shape lines up with, from <c>posh</c> or <c>posv</c>.</summary>
    private static AnchorAlignment Alignment(int property) => property switch
    {
        1 => AnchorAlignment.Start,
        2 => AnchorAlignment.Center,
        3 => AnchorAlignment.End,
        4 => AnchorAlignment.Inside,
        5 => AnchorAlignment.Outside,
        _ => AnchorAlignment.Offset,
    };

    /// <summary>Reads one text box's words and wraps them in markup that says where it sits.</summary>
    /// <remarks>
    /// The binary format keeps a shape's geometry in a drawing this reader does not translate,
    /// so the box is rebuilt as the simplest VML that carries its position, its size and the
    /// fill and outline the drawing states. That is enough for the words to stay in a box
    /// rather than being flattened into the body, and for the box to keep the frame it was
    /// drawn with — everything else about the shape is left behind.
    /// </remarks>
    private static Shape? TextBox(
        DocReadContext context, DocShapeAnchor anchor, OfficeArtShape? drawing, int from, int to)
    {
        var content = new Model.TextBox();
        foreach (DocParagraph entry in ReadParagraphs(context, from, to))
            content.Blocks.Add(entry.Paragraph);

        if (content.Blocks.Count == 0)
            return null;

        context.Warn(
            WarningCode.PreservedVerbatim,
            "A text box was rebuilt from its position, size, fill and outline; nothing else of the shape it was drawn with is converted.");

        return Framed(anchor, drawing, content);
    }

    /// <summary>
    /// Puts content inside the simplest VML rectangle that carries what the drawing said, and
    /// offers the same reading of it as properties so that a renderer need not parse it back.
    /// </summary>
    private static Shape Framed(DocShapeAnchor anchor, OfficeArtShape? drawing, Model.TextBox content)
    {
        OfficeArtAppearance appearance = drawing?.Appearance ?? OfficeArtAppearance.Plain;
        return new Shape([Frame(anchor, appearance), "</w:txbxContent></v:textbox></v:rect></w:pict>"], content)
        {
            Width = Length.FromTwips(anchor.Width),
            Height = Length.FromTwips(anchor.Height),
            IsInline = false,
            Anchor = Position(anchor, drawing?.Position ?? ShapePosition.Unstated),
            Fill = appearance.Fill,
            Outline = appearance.LineColor is { } line
                ? Styles.BorderLine.Single(appearance.LineWidth ?? Length.FromPoints(0.75), line)
                : null,
        };
    }

    private static string Frame(DocShapeAnchor anchor, OfficeArtAppearance appearance) =>
        $"<w:pict><v:rect id=\"shape{anchor.ShapeId.ToString(CultureInfo.InvariantCulture)}\"" +
        $" style=\"position:absolute;left:{Points(anchor.Left)};top:{Points(anchor.Top)};" +
        $"width:{Points(anchor.Width)};height:{Points(anchor.Height)}\"" +
        Painted(appearance) +
        "><v:textbox><w:txbxContent>";

    /// <summary>
    /// The fill and the stroke as VML states them: an attribute each, and the word
    /// <c>false</c> where the drawing said the shape has none, because leaving the attribute
    /// out means the opposite.
    /// </summary>
    private static string Painted(OfficeArtAppearance appearance)
    {
        var attributes = new System.Text.StringBuilder();
        attributes.Append(appearance.Fill is { } fill ? $" fillcolor=\"{Hex(fill)}\"" : " filled=\"false\"");

        if (appearance.LineColor is not { } line)
            return attributes.Append(" stroked=\"false\"").ToString();

        attributes.Append($" strokecolor=\"{Hex(line)}\"");
        if (appearance.LineWidth is { } width)
            attributes.Append($" strokeweight=\"{width.Points.ToString("0.##", CultureInfo.InvariantCulture)}pt\"");

        return attributes.ToString();
    }

    private static string Hex(WordColor color) => "#" + color.ToHex();

    /// <summary>A measurement in twips as the points VML wants, to two decimals.</summary>
    private static string Points(int twips) =>
        (twips / 20d).ToString("0.##", CultureInfo.InvariantCulture) + "pt";

    /// <summary>
    /// Which story a character position falls in, and where that story's shapes and text boxes
    /// are described ([MS-DOC] 2.3).
    /// </summary>
    private static DocStory StoryOf(DocReadContext context, int position)
    {
        FileInformationBlock fib = context.Fib;
        int header = fib.MainTextLength + fib.FootnoteTextLength;
        int textbox = header + fib.HeaderTextLength + fib.CommentTextLength + fib.EndnoteTextLength;

        return position >= header && position < header + fib.HeaderTextLength
            ? new DocStory(header, context.HeaderShapes, context.HeaderTextboxes, textbox + fib.TextboxTextLength)
            : new DocStory(0, context.MainShapes, context.Textboxes, textbox);
    }

    /// <summary>Where a story begins, what it draws, and where the words of its boxes are kept.</summary>
    private readonly record struct DocStory(int Start, DocShapeTable Shapes, DocTextboxTable Boxes, int TextStart);

    /// <summary>
    /// Reads the charts among the embedded objects. A chart in a legacy document is a
    /// Microsoft Graph object rather than a part of the format ([MS-OGRAPH]), so it is
    /// recognised by the program that owns it and read out of the compound file inside it.
    /// </summary>
    private static void ReadCharts(WordDocument document, DocReadContext context)
    {
        foreach (EmbeddedObject embedded in context.EmbeddedObjects)
        {
            if (embedded.ProgramId?.StartsWith("MSGraph.", StringComparison.OrdinalIgnoreCase) != true)
                continue;

            if (GraphChartReader.Read(embedded, context.LoadBudget.Budget) is { } chart)
            {
                document.ChartList.Add(chart);
                continue;
            }

            document.ChartList.Add(new Chart { Location = embedded.Location, Title = embedded.DisplayName });
            context.Warn(
                WarningCode.PreservedVerbatim,
                "A chart was found as an embedded Microsoft Graph object whose data could not be decoded.");
        }
    }
}
