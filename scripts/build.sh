#!/usr/bin/env bash
# Local dev workflow, also what CI runs. Works on Linux or Windows (Git Bash);
# the published exe itself only runs on Windows.
#
#   scripts/build.sh                 locked restore (with NuGet vulnerability audit), build, test
#   scripts/build.sh publish         ...then dist/ShareHider-<version>-win-x64.exe + .sha256
#   scripts/build.sh package [ver]   locked restore + publish only, no tests. For the release
#                                    workflow, whose gate job already required a passing CI run
#                                    on the same commit. [ver] overrides the version, e.g. 0.1.0-dev.1.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

mode="${1:-build}"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"
if [[ -n "${2:-}" ]]; then
  version="$2"
fi

# Only a plain semver (with optional pre-release suffix) may reach file names and MSBuild.
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$ ]]; then
  echo "error: '$version' is not a valid version" >&2
  exit 1
fi

publish() {
  local exe="ShareHider-${version}-win-x64.exe"
  rm -rf dist/publish
  dotnet publish src/ShareHider/ShareHider.csproj -c Release --no-restore -p:Version="$version" -o dist/publish
  cp dist/publish/ShareHider.exe "dist/$exe"
  (cd dist && sha256sum "$exe" > "$exe.sha256")
  echo "Published dist/$exe"
}

case "$mode" in
  build | publish)
    dotnet restore ShareHider.slnx --locked-mode
    dotnet build ShareHider.slnx --no-restore -c Release
    dotnet test --solution ShareHider.slnx --no-build -c Release
    if [[ "$mode" == publish ]]; then
      publish
    fi
    ;;
  package)
    dotnet restore src/ShareHider/ShareHider.csproj --locked-mode
    publish
    ;;
  *)
    echo "usage: scripts/build.sh [build | publish | package [version]]" >&2
    exit 2
    ;;
esac
