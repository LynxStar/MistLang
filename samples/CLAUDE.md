
# Sample App Integrity Policy

## Rule 1: Never Delete or Modify Working Code in Other Samples

When implementing a new feature causes a pre-existing sample or test to fail, you MUST fix the root cause in the NEW code — never delete or modify the previously-working code in the other sample.

The ONLY permitted modifications to other sample apps are:

- Adding the new feature's `RunDemo()` call to `samples/AllFeatures/Program.mist` and its reference to `samples/AllFeatures/mist.toml`.
- Adding a sample's `RunDemo()` call to `samples/AllFeaturesV2/Program.mist` and its reference to `samples/AllFeaturesV2/mist.toml`, when that sample starts working on the experimental pipeline. This is how v2 progress is recorded (see `testing-philosophy.md`, Tier 6).

No other changes to any other sample are allowed.

**Forbidden:**
- Removing lines from other samples because the emitter broke them
- Commenting out previously-working code in unrelated samples
- Updating baselines to accept degraded output from other samples
- Modifying ANY file under `samples/` other than the new feature's own directory and the two orchestrators (`AllFeatures`, `AllFeaturesV2`)

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
2. **Prove type safety** — if the feature creates distinct types (like `FixedBuffer<5>` ≠ `FixedBuffer<6>`), demonstrate that the correct type is accepted and (if possible) show that mismatches are caught.
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

## Rule 4: Samples Are Integration Tests

Treat each sample as a proof that the feature works. The sample should fail to compile or produce wrong output if:
- The parser doesn't recognize the syntax
- The semantic model doesn't register the types correctly
- The type checker doesn't enforce the rules
- The emitter doesn't generate correct code

A sample that compiles and prints correct output regardless of whether the feature is implemented correctly is worthless.

## Rule 5: Samples Must Build With Zero Warnings and Zero Info Diagnostics

All samples under `samples/` MUST compile with zero errors, zero warnings, AND zero informational diagnostics (e.g., `info MIST_*` messages). Treat any diagnostic output from the Mist compiler as a defect.

If a sample triggers a diagnostic:
1. Determine whether the sample code is wrong or the compiler diagnostic is wrong.
2. If the sample is using a pattern that should be valid without diagnostics, fix the compiler — do not suppress or ignore the diagnostic.
3. If the sample genuinely has redundant or suboptimal code, fix the sample.

After any change to samples or to analyzer/diagnostic code, verify by building the affected sample AND `samples/AllFeatures` and confirming zero diagnostic output.
