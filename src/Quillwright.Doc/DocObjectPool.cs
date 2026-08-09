using System.Globalization;
using Quillwright.Diagnostics;
using Quillwright.Doc.Writing;
using Quillwright.IO;
using Quillwright.Model;

namespace Quillwright.Doc;

/// <summary>
/// Reads the embedded objects a legacy document keeps in its <c>ObjectPool</c> storage
/// ([MS-DOC] 2.1.4).
/// </summary>
/// <remarks>
/// Each object is a storage of its own, named for the number the field separator's
/// <c>sprmCPicLocation</c> carries with an underscore in front of it. The storage holds the
/// same streams an embedded object has anywhere else, so it is rewritten as a compound file of
/// its own and handed on: what comes out is the same kind of thing a package keeps in
/// <c>word/embeddings</c>, and can be saved as a file and opened.
/// </remarks>
internal static class DocObjectPool
{
    /// <summary>Name of the storage embedded objects live in.</summary>
    public const string StorageName = "ObjectPool";

    /// <summary>Reads one object out of the pool, or nothing when the storage is not there.</summary>
    /// <param name="container">The whole file.</param>
    /// <param name="number">The number the field separator's picture location carried.</param>
    /// <param name="loadBudget">Optional counters for reconstructed object payloads.</param>
    public static EmbeddedObject? Read(
        CompoundFile container, int number, DocumentLoadBudgetState? loadBudget = null)
    {
        string storage = $"{StorageName}/_{number.ToString(CultureInfo.InvariantCulture)}";
        if (!container.HasStorage(storage))
            return null;

        var writer = new CompoundFileWriter();
        bool any = false;
        foreach (string path in container.ChildrenOf(storage))
        {
            // A storage nested inside the object would need a tree the writer does not build;
            // its streams are the object's own and are what a reader needs.
            string name = path[(storage.Length + 1)..];
            if (name.Length > 31 || container.ReadStream(path) is not { } bytes)
                continue;

            writer.Add(name, bytes);
            any = true;
        }

        if (!any)
            return null;

        loadBudget?.AddEmbeddedObject(writer.EstimateBuildLength());
        byte[] content = writer.Build();
        OleDescription? description = OleContainer.Describe(content, loadBudget?.Budget);
        return new EmbeddedObject
        {
            Location = storage,
            ProgramId = description?.ProgramId,
            DisplayName = description?.DisplayName,
            IsLinked = description?.IsLinked ?? false,
            Content = content,
            PackagedFileName = description?.PackagedFileName,
            PackagedFile = description?.PackagedFile ?? ReadOnlyMemory<byte>.Empty,
        };
    }
}
