using Quillwright.Samples;

string output = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "quillwright-samples");
Directory.CreateDirectory(output);

Console.WriteLine($"Writing samples to {output}");
await BuildFromScratch.RunAsync(output);
await EditExistingDocument.RunAsync(output);
await FillTemplate.RunAsync(output);
await StreamLargeReport.RunAsync(output);
await ExtractText.RunAsync(output);
Console.WriteLine("Done.");
