#!/usr/bin/env bash
set -euo pipefail

REPO="${EPUBSHARP_REPO:-L34T/EpubSharp_Elib2Ebook}"
TAG="${EPUBSHARP_TAG:-}"
OUTDIR="${EPUBSHARP_OUTDIR:-Core/External}"
FORCE="${EPUBSHARP_FORCE:-0}"

api() {
  local url="$1"
  curl -fsSL \
    -H "Accept: application/vnd.github+json" \
    -H "User-Agent: Elib2Ebook-fetch-epubsharp" \
    "$url"
}

if [[ -z "${TAG}" ]]; then
  release_json="$(api "https://api.github.com/repos/${REPO}/releases/latest")"
else
  release_json="$(api "https://api.github.com/repos/${REPO}/releases/tags/${TAG}")"
fi

resolved_tag="$(
  python3 -c 'import json,sys; obj=json.load(sys.stdin); print(obj.get("tag_name",""))' <<<"${release_json}"
)"

if [[ -z "${resolved_tag}" ]]; then
  echo "Failed to resolve release tag for repo '${REPO}'." >&2
  exit 1
fi

echo "Using release tag: ${resolved_tag}"
mkdir -p "${OUTDIR}"

download_url_for() {
  local asset_name="$1"
  python3 -c '
import json,sys
name=sys.argv[1]
obj=json.load(sys.stdin)
for a in obj.get("assets",[]):
  if a.get("name")==name:
    print(a.get("browser_download_url",""))
    sys.exit(0)
sys.exit(1)
' "${asset_name}" <<<"${release_json}"
}

download() {
  local url="$1"
  local dest="$2"
  if [[ "${FORCE}" != "1" && -s "${dest}" ]]; then
    echo "Skip (exists): ${dest}"
    return 0
  fi
  echo "Download: ${dest}"
  curl -fsSL -o "${dest}" "${url}"
}

for suffix in dll pdb deps.json; do
  asset_name="EpubSharp-net10.${suffix}"
  url="$(download_url_for "${asset_name}")"
  if [[ -z "${url}" ]]; then
    echo "Missing asset '${asset_name}' in release '${resolved_tag}' for repo '${REPO}'." >&2
    exit 1
  fi
  download "${url}" "${OUTDIR}/EpubSharp.${suffix}"
done

echo "Done."
