---
inclusion: always
---

# Shell Command Policy

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
