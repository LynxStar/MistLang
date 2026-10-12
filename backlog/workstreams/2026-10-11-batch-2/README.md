# Parallel batch 2 — 2026-10-11

Eight substantial assignments, packets 26–33, cover 32 existing canonical records. Numbering continues the previous batch and does not set execution order. Each full packet stands alone and owns implementation, verification, actual backlog updates, coherent commits and normal publication directly to remote main. Planning did not dispatch implementation workers.

Planning baseline: `0428515879b2d0245c996a5db98a676e441b9cbe`. Compiler probes and clean zero-warning/error solution build: `b1e556fb9d9528367f7de6bd7d71a13bd5c28aad`. The intervening changes are tracking-only reconciliations. Storm pin: `83410653c7d722868a5001d2ef71e6692ec012b3`. Prior 17–25 implementation/closeout has landed. No fresh full-suite pass is claimed; each packet identifies execution versus source evidence and requires current-main revalidation.

| Packet | Outcome | Model / effort |
| --- | --- | --- |
| [26](./26-qualified-type-and-domain-naming.md) | Qualified types and static type lookup | Sol/Opus / high |
| [27](./27-callable-declarations-and-member-discovery.md) | Callable declarations and member discovery | Sol/Opus / high |
| [28](./28-clr-callable-metadata-protocol-adaptation.md) | CLR call contracts and protocol adaptation | Sol/Opus / high |
| [29](./29-generic-context-associated-contract-lowering.md) | Generic context keys and associated contracts | Astra/Fable / high |
| [30](./30-structural-pattern-proof-runtime-parity.md) | Type-pattern proofs and runtime representation | Sol/Opus / high |
| [31](./31-reference-stack-lifetime-safety.md) | Ref and stack-storage lifetime safety | Sol/Opus / high |
| [32](./32-owned-field-state-provenance.md) | Owned fields and receiver capabilities | Astra/Fable / high |
| [33](./33-source-statement-and-initializer-validation.md) | Statement and initialization validity | Sol/Opus / high |

Generic context-key and weaker-answer witness forwarding share emitted signatures, so packet 29 owns their complete representation together with associated-slot lifting. CLR callable/protocol packet 28 privately consumes existing inheritance APIs and preserves that signature contract. Pattern, lifetime, ownership, callable lookup, type naming and statement/initializer judgments use existing bound identities; none requires another packet’s unpublished branch, helper or decision. Narrow shared-file and canonical-ledger conflicts belong to normal integration within each worker.

Models are the portable user categories. Most work recommends Sol/Opus at high effort; 29 and 32 recommend Astra/Fable for demonstrated hidden-signature substitution and composite per-field flow/state reasoning, respectively. Escalation remains optional within the same task.

Decisions remain open: frozen-lease mutable borrowing, unknown positional tuple admission, complete tuple-switch policy, ambiguous literal entry into refined unions, and unspecified ref/ownership forms. Other slicing work, deferred feature expansions and unreproduced metadata/inference leads are deferred rather than used to fill packets. Each packet names its finite scope and evidence limits.

The canonical queue remains `backlog/issues.yaml`. This folder contains assignments, not another ledger. Audit corrections landed in `3e53bdef` and `04285158`: four stale leads were archived with evidence, frozen-lease and unknown tuple-policy conflicts retained, return contracts narrowed correctly, and native Object conversion confirmed. Stable IDs and provenance maps were preserved; no obsolete ledger was restored. Start any packet from current remote main and reserve any newer active scope reported after planning.
