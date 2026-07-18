// Integration tests for the ProgramModel body binder (walking-skeleton slice).
//
// Builds the Program Model from Mist source in-process (through the real
// ProgramModelBuilder, so Phase 3 body binding runs) and asserts on the resolved
// body directly. Standard-library type shells are registered via a small ambient
// provider so that `int` resolves to Standard.Int32 for typing assertions.
//
// These are clean-room assertions: every expectation is derived from what the
// source program means, never from the legacy Semantics/Analysis frontend.

using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Backend;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Integration;

/// <summary>
/// Verifies that Phase 3 (body binding) fills the model's <see cref="Block"/> with
/// resolved statements and expressions: local declarations, literals, local
/// references (wired back to their declaration), and graceful placeholders for
/// constructs the walking-skeleton slice does not yet handle.
/// </summary>
public sealed class BodyBindingIntegrationTests : IDisposable
{
    private readonly string outputDir;

    public BodyBindingIntegrationTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistBodyBind_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TypedLocalDeclarationAndReturn_BindsToResolvedStatements()
    {
        var source = """
            namespace TestProject;

            public class Answerer
            {
                public int Answer()
                {
                    int x = 42;
                    return x;
                }
            }
            """;

        var method = GetMethod(BuildProgramModel(source), "Answerer", "Answer");
        Assert.NotNull(method.Body);
        var statements = method.Body!.Statements;

        Assert.Equal(2, statements.Count);

        // int x = 42;
        var declaration = Assert.IsType<LocalDeclarationStatement>(statements[0]);
        Assert.Equal("x", declaration.Local.Name);
        Assert.Equal("Standard.Int32", declaration.Local.Type.FullyQualifiedName);
        var literal = Assert.IsType<LiteralExpression>(declaration.Initializer);
        Assert.Equal(42L, literal.Value);
        Assert.Equal("Standard.Int32", literal.Type.FullyQualifiedName);

        // return x;  — the reference must resolve to the SAME local the declaration introduced.
        var returnStatement = Assert.IsType<ReturnStatement>(statements[1]);
        var reference = Assert.IsType<LocalReferenceExpression>(returnStatement.Value);
        Assert.Same(declaration.Local, reference.Local);
        Assert.Equal("Standard.Int32", reference.Type.FullyQualifiedName);
    }

    [Fact]
    public void EmptyMethodBody_BindsToEmptyStatementList()
    {
        var source = """
            namespace TestProject;

            public class Empty
            {
                public void Nothing()
                {
                }
            }
            """;

        var method = GetMethod(BuildProgramModel(source), "Empty", "Nothing");
        Assert.NotNull(method.Body);
        Assert.Empty(method.Body!.Statements);
    }

    [Fact]
    public void UnsupportedConstruct_BindsToPlaceholder_WithoutCrashing()
    {
        // Binary operators are not part of the walking-skeleton slice. The binder
        // must degrade to an UnresolvedExpression placeholder rather than throwing,
        // so Phase 3 stays safe over arbitrary source programs.
        var source = """
            namespace TestProject;

            public class Adder
            {
                public int Sum()
                {
                    int a = 1;
                    return a + a;
                }
            }
            """;

        var method = GetMethod(BuildProgramModel(source), "Adder", "Sum");
        Assert.NotNull(method.Body);

        var returnStatement = Assert.IsType<ReturnStatement>(method.Body!.Statements[^1]);
        _ = Assert.IsType<UnresolvedExpression>(returnStatement.Value);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Harness
    // ═══════════════════════════════════════════════════════════════════════

    private static Method GetMethod(IProgramModel model, string typeName, string methodName)
    {
        var type = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN[$"TestProject.{typeName}"]);
        return type.MethodsList.Single(m => m.Name == methodName);
    }

    private IProgramModel BuildProgramModel(string source, string fileName = "test.mist")
    {
        var lexer = new MistLexer();
        var parser = new MistParser();
        var diagnostics = new DiagnosticBag();

        var sourceText = new SourceText(fileName, source);
        var tokens = lexer.Lex(sourceText, diagnostics);
        var parseResult = parser.Parse(tokens, diagnostics);

        Assert.NotNull(parseResult.Root);
        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        var unit = new CompilationUnit(sourceText);
        var compilation = new MistCompilation(ImmutableArray.Create(unit));

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(fileName),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var buildDiagnostics = new DiagnosticBag();
        var programModel = ProgramModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            options,
            buildDiagnostics,
            new StandardTypesAmbientProvider());

        Assert.NotNull(programModel);
        return programModel!;
    }

    /// <summary>
    /// Ambient provider that registers only the Standard Library type shells, so
    /// primitive aliases such as <c>int</c> resolve to <c>Standard.Int32</c> without
    /// loading any CLR assemblies.
    /// </summary>
    private sealed class StandardTypesAmbientProvider : IAmbientTypeProvider
    {
        public void BuildAndRegister(ProgramModelClass programModel)
        {
            AmbientProviderTestHelper.RegisterStandardTypes(programModel);

            // Mark the Standard shells as ambient so the Phase 2a source-type build
            // loop skips them (they carry no AST declaration to resolve), matching how
            // the real ambient provider registers projected types.
            foreach(var type in programModel.TypesByFQN.Values)
            {
                if(type is MistType { NamespaceName: "Standard" } standardType)
                {
                    standardType.Origin = TypeOrigin.Ambient;
                }
            }
        }
    }
}