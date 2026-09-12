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
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Backend;
using CompilationUnit = Mist.Driver.Contracts.CompilationUnit;
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
    public void BinaryOperator_BindsToBinaryExpression()
    {
        // Binary operators are now handled by the operator binder. The binder
        // produces a BinaryExpression wrapping the two operands.
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
        var binary = Assert.IsType<BinaryExpression>(returnStatement.Value);
        Assert.Equal(BinaryOperatorKind.Add, binary.OperatorKind);
        _ = Assert.IsType<LocalReferenceExpression>(binary.Left);
        _ = Assert.IsType<LocalReferenceExpression>(binary.Right);
    }

    [Fact]
    public void SimpliestSample_BindsWithNoSentinels()
    {
        var source = """
            using Standard;
            namespace Simpliest;
            public class Program
            {
                public static void Main(string[] args)
                {
                    RunDemo();
                }
                public static void RunDemo()
                {
                    Console.WriteLine("General KENOBI!");
                }
            }
            """;

        var model = BuildProgramModel(source, namespaceRoot: "Simpliest");

        // Main body: RunDemo() — bare method call on the enclosing type.
        var main = GetMethod(model, "Simpliest", "Program", "Main");
        Assert.NotNull(main.Body);
        AssertNoSentinels(main.Body!);

        // RunDemo body: Console.WriteLine("General KENOBI!") — static member access + invocation.
        var runDemo = GetMethod(model, "Simpliest", "Program", "RunDemo");
        Assert.NotNull(runDemo.Body);
        AssertNoSentinels(runDemo.Body!);
    }

    [Fact]
    public void ArrayLiteral_InfersElementTypeFromElements()
    {
        var source = """
            namespace TestProject;

            public class Literals
            {
                public void Make()
                {
                    var numbers = [1, 2, 3];
                }
            }
            """;

        var method = GetMethod(BuildProgramModel(source), "Literals", "Make");
        Assert.NotNull(method.Body);
        var declaration = Assert.IsType<LocalDeclarationStatement>(method.Body!.Statements[0]);

        var literal = Assert.IsType<ArrayLiteralExpression>(declaration.Initializer);
        Assert.Equal(3, literal.Elements.Count);
        Assert.Equal("Standard.Int32", literal.ElementType.FullyQualifiedName);

        // `var` adopts the literal's ARRAY type, not its element type.
        AssertArrayOf("Standard.Int32", declaration.Local.Type);
    }

    /// <summary>
    /// A three-element array literal and a <c>int[3]</c> type annotation must produce the
    /// SAME interned type instance. Both encode as
    /// <c>Standard.Array&lt;Int32, LiftedType(3)&gt;</c>, and the interner keys type
    /// arguments by reference identity, so this only holds if both paths lift the length
    /// through the same call with the same Mist type and the same boxed value.
    /// <para>
    /// This is a regression guard on that agreement. The two paths are far apart —
    /// <c>LiteralBinder</c> passes an element count, <c>ScopeResolver.ResolveArrayType</c>
    /// parses a rank suffix — and they previously diverged by boxing an <c>int</c> on one
    /// side and a <c>long</c> on the other, which silently produced two distinct types for
    /// the same array.
    /// </para>
    /// </summary>
    [Fact]
    public void ArrayLiteral_AndSizedArrayAnnotation_ShareOneInternedType()
    {
        var source = """
            namespace TestProject;

            public class Literals
            {
                public void Make()
                {
                    var fromLiteral = [6, 7, 8];
                    int[3] fromAnnotation = default;
                }
            }
            """;

        var method = GetMethod(BuildProgramModel(source), "Literals", "Make");
        Assert.NotNull(method.Body);

        var literalDeclaration = Assert.IsType<LocalDeclarationStatement>(method.Body!.Statements[0]);
        var annotationDeclaration = Assert.IsType<LocalDeclarationStatement>(method.Body!.Statements[1]);

        var literal = Assert.IsType<ArrayLiteralExpression>(literalDeclaration.Initializer);
        Assert.Equal(3, literal.Elements.Count);

        Assert.Same(literalDeclaration.Local.Type, annotationDeclaration.Local.Type);
    }

    [Fact]
    public void EmptyArrayLiteral_TakesElementTypeFromDeclaredType()
    {
        var source = """
            namespace TestProject;

            public class Literals
            {
                public void Make()
                {
                    int[] empty = [];
                }
            }
            """;

        var method = GetMethod(BuildProgramModel(source), "Literals", "Make");
        Assert.NotNull(method.Body);
        var declaration = Assert.IsType<LocalDeclarationStatement>(method.Body!.Statements[0]);

        // With no elements the element type can only come from the declared type.
        var literal = Assert.IsType<ArrayLiteralExpression>(declaration.Initializer);
        Assert.Empty(literal.Elements);
        Assert.Equal("Standard.Int32", literal.ElementType.FullyQualifiedName);
    }

    [Fact]
    public void NestedArrayLiteral_InfersArrayAsTheElementType()
    {
        var source = """
            namespace TestProject;

            public class Literals
            {
                public void Make()
                {
                    var grid = [[1, 2], [3, 4]];
                }
            }
            """;

        var method = GetMethod(BuildProgramModel(source), "Literals", "Make");
        Assert.NotNull(method.Body);
        var declaration = Assert.IsType<LocalDeclarationStatement>(method.Body!.Statements[0]);

        var outer = Assert.IsType<ArrayLiteralExpression>(declaration.Initializer);
        Assert.Equal(2, outer.Elements.Count);

        // The outer element type is itself int[], which is what makes the local int[][].
        AssertArrayOf("Standard.Int32", outer.ElementType);

        var first = Assert.IsType<ArrayLiteralExpression>(outer.Elements[0]);
        Assert.Equal("Standard.Int32", first.ElementType.FullyQualifiedName);

        // Both inner literals must resolve to the SAME interned array type. That
        // reference identity is precisely what lets the common-element-type rule
        // accept them, so assert it rather than just comparing names.
        var second = Assert.IsType<ArrayLiteralExpression>(outer.Elements[1]);
        Assert.Same(first.Type, second.Type);
    }

    private static void AssertArrayOf(string expectedElementFqn, IMistType type)
    {
        var constructed = Assert.IsType<ConstructedType>(type);
        Assert.Equal("Array<,>", constructed.Definition.Name);
        Assert.Equal(expectedElementFqn, constructed.TypeArguments[0].FullyQualifiedName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Harness
    // ═══════════════════════════════════════════════════════════════════════

    private static Method GetMethod(IProgramModel model, string typeName, string methodName)
    {
        var type = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN[$"TestProject.{typeName}"]);
        return type.MethodsList.Single(m => m.Name == methodName);
    }

    private static Method GetMethod(IProgramModel model, string namespaceName, string typeName, string methodName)
    {
        var type = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN[$"{namespaceName}.{typeName}"]);
        return type.MethodsList.Single(m => m.Name == methodName);
    }

    private static void AssertNoSentinels(Block block)
    {
        foreach(var statement in block.Statements)
        {
            Assert.IsNotType<UnsupportedStatement>(statement);

            switch(statement)
            {
                case LocalDeclarationStatement decl:
                    if(decl.Initializer is not null)
                    {
                        AssertExpressionResolved(decl.Initializer);
                    }

                    break;
                case ReturnStatement ret:
                    if(ret.Value is not null)
                    {
                        AssertExpressionResolved(ret.Value);
                    }

                    break;
                case ExpressionStatement exprStmt:
                    AssertExpressionResolved(exprStmt.Expression);
                    break;
                case Block nested:
                    AssertNoSentinels(nested);
                    break;
            }
        }
    }

    private static void AssertExpressionResolved(Expression expr)
    {
        Assert.IsNotType<UnresolvedExpression>(expr);
        Assert.IsNotType<ErrorExpression>(expr);

        switch(expr)
        {
            case InvocationExpression invocation:
                AssertExpressionResolved(invocation.Receiver);
                foreach(var arg in invocation.Arguments)
                {
                    AssertExpressionResolved(arg);
                }

                break;
            case MemberAccessExpression memberAccess:
                AssertExpressionResolved(memberAccess.Receiver);
                break;
        }
    }

    private IProgramModel BuildProgramModel(string source, string fileName = "test.mist", string namespaceRoot = "TestProject")
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
            ProjectName: namespaceRoot,
            NamespaceRoot: namespaceRoot,
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

            // Register a Console shell with a WriteLine method so that
            // Console.WriteLine("...") resolves during body binding.
            var consoleScope = new Scope(null);
            var consoleType = new ClassType("Console", "Standard", consoleScope, null, null!);
            _ = programModel.RegisterType(consoleType);

            var voidType = programModel.TypesByFQN["Standard.Void"];
            var stringType = programModel.TypesByFQN["Standard.String"];
            var writeLineMethod = new Method(
                "WriteLine",
                voidType,
                [new Parameter("value", stringType)],
                new MemberModifiers { IsStatic = true });
            consoleType.MethodsList.Add(writeLineMethod);

            // Attribute the Standard shells to the ambient assembly so the Phase 2a
            // source-type build loop skips them (they carry no AST declaration to
            // resolve), matching how the real ambient provider registers projected types.
            foreach(var type in programModel.TypesByFQN.Values)
            {
                if(type is DeclaredType { NamespaceName: "Standard" } standardType)
                {
                    standardType.Assembly = MistAssembly.Ambient;
                }
            }
        }
    }
}