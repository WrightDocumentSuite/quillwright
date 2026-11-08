#!/usr/bin/env bash

set -euo pipefail

if [[ "$(uname -s)" != "Linux" ]]; then
    echo "The CI font bootstrap is supported only on Linux." >&2
    exit 1
fi

if ! command -v apt-get >/dev/null 2>&1; then
    echo "The CI font bootstrap requires an apt-based image such as Ubuntu 24.04." >&2
    exit 1
fi

run_as_root() {
    if [[ "${EUID}" -eq 0 ]]; then
        "$@"
        return
    fi

    if ! command -v sudo >/dev/null 2>&1; then
        echo "Installing the CI fonts requires root or sudo." >&2
        exit 1
    fi

    sudo "$@"
}

# Carlito and Caladea preserve the metrics of the Office defaults. DejaVu is the first installed
# Arial substitute in this deliberately small inventory and carries the Arabic presentation forms
# exercised by the RTL suite. Do not add Liberation ahead of it without re-running those tests:
# Noble's Liberation Sans lacks those glyphs, and FontMap intentionally has no per-glyph fallback.
packages=(
    fonts-crosextra-caladea
    fonts-crosextra-carlito
    fonts-dejavu-core
)

run_as_root env DEBIAN_FRONTEND=noninteractive apt-get update
run_as_root env DEBIAN_FRONTEND=noninteractive apt-get install --yes --no-install-recommends "${packages[@]}"

required_font_files=(
    /usr/share/fonts/truetype/crosextra/Caladea-Regular.ttf
    /usr/share/fonts/truetype/crosextra/Carlito-Regular.ttf
    /usr/share/fonts/truetype/dejavu/DejaVuSans.ttf
    /usr/share/fonts/truetype/dejavu/DejaVuSerif.ttf
)

for font_file in "${required_font_files[@]}"; do
    if [[ ! -f "${font_file}" ]]; then
        echo "The required CI font file '${font_file}' was not installed." >&2
        exit 1
    fi
done

echo "Installed deterministic PDF test fonts:"
dpkg-query --show --showformat='${Package}=${Version}\n' "${packages[@]}"
