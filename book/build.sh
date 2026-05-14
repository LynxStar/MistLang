#!/bin/bash
set -e

# Check that mdbook is installed
if ! command -v mdbook &> /dev/null; then
    echo "Error: mdbook is not installed. Install it with: cargo install mdbook"
    exit 1
fi

# Check that mdbook-tabs is installed
if ! command -v mdbook-tabs &> /dev/null; then
    echo "Error: mdbook-tabs is not installed. Install it with: cargo install mdbook-tabs"
    exit 1
fi

# Change to the book/ directory (where this script lives)
cd "$(dirname "$0")"

# Build the book
mdbook build

# Open the built site in the default browser (macOS)
# On Linux, use: xdg-open output/index.html
open output/index.html
