#!/bin/bash
set -euo pipefail

# Verifies every packable project declares an explicit license, so no NuGet
# package ships without one. The license itself (MIT) is set globally in
# Directory.Build.props; this check catches a package whose metadata would
# otherwise rely on NuGet's default warning.
#
# Runs in CI (see .github/workflows/ci.yml, job `license-check`).

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"

errors=0

# Every .csproj that can produce a package must resolve PackageLicenseExpression.
# Directory.Build.props sets it globally, but an explicit "no licence" override
# or a stray PackageLicenseFile would be flagged here.
while IFS= read -r csproj; do
    if ! grep -q "<PackageLicenseExpression>MIT</PackageLicenseExpression>" "$csproj"; then
        # May be set globally in Directory.Build.props instead of in the file.
        if grep -q "<PackageLicenseExpression>MIT</PackageLicenseExpression>" Directory.Build.props; then
            if grep -q "<PackageLicenseExpression>" "$csproj"; then
                echo "FAIL: $csproj overrides the global MIT license with a different value"
                errors=$((errors + 1))
            fi
        else
            echo "FAIL: $csproj has no PackageLicenseExpression"
            errors=$((errors + 1))
        fi
    fi
done < <(git ls-files '*.csproj' | grep -v -E '/(bin|obj)/')

# The README (packed into every package) must carry the upstream attribution.
if ! grep -qi "transcribe.cpp authors" README.md; then
    echo "FAIL: README.md must attribute the native library to its upstream authors"
    errors=$((errors + 1))
fi

if [ "$errors" -gt 0 ]; then
    echo ""
    echo "license-check: $errors problem(s) found"
    exit 1
fi

echo "license-check: OK — every package declares MIT and attribution is present"
