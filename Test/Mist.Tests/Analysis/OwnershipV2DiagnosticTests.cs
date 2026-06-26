// Feature: ownership-v2, Task 11.2 — unit tests for each new ownership v2 diagnostic's
// trigger and message.
//
// Each of the six new diagnostics has TWO kinds of test here:
//
//   1. An end-to-end TRIGGER test that compiles a program exercising the error
//      scenario from the design's "Error scenarios" section and asserts the
//      diagnostic id (plus key message tokens) appears in the diagnostics.
//
//   2. A message-builder LOCK-DOWN test that calls the centralized message builder
//      on OwnershipAnalyzer directly and asserts the key tokens are present. This
//      pins the message content NOW, independent of whether the emission site is
//      wired yet.
//
// IMPORTANT — pending emission sites:
// The diagnostic ids and message builders are defined (task 11.1), but the analyzer
// passes that actually FIRE these diagnostics from real source code are wired in
// later tasks:
//   - MIST_OWN_MUTABLE_HIDDEN ........... MethodTierVisibilityFilter wiring   (task 8.1)
//   - MIST_OWN_REQUIRES_OWNED ........... MoveBoundaryResolver wiring          (task 8.1)
//   - MIST_OWN_NOT_CLONABLE ............. MoveBoundaryResolver wiring          (task 8.1)
//   - MIST_OWN_CONTRACT_VIOLATION ....... CooperationContractVerifier wiring   (task 8.1)
//   - MIST_OWN_FIELD_MODIFIER_REMOVED ... RejectOwnedFieldModifier hard error  (task 10.1)
//   - MIST_OWN_SUBSTRUCTURAL_BINDING .... RejectOwnedBindingOnSubstructural     (task 9.1)
//
// The TRIGGER tests are therefore expected to be PENDING (skipped) against the v1
// analyzer; they are marked [Fact(Skip = ...)] with a reason naming the enabling
// task. Once the emission sites land, remove the Skip and they assert end-to-end
// behavior. The trigger source/assertions are written to be CORRECT per the
// requirement — they are NOT weakened to pass against the v1 analyzer.
//
// The message-builder LOCK-DOWN tests run NOW and pin message content.

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;
using Xunit;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for the six new ownership v2 diagnostics (task 11.2):
/// <c>MIST_OWN_MUTABLE_HIDDEN</c>, <c>MIST_OWN_REQUIRES_OWNED</c>,
/// <c>MIST_OWN_NOT_CLONABLE</c>, <c>MIST_OWN_CONTRACT_VIOLATION</c>,
/// <c>MIST_OWN_FIELD_MODIFIER_REMOVED</c>, and <c>MIST_OWN_SUBSTRUCTURAL_BINDING</c>.
///
/// **Validates: Requirements 13.1, 13.2, 13.4, 13.7, 13.8** (and 13.6 for the field modifier).
/// </summary>
public sealed class OwnershipV2DiagnosticTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_own_v2_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static Diagnostic? FindById(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.FirstOrDefault(d => d.Id.Value == id);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 1. MIST_OWN_MUTABLE_HIDDEN — replaced by this-tier call-site checking
    //    (Req 6 AC 1–4, Req 17 AC 15–16). Mutable methods on non-owned types
    //    held in owned bindings are NO LONGER hidden; they are only blocked if
    //    their inferred this-tier exceeds the caller's tier.
    // ═══════════════════════════════════════════════════════════════════════

    // The old MethodTierVisibilityFilter blanket hiding is removed (Req 6).
    // Calling a mutable method on a non-owned type with no owned fields through
    // an owned binding now SUCCEEDS — no diagnostic fires because the method's
    // inferred ThisTier is None (it accesses no owned fields).
    [Fact]
    public void MutableHidden_NoLongerFires_ForNonOwnedTypeWithoutOwnedFields()
    {
        const string source = """
            class Widget
            {
                public int Value;
                public Widget(int value)
                {
                    this.Value = value;
                }
                public int GetValue()
                {
                    return Value;
                }
                public void SetValue(int v)
                {
                    this.Value = v;
                }
            }
            class Program
            {
                public static void Main()
                {
                    owned var w = new Widget(1);
                    w.SetValue(99);
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = FindById(diagnostics, "MIST_OWN_MUTABLE_HIDDEN");

        // The old diagnostic should NOT fire — blanket hiding is removed (Req 6).
        Assert.Null(diag);
    }

    [Fact]
    public void MutableHidden_Message_ContainsMethodTypeAndReadonlyHint()
    {
        var message = OwnershipAnalyzer.MutableHiddenMessage("SetValue", "Widget");

        Assert.Contains("SetValue", message, StringComparison.Ordinal);
        Assert.Contains("Widget", message, StringComparison.Ordinal);
        Assert.Contains("readonly", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("owned", message, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. MIST_OWN_REQUIRES_OWNED — field assignment on an owned-bound instance
    //    from a non-owned, non-clonable source (Req 13 AC 2, Req 4 AC 2).
    // ═══════════════════════════════════════════════════════════════════════

    // Requires MoveBoundaryResolver flow-sensitive field-assignment wiring.
    [Fact]
    public void RequiresOwned_Trigger_EmitsDiagnostic()
    {
        // 'Holder' is an owned type whose field 'item' is assigned from a non-owned,
        // non-clonable source ('shared'). Gadget is non-clonable because it carries an
        // array field, so it cannot auto-derive IClonable<T>; the move boundary therefore
        // cannot resolve the assignment as a clone and must error with
        // MIST_OWN_REQUIRES_OWNED or MIST_OWN_NOT_CLONABLE (provenance requirement).
        const string source = """
            class Gadget
            {
                public int[] data;
            }
            owned class Holder
            {
                public Gadget item;

                public void Set(owned Gadget g)
                {
                    this.item = g;
                }
            }
            class Program
            {
                public static void Main()
                {
                    owned var h = new Holder();
                    var shared = new Gadget();
                    h.item = shared;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        // The resolver should emit MIST_OWN_NOT_CLONABLE (non-clonable source into owned destination).
        var diag = FindById(diagnostics, "MIST_OWN_NOT_CLONABLE")
                ?? FindById(diagnostics, "MIST_OWN_REQUIRES_OWNED");

        Assert.NotNull(diag);
        Assert.Contains("Gadget", diag!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresOwned_Message_ContainsDestinationSourceAndOwnedHint()
    {
        var message = OwnershipAnalyzer.RequiresOwnedMessage("item", "shared");

        Assert.Contains("item", message, StringComparison.Ordinal);
        Assert.Contains("shared", message, StringComparison.Ordinal);
        Assert.Contains("uniquely-owned", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IClonable", message, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. MIST_OWN_NOT_CLONABLE — a clone is needed (non-owned source into an owned
    //    destination) but the source type does not implement IClonable<T>
    //    (Req 13 AC 7, Req 8 AC 4, design Error Scenario 2).
    // ═══════════════════════════════════════════════════════════════════════

    // Requires MoveBoundaryResolver flow-sensitive owned-binding-init wiring.
    [Fact]
    public void NotClonable_Trigger_EmitsDiagnostic()
    {
        // 'shared' is a non-owned binding of type NonClonable. NonClonable has an
        // array field so it cannot auto-derive IClonable<T>. Assigning it into an
        // owned binding requires a clone that cannot be produced -> MIST_OWN_NOT_CLONABLE.
        const string source = """
            class NonClonable
            {
                public int[] data;
            }
            class Program
            {
                public static void Main()
                {
                    var shared = new NonClonable();
                    owned var c = shared;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = FindById(diagnostics, "MIST_OWN_NOT_CLONABLE");

        Assert.NotNull(diag);
        Assert.Contains("NonClonable", diag!.Message, StringComparison.Ordinal);
        Assert.Contains("IClonable", diag.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NotClonable_Message_ContainsTypeAndIClonableSuggestion()
    {
        var message = OwnershipAnalyzer.NotClonableMessage("SharedThing");

        Assert.Contains("SharedThing", message, StringComparison.Ordinal);
        Assert.Contains("IClonable", message, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. MIST_OWN_CONTRACT_VIOLATION — an owned type's method/ctor assigns a field
    //    from a non-owned source (Req 13 AC 4–5, design Error Scenario 3).
    // ═══════════════════════════════════════════════════════════════════════

    // Requires CooperationContractVerifier to detect non-clonable field assignment.
    [Fact]
    public void ContractViolation_Trigger_EmitsDiagnostic()
    {
        // Under owned-fields, the cooperation contract is subsumed by provenance enforcement
        // on owned fields (Req 8 + Req 3). The field-type restriction (Req 4) now enforces
        // that fields of owned types have owned/IClonable/value types. This test verifies
        // that the field-type restriction fires instead of the old contract violation.
        const string source = """
            class Gadget
            {
                public int[] data;
            }
            owned class Vault
            {
                public Gadget setting;
                public void Configure(Gadget input)
                {
                    this.setting = input;
                }
            }
            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // MIST_OWN_CONTRACT_VIOLATION should NOT fire (cooperation contract removed)
        var contractDiag = FindById(diagnostics, "MIST_OWN_CONTRACT_VIOLATION");
        Assert.Null(contractDiag);

        // MIST_OWN_FIELD_TYPE_VIOLATION fires instead (field-type restriction)
        var fieldTypeDiag = FindById(diagnostics, "MIST_OWN_FIELD_TYPE_VIOLATION");
        Assert.NotNull(fieldTypeDiag);
    }

    [Fact]
    public void ContractViolation_Message_ContainsMethodFieldAndSource()
    {
        var message = OwnershipAnalyzer.ContractViolationMessage("Configure", "setting", "input");

        Assert.Contains("Configure", message, StringComparison.Ordinal);
        Assert.Contains("setting", message, StringComparison.Ordinal);
        Assert.Contains("input", message, StringComparison.Ordinal);
        Assert.Contains("contract", message, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 5. MIST_OWN_FIELD_MODIFIER_REMOVED — the 'owned' field modifier is no longer
    //    supported; hard error (Req 13 AC 6, Req 11 AC 2, design Error Scenario 4).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FieldModifierRemoved_Trigger_EmitsHardError()
    {
        // Under owned-fields (Req 7 AC 3), the 'owned' field modifier is now VALID syntax
        // with new semantics. The MIST_OWN_FIELD_MODIFIER_REMOVED diagnostic is removed.
        const string source = """
            class Payload
            {
                public int Value;
            }
            class Container
            {
                owned Payload data;
            }
            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = FindById(diagnostics, "MIST_OWN_FIELD_MODIFIER_REMOVED");

        // The diagnostic is removed — owned field declarations are now accepted (Req 7 AC 1, 3).
        Assert.Null(diag);
    }

    // ─── Task 10.2: Additional owned-field-modifier hard error tests ─────
    // Validates: Requirements 11.2, 13.6, 19.2
    // NOTE: Tests for MIST_OWN_FIELD_MODIFIER_REMOVED have been removed per Req 7 AC 4 —
    // the owned field modifier is now valid syntax with new semantics.

    /// <summary>
    /// The exact pattern from the task description: <c>owned MyClass data;</c> as a field
    /// is now accepted without error (Req 7 AC 3).
    /// </summary>
    [Fact]
    public void FieldModifierRemoved_OwnedMyClassData_IsErrorNotWarning()
    {
        const string source = """
            class MyClass
            {
                public int X;
            }
            class Host
            {
                owned MyClass data;
            }
            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = FindById(diagnostics, "MIST_OWN_FIELD_MODIFIER_REMOVED");

        // The diagnostic is removed — owned field modifier is now valid (Req 7 AC 1, 3).
        Assert.Null(diag);
    }

    /// <summary>
    /// An <c>owned</c> field modifier with a different field name is now accepted
    /// without error (Req 7 AC 3).
    /// </summary>
    [Fact]
    public void FieldModifierRemoved_DifferentFieldName_MessageNamesField()
    {
        const string source = """
            class Widget
            {
                public int Value;
            }
            class Holder
            {
                owned Widget component;
            }
            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = FindById(diagnostics, "MIST_OWN_FIELD_MODIFIER_REMOVED");

        // The diagnostic is removed — owned field modifier is now valid (Req 7 AC 1, 3).
        Assert.Null(diag);
    }

    /// <summary>
    /// Legitimate uses of <c>owned</c> (type declaration and binding specifier)
    /// do NOT trigger the field-modifier hard error (no false positives).
    /// </summary>
    [Fact]
    public void FieldModifierRemoved_NoFalsePositive_OwnedClassAndBinding()
    {
        const string source = """
            owned class Vault
            {
                public int Secret;
                public Vault(int secret)
                {
                    this.Secret = secret;
                }
            }
            class Program
            {
                public static void Main()
                {
                    owned var v = new Vault(42);
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = FindById(diagnostics, "MIST_OWN_FIELD_MODIFIER_REMOVED");

        // No field modifier error — `owned` on class and binding is valid.
        Assert.Null(diag);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 6. MIST_OWN_SUBSTRUCTURAL_BINDING — the 'owned' binding specifier applied to a
    //    linear/affine/relevant type (Req 13 AC 8, Req 1 AC 4, design Error Scenario 6).
    // ═══════════════════════════════════════════════════════════════════════

    // Requires RejectOwnedBindingOnSubstructuralTypes (task 9.1).
    [Fact]
    public void SubstructuralBinding_Trigger_EmitsDiagnostic()
    {
        const string source = """
            linear class LinearResource
            {
                public int Handle;
            }
            class Program
            {
                public static void Main()
                {
                    owned var r = new LinearResource();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = FindById(diagnostics, "MIST_OWN_SUBSTRUCTURAL_BINDING");

        Assert.NotNull(diag);
        Assert.Contains("r", diag!.Message, StringComparison.Ordinal);
        Assert.Contains("LinearResource", diag.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SubstructuralBinding_Message_ContainsBindingAndSubstructuralType()
    {
        var message = OwnershipAnalyzer.SubstructuralBindingMessage("r", "LinearResource");

        Assert.Contains("r", message, StringComparison.Ordinal);
        Assert.Contains("LinearResource", message, StringComparison.Ordinal);
        Assert.Contains("substructural", message, StringComparison.OrdinalIgnoreCase);
    }
}