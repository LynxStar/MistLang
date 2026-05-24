#!/bin/bash
set -e

MDBOOK_VERSION="0.4.45"
TABS_VERSION="0.3.4"

mkdir -p ./bin
export PATH="$PWD/bin:$PATH"

# Install mdbook
curl -sSL "https://github.com/rust-lang/mdBook/releases/download/v${MDBOOK_VERSION}/mdbook-v${MDBOOK_VERSION}-x86_64-unknown-linux-gnu.tar.gz" | tar -xz -C ./bin

# Install mdbook-tabs
curl -sSL "https://github.com/RustForWeb/mdbook-plugins/releases/download/v${TABS_VERSION}/mdbook-tabs-v${TABS_VERSION}-x86_64-unknown-linux-gnu.tar.gz" | tar -xz -C ./bin

# Build the book
mdbook build
