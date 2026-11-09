#!/usr/bin/env python3
"""Fetch the pinned public workbook corpus and verify it before tests use it."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import sys
import tarfile
import tempfile
import time
import urllib.error
import urllib.request


def filesystem_path(path: Path) -> Path:
    """Use extended Windows paths without requiring a machine-wide LongPathsEnabled change."""
    resolved = path.resolve()
    if os.name != "nt":
        return resolved
    value = str(resolved)
    if value.startswith("\\\\?\\"):
        return resolved
    if value.startswith("\\\\"):
        return Path("\\\\?\\UNC\\" + value[2:])
    return Path("\\\\?\\" + value)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def download(url: str, destination: Path, expected: str) -> None:
    if destination.exists() and sha256(destination) == expected:
        print(f"Using cached {destination.name}")
        return

    destination.parent.mkdir(parents=True, exist_ok=True)
    partial = destination.with_suffix(destination.suffix + ".partial")
    if partial.exists():
        partial.unlink()

    request = urllib.request.Request(url, headers={"User-Agent": "Quillwright-corpus-fetch/1"})
    digest = hashlib.sha256()
    try:
        with urllib.request.urlopen(request) as response, partial.open("wb") as output:
            while chunk := response.read(1024 * 1024):
                output.write(chunk)
                digest.update(chunk)

        actual = digest.hexdigest()
        if actual != expected:
            raise RuntimeError(
                f"SHA-256 mismatch for {url}: expected {expected}, received {actual}"
            )
        os.replace(partial, destination)
    finally:
        if partial.exists():
            partial.unlink()


def replace_with_retry(source: Path, destination: Path) -> None:
    """Windows refuses to rename a directory while a scanner still has fresh files open."""
    for attempt in range(10):
        try:
            os.replace(source, destination)
            return
        except PermissionError:
            if attempt == 9:
                raise
            time.sleep(2)


def extract_archive(archive: Path, destination: Path, staging_root: Path) -> None:
    if destination.exists():
        raise RuntimeError(
            f"Corpus destination already exists: {destination}. Use a clean root so stale files cannot mix with the pinned corpus."
        )

    staged = staging_root / destination.name
    staged.mkdir()
    with tarfile.open(archive, "r:gz") as bundle:
        members = bundle.getmembers()
        roots = {
            PurePosixPath(member.name).parts[0]
            for member in members
            if PurePosixPath(member.name).parts
        }
        if len(roots) != 1:
            raise RuntimeError(f"{archive} does not contain exactly one top-level directory")
        archive_root = next(iter(roots))

        for member in members:
            source = PurePosixPath(member.name)
            if not source.parts or source.parts[0] != archive_root:
                raise RuntimeError(f"Unexpected archive member: {member.name}")
            relative = PurePosixPath(*source.parts[1:])
            if not relative.parts:
                continue
            if relative.is_absolute() or ".." in relative.parts:
                raise RuntimeError(f"Unsafe archive member: {member.name}")
            if member.issym() or member.islnk() or member.isdev():
                raise RuntimeError(f"Unsupported archive member: {member.name}")

            output = staged.joinpath(*relative.parts)
            if member.isdir():
                output.mkdir(parents=True, exist_ok=True)
                continue
            if not member.isfile():
                continue

            output.parent.mkdir(parents=True, exist_ok=True)
            source_stream = bundle.extractfile(member)
            if source_stream is None:
                raise RuntimeError(f"Could not read archive member: {member.name}")
            with source_stream, output.open("wb") as target_stream:
                shutil.copyfileobj(source_stream, target_stream)

    replace_with_retry(staged, destination)


def corpus_files(root: Path, destinations: list[str]) -> list[Path]:
    files: list[Path] = []
    for name in destinations:
        files.extend(path for path in (root / name).rglob("*") if path.is_file())
    return files


def verify(manifest: dict, root: Path) -> list[Path]:
    destinations = [archive["destination"] for archive in manifest["archives"]]
    missing_destinations = [name for name in destinations if not (root / name).is_dir()]
    if missing_destinations:
        raise RuntimeError(f"Missing corpus directories: {', '.join(missing_destinations)}")

    files = corpus_files(root, destinations)
    lower_names = {path.name.lower() for path in files}
    missing_files = [name for name in manifest["requiredFiles"] if name.lower() not in lower_names]
    if missing_files:
        raise RuntimeError(f"Required corpus files are missing: {', '.join(missing_files)}")

    for pattern, minimum in manifest["minimumFiles"].items():
        suffix = pattern.removeprefix("*").lower()
        count = sum(path.name.lower().endswith(suffix) for path in files)
        if count < minimum:
            raise RuntimeError(f"Corpus has {count} {pattern} files; expected at least {minimum}")
        print(f"Verified {count} {pattern} files (minimum {minimum})")

    print(f"Verified {len(files)} files across {len(destinations)} pinned upstream archives")
    return files


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--cache", type=Path, required=True)
    parser.add_argument("--verify-only", action="store_true")
    args = parser.parse_args()

    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    if manifest.get("schemaVersion") != 1:
        raise RuntimeError("Unsupported corpus manifest schema")

    root = filesystem_path(args.root)
    cache = filesystem_path(args.cache)
    root.mkdir(parents=True, exist_ok=True)
    cache.mkdir(parents=True, exist_ok=True)

    if not args.verify_only:
        staging = Path(tempfile.mkdtemp(prefix=".quillwright-corpus-", dir=root))
        try:
            for archive in manifest["archives"]:
                archive_path = cache / f"{archive['sha256']}.tar.gz"
                print(f"Fetching {archive['name']} at {archive['revision']}")
                download(archive["url"], archive_path, archive["sha256"])
                extract_archive(archive_path, root / archive["destination"], staging)
        finally:
            if staging.parent != root or not staging.name.startswith(".quillwright-corpus-"):
                raise RuntimeError(f"Unexpected corpus staging directory: {staging}")
            shutil.rmtree(staging, ignore_errors=True)

    verify(manifest, root)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, RuntimeError, tarfile.TarError, urllib.error.URLError) as error:
        print(f"error: {error}", file=sys.stderr)
        raise SystemExit(1)
