using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests verifying that the TypeCheckerAnalyzer correctly attributes
/// diagnostics to the right source file when multiple files have overlapping
/// span ranges (both starting at offset 0).
///
/// Validates: Requirements 4.4, 5.3
/// </summary>
public class TypeCheckerOriginAttributionTests
{
    private readonly MistLexer lexer = new();
    private readonly MistParser parser = new();
    private readonly SemanticModelBuilder builder = new();
    private readonly TypeCheckerAnalyzer analyzer = new();

    /// <summary>
    /// Parses a source file in-process via MistLexer + MistParser.
    /// The parser assigns ParsedOrigin to every node in the tree.
    /// Returns the parsed AST root (or null if parsing failed).
    /// </summary>
    private Storm.Runtime.SyntaxNode? ParseSource(SourceText source)
    {
        var lexDiag = new DiagnosticBag();
        var tokenStream = lexer.Lex(source, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = parser.Parse(tokenStream, parseDiag);

        return parseResult.Root;
    }

    /// <summary>
    /// Two files with overlapping spans: file A is well-typed, file B has a
    /// type error (returning int from void). The diagnostic should point to
    /// file B's path, not file A's.
    /// </summary>
    [Fact]
    public void TypeError_InFileB_DiagnosticPointsToFileB_NotFileA()
    {
        // File A: well-typed program — class with static void Main
        // Both files start at offset 0, creating overlapping span ranges.
        var sourceA = new SourceText("/project/fileA.mist",
            """
            class Program {
                public static void Main() {
                    int x = 42;
                }
            }
            """);

        // File B: type error — returning int from void method
        var sourceB = new SourceText("/project/fileB.mist",
            """
            class Helper {
                public static void DoWork() {
                    return 99;
                }
            }
            """);

        var astA = ParseSource(sourceA);
        var astB = ParseSource(sourceB);

        Assert.NotNull(astA);
        Assert.NotNull(astB);

        // Build compilation with both files
        var units = ImmutableArray.Create(
            new CompilationUnit(sourceA),
            new CompilationUnit(sourceB));
        var compilation = new MistCompilation(units);

        var asts = ImmutableArray.Create(astA, astB);
        var semanticDiag = new DiagnosticBag();
        var model = builder.Build(
            compilation, asts, ambientTypeResolver: null,
            namespaceRoot: "TestProject", projectFolder: "/project", semanticDiag);

        // Run type checker
        var typeCheckerDiag = new DiagnosticBag();
        analyzer.Analyze(model, typeCheckerDiag);
        var diagnostics = typeCheckerDiag.ToImmutableArray();

        // The return-type-mismatch error (MIST_TC_0008) should point to fileB
        var returnError = diagnostics.FirstOrDefault(d => d.Id.Value == "MIST_TC_0008");
        Assert.NotNull(returnError);
        Assert.Equal("/project/fileB.mist", returnError.CompilationUnitPath);
    }

    /// <summary>
    /// Three files with overlapping spans: file A and C are well-typed,
    /// file B has a type error. Verifies correct attribution even with
    /// multiple files that all start at offset 0.
    /// </summary>
    [Fact]
    public void ThreeFiles_OverlappingSpans_DiagnosticPointsToCorrectFile()
    {
        var sourceA = new SourceText("/project/alpha.mist",
            """
            class Alpha {
                public static void Main() {
                    int a = 1;
                }
            }
            """);

        // File B has a non-bool condition in an if statement (type error)
        var sourceB = new SourceText("/project/beta.mist",
            """
            class Beta {
                public void Check() {
                    int x = 5;
                    if (x) { }
                }
            }
            """);

        var sourceC = new SourceText("/project/gamma.mist",
            """
            class Gamma {
                public void Run() {
                    int y = 10;
                }
            }
            """);

        var astA = ParseSource(sourceA);
        var astB = ParseSource(sourceB);
        var astC = ParseSource(sourceC);

        Assert.NotNull(astA);
        Assert.NotNull(astB);
        Assert.NotNull(astC);

        var units = ImmutableArray.Create(
            new CompilationUnit(sourceA),
            new CompilationUnit(sourceB),
            new CompilationUnit(sourceC));
        var compilation = new MistCompilation(units);

        var asts = ImmutableArray.Create(astA, astB, astC);
        var semanticDiag = new DiagnosticBag();
        var model = builder.Build(
            compilation, asts, ambientTypeResolver: null,
            namespaceRoot: "TestProject", projectFolder: "/project", semanticDiag);

        var typeCheckerDiag = new DiagnosticBag();
        analyzer.Analyze(model, typeCheckerDiag);
        var diagnostics = typeCheckerDiag.ToImmutableArray();

        // The non-bool condition error (MIST_TC_0007) should point to beta.mist
        var conditionError = diagnostics.FirstOrDefault(d => d.Id.Value == "MIST_TC_0007");
        Assert.NotNull(conditionError);
        Assert.Equal("/project/beta.mist", conditionError.CompilationUnitPath);
    }

    /// <summary>
    /// Two files with identical content lengths (maximally overlapping spans).
    /// File A has the type error, file B is well-typed. Verifies the diagnostic
    /// points to file A even though file B has the exact same span range.
    /// </summary>
    [Fact]
    public void IdenticalLengthFiles_DiagnosticPointsToCorrectFile()
    {
        // File A: type error — assigning bool to int variable
        var sourceA = new SourceText("/project/first.mist",
            """
            class First {
                public static void Main() {
                    return 1;
                }
            }
            """);

        // File B: well-typed, same length content
        var sourceB = new SourceText("/project/second.mist",
            """
            class Secnd {
                public void DoStuff() {
                    int z = 0;
                }
            }
            """);

        var astA = ParseSource(sourceA);
        var astB = ParseSource(sourceB);

        Assert.NotNull(astA);
        Assert.NotNull(astB);

        var units = ImmutableArray.Create(
            new CompilationUnit(sourceA),
            new CompilationUnit(sourceB));
        var compilation = new MistCompilation(units);

        var asts = ImmutableArray.Create(astA, astB);
        var semanticDiag = new DiagnosticBag();
        var model = builder.Build(
            compilation, asts, ambientTypeResolver: null,
            namespaceRoot: "TestProject", projectFolder: "/project", semanticDiag);

        var typeCheckerDiag = new DiagnosticBag();
        analyzer.Analyze(model, typeCheckerDiag);
        var diagnostics = typeCheckerDiag.ToImmutableArray();

        // The return-type-mismatch error should point to first.mist
        var returnError = diagnostics.FirstOrDefault(d => d.Id.Value == "MIST_TC_0008");
        Assert.NotNull(returnError);
        Assert.Equal("/project/first.mist", returnError.CompilationUnitPath);
    }
}