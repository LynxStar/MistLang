#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
dotnet run --project "$repo_root/tools/diagnostics/DiagnosticCatalog/DiagnosticCatalog.csproj" -- "$repo_root/book/src/reference/diagnostics.md"
