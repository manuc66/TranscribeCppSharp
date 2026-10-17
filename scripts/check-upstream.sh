#!/bin/bash
#
# Reports whether the pinned upstream transcribe.cpp version is behind the
# latest stable release published by the transcribe.cpp authors.
#
# The pinned version is build/TRANSCRIBE_VERSION, the single source of truth
# this project binds to. This script only reads it and the GitHub release API;
# it changes nothing.
#
# Output (stdout), one key=value per line:
#   pinned=<version>
#   latest=<version>
#   status=up-to-date | outdated | ahead | unknown
#
# Exit code:
#   0  up-to-date or ahead
#   1  outdated (a newer stable release exists)
#   2  could not determine (network/API error)
#
# Run it locally with:  ./scripts/check-upstream.sh
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UPSTREAM="handy-computer/transcribe.cpp"

if [[ ! -f "${REPO_ROOT}/build/TRANSCRIBE_VERSION" ]]; then
    echo "error: build/TRANSCRIBE_VERSION not found" >&2
    exit 2
fi

pinned="$(tr -d '[:space:]' < "${REPO_ROOT}/build/TRANSCRIBE_VERSION")"
pinned="${pinned#v}"

echo "pinned=${pinned}"

# Latest stable release: the GitHub API excludes prereleases and drafts, which
# is what we want to track. Not built from main on purpose -- see
# catalog/README.md ("Regenerating").
json=""
if command -v gh >/dev/null 2>&1; then
    json="$(gh api "repos/${UPSTREAM}/releases/latest" 2>/dev/null || true)"
fi
if [[ -z "${json}" ]]; then
    json="$(curl -fsSL --proto '=https' --proto-redir '=https' \
        -H 'Accept: application/vnd.github+json' \
        "https://api.github.com/repos/${UPSTREAM}/releases/latest" 2>/dev/null || true)"
fi

if [[ -z "${json}" ]]; then
    echo "latest="
    echo "status=unknown"
    echo "Could not reach the GitHub releases API for ${UPSTREAM}." >&2
    exit 2
fi

latest="$(printf '%s' "${json}" | sed -n 's/.*"tag_name"[[:space:]]*:[[:space:]]*"v\{0,1\}\([^"]*\)".*/\1/p' | head -n1)"
if [[ -z "${latest}" ]]; then
    echo "latest="
    echo "status=unknown"
    echo "Could not read tag_name from the releases API response." >&2
    exit 2
fi

echo "latest=${latest}"

if [[ "${pinned}" == "${latest}" ]]; then
    echo "status=up-to-date"
    exit 0
fi

# sort -V puts the higher version last.
highest="$(printf '%s\n%s\n' "${pinned}" "${latest}" | sort -V | tail -n1)"
if [[ "${highest}" == "${latest}" ]]; then
    echo "status=outdated"
    exit 1
fi

echo "status=ahead"
exit 0
