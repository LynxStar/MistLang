
# Shell Command Policy

## No Inline Scripting — Chained Commands Only

Commands MUST be plain invocations, optionally chained or piped. They MUST NOT be
PowerShell scripts written on one line.

The reason is reviewability. A command the user cannot read at a glance cannot be
approved, so an inline script blocks the whole turn regardless of whether it would have
worked.

**Forbidden:**

- Variable assignment: `$out = dotnet run ...`
- Subexpressions: `$($section.Count)`, `$(git config core.autocrlf)`
- Property or method access on command output: `.Count`, `.Hash`, `.LineNumber`
- Control flow: `if`, `foreach`, `for`, `while`
- Comparison logic: `Compare-Object`, `Get-FileHash` piped into a conditional
- `$LASTEXITCODE` plumbing — the tool already reports the exit code
- Stopwatch or timing wrappers

```powershell
# BAD - unreviewable inline script
$out = dotnet run --project Source/Mist.CLI -- run samples/X 2>&1; $code = $LASTEXITCODE; Write-Output "EXIT=$code lines=$($out.Count)"; $out
```

```powershell
# BAD - embedded loop and comparison
$baseline = Get-Content file.out; $start = ($baseline | Select-String -Pattern '^=== X ===$').LineNumber - 1; for ($i = $start; ...) { ... }; Compare-Object $section $actual
```

**Allowed:** a plain command, with at most simple pipes to a filter using a literal
pattern.

```powershell
# GOOD
dotnet build Mist.slnx -v minimal

# GOOD - single pipe, literal pattern
dotnet test Mist.slnx --no-build | Select-String 'Passed|Failed'

# GOOD - chained, still readable
dotnet build Mist.slnx ; dotnet format Mist.slnx --verify-no-changes
```

### Why This Bites Harder on Windows

This workspace runs **pwsh on Windows**. The failure is specific to that, and it has a
cause worth understanding rather than just a rule to follow.

On bash there are terse primitives that chain — `grep`, `diff`, `head`, `wc -l` — so a
one-liner never needs a variable. On Windows those do not exist. Wanting to diff command
output against a file leads to `Compare-Object`, which needs two operands held somewhere,
so two variables get introduced, and the script grows from there. PowerShell also hands
over an object pipeline with `.Count` and `.Hash` on tap, which makes the slide easy.

The fix is not a cleverer PowerShell one-liner. It is to stop doing comparison and
extraction in the shell at all.

### Do the Logic in the Agent, Not the Shell

When a task needs comparison, extraction, or arithmetic, run the plain command and read
the file, then reason over the results directly. Do not push that work into the shell.

| Need | Do this instead |
|---|---|
| Compare program output to a baseline | Run the program plain; read the baseline with the file-reading tool; compare the two yourself |
| Extract one section from a baseline | Read the file and locate the section yourself |
| Check an exit code | Run the command plain; the tool result reports it |
| Count lines | Read the output and count |
| Confirm two files are identical | Read both, or use `git status` / `git diff --stat` |

## No `cd` in Commands

NEVER prefix a shell command with `cd <directory> &&`. The `execute_bash` tool has a `cwd` parameter — use that instead. The `cd` command is not supported by the tool and will fail or cause confusion.

**Bad:**
```bash
cd /path/to/project && dotnet build
```

**Good — use the `cwd` parameter on the tool call:**
```bash
dotnet build
```
with `cwd` set to `/path/to/project`.

This applies to ALL shell commands, including those dispatched to sub-agents. When delegating tasks to sub-agents, explicitly instruct them to use the `cwd` parameter rather than `cd`.

## No Redundant Directory Prefixes

If the target directory IS the current working directory, do not specify it at all. Just run the command directly.

## Grep with Special Characters

> The rest of this file assumes a bash/zsh shell. On Windows the shell is pwsh, where
> `grep` does not exist — use `Select-String` with a single-quoted pattern instead, and
> the `!` hazard below does not apply.

When using `grep` in shell commands, NEVER use unescaped `!` inside double-quoted patterns. The `!` character triggers zsh history expansion inside double quotes and causes `zsh: event not found` errors.

**Bad:**
```bash
grep -E "(Failed!|Passed!)"
```

**Good — use single quotes:**
```bash
grep -E '(Failed|Passed)'
```

**Good — escape the exclamation:**
```bash
grep -E "(Failed\!|Passed\!)"
```

**Preferred approach:** Use single quotes for all grep/sed patterns that contain special characters.

## Test Output Filtering

On bash/zsh, when filtering `dotnet test` output for pass/fail summaries, use:
```bash
grep -E 'Passed|Failed'
```

For test counts specifically:
```bash
grep -E 'Passed:|Failed:'
```

On Windows (pwsh), `grep` is unavailable. Use a single pipe to `Select-String` with a
single-quoted pattern:
```powershell
dotnet test Mist.slnx --no-build | Select-String 'Passed|Failed'
```

For build output:
```powershell
dotnet build Mist.slnx -v minimal | Select-String 'Warning|Error'
```

Do not wrap either of these in variables or append `.Count`.
