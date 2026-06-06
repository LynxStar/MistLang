// Feature: drop-semantics, Property 12: Synthetic drop inference
// **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for synthetic drop inference (drop-semantics Stage 4d,
/// <see cref="SyntheticDropInferenceEngine"/>).
///
/// Feature: drop-semantics, Property 12: Synthetic drop inference
/// **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5**
///
/// For any type T that has one or more fields with <c>Drop()</c> methods and does NOT
/// explicitly define its own <c>Drop()</c>, the compiler SHALL synthesize a <c>Drop()</c>.
/// The synthesized <c>Drop()</c> SHALL call field drops in declaration order and return void.
/// The obligation cascades transitively: a field of a synthesized-droppable type is itself
/// droppable.
///
/// The model is built in-process via a shared <see cref="CompilerDriver"/> and the type's
/// <c>HasDrop</c> / <c>DropReturnType</c> / <c>SyntheticDropFields</c> state is inspected
/// directly after Stage 4d mutates the captured model. Spawns 0 external processes.
/// </summary>
public sealed class SyntheticDropInferencePropertyTests : IDisposable
{
    private readonly string _outputDir;

    public SyntheticDropInferencePropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistSyntheticDropPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Capturing semantic model builder ────────────────────────────────

    /// <summary>
    /// Wraps the real <see cref="SemanticModelBuilder"/> so tests can inspect the
    /// constructed type model after build. Stage 4d (synthetic drop inference) runs
    /// after <c>Build</c> in the driver and mutates this same model instance, so the
    /// captured reference observes the synthesized <c>SyntheticDropFields</c>.
    /// </summary>
    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder _inner = new();
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
            var model = _inner.Build(compilation, asts, ambientTypeResolver, namespaceRoot, projectFolder, diagnostics, directReferencePaths);
            Captured = model;
            return model;
        }
    }

    // ─── Compile helper (shared driver per instance) ─────────────────────

    private ISemanticModel? CompileAndCapture(string source)
    {
        var filePath = Path.Combine(_outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

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
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        driver.Compile(options);
        return capture.Captured;
    }

    /// <summary>Finds a user-code class type by name.</summary>
    private static ClassType? FindClassType(ISemanticModel model, string name)
    {
        return model.AllSymbols
            .Where(s => s.Origin == SymbolOrigin.UserCode
                && s.Kind == SymbolKind.Class
                && s.Name == name)
            .Select(s => s.DeclaredType)
            .OfType<ClassType>()
            .FirstOrDefault();
    }

    // ─── Scenario ────────────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>One field of a generated container, classified by drop-ability.</summary>
    public enum FieldKind
    {
        /// <summary>A non-droppable value field (int) — never appears in SyntheticDropFields.</summary>
        NonDroppable,

        /// <summary>A field of the explicitly-droppable leaf type (Leaf).</summary>
        Droppable,

        /// <summary>A field of a synthetically-droppable type (Mid) — exercises the transitive cascade.</summary>
        Nested,
    }

    /// <summary>
    /// A generated container declaration together with the droppable-field names (in
    /// declaration order) that synthetic drop inference must record for it.
    /// </summary>
    public sealed record SyntheticDropScenario(
        string Source, IReadOnlyList<string> ExpectedDroppableFieldNames, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────
    //
    // Fixed preamble declares the cascade base:
    //   Leaf  — linear class with an EXPLICIT void Drop()       (droppable, NOT synthesized)
    //   Mid   — linear class with a single Leaf field           (SYNTHESIZED droppable)
    // The generated Container mixes NonDroppable (int), Droppable (Leaf) and Nested (Mid)
    // fields. A Container field is droppable iff its type is droppable (Leaf or Mid), and the
    // synthesized Drop() must list exactly those fields in declaration order.
    //
    // Containers are declared `linear` (owned) so they may legally hold the owned linear
    // Leaf/Mid fields without tripping the field-containment rule (Req 11) — that diagnostic
    // is orthogonal to whether synthesis records the drop fields.

    private static Gen<SyntheticDropScenario> GenScenario()
    {
        return from fieldCount in Gen.Choose(0, 5)
               from kinds in Gen.ArrayOf(
                   Gen.Elements(FieldKind.NonDroppable, FieldKind.Droppable, FieldKind.Nested),
                   fieldCount)
               select Build(kinds);
    }

    private static SyntheticDropScenario Build(FieldKind[] kinds)
    {
        var fieldLines = new List<string>();
        var expected = new List<string>();

        for (var i = 0; i < kinds.Length; i++)
        {
            var fieldName = $"f{i}";
            var (typeName, isDroppable) = kinds[i] switch
            {
                FieldKind.Droppable => ("Leaf", true),
                FieldKind.Nested => ("Mid", true),
                _ => ("int", false),
            };

            fieldLines.Add($"    {typeName} {fieldName};");
            if (isDroppable)
                expected.Add(fieldName);
        }

        var body = fieldLines.Count == 0 ? "" : "\n" + string.Join("\n", fieldLines) + "\n";

        var source = $$"""
            linear class Leaf
            {
                public void Drop()
                {
                }
            }
            linear class Mid
            {
                Leaf inner;
            }
            linear class Container
            {{{body}}}
            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var kindLabel = kinds.Length == 0 ? "no fields" : string.Join(",", kinds);
        var label = $"Container[{kindLabel}] expects [{string.Join(",", expected)}]";

        return new SyntheticDropScenario(source, expected, label);
    }

    public static Arbitrary<SyntheticDropScenario> ArbitrarySyntheticDropScenario() =>
        GenScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12: Synthetic drop inference for containing types
    //
    //   * Leaf has an EXPLICIT Drop()  → HasDrop, no SyntheticDropFields, void return.
    //   * Mid is SYNTHESIZED droppable → HasDrop, SyntheticDropFields == [inner], void.
    //   * Container gets a synthesized void Drop() with SyntheticDropFields equal to its
    //     droppable fields in declaration order IFF it has >= 1 droppable field; otherwise
    //     HasDrop is false and SyntheticDropFields is null.
    //
    // **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(SyntheticDropInferencePropertyTests) })]
    public Property SyntheticDrop_IsInferredForContainingTypes(SyntheticDropScenario scenario)
    {
        var model = CompileAndCapture(scenario.Source);

        if (model is null)
            return false.ToProperty().Label("Semantic model was null (compilation failed early)");

        var leaf = FindClassType(model, "Leaf");
        var mid = FindClassType(model, "Mid");
        var container = FindClassType(model, "Container");

        if (leaf is null || mid is null || container is null)
            return false.ToProperty().Label($"Missing type(s) in model.\nSource:\n{scenario.Source}");

        // ── Leaf: explicit Drop() — droppable, never synthesized (Req 10.6). ──
        if (!leaf.HasDrop)
            return false.ToProperty().Label("Leaf with explicit Drop() should have HasDrop == true.");
        if (leaf.SyntheticDropFields is not null)
            return false.ToProperty().Label("Leaf has an explicit Drop(); SyntheticDropFields must be null (no synthesis).");
        if (leaf.DropReturnType is not null)
            return false.ToProperty().Label("Leaf.Drop() returns void; DropReturnType must be null.");

        // ── Mid: synthesized over a single Leaf field (cascade base, Req 10.1-10.3). ──
        if (!mid.HasDrop)
            return false.ToProperty().Label("Mid contains a droppable Leaf field and should have a synthesized Drop().");
        if (mid.DropReturnType is not null)
            return false.ToProperty().Label("Synthesized Mid.Drop() must return void (DropReturnType == null).");
        var midFields = (mid.SyntheticDropFields ?? []).Select(f => f.Name).ToList();
        if (!midFields.SequenceEqual(new[] { "inner" }))
            return false.ToProperty().Label($"Mid.SyntheticDropFields expected [inner], got [{string.Join(",", midFields)}].");

        // ── Container: synthesized iff it has >= 1 droppable field; declaration order. ──
        var expected = scenario.ExpectedDroppableFieldNames;
        var actual = (container.SyntheticDropFields ?? Array.Empty<ISymbol>()).Select(f => f.Name).ToList();

        if (expected.Count == 0)
        {
            // No droppable fields → no synthesis (Req 10.1 only fires when droppable fields exist).
            var ok = !container.HasDrop && container.SyntheticDropFields is null;
            return ok.ToProperty().Label(
                $"Container with no droppable fields must have HasDrop == false and null SyntheticDropFields. "
                + $"Got HasDrop={container.HasDrop}, fields=[{string.Join(",", actual)}].\nSource:\n{scenario.Source}");
        }

        // Has droppable fields → synthesized void Drop() listing them in declaration order.
        var hasDropOk = container.HasDrop;
        var returnsVoid = container.DropReturnType is null;
        var orderOk = actual.SequenceEqual(expected);

        return (hasDropOk && returnsVoid && orderOk).ToProperty().Label(
            $"Container synthetic drop mismatch. HasDrop={container.HasDrop} (want true), "
            + $"DropReturnType={(returnsVoid ? "void" : "non-void")} (want void), "
            + $"SyntheticDropFields=[{string.Join(",", actual)}] want [{string.Join(",", expected)}] "
            + $"(declaration order, Req 10.2/10.5).\nSource:\n{scenario.Source}");
    }

    // ─── Example-based sanity checks ──────────────────────────────────────

    [Fact]
    public void Container_WithoutDroppableFields_GetsNoSyntheticDrop()
    {
        var source = """
            linear class Leaf
            {
                public void Drop()
                {
                }
            }
            linear class Plain
            {
                int a;
                int b;
            }
            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var plain = FindClassType(model!, "Plain");
        Assert.NotNull(plain);
        Assert.False(plain!.HasDrop);
        Assert.Null(plain.SyntheticDropFields);
    }

    [Fact]
    public void Container_SynthesizesDropInDeclarationOrder()
    {
        var source = """
            linear class Leaf
            {
                public void Drop()
                {
                }
            }
            linear class Container
            {
                Leaf first;
                int middle;
                Leaf last;
            }
            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var container = FindClassType(model!, "Container");
        Assert.NotNull(container);
        Assert.True(container!.HasDrop);
        Assert.Null(container.DropReturnType);
        Assert.NotNull(container.SyntheticDropFields);

        var names = container.SyntheticDropFields!.Select(f => f.Name).ToList();
        Assert.Equal(new[] { "first", "last" }, names);
    }

    [Fact]
    public void Cascade_IsTransitive_AcrossThreeLevels()
    {
        // Leaf (explicit Drop) → Mid (synthesized) → Outer (synthesized via Mid).
        var source = """
            linear class Leaf
            {
                public void Drop()
                {
                }
            }
            linear class Mid
            {
                Leaf inner;
            }
            linear class Outer
            {
                Mid nested;
            }
            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var outer = FindClassType(model!, "Outer");
        Assert.NotNull(outer);
        Assert.True(outer!.HasDrop);
        Assert.Null(outer.DropReturnType);
        Assert.NotNull(outer.SyntheticDropFields);
        Assert.Equal(new[] { "nested" }, outer.SyntheticDropFields!.Select(f => f.Name).ToList());
    }

    [Fact]
    public void ExplicitDrop_TakesPrecedence_NoSynthesis()
    {
        var source = """
            linear class Leaf
            {
                public void Drop()
                {
                }
            }
            linear class Manual
            {
                Leaf inner;

                public void Drop()
                {
                }
            }
            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var manual = FindClassType(model!, "Manual");
        Assert.NotNull(manual);
        Assert.True(manual!.HasDrop);
        // Explicit Drop() means SyntheticDropFields must remain null (Req 10.6).
        Assert.Null(manual.SyntheticDropFields);
    }
}
