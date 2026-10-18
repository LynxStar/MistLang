# Agent contract

Read the root `CLAUDE.md` and the scoped `CLAUDE.md` of every directory you touch. They bind
every agent, Codex included.

## The record

Carbonek project `MIST` is the only record of obligations, evidence, flake observations and
execution. Nothing in this repository is a work queue: not `backlog/`, not the spec `tasks.md`
documents, not the test-run ledgers under `TestResults/`.

Task keys `MIST-NNN` and `ARCH-NNN` are the original ledger IDs, so a spec or code comment that
cites one names that task. Every imported task's `sourceReference` names its entry in the frozen
ledger at the import revision. `backlog/README.md` says how to read it. It is history, not edited.

## Kinds

Every Backlog task has a kind:

- **Confirmed**: current source or an executed probe shows the contract absent or violated.
- **Unverified**: a lead that needs confirmation or attribution. It becomes Confirmed only by
  `update_task` with `kind: Confirmed` and an Evidence-category entry that carries the proof.
- **Decision** and **Optional**: the owner's. They are never dispatched. No agent changes their
  kind or implements an invented policy to close one.

Promotion to Ready (`update_task` with `promote: true`) needs a Confirmed task with acceptance
criteria and delivery instructions.

## Finding work

Call `list_tasks` with `project: MIST` and the `status` and `kind` filters, for example
`status: [Backlog]` with `kind: [Confirmed]`. Its `text` filter searches titles, descriptions and
source references. `get_task` returns the body, revision and evidence entries. Read every entry
before acting: each says what was measured, at which revision, and its limits.

## Recording a flaky test

`Test/CLAUDE.md` rule 2 says when a sighting is owed and which fields it carries. Append it to the
matching task with `add_evidence`, category `Observation`. A sighting with no matching task goes in
your report, with the same fields, for the operator to file as Unverified.

## Closing work

Append an Evidence-category entry with `revision` (the commit that holds the change), `date`, `kind`
(for example `implementation and executed verification`) and `detail`. The detail states what
changed, the acceptance probes, the tests and their tiers, the ledger run path, the build and format
results, and the limits. Done comes from the Session outcome, never from an agent: Completed with a
summary and evidence finishes the task, and Blocked or Failed leaves it Blocked.

## New work

`create_tasks` makes Ready work with acceptance criteria and delivery instructions. It never makes
a Backlog record. Before calling it, check for a duplicate: `list_tasks` with `sourceReference` for
an exact source and `text` for the ID, symptom or path. Extend a match with `add_evidence` instead.
Give every new task a `sourceReference` that names its origin, so the next check finds it. A lead
that is not yet Confirmed goes in your report for the operator to file.
