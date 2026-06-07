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
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
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
        Directory.CreateDirectory(tempDir);
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

    private static Diagnostic? FindById(ImmutableArray<Diagnostic> diagnostics, string id) =>
        diagnostics.FirstOrDefault(d => d.Id.Value == id);

    // ═══════════════════════════════════════════════════════════════════════
    // 1. MIST_OWN_MUTABLE_HIDDEN — mutable method on a non-owned type held in an
    //    owned binding (Req 13 AC 1, design Error Scenario 1).
    // ═══════════════════════════════════════════════════════════════════════

    // Pending: requires MethodTierVisibilityFilter wiring (task 8.1).
    [Fact(Skip = "Pending MethodTierVisibilityFilter emission wiring (task 8.1).")]
    public void MutableHidden_Trigger_EmitsDiagnostic()
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

        Assert.NotNull(diag);
        Assert.Contains("SetValue", diag!.Message, StringComparison.Ordinal);
        Assert.Contains("Widget", diag.Message, StringComparison.Ordinal);
        Assert.Contains("readonly", diag.Message, StringComparison.OrdinalIgnoreCase);
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

    // Pending: requires MoveBoundaryResolver wiring (task 8.1).
    [Fact(Skip = "Pending MoveBoundaryResolver emission wiring (task 8.1).")]
    public void RequiresOwned_Trigger_EmitsDiagnostic()
    {
        // 'Holder' is an owned type whose field 'item' is assigned from a non-owned,
        // non-clonable source ('shared'). Gadget is non-clonable because it carries an
        // interface-typed field, so it cannot auto-derive IClonable<T>; the move
        // boundary therefore cannot resolve the assignment as a clone and must error
        // with MIST_OWN_REQUIRES_OWNED (provenance requirement).
        const string source = """
            interface ISignal
            {
            }
            class Gadget
            {
                public ISignal signal;
            }
            owned class Holder
            {
                public Gadget item;
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
        var diag = FindById(diagnostics, "MIST_OWN_REQUIRES_OWNED");

        Assert.NotNull(diag);
        Assert.Contains("item", diag!.Message, StringComparison.Ordinal);
        Assert.Contains("shared", diag.Message, StringComparison.Ordinal);
        Assert.Contains("owned", diag.Message, StringComparison.OrdinalIgnoreCase);
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

    // Pending: requires MoveBoundaryResolver wiring (task 8.1).
    [Fact(Skip = "Pending MoveBoundaryResolver emission wiring (task 8.1).")]
    public void NotClonable_Trigger_EmitsDiagnostic()
    {
        // 'shared' is a non-owned binding of type SharedThing. SharedThing is
        // non-clonable because it has an interface-typed field, so it cannot
        // auto-derive IClonable<T>. Assigning it into an owned binding requires a
        // clone that cannot be produced -> MIST_OWN_NOT_CLONABLE.
        const string source = """
            interface ISignal
            {
            }
            class SharedThing
            {
                public ISignal signal;
            }
            class Program
            {
                public static void Main()
                {
                    var shared = new SharedThing();
                    owned var c = shared;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = FindById(diagnostics, "MIST_OWN_NOT_CLONABLE");

        Assert.NotNull(diag);
        Assert.Contains("SharedThing", diag!.Message, StringComparison.Ordinal);
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

    // Pending: requires CooperationContractVerifier wiring (task 8.1).
    [Fact(Skip = "Pending CooperationContractVerifier emission wiring (task 8.1).")]
    public void ContractViolation_Trigger_EmitsDiagnostic()
    {
        // 'owned class Vault' has a method that assigns field 'setting' from a
        // non-owned parameter 'input'. The cooperation contract requires owned
        // sources -> MIST_OWN_CONTRACT_VIOLATION.
        const string source = """
            interface ISignal
            {
            }
            class Gadget
            {
                public ISignal signal;
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
        var diag = FindById(diagnostics, "MIST_OWN_CONTRACT_VIOLATION");

        Assert.NotNull(diag);
        Assert.Contains("Configure", diag!.Message, StringComparison.Ordinal);
        Assert.Contains("setting", diag.Message, StringComparison.Ordinal);
        Assert.Contains("input", diag.Message, StringComparison.Ordinal);
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

    // Pending: requires RejectOwnedFieldModifier hard error (task 10.1).
    [Fact(Skip = "Pending RejectOwnedFieldModifier hard error (task 10.1).")]
    public void FieldModifierRemoved_Trigger_EmitsHardError()
    {
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

        Assert.NotNull(diag);
        // Must be a hard error, not a warning (Req 11 AC 2 / Req 13 AC 6).
        Assert.Equal(DiagnosticSeverity.Error, diag!.Severity);
        Assert.Contains("data", diag.Message, StringComparison.Ordinal);
        Assert.Contains("owned", diag.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FieldModifierRemoved_Message_ContainsFieldAndRemovalHint()
    {
        var message = OwnershipAnalyzer.FieldModifierRemovedMessage("data");

        Assert.Contains("data", message, StringComparison.Ordinal);
        Assert.Contains("owned", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no longer supported", message, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 6. MIST_OWN_SUBSTRUCTURAL_BINDING — the 'owned' binding specifier applied to a
    //    linear/affine/relevant type (Req 13 AC 8, Req 1 AC 4, design Error Scenario 6).
    // ═══════════════════════════════════════════════════════════════════════

    // Pending: requires RejectOwnedBindingOnSubstructuralTypes (task 9.1).
    [Fact(Skip = "Pending RejectOwnedBindingOnSubstructuralTypes (task 9.1).")]
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
