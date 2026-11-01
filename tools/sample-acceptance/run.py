#!/usr/bin/env python3
# A standalone acceptance run over the sample suite that cannot silently drop a
# project or bless a failure: discovery is reconciled against the committed
# manifest before anything runs, and every runnable sample keeps its own result.
#
#   tools/sample-acceptance/run.py [options]
#   tools/sample-acceptance/run.py selftest
#
# The harness runs an arbitrary compiler (--compiler) so the same manifest and
# expectations can later judge a Mist-hosted compiler without editing this file.
# It does not build the compiler; point --compiler at one that is already built.
#
# Options:
#   --compiler "<cmd>"   Command that invokes the compiler under test. The verb
#                        (run/test), sample path and flags are appended. Default:
#                        the osx/linux mist binary under Source/Mist.CLI/bin if present.
#   --generation <id>    Generation/host label recorded in the provenance block.
#   --manifest <path>    Acceptance manifest (default: alongside this script).
#   --expectations <dir> Directory of committed per-sample expectations.
#   --out <dir>          Where logs, the report and provenance are written
#                        (artifacts; default: _runs/<stamp>, gitignored).
#   --only a,b,c         Run only these project names (by manifest `name`).
#   --include-network    Also run projects whose role needs network/extra tools
#                        (the EfCore demonstration); off by default and reported
#                        as a prerequisite skip, never as a pass.
#   --capture            Write expectations from this run instead of comparing.
#                        Seeds or refreshes expectations/<name>.out. Never use to
#                        paper over a real failure.
#   --list               Reconcile and print the classification, then stop.
#   --timeout <seconds>  Per-invocation wall-clock bound (default 600).
#
# Exit code is 0 only when reconciliation and the aggregate cross-check hold and every
# selected project reached a covered pass: a not-exercised or skipped-prerequisite
# outcome leaves verification incomplete and exits nonzero, as does an unknown --only name.

import argparse
import datetime
import json
import os
import platform
import re
import shlex
import subprocess
import sys
import tomllib

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", ".."))
SAMPLES_ROOT = os.path.join(REPO_ROOT, "samples")

# A Mist diagnostic on stderr, in the shape the CLI streams it. Shared with the
# one Tier 6 aggregate test so the standalone runner refuses the same output it does.
DIAGNOSTIC = re.compile(r"(?:^|: )(?:warning|info|error) [A-Z0-9_]+:")

# Roles and how each is exercised. compare modes: exact | contains | test-tally | none.
# scan_stdout marks the roles whose documented invocation builds or runs generated C#
# through MSBuild/dotnet with inherited streams, so a build diagnostic (e.g. "warning
# CS...") lands on stdout rather than the compiler's stderr and must be refused there.
# A composed-executable defaults to executable per its mist.toml but has no standalone
# entry point; it is still run standalone so its failure is recorded, never hidden.
ROLES = {
    "orchestrator":        {"invocation": "run",  "compare": "exact",      "runs": True},
    "executable":          {"invocation": "run",  "compare": "exact",      "runs": True},
    "generator-consumer":  {"invocation": "run",  "compare": "exact",      "runs": True, "scan_stdout": True},
    "msbuild-consumer":    {"invocation": "run",  "compare": "contains",   "runs": True, "scan_stdout": True},
    "ef-consumer":         {"invocation": "run",  "compare": "contains",   "runs": True, "network": True, "scan_stdout": True},
    "test-library":        {"invocation": "test", "compare": "test-tally", "runs": True, "scan_stdout": True},
    "composed-executable": {"invocation": "run",  "compare": "none",       "runs": True},
    "library":             {"invocation": None,   "compare": "none",       "runs": False},
    "source-generator":    {"invocation": None,   "compare": "none",       "runs": False},
    "csharp-companion":    {"invocation": None,   "compare": "none",       "runs": False, "csproj": True},
}


def normalize(text):
    return text.replace("\r\n", "\n")


def as_text(data):
    """subprocess.TimeoutExpired carries whatever was captured as bytes even under
    text mode, so decode defensively before any string use."""
    if data is None:
        return ""
    if isinstance(data, (bytes, bytearray)):
        return data.decode("utf-8", "replace")
    return data


def diagnostics_in(stderr):
    return [line for line in stderr.split("\n") if DIAGNOSTIC.search(line)]


def role_diagnostics(role, stdout, stderr):
    """Compiler/build diagnostics for a project, read from the channels its role
    actually uses. The compiler streams its own diagnostics to stderr for every
    role; a role that drives generated C# through MSBuild/dotnet (scan_stdout) also
    surfaces build diagnostics on stdout, so both are refused there. A plain
    executable's stdout is its application output and is never scanned, so legitimate
    program text that happens to look like a diagnostic is not misread as one."""
    diags = diagnostics_in(stderr)
    if ROLES[role].get("scan_stdout"):
        diags = diags + diagnostics_in(stdout)
    return diags


def discover_manifests(samples_root):
    """Every samples/**/mist.toml, as repo-relative directory paths."""
    found = []
    for base, _dirs, files in os.walk(samples_root):
        if "mist.toml" in files:
            found.append(os.path.relpath(base, REPO_ROOT))
    return sorted(found)


def discover_referenced_csproj(mist_dirs):
    """C# companion projects reached through a sample's [references]: a reference
    whose path is a directory holding exactly one .csproj, or a .csproj directly."""
    companions = set()
    for rel in mist_dirs:
        manifest = os.path.join(REPO_ROOT, rel, "mist.toml")
        with open(manifest, "rb") as handle:
            data = tomllib.load(handle)
        for ref in (data.get("references") or {}).values():
            path = ref.get("path") if isinstance(ref, dict) else None
            if not path:
                continue
            target = os.path.normpath(os.path.join(REPO_ROOT, rel, path))
            if os.path.isfile(target) and target.endswith(".csproj"):
                companions.add(os.path.relpath(os.path.dirname(target), REPO_ROOT))
            elif os.path.isdir(target):
                csprojs = [f for f in os.listdir(target) if f.endswith(".csproj")]
                if csprojs and not os.path.exists(os.path.join(target, "mist.toml")):
                    companions.add(os.path.relpath(target, REPO_ROOT))
    return sorted(companions)


def referenced_paths(entry):
    """Repo-relative paths an entry reaches through [references] and [source_generators]:
    the libraries, C# companions and generators it pulls in and thereby exercises."""
    manifest = os.path.join(REPO_ROOT, entry["path"], "mist.toml")
    if not os.path.exists(manifest):
        return []
    with open(manifest, "rb") as handle:
        data = tomllib.load(handle)
    out = []
    for section in ("references", "source_generators"):
        for ref in (data.get(section) or {}).values():
            path = ref.get("path") if isinstance(ref, dict) else None
            if not path:
                continue
            target = os.path.normpath(os.path.join(REPO_ROOT, entry["path"], path))
            out.append(os.path.relpath(target, REPO_ROOT))
    return out


def consumer_map(entries):
    """path -> sorted names of entries that reference it, so a supporting project's
    coverage can be derived from whether a real consumer actually exercised it."""
    consumers = {}
    for entry in entries:
        for ref in referenced_paths(entry):
            consumers.setdefault(ref, set()).add(entry["name"])
    return {path: sorted(names) for path, names in consumers.items()}


def load_manifest(path):
    with open(path, "rb") as handle:
        data = tomllib.load(handle)
    return data.get("project", [])


def reconcile(mist_dirs, companion_dirs, entries):
    """Pure check that the committed manifest and live discovery agree, so a new,
    removed, duplicated or unclassified project cannot slip through unnoticed."""
    problems = []
    by_path = {}
    seen_names = set()
    for entry in entries:
        p = entry.get("path")
        if p in by_path:
            problems.append(f"duplicate manifest entry for {p}")
        by_path[p] = entry
        # A duplicate name collides in the results map and the per-project logs, so a
        # passing project could silently overwrite a failing one: reject it outright.
        name = entry.get("name")
        if name in seen_names:
            problems.append(f"duplicate manifest name {name!r}")
        seen_names.add(name)
        if entry.get("role") not in ROLES:
            problems.append(f"unclassified role {entry.get('role')!r} for {p}")

    mist_entries = {p for p, e in by_path.items() if ROLES.get(e.get("role"), {}).get("csproj") is not True}
    companion_entries = {p for p, e in by_path.items() if ROLES.get(e.get("role"), {}).get("csproj") is True}

    for p in sorted(set(mist_dirs) - mist_entries):
        problems.append(f"discovered mist project not in manifest: {p}")
    for p in sorted(mist_entries - set(mist_dirs)):
        problems.append(f"manifest mist project not found on disk: {p}")
    for p in sorted(set(companion_dirs) - companion_entries):
        problems.append(f"discovered C# companion project not in manifest: {p}")
    for p in sorted(companion_entries - set(companion_dirs)):
        problems.append(f"manifest C# companion not found/referenced on disk: {p}")

    return problems


def orchestrator_order(program_mist):
    """The RunDemo call order from AllFeaturesV2's Program.mist -- the order its
    baseline concatenates the composed samples in."""
    with open(program_mist, encoding="utf-8") as handle:
        return re.findall(r"(\w+)\.Program\.RunDemo\(\)", handle.read())


def check_aggregate(entries, expectations_dir, baseline, program_mist):
    """I/O wrapper: read the orchestrator order, the aggregate baseline and each
    composed sample's committed standalone expectation from disk, then hand the
    decision to the pure check_aggregate_core."""
    order = orchestrator_order(program_mist)
    with open(baseline, encoding="utf-8") as handle:
        baseline_text = handle.read()
    expectation_texts = {}
    for entry in entries:
        path = expectation_path(entry, expectations_dir)
        if path and os.path.exists(path):
            with open(path, encoding="utf-8") as handle:
                expectation_texts[entry["name"]] = handle.read()
    return check_aggregate_core(entries, order, baseline_text, expectation_texts)


def check_aggregate_core(entries, order, baseline_text, expectation_texts):
    """Review the standalone expectations against the aggregate: each composed
    sample's committed standalone output must appear as a contiguous block in the
    baseline, and the blocks must appear in orchestrator order. This ties the two
    together so an aggregate pass and a standalone pass cannot disagree. Samples
    composed only through the orchestrator (no standalone entry point) have no
    standalone output to compare, so they are noted and skipped rather than faked.
    Pure: the bounded self-test drives this with hand-built text, no files.
    Returns (ok, list-of-notes)."""
    by_name = {e["name"]: e for e in entries}
    want = normalize(baseline_text)

    cursor = 0
    checked = 0
    skipped = []
    problems = []
    for name in order:
        entry = by_name.get(name)
        if entry is None:
            problems.append(f"orchestrator calls {name}.RunDemo but it is not in the manifest")
            continue
        if entry["role"] == "composed-executable":
            skipped.append(f"{name} (composed-only; no standalone entry point, so no standalone output to cross-check)")
            continue
        if entry.get("aggregate_check") is False:
            skipped.append(f"{name} (standalone output differs from the orchestrator call by design; reviewed against its own program)")
            continue
        text = expectation_texts.get(name)
        if text is None:
            problems.append(f"no committed standalone expectation for composed sample {name}")
            continue
        block = normalize(text)
        if not block.strip():
            problems.append(f"empty standalone expectation for composed sample {name}")
            continue
        at = want.find(block, cursor)
        if at < 0:
            problems.append(f"{name}: standalone output is not a block of the baseline at/after offset {cursor}")
        else:
            cursor = at + len(block)
            checked += 1

    notes = [f"{checked} composed standalone expectations matched the baseline in orchestrator order"]
    if skipped:
        notes.append(f"{len(skipped)} composed samples not block-cross-checked against the aggregate:")
        notes.extend(f"    {s}" for s in skipped)
    notes.extend(problems)
    return not problems, notes


def evaluate(role, exit_code, stdout, stderr, expectation):
    """Decide a single runnable project's verdict from its captured result.
    Pure: the bounded self-test drives this with synthetic inputs."""
    mode = ROLES[role]["compare"]
    reasons = []
    diags = role_diagnostics(role, stdout, stderr)
    if diags:
        reasons.append(f"{len(diags)} compiler diagnostic(s): {diags[0].strip()}")
    if exit_code != 0:
        reasons.append(f"nonzero exit {exit_code}")

    out = normalize(stdout)
    if mode == "exact":
        if expectation is None:
            reasons.append("no committed expectation")
        elif not normalize(expectation).strip():
            # An empty exact expectation matches empty output and would then pass
            # forever, erasing the program's whole contract; a real executable has
            # output, so an empty required expectation is a defect, not a pass.
            reasons.append("empty expectation (no required output to verify)")
        elif out != normalize(expectation):
            reasons.append("stdout does not match expectation")
    elif mode == "contains":
        if expectation is None:
            reasons.append("no committed expectation")
        else:
            wants = [l for l in normalize(expectation).split("\n") if l.strip()]
            if not wants:
                # An empty marker file verifies nothing, so every output would pass.
                reasons.append("empty expectation (no required lines to verify)")
            for want in wants:
                if want not in out:
                    reasons.append(f"stdout missing expected line: {want!r}")
    elif mode == "test-tally":
        if expectation is None:
            reasons.append("no committed expectation")
        else:
            wants = [l for l in normalize(expectation).split("\n") if l.strip()]
            if not wants:
                reasons.append("empty expectation (no required tally to verify)")
            for want in wants:
                if not re.search(want, out):
                    reasons.append(f"stdout missing expected tally: {want!r}")

    return ("pass" if not reasons else "fail", reasons)


def capture_verdict(role, exit_code, stdout, stderr):
    """Pure: whether a --capture run may be recorded as the new expectation. Only a
    clean, nonempty run is recordable; a diagnostic (on any channel the role uses), a
    nonzero exit, or empty output is refused, so a degraded run never becomes a
    baseline and an empty capture never erases a program's contract. Returns
    (ok, captured_text, reasons); on refusal captured_text is not meant to be written.
    The bounded self-test drives this with synthetic inputs."""
    text = normalize(stdout)
    if role_diagnostics(role, stdout, stderr) or exit_code != 0:
        return False, text, evaluate(role, exit_code, stdout, stderr, text)[1]
    if not text.strip():
        return False, text, ["refused empty capture (no output to record)"]
    return True, text, []


# A selected project that neither passed nor was covered through a passing consumer
# leaves the verification incomplete, so these statuses deny a successful verdict.
BLOCKING_STATUSES = ("fail", "not-exercised", "skipped-prerequisite")


def unknown_selection(only, entries):
    """Pure: the --only names no manifest entry defines, so a typo cannot silently
    select nothing and be reported as a clean pass."""
    if not only:
        return []
    known = {e["name"] for e in entries}
    return sorted(n for n in only if n not in known)


def verdict(results, problems, agg_ok, capture):
    """Pure: the run's exit verdict. Success only when reconciliation and the
    aggregate cross-check hold and every selected project reached a covered pass. A
    failure, omitted coverage (not-exercised) or an unmet prerequisite
    (skipped-prerequisite) is not a successful complete verification, so an incomplete
    default or subset run cannot exit 0."""
    blocking = [r for r in results if r["status"] in BLOCKING_STATUSES]
    agg_fail = (not agg_ok) and not capture
    return 0 if (not blocking and not problems and not agg_fail) else 1


def expectation_path(entry, expectations_dir):
    exp = entry.get("expectation")
    if not exp:
        return None
    if os.path.isabs(exp):
        return exp
    if exp.startswith("samples/") or exp.startswith("tools/"):
        return os.path.join(REPO_ROOT, exp)
    return os.path.join(expectations_dir, exp)


def read_expectation(entry, expectations_dir):
    path = expectation_path(entry, expectations_dir)
    if path and os.path.exists(path):
        with open(path, encoding="utf-8") as handle:
            return handle.read()
    return None


def _resolve_token(token):
    # run_project changes cwd for consumer samples, so a path that was relative to the
    # invocation directory would otherwise resolve under the sample container and vanish.
    return os.path.abspath(token) if os.path.exists(token) else token


def compiler_argv(compiler):
    """A bare path to an existing executable is used verbatim (it may contain
    spaces); anything else is a command line split the shell's way. Either way any
    token that names an existing path is made absolute so the command keeps its
    meaning after run_project changes the working directory for a consumer sample."""
    if os.path.exists(compiler):
        return [os.path.abspath(compiler)]
    return [_resolve_token(t) for t in shlex.split(compiler)]


def run_project(entry, compiler, out_dir, timeout):
    role = ROLES[entry["role"]]
    verb = role["invocation"]
    name = entry["name"]
    project_dir = os.path.join(REPO_ROOT, entry["path"])
    work = os.path.join(out_dir, "scratch", name)
    os.makedirs(work, exist_ok=True)

    env = dict(os.environ)
    base = compiler_argv(compiler)
    env_overrides = {}
    if entry["role"] in ("msbuild-consumer", "ef-consumer"):
        # Documented consumer invocation: run the executable sub-project from the
        # container, letting the compiler build the C# companion. The nested
        # `mist build` the bridge runs is the compiler under test, not PATH's mist.
        cwd = os.path.dirname(project_dir)
        sub = os.path.basename(project_dir)
        env_overrides["MistCommand"] = shlex.join(base)
        env.update(env_overrides)
        argv = base + [verb, sub, "--quiet"]
    else:
        cwd = REPO_ROOT
        argv = base + [verb, entry["path"], "--quiet",
                       "--output", os.path.join(work, "out"),
                       "--bin", os.path.join(work, "bin")]

    # The exact command, working directory and any environment override, recorded
    # per project so the report captures how each sample was actually invoked.
    invocation = {"argv": argv, "cwd": os.path.relpath(cwd, REPO_ROOT), "env": env_overrides}
    try:
        proc = subprocess.run(argv, cwd=cwd, env=env, capture_output=True,
                              text=True, timeout=timeout)
        return proc.returncode, as_text(proc.stdout), as_text(proc.stderr), invocation
    except subprocess.TimeoutExpired as ex:
        return (124, as_text(ex.stdout), as_text(ex.stderr) + f"\n[harness] timed out after {timeout}s",
                invocation)
    except OSError as ex:
        # A launch failure (missing/unexecutable compiler) is this project's failing
        # result, not an exception that aborts the run and loses every other result.
        return 127, "", f"[harness] failed to launch: {ex}", invocation


def compiler_identity(compiler):
    for verb in ("version", "--version"):
        try:
            proc = subprocess.run(compiler_argv(compiler) + [verb], cwd=REPO_ROOT,
                                  capture_output=True, text=True, timeout=120)
            if proc.returncode == 0 and proc.stdout.strip():
                return proc.stdout.strip()
        # A bad compiler path/command fails here first (an existing but non-executable
        # file raises PermissionError, not FileNotFoundError); identity probing must
        # tolerate any launch error so the per-project launch-failure path still runs
        # and preserves a failing result and report.json instead of aborting the run.
        except (subprocess.TimeoutExpired, OSError):
            pass
    return "unknown"


def git(*args):
    try:
        return subprocess.run(["git", *args], cwd=REPO_ROOT, capture_output=True,
                              text=True, timeout=60).stdout.strip()
    except (subprocess.TimeoutExpired, FileNotFoundError):
        return "unknown"


def _dotnet(*args):
    try:
        proc = subprocess.run(["dotnet", *args], cwd=REPO_ROOT,
                              capture_output=True, text=True, timeout=120)
        return proc.stdout.strip()
    except (subprocess.TimeoutExpired, FileNotFoundError):
        return "unknown"


def provenance(compiler, generation):
    return {
        "generation": generation,
        "capturedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "mistRevision": git("rev-parse", "HEAD"),
        "stormPin": git("-C", "Source/Storm.SourceGenerator", "rev-parse", "HEAD"),
        "compilerCommand": compiler,
        "compilerIdentity": compiler_identity(compiler),
        "dotnetSdk": _dotnet("--version"),
        # The installed .NET runtimes and the NuGet environment the samples resolve
        # against, so a later generation's package/reference world is comparable.
        "dotnetRuntimes": _dotnet("--list-runtimes").split("\n"),
        "nugetGlobalPackages": _dotnet("nuget", "locals", "global-packages", "--list"),
        "runtimeIdentifier": _rid(),
        "os": platform.platform(),
    }


def _rid():
    system = {"Darwin": "osx", "Linux": "linux", "Windows": "win"}.get(platform.system(), platform.system())
    arch = {"arm64": "arm64", "aarch64": "arm64", "x86_64": "x64", "AMD64": "x64"}.get(platform.machine(), platform.machine())
    return f"{system}-{arch}"


def default_compiler():
    for rid in (_rid(),):
        for cfg in ("Release", "Debug"):
            cand = os.path.join(REPO_ROOT, "Source", "Mist.CLI", "bin", cfg, "net10.0", rid, "mist")
            if os.path.exists(cand):
                return cand
            if os.path.exists(cand + ".exe"):
                return cand + ".exe"
    return None


def main(argv):
    parser = argparse.ArgumentParser(description="Sample acceptance harness")
    parser.add_argument("--compiler", default=None)
    parser.add_argument("--generation", default="csharp-host")
    parser.add_argument("--manifest", default=os.path.join(SCRIPT_DIR, "manifest.toml"))
    parser.add_argument("--expectations", default=os.path.join(SCRIPT_DIR, "expectations"))
    parser.add_argument("--out", default=None)
    parser.add_argument("--only", default=None)
    parser.add_argument("--include-network", action="store_true")
    parser.add_argument("--capture", action="store_true")
    parser.add_argument("--list", action="store_true")
    parser.add_argument("--check-aggregate", action="store_true",
                        help="only verify the baseline equals the composed standalone expectations, then stop")
    parser.add_argument("--timeout", type=int, default=600)
    parser.add_argument("--jobs", type=int, default=4,
                        help="parallel sample runs (safe with a prebuilt binary: Standard is read-only, scratch is per-sample)")
    args = parser.parse_args(argv)

    entries = load_manifest(args.manifest)
    mist_dirs = discover_manifests(SAMPLES_ROOT)
    companion_dirs = discover_referenced_csproj(mist_dirs)
    problems = reconcile(mist_dirs, companion_dirs, entries)

    print(f"discovered: {len(mist_dirs)} mist projects, {len(companion_dirs)} C# companions")
    print(f"manifest:   {len(entries)} entries")
    if problems:
        print("RECONCILIATION FAILED:")
        for p in problems:
            print(f"  - {p}")
        if not args.list:
            return 2
    else:
        print("reconciliation: OK (manifest and discovery agree)")

    if args.list:
        for entry in entries:
            print(f"  {entry['role']:<18} {entry['name']:<28} {entry['path']}")
        return 0 if not problems else 2

    if args.check_aggregate:
        ok, notes = check_aggregate(
            entries, args.expectations,
            os.path.join(SAMPLES_ROOT, "__baselines__", "AllFeaturesV2.out"),
            os.path.join(SAMPLES_ROOT, "AllFeaturesV2", "Program.mist"))
        print("aggregate cross-check: " + ("OK" if ok else "MISMATCH"))
        for note in notes:
            print(f"  - {note}")
        return 0 if (ok and not problems) else 1

    compiler = args.compiler or default_compiler()
    if not compiler:
        print("ERROR: no --compiler given and no built mist binary found. "
              "Build the CLI (dotnet build Mist.slnx) or pass --compiler.", file=sys.stderr)
        return 2

    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    out_dir = args.out or os.path.join(SCRIPT_DIR, "_runs", stamp)
    os.makedirs(os.path.join(out_dir, "logs"), exist_ok=True)

    only = set(args.only.split(",")) if args.only else None
    unknown = unknown_selection(only, entries)
    if unknown:
        print("SELECTION FAILED: --only names not in the manifest: " + ", ".join(unknown),
              file=sys.stderr)
        return 2
    prov = provenance(compiler, args.generation)
    selected = [e for e in entries if not (only and e["name"] not in only)]

    def run_one(entry):
        name, role = entry["name"], entry["role"]
        exit_code, stdout, stderr, invocation = run_project(entry, compiler, out_dir, args.timeout)
        with open(os.path.join(out_dir, "logs", f"{name}.stdout"), "w", encoding="utf-8") as fh:
            fh.write(stdout)
        with open(os.path.join(out_dir, "logs", f"{name}.stderr"), "w", encoding="utf-8") as fh:
            fh.write(stderr)

        capturable = (args.capture and ROLES[role]["compare"] == "exact"
                      and os.path.abspath(expectation_path(entry, args.expectations))
                      .startswith(os.path.abspath(args.expectations) + os.sep))
        if capturable:
            # The recordable/refuse decision is the pure capture_verdict; only a clean,
            # nonempty run reaches the open(), so a degraded or empty run never
            # overwrites a committed expectation and the destination stays byte-identical.
            ok, captured_text, reasons = capture_verdict(role, exit_code, stdout, stderr)
            if not ok:
                status = "fail"
            else:
                dest = expectation_path(entry, args.expectations)
                os.makedirs(os.path.dirname(dest), exist_ok=True)
                with open(dest, "w", encoding="utf-8") as fh:
                    fh.write(captured_text)
                status, reasons = ("captured", [f"wrote {os.path.relpath(dest, REPO_ROOT)}"])
        else:
            status, reasons = evaluate(role, exit_code, stdout, stderr,
                                       read_expectation(entry, args.expectations))
        print(f"  [{status}] {name}", flush=True)
        return {"name": name, "role": role, "path": entry["path"],
                "exit": exit_code, "status": status, "reasons": reasons,
                "invocation": invocation}

    results = {}
    runnable = []
    supporting = []
    for entry in selected:
        spec = ROLES[entry["role"]]
        if not spec["runs"]:
            # Coverage is derived after the run from the consumer that references it,
            # never asserted up front, so a skipped or unselected consumer cannot
            # masquerade as having exercised the supporting project.
            supporting.append(entry)
        elif spec.get("network") and not args.include_network:
            results[entry["name"]] = {"name": entry["name"], "role": entry["role"], "path": entry["path"],
                                      "status": "skipped-prerequisite",
                                      "reasons": ["needs network and dotnet-ef; rerun with --include-network"]}
        else:
            runnable.append(entry)

    print(f"running {len(runnable)} projects with {args.jobs} job(s)...", flush=True)
    if args.jobs > 1:
        from concurrent.futures import ThreadPoolExecutor
        with ThreadPoolExecutor(max_workers=args.jobs) as pool:
            for res in pool.map(run_one, runnable):
                results[res["name"]] = res
    else:
        for entry in runnable:
            res = run_one(entry)
            results[res["name"]] = res

    consumers = consumer_map(entries)
    selected_names = {e["name"] for e in selected}
    for entry in supporting:
        who = consumers.get(entry["path"], [])
        # A captured consumer also ran cleanly (capture refuses a diagnostic/nonzero run),
        # so it exercises its support just as a passing compare does.
        passed = [c for c in who if results.get(c, {}).get("status") in ("pass", "captured")]
        if passed:
            status, reasons = "exercised-via-consumer", [f"exercised through {', '.join(passed)}"]
        else:
            # Name why coverage was not achieved rather than claiming it: no consumer,
            # the consumer was not selected, skipped a prerequisite, or itself failed.
            if not who:
                detail = "no consumer in the manifest references it"
            else:
                states = [f"{c}={results.get(c, {}).get('status', 'unselected')}" for c in who]
                detail = "consumer not exercised ({})".format(", ".join(states))
            status, reasons = "not-exercised", [detail]
        results[entry["name"]] = {"name": entry["name"], "role": entry["role"], "path": entry["path"],
                                  "status": status, "reasons": reasons}

    results = [results[e["name"]] for e in selected if e["name"] in results]
    agg_ok, agg_notes = check_aggregate(
        entries, args.expectations,
        os.path.join(SAMPLES_ROOT, "__baselines__", "AllFeaturesV2.out"),
        os.path.join(SAMPLES_ROOT, "AllFeaturesV2", "Program.mist"))
    blocking_names = [r["name"] for r in results if r["status"] in BLOCKING_STATUSES]
    report = {"provenance": prov, "reconciliation": problems or "ok",
              "aggregateCrossCheck": {"ok": agg_ok, "notes": agg_notes},
              "verification": {"complete": not blocking_names and not problems and (agg_ok or args.capture),
                               "incomplete": blocking_names},
              "results": results}
    with open(os.path.join(out_dir, "report.json"), "w", encoding="utf-8") as fh:
        json.dump(report, fh, indent=2)

    counts = {}
    for r in results:
        counts[r["status"]] = counts.get(r["status"], 0) + 1
    print(f"\ngeneration: {prov['generation']}  mist {prov['mistRevision'][:12]}  "
          f"{prov['runtimeIdentifier']}  sdk {prov['dotnetSdk']}")
    print("results: " + ", ".join(f"{k}={v}" for k, v in sorted(counts.items())))
    print("aggregate cross-check: " + ("OK" if agg_ok else "MISMATCH"))
    for note in agg_notes:
        print(f"  - {note}")
    for r in results:
        if r["status"] in ("fail", "captured", "skipped-prerequisite", "not-exercised"):
            for reason in r["reasons"]:
                print(f"  [{r['status']}] {r['name']}: {reason}")
    blocking = [r for r in results if r["status"] in BLOCKING_STATUSES]
    if blocking:
        print(f"verification INCOMPLETE: {len(blocking)} selected project(s) did not reach a "
              f"covered pass ({', '.join(r['name'] for r in blocking)})")
    print(f"\nreport: {os.path.join(out_dir, 'report.json')}")
    print(f"logs:   {os.path.join(out_dir, 'logs')}")

    return verdict(results, problems, agg_ok, args.capture)


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "selftest":
        sys.path.insert(0, SCRIPT_DIR)
        import selftest
        sys.exit(selftest.main())
    sys.exit(main(sys.argv[1:]))
