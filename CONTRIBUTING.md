# Contributing

Thank you for improving Quillwright. Bug reports should include a minimal reproducer and the
exact package, .NET, and operating-system versions. Security reports follow
[SECURITY.md](SECURITY.md), not the public issue tracker.

## Development setup

Install the .NET SDK selected by [`global.json`](global.json), then run from the repository root:

```powershell
dotnet restore Quillwright.slnx
dotnet build Quillwright.slnx -c Release --no-restore
dotnet test Quillwright.slnx -c Release --no-build --no-restore
dotnet format Quillwright.slnx --verify-no-changes --no-restore --severity warn
```

The ordinary test suite is self-contained. Optional real-world DOC/DOCX corpora exercise broader
compatibility; their provenance and setup are described in [docs/testing.md](docs/testing.md).
Do not commit those third-party corpora or generated files under `artifacts/`.

## Changes

- Keep a change focused and add a regression test for observable behaviour.
- Update the relevant file under `docs/` in the same change when behaviour, limits, or operation
  changes. Prefer correcting an existing guide over adding a parallel one.
- Preserve unknown document parts and markup unless the change explicitly owns their conversion.
- Treat documents, XML, archives, media references, and legacy binary formats as untrusted input;
  new readers need bounded work and cancellation where their public API supports it.
- Public API changes must pass package validation and need a changelog entry. Breaking changes
  require an explicit rationale and a release plan.

Before submitting, run the repository coverage command documented in
[docs/testing.md](docs/testing.md) and the release verification described in
[`docs/releasing.md`](docs/releasing.md). If a corpus is installed, report its exact revision and
the number of executed/skipped cases.

By contributing, you agree that your contribution is licensed under the repository's
[Apache License 2.0](LICENSE).
