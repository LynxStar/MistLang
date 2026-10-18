# Backlog

Outstanding work lives in Carbonek project MIST. The root `AGENTS.md` states how to find, record
and close it. Nothing in this folder is a work queue.

The former ledger is frozen at `6d862f638e1b32cf0dc7bcd80e7d83b18617ebf7`, the revision Carbonek
imported. Each imported task keeps its ledger ID as its key, and its `sourceReference` names the
record's entry in the frozen file. Read that file with:

```bash
git show 6d862f638e1b32cf0dc7bcd80e7d83b18617ebf7:backlog/issues.yaml
```

The `source_refs` an imported task carries are provenance: each path is read from Git at the
revision it records, even when the file is gone from the checkout. The workstream packets that
assigned ledger records to worker sessions are under `backlog/workstreams/` at the frozen revision.

`design-notes/` holds the regions and lifetimes discussion, a pre-design note that is neither
requirements nor work.
