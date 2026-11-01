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

import run


# (name, tier, fn). Tier is the exact Test/AGENTS.md key value, "1".."6".
CASES = []


def _aggregate_entries(order, roles=None):
    """Hand-built manifest entries for the composed samples in orchestrator order,
    used to drive check_aggregate_core with in-memory text and no files."""
    roles = roles or {}
    return [{"name": n, "role": roles.get(n, "executable"),
             "expectation": f"{n}.out", "path": f"samples/{n}"} for n in order]


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


@case("exact mode rejects an empty required expectation")
def _():
    # An empty exact expectation matches empty output; without this it would pass
    # forever, silently erasing the program's output contract (F3).
    status, reasons = run.evaluate("executable", 0, "", "", "")
    assert status == "fail" and any("empty expectation" in r for r in reasons), reasons


@case("test-tally refuses a generated-C# warning on stdout")
def _():
    # mist test runs dotnet test with inherited streams, so a generated-C# build
    # warning lands on stdout, not the compiler's stderr; it must still be refused (F2).
    out = "X.cs(1,1): warning CS0168: unused variable\nPassed: 5\nFailed: 0\n"
    status, reasons = run.evaluate("test-library", 0, out, "", r"Passed:\s+5\b")
    assert status == "fail" and any("diagnostic" in r and "CS0168" in r for r in reasons), reasons


@case("msbuild-consumer refuses a build warning on stdout")
def _():
    out = "Y.cs(2,3): warning CS0219: assigned but never used\nbridge:widget-1\n"
    status, reasons = run.evaluate("msbuild-consumer", 0, out, "", "bridge:widget-1")
    assert status == "fail" and any("diagnostic" in r for r in reasons), reasons


@case("executable stdout is not scanned for diagnostics")
def _():
    # A plain executable's stdout is its application output and is only ever compared,
    # never scanned, so legitimate text that looks like a diagnostic is not misread (F2).
    app = "error CS0001: this is the program's own printed line\n"
    status, _ = run.evaluate("executable", 0, app, "", app)
    assert status == "pass"


@case("role_diagnostics reads stdout only for build-C# roles")
def _():
    warn = "Z.cs(1,1): warning CS0168: x"
    assert run.role_diagnostics("test-library", warn, "") != []
    assert run.role_diagnostics("executable", warn, "") == []


@case("composed-executable records a standalone run and needs no expectation")
def _():
    # A composed-executable is run standalone; with no entry point the compiler exits
    # nonzero with MIST0108, which is recorded as a failure, never substituted by
    # aggregate coverage, and compare=none asks for no committed expectation (F1).
    stderr = "samples/Tuples/Program.mist: error MIST0108: produces an executable but declares no entry point"
    status, reasons = run.evaluate("composed-executable", 1, "", stderr, None)
    assert status == "fail", reasons
    assert any("MIST0108" in r for r in reasons) and any("nonzero exit" in r for r in reasons), reasons
    assert not any("no committed expectation" in r for r in reasons), reasons


@case("capture refuses an empty run before writing")
def _():
    # Refusing in the pure verdict keeps run_one from ever reaching the open(), so an
    # empty capture cannot overwrite a committed expectation (F3).
    ok, text, reasons = run.capture_verdict("executable", 0, "", "")
    assert ok is False and text == "" and any("empty capture" in r for r in reasons), reasons


@case("capture refuses a diagnostic run and permits a clean one")
def _():
    bad_ok, _, bad_reasons = run.capture_verdict(
        "test-library", 0, "A.cs(1,1): warning CS0168: x\nPassed: 5\n", "")
    assert bad_ok is False and any("diagnostic" in r for r in bad_reasons), bad_reasons
    good_ok, good_text, _ = run.capture_verdict("executable", 0, "hello\n", "")
    assert good_ok is True and good_text == "hello\n"


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
    # A composed-executable has no standalone output, so it is noted and skipped by the
    # cross-check rather than demanded as a block; the surrounding blocks still match.
    entries = _aggregate_entries(["A", "Hidden", "B"], roles={"Hidden": "composed-executable"})
    ok, notes = run.check_aggregate_core(
        entries, ["A", "Hidden", "B"],
        "HEADER\na1\na2\nx-composed-x\nb1\n",
        {"A": "a1\na2\n", "B": "b1\n"})
    assert ok, notes
    assert any("Hidden" in n for n in notes), notes


@case("aggregate check fails when a block is out of order")
def _():
    entries = _aggregate_entries(["A", "B"])
    ok, notes = run.check_aggregate_core(
        entries, ["A", "B"], "HEADER\nb1\na1\n",  # B appears before A
        {"A": "a1\n", "B": "b1\n"})
    assert not ok and any("B:" in n for n in notes), notes


@case("aggregate check fails on a missing composed expectation")
def _():
    entries = _aggregate_entries(["A", "B"])
    ok, notes = run.check_aggregate_core(
        entries, ["A", "B"], "HEADER\na1\nb1\n",
        {"A": "a1\n"})  # B has no expectation
    assert not ok and any("no committed standalone expectation for" in n for n in notes), notes


@case("unknown selection rejects an --only typo")
def _():
    entries = [{"name": "HelloMist"}, {"name": "Classes"}]
    assert run.unknown_selection({"HelloMist", "NoSuchSample"}, entries) == ["NoSuchSample"]
    assert run.unknown_selection({"HelloMist"}, entries) == []
    assert run.unknown_selection(None, entries) == []


@case("verdict passes only a fully covered run")
def _():
    good = [{"name": "A", "status": "pass"}, {"name": "L", "status": "exercised-via-consumer"}]
    assert run.verdict(good, [], True, False) == 0


@case("verdict fails omitted coverage and unmet prerequisites")
def _():
    # not-exercised and skipped-prerequisite leave verification incomplete, so neither
    # a default network-skipped run nor an --only supporting subset can report success.
    assert run.verdict([{"name": "L", "status": "not-exercised"}], [], True, False) == 1
    assert run.verdict([{"name": "E", "status": "skipped-prerequisite"}], [], True, False) == 1
    assert run.verdict([{"name": "A", "status": "fail"}], [], True, False) == 1


@case("verdict fails on a reconciliation problem")
def _():
    good = [{"name": "A", "status": "pass"}]
    assert run.verdict(good, ["duplicate manifest name 'A'"], True, False) == 1


@case("verdict respects capture for the aggregate cross-check only")
def _():
    good = [{"name": "A", "status": "pass"}]
    # A mismatched aggregate blocks a normal run but not a capture that is rewriting it;
    # coverage gaps still block even under capture.
    assert run.verdict(good, [], False, False) == 1
    assert run.verdict(good, [], False, True) == 0
    assert run.verdict([{"name": "L", "status": "not-exercised"}], [], True, True) == 1


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
