
# Sample App Integrity Policy

## Baseline provenance

`samples/__baselines__/AllFeaturesV2.out` retains output captured from the retired classic
compiler where that compiler could run the sample. The following exceptions were captured
from v2 (the current standard pipeline) and reviewed against the program by hand. They are
recorded as V2-ONLY provenance, not as output independently produced by another compiler.
This records baseline origins, not current feature status; orchestrator membership remains
the record of which samples run.

| Sample or section | Reason for v2 provenance |
|---|---|
| `NestedBlockNamespaces` | Classic could not resolve the nested namespace paths. |
| `NamedAllocationElements` | Classic refused the relative qualified element types and aliases. |
| `StampableTypes` | The stampable modifier and constraint were implemented only in v2. |
| `Purity` | Classic lacked Standard's `List<T>` and local-function binding. |
| `DependentTypes`, sections 16–19 | Added 2026-10-03 and 2026-10-11; classic lacked projected value properties, the upper `ulong` range, widening forwarding and an alias's own dependent projection. |
| `ClrRefReadonly` | Classic hid CLR members returning `ref readonly`. |
| `LocalFunctions` | Classic could not compile local functions. |
| `OperatorOverloads` | Classic parsed operator declarations but did not emit them. |
| `StandardSurface` | Classic refused awaiting a resultless task. |
| `InheritedGenericMembers` | Classic left an inherited generic method's result unsubstituted. |
| `NestedGenericTypes` | Classic could not emit nested types under an enclosing instantiation. |
| `GenericAbstractMembers` | Classic emitted generic abstract defaults with an out-of-scope type parameter. |
| `NewtypeClones` | The sample uses a later newtypes contract than classic implemented. |
| `OverloadedIndexers` | Classic selected the first declared indexer rather than resolving the overload. |
| `GenericOptionals` | Classic refused optional field assignment and misrepresented generic optional storage. |
| `OpenUnions` | Classic refused the combined open-union, optional, inherited-generic and newtype shapes. |
| `CallableInference` | Classic refused method-group arguments and candidate-targeted lambdas. |
| `GenericExtensionMethods` | Classic could not resolve generic extension-block parameters. |
| `ConstructorConstraintSignatures` | Classic could not resolve a type parameter in a constructor constraint. |
| `VariadicSpecialization` | Written 2026-10-03, after classic was retired. |
| `VariadicSpecialization`, `ResidualShapes` section | V2-ONLY: added 2026-10-19 for nested and extension method packs (including generic receivers and forwarded packs), pack type values and variadic method groups; captured from v2 and read against the program by hand. |
| `SizedSequencePatterns` | Written 2026-10-03, after classic was retired. |
| `ResourceTransfer` | Written 2026-10-11, after classic was retired. |
| `ClrCallContracts` | Written 2026-10-11, after classic was retired. |
| `ClosedOperators` | Written 2026-10-13, after classic was retired. |
| `SliceableTypes` | Written 2026-10-14, after classic was retired. |
| `GenericSelfReceivers` | Written 2026-10-15, after classic was retired. |
| `StructuredContext`, section 19 | Added 2026-10-11, after classic was retired; classic erased a generic clause's key to its CLR type. |
| `Variadics`, `RefinementTypes`, `DropSemantics`, `DelegateFuncConversion`, `OptionalDefaultability`, `DefaultKeyword` and `VoidType` (`HoistingDemo`), the sections added 2026-10-12 | Bank the claims of the retired compile-and-run tests; captured from v2 and read against the programs by hand. |
| `Variadics` (`emptyPackChain`), the section added 2026-10-15 | Classic never ran an omitted chain to a pack-only base closed empty; captured from v2 and read against the program by hand. |
| `DefaultKeyword` (custom setter), the section added 2026-10-15 | Banks a default writing backing storage past a bodied setter, written after classic was retired; captured from v2 and read against the program by hand. |
| `FirstClassFunctions`, `CallableInference`, `StructuredConcurrency`, `Primitives` and `StructInheritance`, the sections added 2026-10-20 | Bank MIST-1022's expression lowerings (a discarded lambda value, void lambdas and groups at an inferred-void result, an awaited async void value, wrapping constant arithmetic, a brace in interpolated text, a base-struct operator on derived operands), written after classic was retired; captured from v2 and read against the programs by hand. |

`OptionalTypeParameters` is not V2-ONLY: its existing sample lines were cross-checked against
classic even though it is composed only into `AllFeaturesV2`. Nor is the old
`EnumReadonlyRestriction` divergence an accepted exception: printing `TopLeft` for every
value-equal `Corner` variant was a defect and was corrected. The baseline prints each
variant's own name.

## Rule 1: Never Delete or Modify Working Code in Other Samples

When implementing a new feature causes a pre-existing sample or test to fail, you MUST fix the root cause in the NEW code — never delete or modify the previously-working code in the other sample.

The ONLY permitted modification to other sample apps is adding a sample to the orchestrator, `samples/AllFeaturesV2`: its reference in `samples/AllFeaturesV2/mist.toml`, its `RunDemo()` call in `samples/AllFeaturesV2/Program.mist`, and its output lines in `samples/__baselines__/AllFeaturesV2.out`. This is how progress is recorded (see the testing philosophy, `.claude/skills/testing-philosophy/SKILL.md`, Tier 6).

No other changes to any other sample are allowed.

**Forbidden:**
- Removing lines from other samples because the emitter broke them
- Commenting out previously-working code in unrelated samples
- Updating baselines to accept degraded output from other samples
- Modifying ANY file under `samples/` other than the new feature's own directory and the orchestrator (`AllFeaturesV2`)

**Required:**
- Diagnose WHY the new feature broke the existing sample
- Fix the bug in the compiler/emitter that caused the regression
- If the bug cannot be fixed within the current task scope, stop and ask the user
- Never touch other samples — the fix must be in the compiler code, not in the victim sample

## Rule 2: Samples Must Actually Exercise the Feature, Not Just Declare It

Every sample MUST prove the feature works end-to-end by **using** it in a meaningful way that would fail if the feature were broken. Declaring a type with the new syntax and printing a hardcoded string is NOT exercising the feature.

**Bad — declares but never uses:**
```mist
class FixedBuffer<Size:int> {
    public string Describe() { return "FixedBuffer"; }  // Size is never used
}
```

**Good — actually exercises the feature:**
```mist
class FixedBuffer<Size:int> {
    public static int GetSize() { return Size; }
}

// Proves the dependent value is accessible and correct:
Console.WriteLine($"FixedBuffer<5>.Size = {FixedBuffer<5>.Size}");

// Proves type identity — passing a FixedBuffer<5> where one is expected:
public static void AcceptBuffer(FixedBuffer<5> buf) { ... }
var b = new FixedBuffer<5>();
AcceptBuffer(b);  // compiles — same dependent value
```

### What "exercising" means concretely:

1. **Use the feature's value** — if the feature introduces a value (like a dependent type parameter), read it, print it, pass it around. Don't just declare it and ignore it.
2. **Prove type safety** — if the feature creates distinct types (like `FixedBuffer<5>` ≠ `FixedBuffer<6>`), demonstrate that the correct type is accepted. A sample cannot show a mismatch being caught (Rule 4).
3. **Call through the feature** — if the feature generates properties/methods, call them and print their results.
4. **Pass values through functions** — show that a function accepting the feature's type works when given the right argument.
5. **Chain features** — show the feature interacting with other language features (inheritance, pattern matching, generics, etc.) in ways that would break if the implementation were wrong.

### The "would it still pass if the feature were deleted?" test:

For every line in the sample, ask: "Would this line still compile and produce the same output if I removed the feature implementation?" If yes, the line is not exercising the feature — it's just decoration.

## Rule 3: If a Feature Aspect Cannot Be Demonstrated, Say Why

If a specific aspect of the feature cannot be exercised in the sample due to a known compiler limitation:

1. Add a comment explaining the limitation
2. Show the code that WOULD exercise it (commented out)
3. But the REST of the sample must still fully exercise everything that DOES work
4. Report the limitation to the user — do not silently skip it

## Rule 4: Samples Are System Tests

A sample is Tier 5, and through `AllFeaturesV2` Tier 6, in the testing philosophy (`.claude/skills/testing-philosophy/SKILL.md`). It proves that a valid program compiles and prints the right output, so it should fail to compile or produce wrong output if:
- The parser doesn't recognize the syntax
- The program model doesn't register the types correctly
- The emitter doesn't generate correct code

It cannot prove a refusal. Every sample is a valid program, so a checker that enforces nothing passes it. What a rule refuses is proven at Tier 1–2 beside the rule, never with a sample or a program compiled to fail.

A sample that compiles and prints correct output regardless of whether the feature is implemented correctly is worthless.

## Rule 5: Samples Must Build With Zero Warnings and Zero Info Diagnostics

All samples under `samples/` MUST compile with zero errors, zero warnings, AND zero informational diagnostics (e.g., `info MIST_*` messages). Treat any diagnostic output from the Mist compiler as a defect.

If a sample triggers a diagnostic:
1. Determine whether the sample code is wrong or the compiler diagnostic is wrong.
2. If the sample is using a pattern that should be valid without diagnostics, fix the compiler — do not suppress or ignore the diagnostic.
3. If the sample genuinely has redundant or suboptimal code, fix the sample.

After any change to samples or to analyzer/diagnostic code, verify by building the affected sample AND `samples/AllFeaturesV2` and confirming zero diagnostic output.
