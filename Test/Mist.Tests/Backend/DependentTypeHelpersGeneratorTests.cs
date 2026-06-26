// Task 8.1: Unit tests for const-carrier interface generation
// Validates: Requirements 13.1, 13.2

using System.Collections.Immutable;
using System.Text;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for the <see cref="DependentTypeHelpersGenerator"/> verifying that
/// const-carrier interfaces are emitted only for primitive types actually used as
/// dependent parameters in the program.
///
/// **Validates: Requirements 13.1, 13.2**
/// </summary>
public sealed class DependentTypeHelpersGeneratorTests
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── In-memory compilation helper ────────────────────────────────────

    private static Dictionary<string, string> CompileToFiles(string mistSource)
    {
        var sourceText = new SourceText("test.mist", mistSource);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        // Lex
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, $"Lex errors: {FormatDiagnostics(lexBag)}");

        // Parse
        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, $"Parse errors: {FormatDiagnostics(parseBag)}");
        Assert.NotNull(parseResult.Root);

        // Semantic model
        var semBag = new DiagnosticBag();
        var model = ModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProgram",
            projectFolder: "/test",
            semBag);
        Assert.False(semBag.HasErrors, $"Semantic errors: {FormatDiagnostics(semBag)}");

        // Type checker
        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);

        // Emit in-memory
        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, "TestProgram", emitBag);
        Assert.False(emitBag.HasErrors, $"Emit errors: {FormatDiagnostics(emitBag)}");

        return files;
    }

    private static string FormatDiagnostics(DiagnosticBag bag)
    {
        return string.Join("\n", bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    // ─── Tests ───────────────────────────────────────────────────────────

    [Fact]
    public void EmitsIConstIntForClassWithIntDependentParameter()
    {
        var source = "class FixedBuffer<Size:int> { }";
        var files = CompileToFiles(source);

        Assert.True(files.ContainsKey("DependentTypeHelpers.g.cs"),
            "Expected DependentTypeHelpers.g.cs to be emitted");

        var content = files["DependentTypeHelpers.g.cs"];
        Assert.Contains("public interface IConstInt", content);
        Assert.Contains("static abstract int Value { get; }", content);
    }

    [Fact]
    public void EmitsIConstBoolForClassWithBoolDependentParameter()
    {
        var source = "class Flag<Enabled:bool> { }";
        var files = CompileToFiles(source);

        Assert.True(files.ContainsKey("DependentTypeHelpers.g.cs"),
            "Expected DependentTypeHelpers.g.cs to be emitted");

        var content = files["DependentTypeHelpers.g.cs"];
        Assert.Contains("public interface IConstBool", content);
        Assert.Contains("static abstract bool Value { get; }", content);
    }

    [Fact]
    public void EmitsMultipleInterfacesForMultipleDependentTypes()
    {
        var source = """
            class Buffer<Size:int> { }
            class Flag<Enabled:bool> { }
            class CharHolder<C:char> { }
            """;
        var files = CompileToFiles(source);

        Assert.True(files.ContainsKey("DependentTypeHelpers.g.cs"));

        var content = files["DependentTypeHelpers.g.cs"];
        Assert.Contains("public interface IConstInt", content);
        Assert.Contains("public interface IConstBool", content);
        Assert.Contains("public interface IConstChar", content);
    }

    [Fact]
    public void DoesNotEmitHelpersFileWhenNoDependentParameters()
    {
        var source = "class Box<T> { }";
        var files = CompileToFiles(source);

        Assert.False(files.ContainsKey("DependentTypeHelpers.g.cs"),
            "DependentTypeHelpers.g.cs should not be emitted when no dependent parameters exist");
    }

    [Fact]
    public void OnlyEmitsInterfacesForUsedTypes()
    {
        var source = "class Buffer<Size:int> { }";
        var files = CompileToFiles(source);

        var content = files["DependentTypeHelpers.g.cs"];
        Assert.Contains("public interface IConstInt", content);
        // Should NOT contain interfaces for unused types
        Assert.DoesNotContain("IConstBool", content);
        Assert.DoesNotContain("IConstChar", content);
        Assert.DoesNotContain("IConstLong", content);
    }

    [Fact]
    public void EmitsInterfaceForMethodLevelDependentParameter()
    {
        var source = """
            class Util {
                public void Repeat<N:int>() { }
            }
            """;
        var files = CompileToFiles(source);

        Assert.True(files.ContainsKey("DependentTypeHelpers.g.cs"));
        var content = files["DependentTypeHelpers.g.cs"];
        Assert.Contains("public interface IConstInt", content);
    }

    [Fact]
    public void DeduplicatesInterfacesForSameType()
    {
        var source = """
            class Buffer<Size:int> { }
            class Matrix<Rows:int, Cols:int> { }
            """;
        var files = CompileToFiles(source);

        var content = files["DependentTypeHelpers.g.cs"];
        // IConstInt should appear exactly once
        var count = content.Split("public interface IConstInt").Length - 1;
        Assert.Equal(1, count);
    }

    [Fact]
    public void EmitsAllSupportedAbstractTypes()
    {
        var source = """
            class A<V:int> { }
            class B<V:uint> { }
            class C<V:long> { }
            class D<V:ulong> { }
            class E<V:short> { }
            class F<V:ushort> { }
            class G<V:byte> { }
            class H<V:sbyte> { }
            class I<V:bool> { }
            class J<V:char> { }
            """;
        var files = CompileToFiles(source);

        var content = files["DependentTypeHelpers.g.cs"];
        Assert.Contains("public interface IConstInt", content);
        Assert.Contains("public interface IConstUint", content);
        Assert.Contains("public interface IConstLong", content);
        Assert.Contains("public interface IConstUlong", content);
        Assert.Contains("public interface IConstShort", content);
        Assert.Contains("public interface IConstUshort", content);
        Assert.Contains("public interface IConstByte", content);
        Assert.Contains("public interface IConstSbyte", content);
        Assert.Contains("public interface IConstBool", content);
        Assert.Contains("public interface IConstChar", content);
    }
}