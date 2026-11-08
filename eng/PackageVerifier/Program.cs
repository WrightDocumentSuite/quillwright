using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Quillwright.Release.PackageVerifier;

internal static class Program
{
    private static readonly Guid SourceLinkKind = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");
    private static readonly Guid EmbeddedSourceKind = new("0E8A571B-6926-466E-B4AD-8AB04611F5FE");
    private static readonly Guid Sha1Kind = new("FF1816EC-AA5E-4D10-87F7-6F4963833460");
    private static readonly Guid Sha256Kind = new("8829D00F-11B8-4213-878B-770E8597AC16");

    public static int Main(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            ReleaseManifest manifest = ReadManifest(options.ManifestPath);

            VerifyPackageFileSet(options.PackageDirectory, options.Version, manifest.Packages);
            using var gitBlobReader = new GitBlobReader(options.RepositoryRoot, options.Commit);

            foreach (PackageSpec package in manifest.Packages)
            {
                string packagePath = Path.Combine(
                    options.PackageDirectory,
                    $"{package.Id}.{options.Version}.nupkg");
                string symbolsPath = Path.Combine(
                    options.PackageDirectory,
                    $"{package.Id}.{options.Version}.snupkg");

                PdbIdentity pdbIdentity = VerifyMainPackage(packagePath, package, manifest, options);
                VerifySymbolPackage(
                    symbolsPath,
                    package,
                    manifest,
                    options,
                    gitBlobReader,
                    pdbIdentity);
                Console.WriteLine($"Verified {package.Id} {options.Version}.");
            }

            Console.WriteLine(
                $"Verified {manifest.Packages.Count} packages and symbol packages at {options.Commit}.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Package verification failed: {exception.Message}");
            return 1;
        }
    }

    private static ReleaseManifest ReadManifest(string path)
    {
        ReleaseManifest? manifest = JsonSerializer.Deserialize<ReleaseManifest>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (manifest is null || manifest.SchemaVersion != 1 || manifest.Packages.Count == 0)
            throw new InvalidDataException($"'{path}' is not a supported release manifest.");
        return manifest;
    }

    private static void VerifyPackageFileSet(
        string directory,
        string version,
        IReadOnlyCollection<PackageSpec> packages)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (PackageSpec package in packages)
        {
            expected.Add($"{package.Id}.{version}.nupkg");
            expected.Add($"{package.Id}.{version}.snupkg");
        }

        string[] actual = Directory
            .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path =>
                path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expectedOrdered = expected.Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(expectedOrdered, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Package file set differs. Expected [{string.Join(", ", expectedOrdered)}], " +
                $"found [{string.Join(", ", actual)}].");
        }
    }

    private static PdbIdentity VerifyMainPackage(
        string path,
        PackageSpec package,
        ReleaseManifest manifest,
        Options options)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        Dictionary<string, ZipArchiveEntry> entries = IndexEntries(archive, path);

        var allowedPayload = new HashSet<string>(StringComparer.Ordinal)
        {
            $"lib/net10.0/{package.Id}.dll",
            $"lib/net10.0/{package.Id}.xml",
        };
        foreach (string entry in package.RequiredPackageEntries)
            allowedPayload.Add(entry);

        RequireEntry(entries, "README.md", path);
        RequireEntry(entries, $"{package.Id}.nuspec", path);
        RequireEntry(entries, "[Content_Types].xml", path);
        RequireEntry(entries, "_rels/.rels", path);
        foreach (string entry in allowedPayload)
            RequireEntry(entries, entry, path);

        foreach (string entry in entries.Keys)
        {
            if (entry.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The primary package '{path}' contains PDB '{entry}'.");

            if (!allowedPayload.Contains(entry) &&
                entry != "README.md" &&
                entry != $"{package.Id}.nuspec" &&
                !IsStandardNuGetMetadata(entry))
            {
                throw new InvalidDataException(
                    $"The package '{path}' has unexpected archive entry '{entry}'.");
            }
        }

        RequireSingleCorePropertiesEntry(entries.Keys, path);

        VerifyNuspec(entries[$"{package.Id}.nuspec"], package, manifest, options, isSymbols: false);
        return ReadPdbIdentity(entries[$"lib/net10.0/{package.Id}.dll"], package.Id, path);
    }

    private static void VerifySymbolPackage(
        string path,
        PackageSpec package,
        ReleaseManifest manifest,
        Options options,
        GitBlobReader gitBlobReader,
        PdbIdentity expectedPdbIdentity)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        Dictionary<string, ZipArchiveEntry> entries = IndexEntries(archive, path);
        string pdbPath = $"lib/net10.0/{package.Id}.pdb";

        RequireEntry(entries, $"{package.Id}.nuspec", path);
        RequireEntry(entries, pdbPath, path);
        RequireEntry(entries, "[Content_Types].xml", path);
        RequireEntry(entries, "_rels/.rels", path);
        foreach (string entry in entries.Keys)
        {
            if (entry != pdbPath &&
                entry != $"{package.Id}.nuspec" &&
                !IsStandardNuGetMetadata(entry))
            {
                throw new InvalidDataException(
                    $"The symbol package '{path}' has unexpected archive entry '{entry}'.");
            }
        }

        RequireSingleCorePropertiesEntry(entries.Keys, path);

        VerifyNuspec(entries[$"{package.Id}.nuspec"], package, manifest, options, isSymbols: true);
        using Stream pdbEntry = entries[pdbPath].Open();
        using var pdb = new MemoryStream();
        pdbEntry.CopyTo(pdb);
        pdb.Position = 0;
        VerifySourceLink(
            pdb,
            manifest,
            options,
            gitBlobReader,
            expectedPdbIdentity,
            path);
    }

    private static Dictionary<string, ZipArchiveEntry> IndexEntries(ZipArchive archive, string path)
    {
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string name = entry.FullName.Replace('\\', '/');
            if (name.StartsWith("/", StringComparison.Ordinal) ||
                name.Split('/').Any(static segment => segment == ".."))
            {
                throw new InvalidDataException($"Archive '{path}' contains unsafe path '{entry.FullName}'.");
            }

            if (!entries.TryAdd(name, entry))
                throw new InvalidDataException($"Archive '{path}' contains duplicate entry '{name}'.");
        }
        return entries;
    }

    private static bool IsStandardNuGetMetadata(string path) =>
        path is "[Content_Types].xml" or "_rels/.rels" || IsCorePropertiesEntry(path);

    private static bool IsCorePropertiesEntry(string path)
    {
        const string prefix = "package/services/metadata/core-properties/";
        const string suffix = ".psmdcp";
        if (!path.StartsWith(prefix, StringComparison.Ordinal) ||
            !path.EndsWith(suffix, StringComparison.Ordinal) ||
            path.Length == prefix.Length + suffix.Length)
        {
            return false;
        }

        return !path.AsSpan(prefix.Length, path.Length - prefix.Length - suffix.Length).Contains('/');
    }

    private static void RequireSingleCorePropertiesEntry(IEnumerable<string> entries, string archivePath)
    {
        if (entries.Count(IsCorePropertiesEntry) != 1)
        {
            throw new InvalidDataException(
                $"Archive '{archivePath}' must contain exactly one NuGet core-properties entry.");
        }
    }

    private static void RequireEntry(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        string name,
        string archivePath)
    {
        if (!entries.ContainsKey(name))
            throw new InvalidDataException($"Archive '{archivePath}' is missing '{name}'.");
    }

    private static PdbIdentity ReadPdbIdentity(
        ZipArchiveEntry assemblyEntry,
        string packageId,
        string packagePath)
    {
        using Stream assemblyStream = assemblyEntry.Open();
        using var assembly = new MemoryStream();
        assemblyStream.CopyTo(assembly);
        assembly.Position = 0;

        using var peReader = new PEReader(assembly);
        DebugDirectoryEntry[] codeViewEntries = peReader
            .ReadDebugDirectory()
            .Where(static entry => entry.Type == DebugDirectoryEntryType.CodeView)
            .ToArray();
        if (codeViewEntries.Length != 1 || !codeViewEntries[0].IsPortableCodeView)
        {
            throw new InvalidDataException(
                $"Assembly in '{packagePath}' must reference exactly one portable PDB.");
        }

        CodeViewDebugDirectoryData codeView = peReader.ReadCodeViewDebugDirectoryData(codeViewEntries[0]);
        string normalizedPdbPath = codeView.Path.Replace('\\', '/');
        string pdbFileName = normalizedPdbPath[(normalizedPdbPath.LastIndexOf('/') + 1)..];
        if (codeView.Age != 1 ||
            !string.Equals(
                pdbFileName,
                $"{packageId}.pdb",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Assembly in '{packagePath}' has an unexpected CodeView PDB reference.");
        }

        return new PdbIdentity(codeView.Guid, codeViewEntries[0].Stamp);
    }

    private static void VerifyNuspec(
        ZipArchiveEntry entry,
        PackageSpec package,
        ReleaseManifest manifest,
        Options options,
        bool isSymbols)
    {
        using Stream stream = entry.Open();
        XDocument document = XDocument.Load(stream, LoadOptions.None);
        XElement root = document.Root ?? throw new InvalidDataException("Nuspec has no root element.");
        XNamespace ns = root.Name.Namespace;
        XElement metadata = root.Element(ns + "metadata") ??
            throw new InvalidDataException("Nuspec has no metadata element.");

        RequireValue(metadata, ns + "id", package.Id);
        RequireValue(metadata, ns + "version", options.Version);
        RequireValue(metadata, ns + "projectUrl", manifest.ProjectUrl);
        if (isSymbols)
        {
            string packageType = metadata
                .Descendants(ns + "packageType")
                .SingleOrDefault()?.Attribute("name")?.Value ?? string.Empty;
            if (packageType != "SymbolsPackage")
                throw new InvalidDataException($"{package.Id} symbol nuspec has package type '{packageType}'.");
        }
        else
        {
            RequireValue(metadata, ns + "license", "Apache-2.0");
            RequireValue(metadata, ns + "readme", "README.md");
        }

        XElement repository = metadata.Element(ns + "repository") ??
            throw new InvalidDataException($"{package.Id} nuspec has no repository metadata.");
        RequireAttribute(repository, "type", "git", package.Id);
        RequireAttribute(repository, "url", manifest.RepositoryUrl, package.Id);
        RequireAttribute(repository, "commit", options.Commit, package.Id);

        if (metadata.Descendants().Any(static element =>
            element.Name.LocalName is "frameworkReference" or "frameworkAssembly"))
        {
            throw new InvalidDataException(
                $"{package.Id} declares a framework reference not present in the release manifest.");
        }

        DependencySpec[] expected = package.Dependencies
            .Select(dependency => dependency with
            {
                Version = dependency.Version.Replace(
                    "{packageVersion}", options.Version, StringComparison.Ordinal),
            })
            .OrderBy(static dependency => dependency.Id, StringComparer.Ordinal)
            .ToArray();
        XElement[] dependencySections = metadata.Elements(ns + "dependencies").ToArray();
        if (dependencySections.Length > 1)
            throw new InvalidDataException($"{package.Id} has multiple dependencies elements.");

        DependencySpec[] actual;
        if (dependencySections.Length == 0)
        {
            actual = [];
        }
        else
        {
            XElement dependencies = dependencySections[0];
            XElement[] groups = dependencies.Elements(ns + "group").ToArray();
            if (groups.Length != 1 ||
                dependencies.Elements().Count() != 1 ||
                (string?)groups[0].Attribute("targetFramework") != "net10.0")
            {
                throw new InvalidDataException(
                    $"{package.Id} must have exactly one net10.0 dependency group.");
            }
            if (groups[0].Elements().Any(element => element.Name != ns + "dependency"))
            {
                throw new InvalidDataException(
                    $"{package.Id} dependency group contains an unknown element.");
            }

            actual = groups[0]
                .Elements(ns + "dependency")
                .Select(element => ReadDependency(element, package.Id))
                .OrderBy(static dependency => dependency.Id, StringComparer.Ordinal)
                .ToArray();
        }

        if (!actual.SequenceEqual(expected))
        {
            throw new InvalidDataException(
                $"{package.Id} dependencies differ. Expected [{FormatDependencies(expected)}], " +
                $"found [{FormatDependencies(actual)}].");
        }
    }

    private static DependencySpec ReadDependency(XElement element, string packageId)
    {
        string[] allowedAttributes = ["id", "version", "include", "exclude"];
        XAttribute? unexpectedAttribute = element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.NamespaceName.Length > 0 ||
            !allowedAttributes.Contains(attribute.Name.LocalName, StringComparer.Ordinal));
        if (unexpectedAttribute is not null || element.HasElements)
        {
            throw new InvalidDataException(
                $"{packageId} dependency contains unsupported nuspec metadata.");
        }

        return new DependencySpec
        {
            Id = RequiredAttribute(element, "id", packageId),
            Version = RequiredAttribute(element, "version", packageId),
            Include = (string?)element.Attribute("include") ?? string.Empty,
            Exclude = (string?)element.Attribute("exclude") ?? string.Empty,
        };
    }

    private static void RequireValue(XElement parent, XName name, string expected)
    {
        string actual = parent.Element(name)?.Value ?? string.Empty;
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"Nuspec '{name.LocalName}' is '{actual}', expected '{expected}'.");
    }

    private static void RequireAttribute(
        XElement element,
        XName name,
        string expected,
        string packageId)
    {
        string actual = (string?)element.Attribute(name) ?? string.Empty;
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{packageId} repository {name.LocalName} is '{actual}', expected '{expected}'.");
        }
    }

    private static string RequiredAttribute(XElement element, XName name, string packageId) =>
        (string?)element.Attribute(name) ??
        throw new InvalidDataException($"{packageId} dependency has no {name.LocalName} attribute.");

    private static string FormatDependencies(IEnumerable<DependencySpec> dependencies) =>
        string.Join(", ", dependencies.Select(static value =>
            $"{value.Id}/{value.Version}/include={value.Include}/exclude={value.Exclude}"));

    private static void VerifySourceLink(
        Stream pdbStream,
        ReleaseManifest manifest,
        Options options,
        GitBlobReader gitBlobReader,
        PdbIdentity expectedPdbIdentity,
        string symbolPackagePath)
    {
        using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(
            pdbStream,
            MetadataStreamOptions.LeaveOpen);
        MetadataReader reader = provider.GetMetadataReader();
        DebugMetadataHeader pdbHeader = reader.DebugMetadataHeader ??
            throw new InvalidDataException($"'{symbolPackagePath}' is not a portable PDB.");
        BlobContentId pdbIdentity = new(pdbHeader.Id);
        if (pdbIdentity.Guid != expectedPdbIdentity.Guid ||
            pdbIdentity.Stamp != expectedPdbIdentity.Stamp)
        {
            throw new InvalidDataException(
                $"'{symbolPackagePath}' PDB identity does not match its primary package assembly.");
        }

        CustomDebugInformation sourceLink = reader
            .GetCustomDebugInformation(MetadataTokens.EntityHandle(TableIndex.Module, 1))
            .Select(reader.GetCustomDebugInformation)
            .SingleOrDefault(value => reader.GetGuid(value.Kind) == SourceLinkKind);
        if (sourceLink.Value.IsNil)
            throw new InvalidDataException($"'{symbolPackagePath}' has no Source Link record.");

        byte[] sourceLinkBytes = reader.GetBlobBytes(sourceLink.Value);
        using JsonDocument sourceLinkJson = JsonDocument.Parse(sourceLinkBytes);
        JsonElement documents = sourceLinkJson.RootElement.GetProperty("documents");
        JsonProperty[] mappings = documents.EnumerateObject().ToArray();
        if (mappings.Length != 1)
            throw new InvalidDataException($"'{symbolPackagePath}' must contain exactly one Source Link mapping.");

        string sourcePattern = mappings[0].Name;
        string actualUrl = mappings[0].Value.GetString() ?? string.Empty;
        string expectedUrl = manifest.RawSourceUrl.Replace("{commit}", options.Commit, StringComparison.Ordinal);
        if (!string.Equals(actualUrl, expectedUrl, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"'{symbolPackagePath}' maps sources to '{actualUrl}', expected '{expectedUrl}'.");
        }
        if (!sourcePattern.EndsWith('*'))
            throw new InvalidDataException($"'{symbolPackagePath}' Source Link key has no wildcard.");

        string sourcePrefix = sourcePattern[..^1];
        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        int trackedDocuments = 0;
        int embeddedDocuments = 0;

        foreach (DocumentHandle handle in reader.Documents)
        {
            Document document = reader.GetDocument(handle);
            string documentPath = reader.GetString(document.Name);
            if (!documentPath.StartsWith(sourcePrefix, pathComparison))
            {
                throw new InvalidDataException(
                    $"PDB document '{documentPath}' is outside Source Link root '{sourcePrefix}'.");
            }

            string relativePath = documentPath[sourcePrefix.Length..]
                .Replace('\\', '/')
                .TrimStart('/');
            if (relativePath.Length == 0 ||
                relativePath.IndexOfAny(['\r', '\n', '\0']) >= 0 ||
                relativePath.Split('/').Any(static segment => segment == ".."))
            {
                throw new InvalidDataException($"PDB contains unsafe document path '{documentPath}'.");
            }

            byte[] source;
            if (gitBlobReader.TryRead(relativePath, out byte[]? trackedSource))
            {
                source = trackedSource!;
                trackedDocuments++;
            }
            else if (TryReadEmbeddedSource(reader, handle, out byte[]? embeddedSource))
            {
                VerifyGeneratedEmbeddedSourcePath(relativePath);
                source = embeddedSource!;
                embeddedDocuments++;
            }
            else
            {
                throw new InvalidDataException(
                    $"PDB document '{relativePath}' is neither tracked at HEAD nor embedded.");
            }

            byte[] expectedHash = reader.GetBlobBytes(document.Hash);
            byte[] actualHash = HashSource(reader.GetGuid(document.HashAlgorithm), source);
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
            {
                throw new InvalidDataException(
                    $"Source checksum mismatch for '{relativePath}': PDB {Convert.ToHexString(expectedHash)}, " +
                    $"Git {Convert.ToHexString(actualHash)}. Build from the exact LF Git snapshot.");
            }
        }

        if (trackedDocuments == 0)
            throw new InvalidDataException($"'{symbolPackagePath}' has no Source Link documents tracked at HEAD.");

        Console.WriteLine(
            $"  Source Link: {trackedDocuments} tracked document(s), {embeddedDocuments} embedded document(s).");
    }

    private static void VerifyGeneratedEmbeddedSourcePath(string relativePath)
    {
        bool knownGeneratedName = relativePath.EndsWith(".g.cs", StringComparison.Ordinal) ||
            relativePath.EndsWith(".AssemblyInfo.cs", StringComparison.Ordinal) ||
            relativePath.EndsWith(".AssemblyAttributes.cs", StringComparison.Ordinal);
        if (!relativePath.StartsWith("artifacts/obj/", StringComparison.Ordinal) ||
            !knownGeneratedName)
        {
            throw new InvalidDataException(
                $"Embedded PDB document '{relativePath}' is outside the generated-source allowlist.");
        }
    }

    private static bool TryReadEmbeddedSource(
        MetadataReader reader,
        DocumentHandle document,
        out byte[]? source)
    {
        foreach (CustomDebugInformationHandle handle in reader.GetCustomDebugInformation(document))
        {
            CustomDebugInformation information = reader.GetCustomDebugInformation(handle);
            if (reader.GetGuid(information.Kind) != EmbeddedSourceKind)
                continue;

            byte[] blob = reader.GetBlobBytes(information.Value);
            if (blob.Length < sizeof(int))
                throw new InvalidDataException("Embedded source record is truncated.");

            int uncompressedSize = BinaryPrimitives.ReadInt32LittleEndian(blob);
            if (uncompressedSize == 0)
            {
                source = blob[sizeof(int)..];
                return true;
            }
            if (uncompressedSize < 0)
                throw new InvalidDataException("Embedded source has a negative size.");

            using var compressed = new MemoryStream(blob, sizeof(int), blob.Length - sizeof(int));
            using var deflate = new DeflateStream(compressed, CompressionMode.Decompress);
            using var output = new MemoryStream(uncompressedSize);
            deflate.CopyTo(output);
            if (output.Length != uncompressedSize)
                throw new InvalidDataException("Embedded source length differs from its declared size.");
            source = output.ToArray();
            return true;
        }

        source = null;
        return false;
    }

    private static byte[] HashSource(Guid algorithm, byte[] source)
    {
        if (algorithm == Sha256Kind)
            return SHA256.HashData(source);
        if (algorithm == Sha1Kind)
            return SHA1.HashData(source);
        throw new InvalidDataException($"PDB uses unsupported document hash algorithm '{algorithm}'.");
    }

    private sealed class GitBlobReader : IDisposable
    {
        private readonly Dictionary<string, byte[]?> _content = new(StringComparer.Ordinal);
        private readonly Process _process;
        private readonly StreamWriter _input;
        private readonly Stream _output;
        private readonly Task<string> _stderr;
        private readonly string _commit;

        public GitBlobReader(string repositoryRoot, string commit)
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = repositoryRoot,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("cat-file");
            startInfo.ArgumentList.Add("--batch");

            _process = Process.Start(startInfo) ??
                throw new InvalidOperationException("Could not start git cat-file --batch.");
            _input = new StreamWriter(
                _process.StandardInput.BaseStream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 1024,
                leaveOpen: false)
            {
                AutoFlush = true,
                NewLine = "\n",
            };
            _output = _process.StandardOutput.BaseStream;
            _stderr = _process.StandardError.ReadToEndAsync();
            _commit = commit;
        }

        public bool TryRead(string relativePath, out byte[]? content)
        {
            if (_content.TryGetValue(relativePath, out content))
                return content is not null;

            _input.WriteLine($"{_commit}:{relativePath}");
            string header = ReadAsciiLine(_output);
            if (header.EndsWith(" missing", StringComparison.Ordinal))
            {
                _content.Add(relativePath, null);
                content = null;
                return false;
            }

            int sizeSeparator = header.LastIndexOf(' ');
            int typeSeparator = sizeSeparator > 0 ? header.LastIndexOf(' ', sizeSeparator - 1) : -1;
            if (typeSeparator < 0 ||
                header[(typeSeparator + 1)..sizeSeparator] != "blob" ||
                !int.TryParse(header[(sizeSeparator + 1)..], out int size) ||
                size < 0)
            {
                throw new InvalidDataException($"Unexpected git cat-file response '{header}'.");
            }

            content = new byte[size];
            _output.ReadExactly(content);
            if (_output.ReadByte() != '\n')
                throw new InvalidDataException("git cat-file did not terminate a blob with LF.");
            _content.Add(relativePath, content);
            return true;
        }

        public void Dispose()
        {
            _input.Dispose();
            _output.CopyTo(Stream.Null);
            _process.WaitForExit();
            string error = _stderr.GetAwaiter().GetResult();
            _output.Dispose();
            int exitCode = _process.ExitCode;
            _process.Dispose();
            if (exitCode != 0)
                throw new InvalidOperationException($"git cat-file --batch failed: {error.Trim()}");
        }

        private static string ReadAsciiLine(Stream stream)
        {
            var bytes = new List<byte>();
            while (true)
            {
                int value = stream.ReadByte();
                if (value < 0)
                    throw new EndOfStreamException("git cat-file ended before its response header.");
                if (value == '\n')
                    return Encoding.ASCII.GetString(bytes.ToArray());
                bytes.Add((byte)value);
            }
        }
    }

    private sealed class Options
    {
        private Options(
            string packageDirectory,
            string version,
            string commit,
            string repositoryRoot,
            string manifestPath)
        {
            PackageDirectory = Path.GetFullPath(packageDirectory);
            Version = version;
            Commit = commit.ToLowerInvariant();
            RepositoryRoot = Path.GetFullPath(repositoryRoot);
            ManifestPath = Path.GetFullPath(manifestPath);
        }

        public string PackageDirectory { get; }

        public string Version { get; }

        public string Commit { get; }

        public string RepositoryRoot { get; }

        public string ManifestPath { get; }

        public static Options Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("Arguments must be --name value pairs.");
                if (!values.TryAdd(args[index], args[index + 1]))
                    throw new ArgumentException($"Argument '{args[index]}' was specified twice.");
            }

            string packageDirectory = Required("--package-directory");
            string version = Required("--version");
            string commit = Required("--commit");
            string repositoryRoot = Required("--repository-root");
            string manifestPath = Required("--manifest");

            if (!Directory.Exists(packageDirectory))
                throw new DirectoryNotFoundException(packageDirectory);
            if (!Directory.Exists(repositoryRoot))
                throw new DirectoryNotFoundException(repositoryRoot);
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException("Release manifest was not found.", manifestPath);
            if (commit.Length != 40 || commit.Any(static value => !Uri.IsHexDigit(value)))
                throw new ArgumentException("Commit must be a full 40-character Git object id.");

            return new Options(packageDirectory, version, commit, repositoryRoot, manifestPath);

            string Required(string key) => values.TryGetValue(key, out string? value) && value.Length > 0
                ? value
                : throw new ArgumentException($"Missing required argument '{key}'.");
        }
    }

    private sealed class ReleaseManifest
    {
        public int SchemaVersion { get; init; }

        public string RepositoryUrl { get; init; } = string.Empty;

        public string ProjectUrl { get; init; } = string.Empty;

        public string RawSourceUrl { get; init; } = string.Empty;

        public string BaselineVersion { get; init; } = string.Empty;

        public List<PackageSpec> Packages { get; init; } = [];
    }

    private sealed class PackageSpec
    {
        public string Id { get; init; } = string.Empty;

        public string Project { get; init; } = string.Empty;

        public List<string> RequiredPackageEntries { get; init; } = [];

        public List<DependencySpec> Dependencies { get; init; } = [];
    }

    private sealed record DependencySpec
    {
        public string Id { get; init; } = string.Empty;

        public string Version { get; init; } = string.Empty;

        public string Include { get; init; } = string.Empty;

        public string Exclude { get; init; } = string.Empty;
    }

    private readonly record struct PdbIdentity(Guid Guid, uint Stamp);
}
