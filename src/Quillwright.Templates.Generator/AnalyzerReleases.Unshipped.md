; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------------------------------------------------------------
QWT001  | Quillwright.Templates | Error | A type marked with [WordTemplate] and all its containing types must be declared partial.
QWT002  | Quillwright.Templates | Error | A type shape cannot implement the generated Word template model.
QWT003  | Quillwright.Templates | Error | A member marked with [TemplateRows] must be a sequence of [WordTemplate] models.
