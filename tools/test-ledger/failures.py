#!/usr/bin/env python3
"""Reads a ledger directory and writes failures.txt and summary.txt beside its trx folder.

    python3 tools/test-ledger/failures.py <ledger-dir> [repo-root]

The classifier recognises the signatures recorded in backlog/issues.yaml, so a mass failure
is named by its shape rather than by 400 messages read one at a time.

A trx is written only when its assembly finishes, so a stopped run leaves none for the assembly
it was in. Those assemblies are named from test.log, and any failure the console printed before
the stop is recovered from test.log into failures.txt.
"""
import collections
import datetime
import functools
import glob
import os
import re
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

AST_MISMATCH = re.compile(r"AST count \((\d+)\) does not match compilation unit count \((\d+)\)")
STANDARD_MISSING = re.compile(r"The namespace 'Standard' could not be found")
UNDEFINED_NAME = re.compile(r"(MIST_UNDEFINED_\w+)[^'\n]*'([^'\n]+)'")
ERROR_SENTINEL = re.compile(r"ErrorType has no TypeKind")

OUTCOME = re.compile(r"^outcome: (.*)$|^(test exit \d+ after \d+s)$", re.M)
STARTED = re.compile(r"^Test run for (.+?\.dll) ", re.M)
CONSOLE_RESULT = re.compile(r"^  (Failed|Passed|Skipped) (.+) \[[^\]]*\]$")
CONSOLE_FRAME = re.compile(
    r"^(Test run for |A total of |Starting test execution|VSTest version|Results File:|Passed!|Failed!|\[xUnit\.net )")


def parse_time(text):
    # trx carries seven fractional digits, which fromisoformat on 3.9 rejects.
    match = re.match(r"(.*T\d\d:\d\d:\d\d)(\.\d+)?(.*)", text)
    fraction = (match.group(2) or ".0")[:7].ljust(7, "0")
    zone = match.group(3).replace("Z", "+00:00")
    return datetime.datetime.fromisoformat(match.group(1) + fraction + zone)


def read_trx(path):
    root = ET.parse(path).getroot()
    times = root.find("t:Times", NS)
    counters = root.find("t:ResultSummary/t:Counters", NS)
    storage = root.find("t:TestDefinitions/t:UnitTest", NS)
    assembly = os.path.basename(storage.get("storage")) if storage is not None else os.path.basename(path)
    results = []
    for result in root.iterfind("t:Results/t:UnitTestResult", NS):
        message = result.findtext("t:Output/t:ErrorInfo/t:Message", default="", namespaces=NS)
        stack = result.findtext("t:Output/t:ErrorInfo/t:StackTrace", default="", namespaces=NS)
        stdout = result.findtext("t:Output/t:StdOut", default="", namespaces=NS)
        results.append({
            "name": result.get("testName"),
            "outcome": result.get("outcome"),
            "start": parse_time(result.get("startTime")),
            "duration": result.get("duration"),
            "message": message,
            "stack": stack,
            "stdout": stdout,
        })
    return {
        "path": path,
        "assembly": assembly,
        "start": parse_time(times.get("start")) if times is not None else None,
        "finish": parse_time(times.get("finish")) if times is not None else None,
        "total": counters.get("total") if counters is not None else "?",
        "passed": counters.get("passed") if counters is not None else "?",
        "failed": counters.get("failed") if counters is not None else "?",
        "results": results,
    }


def classify(message):
    if AST_MISMATCH.search(message):
        return "ast-unit-mismatch"
    if STANDARD_MISSING.search(message):
        return "standard-namespace-missing"
    if ERROR_SENTINEL.search(message):
        return "error-type-sentinel"
    if any(name != "Standard" for _, name in UNDEFINED_NAME.findall(message)):
        return "undefined-name"
    return "other"


def read_text(path):
    if not os.path.exists(path):
        return ""
    with open(path, encoding="utf-8", errors="replace") as handle:
        return handle.read()


def outcome(ledger):
    matches = OUTCOME.findall(read_text(os.path.join(ledger, "env.txt")))
    if not matches:
        return "none recorded -- the run is still going, or was killed before it could record one"
    current, legacy = matches[-1]
    return current or legacy


def console_failures(log):
    blocks = []
    current = None
    for line in log.splitlines():
        result = CONSOLE_RESULT.match(line)
        if result or CONSOLE_FRAME.match(line):
            current = (result.group(2), [line]) if result and result.group(1) == "Failed" else None
            if current:
                blocks.append(current)
        elif current is not None:
            current[1].append(line)
    return [(name, "\n".join(lines).rstrip()) for name, lines in blocks]


@functools.lru_cache(maxsize=None)
def standard_files(repo):
    paths = sorted(glob.glob(os.path.join(repo, "Source", "Standard", "**", "*.mist"), recursive=True))
    return [(path, read_text(path)) for path in paths]


def provenance(name, repo):
    if repo is None:
        return ""
    hits = []
    for path, text in standard_files(repo):
        offset = text.find(name)
        if offset >= 0:
            line = text.count("\n", 0, offset) + 1
            hits.append(f"{os.path.relpath(path, repo)}:{line}")
    shown = ", ".join(hits[:3]) + (f" (+{len(hits) - 3} more)" if len(hits) > 3 else "")
    return f"in Standard: {shown}" if hits else "in no Standard file"


def main():
    ledger = sys.argv[1]
    repo = sys.argv[2] if len(sys.argv) > 2 else None
    runs = [read_trx(path) for path in sorted(glob.glob(os.path.join(ledger, "trx", "*.trx")))]
    log = read_text(os.path.join(ledger, "test.log"))

    written = {run["assembly"].lower() for run in runs}
    started = [os.path.basename(path) for path in STARTED.findall(log)]
    unfinished = [name for name in started if name.lower() not in written] if len(runs) < len(started) else []

    in_trx = {r["name"] for run in runs for r in run["results"] if r["outcome"] == "Failed"}
    console_only = [(name, block) for name, block in console_failures(log) if name not in in_trx]

    with open(os.path.join(ledger, "failures.txt"), "w", encoding="utf-8") as out:
        for run in runs:
            for result in sorted(run["results"], key=lambda r: r["start"]):
                if result["outcome"] != "Failed":
                    continue
                offset = (result["start"] - run["start"]).total_seconds() if run["start"] else 0.0
                out.write(f"=== FAILED {result['name']}\n")
                out.write(f"    assembly {run['assembly']}, started +{offset:.2f}s, duration {result['duration']}\n")
                out.write(result["message"].rstrip() + "\n")
                if result["stack"]:
                    out.write("--- stack\n" + result["stack"].rstrip() + "\n")
                if result["stdout"]:
                    out.write("--- stdout\n" + result["stdout"].rstrip() + "\n")
                out.write("\n")
        for name, block in console_only:
            out.write(f"=== FAILED {name}\n")
            out.write("    from test.log: its assembly wrote no trx\n")
            out.write(block + "\n\n")

    lines = [f"outcome: {outcome(ledger)}"]
    for name in unfinished:
        lines.append(f"no trx: {name} started and never finished")
    if unfinished:
        seen = f"{len(console_only)} failure(s), copied to failures.txt," if console_only else "no failure"
        lines.append(f"  test.log shows {seen} before the run stopped")
    for run in runs:
        elapsed = (run["finish"] - run["start"]).total_seconds() if run["start"] and run["finish"] else 0.0
        lines.append(f"{run['assembly']}: total {run['total']}, passed {run['passed']}, failed {run['failed']}, {elapsed:.0f}s")
        failed = [r for r in run["results"] if r["outcome"] == "Failed"]
        if not failed:
            continue

        classes = collections.Counter(classify(r["message"]) for r in failed)
        lines.append("  signature: " + ", ".join(f"{kind} x{count}" for kind, count in classes.most_common()))

        pairs = collections.Counter()
        names = collections.Counter()
        others = collections.Counter()
        for result in failed:
            for match in AST_MISMATCH.findall(result["message"]):
                pairs[match] += 1
            for code, name in UNDEFINED_NAME.findall(result["message"]):
                names[(code, name)] += 1
            if classify(result["message"]) == "other":
                first = result["message"].strip().splitlines()[0] if result["message"].strip() else "(no message)"
                others[first[:160]] += 1
        if pairs:
            lines.append("  AST/unit count pairs: " + ", ".join(f"({a}) vs ({b}) x{n}" for (a, b), n in pairs.most_common()))
        if names:
            lines.append("  undefined names:")
            for (code, name), count in names.most_common(25):
                lines.append(f"    {code} '{name}' x{count} -- {provenance(name, repo)}")
        if others:
            lines.append("  other first lines:")
            for first, count in others.most_common(15):
                lines.append(f"    x{count}  {first}")

        ordered = sorted(run["results"], key=lambda r: r["start"])
        first_failed = min(failed, key=lambda r: r["start"])
        last_failed = max(failed, key=lambda r: r["start"])
        lines.append(f"  first failure +{(first_failed['start'] - run['start']).total_seconds():.2f}s: {first_failed['name']}")
        lines.append(f"  last failure  +{(last_failed['start'] - run['start']).total_seconds():.2f}s: {last_failed['name']}")
        lines.append("  earliest tests to start:")
        for result in ordered[:20]:
            offset = (result["start"] - run["start"]).total_seconds()
            lines.append(f"    +{offset:6.2f}s {result['outcome']:<8} {result['name']}")

    with open(os.path.join(ledger, "summary.txt"), "w", encoding="utf-8") as out:
        out.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    main()