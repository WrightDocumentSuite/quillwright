namespace Quillwright.Doc;

/// <summary>
/// The property-modifier opcodes this library reads and writes, named as [MS-DOC] 2.6 names
/// them.
/// </summary>
/// <remarks>
/// The opcodes encode their own operand size in the top three bits, and several of them sit
/// one apart from a neighbour that means something entirely different — <c>sprmCFOutline</c>
/// is 0x0838 and <c>sprmCFCaps</c> is 0x083B, with shadow, small caps and hidden text in
/// between. Naming them in one place is what keeps the reader and the writer honest about
/// which is which.
/// </remarks>
internal static class SprmCode
{
    // Character properties (sprmC*).
    public const ushort CharacterStyle = 0x4A30;        // sprmCIstd
    public const ushort Bold = 0x0835;                  // sprmCFBold
    public const ushort Italic = 0x0836;                // sprmCFItalic
    public const ushort Strike = 0x0837;                // sprmCFStrike
    public const ushort Outline = 0x0838;               // sprmCFOutline
    public const ushort Shadow = 0x0839;                // sprmCFShadow
    public const ushort SmallCaps = 0x083A;             // sprmCFSmallCaps
    public const ushort Caps = 0x083B;                  // sprmCFCaps
    public const ushort Hidden = 0x083C;                // sprmCFVanish
    public const ushort DoubleStrike = 0x2A53;          // sprmCFDStrike
    public const ushort Imprint = 0x0854;               // sprmCFImprint
    public const ushort Emboss = 0x0858;                // sprmCFEmboss
    public const ushort NoProof = 0x0875;               // sprmCFNoProof
    public const ushort WebHidden = 0x0811;             // sprmCFWebHidden
    public const ushort Special = 0x0855;               // sprmCFSpec
    public const ushort EmbeddedObject = 0x080A;        // sprmCFOle2
    public const ushort BoldComplexScript = 0x085C;     // sprmCFBoldBi
    public const ushort ItalicComplexScript = 0x085D;   // sprmCFItalicBi
    public const ushort Underline = 0x2A3E;             // sprmCKul
    public const ushort Highlight = 0x2A0C;             // sprmCHighlight
    public const ushort VerticalAlignment = 0x2A48;     // sprmCIss
    public const ushort ColorIndexed = 0x2A42;          // sprmCIco
    public const ushort ColorTrue = 0x6870;             // sprmCCv
    public const ushort FontSize = 0x4A43;              // sprmCHps
    public const ushort FontSizeComplexScript = 0x4A61; // sprmCHpsBi
    public const ushort Position = 0x4845;              // sprmCHpsPos
    public const ushort Kerning = 0x484B;               // sprmCHpsKern
    public const ushort CharacterSpacing = 0x8840;      // sprmCDxaSpace
    public const ushort CharacterScale = 0x4852;        // sprmCCharScale
    public const ushort FontAscii = 0x4A4F;             // sprmCRgFtc0
    public const ushort FontEastAsia = 0x4A50;          // sprmCRgFtc1
    public const ushort FontComplexScript = 0x4A51;     // sprmCRgFtc2
    public const ushort PictureLocation = 0x6A03;       // sprmCPicLocation

    // Paragraph properties (sprmP*).
    public const ushort ParagraphStyle = 0x4600;        // sprmPIstd
    public const ushort Alignment = 0x2403;             // sprmPJc80
    public const ushort AlignmentNew = 0x2461;          // sprmPJc
    public const ushort KeepLinesTogether = 0x2405;     // sprmPFKeep
    public const ushort KeepWithNext = 0x2406;          // sprmPFKeepFollow
    public const ushort PageBreakBefore = 0x2407;       // sprmPFPageBreakBefore
    public const ushort WidowControl = 0x2431;          // sprmPFWidowControl
    public const ushort ContextualSpacing = 0x246D;     // sprmPFContextualSpacing
    public const ushort OutlineLevel = 0x2640;          // sprmPOutLvl
    public const ushort NumberingLevel = 0x260A;        // sprmPIlvl
    public const ushort NumberingId = 0x460B;           // sprmPIlfo
    public const ushort IndentRight = 0x840E;           // sprmPDxaRight80
    public const ushort IndentLeft = 0x840F;            // sprmPDxaLeft80
    public const ushort IndentRightNew = 0x845D;        // sprmPDxaRight
    public const ushort IndentLeftNew = 0x845E;         // sprmPDxaLeft
    public const ushort IndentFirstLine = 0x8411;       // sprmPDxaLeft180
    public const ushort LineSpacing = 0x6412;           // sprmPDyaLine
    public const ushort SpacingBefore = 0xA413;         // sprmPDyaBefore
    public const ushort SpacingAfter = 0xA414;          // sprmPDyaAfter
    public const ushort InTable = 0x2416;               // sprmPFInTable
    public const ushort RowEnd = 0x2417;                // sprmPFTtp
    public const ushort TableDepth = 0x6649;            // sprmPItap
    public const ushort InnerTableCell = 0x244B;        // sprmPFInnerTableCell
    public const ushort InnerRowEnd = 0x244C;           // sprmPFInnerTtp
    public const ushort HugeParagraphProperties = 0x6646; // sprmPHugePapx
    public const ushort TabStops = 0xC60D;              // sprmPChgTabsPapx
    public const ushort ParagraphBorderTop = 0x6424;    // sprmPBrcTop80
    public const ushort ParagraphBorderLeft = 0x6425;   // sprmPBrcLeft80
    public const ushort ParagraphBorderBottom = 0x6426; // sprmPBrcBottom80
    public const ushort ParagraphBorderRight = 0x6427;  // sprmPBrcRight80
    public const ushort ParagraphBorderBetween = 0x6428; // sprmPBrcBetween80
    public const ushort ParagraphShading = 0xC64D;      // sprmPShd

    // Table properties (sprmT*).
    public const ushort TableDefinition = 0xD608;       // sprmTDefTable
    public const ushort TableIndent = 0x9601;           // sprmTDxaLeft
    public const ushort TableGapHalf = 0x9602;          // sprmTDxaGapHalf
    public const ushort TableRowHeight = 0x9407;        // sprmTDyaRowHeight
    public const ushort TableHeaderRow = 0x3404;        // sprmTTableHeader
    public const ushort TableCannotSplit = 0x3403;      // sprmTFCantSplit90
    public const ushort TableShading = 0xD612;          // sprmTDefTableShd

    // Section properties (sprmS*).
    public const ushort SectionBreak = 0x3009;          // sprmSBkc
    public const ushort TitlePage = 0x300A;             // sprmSFTitlePage
    public const ushort ColumnCount = 0x500B;           // sprmSCcolumns
    public const ushort ColumnSpacing = 0x900C;         // sprmSDxaColumns
    public const ushort PageNumberFormat = 0x300E;      // sprmSNfcPgn
    public const ushort PageNumberRestart = 0x3011;     // sprmSFPgnRestart
    public const ushort MarginHeader = 0xB017;          // sprmSDyaHdrTop
    public const ushort MarginFooter = 0xB018;          // sprmSDyaHdrBottom
    public const ushort PageNumberStart = 0x501C;       // sprmSPgnStart97
    public const ushort Orientation = 0x301D;           // sprmSBOrientation
    public const ushort PageWidth = 0xB01F;             // sprmSXaPage
    public const ushort PageHeight = 0xB020;            // sprmSYaPage
    public const ushort MarginLeft = 0xB021;            // sprmSDxaLeft
    public const ushort MarginRight = 0xB022;           // sprmSDxaRight
    public const ushort MarginTop = 0x9023;             // sprmSDyaTop
    public const ushort MarginBottom = 0x9024;          // sprmSDyaBottom
    public const ushort Gutter = 0xB025;                // sprmSDzaGutter
}
