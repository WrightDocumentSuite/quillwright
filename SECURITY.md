# Security policy

## Supported versions

Quillwright is still pre-1.0. Security fixes are made on the latest released version only.
Please upgrade before reporting a problem that is already fixed on the default branch.

## Reporting a vulnerability

Do not open a public issue with an exploit, malicious document, or other sensitive details.
Use [GitHub private vulnerability reporting](https://github.com/WrightDocumentSuite/quillwright/security/advisories/new)
instead. If that form is unavailable, open an issue containing only a request for a private
contact channel; do not attach the proof of concept.

Include, where possible:

- the affected package and exact version;
- the runtime and operating system;
- the smallest document or byte sequence that reproduces the problem;
- the impact, including whether the input crosses a trust boundary;
- any workaround you have confirmed.

We aim to acknowledge a report within five business days and provide an initial assessment
within ten. Timelines for a fix and coordinated disclosure depend on severity and release risk.
Please allow a patch to be published before disclosing the details.

## Security boundary

Documents and their related resources must be treated as untrusted input. The supported limits,
external-resource policy, encryption behaviour, and cancellation guarantees are documented in
[Loading untrusted input](docs/loading-untrusted-input.md). A parser accepting a document is not
a statement that its macros, links, embedded objects, or signatures are trustworthy.
