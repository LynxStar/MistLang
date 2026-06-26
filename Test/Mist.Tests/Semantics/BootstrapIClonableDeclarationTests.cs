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
/// Unit tests asserting that every bootstrap value type and <c>String</c> reports
/// <see cref="StructType.ImplementsIClonable"/> / <see cref="ClassType.ImplementsIClonable"/>
/// as <c>true</c> in the semantic model.
///
/// The bootstrap types (<c>Int32</c>, <c>Int64</c>, <c>Int16</c>, <c>UInt32</c>,
/// <c>UInt64</c>, <c>UInt16</c>, <c>Byte</c>, <c>SByte</c>, <c>Single</c>,
/// <c>Double</c>, <c>Decimal</c>, <c>Boolean</c>, <c>Char</c>) and <c>String</c>
/// each declare <c>: IClonable&lt;Self&gt;</c> with an
/// <c>intrinsic public owned T Clone();</c> (Task 1.2). They are the recursion
/// base cases for IClonable auto-derivation, so the IClonable derivation pass
/// must mark each of them as implementing <c>IClonable&lt;T&gt;</c>.
///
/// The model is built in-process via the full <see cref="CompilerDriver"/> (which
/// prepends the stdlib), and the bootstrap type's <c>ImplementsIClonable</c> flag
/// is inspected directly. Spawns 0 external processes.
///
/// Validates: Requirements 8.6
/// </summary>
public sealed class BootstrapIClonableDeclarationTests : IDisposable
{
    private readonly string scratchDir;

    public BootstrapIClonableDeclarationTests()
    {
        scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-bootstrap-iclonable-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Wraps the real <see cref="SemanticModelBuilder"/> so the test can inspect the
    /// constructed type model (e.g. <c>ImplementsIClonable</c>) after the driver builds it.
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

    /// <summary>
    /// Compiles a trivial user program through the full driver (which prepends the
    /// stdlib bootstrap types) and returns the captured semantic model. All builtin
    /// analyzers that participate in IClonable derivation are wired in.
    /// </summary>
    private ISemanticModel? CompileAndCapture()
    {
        // A minimal user program in a distinct namespace so no name clash with the stdlib.
        var userSource = "namespace X;\npublic class Y\n{\n    public static void Main() { }\n}\n";
        var userPath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(userPath, userSource);

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
            SourcePaths: ImmutableArray.Create(userPath),
            Backend: new BackendId("csharp"),
            OutputDirectory: scratchDir,
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "X",
            NamespaceRoot: "X",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        _ = driver.Compile(options);
        return capture.Captured;
    }

    /// <summary>
    /// Finds the stdlib (<c>Standard</c> namespace) class or struct type by name and
    /// returns its <c>ImplementsIClonable</c> flag, or <c>null</c> if not found.
    /// </summary>
    private static bool? FindImplementsIClonable(ISemanticModel model, string typeName)
    {
        var declaredType = model.AllSymbols
            .Where(s => (s.Kind == SymbolKind.Struct || s.Kind == SymbolKind.Class)
                && s.Name == typeName)
            .Select(s => s.DeclaredType)
            .OfType<IType>()
            .FirstOrDefault(t =>
                (t is StructType st && st.ContainingNamespace == "Standard")
                || (t is ClassType ct && ct.ContainingNamespace == "Standard"));

        return declaredType switch
        {
            StructType st => st.ImplementsIClonable,
            ClassType ct => ct.ImplementsIClonable,
            _ => null,
        };
    }

    // The 14 bootstrap value types plus String — the IClonable auto-derivation base cases.
    [Theory]
    [InlineData("Int32")]
    [InlineData("Int64")]
    [InlineData("Int16")]
    [InlineData("UInt32")]
    [InlineData("UInt64")]
    [InlineData("UInt16")]
    [InlineData("Byte")]
    [InlineData("SByte")]
    [InlineData("Single")]
    [InlineData("Double")]
    [InlineData("Decimal")]
    [InlineData("Boolean")]
    [InlineData("Char")]
    [InlineData("String")]
    public void BootstrapType_ReportsImplementsIClonable(string typeName)
    {
        var model = CompileAndCapture();
        Assert.NotNull(model);

        var implementsIClonable = FindImplementsIClonable(model!, typeName);

        Assert.True(
            implementsIClonable.HasValue,
            $"Bootstrap type 'Standard.{typeName}' was not found in the semantic model.");

        Assert.True(
            implementsIClonable!.Value,
            $"Expected bootstrap type 'Standard.{typeName}' to report ImplementsIClonable == true "
            + "(it declares `: IClonable<Self>` and is an auto-derivation base case), "
            + $"but it reported {implementsIClonable.Value}.");
    }
}