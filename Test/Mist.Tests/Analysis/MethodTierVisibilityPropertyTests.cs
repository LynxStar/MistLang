// Feature: ownership-v2, Task 5.2 — Property 7: Method tier visibility
// (design Component 4, MethodTierVisibilityFilter).
//
// Property 7 (design.md):
//   For all owned bindings of type T and methods m:
//       m is callable ⟺ (T is owned) ∨ (m is readonly) ∨ (T is a value type)
//
// This property is exercised directly against MethodTierVisibilityFilter.Resolve.
// A single Mist program declaring an owned class, a non-owned class, and a
// (non-owned) struct value type — each with one readonly and one mutable method —
// is compiled ONCE in-process via a shared CompilerDriver and cached. FsCheck then
// generates random combinations of:
//
//   (receiver type kind: owned class / non-owned class / non-owned struct value type)
//     × (method kind: readonly / mutable)
//     × (receiverIsOwnedBinding: true / false)
//
// and, for each combination, locates the REAL method symbol + receiver type from the
// captured semantic model and asserts the biconditional above. The ReadonlyAnalyzer
// is wired into the pipeline so Symbol.ReadonlyStatus is populated (the filter
// classifies readonly vs mutable from that status).
//
// Process-spawning policy (test-process-spawning.md): compilation is fully in-process
// via CompilerDriver.Compile() with EmitToDisk:false — ZERO external process spawns.
// The compile happens once (cached, lazy) and is reused across every FsCheck
// iteration, so even 500 iterations cost a single in-process compile.
//
// NOTE: This property targets the FILTER directly. The end-to-end
// diagnostic-inspection form (compiling a program that calls a hidden mutable method
// and asserting MIST_OWN_MUTABLE_HIDDEN fires) is exercised once Task 8.1 wires the
// filter into the OwnershipAnalyzer; trigger coverage is provided by Tasks 5.3 / 11.2.
// The biconditional asserted here is the full intent of Property 7 and is NOT weakened
// by targeting the filter — the filter is the sole authority for the verdict.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>The three receiver type categories Property 7 distinguishes.</summary>
public enum ReceiverCategory
{
    /// <summary>Type declared <c>owned</c> (cooperation contract) — full access.</summary>
    OwnedClass,

    /// <summary>Non-owned reference type — readonly visible, mutable hidden.</summary>
    NonOwnedClass,

    /// <summary>Non-owned struct value type — all methods visible (copied).</summary>
    ValueStruct,
}

/// <summary>Whether the generated method is the readonly or mutable one.</summary>
public enum ReceiverMethodKind
{
    Readonly,
    Mutable,
}

/// <summary>
/// A single generated scenario: a receiver category, a method kind on that
/// receiver, and whether the receiver is held in an owned binding.
/// </summary>
public readonly record struct MethodTierScenario(
    ReceiverCategory Category,
    ReceiverMethodKind Method,
    bool ReceiverIsOwnedBinding);

/// <summary>
/// Property-based test for <see cref="MethodTierVisibilityFilter"/> — Property 7
/// (ownership-v2 design Component 4, Task 5.2).
///
/// **Validates: Requirements 2.1, 2.2, 3.1**
///
/// For all owned bindings of type T and methods m:
///   m is callable ⟺ (T is owned) ∨ (m is readonly) ∨ (T is a value type).
/// </summary>
public sealed class MethodTierVisibilityPropertyTests
{
    // ─── Fixture program (one readonly + one mutable method per category) ────

    private const string FixtureSource = """
        namespace TestProgram;

        // Owned type (cooperation contract) — full method access in owned bindings.
        owned class OwnedBox
        {
            public int Value;
            public OwnedBox(int value)
            {
                this.Value = value;
            }
            public readonly int OwnedReadonly()
            {
                return Value;
            }
            public void OwnedMutable(int v)
            {
                this.Value = v;
            }
        }

        // Non-owned reference type — readonly methods visible, mutable hidden.
        class PlainBox
        {
            public int Value;
            public PlainBox(int value)
            {
                this.Value = value;
            }
            public readonly int PlainReadonly()
            {
                return Value;
            }
            public void PlainMutable(int v)
            {
                this.Value = v;
            }
        }

        // Non-owned value type — all methods visible (copied on access).
        struct ValueBox
        {
            public int Value;
            public ValueBox(int value)
            {
                this.Value = value;
            }
            public readonly int ValueReadonly()
            {
                return Value;
            }
            public void ValueMutable(int v)
            {
                this.Value = v;
            }
        }

        class Program
        {
            public static void Main()
            {
            }
        }
        """;

    /// <summary>
    /// The fixture is compiled exactly once (in-process) and the resulting semantic
    /// model is reused across every FsCheck iteration. No external process is spawned.
    /// </summary>
    private static readonly Lazy<ISemanticModel> CapturedModel = new(CompileFixtureAndCapture);

    /// <summary>
    /// Wraps the real <see cref="SemanticModelBuilder"/> so the constructed model
    /// (method symbols with ReadonlyStatus, type models) can be inspected after the
    /// driver builds it.
    /// </summary>
    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder inner = new();
        public ISemanticModel? Captured { get; private set; }

        public ISemanticModel Build(
            MistCompilation compilation,
            ImmutableArray<Storm.Runtime.SyntaxNode> asts,
            IAmbientTypeResolver? ambientTypeResolver,
            string namespaceRoot,
            string projectFolder,
            DiagnosticBag diagnostics,
            ImmutableHashSet<string>? directReferencePaths = null)
        {
            var model = inner.Build(compilation, asts, ambientTypeResolver, namespaceRoot, projectFolder, diagnostics, directReferencePaths);
            Captured = model;
            return model;
        }
    }

    private static ISemanticModel CompileFixtureAndCapture()
    {
        var scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-method-tier-prop-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scratchDir);

        try
        {
            var sourcePath = Path.Combine(scratchDir, "Program.mist");
            File.WriteAllText(sourcePath, FixtureSource);

            var capture = new CapturingSemanticModelBuilder();
            var driver = new CompilerDriver(
                new Mist.Lexing.MistLexer(),
                new Mist.Parsing.MistParser(),
                capture,
                ImmutableArray.Create<IAnalyzer>(
                    new TypeCheckerAnalyzer(),
                    new ReadonlyAnalyzer(),
                    new OwnershipAnalyzer()),
                ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

            var options = new CompilerOptions(
                ProjectFolder: scratchDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: scratchDir,
                BinDirectory: Path.Combine(scratchDir, "bin"),
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            _ = driver.Compile(options);

            return capture.Captured
                ?? throw new InvalidOperationException("Fixture compilation did not produce a semantic model.");
        }
        finally
        {
            if(Directory.Exists(scratchDir))
            {
                try { Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
            }
        }
    }

    // ─── Model lookups ───────────────────────────────────────────────────────

    private static Symbol FindMethod(ISemanticModel model, string methodName)
    {
        return model.AllSymbols
            .OfType<Symbol>()
            .First(s => s.Kind == SymbolKind.Method && s.Name == methodName);
    }

    private static IType FindType(ISemanticModel model, string typeName)
    {
        return model.AllSymbols
            .Where(s => (s.Kind == SymbolKind.Class || s.Kind == SymbolKind.Struct)
                && s.Name == typeName)
            .Select(s => s.DeclaredType)
            .OfType<IType>()
            .First(t =>
                (t is ClassType ct && ct.ContainingNamespace == "TestProgram")
                || (t is StructType st && st.ContainingNamespace == "TestProgram"));
    }

    /// <summary>
    /// Maps a <see cref="ReceiverCategory"/> to its fixture type name and whether the
    /// category is an owned type / value type. These ground-truth facts drive the
    /// expected side of the biconditional independently of the filter under test.
    /// </summary>
    private static (string TypeName, bool IsOwnedType, bool IsValueType) CategoryFacts(ReceiverCategory category)
    {
        return category switch
        {
            ReceiverCategory.OwnedClass => ("OwnedBox", true, false),
            ReceiverCategory.NonOwnedClass => ("PlainBox", false, false),
            ReceiverCategory.ValueStruct => ("ValueBox", false, true),
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };
    }

    private static string MethodName(ReceiverCategory category, ReceiverMethodKind method)
    {
        var prefix = category switch
        {
            ReceiverCategory.OwnedClass => "Owned",
            ReceiverCategory.NonOwnedClass => "Plain",
            ReceiverCategory.ValueStruct => "Value",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };
        var suffix = method == ReceiverMethodKind.Readonly ? "Readonly" : "Mutable";
        return prefix + suffix;
    }

    // ─── Generator ───────────────────────────────────────────────────────────

    /// <summary>
    /// Generates random combinations of (receiver category × method kind × owned
    /// binding flag) — the full input space Property 7 quantifies over.
    /// </summary>
    public static Arbitrary<MethodTierScenario> ArbitraryScenario()
    {
        var gen =
            from category in Gen.Elements(
                ReceiverCategory.OwnedClass,
                ReceiverCategory.NonOwnedClass,
                ReceiverCategory.ValueStruct)
            from method in Gen.Elements(ReceiverMethodKind.Readonly, ReceiverMethodKind.Mutable)
            from isOwnedBinding in Gen.Elements(true, false)
            select new MethodTierScenario(category, method, isOwnedBinding);

        return gen.ToArbitrary();
    }

    // ─── Property 7 ────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 7: Method tier visibility.
    ///
    /// For all owned bindings of type T and methods m, with the receiver held in an
    /// owned binding:
    ///   Resolve(...) == Allowed  ⟺  (T owned) ∨ (m readonly) ∨ (T value type).
    ///
    /// And for a receiver NOT held in an owned binding, Resolve(...) is always Allowed.
    ///
    /// **Validates: Requirements 2.1, 2.2, 3.1**
    /// </summary>
    [Property(MaxTest = 300, Arbitrary = new[] { typeof(MethodTierVisibilityPropertyTests) })]
    public bool MethodTierVisibility_MatchesBiconditional(MethodTierScenario scenario)
    {
        var model = CapturedModel.Value;

        var (typeName, isOwnedType, isValueType) = CategoryFacts(scenario.Category);
        var receiverType = FindType(model, typeName);
        var method = FindMethod(model, MethodName(scenario.Category, scenario.Method));
        var isReadonly = scenario.Method == ReceiverMethodKind.Readonly;

        var verdict = MethodTierVisibilityFilter.Resolve(
            method,
            receiverType,
            scenario.ReceiverIsOwnedBinding,
            model);

        if(!scenario.ReceiverIsOwnedBinding)
        {
            // A receiver that is not an owned binding always has full method access.
            return verdict == MethodVisibility.Allowed;
        }

        // Owned binding: the biconditional from Property 7.
        var expectedCallable = isOwnedType || isReadonly || isValueType;
        var actualCallable = verdict == MethodVisibility.Allowed;
        return expectedCallable == actualCallable;
    }
}