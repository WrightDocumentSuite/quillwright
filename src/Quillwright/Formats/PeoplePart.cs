using System.Xml;
using Quillwright.IO;
using Quillwright.Model;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>
/// Reads and writes the identities behind the author names ([MS-DOCX] 2.5.3.4,
/// <c>people.xml</c>).
/// </summary>
/// <remarks>
/// A comment or a tracked change names its author as free text. This part says which account
/// that text stands for, which is what lets Word show a picture beside a comment and tell two
/// reviewers of the same name apart.
/// </remarks>
internal static class PeoplePart
{
    /// <summary>Reads the identities into the document.</summary>
    /// <param name="xml">Reader over the part.</param>
    /// <param name="document">The document being loaded.</param>
    public static void Read(XmlReader xml, WordDocument document)
    {
        StylesPartReader.MoveToRoot(xml, "people");
        if (xml.NodeType != XmlNodeType.Element)
            return;

        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            if (name != "person")
            {
                reader.Skip();
                return;
            }

            var person = new Person(Attribute(reader, "author") ?? string.Empty);
            XmlHelp.ForEachChild(reader, (child, childName) =>
            {
                if (childName == "presenceInfo")
                {
                    person.ProviderId = Attribute(child, "providerId");
                    person.UserId = Attribute(child, "userId");
                }

                child.Skip();
            });

            document.PeopleList.Add(person);
        });
    }

    /// <summary>The part holding the identities, or <see langword="null"/> when there is none.</summary>
    /// <param name="preserved">The package as loaded.</param>
    public static string? FindPart(PreservedPackage preserved)
    {
        OpcRelationship relationship = preserved.MainRelationships.FirstOrDefault(
            static r => r.Is(DocxSchema.RelPeople));
        return relationship.Target is null ? null : OpcPath.Resolve(preserved.MainPartPath, relationship.Target);
    }

    /// <summary>
    /// Adds an entry for every comment author the part does not already name, so that a
    /// comment added since the document was loaded does not leave the part contradicting it.
    /// </summary>
    /// <param name="document">The document being written.</param>
    /// <remarks>
    /// Nothing is ever removed. The part covers the authors of tracked changes as well as of
    /// comments, and those are scattered over every revision wrapper in the document; dropping
    /// an entry that only looks unused would throw away presence information that is still
    /// referred to.
    /// </remarks>
    public static void Reconcile(WordDocument document)
    {
        var known = new HashSet<string>(document.People.Select(static person => person.Author), StringComparer.Ordinal);
        foreach (Comment comment in document.Comments)
        {
            if (comment.Author is not { Length: > 0 } author || !known.Add(author))
                continue;

            // With no directory behind the name, the identity is the name itself
            // ([MS-DOCX] 2.5.3.6).
            document.PeopleList.Add(new Person(author) { ProviderId = "None", UserId = author });
        }
    }

    /// <summary>Writes the part.</summary>
    /// <param name="writer">The part's writer.</param>
    /// <param name="document">The document being written.</param>
    public static void Write(Utf8XmlWriter writer, WordDocument document)
    {
        writer.WriteDeclaration();
        writer.WriteRaw("<w15:people xmlns:w15=\""u8);
        writer.WriteRawXml(DocxSchema.NsW15);
        writer.WriteRaw("\" xmlns:mc=\""u8);
        writer.WriteRawXml(DocxSchema.NsMarkupCompatibility);
        writer.WriteRaw("\" mc:Ignorable=\"w15\">"u8);

        foreach (Person person in document.People)
        {
            writer.WriteRaw("<w15:person w15:author=\""u8);
            writer.WriteAttributeText(person.Author);
            writer.WriteRaw("\">"u8);

            if (person.ProviderId is not null || person.UserId is not null)
            {
                writer.WriteRaw("<w15:presenceInfo w15:providerId=\""u8);
                writer.WriteAttributeText(person.ProviderId ?? "None");
                writer.WriteRaw("\" w15:userId=\""u8);
                writer.WriteAttributeText(person.UserId ?? person.Author);
                writer.WriteRaw("\"/>"u8);
            }

            writer.WriteRaw("</w15:person>"u8);
        }

        writer.WriteRaw("</w15:people>"u8);
    }

    private static string? Attribute(XmlReader xml, string name) =>
        xml.GetAttribute(name, DocxSchema.NsW15) ?? xml.GetAttribute("w15:" + name);
}
