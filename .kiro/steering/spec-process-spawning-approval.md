---
inclusion: always
---

# Spec Approval: Tests That Spawn Processes

## During Design and Task Planning

If a proposed design or task involves writing a test that will spawn an external process (e.g. `Process.Start`, `dotnet run`, `dotnet exec`), you MUST stop and ask the user for explicit approval before adding it to the design document or task list.

Present the following information when asking:

1. Which test class / task it applies to
2. How many times the process would be spawned per test run
3. Estimated time cost (process spawns × ~1.5s each)
4. Whether a batching or in-process alternative is feasible

Do not proceed with writing the design or task until the user approves.

**Example prompt to user:**

> Task 44 proposes a property-based test that compiles and runs a Mist program for each of 100 FsCheck iterations. This would spawn `dotnet` ~100 times per test run (~2.5 minutes). Should I redesign this as a batched test (1 compile, 1 run, 100 output lines), or do you want to approve the per-iteration approach?

## After Implementation

After implementing any test that spawns a process, you MUST ask the user to review it before marking the task complete. Include:

1. The test class name
2. How many process spawns occur per full test run
3. The measured wall-clock time for that test class (run it with `dotnet test --filter` and report the duration)

**Example summary to user:**

> `BootstrapNumericOperatorPropertyTests` is implemented. It spawns `dotnet` **1 time** per test run (batched). Measured duration: **1.9s**. Please review before I mark this task complete.

If the measured time exceeds **5 seconds** for a single test class, flag it explicitly and propose an optimization before asking for review.
