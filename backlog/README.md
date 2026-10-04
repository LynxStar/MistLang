# Backlog

[issues.yaml](issues.yaml) is the only work ledger. Add, prioritize, assign and close work there.
The former ledgers, dated inventory tables, completed migration checklist and empty placeholders
have been deleted. Git retains the original documents at the revisions recorded in the ledger.

## Using the ledger

Each record has a stable ID, expected obligation and evidence at an identified revision.
`confirmed` means current source or an executed probe supports a missing or violated contract;
read the evidence to see which. `unverified` needs confirmation or attribution. `decision`
requires a language/product choice. `optional` covers deferred features, refactors and performance
explorations. The archive retains completed, stale and superseded leads with their disposition.

Counts under `reconciliation` describe the 2026-10-03 audit, not later current status.
Count the open `issues` by status when planning work. Preserve IDs when moving closed records
to `archive`, and add implementation, revision and verification evidence.

## Evidence and observations

Record intermittent test sightings in `observations` on the matching issue, as `Test/CLAUDE.md`
requires. Keep failing and passing run evidence, test names, discovery dates, symptoms and
causes; use `unknown` when attribution is unknown. A green rerun does not close an unexplained
failure. The 15 imported flake reports retain their complete evidence on the existing issue or
archive record; `status_at_import` is their old observation label, not current work status.

`source_refs`, `sources` and the coverage maps are historical provenance. An explicit `revision`
means the path is read from Git at that revision, even when the file no longer exists in the
checkout. These entries do not point to another list to update. The clean-room restriction on
classic implementation still applies.

Requirements, designs and the grammar remain contracts. The owner confirms spec task documents
are implemented; do not harvest them as another backlog. Workstream packets are assignments,
test-run ledgers are verification, and AllFeaturesV2 remains the sole sample stdout bank.
The regions/lifetimes discussion in `design-notes/` is an excluded pre-design note, not requirements
or an additional work queue.
