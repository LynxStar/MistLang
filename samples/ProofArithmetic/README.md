# ProofArithmetic

Proof-sensitive integer arithmetic (exceptions Requirement 4): a division whose operands are
proven safe is a plain `int`, an unproven one is a `Result<int, DivisionError>`, and a statically
invalid one is refused at compile time. Result values compose through ordinary arithmetic, carrying
an error forward, and postfix `!` propagates a composed failure.

Provenance: V2-only. Written for the proof-sensitive arithmetic feature; its baseline lines were
captured from v2 and checked by hand.
