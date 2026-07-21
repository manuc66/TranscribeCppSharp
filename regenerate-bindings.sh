#!/bin/bash
set -e

VERSION=$(cat TRANSCRIBE_VERSION)
TAG="v${VERSION}"
REPO="https://github.com/handy-computer/transcribe.cpp"
TMPDIR=$(mktemp -d)

echo "Regenerating bindings from ${TAG}..."

git clone --depth 1 --branch "${TAG}" "${REPO}" "${TMPDIR}"
dotnet run --project src/Generator -- "${TMPDIR}/bindings/rust/sys/src/transcribe_sys.rs"

mkdir -p rust
cp "${TMPDIR}/bindings/rust/sys/src/transcribe_sys.rs" rust/transcribe_sys.rs

rm -rf "${TMPDIR}"
echo "Done. Bindings match ${TAG}."
