// Feature: void-unit-type, Property 5: Void array restriction
// **Validates: Requirements 8.1, 8.2**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying that void arrays are rejected by the type checker.
///
/// Property 5: For any program containing a `void[]` type declaration or a `new void[n]`
/// array creation expression, the type checker SHALL emit diagnostic `MIST_TC_0034`.
/// </summary>
public class VoidArrayRestrictionPropertyTests
{
    private readonly SemanticModelBuilder builder = new();
    private readonly TypeCheckerAnalyzer analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast)
    {
        var compilation = MakeCompilation();
        var diagnostics = new DiagnosticBag();
        return builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    // ─── Test data models ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// The context in which a void array appears.
    /// </summary>
    public enum VoidArrayContext
    {
        /// <summary><c>Void[] x = new Void[n];</c> — variable declaration with void array type (Req 8.1)</summary>
        Declaration,

        /// <summary><c>var x = new void[n];</c> — array creation expression with void element type (Req 8.2)</summary>
        Creation,
    }

    /// <summary>
    /// A scenario combining a void array context with random variable name and array size.
    /// </summary>
    public record VoidArrayScenario(
        VoidArrayContext Context,
        string VarName,
        int ArraySize)
    {
        public override string ToString() =>
            $"{Context} (var={VarName}, size={ArraySize})";
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] VarNames =
        ["x", "y", "arr", "items", "data", "values", "list", "buf"];

    public static Gen<VoidArrayScenario> GenScenario()
    {
        return from context in Gen.Elements(
                   VoidArrayContext.Declaration,
                   VoidArrayContext.Creation)
               from varName in Gen.Elements(VarNames)
               from arraySize in Gen.Choose(0, 100)
               select new VoidArrayScenario(context, varName, arraySize);
    }

    public static Arbitrary<VoidArrayScenario> ArbitraryScenario() =>
        GenScenario().ToArbitrary();

    // ─── AST construction helpers ────────────────────────────────────────

    /// <summary>
    /// Creates a standard Main method for the entry point.
    /// </summary>
    private static MethodDeclarationNode MakeMainMethod(ImmutableArray<StatementNode>? body = null)
    {
        return new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = body ?? ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };
    }

    /// <summary>
    /// Creates a <c>new void[n]</c> array creation expression.
    /// </summary>
    private static ExpressionNode MakeVoidArrayCreation(int size)
    {
        var primary = new ArrayCreationExpressionPrimaryNode
        {
            Type = new NamedElementTypeRefElementTypeRefNode
            {
                Name = new QualifiedIdentifierNode { IDENTIFIER = Tok("void") },
            },
            Size = MistAstGenerator.WrapPrimary(
                new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok(size.ToString()) }),
        };
        return MistAstGenerator.WrapPrimary(primary);
    }

    // ─── Scenario builders ───────────────────────────────────────────────

    /// <summary>
    /// Builds a program with a <c>Void[] x = new void[n];</c> declaration.
    /// Uses <c>NamedSingleTypeRefSingleTypeRefNode</c> with "Void" identifier and ArrayRank
    /// to represent the <c>Void[]</c> type reference.
    /// Expected: MIST_TC_0034 diagnostic (Req 8.1).
    /// </summary>
    private static CompilationUnitNode BuildVoidArrayDeclaration(VoidArrayScenario scenario)
    {
        var arrayCreationExpr = MakeVoidArrayCreation(scenario.ArraySize);

        // Void[] type reference: NamedTypeRef with "Void" identifier and ArrayRank set
        var voidArrayTypeRef = new TypeReferenceNode
        {
            First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode { IDENTIFIER = Tok("Void") },
                ArrayRank = new UnsizedArrayRankArrayRankSuffixNode(),
            }
        };

        var varDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = voidArrayTypeRef,
                Name = Tok(scenario.VarName),
                Initializer = arrayCreationExpr,
            },
        };

        var mainBody = ImmutableArray.Create<StatementNode>(varDecl);

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = MakeMainMethod(mainBody) }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Builds a program with <c>var x = new void[n];</c> — an inferred variable
    /// initialized with a void array creation expression.
    /// Expected: MIST_TC_0034 diagnostic (Req 8.2).
    /// </summary>
    private static CompilationUnitNode BuildVoidArrayCreation(VoidArrayScenario scenario)
    {
        var arrayCreationExpr = MakeVoidArrayCreation(scenario.ArraySize);

        var varDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode
            {
                Name = Tok(scenario.VarName),
                Initializer = arrayCreationExpr,
            },
        };

        var mainBody = ImmutableArray.Create<StatementNode>(varDecl);

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = MakeMainMethod(mainBody) }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 5: For any program containing a `void[]` type declaration or a `new void[n]`
    /// array creation expression, the type checker SHALL emit diagnostic `MIST_TC_0034`.
    ///
    /// **Validates: Requirements 8.1, 8.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(VoidArrayRestrictionPropertyTests) }, MaxTest = 100)]
    public bool VoidArray_ProducesDiagnostic(VoidArrayScenario scenario)
    {
        var ast = scenario.Context switch
        {
            VoidArrayContext.Declaration => BuildVoidArrayDeclaration(scenario),
            VoidArrayContext.Creation => BuildVoidArrayCreation(scenario),
            _ => throw new ArgumentOutOfRangeException(),
        };

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var errors = diagnostics.Where(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Id.Value == "MIST_TC_0034").ToList();

        return errors.Count > 0;
    }
}