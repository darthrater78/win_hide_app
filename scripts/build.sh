#!/usr/bin/env bash
# Local dev workflow: restore (with NuGet vulnerability audit), build, test, publish.
# Runs on Linux or Windows (Git Bash). The published exe itself only runs on Windows.
#
#   scripts/build.sh           build + test
#   scripts/build.sh publish   build + test + dist/ShareHider-<version>-win-x64.exe
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

dotnet restore ShareHider.slnx --locked-mode
dotnet build ShareHider.slnx --no-restore -c Release
dotnet test --solution ShareHider.slnx --no-build -c Release

if [[ "${1:-}" == "publish" ]]; then
  version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"
  rm -rf dist/publish
  dotnet publish src/ShareHider/ShareHider.csproj -c Release -o dist/publish
  cp dist/publish/ShareHider.exe "dist/ShareHider-${version}-win-x64.exe"
  (cd dist && sha256sum "ShareHider-${version}-win-x64.exe" > "ShareHider-${version}-win-x64.exe.sha256")
  echo "Published dist/ShareHider-${version}-win-x64.exe"
fi
