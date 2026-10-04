# Backlog

[issues.yaml](issues.yaml) is the single queue for outstanding repository work.
[The 2026-10-03 reconciliation](inventory-2026-10-03.md) is a readable dated snapshot,
including the method, counts and verification limits.

## Using the ledger

Each record has a stable ID, expected obligation, source selectors and evidence at an
identified revision. `confirmed` means current source or a probe supports a missing or
violated contract; read the evidence to see which. `unverified` requires confirmation or
attribution before choosing a fix. `decision` requires a language/product choice.
`optional` covers deferred features, refactors and performance explorations.

The archive retains completed, stale and superseded leads and explains their disposition.
Archived groups are not counts of the individual checklist bullets they replaced. Counts
under `reconciliation` describe the audited baseline, not later current status. Count
current open rows by status when planning another batch.

## Updating work

1. Search for the same expected behavior before adding an issue. Merge another source or
   reproduction into its existing ID; keep distinct obligations and unattributed incidents
   separate.
2. Record the revision, expected contract, owning code and precise evidence. Distinguish
   executed checks from source inspection, and old verification from current results.
3. The worker completes implementation and suitable verification, then moves the record to
   the archive with `status: implemented`, preserving its ID and adding closure evidence.
   Keep capability evidence current where representation or emission changes.
4. Record flaky-test sightings in [flaky-tests.yaml](flaky-tests.yaml), as repository rules
   require. Link unresolved follow-up work to one issue here; a green rerun alone does not
   explain or retire an unknown intermittent failure.

## Source documents

The 15 reconciled documents retain their detailed contracts, measurements and history.
Their old tasks and checkboxes are not independent queues; the ledger's source index and
each record's selectors show what was imported, merged or archived. No historical evidence
has been deleted to make the count smaller.

The older inventory and completed refusal migration are historical records. Empty
placeholders and the regions/lifetimes pre-design note were excluded. The owner confirms
the spec task documents are implemented; they are excluded from the work inventory.
Requirements/design and the grammar remain contracts. Workstream packets record assignments,
test-run ledgers record verification, and AllFeaturesV2 remains the sole sample stdout bank.
