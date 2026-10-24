#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO_ROOT"

# =============================================================================
# publish.sh — Build and install the Mist CLI tool
# =============================================================================

# ---------------------------------------------------------------------------
# Task 3.2: Prerequisite check for dotnet CLI
# ---------------------------------------------------------------------------
if ! command -v dotnet &>/dev/null; then
    echo "Error: .NET SDK is required but 'dotnet' was not found on PATH." >&2
    exit 1
fi

STORM_PROJECT="Source/Storm.SourceGenerator/Storm.SourceGenerator.csproj"
if [[ ! -f "$STORM_PROJECT" ]]; then
    if ! command -v git &>/dev/null; then
        echo "Error: Git is required to initialize the Storm source-generator submodule." >&2
        exit 1
    fi

    echo "Initializing Storm source-generator submodule..."
    if ! git submodule update --init --recursive -- Source/Storm.SourceGenerator; then
        echo "Error: Could not initialize Storm. Ensure Git can access the private LynxStar/StormLang repository, then run 'git submodule update --init --recursive'." >&2
        exit 1
    fi

    if [[ ! -f "$STORM_PROJECT" ]]; then
        echo "Error: Storm source-generator project is still missing at ${REPO_ROOT}/${STORM_PROJECT}." >&2
        exit 1
    fi
fi

# ---------------------------------------------------------------------------
# Task 3.3: Platform RID auto-detection
# ---------------------------------------------------------------------------
echo "Detecting platform..."

OS_NAME="$(uname -s)"
ARCH_NAME="$(uname -m)"

case "$OS_NAME" in
    Darwin) RID_OS="osx" ;;
    Linux)  RID_OS="linux" ;;
    *)
        echo "Error: Unsupported platform: ${OS_NAME}-${ARCH_NAME}" >&2
        exit 1
        ;;
esac

case "$ARCH_NAME" in
    arm64)   RID_ARCH="arm64" ;;
    x86_64)  RID_ARCH="x64" ;;
    aarch64) RID_ARCH="arm64" ;;
    *)
        echo "Error: Unsupported platform: ${OS_NAME}-${ARCH_NAME}" >&2
        exit 1
        ;;
esac

RID="${RID_OS}-${RID_ARCH}"
echo "Detected platform: ${RID}"

# ---------------------------------------------------------------------------
# Task 3.4: dotnet publish invocation
# ---------------------------------------------------------------------------
echo "Publishing Mist CLI (Release, ${RID})..."

if ! dotnet publish Source/Mist.CLI/Mist.CLI.csproj -c Release -r "$RID" -p:TreatWarningsAsErrors=true; then
    echo "Error: dotnet publish failed." >&2
    exit 1
fi

# ---------------------------------------------------------------------------
# Task 3.5: Binary installation to ~/.mist/bin
# ---------------------------------------------------------------------------
INSTALL_DIR="$HOME/.mist/bin"
PUBLISH_DIR="Source/Mist.CLI/bin/Release/net10.0/${RID}/publish"
BINARY_SRC="${PUBLISH_DIR}/mist"
BINARY_DST="${INSTALL_DIR}/mist"

echo "Installing mist binary to ${INSTALL_DIR}..."

mkdir -p "$INSTALL_DIR"

if ! cp "$BINARY_SRC" "$BINARY_DST"; then
    echo "Error: Failed to copy mist binary to ~/.mist/bin/" >&2
    exit 1
fi

# Copy the Standard Library source files alongside the binary so that
# StdlibLoader can find them at AppContext.BaseDirectory/Standard/.
STDLIB_SRC="${PUBLISH_DIR}/Standard"
STDLIB_DST="${INSTALL_DIR}/Standard"

if [[ -d "$STDLIB_SRC" ]]; then
    rm -rf "$STDLIB_DST"
    cp -R "$STDLIB_SRC" "$STDLIB_DST"
    echo "Installed Standard Library to ${STDLIB_DST}/"
else
    echo "Warning: Standard Library not found at ${STDLIB_SRC} — stdlib will not be available." >&2
fi

# Copy the bundled steering files alongside the binary so that `mist steering`
# can find them at AppContext.BaseDirectory/Steering/ and install them into a
# project's .kiro/steering/ directory.
STEERING_SRC="${PUBLISH_DIR}/Steering"
STEERING_DST="${INSTALL_DIR}/Steering"

if [[ -d "$STEERING_SRC" ]]; then
    rm -rf "$STEERING_DST"
    cp -R "$STEERING_SRC" "$STEERING_DST"
    echo "Installed steering files to ${STEERING_DST}/"
else
    echo "Warning: Steering files not found at ${STEERING_SRC} — 'mist steering' will not be available." >&2
fi

# ---------------------------------------------------------------------------
# Task 3.6: PATH configuration with shell profile detection
# ---------------------------------------------------------------------------
echo "Configuring PATH..."

PROFILE_UPDATED=""

if echo "$PATH" | tr ':' '\n' | grep -qx "$HOME/.mist/bin"; then
    echo "PATH already contains ~/.mist/bin — skipping profile modification."
else
    # Detect shell profile file
    case "${SHELL:-}" in
        */zsh)
            PROFILE_FILE="$HOME/.zshrc"
            ;;
        */bash)
            if [[ -f "$HOME/.bashrc" ]]; then
                PROFILE_FILE="$HOME/.bashrc"
            elif [[ "$OS_NAME" == "Darwin" && -f "$HOME/.bash_profile" ]]; then
                PROFILE_FILE="$HOME/.bash_profile"
            else
                PROFILE_FILE="$HOME/.bashrc"
            fi
            ;;
        *)
            PROFILE_FILE="$HOME/.profile"
            ;;
    esac

    EXPORT_LINE='export PATH="$HOME/.mist/bin:$PATH"'

    # Only append if the line is not already present in the profile
    if [[ -f "$PROFILE_FILE" ]] && grep -qF "$EXPORT_LINE" "$PROFILE_FILE"; then
        echo "PATH export already present in ${PROFILE_FILE} — skipping."
    else
        echo "$EXPORT_LINE" >> "$PROFILE_FILE"
        PROFILE_UPDATED="$PROFILE_FILE"
        echo "Added PATH export to ${PROFILE_FILE}."
    fi
fi

# ---------------------------------------------------------------------------
# Task 3.7: Completion summary output
# ---------------------------------------------------------------------------
echo ""
echo "✓ Mist CLI installed to ${BINARY_DST}"

if [[ -n "$PROFILE_UPDATED" ]]; then
    echo ""
    echo "To start using mist, restart your terminal or run:"
    echo "  source ${PROFILE_UPDATED}"
fi

echo ""
echo "Usage:"
echo "  mist build <project-folder>"
echo "  mist run <project-folder>"
echo "  mist steering [project-folder]"
