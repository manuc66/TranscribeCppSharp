#!/bin/bash
set -e

VERSION=$(cat build/TRANSCRIBE_VERSION)
TAG="v${VERSION}"
REPO="https://github.com/handy-computer/transcribe.cpp"
TMPDIR=$(mktemp -d)

echo "Regenerating bindings from ${TAG}..."

git clone --depth 1 --branch "${TAG}" "${REPO}" "${TMPDIR}"

FFI_PATH="${TMPDIR}/bindings/rust/sys/src/transcribe_sys.rs"
if [[ ! -f "${FFI_PATH}" ]]; then
    echo "Error: FFI source not found at ${FFI_PATH}" >&2
    echo "Upstream repo structure may have changed." >&2
    rm -rf "${TMPDIR}"
    exit 1
fi

HEADER_PATH="${TMPDIR}/include/transcribe.h"
if [[ ! -f "${HEADER_PATH}" ]]; then
    echo "Error: C header not found at ${HEADER_PATH}" >&2
    echo "Upstream repo structure may have changed." >&2
    rm -rf "${TMPDIR}"
    exit 1
fi

dotnet run --project src/Generator -- "${FFI_PATH}" "${HEADER_PATH}"

mkdir -p rust
cp "${FFI_PATH}" ffi/rust/transcribe_sys.rs
cp "${HEADER_PATH}" ffi/c/transcribe.h

rm -rf "${TMPDIR}"
echo "Done. Bindings match ${TAG}."
