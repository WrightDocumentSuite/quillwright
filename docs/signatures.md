# Digital signatures

A signed package carries the signature in parts of its own (ECMA-376 part 2, clause 10). The
package points at a *signature origin* part, the origin part points at one *signature* part per
signature, and each signature part is XML-Signature markup whose manifest lists the parts the
signature covers along with a digest of each.

```csharp
foreach (DigitalSignature signature in document.Signatures)
{
    Console.WriteLine($"{signature.Signer} on {signature.SignedAt:d}");
    Console.WriteLine($"  value:  {signature.ValueStatus}");
    Console.WriteLine($"  parts:  {signature.Status}");
    Console.WriteLine($"  trust:  {signature.CheckTrust()?.IsTrusted}");
}
```

| Member | What it holds |
| --- | --- |
| `Signer` | The subject of the certificate, as a display name |
| `SignedAt` | The `SignatureTime` the signer claimed (10.5.15) |
| `Comment` | The reason the signer gave, when Word recorded one |
| `Certificate` | The `X509Certificate2` from `KeyInfo/X509Data` |
| `Parts` | Each part the manifest covers, with the digest recorded for it |
| `Status` | Whether those parts are unchanged |
| `ValueStatus` | Whether the signature value verifies against the signer's key |
| `SignedInfoIntact` | Whether the references tying the manifest to the value still hold |
| `CheckTrust()` | What a chain built from the certificate finds, under a policy you supply |

## Three questions, answered separately

A signature raises three questions and they have three different answers. Collapsing them into
one word — "valid" — hides which of them failed, so they are kept apart.

**Does the mathematics hold?** `ValueStatus`. The `SignedInfo` element is canonicalised and the
signature value verified against the public key in the certificate. `Verified` means the holder
of that key signed those bytes. `Invalid` means they did not. `NotChecked` means the signature
names an algorithm this version does not perform, and says nothing either way.

| `ValueStatus` | Meaning |
| --- | --- |
| `Verified` | The value verifies against the public key of the certificate the signature carries |
| `Invalid` | It does not, so the signature has been tampered with or is not one |
| `NotChecked` | No certificate, or an algorithm not performed here |

**Are the bytes it covers unchanged?** `Status`, from the manifest: each part is hashed again
and compared. This is a separate question because a document can fail either one on its own — a
signature whose value verifies over a document somebody has since edited is exactly the case
`PartModified` exists for.

| `Status` | Meaning |
| --- | --- |
| `PartsUnchanged` | Every part the signature covers still hashes to what was recorded |
| `PartModified` | A part the signature covers has changed since it was signed |
| `Unverified` | Not everything could be checked here — it says nothing about the signature |

`SignedInfoIntact` closes the gap between the two: the value signs `SignedInfo`, and
`SignedInfo` digests the object holding the manifest. A manifest swapped after signing leaves
the value verifying and the inner references failing, which is what that flag reports.

**Is the signer anybody to believe?** `CheckTrust()`, and only when you ask. Chain building
reaches the network, depends on a certificate store that changes underneath it, and answers
only as well as the policy behind it — none of which belongs in something read once while a
file was open. Pass an `X509ChainPolicy` to say which roots to accept and whether to check
revocation; the default is the platform's.

```csharp
var policy = new X509ChainPolicy { RevocationMode = X509RevocationMode.NoCheck };
if (document.Signatures[0].CheckTrust(policy) is { IsTrusted: true, IsExpired: false })
    Console.WriteLine("signed by somebody this machine trusts");
```

## What is performed, and what is not

**Canonicalisation.** Both forms an OPC signature uses: inclusive
[Canonical XML 1.0](https://www.w3.org/TR/2001/REC-xml-c14n-20010315) and
[exclusive](https://www.w3.org/TR/xml-exc-c14n/), each with or without comments. The
`#WithComments` variants retain comments; the other two discard them. XML declarations and
byte-order marks are not canonical content, so signature and referenced XML parts are decoded
from their declarations or BOMs (including UTF-16) and canonicalised to UTF-8. The
canonicaliser is written out in the library rather than taken from
`System.Security.Cryptography.Xml`, because that library is not guaranteed to survive trimming
and this package is marked AOT-compatible. Everything verification needs — LINQ to XML and the
hashing and signing primitives — is trim-safe, which is why verification lives in the core
package rather than behind a boundary you would have to cross.

**The relationships transform** (10.6). A signature covering a `.rels` part does not digest the
part: it digests a document rebuilt from the relationships the transform names, sorted by
identifier, with `TargetMode` written out. That is what lets a new image be added to a document
without breaking a signature over its relationships, and it is performed here, so those
references are checked rather than passed over. Before selecting relationships, the part is
processed with the MCE application configuration required by clause 10.6: only the Relationships
namespace is understood. `mc:Ignorable`, `mc:ProcessContent`, `mc:MustUnderstand`, and
`mc:AlternateContent` therefore affect the digest exactly as they affect the transform's input.
`SourceId` and `SourceType` use ASCII case-insensitive matching; a transform without either kind
of reference is rejected as malformed.

**Signature algorithms.** RSA with PKCS#1 padding and RSA-PSS, and ECDSA, each over SHA-256,
SHA-384 or SHA-512; RSA with SHA-1 as well, because documents signed a decade ago used it and
looking at them is better than refusing to. RSA keys shorter than the 1024-bit OPC minimum are
not accepted. Digests: SHA-1, SHA-256, SHA-384, SHA-512.

Anything outside those lists leaves the reference or the value **not checked** rather than
failed. `SignedPart.Matches` is `null` rather than `false`, so a caller can always tell "not
checked" from "does not match".

## Reading and saving

Signature parts are preserved verbatim like any other part the model does not own, so reading
a signed document and saving it again leaves the signatures byte for byte as they were.

Editing the document is a different matter, and it is not one this library can paper over: a
signature covers `/word/document.xml`, so rewriting that part breaks it, exactly as it would
if any other tool had made the edit. The signature parts still come through, and the next
reader — this one included — will report `PartModified`.

## Signing

`DocumentSigner` signs a *saved* package, because a signature covers bytes and the package
must already be its final self. Sign as the last step; a later save needs a new signature,
which is not a limitation but what a signature means.

```csharp
await document.SaveAsync("contract.docx");
await DocumentSigner.SignAsync("contract.docx", certificate, new SigningOptions
{
    Comments = "Approved for release",
});
```

The certificate is an `X509Certificate2` that carries its private key — from a PFX, from the
Windows store, from wherever the caller's threat model keeps it; how the key is accepted, held
and released stays the caller's business, which is why the signer takes the certificate object
and never a path to key material. RSA signs with PKCS#1 over the digest of
`SigningOptions.Digest` (SHA-256 unless told otherwise); an elliptic-curve key signs ECDSA.

What gets covered: every part except the signature area itself — and except `docProps` when
`CoverDocumentProperties` is turned off, which is the choice Word itself makes so that
metadata edits do not read as tampering; here covering everything is the default because
covering less should be a decision. The package relationships are covered through the
relationships transform of 10.6, selecting each relationship that does not point at a
signature — which is what lets a second `SignAsync` add a co-signature without breaking the
first. The signature carries the two objects Word expects, the `SignatureTime` of 10.5.11 and
the `SignatureInfoV1` with the signer's comment, so Word shows the signature the way it shows
its own. There is no XAdES object: what is written is the plain XML-Signature profile of
clause 10, which Word verifies and displays; qualifying properties beyond that are a policy
layer this library does not fabricate.

The signer and the verifier are two independent implementations of the same clauses, and the
tests hold them against each other in both directions: what `DocumentSigner` writes must come
back `Verified` with every part `PartsUnchanged`, must break the moment a covered byte
changes, and is put in front of Word itself in the opt-in oracle tests.

Where this sits against the other formats is in [conformance.md](conformance.md).
