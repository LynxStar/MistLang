
# Shell Command Policy

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

When filtering `dotnet test` output for pass/fail summaries, use:
```bash
grep -E 'Passed|Failed'
```

For test counts specifically:
```bash
grep -E 'Passed:|Failed:'
```
