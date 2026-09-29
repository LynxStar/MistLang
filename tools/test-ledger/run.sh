#!/bin/bash
# A test run that cannot lose a failure's name or message.
#
#   tools/test-ledger/run.sh [target] [extra dotnet test arguments...]
#
# target defaults to Mist.slnx. Set LEDGER_NO_BUILD=1 to skip the build step when the
# binaries are known to be current. Everything lands in TestResults/ledger/<stamp>/, and
# TestResults/ledger/latest points at the newest run from the moment it starts (a symlink, or on
# Windows a directory junction, since Git Bash's ln -s copies a directory instead of linking it):
#   env.txt       machine state and every other dotnet process at the start, then how the run ended
#   build.log     the whole build output
#   test.log      the whole test output, never filtered
#   trx/          one trx per test assembly
#   summary.txt   how the run ended, per-assembly counts, and the failure signature classification
#   failures.txt  every failed test with its full message and stack
#
# Only the ledger's path and summary.txt reach stdout, so there is nothing to redirect. A run
# stopped by INT, TERM or HUP still writes summary.txt. Claude Code's TaskStop sends TERM and
# kills about a second and a half later, so a stopped run never waits for dotnet to exit; on
# Windows it only ends the shell that launched this one, which the run treats as HUP.
# summary.txt and failures.txt need a Python 3, found as python3, python or py -3.
set -u

repo="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$repo"

ledger=$$
parent=$PPID
windows=
case "$(uname -s)" in
    MINGW* | MSYS* | CYGWIN*) windows=1 ;;
esac

python=
for candidate in python3 python "py -3"; do
    if $candidate -c 'import sys; sys.exit(sys.version_info[0] != 3)' > /dev/null 2>&1; then
        python=$candidate
        break
    fi
done

target="${1:-Mist.slnx}"
if [ $# -gt 0 ]; then
    shift
fi

point_latest() {
    local latest="$repo/TestResults/ledger/latest"
    if [ -z "$windows" ]; then
        ln -sfn "$stamp" "$latest"
        return
    fi
    if [ -L "$latest" ]; then
        rm -f "$latest"
    elif [ -d "$latest" ]; then
        rm -rf "$latest"
    fi
    MSYS_NO_PATHCONV=1 cmd /c mklink /J "$(cygpath -w "$latest")" "$(cygpath -w "$dir")" > /dev/null
}

stamp="$(date +%Y%m%d-%H%M%S)"
dir="$repo/TestResults/ledger/$stamp"
mkdir -p "$dir/trx"
point_latest
since=$(date +%s)

echo "ledger: $dir"
echo "follow: tail -f $dir/test.log"

child=
interrupted=
interrupted_status=

descendants() {
    local pid
    for pid in $(pgrep -P "$1"); do
        descendants "$pid"
        echo "$pid"
    done
}

stop() {
    interrupted=$1
    interrupted_status=$2
    if [ -z "$child" ]; then
        return
    fi
    if [ -n "$windows" ] && [ -r "/proc/$child/winpid" ]; then
        MSYS_NO_PATHCONV=1 taskkill /T /F /PID "$(cat "/proc/$child/winpid")" > /dev/null 2>&1
    else
        kill -TERM $(descendants "$child") "$child" 2>/dev/null
    fi
}

processes() {
    if [ -n "$windows" ]; then
        powershell.exe -NoProfile -NonInteractive -Command 'Get-CimInstance Win32_Process | Where-Object { $_.Name -match "^(dotnet|testhost|MSBuild|VBCSCompiler)" } | ForEach-Object { "{0,7} {1,9}K {2}" -f $_.ProcessId, [int64]($_.WorkingSetSize / 1KB), $_.CommandLine }'
    else
        ps -axo pid,rss,etime,args | grep -E 'dotnet|testhost|MSBuild|VBCSCompiler' | grep -v grep
    fi
}
trap 'stop SIGINT 130' INT
trap 'stop SIGTERM 143' TERM
trap 'stop SIGHUP 129' HUP

# Windows only: a stop there ends the shell that launched this one without signalling it, and a
# native dotnet tree shares the fate of neither, so this watcher turns losing the parent into a
# hangup and losing this shell into ending the tree itself.
outlive() {
    local winpid=$1
    while kill -0 "$ledger" 2>/dev/null; do
        if ! kill -0 "$parent" 2>/dev/null; then
            kill -HUP "$ledger"
            return
        fi
        sleep 1
    done
    MSYS_NO_PATHCONV=1 taskkill /T /F /PID "$winpid" > /dev/null 2>&1
}

step() {
    local log=$1 status guard=
    shift
    "$@" > "$log" 2>&1 &
    child=$!
    if [ -n "$windows" ] && [ -r "/proc/$child/winpid" ]; then
        outlive "$(cat "/proc/$child/winpid")" &
        guard=$!
    fi
    wait "$child"
    status=$?
    if [ -n "$guard" ]; then
        kill "$guard" 2>/dev/null
    fi
    child=
    return "$status"
}

finish() {
    echo "outcome: $2" >> "$dir/env.txt"
    if [ -n "$python" ]; then
        $python "$repo/tools/test-ledger/failures.py" "$dir" "$repo"
    else
        printf 'outcome: %s\nno summary: no Python 3 found as python3, python or py -3; read trx/ and test.log\n' "$2" > "$dir/summary.txt"
    fi
    echo
    cat "$dir/summary.txt" 2>/dev/null
    echo
    if [ -s "$dir/failures.txt" ]; then
        echo "failures: $dir/failures.txt"
    fi
    echo "ledger: $dir"
    exit "$1"
}

finish_if_interrupted() {
    if [ -n "$interrupted" ]; then
        finish "$interrupted_status" "interrupted by $interrupted during $1 after $(( $(date +%s) - since ))s"
    fi
}

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
    processes | cut -c1-240
    echo
    echo "--- swap"
    sysctl vm.swapusage 2>/dev/null || free -m 2>/dev/null
    echo
    echo "--- vm_stat"
    vm_stat 2>/dev/null
    echo
} > "$dir/env.txt" 2>&1
finish_if_interrupted "setup"

if [ "${LEDGER_NO_BUILD:-0}" = "1" ]; then
    echo "build skipped: LEDGER_NO_BUILD=1" >> "$dir/env.txt"
else
    since=$(date +%s)
    step "$dir/build.log" dotnet build "$target"
    build_exit=$?
    finish_if_interrupted "build"
    lock_warnings=$(grep -c MSB3026 "$dir/build.log")
    echo "build exit $build_exit, MSB3026 x$lock_warnings" >> "$dir/env.txt"
    if [ "$build_exit" -ne 0 ] || [ "$lock_warnings" -ne 0 ]; then
        finish 1 "tests not run: build exit $build_exit, MSB3026 x$lock_warnings -- see build.log"
    fi
fi

since=$(date +%s)
step "$dir/test.log" dotnet test "$target" --no-build --logger trx --results-directory "$dir/trx" "$@"
test_exit=$?
finish_if_interrupted "tests"
finish "$test_exit" "test exit $test_exit after $(( $(date +%s) - since ))s"