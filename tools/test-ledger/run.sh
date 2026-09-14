#!/bin/bash
# A test run that cannot lose a failure's name or message.
#
#   tools/test-ledger/run.sh [target] [extra dotnet test arguments...]
#
# target defaults to Mist.slnx. Set LEDGER_NO_BUILD=1 to skip the build step when the
# binaries are known to be current. Everything lands in TestResults/ledger/<stamp>/:
#   env.txt       machine state and every other dotnet process at the start of the run
#   build.log     the whole build output
#   test.log      the whole test output, never filtered
#   trx/          one trx per test assembly
#   failures.txt  every failed test with its full message and stack, read from the trx
#   summary.txt   per-assembly counts, duration, and the failure signature classification
set -u

repo="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$repo"

target="${1:-Mist.slnx}"
if [ $# -gt 0 ]; then
    shift
fi

dir="$repo/TestResults/ledger/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$dir/trx"

{
    echo "date:     $(date -u +%Y-%m-%dT%H:%M:%SZ)"
    echo "target:   $target $*"
    echo "head:     $(git rev-parse HEAD)"
    echo "ulimit-n: $(ulimit -n)"
    echo "cpus:     $(sysctl -n hw.ncpu 2>/dev/null || nproc)"
    echo
    echo "--- git status --short"
    git status --short
    echo
    echo "--- git worktree list"
    git worktree list
    echo
    echo "--- other dotnet processes"
    ps -axo pid,rss,etime,args | grep -E 'dotnet|testhost|MSBuild|VBCSCompiler' | grep -v grep | cut -c1-240
    echo
    echo "--- swap"
    sysctl vm.swapusage 2>/dev/null || free -m
    echo
    echo "--- vm_stat"
    vm_stat 2>/dev/null
} > "$dir/env.txt" 2>&1

if [ "${LEDGER_NO_BUILD:-0}" != "1" ]; then
    dotnet build "$target" > "$dir/build.log" 2>&1
    build_exit=$?
    lock_warnings=$(grep -c MSB3026 "$dir/build.log")
    echo "build exit $build_exit, MSB3026 x$lock_warnings" | tee -a "$dir/env.txt"
    if [ "$build_exit" -ne 0 ] || [ "$lock_warnings" -ne 0 ]; then
        echo "Build did not finish cleanly; not running tests. See $dir/build.log"
        exit 1
    fi
fi

echo "ledger: $dir"
start=$(date +%s)
dotnet test "$target" --no-build --logger trx --results-directory "$dir/trx" "$@" 2>&1 | tee "$dir/test.log"
test_exit=${PIPESTATUS[0]}
echo "test exit $test_exit after $(( $(date +%s) - start ))s" >> "$dir/env.txt"

python3 "$repo/tools/test-ledger/failures.py" "$dir" "$repo"
echo
cat "$dir/summary.txt"
exit "$test_exit"