# Flow-Sensitive Refinement Inference

## Scope

This draft develops the claims model proposed during the Array growth discussion.
At each program point, the compiler knows facts about the current values of
expressions and storage locations. It uses those facts, along with declared type
and generic constraints, to prove the contracts required by an operation.

Mist already has [refinement types](../refinement-types/requirements.md),
[ad hoc constraints](../adhoc-refinement-constraints/requirements.md),
[implication checks](../refinement-comparison-engine/requirements.md), and
[array bounds flow analysis](../array-bounds-flow-analysis/requirements.md).
The [current flow pass](../../Source/Mist/ProgramAnalysis/Rules/RefinementFlow.cs)
tracks intervals, literal values, and relationships to array lengths. This draft
extends the facts the pass can infer and makes those facts available to contract
checking. The growth example needs a relation between the values of two expressions.

## Inferring and updating claims

A claim is a proposition the compiler can establish at a program point. For a
mutable local, it describes the current value, while the local's type governs
future writes. For example:

```mist
var x = 7;
RequiresSeven(x);
x = RandomInt();
RequiresSeven(x); // Compile-time error if RandomInt only promises int.
```

The initializer establishes `x == 7`. The assignment replaces the facts about
the current `x` with facts established by `RandomInt`'s return type and contract.
An explicit refined declaration would still require every write to satisfy its
declared refinement.

- Infer facts from typed expressions, declared refinements, and conditions on the
  current control-flow path. Support equality and order between integral values
  and, for the growth proof, addition, subtraction, and multiplication by a constant.
- Relate facts to the values actually read. A fact about the length of an old
  array remains a fact about that array after its field is replaced. It cannot
  establish a bound on the replacement array's length.
- At a join, keep facts supported on every incoming path. Invalidate facts about
  a storage location when a write or a call may change the value they describe.
  Reuse the existing analysis of writes and method effects where it supplies that proof.
- Use the facts at assignments, arguments, results, and generic constraints that
  require static proof. Bind the contract's parameters to the actual values and
  type arguments before checking implication. In particular, the factory's source
  and destination length parameters must refer to the values used by that call.
- Reject a contract requiring static proof when it cannot be established. A fact
  the solver cannot express or prove grants no permission.

The solver's initial arithmetic rules must honor the selected intrinsic operator's
semantics and integer width. To reason about a wrapping operation using an ordinary
integer equation, the checker must first establish that the operation stays in
range. For a user-defined operator, use the facts its declared contract establishes.

## Array growth proof

The Array operation preserves every source slot and requires the destination to be
at least as large. Its required relation is `source.Length <= destination capacity`.
The backing field's lease and the ownership of the stored elements follow the
[Array growth contract](../generic-discipline-and-borrow-contracts/field-replacement-proposal.md).

The intended use inside `UserList<T>` is:

```mist
var oldSize = backing.Length;
var newSize = oldSize * 2;
backing = new Array<T?, newSize>(backing);
```

This example assumes the operation can prove the following facts at the call:

```text
oldSize == length of the source array passed to this call
0 <= oldSize <= Int32.MaxValue / 2
newSize == 2 * oldSize
```

Those facts imply `oldSize <= newSize` and a representable destination capacity.
`ZeroOrPositiveInt` supplies `oldSize >= 0`. A strict increase requires `oldSize > 0`.
The list's first allocation and its behavior at the maximum capacity are separate
from the proof that doubling a suitable existing capacity is safe.

The compiler must carry the runtime value of `newSize` into the destination length
used by the operation. A static factory can bind a named source length through a
method parameter list. That changes how the call is written. The same capacity
proof is required when either length is only known at runtime.

## Acceptance cases

| Available facts | Expected result |
|---|---|
| `var x = 7` before a call requiring `x == 7` | Prove the argument refinement. |
| `x = RandomInt()` with an `int` result and no stronger contract | Discard the old equality for the current `x`. |
| The source length is nonnegative and at most `Int32.MaxValue / 2` | Prove that doubling it gives a capacity that can hold the source. |
| The source length is zero | Prove that the doubled capacity fits. A contract requiring a strict increase fails. |
| The source length is only known to be nonnegative | A doubling expression cannot establish the required capacity contract. |
| The backing field is replaced after calculating the new size | Require a bound on the actual source passed to the operation. |

This draft is a prerequisite for guard-free runtime growth from inferred arithmetic
facts. Its first slice can be delivered independently of the broader Array and
ownership designs. The solver representation, proof limits, and spelling of runtime
dependent arguments remain design choices.
