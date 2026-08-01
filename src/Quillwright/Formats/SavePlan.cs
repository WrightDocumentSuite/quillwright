using Quillwright.IO;
using Quillwright.Model;

namespace Quillwright.Formats;

/// <summary>
/// Works out, before a byte is written, which parts the model regenerates, which are copied
/// through, and what relationship id every reference in the markup resolves to.
/// </summary>
/// <remarks>
/// Relationship ids of a loaded package are kept exactly as they were. Preserved markup —
/// a chart, an OLE object, a picture this version does not model — points at its target by
/// id, so renumbering would repoint it at something else. New references are given ids the
/// allocator has verified are free.
/// </remarks>
internal sealed class SavePlan
{
    private readonly WordDocument _document;
    private readonly PreservedPackage? _preserved;
    private readonly HashSet<string> _presentParts;
    private readonly RelationshipIdAllocator _ids = new();
    private readonly Dictionary<HeaderFooter, string> _headerFooterIds = [];
    private readonly Dictionary<string, PartImages> _partImages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PartHyperlinks> _partHyperlinks = new(StringComparer.OrdinalIgnoreCase);
    private int _nextMediaNumber = 1;
    private int _nextHeaderNumber = 1;
    private int _nextFooterNumber = 1;

    public SavePlan(WordDocument document)
    {
        _document = document;
        PreservedPackage? preserved = document.Preserved;

        _preserved = preserved;
        _presentParts = preserved?.PresentModelledParts ?? [];
        MainPartPath = preserved?.MainPartPath ?? DocxSchema.PartDocument;
        MainContentType = preserved?.MainContentType ?? DocxSchema.ContentTypeDocument;
        MainRelationships = preserved is null ? [] : [.. preserved.MainRelationships];
        _ids.Reserve(MainRelationships);

        foreach (OpcRelationship relationship in MainRelationships)
            _nextMediaNumber = Math.Max(_nextMediaNumber, ExtractTrailingNumber(relationship.Target) + 1);
    }

    /// <summary>Absolute name of the main document part.</summary>
    public string MainPartPath { get; }

    /// <summary>Content type of the main document part.</summary>
    public string MainContentType { get; }

    /// <summary>Relationships the main document part will declare.</summary>
    public List<OpcRelationship> MainRelationships { get; }

    /// <summary>Parts the model regenerates, which therefore must not be copied through.</summary>
    public HashSet<string> RegeneratedParts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the durable comment identifiers part is written.</summary>
    public bool WritesCommentIds { get; private set; }

    /// <summary>Whether the extended comment metadata part is written.</summary>
    public bool WritesCommentsExtensible { get; private set; }

    /// <summary>Whether the author identities part is written.</summary>
    public bool WritesPeople { get; private set; }

    /// <summary>Whether the application properties part is written.</summary>
    public bool WritesApplicationProperties { get; private set; }

    /// <summary>Whether the custom properties part is written.</summary>
    public bool WritesCustomProperties { get; private set; }

    /// <summary>New media parts to write, keyed by part path.</summary>
    public Dictionary<string, ImageData> NewMedia { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Relationships a part other than the main document has gained, keyed by that part. A
    /// header owns the relationships its own markup points at, so a picture that arrived in
    /// one is declared there rather than in the document.
    /// </summary>
    public Dictionary<string, List<OpcRelationship>> PartRelationships { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Assigns relationships and part paths to everything the document refers to.</summary>
    public void Prepare()
    {
        RegeneratedParts.Add(MainPartPath);
        PlanFixedPart(DocxSchema.RelStyles, DocxSchema.PartStyles);
        PlanFixedPart(DocxSchema.RelSettings, DocxSchema.PartSettings);

        if (Writes(DocxSchema.RelNumbering, DocxSchema.PartNumbering, !_document.Numbering.IsEmpty))
            PlanFixedPart(DocxSchema.RelNumbering, DocxSchema.PartNumbering);
        if (Writes(DocxSchema.RelFootnotes, DocxSchema.PartFootnotes, _document.Footnotes.Count > 0))
            PlanFixedPart(DocxSchema.RelFootnotes, DocxSchema.PartFootnotes);
        if (Writes(DocxSchema.RelEndnotes, DocxSchema.PartEndnotes, _document.Endnotes.Count > 0))
            PlanFixedPart(DocxSchema.RelEndnotes, DocxSchema.PartEndnotes);
        if (Writes(DocxSchema.RelComments, DocxSchema.PartComments, _document.Comments.Count > 0))
            PlanFixedPart(DocxSchema.RelComments, DocxSchema.PartComments);
        if (CommentThreadWriter.HasThreads(_document))
            PlanFixedPart(DocxSchema.RelCommentsExtended, DocxSchema.PartCommentsExtended);

        // The extended metadata names comments by durable identifier, so asking for it is
        // asking for the identifiers part as well; Word never writes one without the other.
        WritesCommentsExtensible = _document.Comments.Count > 0 &&
            (MainRelationships.Any(static r => r.Is(DocxSchema.RelCommentsExtensible)) ||
                CommentExtensiblePart.HasMetadata(_document));

        // Otherwise durable identifiers are Word's own bookkeeping for co-authoring, so one is
        // minted only for a package that already asked for them; the part is rewritten rather
        // than copied so that comments added since still get one.
        WritesCommentIds = WritesCommentsExtensible ||
            (_document.Comments.Count > 0 && MainRelationships.Any(static r => r.Is(DocxSchema.RelCommentsIds)));

        if (WritesCommentIds)
            PlanFixedPart(DocxSchema.RelCommentsIds, DocxSchema.PartCommentsIds);
        if (WritesCommentsExtensible)
            PlanFixedPart(DocxSchema.RelCommentsExtensible, DocxSchema.PartCommentsExtensible);

        // The identities are only regenerated for a document that has somewhere to put them,
        // so a package that never carried the part does not grow one just for a comment.
        WritesPeople = _document.People.Count > 0 || MainRelationships.Any(static r => r.Is(DocxSchema.RelPeople));
        if (WritesPeople)
            PlanFixedPart(DocxSchema.RelPeople, DocxSchema.PartPeople);

        // The property parts hang off the package root rather than the main document, so they
        // are only marked as regenerated here; the saver adds the root relationship itself.
        WritesApplicationProperties = !_document.ApplicationProperties.IsEmpty;
        WritesCustomProperties = _document.CustomProperties.Count > 0;
        if (WritesApplicationProperties)
            RegeneratedParts.Add(DocxSchema.PartExtendedProperties);
        if (WritesCustomProperties)
            RegeneratedParts.Add(DocxSchema.PartCustomProperties);

        foreach (HeaderFooter part in _document.HeaderFooters)
            PlanHeaderFooter(part);
        foreach (ImageData image in _document.Media)
            PlanMedia(image);
    }

    /// <summary>Where a part the model owns is written, following the loaded package's own layout.</summary>
    public string PathFor(string relationshipType, string fallback) =>
        _preserved?.PathFor(relationshipType, fallback) ?? fallback;

    /// <summary>
    /// Whether an optional part goes into the package: because the model has something to
    /// put in it, or because the loaded package already had one.
    /// </summary>
    public bool Writes(string relationshipType, string fallback, bool hasContent) =>
        hasContent || _presentParts.Contains(PathFor(relationshipType, fallback));

    /// <summary>Relationship id a header or footer part is referenced by.</summary>
    public string RelationshipFor(HeaderFooter part) => _headerFooterIds[part];

    /// <summary>Relationship id a picture in the main document uses.</summary>
    public string? RelationshipFor(Picture picture) => RelationshipFor(picture, MainPartPath);

    /// <summary>
    /// Relationship id a picture's image is referenced by from a part that declares its own,
    /// minting one when the part has never pointed at that image before.
    /// </summary>
    /// <param name="picture">The picture the markup is being written for.</param>
    /// <param name="partPath">Absolute name of the part the markup goes into.</param>
    public string? RelationshipFor(Picture picture, string partPath)
    {
        if (!_partImages.TryGetValue(partPath, out PartImages? images))
            _partImages[partPath] = images = CreatePartImages(partPath);

        ImageData image = picture.Image;
        if (images.ByImage.TryGetValue(image, out string? known))
            return known;

        PlanMedia(image);
        if (image.PartPath is not { } target)
            return null;

        // An image the part already points at keeps the id it was loaded with, because the
        // markup that survived the round trip refers to it by that id.
        string? declared = images.Declared(target);
        string id = declared ?? images.Ids.Next();
        images.ByImage[image] = id;

        if (declared is null)
        {
            RelationshipsForWriting(partPath).Add(
                new OpcRelationship(id, Spell(DocxSchema.RelImage), OpcPath.MakeRelative(partPath, target)));
        }

        if (partPath.Equals(MainPartPath, StringComparison.OrdinalIgnoreCase))
            image.RelationshipId = id;

        return id;
    }

    private PartImages CreatePartImages(string partPath)
    {
        if (partPath.Equals(MainPartPath, StringComparison.OrdinalIgnoreCase))
            return PartImages.For(partPath, MainRelationships, _ids);

        IReadOnlyList<OpcRelationship> existing =
            _preserved?.Relationships.TryGetValue(partPath, out List<OpcRelationship>? relationships) == true
                ? relationships
                : [];
        return PartImages.For(partPath, existing, new RelationshipIdAllocator());
    }

    private List<OpcRelationship> Relationships(string partPath)
    {
        if (!PartRelationships.TryGetValue(partPath, out List<OpcRelationship>? list))
            PartRelationships[partPath] = list = [];
        return list;
    }

    private List<OpcRelationship> RelationshipsForWriting(string partPath) =>
        partPath.Equals(MainPartPath, StringComparison.OrdinalIgnoreCase) ? MainRelationships : Relationships(partPath);

    /// <summary>Relationship id a hyperlink in the main document uses.</summary>
    public string? RelationshipFor(Hyperlink link) => RelationshipFor(link, MainPartPath);

    /// <summary>
    /// Relationship id a hyperlink uses in the part whose markup contains it. Relationship
    /// ids are allocated independently for the document, every header, notes and comments.
    /// </summary>
    public string? RelationshipFor(Hyperlink link, string partPath)
    {
        if (link.Url is not { } target)
            return null;

        if (!_partHyperlinks.TryGetValue(partPath, out PartHyperlinks? hyperlinks))
            _partHyperlinks[partPath] = hyperlinks = CreatePartHyperlinks(partPath);
        if (hyperlinks.ByLink.TryGetValue(link, out string? known))
            return known;

        string? declared = hyperlinks.Declared(link.RelationshipId, target);
        string id = declared ?? hyperlinks.Ids.Next();
        hyperlinks.ByLink[link] = id;
        link.RelationshipId = id;

        if (declared is null)
        {
            RelationshipsForWriting(partPath).Add(
                new OpcRelationship(id, Spell(DocxSchema.RelHyperlink), target, IsExternal: true));
            hyperlinks.Declare(target, id);
        }

        return id;
    }

    private PartHyperlinks CreatePartHyperlinks(string partPath)
    {
        if (partPath.Equals(MainPartPath, StringComparison.OrdinalIgnoreCase))
            return PartHyperlinks.For(MainRelationships, _ids);

        IReadOnlyList<OpcRelationship> existing =
            _preserved?.Relationships.TryGetValue(partPath, out List<OpcRelationship>? relationships) == true
                ? relationships
                : [];
        return PartHyperlinks.For(existing, new RelationshipIdAllocator());
    }

    private void PlanFixedPart(string relationshipType, string fallback)
    {
        string partPath = PathFor(relationshipType, fallback);
        RegeneratedParts.Add(partPath);
        if (MainRelationships.Any(r => r.Is(relationshipType)))
            return;

        MainRelationships.Add(new OpcRelationship(_ids.Next(), Spell(relationshipType), OpcPath.MakeRelative(MainPartPath, partPath)));
    }

    /// <summary>Spells a relationship type in the vocabulary the package already uses.</summary>
    public string Spell(string canonicalType) =>
        _preserved?.IsStrict == true ? DocxSchema.ToStrict(canonicalType) : canonicalType;

    private void PlanHeaderFooter(HeaderFooter part)
    {
        string relationshipType = part.IsFooter ? DocxSchema.RelFooter : DocxSchema.RelHeader;
        if (part.RelationshipId is { } existing && MainRelationships.Any(r => r.Id == existing))
        {
            _headerFooterIds[part] = existing;
            RegeneratedParts.Add(part.PartPath ?? DefaultHeaderFooterPath(part));
            return;
        }

        part.PartPath ??= DefaultHeaderFooterPath(part);
        RegeneratedParts.Add(part.PartPath);
        string id = _ids.Next();
        part.RelationshipId = id;
        _headerFooterIds[part] = id;
        MainRelationships.Add(new OpcRelationship(id, Spell(relationshipType), OpcPath.MakeRelative(MainPartPath, part.PartPath)));
    }

    /// <summary>Gives an image a part of its own, unless it already came from one.</summary>
    private void PlanMedia(ImageData image)
    {
        if (image.PartPath is not null && _preserved?.Parts.ContainsKey(image.PartPath) == true)
            return;

        image.PartPath ??= $"/word/media/image{_nextMediaNumber++}.{image.Extension}";
        NewMedia[image.PartPath] = image;
    }

    /// <summary>The images one part points at, and the ids it is free to give new ones.</summary>
    private sealed class PartImages
    {
        private readonly Dictionary<string, string> _declared = new(StringComparer.OrdinalIgnoreCase);

        private PartImages(RelationshipIdAllocator ids) => Ids = ids;

        public RelationshipIdAllocator Ids { get; }

        public Dictionary<ImageData, string> ByImage { get; } = [];

        /// <summary>The id the part already refers to a media part by, when it does.</summary>
        public string? Declared(string targetPartPath) =>
            _declared.TryGetValue(targetPartPath, out string? id) ? id : null;

        public static PartImages For(
            string partPath, IReadOnlyList<OpcRelationship> existing, RelationshipIdAllocator ids)
        {
            var images = new PartImages(ids);
            ids.Reserve(existing);
            foreach (OpcRelationship relationship in existing)
            {
                if (!relationship.IsExternal && relationship.Is(DocxSchema.RelImage))
                    images._declared[OpcPath.Resolve(partPath, relationship.Target)] = relationship.Id;
            }

            return images;
        }
    }

    /// <summary>The external hyperlinks one source part declares.</summary>
    private sealed class PartHyperlinks
    {
        private readonly Dictionary<string, string> _byTarget = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (string Target, string Id)> _byId = new(StringComparer.Ordinal);

        private PartHyperlinks(RelationshipIdAllocator ids) => Ids = ids;

        public RelationshipIdAllocator Ids { get; }

        public Dictionary<Hyperlink, string> ByLink { get; } = [];

        public string? Declared(string? preferredId, string target)
        {
            if (preferredId is not null && _byId.TryGetValue(preferredId, out var preferred) && preferred.Target == target)
                return preferred.Id;
            return _byTarget.TryGetValue(target, out string? id) ? id : null;
        }

        public void Declare(string target, string id)
        {
            _byTarget.TryAdd(target, id);
            _byId[id] = (target, id);
        }

        public static PartHyperlinks For(IReadOnlyList<OpcRelationship> existing, RelationshipIdAllocator ids)
        {
            var hyperlinks = new PartHyperlinks(ids);
            ids.Reserve(existing);
            foreach (OpcRelationship relationship in existing)
            {
                if (relationship.IsExternal && relationship.Is(DocxSchema.RelHyperlink))
                    hyperlinks.Declare(relationship.Target, relationship.Id);
            }

            return hyperlinks;
        }
    }

    private string DefaultHeaderFooterPath(HeaderFooter part) => part.IsFooter
        ? $"/word/footer{_nextFooterNumber++}.xml"
        : $"/word/header{_nextHeaderNumber++}.xml";

    private static int ExtractTrailingNumber(string target)
    {
        int end = target.LastIndexOf('.');
        if (end <= 0)
            return 0;

        int start = end;
        while (start > 0 && char.IsAsciiDigit(target[start - 1]))
            start--;
        return start == end ? 0 : int.Parse(target.AsSpan(start, end - start), provider: System.Globalization.CultureInfo.InvariantCulture);
    }
}
