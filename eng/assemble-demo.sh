#!/usr/bin/env bash
# Publishes samples/BlazeForms.Demo.Wasm and assembles a deployable demo directory at
# <demo-target-dir>: the publish output copied in, the build-time "/" base href rewritten to
# <base-href> (failing loudly, via a grep guard, if that literal isn't found), and a
# byte-identical 404.html for GitHub Pages' per-directory SPA deep-link fallback.
#
# Shared by .github/workflows/pages.yml (the real deploy, base href "/blaze-forms/demo/") and
# .github/workflows/ci.yml's demo-publish job (a scratch base href, so the rewrite + guard run on
# every PR, not only on push to main -- this is the one thing a bare `dotnet publish` can't catch).
#
# Usage: eng/assemble-demo.sh <demo-target-dir> <base-href>
set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "usage: $0 <demo-target-dir> <base-href>" >&2
  exit 1
fi

demo_target_dir="$1"
base_href="$2"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
publish_dir="$(mktemp -d)"
trap 'rm -rf "$publish_dir"' EXIT

dotnet publish "$repo_root/samples/BlazeForms.Demo.Wasm" -c Release -o "$publish_dir"

mkdir -p "$demo_target_dir"
cp -r "$publish_dir/wwwroot/." "$demo_target_dir/"

index_html="$demo_target_dir/index.html"

if ! grep -q '<base href="/" />' "$index_html"; then
  echo "::error::base href rewrite guard: expected <base href=\"/\" /> not found in $index_html" >&2
  exit 1
fi

# -i.bak (no space before the suffix) is the one `sed -i` form both BSD sed (macOS, local runs)
# and GNU sed (ubuntu-latest) accept identically.
sed -i.bak "s|<base href=\"/\" />|<base href=\"$base_href\" />|" "$index_html"
rm -f "$index_html.bak"

cp "$index_html" "$demo_target_dir/404.html"

echo "Assembled demo at $demo_target_dir with base href $base_href"
