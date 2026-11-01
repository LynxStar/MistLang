#!/usr/bin/env python3
# Bounded controlled checks that the harness detects the ways a sample run can be
# wrong. They drive run.py's pure verdict functions with hand-built inputs and
# spawn no compiler, so this proves the detection logic itself, not the suite.
#
#   tools/sample-acceptance/run.py selftest
#
# This is deliberately not an xUnit test: it owns no compiler process and must
# not grow into one test per sample. The one aggregate check stays in the suite.
#
# Every case carries runnable tier metadata, mirroring Test/AGENTS.md: the key is
# the exact string "Tier". These drive pure verdict functions with no compiler and
# no pipeline, so each is Tier 1 (Unit). The registry prints and tallies the tier
# so the classification is discoverable, not just asserted in a comment.

import os
import tempfile

import run


# (name, tier, fn). Tier is the exact Test/AGENTS.md key value, "1".."6".
CASES = []


def _aggregate_fixture(tmp, order, expectations, baseline, roles=None):
    """A synthetic orchestrator program, expectations dir and baseline for driving
    check_aggregate without any compiler or real sample."""
    roles = roles or {}
    exp_dir = os.path.join(tmp, "expectations")
    os.makedirs(exp_dir, exist_ok=True)
    for name, text in expectations.items():
        with open(os.path.join(exp_dir, f"{name}.out"), "w") as fh:
            fh.write(text)
    program = os.path.join(tmp, "Program.mist")
    with open(program, "w") as fh:
        fh.write("".join(f"{n}.Program.RunDemo();\n" for n in order))
    base = os.path.join(tmp, "baseline.out")
    with open(base, "w") as fh:
        fh.write(baseline)
    entries = [{"name": n, "role": roles.get(n, "executable"),
                "expectation": f"{n}.out", "path": f"samples/{n}"} for n in order]
    return entries, exp_dir, base, program


def case(name, tier="1"):
    def register(fn):
        CASES.append((name, tier, fn))
        return fn
    return register


@case("reconcile accepts agreement")
def _():
    entries = [{"path": "samples/A", "name": "A", "role": "executable"},
               {"path": "samples/B/Lib", "name": "Lib", "role": "library"}]
    assert run.reconcile(["samples/A", "samples/B/Lib"], [], entries) == []


@case("reconcile detects an omitted project")
def _():
    entries = [{"path": "samples/A", "name": "A", "role": "executable"}]
    problems = run.reconcile(["samples/A", "samples/NewSample"], [], entries)
    assert any("NewSample" in p and "not in manifest" in p for p in problems), problems


@case("reconcile detects a manifest entry with no project on disk")
def _():
    entries = [{"path": "samples/Gone", "name": "Gone", "role": "executable"}]
    problems = run.reconcile([], [], entries)
    assert any("Gone" in p and "not found on disk" in p for p in problems), problems


@case("reconcile detects a duplicate and an unclassified role")
def _():
    entries = [{"path": "samples/A", "name": "A", "role": "executable"},
               {"path": "samples/A", "name": "A", "role": "mystery"}]
    problems = run.reconcile(["samples/A"], [], entries)
    assert any("duplicate" in p for p in problems), problems
    assert any("unclassified" in p for p in problems), problems


@case("reconcile detects an unaccounted C# companion")
def _():
    entries = [{"path": "samples/A", "name": "A", "role": "executable"}]
    problems = run.reconcile(["samples/A"], ["samples/A/Bridge"], entries)
    assert any("companion" in p and "not in manifest" in p for p in problems), problems


@case("reconcile detects a duplicate name on distinct paths")
def _():
    # Two entries sharing a name collide in the results map and logs; reconciliation
    # must reject them even though their paths differ and both exist on disk.
    entries = [{"path": "samples/A", "name": "Dup", "role": "executable"},
               {"path": "samples/B", "name": "Dup", "role": "executable"}]
    problems = run.reconcile(["samples/A", "samples/B"], [], entries)
    assert any("duplicate manifest name" in p and "Dup" in p for p in problems), problems


@case("evaluate passes a clean matching run")
def _():
    status, reasons = run.evaluate("executable", 0, "hello\nworld\n", "", "hello\nworld\n")
    assert status == "pass", reasons


@case("evaluate fails on a compiler diagnostic")
def _():
    stderr = "samples/X/Program.mist(3,5): warning MIST0177: redundant cast"
    status, reasons = run.evaluate("executable", 0, "hello\n", stderr, "hello\n")
    assert status == "fail" and any("diagnostic" in r for r in reasons), reasons


@case("evaluate fails on an info diagnostic too")
def _():
    stderr = "info MIST9001: something informational"
    status, _ = run.evaluate("executable", 0, "ok\n", stderr, "ok\n")
    assert status == "fail"


@case("evaluate fails on a nonzero exit")
def _():
    status, reasons = run.evaluate("executable", 1, "ok\n", "", "ok\n")
    assert status == "fail" and any("nonzero exit" in r for r in reasons), reasons


@case("evaluate fails on an output mismatch")
def _():
    status, reasons = run.evaluate("executable", 0, "actual\n", "", "expected\n")
    assert status == "fail" and any("does not match" in r for r in reasons), reasons


@case("evaluate fails when no expectation is committed")
def _():
    status, reasons = run.evaluate("executable", 0, "actual\n", "", None)
    assert status == "fail" and any("no committed expectation" in r for r in reasons), reasons


@case("evaluate normalizes CRLF before comparing")
def _():
    status, _ = run.evaluate("executable", 0, "a\r\nb\r\n", "", "a\nb\n")
    assert status == "pass"


@case("contains mode tolerates surrounding noise")
def _():
    out = "restoring...\nbridge:widget-1\nbuild succeeded\n"
    status, _ = run.evaluate("msbuild-consumer", 0, out, "", "bridge:widget-1")
    assert status == "pass"


@case("contains mode fails when the marker is absent")
def _():
    status, _ = run.evaluate("msbuild-consumer", 0, "nothing here\n", "", "bridge:widget-1")
    assert status == "fail"


@case("test-tally matches the runner's reported counts")
def _():
    out = "Tests run\nPassed:  5\nFailed:  0\n"
    status, _ = run.evaluate("test-library", 0, out, "", r"Passed:\s+5\b|Failed:\s+0\b")
    assert status == "pass"


@case("test-tally fails on a changed count")
def _():
    out = "Passed:  4\nFailed:  1\n"
    status, _ = run.evaluate("test-library", 0, out, "", r"Passed:\s+5\b")
    assert status == "fail"


@case("contains mode rejects an empty marker expectation")
def _():
    # An empty consumer-marker file verifies nothing, so any output would pass; it
    # must be reported as a defect rather than silently accepted.
    status, reasons = run.evaluate("msbuild-consumer", 0, "anything\n", "", "   \n")
    assert status == "fail" and any("empty expectation" in r for r in reasons), reasons


@case("test-tally rejects an empty tally expectation")
def _():
    status, reasons = run.evaluate("test-library", 0, "Passed: 5\n", "", "\n")
    assert status == "fail" and any("empty expectation" in r for r in reasons), reasons


@case("consumer map derives coverage from the referencing consumer")
def _():
    # The reverse reference map is what lets a supporting project's status be derived
    # from a real consumer instead of being asserted up front.
    entries = [{"name": "App", "path": "samples/App"},
               {"name": "Lib", "path": "samples/Lib"},
               {"name": "Orphan", "path": "samples/Orphan"}]
    saved = run.referenced_paths
    run.referenced_paths = lambda e: ["samples/Lib"] if e["name"] == "App" else []
    try:
        cmap = run.consumer_map(entries)
    finally:
        run.referenced_paths = saved
    assert cmap.get("samples/Lib") == ["App"], cmap
    assert "samples/Orphan" not in cmap, cmap


@case("aggregate check accepts ordered blocks and skips composed-only")
def _():
    with tempfile.TemporaryDirectory() as tmp:
        entries, exp, base, prog = _aggregate_fixture(
            tmp,
            order=["A", "Hidden", "B"],
            expectations={"A": "a1\na2\n", "B": "b1\n"},
            baseline="HEADER\na1\na2\nx-composed-x\nb1\n",
            roles={"Hidden": "composed-library"})
        ok, notes = run.check_aggregate(entries, exp, base, prog)
        assert ok, notes
        assert any("Hidden" in n for n in notes), notes


@case("aggregate check fails when a block is out of order")
def _():
    with tempfile.TemporaryDirectory() as tmp:
        entries, exp, base, prog = _aggregate_fixture(
            tmp, order=["A", "B"],
            expectations={"A": "a1\n", "B": "b1\n"},
            baseline="HEADER\nb1\na1\n")  # B appears before A
        ok, notes = run.check_aggregate(entries, exp, base, prog)
        assert not ok and any("B:" in n for n in notes), notes


@case("aggregate check fails on a missing composed expectation")
def _():
    with tempfile.TemporaryDirectory() as tmp:
        entries, exp, base, prog = _aggregate_fixture(
            tmp, order=["A", "B"],
            expectations={"A": "a1\n"},  # B has no expectation file
            baseline="HEADER\na1\nb1\n")
        ok, notes = run.check_aggregate(entries, exp, base, prog)
        assert not ok and any("no committed standalone expectation for" in n for n in notes), notes


def main():
    failed = 0
    tiers = {}
    for name, tier, fn in CASES:
        tiers[tier] = tiers.get(tier, 0) + 1
        try:
            fn()
            print(f"  ok   [T{tier}] {name}")
        except AssertionError as ex:
            failed += 1
            print(f"  FAIL [T{tier}] {name}: {ex}")
    tier_summary = ", ".join(f"Tier {t}: {n}" for t, n in sorted(tiers.items()))
    print(f"\n{len(CASES) - failed}/{len(CASES)} checks passed ({tier_summary})")
    return 1 if failed else 0


if __name__ == "__main__":
    import sys
    sys.exit(main())
