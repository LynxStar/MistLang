using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for IClonable auto-derivation edge cases (Ownership v2, Task 3.3).
///
/// <para>
/// The <c>IClonableDerivationEngine</c> populates
/// <see cref="ClassType.ImplementsIClonable"/> /
/// <see cref="StructType.ImplementsIClonable"/> using the greatest-fixpoint of the
/// "all fields clonable" rule. These tests pin the boundary behaviour described by
/// Requirement 8 AC 6 and AC 7:
/// </para>
/// <list type="bullet">
///   <item><b>All-clonable fields</b> (every field's type implements
///         <c>IClonable&lt;T&gt;</c>) → the type auto-derives (AC 6).</item>
///   <item><b>One non-clonable field</b> (a field of a type that is itself not
///         clonable) → the type does NOT derive (AC 7).</item>
///   <item><b>Recursive struct-of-structs</b> (a struct whose fields are other
///         all-clonable structs) → derives, exercising the recursive nature of the
///         rule over the type graph (AC 6).</item>
///   <item><b>Explicit declaration overrides</b> (a type that explicitly declares
///         <c>IClonable&lt;T&gt;</c> but has a non-clonable field) → still derives,
///         because an explicit declaration is a fixed base case that is never
///         eliminated by field-based derivation (AC 6).</item>
/// </list>
///
/// The model is built in-process through the full <see cref="CompilerDriver"/>
/// (which prepends the stdlib and runs the derivation pass before ownership
/// analysis); the user type's <c>ImplementsIClonable</c> flag is then inspected
/// directly. Spawns 0 external processes.
///
/// Validates: Requirements 8.6, 8.7
/// </summary>
public sealed class IClonableDerivationEdgeCaseTests : IDisposable
{
    private readonly string _scratchDir;

    public IClonableDerivationEdgeCaseTests()
    {
        _scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-iclonable-edge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_scratchDir))
        {
            try { Directory.Delete(_scratchDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A user program exercising every derivation edge case. The types live in the
    /// <c>X</c> namespace so they never clash with stdlib types.
    /// </summary>
    private const string EdgeCaseProgram = """
        using Standard;

        namespace X;

        // (a) ALL-CLONABLE FIELDS — every field's type implements IClonable<T>
        //     (Int32 and String are bootstrap base cases) → DERIVES.
        public class AllClonable
        {
            public int Count;
            public string Name;
        }

        // A non-clonable leaf: it has an array-typed instance field. Arrays are not
        // auto-derivable clonable sources, so this type is NOT clonable itself.
        public class NonClonableLeaf
        {
            public int[] Data;
        }

        // (b) ONE NON-CLONABLE FIELD — holds a field of a class that is itself not
        //     clonable (NonClonableLeaf) → DOES NOT DERIVE.
        public class HasNonClonableField
        {
            public NonClonableLeaf Leaf;
            public int Value;
        }

        // (c) RECURSIVE STRUCT-OF-STRUCTS — a struct whose fields are other
        //     all-clonable structs. The inner struct's fields are all bootstrap
        //     clonable, so the outer struct derives recursively → DERIVES.
        public struct InnerPoint
        {
            public int X;
            public int Y;
        }

        public struct OuterPoint
        {
            public InnerPoint Origin;
            public InnerPoint Extent;
            public int Scale;
        }

        // (d) EXPLICIT DECLARATION OVERRIDES — the type explicitly declares
        //     IClonable<T> but also has a non-clonable (array) field. The explicit
        //     declaration is a fixed base case, so it is never eliminated by
        //     field-based derivation → STILL DERIVES (true).
        public class ExplicitlyClonable : IClonable<ExplicitlyClonable>
        {
            public int[] Data;

            public owned ExplicitlyClonable Clone()
            {
                return default;
            }
        }

        public class Y
        {
            public static void Main() { }
        }
        """;

    /// <summary>
    /// Wraps the real <see cref="SemanticModelBuilder"/> so the test can inspect the
    /// constructed type model (e.g. <c>ImplementsIClonable</c>) after the driver builds it.
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

    /// <summary>
    /// Compiles the edge-case program through the full driver (which prepends the
    /// stdlib and runs IClonable derivation) and returns the captured semantic model.
    /// </summary>
    private ISemanticModel CompileAndCapture()
    {
        var userPath = Path.Combine(_scratchDir, "Program.mist");
        File.WriteAllText(userPath, EdgeCaseProgram);

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
            ProjectFolder: _scratchDir,
            SourcePaths: ImmutableArray.Create(userPath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _scratchDir,
            BinDirectory: Path.Combine(_scratchDir, "bin"),
            ProjectName: "X",
            NamespaceRoot: "X",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        driver.Compile(options);

        Assert.NotNull(capture.Captured);
        return capture.Captured!;
    }

    /// <summary>
    /// Finds a user (<c>X</c> namespace) class or struct type by name and returns its
    /// <c>ImplementsIClonable</c> flag, or <c>null</c> if the type was not found.
    /// </summary>
    private static bool? FindImplementsIClonable(ISemanticModel model, string typeName)
    {
        var declaredType = model.AllSymbols
            .Where(s => (s.Kind == SymbolKind.Struct || s.Kind == SymbolKind.Class)
                && s.Name == typeName)
            .Select(s => s.DeclaredType)
            .OfType<IType>()
            .FirstOrDefault(t =>
                (t is StructType st && st.ContainingNamespace == "X")
                || (t is ClassType ct && ct.ContainingNamespace == "X"));

        return declaredType switch
        {
            StructType st => st.ImplementsIClonable,
            ClassType ct => ct.ImplementsIClonable,
            _ => null,
        };
    }

    [Fact]
    public void AllClonableFields_Derives()
    {
        var model = CompileAndCapture();

        var result = FindImplementsIClonable(model, "AllClonable");

        Assert.True(result.HasValue, "Type 'X.AllClonable' was not found in the semantic model.");
        Assert.True(
            result!.Value,
            "Expected 'AllClonable' (all fields are bootstrap-clonable: Int32, String) to "
            + "auto-derive ImplementsIClonable == true (Req 8.6).");
    }

    [Fact]
    public void OneNonClonableField_DoesNotDerive()
    {
        var model = CompileAndCapture();

        // The leaf with an abstract-typed field is itself non-clonable.
        var leaf = FindImplementsIClonable(model, "NonClonableLeaf");
        Assert.True(leaf.HasValue, "Type 'X.NonClonableLeaf' was not found in the semantic model.");
        Assert.False(
            leaf!.Value,
            "Expected 'NonClonableLeaf' (has an array-typed field) to NOT derive "
            + "ImplementsIClonable (Req 8.7).");

        // A type holding a field of that non-clonable type also does not derive.
        var result = FindImplementsIClonable(model, "HasNonClonableField");
        Assert.True(result.HasValue, "Type 'X.HasNonClonableField' was not found in the semantic model.");
        Assert.False(
            result!.Value,
            "Expected 'HasNonClonableField' (holds a field of the non-clonable type "
            + "'NonClonableLeaf') to NOT derive ImplementsIClonable (Req 8.7).");
    }

    [Fact]
    public void RecursiveStructOfStructs_Derives()
    {
        var model = CompileAndCapture();

        var inner = FindImplementsIClonable(model, "InnerPoint");
        Assert.True(inner.HasValue, "Type 'X.InnerPoint' was not found in the semantic model.");
        Assert.True(
            inner!.Value,
            "Expected 'InnerPoint' (all Int32 fields) to auto-derive ImplementsIClonable (Req 8.6).");

        var outer = FindImplementsIClonable(model, "OuterPoint");
        Assert.True(outer.HasValue, "Type 'X.OuterPoint' was not found in the semantic model.");
        Assert.True(
            outer!.Value,
            "Expected 'OuterPoint' (a struct whose fields are the all-clonable struct "
            + "'InnerPoint') to auto-derive ImplementsIClonable recursively (Req 8.6).");
    }

    [Fact]
    public void ExplicitDeclaration_OverridesNonClonableField()
    {
        var model = CompileAndCapture();

        var result = FindImplementsIClonable(model, "ExplicitlyClonable");

        Assert.True(result.HasValue, "Type 'X.ExplicitlyClonable' was not found in the semantic model.");
        Assert.True(
            result!.Value,
            "Expected 'ExplicitlyClonable' (explicitly declares IClonable<T> despite an "
            + "array-typed non-clonable field) to report ImplementsIClonable == true — "
            + "an explicit declaration overrides auto-derivation (Req 8.6).");
    }
}