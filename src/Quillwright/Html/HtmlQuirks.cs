namespace Quillwright.Html;

/// <summary>
/// Whether a doctype puts the document in quirks mode (WHATWG HTML §13.2.6.4.1), which the
/// parser consults in exactly one place: a <c>table</c> start tag closes an open paragraph
/// unless the document is in quirks mode.
/// </summary>
/// <remarks>
/// The identifiers are the standard's own lists, compared without regard to case as it
/// requires. They name the browsers and authoring tools of the 1990s, which is the point: a
/// document claiming one of them was written for a layout nobody has implemented since.
/// </remarks>
internal static class HtmlQuirks
{
    private static readonly string[] PublicPrefixes =
    [
        "+//Silmaril//dtd html Pro v0r11 19970101//",
        "-//AS//DTD HTML 3.0 asWedit + extensions//",
        "-//AdvaSoft Ltd//DTD HTML 3.0 asWedit + extensions//",
        "-//IETF//DTD HTML 2.0 Level 1//",
        "-//IETF//DTD HTML 2.0 Level 2//",
        "-//IETF//DTD HTML 2.0 Strict Level 1//",
        "-//IETF//DTD HTML 2.0 Strict Level 2//",
        "-//IETF//DTD HTML 2.0 Strict//",
        "-//IETF//DTD HTML 2.0//",
        "-//IETF//DTD HTML 2.1E//",
        "-//IETF//DTD HTML 3.0//",
        "-//IETF//DTD HTML 3.2 Final//",
        "-//IETF//DTD HTML 3.2//",
        "-//IETF//DTD HTML 3//",
        "-//IETF//DTD HTML Level 0//",
        "-//IETF//DTD HTML Level 1//",
        "-//IETF//DTD HTML Level 2//",
        "-//IETF//DTD HTML Level 3//",
        "-//IETF//DTD HTML Strict Level 0//",
        "-//IETF//DTD HTML Strict Level 1//",
        "-//IETF//DTD HTML Strict Level 2//",
        "-//IETF//DTD HTML Strict Level 3//",
        "-//IETF//DTD HTML Strict//",
        "-//IETF//DTD HTML//",
        "-//Metrius//DTD Metrius Presentational//",
        "-//Microsoft//DTD Internet Explorer 2.0 HTML Strict//",
        "-//Microsoft//DTD Internet Explorer 2.0 HTML//",
        "-//Microsoft//DTD Internet Explorer 2.0 Tables//",
        "-//Microsoft//DTD Internet Explorer 3.0 HTML Strict//",
        "-//Microsoft//DTD Internet Explorer 3.0 HTML//",
        "-//Microsoft//DTD Internet Explorer 3.0 Tables//",
        "-//Netscape Comm. Corp.//DTD HTML//",
        "-//Netscape Comm. Corp.//DTD Strict HTML//",
        "-//O'Reilly and Associates//DTD HTML 2.0//",
        "-//O'Reilly and Associates//DTD HTML Extended 1.0//",
        "-//O'Reilly and Associates//DTD HTML Extended Relaxed 1.0//",
        "-//SQ//DTD HTML 2.0 HoTMetaL + extensions//",
        "-//SoftQuad Software//DTD HoTMetaL PRO 6.0::19990601::extensions to HTML 4.0//",
        "-//SoftQuad//DTD HoTMetaL PRO 4.0::19971010::extensions to HTML 4.0//",
        "-//Spyglass//DTD HTML 2.0 Extended//",
        "-//Sun Microsystems Corp.//DTD HotJava HTML//",
        "-//Sun Microsystems Corp.//DTD HotJava Strict HTML//",
        "-//W3C//DTD HTML 3 1995-03-24//",
        "-//W3C//DTD HTML 3.2 Draft//",
        "-//W3C//DTD HTML 3.2 Final//",
        "-//W3C//DTD HTML 3.2//",
        "-//W3C//DTD HTML 3.2S Draft//",
        "-//W3C//DTD HTML 4.0 Frameset//",
        "-//W3C//DTD HTML 4.0 Transitional//",
        "-//W3C//DTD HTML Experimental 19960712//",
        "-//W3C//DTD HTML Experimental 970421//",
        "-//W3C//DTD W3 HTML//",
        "-//W3O//DTD W3 HTML 3.0//",
        "-//WebTechs//DTD Mozilla HTML 2.0//",
        "-//WebTechs//DTD Mozilla HTML//",
    ];

    private static readonly string[] PublicIdentifiers =
    [
        "-//W3O//DTD W3 HTML Strict 3.0//EN//",
        "-/W3C/DTD HTML 4.0 Transitional/EN",
        "HTML",
    ];

    private static readonly string[] SystemIdentifiers =
    [
        "http://www.ibm.com/data/dtd/v11/ibmxhtml1-transitional.dtd",
    ];

    /// <summary>The prefixes that force quirks only when there is no system identifier.</summary>
    private static readonly string[] PublicPrefixesWithoutSystem =
    [
        "-//W3C//DTD HTML 4.01 Frameset//",
        "-//W3C//DTD HTML 4.01 Transitional//",
    ];

    /// <summary>Whether the identifiers of a doctype force quirks mode.</summary>
    /// <param name="publicIdentifier">The public identifier, or <see langword="null"/> when missing.</param>
    /// <param name="systemIdentifier">The system identifier, or <see langword="null"/> when missing.</param>
    public static bool ForcesQuirks(string? publicIdentifier, string? systemIdentifier)
    {
        if (systemIdentifier is { } system && Contains(SystemIdentifiers, system))
            return true;

        if (publicIdentifier is not { } identifier)
            return false;

        if (Contains(PublicIdentifiers, identifier) || StartsWithAny(PublicPrefixes, identifier))
            return true;

        return string.IsNullOrEmpty(systemIdentifier) && StartsWithAny(PublicPrefixesWithoutSystem, identifier);
    }

    private static bool Contains(string[] values, string candidate)
    {
        foreach (string value in values)
        {
            if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool StartsWithAny(string[] prefixes, string candidate)
    {
        foreach (string prefix in prefixes)
        {
            if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
