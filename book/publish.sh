#!/usr/bin/env bash
set -euo pipefail

MDBOOK_VERSION="0.5.2"
TABS_VERSION="0.3.4"
book_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
bin_dir="$book_dir/bin"

case "$(uname -s)/$(uname -m)" in
    Linux/x86_64) target=x86_64-unknown-linux-gnu ;;
    Linux/aarch64|Linux/arm64) target=aarch64-unknown-linux-gnu; mdbook_target=aarch64-unknown-linux-musl ;;
    Darwin/x86_64) target=x86_64-apple-darwin ;;
    Darwin/arm64) target=aarch64-apple-darwin ;;
    *) echo "Unsupported book build platform: $(uname -s)/$(uname -m)" >&2; exit 1 ;;
esac

mkdir -p "$bin_dir"
export PATH="$bin_dir:$PATH"
temporary="$(mktemp -d)"
trap 'rm -rf "$temporary"' EXIT

install_tool() {
    local name="$1" version="$2" repository="$3" target="$4"
    if [[ -x "$bin_dir/$name" && "$("$bin_dir/$name" --version)" == "$name v$version" ]]; then
        return
    fi
    if [[ -x "$bin_dir/$name" && "$("$bin_dir/$name" --version)" == "$name $version" ]]; then
        return
    fi
    curl --fail --location --silent --show-error --retry 3 \
        "https://github.com/$repository/releases/download/v$version/$name-v$version-$target.tar.gz" \
        --output "$temporary/$name.tar.gz"
    tar -xzf "$temporary/$name.tar.gz" -C "$temporary" "$name"
    install -m 755 "$temporary/$name" "$bin_dir/$name"
}

install_tool mdbook "$MDBOOK_VERSION" rust-lang/mdBook "${mdbook_target:-$target}"
install_tool mdbook-tabs "$TABS_VERSION" RustForWeb/mdbook-plugins "$target"
mdbook build "$book_dir"
test -s "$book_dir/output/index.html"
