// Feature: enums, Property 12: Nominal type distinction
// **Validates: Requirements 2.4**

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
using Mist.Tests.Generators;

namespace Mist.Tests.Analysis;

/// <summary>
/// Describes a scenario for testing nominal type distinction between two enums.
/// </summary>
public sealed class EnumNominalDistinctionScenario
{
    public string EnumNameA { get; }
    public string EnumNameB { get; }
    public IReadOnlyList<string> MemberNamesA { get; }
    public IReadOnlyList<string> MemberNamesB { get; }

    public EnumNominalDistinctionScenario(
        string enumNameA,
        string enumNameB,
        IReadOnlyList<string> memberNamesA,
        IReadOnlyList<string> memberNamesB)
    {
        EnumNameA = enumNameA;
        EnumNameB = enumNameB;
        MemberNamesA = memberNamesA;
        MemberNamesB = memberNamesB;
    }

    public override string ToString()
    {
        return $"{EnumNameA} vs {EnumNameB} (same underlying int)";
    }
}

/// <summary>
/// Property-based tests for nominal type distinction.
///
/// **Validates: Requirements 2.4**
///
/// For any two enum declarations with different names but identical underlying
/// types, assigning a value of one enum type to a variable of the other enum
/// type SHALL produce a type error.
/// </summary>
public class EnumNominalDistinctionPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return new(kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static (ISemanticModel Model, ImmutableArray<Diagnostic> BuildDiagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast)
    {
        var sourceText = new SourceText("test.mist", "");
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    private static ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        Analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    // ─── Known names for generation ─────────────────────────────────────

    /// <summary>
    /// Pairs of enum names that are always distinct from each other.
    /// </summary>
    private static readonly (string A, string B)[] EnumNamePairs =
    [
        ("Direction", "Priority"),
        ("Color", "Status"),
        ("Level", "Mode"),
        ("Shape", "Signal"),
        ("Weekday", "Month"),
    ];

    private static readonly string[] MemberPoolA =
        ["North", "South", "East", "West", "Up", "Down"];

    private static readonly string[] MemberPoolB =
        ["High", "Medium", "Low", "Critical", "Normal", "Minimal"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    private static Gen<EnumNominalDistinctionScenario> GenScenario()
    {
        return from pair in Gen.Elements(EnumNamePairs)
               from countA in Gen.Choose(1, 4)
               from countB in Gen.Choose(1, 4)
               from membersA in Gen.Shuffle(MemberPoolA)
                   .Select(m => m.Take(countA).ToArray())
               from membersB in Gen.Shuffle(MemberPoolB)
                   .Select(m => m.Take(countB).ToArray())
               select new EnumNominalDistinctionScenario(
                   pair.A, pair.B, membersA, membersB);
    }

    public static Arbitrary<EnumNominalDistinctionScenario> ArbitraryScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static ExpressionNode MakeIntLiteralExpression(string value)
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = Tok(value),
        };
        return MistAstGenerator.WrapPrimary(primary);
    }

    private static EnumMemberNode MakeEnumMember(string name, int index)
    {
        return new()
        {
            Name = Tok(name),
            Value = MakeIntLiteralExpression(index.ToString()),
        };
    }

    private static EnumMemberListNode MakeEnumMemberList(IReadOnlyList<EnumMemberNode> members)
    {
        var first = members[0];
        var rest = members.Skip(1).ToImmutableArray();
        return new EnumMemberListNode
        {
            EnumMember = first,
            EnumMembers = rest,
        };
    }

    private static TypeReferenceNode MakeIntTypeRef()
    {
        return new TypeReferenceNode
        {
            First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            }
        };
    }

    private static TypeReferenceNode MakeEnumTypeRef(string enumName)
    {
        return new TypeReferenceNode
        {
            First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode
                {
                    IDENTIFIER = Tok(enumName),
                    Span = new Storm.Runtime.SourceSpan(0, enumName.Length),
                },
            }
        };
    }

    private static ExpressionNode MakeMemberAccessExpression(string target, string memberName)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = Tok(target),
        };
        var suffix = new MemberAccessSuffixPostfixSuffixNode
        {
            Name = Tok(memberName),
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(suffix),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static CompilationUnitNode BuildAst(EnumNominalDistinctionScenario scenario)
    {
        // Build two enum declarations with the same underlying type (int).
        var membersA = new List<EnumMemberNode>();
        for(var i = 0; i < scenario.MemberNamesA.Count; i++)
        {
            membersA.Add(MakeEnumMember(scenario.MemberNamesA[i], i));
        }

        var enumDeclA = new EnumDeclarationNode
        {
            Name = Tok(scenario.EnumNameA),
            Modifiers = ImmutableArray<ModifierNode>.Empty,
            TypeAnnotation = MakeIntTypeRef(),
            Members = MakeEnumMemberList(membersA),
        };

        var enumTypeDeclA = new EnumTypeTypeDeclarationNode
        {
            EnumDeclaration = enumDeclA,
        };

        var membersB = new List<EnumMemberNode>();
        for(var i = 0; i < scenario.MemberNamesB.Count; i++)
        {
            membersB.Add(MakeEnumMember(scenario.MemberNamesB[i], i));
        }

        var enumDeclB = new EnumDeclarationNode
        {
            Name = Tok(scenario.EnumNameB),
            Modifiers = ImmutableArray<ModifierNode>.Empty,
            TypeAnnotation = MakeIntTypeRef(),
            Members = MakeEnumMemberList(membersB),
        };

        var enumTypeDeclB = new EnumTypeTypeDeclarationNode
        {
            EnumDeclaration = enumDeclB,
        };

        // Build a class with Main that cross-assigns:
        // EnumNameB x = EnumNameA.FirstMember;  (should be rejected)
        var firstMemberA = scenario.MemberNamesA[0];
        var memberAccessExpr = MakeMemberAccessExpression(scenario.EnumNameA, firstMemberA);

        var varDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = MakeEnumTypeRef(scenario.EnumNameB),
                Name = Tok("x"),
                Initializer = memberAccessExpr,
            },
        };

        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(varDecl),
                },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var classTypeDecl = new ClassTypeTypeDeclarationNode
        {
            ClassDeclaration = classDecl,
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                    enumTypeDeclA, enumTypeDeclB, classTypeDecl)
                .AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12: Nominal type distinction
    //
    // **Validates: Requirements 2.4**
    //
    // For any two enum declarations with different names but identical
    // underlying types, assigning a value of one enum type to a variable
    // of the other enum type SHALL produce a type error.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        Arbitrary = new[] { typeof(EnumNominalDistinctionPropertyTests) },
        MaxTest = 100)]
    public Property CrossEnumAssignment_ProducesTypeError(
        EnumNominalDistinctionScenario scenario)
    {
        var ast = BuildAst(scenario);
        var (model, buildDiagnostics) = BuildModelWithDiagnostics(ast);
        var tcDiagnostics = RunTypeChecker(model);

        // Combine all diagnostics.
        var combined = buildDiagnostics.AddRange(tcDiagnostics);

        // Look for a type error indicating the cross-assignment was rejected.
        var hasTypeError = combined.Any(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Id.Value != "MIST_TC_0017"); // Ignore entry point diagnostic

        if(!hasTypeError)
        {
            return false.ToProperty()
                .Label($"Expected type error for assigning {scenario.EnumNameA} to {scenario.EnumNameB}, " +
                       $"got: [{string.Join("; ", combined.Where(d => d.Severity == DiagnosticSeverity.Error).Select(e => $"{e.Id.Value}: {e.Message}"))}]");
        }

        return true.ToProperty()
            .Label($"Cross-assignment ({scenario.EnumNameA} → {scenario.EnumNameB}) correctly rejected");
    }
}