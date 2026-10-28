#!/bin/bash
set -e

VERSION=$(cat build/TRANSCRIBE_VERSION)
TAG="v${VERSION}"
REPO="https://github.com/handy-computer/transcribe.cpp"
PINNED_COMMIT_FILE="build/TRANSCRIBE_COMMIT"
TMPDIR=$(mktemp -d)

echo "Regenerating bindings from ${TAG}..."

git clone --depth 1 --branch "${TAG}" "${REPO}" "${TMPDIR}"

# A tag can be moved; a commit cannot. The commit of this tag is recorded so the
# bindings and the vendored headers are known to come from the tree that was
# reviewed, not from whatever the tag points at today.
if [[ ! -f "${PINNED_COMMIT_FILE}" ]]; then
    echo "Error: ${PINNED_COMMIT_FILE} is missing; it must hold the commit ${TAG} resolves to." >&2
    rm -rf "${TMPDIR}"
    exit 1
fi

expected_commit="$(tr -d '[:space:]' < "${PINNED_COMMIT_FILE}")"
actual_commit="$(git -C "${TMPDIR}" rev-parse HEAD)"
if [[ "${actual_commit}" != "${expected_commit}" ]]; then
    echo "Error: ${TAG} points at ${actual_commit}, not at the pinned ${expected_commit}." >&2
    echo "The tag moved upstream. Read the upstream diff, then update ${PINNED_COMMIT_FILE} deliberately." >&2
    rm -rf "${TMPDIR}"
    exit 1
fi

FFI_PATH="${TMPDIR}/bindings/rust/sys/src/transcribe_sys.rs"
if [[ ! -f "${FFI_PATH}" ]]; then
    echo "Error: FFI source not found at ${FFI_PATH}" >&2
    echo "Upstream repo structure may have changed." >&2
    rm -rf "${TMPDIR}"
    exit 1
fi

HEADER_ROOT="${TMPDIR}/include/transcribe.h"
if [[ ! -f "${HEADER_ROOT}" ]]; then
    echo "Error: C header not found at ${HEADER_ROOT}" >&2
    echo "Upstream repo structure may have changed." >&2
    rm -rf "${TMPDIR}"
    exit 1
fi

mkdir -p ffi/rust ffi/c
cp "${FFI_PATH}" ffi/rust/transcribe_sys.rs
cp "${HEADER_ROOT}" ffi/c/transcribe.h

# Upstream splits the public C surface across include/transcribe/*.h
# (extensions, sortformer, moonshine_streaming, voxtral_realtime, and in 0.4
# vad, langid, diarize). The root header does NOT include them, so they must be
# vendored and parsed too; otherwise those functions are generated without doc
# comments. Keep upstream's relative layout under ffi/c/transcribe/.
rm -rf ffi/c/transcribe
if [[ -d "${TMPDIR}/include/transcribe" ]]; then
    mkdir -p ffi/c/transcribe
    cp "${TMPDIR}/include/transcribe/"*.h ffi/c/transcribe/
fi

dotnet run --project src/Generator -- "${FFI_PATH}" "ffi/c"

rm -rf "${TMPDIR}"
echo "Done. Bindings match ${TAG}."
