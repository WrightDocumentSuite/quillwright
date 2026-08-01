using System.Globalization;
using Quillwright.IO;
using Quillwright.Primitives;

namespace Quillwright.Vba.OForms;

/// <summary>
/// Reads the layout of a user form out of the storage beside its code ([MS-OFORMS] 2.1.2).
/// </summary>
/// <remarks>
/// <para>
/// A form is a little file system of its own. The stream <c>f</c> describes the container and
/// lists the controls on it — their names, types, places and tab order — but not what is in
/// them. The stream <c>o</c> holds the controls themselves, one after another with nothing
/// between them, found only by the byte count each entry in <c>f</c> declares. A control that
/// can hold other controls is not in <c>o</c> at all; it gets a storage of its own named after
/// its identifier, holding an <c>f</c> and an <c>o</c> of the same shape.
/// </para>
/// <para>
/// Nothing in the format is self-describing, so a control that will not parse would otherwise
/// take the rest of the form with it. Each record is therefore read inside the bounds its own
/// size field declares, and a failure leaves that one control with only what the parent said
/// about it.
/// </para>
/// </remarks>
internal static class VbaFormReader
{
    /// <summary>How deep the reader will follow frames inside frames before giving up.</summary>
    private const int MaxDepth = 12;

    /// <summary>A form measures itself in hundredths of a millimetre ([MS-OFORMS] 2.4.1).</summary>
    private static Length FromHimetric(int value) => Length.FromMillimeters(value / 100.0);

    /// <summary>Reads the controls of a form, or returns <see langword="null"/> when it has none.</summary>
    /// <param name="container">The compound file the project lives in.</param>
    /// <param name="storage">Path of the form's storage inside the container.</param>
    public static VbaFormControl? Read(CompoundFile container, string storage)
    {
        try
        {
            return ReadContainer(container, storage, VbaFormControlKind.Form, depth: 0);
        }
        catch (Exception error) when (error is InvalidDataException or IndexOutOfRangeException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Reads one container: the form itself, or a frame or page nested inside it.</summary>
    /// <param name="container">The compound file the project lives in.</param>
    /// <param name="storage">Path of this container's storage.</param>
    /// <param name="kind">Whether this is the form, a frame or a page.</param>
    /// <param name="depth">How far down the nesting this container sits.</param>
    private static VbaFormControl? ReadContainer(CompoundFile container, string storage, VbaFormControlKind kind, int depth)
    {
        if (container.ReadStream(storage + "/f") is not { Length: >= 8 } stream)
            return null;

        var reader = new OFormsReader(stream, 0, stream.Length);
        OFormsValues form = OFormsPropertyBag.Read(reader, OFormsSchemas.Form);
        OFormsPropertyBag.SkipStreamData(reader, OFormsSchemas.Form, form.Mask);

        List<SiteRecord> sites = ReadSites(reader, form);
        List<VbaFormControlSite> controls = Assemble(container, storage, sites, kind, depth);
        (int Width, int Height) size = form.Pair(OFormsSchemas.DisplayedSize) ?? (0, 0);

        return new VbaFormControl(kind, controls)
        {
            Caption = form.Text(OFormsSchemas.Caption),
            Width = FromHimetric(size.Width),
            Height = FromHimetric(size.Height),
            BackColor = form.Number(OFormsSchemas.BackColor),
            ForeColor = form.Number(OFormsSchemas.ForeColor),
        };
    }

    /// <summary>
    /// Reads the part of the form stream that describes the embedded controls
    /// ([MS-OFORMS] 2.2.10.6): an optional table of classes the format has no number for, then
    /// the depth and type of every control, then one record per control.
    /// </summary>
    /// <param name="reader">Cursor positioned at the start of the site data.</param>
    /// <param name="form">What the container's own record said.</param>
    private static List<SiteRecord> ReadSites(OFormsReader reader, OFormsValues form)
    {
        // The class table is written unless the form asked for it to be left out, and a form
        // that stored no boolean properties at all did not ask.
        const uint DontSaveClassTable = 0x8000;
        if (((form.Number(OFormsSchemas.BooleanProperties) ?? 0) & DontSaveClassTable) == 0)
        {
            int classes = reader.UInt16();
            for (int i = 0; i < classes && reader.Remaining >= 4; i++)
            {
                reader.Skip(2);
                reader.Skip(reader.UInt16());
            }
        }

        int count = (int)reader.UInt32();
        reader.Skip(4);
        if (count is <= 0 or > 4096)
            return [];

        List<int> depths = ReadDepths(reader, count);

        var sites = new List<SiteRecord>(depths.Count);
        foreach (int depth in depths)
        {
            if (reader.Remaining < 4)
                break;

            // Each record declares its own size, so reading it also says where the next begins.
            OFormsValues values = OFormsPropertyBag.Read(reader.Nested(reader.Remaining), OFormsSchemas.Site);
            sites.Add(new SiteRecord(depth, values));
            reader.Position = Math.Min(reader.End, values.End);
        }

        return sites;
    }

    /// <summary>
    /// Reads the depth of each embedded control ([MS-OFORMS] 2.2.10.7). One entry can stand for
    /// a run of controls that share a depth and a type, so the array is shorter than the list
    /// it describes.
    /// </summary>
    /// <param name="reader">Cursor positioned at the start of the array.</param>
    /// <param name="count">How many controls the array must account for.</param>
    private static List<int> ReadDepths(OFormsReader reader, int count)
    {
        const int Counted = 0x80;
        int start = reader.Position;
        var depths = new List<int>(count);

        while (depths.Count < count && reader.Remaining >= 2)
        {
            int depth = reader.Byte();
            int typeOrCount = reader.Byte();
            int repeat = 1;
            if ((typeOrCount & Counted) != 0)
            {
                repeat = typeOrCount & ~Counted;
                reader.Skip(1);
            }

            for (int i = 0; i < repeat && depths.Count < count; i++)
                depths.Add(depth);
        }

        // The array is padded out to a length divisible by four before the records begin.
        int written = reader.Position - start;
        reader.Skip((4 - (written % 4)) % 4);
        return depths;
    }

    /// <summary>
    /// Turns the flat list of records into the tree of controls it describes, filling each one
    /// in from the object stream or from a storage of its own.
    /// </summary>
    /// <param name="container">The compound file the project lives in.</param>
    /// <param name="storage">Path of the storage the records were read from.</param>
    /// <param name="sites">The records, in the order the form stores them.</param>
    /// <param name="parent">What the container holding these records is.</param>
    /// <param name="depth">How far down the nesting this container sits.</param>
    private static List<VbaFormControlSite> Assemble(
        CompoundFile container, string storage, List<SiteRecord> sites, VbaFormControlKind parent, int depth)
    {
        var built = new List<VbaFormControlSite>(sites.Count);
        var nested = new List<List<VbaFormControlSite>>(sites.Count);
        var roots = new List<VbaFormControlSite>();
        var openAt = new List<int>();

        foreach (SiteRecord record in sites)
        {
            VbaFormControlSite site = Build(record.Values, parent);
            built.Add(site);
            nested.Add([]);

            // A record deeper than the one before it belongs to it; the list of open parents
            // is trimmed back to the depth this record sits at.
            if (record.Depth > 0 && record.Depth <= openAt.Count)
                nested[openAt[record.Depth - 1]].Add(site);
            else
                roots.Add(site);

            openAt.RemoveRange(Math.Min(record.Depth, openAt.Count), Math.Max(0, openAt.Count - record.Depth));
            openAt.Add(built.Count - 1);
        }

        ReadObjectStream(container, storage, built);

        for (int i = 0; i < built.Count; i++)
            Attach(container, storage, built[i], nested[i], depth);

        return roots;
    }

    /// <summary>Gives a control that holds others the children it holds.</summary>
    /// <param name="container">The compound file the project lives in.</param>
    /// <param name="storage">Path of the storage the control's record was read from.</param>
    /// <param name="site">The control being filled in.</param>
    /// <param name="inlineChildren">Children the depth array already gave it, if any.</param>
    /// <param name="depth">How far down the nesting the containing storage sits.</param>
    private static void Attach(
        CompoundFile container, string storage, VbaFormControlSite site, List<VbaFormControlSite> inlineChildren, int depth)
    {
        if (inlineChildren.Count > 0)
        {
            site.Child = new VbaFormControl(site.Kind, inlineChildren);
            return;
        }

        if (!OFormsControlKind.IsParent(site.Kind) && site.Kind != VbaFormControlKind.MultiPage)
            return;
        if (depth >= MaxDepth)
            return;

        string child = storage + "/" + StorageName(site.Id);
        if (!container.HasStorage(child))
            return;

        site.Child = ReadContainer(container, child, site.Kind, depth + 1);
        if (site.Child is null)
            return;

        if (site.Kind == VbaFormControlKind.MultiPage)
            OrderPages(container, child, site.Child);

        // A control that holds others is not written beside them, so its own storage is the
        // only place its caption and its size are recorded.
        site.Caption ??= site.Child.Caption;
        if (site.Width == default && site.Height == default)
        {
            site.Width = site.Child.Width;
            site.Height = site.Child.Height;
        }
    }

    /// <summary>
    /// Puts the pages of a multi-page into the order it shows them, which the extended stream
    /// records as a list of identifiers ([MS-OFORMS] 2.2.6.1).
    /// </summary>
    /// <param name="container">The compound file the project lives in.</param>
    /// <param name="storage">Path of the multi-page's storage.</param>
    /// <param name="pages">The pages as the form stream listed them.</param>
    private static void OrderPages(CompoundFile container, string storage, VbaFormControl pages)
    {
        if (container.ReadStream(storage + "/x") is not { Length: >= 8 } stream)
            return;

        IReadOnlyList<VbaFormControlSite> current = pages.Controls;
        var reader = new OFormsReader(stream, 0, stream.Length);

        // The stream opens with one record per page plus a leading one that is ignored, and
        // only then the record whose trailing array names the pages in order.
        for (int i = 0; i <= current.Count && reader.Remaining >= 4; i++)
        {
            OFormsValues page = OFormsPropertyBag.Read(reader.Nested(reader.Remaining), OFormsSchemas.Page);
            reader.Position = Math.Min(reader.End, page.End);
        }

        if (reader.Remaining < 8)
            return;

        OFormsValues properties = OFormsPropertyBag.Read(reader.Nested(reader.Remaining), OFormsSchemas.MultiPage);
        reader.Position = Math.Min(reader.End, properties.End);

        var order = new List<int>(current.Count);
        while (reader.Remaining >= 4 && order.Count < current.Count)
            order.Add(reader.Int32());

        Dictionary<int, VbaFormControlSite> byId = current
            .GroupBy(static page => page.Id)
            .ToDictionary(static group => group.Key, static group => group.First());

        if (order.Count != current.Count || order.Distinct().Count() != order.Count || order.Any(id => !byId.ContainsKey(id)))
            return;

        pages.Reorder(order.Select(id => byId[id]));
    }

    /// <summary>
    /// Reads the object stream, which holds the controls that cannot themselves hold others,
    /// laid end to end. Each record is found only by the length its site declared, so a site
    /// that declares one is stepped over whether or not this reader understands it.
    /// </summary>
    /// <param name="container">The compound file the project lives in.</param>
    /// <param name="storage">Path of the storage the controls belong to.</param>
    /// <param name="sites">The controls, in the order the form stores them.</param>
    private static void ReadObjectStream(CompoundFile container, string storage, List<VbaFormControlSite> sites)
    {
        if (container.ReadStream(storage + "/o") is not { Length: > 0 } stream)
            return;

        int at = 0;
        foreach (VbaFormControlSite site in sites)
        {
            if (site.ObjectStreamSize <= 0 || at + site.ObjectStreamSize > stream.Length)
                continue;

            if (OFormsControlKind.SchemaFor(site.Kind) is { } schema)
                ReadControl(stream, at, at + site.ObjectStreamSize, schema, site);

            at += site.ObjectStreamSize;
        }
    }

    /// <summary>Reads one control's own record, leaving it as the site described it on failure.</summary>
    /// <param name="stream">The object stream.</param>
    /// <param name="start">Where this control's record begins.</param>
    /// <param name="end">Where it ends, from the size the site declared.</param>
    /// <param name="schema">The layout the control is written with.</param>
    /// <param name="site">The control being filled in.</param>
    private static void ReadControl(byte[] stream, int start, int end, OFormsSchema schema, VbaFormControlSite site)
    {
        try
        {
            OFormsValues values = OFormsPropertyBag.Read(new OFormsReader(stream, start, end), schema);
            if (values.Number(OFormsSchemas.DisplayStyle) is { } style)
                site.Kind = OFormsControlKind.FromDisplayStyle(style);

            site.Caption = values.Text(OFormsSchemas.Caption);
            site.GroupName = values.Text(OFormsSchemas.GroupName);
            site.Value = values.Text(OFormsSchemas.Value)
                ?? values.Number(OFormsSchemas.Value)?.ToString(CultureInfo.InvariantCulture);

            if (values.Pair(OFormsSchemas.Size) is { } size)
            {
                site.Width = FromHimetric(size.First);
                site.Height = FromHimetric(size.Second);
            }
        }
        catch (Exception error) when (error is InvalidDataException or IndexOutOfRangeException or ArgumentException)
        {
            // The site still says what the control is called and where it sits, which is more
            // than dropping the whole form would leave.
        }
    }

    /// <summary>Builds a control from what its parent's form stream said about it.</summary>
    /// <param name="values">The record read out of the form stream.</param>
    /// <param name="parent">What the container holding the record is.</param>
    private static VbaFormControlSite Build(OFormsValues values, VbaFormControlKind parent)
    {
        int index = (int)(values.Number(OFormsSchemas.ClsidCacheIndex) ?? 0);
        (int Left, int Top) position = values.Pair(OFormsSchemas.Position) ?? (0, 0);

        // A page is written as a form and is only a page by virtue of what holds it.
        VbaFormControlKind kind = OFormsControlKind.FromCacheIndex(index);
        if (kind == VbaFormControlKind.Form && parent == VbaFormControlKind.MultiPage)
            kind = VbaFormControlKind.Page;

        return new VbaFormControlSite(values.Text(OFormsSchemas.Name) ?? string.Empty, kind)
        {
            Id = (int)(values.Number(OFormsSchemas.Id) ?? 0),
            Left = FromHimetric(position.Left),
            Top = FromHimetric(position.Top),
            TabIndex = unchecked((short)(values.Number(OFormsSchemas.TabIndex) ?? 0)),
            GroupId = (int)(values.Number(OFormsSchemas.GroupId) ?? 0),
            Tooltip = values.Text(OFormsSchemas.Tooltip),
            ControlSource = values.Text(OFormsSchemas.ControlSource),
            RowSource = values.Text(OFormsSchemas.RowSource),
            ObjectStreamSize = (int)(values.Number(OFormsSchemas.ObjectStreamSize) ?? 0),
        };
    }

    /// <summary>
    /// The storage a control that holds others is written to ([MS-OFORMS] 2.1.2.2.2): the
    /// letter <c>i</c> and the identifier, padded to two digits.
    /// </summary>
    /// <param name="id">The control's identifier.</param>
    private static string StorageName(int id) => "i" + id.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>One control as the form stream describes it, before its own record is read.</summary>
    /// <param name="Depth">How many controls sit between it and this container.</param>
    /// <param name="Values">What its record in the form stream held.</param>
    private sealed record SiteRecord(int Depth, OFormsValues Values);
}
