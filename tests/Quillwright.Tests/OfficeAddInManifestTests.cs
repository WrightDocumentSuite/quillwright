using System.Text;
using Quillwright.IO;
using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// The manifest of an Office add-in ([MS-OWEMXML]): a file that never enters a document, read
/// on its own for the metadata its two base namespaces share.
/// </summary>
/// <remarks>
/// Every fixture is built from an example or a schema constraint in the specification rather
/// than from anything this reader produced, so a reader that has misunderstood the format
/// cannot agree with itself.
/// </remarks>
public class OfficeAddInManifestTests
{
    /// <summary>The content add-in of [MS-OWEMXML] section 3.1, verbatim.</summary>
    private const string ContentApp =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <OfficeApp xmlns="http://schemas.microsoft.com/office/appforoffice/1.0"
            xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
            xmlns:ver="http://schemas.microsoft.com/office/appforoffice/1.0"
            xsi:type="ContentApp">
          <Id>df5b5660-84ce-11e1-b0c4-0800200c9a66</Id>
          <AlternateId>en-US\WA123456789</AlternateId>
          <Version>1.0.0.0</Version>
          <ProviderName>Microsoft</ProviderName>
          <DefaultLocale>en-US.pseudo</DefaultLocale>
          <DisplayName DefaultValue="AuthentiMOE" />
          <Description DefaultValue="Authenticates to various services" />
          <IconUrl DefaultValue="http://www.contoso.com/Bonsai1.png" />
          <Capabilities>
            <Capability Name="Workbook" />
          </Capabilities>
          <DefaultSettings>
            <SourceLocation DefaultValue="http://www.contoso.com/AuthentiMoe.html" />
            <RequestedWidth>400</RequestedWidth>
            <RequestedHeight>400</RequestedHeight>
          </DefaultSettings>
          <Permissions>Restricted</Permissions>
          <AllowSnapshot>true</AllowSnapshot>
        </OfficeApp>
        """;

    /// <summary>
    /// A 1.0 task-pane add-in with localized wording, after [MS-OWEMXML] section 3.2 with its
    /// right-to-left default values written in Latin script so the fixture stays legible.
    /// </summary>
    private const string TaskPaneApp =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <OfficeApp xmlns="http://schemas.microsoft.com/office/appforoffice/1.0"
            xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
            xsi:type="TaskPaneApp">
          <Id>urn:uuid:ff3a1120-87ed-11e1-b0c4-0800200c9a66</Id>
          <Version>1.0</Version>
          <ProviderName>Microsoft</ProviderName>
          <DefaultLocale>ar-SA</DefaultLocale>
          <DisplayName DefaultValue="mashrue altatbiq">
            <Override Value="Project App" Locale="en-US" />
          </DisplayName>
          <Description DefaultValue="yudif maelumat 'iidarat almashrue">
            <Override Value="Adds project management information to documents" Locale="en-US" />
          </Description>
          <AppDomains>
            <AppDomain>www.contoso.com</AppDomain>
            <AppDomain>m.contoso.com</AppDomain>
          </AppDomains>
          <Capabilities>
            <Capability Name="Workbook" />
            <Capability Name="Document" />
            <Capability Name="Project" />
          </Capabilities>
          <DefaultSettings>
            <SourceLocation DefaultValue="http://www.contoso.com.sa/ProjectApp/ProjectiMoear_SA.html">
              <Override Value="http://www.contoso.com/ProjectApp/ProjectiMoeen-US.html" Locale="en-US" />
            </SourceLocation>
          </DefaultSettings>
          <Permissions>ReadDocument</Permissions>
        </OfficeApp>
        """;

    /// <summary>The 1.0 mail add-in of [MS-OWEMXML] section 3.4, which gives three surfaces a page each.</summary>
    private const string MailApp =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <OfficeApp xmlns="http://schemas.microsoft.com/office/appforoffice/1.0"
            xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:type="MailApp">
          <Id>FA55E9EA-52A4-4198-B23A-F106E223AB63</Id>
          <Version>1.0.75</Version>
          <ProviderName>Microsoft</ProviderName>
          <DefaultLocale>en-US</DefaultLocale>
          <DisplayName DefaultValue="Lync Dialer">
            <Override Locale="en-US" Value="Lync Dialer"/>
          </DisplayName>
          <Description DefaultValue="Use this web extension to dial phone numbers using Lync." />
          <Capabilities>
            <Capability Name="Mailbox"/>
          </Capabilities>
          <DesktopSettings>
            <SourceLocation DefaultValue="https://www.contoso.com/dialer/dtdialer.htm" />
            <RequestedHeight>250</RequestedHeight>
          </DesktopSettings>
          <TabletSettings>
            <SourceLocation DefaultValue="https://www.contoso.com/dialer/tdialer.htm" />
            <RequestedHeight>150</RequestedHeight>
          </TabletSettings>
          <PhoneSettings>
            <SourceLocation DefaultValue="https://www.contoso.com/dialer/pdialer.htm" />
          </PhoneSettings>
          <Permissions>ReadItem</Permissions>
          <Rule xsi:type="RuleCollection" Mode="And">
            <Rule xsi:type="ItemIs" ItemType="Message"/>
          </Rule>
          <DisableEntityHighlighting>false</DisableEntityHighlighting>
        </OfficeApp>
        """;

    /// <summary>The 1.1 mail add-in with overrides of [MS-OWEMXML] section 3.5, shortened.</summary>
    private const string MailAppWithOverrides =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <OfficeApp
          xmlns="http://schemas.microsoft.com/office/appforoffice/1.1"
          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xmlns:bt="http://schemas.microsoft.com/office/officeappbasictypes/1.0"
          xsi:type="MailApp">
          <Id>{997062B1-1AF3-48BC-8AE3-BB25CAB9D4CA}</Id>
          <Version>1.0</Version>
          <ProviderName>Microsoft</ProviderName>
          <DefaultLocale>en-us</DefaultLocale>
          <DisplayName DefaultValue="Add-In Commands Full Sample"></DisplayName>
          <Description DefaultValue="Sample add-in that showcases different command actions">
           </Description>
          <Hosts>
            <Host Name="Mailbox" />
            <Host Name="SomethingNobodyHasShippedYet" />
          </Hosts>
          <Requirements>
            <Sets DefaultMinVersion="1.1">
              <Set Name="Mailbox" />
            </Sets>
          </Requirements>
          <FormSettings>
            <Form xsi:type="ItemRead">
              <DesktopSettings>
                <SourceLocation DefaultValue="https://contoso.com/pageRead.html" />
                <RequestedHeight>150</RequestedHeight>
              </DesktopSettings>
            </Form>
            <Form xsi:type="ItemEdit">
              <DesktopSettings>
                <SourceLocation DefaultValue="https://contoso.com/page.html" />
              </DesktopSettings>
            </Form>
          </FormSettings>
          <Permissions>ReadWriteItem</Permissions>
          <VersionOverrides xmlns="http://schemas.microsoft.com/office/mailappversionoverrides"
        xsi:type="VersionOverridesV1_0">
            <Description resid="residDescription" />
            <Requirements>
              <bt:Sets DefaultMinVersion="1.3">
                <bt:Set Name="Mailbox" />
              </bt:Sets>
            </Requirements>
          </VersionOverrides>
        </OfficeApp>
        """;

    private static OfficeAddInManifest Read(string manifest) =>
        OfficeAddInManifestReader.Read(Encoding.UTF8.GetBytes(manifest))!;

    [Fact]
    public void AContentAddIn_SaysWhatItIsAndWhoWroteIt()
    {
        OfficeAddInManifest manifest = Read(ContentApp);

        Assert.Equal("http://schemas.microsoft.com/office/appforoffice/1.0", manifest.Namespace);
        Assert.Equal(OfficeAddInKind.ContentApp, manifest.Kind);
        Assert.Equal("ContentApp", manifest.DeclaredType);
        Assert.Equal("df5b5660-84ce-11e1-b0c4-0800200c9a66", manifest.Id);
        Assert.Equal("1.0.0.0", manifest.Version);
        Assert.Equal("Microsoft", manifest.ProviderName);
        Assert.Equal("en-US.pseudo", manifest.DefaultLocale);
        Assert.Equal("AuthentiMOE", manifest.DisplayName!.DefaultValue);
        Assert.Equal("Authenticates to various services", manifest.Description!.DefaultValue);
        Assert.Equal("Restricted", manifest.Permissions);
        Assert.Equal(["Workbook"], manifest.Capabilities);
    }

    [Fact]
    public void AContentAddIn_SaysWherePageComesFromAndHowLargeItWantsToBe()
    {
        OfficeAddInSourceLocation page = Assert.Single(Read(ContentApp).SourceLocations);

        Assert.Equal("DefaultSettings", page.Context);
        Assert.Equal("http://www.contoso.com/AuthentiMoe.html", page.Url.DefaultValue);
        Assert.Equal(400, page.RequestedWidth);
        Assert.Equal(400, page.RequestedHeight);
    }

    [Fact]
    public void ATranslatedSetting_KeepsBothWordingsRatherThanOne()
    {
        OfficeAddInManifest manifest = Read(TaskPaneApp);

        Assert.Equal(OfficeAddInKind.TaskPaneApp, manifest.Kind);
        Assert.Equal("mashrue altatbiq", manifest.DisplayName!.DefaultValue);
        Assert.Equal(new LocaleOverride("en-US", "Project App"), Assert.Single(manifest.DisplayName.Overrides));
        Assert.Equal("Project App", manifest.DisplayName.For("en-us"));
        Assert.Equal("mashrue altatbiq", manifest.DisplayName.For("ar-SA"));
        Assert.Equal(
            "Adds project management information to documents",
            manifest.Description!.For("en-US"));
    }

    [Fact]
    public void ATranslatedSourceLocation_IsTranslatedToo()
    {
        OfficeAddInSourceLocation page = Assert.Single(Read(TaskPaneApp).SourceLocations);

        Assert.Equal("http://www.contoso.com.sa/ProjectApp/ProjectiMoear_SA.html", page.Url.DefaultValue);
        Assert.Equal("http://www.contoso.com/ProjectApp/ProjectiMoeen-US.html", page.Url.For("en-US"));
        Assert.Null(page.RequestedWidth);
    }

    /// <summary>
    /// A mail add-in gives the desktop, the tablet and the phone a page each, and flattening
    /// them into one would leave two of the three devices loading the wrong thing.
    /// </summary>
    [Fact]
    public void ThreeSurfacesWithThreePages_StayThreePages()
    {
        OfficeAddInManifest manifest = Read(MailApp);

        Assert.Equal(OfficeAddInKind.MailApp, manifest.Kind);
        Assert.Equal(
            ["DesktopSettings", "TabletSettings", "PhoneSettings"],
            manifest.SourceLocations.Select(static page => page.Context));
        Assert.Equal(
            [
                "https://www.contoso.com/dialer/dtdialer.htm",
                "https://www.contoso.com/dialer/tdialer.htm",
                "https://www.contoso.com/dialer/pdialer.htm",
            ],
            manifest.SourceLocations.Select(static page => page.Url.DefaultValue));
        Assert.Equal([250, 150, null], manifest.SourceLocations.Select(static page => page.RequestedHeight));
    }

    [Fact]
    public void TheSecondBaseNamespace_IsReadAsWellAsTheFirst()
    {
        OfficeAddInManifest manifest = Read(MailAppWithOverrides);

        Assert.Equal("http://schemas.microsoft.com/office/appforoffice/1.1", manifest.Namespace);
        Assert.Equal("{997062B1-1AF3-48BC-8AE3-BB25CAB9D4CA}", manifest.Id);
        Assert.Equal("ReadWriteItem", manifest.Permissions);
        Assert.Equal(["Mailbox", "SomethingNobodyHasShippedYet"], manifest.Hosts);
        Assert.Empty(manifest.Capabilities);
    }

    /// <summary>Two forms with a desktop page each are two pages, told apart by their type.</summary>
    [Fact]
    public void PagesNestedUnderAFormType_CarryTheFormTypeInTheirPath()
    {
        OfficeAddInManifest manifest = Read(MailAppWithOverrides);

        Assert.Equal(
            ["FormSettings/Form[ItemRead]/DesktopSettings", "FormSettings/Form[ItemEdit]/DesktopSettings"],
            manifest.SourceLocations.Select(static page => page.Context));
        Assert.Equal("https://contoso.com/pageRead.html", manifest.SourceLocations[0].Url.DefaultValue);
        Assert.Equal(150, manifest.SourceLocations[0].RequestedHeight);
    }

    [Fact]
    public void AVersionOverridesSubtree_ComesBackAsMarkupWithTheNamespaceThatIdentifiesIt()
    {
        OfficeAddInVersionOverrides overrides = Assert.Single(Read(MailAppWithOverrides).VersionOverrides);

        Assert.Equal("http://schemas.microsoft.com/office/mailappversionoverrides", overrides.Namespace);
        Assert.Contains("residDescription", overrides.Markup, StringComparison.Ordinal);
        Assert.Contains("DefaultMinVersion=\"1.3\"", overrides.Markup, StringComparison.Ordinal);

        // The markup has to stand on its own, so the prefixes it uses have to travel with it.
        Assert.Contains("officeappbasictypes", overrides.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAddInKindNobodyHasDefined_IsUnknownWithoutLosingWhatItSaid()
    {
        OfficeAddInManifest manifest = Read(
            "<OfficeApp xmlns=\"http://schemas.microsoft.com/office/appforoffice/1.1\" " +
            "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:type=\"WhiteboardApp\">" +
            "<Id>1</Id></OfficeApp>");

        Assert.Equal(OfficeAddInKind.Unknown, manifest.Kind);
        Assert.Equal("WhiteboardApp", manifest.DeclaredType);
        Assert.Equal("1", manifest.Id);
    }

    [Fact]
    public void AManifestInSomeOtherNamespace_IsRefused()
    {
        Assert.Null(OfficeAddInManifestReader.Read(Encoding.UTF8.GetBytes(
            "<OfficeApp xmlns=\"http://schemas.microsoft.com/office/appforoffice/2.0\"><Id>1</Id></OfficeApp>")));
    }

    [Fact]
    public void SomethingThatIsNotAManifest_IsRefused()
    {
        Assert.Null(OfficeAddInManifestReader.Read(Encoding.UTF8.GetBytes(
            "<Anything xmlns=\"http://schemas.microsoft.com/office/appforoffice/1.0\"/>")));
        Assert.Null(OfficeAddInManifestReader.Read(Encoding.UTF8.GetBytes("<OfficeApp><Id>1</Id></OfficeApp>")));
    }

    [Fact]
    public void MarkupThatIsNotWellFormed_IsRefusedRatherThanThrowing()
    {
        Assert.Null(OfficeAddInManifestReader.Read(Encoding.UTF8.GetBytes(
            "<OfficeApp xmlns=\"http://schemas.microsoft.com/office/appforoffice/1.0\"><Id>1</OfficeApp>")));
        Assert.Null(OfficeAddInManifestReader.Read([]));
        Assert.Null(OfficeAddInManifestReader.Read(Encoding.UTF8.GetBytes("not xml at all")));
    }

    /// <summary>
    /// A manifest is a file from a catalogue, which is to say a file from a stranger. It must
    /// not be able to make the reader open anything.
    /// </summary>
    [Fact]
    public void AManifestThatDeclaresAnExternalEntity_IsRefusedWithoutResolvingIt()
    {
        Assert.Null(OfficeAddInManifestReader.Read(Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?>" +
            "<!DOCTYPE OfficeApp [<!ENTITY secret SYSTEM \"file:///C:/Windows/win.ini\">]>" +
            "<OfficeApp xmlns=\"http://schemas.microsoft.com/office/appforoffice/1.0\">" +
            "<Id>&secret;</Id></OfficeApp>")));
    }
}
