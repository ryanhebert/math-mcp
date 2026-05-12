#!/usr/bin/env bash
# Build, package, and publish a new MathMcp release to GitHub.
#
# Reads <Version> from src/MathMcp/MathMcp.csproj, publishes win-x64 self-contained,
# tags as v<version>, uploads two assets:
#   - MathMcp-v<version>.exe  (versioned, stable per-release URL)
#   - MathMcp.exe             (unversioned, lets `releases/latest/download/MathMcp.exe` resolve)
# and pulls the release notes from the matching section of CHANGELOG.md.
#
# Usage:
#   ./release.sh              build and publish using the version in MathMcp.csproj
#   ./release.sh --dry-run    do everything except create the GitHub release
#
# Prereqs: dotnet 8 SDK on PATH; gh authenticated; clean working tree;
#          CHANGELOG.md has an entry "## [v<version>]" for this release.

set -euo pipefail

DRY_RUN=0
if [[ "${1:-}" == "--dry-run" ]]; then DRY_RUN=1; fi

ROOT="$(cd "$(dirname "$0")" && pwd)"
CSPROJ="$ROOT/src/MathMcp/MathMcp.csproj"
PUBLISH_DIR="$ROOT/src/MathMcp/bin/Release/net8.0/win-x64/publish"
CHANGELOG="$ROOT/CHANGELOG.md"

VERSION="$(grep -oP '(?<=<Version>)[^<]+' "$CSPROJ" | head -1)"
if [[ -z "$VERSION" ]]; then
    echo "error: could not read <Version> from $CSPROJ" >&2
    exit 1
fi
TAG="v$VERSION"
ARTIFACT="$ROOT/MathMcp-$TAG.exe"
ARTIFACT_LATEST="$ROOT/MathMcp.exe"

echo "==> Math MCP Server release $TAG"

if [[ -n "$(git status --porcelain)" ]]; then
    echo "error: working tree has uncommitted changes; commit or stash first" >&2
    git status --short >&2
    exit 1
fi

if git rev-parse "$TAG" >/dev/null 2>&1; then
    echo "error: tag $TAG already exists locally" >&2
    exit 1
fi
if gh release view "$TAG" >/dev/null 2>&1; then
    echo "error: release $TAG already exists on GitHub" >&2
    exit 1
fi

# Extract this version's section from CHANGELOG.md.
# Matches from "## [vX.Y.Z]" up to (but not including) the next "## [" heading.
NOTES="$(awk -v tag="$TAG" '
    BEGIN { found = 0 }
    /^## \[/ {
        if (found) exit
        if (index($0, "[" tag "]") > 0) { found = 1; next }
    }
    found { print }
' "$CHANGELOG")"

if [[ -z "$NOTES" ]]; then
    echo "error: no '## [$TAG]' section found in $CHANGELOG" >&2
    echo "       add a changelog entry before releasing" >&2
    exit 1
fi

NOTES="$(cat <<EOF
$NOTES
---

**Download:** [\`MathMcp-$TAG.exe\`](https://github.com/ryanhebert/math-mcp/releases/download/$TAG/MathMcp-$TAG.exe)
(also available as [\`MathMcp.exe\`](https://github.com/ryanhebert/math-mcp/releases/download/$TAG/MathMcp.exe) — same file, unversioned name for stable URLs).

Install: double-click; UAC prompts automatically. See [README](https://github.com/ryanhebert/math-mcp#install) for upgrade instructions when auth is enabled.
EOF
)"

echo "==> dotnet publish"
dotnet publish "$CSPROJ" -c Release -r win-x64 --self-contained \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    --nologo --verbosity quiet

cp -f "$PUBLISH_DIR/MathMcp.exe" "$ARTIFACT"
cp -f "$PUBLISH_DIR/MathMcp.exe" "$ARTIFACT_LATEST"
SIZE_MB="$(du -m "$ARTIFACT" | cut -f1)"
echo "    built $ARTIFACT and $ARTIFACT_LATEST ($SIZE_MB MB each)"

if [[ "$DRY_RUN" -eq 1 ]]; then
    echo "==> dry run: would create release $TAG with assets:"
    echo "             $ARTIFACT"
    echo "             $ARTIFACT_LATEST"
    echo "--- release notes ---"
    echo "$NOTES"
    exit 0
fi

echo "==> gh release create $TAG"
gh release create "$TAG" "$ARTIFACT" "$ARTIFACT_LATEST" \
    --title "$TAG" \
    --notes "$NOTES"

echo "==> done"
gh release view "$TAG" --json url --jq '.url'
