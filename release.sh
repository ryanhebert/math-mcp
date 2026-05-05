#!/usr/bin/env bash
# Build, package, and publish a new MathMcp release to GitHub.
#
# Reads <Version> from src/MathMcp/MathMcp.csproj, publishes win-x64 self-contained,
# tags as v<version>, and uploads MathMcp.exe as a release asset.
#
# Usage:
#   ./release.sh              build and publish using the version in MathMcp.csproj
#   ./release.sh --dry-run    do everything except create the GitHub release
#
# Prereqs: dotnet 8 SDK on PATH; gh authenticated; clean working tree.

set -euo pipefail

DRY_RUN=0
if [[ "${1:-}" == "--dry-run" ]]; then DRY_RUN=1; fi

ROOT="$(cd "$(dirname "$0")" && pwd)"
CSPROJ="$ROOT/src/MathMcp/MathMcp.csproj"
PUBLISH_DIR="$ROOT/src/MathMcp/bin/Release/net8.0/win-x64/publish"
ARTIFACT="$ROOT/MathMcp.exe"

VERSION="$(grep -oP '(?<=<Version>)[^<]+' "$CSPROJ" | head -1)"
if [[ -z "$VERSION" ]]; then
    echo "error: could not read <Version> from $CSPROJ" >&2
    exit 1
fi
TAG="v$VERSION"

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

echo "==> dotnet publish"
dotnet publish "$CSPROJ" -c Release -r win-x64 --self-contained \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    --nologo --verbosity quiet

cp -f "$PUBLISH_DIR/MathMcp.exe" "$ARTIFACT"
SIZE_MB="$(du -m "$ARTIFACT" | cut -f1)"
echo "    built $ARTIFACT ($SIZE_MB MB)"

NOTES="$(cat <<EOF
## Math MCP Server $TAG

Self-contained Windows installer for the Math MCP Server. Download \`MathMcp.exe\` below, double-click to install (UAC prompts automatically). The service registers itself, opens firewall ports, and starts listening on 52080 (HTTP) / 52443 (HTTPS).

Visit \`http://<host>:52080/\` for a status dashboard, or point an MCP client at \`http://<host>:52080/mcp\`.

To uninstall: \`MathMcp.exe uninstall\`.

See [README](../README.md) for full details.
EOF
)"

if [[ "$DRY_RUN" -eq 1 ]]; then
    echo "==> dry run: would create release $TAG with asset $ARTIFACT"
    echo "--- release notes ---"
    echo "$NOTES"
    exit 0
fi

echo "==> gh release create $TAG"
gh release create "$TAG" "$ARTIFACT" \
    --title "$TAG" \
    --notes "$NOTES"

echo "==> done"
gh release view "$TAG" --json url --jq '.url'
