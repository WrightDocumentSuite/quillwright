# Streaming

The document model is the right tool when a document will be read, changed and looked at from
several angles. Two other cases do not need it: generating a report that is never read back,
and pulling text out of documents that will not be changed. For those, memory spent on a tree
is memory wasted.

## Writing

`DocxWriter` produces the same markup the model does — it shares the writing code — but keeps
only the current block alive.

```csharp
await using DocxWriter writer = await DocxWriter.CreateAsync("ledger.docx");

writer.Styles.GetOrAdd("Heading1");
writer.Section.Orientation = PageOrientation.Landscape;
writer.Properties.Title = "Transaction ledger";

writer.WriteParagraph("Transactions", styleId: "Heading1");
for (int i = 1; i <= 100_000; i++)
{
    writer.WriteParagraph($"{i:D6}\tAccount {i % 997:D3}", RunFormat.Default with { FontAscii = "Consolas" });
    await writer.FlushIfNeededAsync();
}
```

Styles, page setup and properties are configured before the first block and written when the
package closes. Blocks built in memory can be written directly, so anything the model can
express can be streamed:

```csharp
Table table = Table.Create(1, 3);
table[0, 0].SetText("Item");
writer.WriteTable(table);

var paragraph = new Paragraph();
paragraph.AppendPicture(logo, Length.FromCentimeters(3));
writer.WriteParagraph(paragraph);
```

Pictures are reserved as they are encountered and their bytes written when the package closes,
because a forward-only zip can only have one entry open at a time and that entry is the body.

The writer produces one section. A document with several sections needs the model.

## Reading

`DocxReader` yields one top-level block at a time, so memory tracks the largest paragraph or
table rather than the size of the file. Each block is a fully modelled `Paragraph` or `Table`,
so everything the model can say about a paragraph applies here too.

```csharp
await using DocxReader reader = await DocxReader.OpenAsync("contract.docx");

await foreach (Block block in reader.ReadBlocksAsync())
{
    if (block is Paragraph paragraph && paragraph.Format.StyleId?.StartsWith("Heading") == true)
        Console.WriteLine(paragraph.GetText());
}
```

For text alone:

```csharp
await using DocxReader reader = await DocxReader.OpenAsync(path);
await foreach (string line in reader.ReadTextAsync())
    index.Add(line);
```

Streaming does not mean unbounded. Pass `LoadOptions` to the options method to apply
the same input, ZIP expansion, part-count and XML limits as model loading:

```csharp
var options = new LoadOptions
{
    Budget = DocumentLoadBudget.Default with { MaxInputBytes = 32 * 1024 * 1024 },
};
await using DocxReader reader = await DocxReader.OpenWithOptionsAsync(path, options);
```

See [loading-untrusted-input.md](loading-untrusted-input.md) for the complete budget.

The package is opened asynchronously and the blocks are parsed straight off the decompressed
stream. Buffering each block into a string and standing up a reader for it was the first
design, and it allocated eighteen times what loading the whole document did — a good reminder
that "streaming" is a memory claim, not a syntax.

## Which to use

| | Model | Streaming |
| --- | --- | --- |
| Change an existing document | yes | no |
| Several sections, headers, footnotes | yes | body only |
| Search, replace, style resolution | yes | per block |
| Generating a report once | works | faster, and about half the allocation |
| Extracting text from many files | works | faster, constant memory |

Measured numbers are in [benchmarks.md](benchmarks.md).
