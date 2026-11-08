# Releasing packages

Quillwright releases are built only from the standalone public repository. A package built
from the private `WordLib` monorepo would name a private commit in its nuspec and map its PDBs
to the wrong Source Link repository, even when `RepositoryUrl` looks public. The release guard
therefore rejects a checkout whose Git root is not this directory, whose `origin` is not
`WrightDocumentSuite/quillwright`, whose repository and commit are not anonymously readable,
whose tracked/untracked/ignored inputs are dirty, or whose release inputs are not LF. The
guard downloads `Directory.Build.props` from the canonical public raw URL without credentials
and compares its SHA-256 with the raw Git blob, so credentials for a private fork cannot make a
hidden commit eligible.

The five package IDs and their exact dependency graph live in
[`eng/release-manifest.json`](../eng/release-manifest.json). The manifest also names the stable
release used by package validation. It must always be the latest version already
published for all five IDs. The current baseline is 0.2.4. While building release N, keep the
baseline at the published N-1; after N is published, update `main` so the baseline becomes N and
new APIs from that release cannot be removed unnoticed. Do not weaken an API compatibility
diagnostic just to make the gate green: either restore compatibility or commit a reviewed
compatibility suppression that explains the intentional break.

## What the gate proves

[`eng/release.ps1`](../eng/release.ps1) performs the complete candidate build:

1. Verifies the clean, LF-only, public Git snapshot and, for publication, the public `vX.Y.Z`
   tag.
2. Restores all five baseline packages with `PackageDownload` into the selected isolated NuGet
   cache. Package validation no longer depends on a developer's pre-populated global cache.
3. Restores, verifies formatting, performs a clean Release rebuild, and runs the tests.
4. Packs all five projects with package validation and its API compatibility checks enabled.
5. Checks the exact package set and archive-entry allowlist, IDs, dependency versions,
   DLL/XML/README and source-generator payload, repository commit, and canonical repository
   URLs.
6. Binds each main DLL's CodeView identity to its portable PDB in the `.snupkg`, requires the
   canonical Source Link mapping, and compares each tracked document checksum with raw blob
   bytes returned by `git cat-file --batch`. This bypasses checkout/archive EOL conversion and
   catches the CRLF/LF mismatch that an ordinary successful pack does not detect.
7. Restores the candidate packages into an empty consumer cache, verifies the restored nupkg
   hashes, exercises DOC/RTF/templates, and publishes and runs the consumer with Native AOT.
8. Writes and re-verifies `SHA256SUMS` beside the five `.nupkg` and five `.snupkg` files.

CI performs clean Release builds on Ubuntu 24.04 and Windows 2025. Windows runs the complete test
suite and the coverage ratchet against the Microsoft fonts those layout assertions were authored
for. Ubuntu runs every test except the small set marked `RequiresMicrosoftFonts`; those tests
assert exact Office-font glyph availability or pagination and are not weakened to accept different
open-font geometry. The Linux package gate still renders a real PDF and explicitly verifies the
expected Calibri-to-Carlito substitution before running the Native AOT executable. Separate
required jobs enforce formatting and the full package/API compatibility/Source Link/AOT gate. The
SDK is fixed at 10.0.403 with roll-forward disabled; workflows install it from `global.json`.

PDF pagination and glyph assertions depend on font metrics, so a bare Linux SDK image is not a
valid test environment. Before every Ubuntu job that runs PDF tests or the packed PDF smoke, CI
runs [`eng/install-linux-fonts.sh`](../eng/install-linux-fonts.sh). It installs and verifies the
same deliberately small Noble font inventory on every runner: metric-compatible Carlito/Caladea
and DejaVu. Quillwright indexes font files itself by their internal family names rather than asking
Fontconfig to choose a face, and there is no per-glyph fallback inside a selected face. Liberation
Sans is therefore intentionally absent: it would win the Arial substitution before DejaVu Sans
but lacks the Arabic presentation forms exercised by the RTL suite. Installing unrelated Noto or
STIX faces would not repair a run after a different face has already been selected. Tests that
truly depend on Microsoft glyphs or metrics remain an explicit Windows gate rather than a Linux
exclusion hidden in the test code. Package versions follow Ubuntu 24.04 servicing updates and are
logged by the script; this is environment normalization, not a claim of byte-identical fonts
forever.

These workflows are activated only after this tree is exported as the root of the standalone
public repository. GitHub does not discover a nested `Quillwright/.github/workflows` directory
inside the private monorepo; private pre-export checks must be configured at that monorepo's
root.

The gate makes the build process and provenance repeatable, but it does not promise a
byte-identical rebuild indefinitely. `global.json` fixes the SDK servicing patch and
committed lock files constrain dependency restoration, while external infrastructure can change. `SHA256SUMS` instead freezes the
exact candidate bytes that were reviewed and passed to the protected publish job.

## Prepare a release

1. Update `Version` in `Directory.Build.props` and the changelog. Confirm that
   `baselineVersion` in `eng/release-manifest.json` is the latest version currently published;
   do not set it to the candidate version before that version exists on NuGet.org. Change
   dependency entries in the manifest only when the package projects deliberately changed them.
2. Let all required CI jobs pass in the private development repository, then export the exact
   `Quillwright/` tree to the standalone public repository. Review the public diff and commit it
   there. The release artifact must never be copied from a monorepo pack.
3. When introducing `.gitattributes` to the existing public history, normalize the index once
   in the standalone repository before committing:

   ```bash
   git add .gitattributes
   git add --renormalize .
   git diff --cached --check
   git ls-files --eol
   ```

   The command is mandatory as a migration check because adding attributes controls future
   checkouts but does not by itself normalize an existing non-LF blob. Review any staged
   normalization before committing it with `.gitattributes`. At the audited 0.2.4 public commit,
   the line-oriented source/build/configuration/documentation blobs are already LF, so the
   command should not invent a repository-wide content rewrite. The one exceptional input is
   `src/Quillwright/Editing/DocumentComparer.cs`: literal NUL characters made Git classify its
   old blob as binary, and the release migration replaces them with equivalent `\0` escapes so
   the staged file becomes `i/lf`. An old Windows working tree can nevertheless remain CRLF or
   mixed after the attribute is added. Do the release build from a new clone; do not try to
   repair a permanently mixed pre-migration checkout in place.
4. Wait for all required checks on the public commit. From a fresh Windows clone, an optional
   full local rehearsal is:

   ```powershell
   pwsh ./eng/release.ps1 -ExpectedVersion 0.2.5
   ```

   This requires PowerShell 7, the SDK selected by `global.json`, Git, network access to
   NuGet.org and GitHub, and the platform's Native AOT toolchain. It writes only beneath
   `artifacts/release` and `artifacts/package-smoke` by default. The package output and
   release NuGet cache must be empty so stale bytes cannot satisfy a gate. The initial guard
   also rejects every ignored file: `*.csproj.user` and default-Compile files under `src`, for
   example, can alter MSBuild output without appearing in ordinary `git status`. Use
   `git clean -ndX` to inspect ignored paths, then release from a new clone. The release workflow
   performs the Windows full-test/coverage job in an isolated workspace, then checks the Linux
   candidate workspace before invoking any repository bootstrap and repeats the strict guard in
   `release.ps1`. Ignored `.user` files and ignored paths remain fatal.

   On Ubuntu 24.04, after the same commit has passed the Windows quality gate, install the CI
   fonts and rehearse the package/Linux smoke stages without rerunning Microsoft-font tests:

   ```bash
   bash ./eng/install-linux-fonts.sh
   pwsh ./eng/release.ps1 -ExpectedVersion 0.2.5 -SkipTests
   ```

   The script uses `sudo` when the current user is not root. On another distribution, provision
   equivalent font families explicitly rather than accepting host-dependent PDF geometry. To
   reproduce the Linux-compatible source-test job itself, run `dotnet test` with
   `--filter "RequiresMicrosoftFonts!=true"`; never treat that filtered run as a replacement for
   the Windows full suite and coverage ratchet.

   The package smoke derives the host RID, including `linux-musl-*` on Alpine or another host
   with a musl loader. Pass `-RuntimeIdentifier` to `eng/test-packages.ps1` explicitly for a
   nonstandard host or an intentional cross-publish.
5. Create the immutable public tag at that exact commit and push it:

   ```bash
   git tag -a v0.2.5 -m "Quillwright 0.2.5"
   git push origin v0.2.5
   ```

   Do not move a published release tag.
6. In the GitHub **Release** workflow, select the `v0.2.5` ref, enter `0.2.5`, and first run with
   **publish** disabled for a publication rehearsal. That run's artifact is not reused by a
   later workflow run.

## Publish from the protected environment

Create a GitHub environment named `nuget.org`, require reviewer approval, restrict deployment
to protected release tags, and store `NUGET_API_KEY` as an environment secret. The key should
be a NuGet.org key scoped only to pushing new versions of the five Quillwright IDs. Never put
the key in the repository, a workflow input, a command-line transcript, or an artifact.

Dispatch the same workflow from the matching `vX.Y.Z` tag with **publish** enabled. While the
publish job from that run is waiting for protected-environment approval, download and inspect
that run's candidate artifact if manual inspection is required. After approval, the protected
job downloads that same immutable candidate, verifies its checksums, reruns the
public-snapshot/package/Source Link checks, and confirms
that the version is absent for every package ID. `dotnet nuget push` then publishes each main
package together with its adjacent `.snupkg`. A branch dispatch, a mismatched input version, a
missing public tag, an existing NuGet version, or a monorepo commit cannot reach the push step.
Workflow runs for the same version are serialized. Do not approve any out-of-band publication
of that version while the protected job is waiting.

After NuGet.org finishes indexing, verify all five package pages, symbol availability, and a
fresh NuGet.org-only consumer restore. Record the public commit and tag in the changelog or
release notes. Then update `baselineVersion` on `main` to the version just published and let the
package-validation job pass; this is the post-release step that protects APIs first shipped in
that version.

## Failure and rollback

NuGet.org versions are immutable. Never retry a different set of bytes under a version that
was partially or fully published. If publication fails after one or more IDs were accepted,
unlist every accepted ID for that version, fix the cause, increment the patch version for all
five packages, rebuild from a new public commit and tag, and publish the complete set.

NuGet.org implements `dotnet nuget delete` as unlisting. With a key supplied through a local
environment variable, repeat this for each affected ID:

```powershell
dotnet nuget delete Quillwright 0.2.5 `
  --source https://api.nuget.org/v3/index.json `
  --api-key $env:NUGET_API_KEY `
  --non-interactive
```

Unlisting does not erase packages already present in consumer caches, so publish a fixed patch
and document the affected version. Keep the original public commit and tag for provenance;
revert the code on `main` separately if necessary.

## SDK servicing and lock files

This release uses exactly .NET SDK 10.0.403 (`global.json`, roll-forward disabled).
The SDK supplies implicit linker dependencies, so changing its servicing patch can
invalidate a locked restore even when the explicit package references are unchanged.
To service the SDK, update `global.json`, restore with `--force-evaluate`, review the
lock-file diff, and repeat the locked restore, source tests, and package consumer gates.
Commit these changes together; do not disable locked restore in CI.

The test projects deliberately use `xunit.v3.mtp-off` 3.2.2 with the VSTest adapter.
This retains the existing `dotnet test`, TRX, filters, and coverage commands while
removing the unused, deprecated Microsoft.Testing.Platform.MSBuild 1.9.1 dependency.
Do not switch to the MTP v2 package without migrating those commands together.

The baseline, verifier, and package-consumer helper projects write their per-run lock files under
`artifacts/release-locks/`. Their baseline/candidate versions and runtime identifiers vary by
invocation, so those helper locks are regenerated instead of becoming untracked source inputs.
Library, test, sample, benchmark, and fuzz project locks remain committed and checked by CI.
