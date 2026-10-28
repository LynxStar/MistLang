#!/usr/bin/env bash
set -euo pipefail
set +x
: "${BOOK_SSH_KEY:?}" "${BOOK_SSH_USER:?}" "${BOOK_REVISION:?}"
[[ "$BOOK_REVISION" =~ ^[a-f0-9]{40}$ ]] || exit 2
book_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ssh -F /dev/null -i "$BOOK_SSH_KEY" \
    -o BatchMode=yes -o IdentitiesOnly=yes -o StrictHostKeyChecking=yes \
    -o UserKnownHostsFile="$book_dir/ci/known_hosts" \
    -o ConnectTimeout=15 -o ServerAliveInterval=15 -o ServerAliveCountMax=4 \
    "$BOOK_SSH_USER@192.168.1.6" "$BOOK_REVISION" < "$book_dir/site.tar.gz"
