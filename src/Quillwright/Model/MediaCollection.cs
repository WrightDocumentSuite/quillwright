using System.Collections;

namespace Quillwright.Model;

/// <summary>
/// The images a document uses. Adding the same <see cref="ImageData"/> twice stores it once,
/// so a logo repeated throughout the document costs one package part.
/// </summary>
public sealed class MediaCollection : IReadOnlyCollection<ImageData>
{
    private readonly List<ImageData> _items = [];

    /// <inheritdoc />
    public int Count => _items.Count;

    /// <summary>Adds an image, or returns the existing entry when it is already present.</summary>
    /// <param name="image">The image to add.</param>
    public ImageData Add(ImageData image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!_items.Contains(image))
            _items.Add(image);
        return image;
    }

    /// <summary>Reads an image from a file and adds it.</summary>
    /// <param name="path">Path to the image file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async ValueTask<ImageData> AddFileAsync(string path, CancellationToken cancellationToken = default) =>
        Add(await ImageData.FromFileAsync(path, cancellationToken).ConfigureAwait(false));

    /// <summary>Returns the image stored in the given package part, or <see langword="null"/>.</summary>
    /// <param name="partPath">Absolute part name.</param>
    public ImageData? FindByPart(string partPath) =>
        _items.FirstOrDefault(i => string.Equals(i.PartPath, partPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns the image a relationship id points at, or <see langword="null"/>.</summary>
    /// <param name="relationshipId">Relationship id used in the markup.</param>
    public ImageData? FindByRelationship(string relationshipId) =>
        _items.FirstOrDefault(i => i.RelationshipId == relationshipId);

    /// <summary>Removes an image.</summary>
    /// <param name="image">The image to remove.</param>
    public bool Remove(ImageData image) => _items.Remove(image);

    /// <inheritdoc />
    public IEnumerator<ImageData> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
