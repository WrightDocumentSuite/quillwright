using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Quillwright.Model;
using Quillwright.Primitives;

namespace Quillwright.Tests;

/// <summary>
/// Resolving a theme colour: the slot it names, through the mapping the settings give, with
/// the tint or shade it asks for (ECMA-376 part 1 §20.1.6.2, [MS-OI29500] 2.1.87).
/// </summary>
/// <remarks>
/// The arithmetic is checked against Word rather than against itself. When Word writes a
/// theme colour it puts the name and the value it computed in the same element, so every
/// <c>w:color</c> in the corpus that carries both is a worked example with the answer at the
/// back — and there are thousands of them.
/// </remarks>
public partial class ThemeColorTests
{
    private static readonly string[] CorpusRoots = ReferenceCorpus.Roots;

    [Fact]
    public async Task ADocumentWithATheme_KnowsItsColours()
    {
        WordDocument document = await AnyThemedAsync();

        Assert.NotNull(document.Theme);
        Assert.NotEmpty(document.Theme.Scheme);
        Assert.NotNull(document.Theme.Slot(ThemeColorSlot.Accent1));
    }

    /// <summary>A literal colour resolves to itself; automatic resolves to nothing.</summary>
    [Fact]
    public async Task AColourThatIsNotAThemeColour_ResolvesToItself()
    {
        WordDocument document = await AnyThemedAsync();

        Assert.Equal(0x336699u, document.ResolveColor(WordColor.FromRgb(0x336699)));
        Assert.Null(document.ResolveColor(WordColor.Auto));
    }

    [Fact]
    public async Task ATintedThemeColour_IsLighterAndAShadedOneDarker()
    {
        WordDocument document = await AnyThemedAsync();
        uint plain = document.Theme!.Slot(ThemeColorSlot.Accent1)!.Value;

        uint tinted = document.ResolveColor(WordColor.FromTheme(ThemeColorSlot.Accent1, tint: 0x66))!.Value;
        uint shaded = document.ResolveColor(WordColor.FromTheme(ThemeColorSlot.Accent1, shade: 0x66))!.Value;

        Assert.True(Brightness(tinted) > Brightness(plain), "a tint should lighten");
        Assert.True(Brightness(shaded) < Brightness(plain), "a shade should darken");
    }

    /// <summary>A document with no theme part cannot resolve a slot, and says so rather than guessing.</summary>
    [Fact]
    public void ADocumentWithNoTheme_ResolvesNoThemeColour()
    {
        WordDocument document = WordDocument.Create();

        Assert.Null(document.Theme);
        Assert.Null(document.ResolveColor(WordColor.FromTheme(ThemeColorSlot.Accent1)));
    }

    /// <summary>
    /// Every worked example in the corpus. Most agree exactly; the rest are a single step out
    /// in one channel, because Word rounds somewhere this does not and has not always rounded
    /// the same way. The bar is therefore both: nothing is visibly wrong, and the great
    /// majority is bit-for-bit what Word computed.
    /// </summary>
    [Fact]
    public async Task AcrossTheCorpus_ResolvingAgreesWithWhatWordComputed()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        int examples = 0;
        int exact = 0;
        var worst = new List<string>();

        foreach (string path in Packages())
        {
            WordDocument document;
            try
            {
                document = await WordDocument.LoadAsync(path, cancellationToken: cancellationToken);
            }
            catch (Quillwright.Diagnostics.DocxFormatException)
            {
                continue;
            }

            if (document.Theme is null)
                continue;

            foreach ((WordColor color, uint cached) in CachedColors(path))
            {
                if (document.ResolveColor(color) is not { } resolved)
                    continue;

                examples++;
                if (resolved == cached)
                {
                    exact++;
                }
                else if (Distance(resolved, cached) > 1 && worst.Count < 10)
                {
                    worst.Add($"{color} gave {resolved:X6}, Word cached {cached:X6}");
                }
            }
        }

        Assert.SkipWhen(examples == 0, ReferenceCorpus.Absent);
        Assert.True(worst.Count == 0, $"{worst.Count} colours were visibly wrong:\n{string.Join('\n', worst)}");
        Assert.True(
            exact >= examples * 0.9,
            $"only {exact} of {examples} colours matched Word exactly");
    }

    /// <summary>
    /// Every <c>w:color</c> in a package that names a theme slot and caches its value, read
    /// out of the markup rather than through the model so that the cache is a fact about the
    /// file rather than something this library decided.
    /// </summary>
    private static IEnumerable<(WordColor Color, uint Cached)> CachedColors(string path)
    {
        string markup;
        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.GetEntry("word/document.xml") is not { } entry)
                yield break;

            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            markup = reader.ReadToEnd();
        }
        catch (InvalidDataException)
        {
            yield break;
        }

        foreach (Match match in ColorElement().Matches(markup))
        {
            string element = match.Value;
            if (Attribute(element, "themeColor") is not { } slot ||
                Attribute(element, "val") is not { } value ||
                WordColor.ParseThemeSlot(slot) is var parsed && parsed == ThemeColorSlot.None ||
                !uint.TryParse(value, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out uint cached))
            {
                continue;
            }

            yield return (
                WordColor.FromTheme(parsed, Byte(element, "themeTint"), Byte(element, "themeShade")),
                cached);
        }
    }

    private static string? Attribute(string element, string name)
    {
        Match match = Regex.Match(element, $"w:{name}=\"([^\"]*)\"", RegexOptions.None, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups[1].Value : null;
    }

    private static byte Byte(string element, string name) =>
        Attribute(element, name) is { } value &&
        byte.TryParse(value, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out byte parsed)
            ? parsed
            : (byte)0;

    private static int Distance(uint left, uint right) =>
        Math.Abs((int)((left >> 16) & 0xFF) - (int)((right >> 16) & 0xFF))
        + Math.Abs((int)((left >> 8) & 0xFF) - (int)((right >> 8) & 0xFF))
        + Math.Abs((int)(left & 0xFF) - (int)(right & 0xFF));

    private static int Brightness(uint rgb) =>
        (int)((rgb >> 16) & 0xFF) + (int)((rgb >> 8) & 0xFF) + (int)(rgb & 0xFF);

    private static async Task<WordDocument> AnyThemedAsync()
    {
        foreach (string path in Packages())
        {
            WordDocument document;
            try
            {
                document = await WordDocument.LoadAsync(path, cancellationToken: TestContext.Current.CancellationToken);
            }
            catch (Quillwright.Diagnostics.DocxFormatException)
            {
                continue;
            }

            if (document.Theme?.Slot(ThemeColorSlot.Accent1) is not null)
                return document;
        }

        Assert.Skip(ReferenceCorpus.Absent);
        throw new InvalidOperationException("unreachable");
    }

    private static IEnumerable<string> Packages()
    {
        foreach (string root in CorpusRoots)
        {
            if (!Directory.Exists(root))
                continue;

            foreach (string path in Directory.EnumerateFiles(root, "*.docx", SearchOption.AllDirectories))
            {
                if (new FileInfo(path).Length is > 0 and < 8 * 1024 * 1024)
                    yield return path;
            }
        }
    }

    [GeneratedRegex("<w:color [^>]*/>", RegexOptions.None, matchTimeoutMilliseconds: 5000)]
    private static partial Regex ColorElement();
}
