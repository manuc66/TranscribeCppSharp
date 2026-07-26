#!/bin/bash
set -e

VERSION=$(cat TRANSCRIBE_VERSION)
TAG="v${VERSION}"
REPO="https://github.com/handy-computer/transcribe.cpp"
TMPDIR=$(mktemp -d)

echo "Regenerating bindings from ${TAG}..."

git clone --depth 1 --branch "${TAG}" "${REPO}" "${TMPDIR}"

FFI_PATH="${TMPDIR}/bindings/rust/sys/src/transcribe_sys.rs"
if [ ! -f "${FFI_PATH}" ]; then
    echo "Error: FFI source not found at ${FFI_PATH}"
    echo "Upstream repo structure may have changed."
    rm -rf "${TMPDIR}"
    exit 1
fi

dotnet run --project src/Generator -- "${FFI_PATH}"

mkdir -p rust
cp "${FFI_PATH}" rust/transcribe_sys.rs

rm -rf "${TMPDIR}"
echo "Done. Bindings match ${TAG}."
