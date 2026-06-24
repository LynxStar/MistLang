// Feature: ownership-v2, Property 6: IClonable auto-derivation is exactly "all fields clonable"
// **Validates: Requirements 8.6, 8.7**

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
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based test for IClonable auto-derivation (Ownership v2, Property 6,
/// computed by <see cref="Mist.Semantics.IClonableDerivationEngine"/>).
///
/// Feature: ownership-v2, Property 6: IClonable auto-derivation is exactly "all fields clonable"
/// **Validates: Requirements 8.6, 8.7**
///
/// For all types T:
///   <c>ImplementsIClonable(T)</c> ⟺
///       (T explicitly declares <c>IClonable&lt;T&gt;</c>)
///     ∨ (∀ field f ∈ T.Fields: <c>ImplementsIClonable(typeof(f))</c>).
/// The bootstrap value types and <c>String</c> are base cases (always true), so a
/// type whose fields are all primitives/strings auto-derives, and a type with any
/// non-clonable field (e.g. an array field) does not.
///
/// The test generates random user type definitions whose fields vary between
/// clonable (int / string / a clonable earlier type) and non-clonable (an array
/// field, or a non-clonable earlier type), plus types that explicitly declare
/// <c>IClonable&lt;T&gt;</c>. The expected clonability of each type is computed
/// independently by a single forward pass over the (acyclic, forward-only)
/// dependency graph — never by calling the engine — then compared against the
/// <c>ImplementsIClonable</c> flag the derivation pass set on the captured model.
///
/// The model is built in-process via a fresh <see cref="CompilerDriver"/> per
/// iteration (the driver runs the derivation pass after the semantic model is
/// built). Spawns 0 external processes.
/// </summary>
public sealed class IClonableDerivationPropertyTests : IDisposable
{
    private readonly string _outputDir;

    public IClonableDerivationPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistIClonableDerivePBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Capturing semantic model builder ────────────────────────────────

    /// <summary>
    /// Wraps the real <see cref="SemanticModelBuilder"/> so the test can inspect the
    /// constructed type model after the driver builds it. The IClonable derivation
    /// pass runs after <c>Build</c> in the driver and mutates this same model
    /// instance, so the captured reference observes the derived
    /// <c>ImplementsIClonable</c> flags.
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

    // ─── Compile helper ──────────────────────────────────────────────────

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

    /// <summary>
    /// Finds a user-code class or struct type by name and returns its
    /// <c>ImplementsIClonable</c> flag, or <c>null</c> if the type is not found.
    /// </summary>
    private static bool? FindImplementsIClonable(ISemanticModel model, string name)
    {
        var declaredType = model.AllSymbols
            .Where(s => s.Origin == SymbolOrigin.UserCode
                && s.Kind is SymbolKind.Class or SymbolKind.Struct
                && s.Name == name)
            .Select(s => s.DeclaredType)
            .OfType<IType>()
            .FirstOrDefault();

        return declaredType switch
        {
            ClassType ct => ct.ImplementsIClonable,
            StructType st => st.ImplementsIClonable,
            _ => null,
        };
    }

    // ─── Scenario model ──────────────────────────────────────────────────

#pragma warning disable CA1034 // nested types are the FsCheck-friendly way to surface a scenario

    /// <summary>The clonability classification of a single generated field.</summary>
    public enum FieldKind
    {
        /// <summary>An <c>int</c> field — clonable base case.</summary>
        IntLeaf,

        /// <summary>A <c>string</c> field — clonable base case.</summary>
        StringLeaf,

        /// <summary>An <c>int[]</c> field — NOT clonable (arrays are not auto-derivable).</summary>
        ArrayLeaf,

        /// <summary>A field whose type is an earlier generated type (propagates clonability).</summary>
        RefType,
    }

    /// <summary>One generated field: its kind and, for <see cref="FieldKind.Ref"/>, the target type index.</summary>
    public sealed record GenField(FieldKind Kind, int RefIndex);

    /// <summary>One generated type declaration.</summary>
    public sealed record GenType(bool IsStruct, bool DeclaresIClonable, IReadOnlyList<GenField> Fields);

    /// <summary>
    /// A complete generated program: the ordered type declarations plus the
    /// independently-computed expected <c>ImplementsIClonable</c> for each.
    /// </summary>
    public sealed record DerivationScenario(
        string Source, IReadOnlyList<bool> Expected, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────
    //
    // Generates 1..4 user types T0..Tn. Fields reference only EARLIER types
    // (forward-only / acyclic), so expected clonability is computed in a single
    // pass without a fixpoint — keeping the expected computation provably
    // independent of the engine's greatest-fixpoint implementation.

    private static Gen<DerivationScenario> GenScenario()
    {
        return from typeCount in Gen.Choose(1, 4)
               from types in GenTypes(typeCount)
               select Build(types);
    }

    private static Gen<List<GenType>> GenTypes(int count)
    {
        return GenTypesRec(0, count, new List<GenType>());
    }

    private static Gen<List<GenType>> GenTypesRec(int index, int count, List<GenType> acc)
    {
        if (index >= count)
            return Gen.Constant(acc);

        return from t in GenOneType(index)
               from rest in DeferAdd(index, count, acc, t)
               select rest;
    }

    private static Gen<List<GenType>> DeferAdd(int index, int count, List<GenType> acc, GenType t)
    {
        var next = new List<GenType>(acc) { t };
        return GenTypesRec(index + 1, count, next);
    }

    private static Gen<GenType> GenOneType(int index)
    {
        return from isStruct in Gen.Elements(false, false, true) // bias toward classes
               from declares in Gen.Elements(false, false, false, true) // ~25% explicit declarers
               from fieldCount in Gen.Choose(0, 3)
               from fields in GenFields(index, fieldCount)
               select new GenType(isStruct, declares, fields);
    }

    private static Gen<IReadOnlyList<GenField>> GenFields(int typeIndex, int fieldCount)
    {
        return Gen.ArrayOf(GenOneField(typeIndex), fieldCount)
            .Select(a => (IReadOnlyList<GenField>)a.ToList());
    }

    private static Gen<GenField> GenOneField(int typeIndex)
    {
        if (typeIndex == 0)
        {
            // No earlier type to reference — only leaf field kinds.
            return Gen.Elements(FieldKind.IntLeaf, FieldKind.StringLeaf, FieldKind.ArrayLeaf)
                .Select(k => new GenField(k, -1));
        }

        return from kind in Gen.Elements(FieldKind.IntLeaf, FieldKind.StringLeaf, FieldKind.ArrayLeaf, FieldKind.RefType)
               from refIndex in Gen.Choose(0, typeIndex - 1)
               select new GenField(kind, kind == FieldKind.RefType ? refIndex : -1);
    }

    // ─── Build source + expected ─────────────────────────────────────────

    private static DerivationScenario Build(List<GenType> types)
    {
        var expected = new List<bool>(types.Count);

        for (var i = 0; i < types.Count; i++)
        {
            var t = types[i];
            bool clonable;
            if (t.DeclaresIClonable)
            {
                // Left disjunct: an explicit declaration forces clonability true,
                // regardless of whether the fields are clonable.
                clonable = true;
            }
            else
            {
                // Right disjunct: clonable iff every field's type is clonable.
                clonable = true;
                foreach (var f in t.Fields)
                {
                    var fieldClonable = f.Kind switch
                    {
                        FieldKind.IntLeaf => true,
                        FieldKind.StringLeaf => true,
                        FieldKind.ArrayLeaf => false,
                        FieldKind.RefType => expected[f.RefIndex],
                        _ => false,
                    };
                    if (!fieldClonable)
                    {
                        clonable = false;
                        break;
                    }
                }
            }
            expected.Add(clonable);
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("using Standard;\n\nnamespace Gen;\n\n");

        for (var i = 0; i < types.Count; i++)
        {
            var t = types[i];
            var keyword = t.IsStruct ? "struct" : "class";
            var clonableClause = t.DeclaresIClonable ? $" : IClonable<T{i}>" : "";
            sb.Append($"public {keyword} T{i}{clonableClause}\n{{\n");

            for (var f = 0; f < t.Fields.Count; f++)
            {
                var field = t.Fields[f];
                var typeText = field.Kind switch
                {
                    FieldKind.IntLeaf => "int",
                    FieldKind.StringLeaf => "string",
                    FieldKind.ArrayLeaf => "int[]",
                    FieldKind.RefType => $"T{field.RefIndex}",
                    _ => "int",
                };
                sb.Append($"    public {typeText} f{f};\n");
            }

            if (t.DeclaresIClonable)
            {
                // A Clone() satisfying IClonable<T{i}>. The body returns `default`
                // (a fresh owned graph, Req 6.10) so it compiles for any field shape.
                sb.Append($"    public owned T{i} Clone() {{ return default; }}\n");
            }

            sb.Append("}\n\n");
        }

        sb.Append("public class Program\n{\n    public static void Main()\n    {\n    }\n}\n");

        var label = string.Join(
            ", ",
            types.Select((t, i) =>
            {
                var kind = t.IsStruct ? "struct" : "class";
                var d = t.DeclaresIClonable ? "decl" : "auto";
                var fields = t.Fields.Count == 0
                    ? "()"
                    : "(" + string.Join(",", t.Fields.Select(f =>
                        f.Kind == FieldKind.RefType ? $"T{f.RefIndex}" : f.Kind.ToString())) + ")";
                return $"T{i}:{kind}/{d}{fields}=>{expected[i]}";
            }));

        return new DerivationScenario(sb.ToString(), expected, label);
    }

    public static Arbitrary<DerivationScenario> ArbitraryDerivationScenario() =>
        GenScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: ImplementsIClonable(T) ⟺ declares IClonable ∨ all fields clonable
    //
    // **Validates: Requirements 8.6, 8.7**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(IClonableDerivationPropertyTests) })]
    public Property AutoDerivation_IsExactlyAllFieldsClonable(DerivationScenario scenario)
    {
        var model = CompileAndCapture(scenario.Source);

        if (model is null)
            return false.ToProperty().Label($"Semantic model was null (compilation failed early).\nSource:\n{scenario.Source}");

        for (var i = 0; i < scenario.Expected.Count; i++)
        {
            var actual = FindImplementsIClonable(model, $"T{i}");

            if (!actual.HasValue)
                return false.ToProperty().Label($"Type 'Gen.T{i}' not found in the model.\nSource:\n{scenario.Source}");

            if (actual.Value != scenario.Expected[i])
            {
                return false.ToProperty().Label(
                    $"T{i}: expected ImplementsIClonable={scenario.Expected[i]} "
                    + $"(declares IClonable ∨ all fields clonable), but model reported {actual.Value}.\n"
                    + $"Scenario: {scenario.Label}\nSource:\n{scenario.Source}");
            }
        }

        return true.ToProperty();
    }
}
