// Feature: enums, Property 8: Implicit conversion to underlying type
// **Validates: Requirements 7.1, 7.2**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Generators;

namespace Mist.Tests.Analysis;

/// <summary>
/// Describes a scenario for testing implicit conversion from enum to underlying type.
/// </summary>
public sealed class EnumImplicitConversionScenario
{
    public string EnumName { get; }
    public IReadOnlyList<string> MemberNames { get; }
    public bool UsesExplicitTypeAnnotation { get; }

    public EnumImplicitConversionScenario(
        string enumName,
        IReadOnlyList<string> memberNames,
        bool usesExplicitTypeAnnotation)
    {
        EnumName = enumName;
        MemberNames = memberNames;
        UsesExplicitTypeAnnotation = usesExplicitTypeAnnotation;
    }

    public override string ToString()
    {
        return $"enum {EnumName} (members: {MemberNames.Count}, explicitType={UsesExplicitTypeAnnotation})";
    }
}

/// <summary>
/// Property-based tests for implicit conversion from enum to underlying type.
///
/// **Validates: Requirements 7.1, 7.2**
///
/// For any enum value, assigning it to a variable of the underlying type or
/// passing it as an argument where the underlying type is expected SHALL be
/// accepted without error.
/// </summary>
public class EnumImplicitConversionPropertyTests
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

    private static readonly string[] EnumNames =
        ["Direction", "Color", "Status", "Priority", "Level"];

    private static readonly string[] MemberNamePool =
        ["North", "South", "East", "West", "Red", "Green", "Blue",
         "Active", "Inactive", "Pending"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    private static Gen<EnumImplicitConversionScenario> GenScenario()
    {
        return from name in Gen.Elements(EnumNames)
               from memberCount in Gen.Choose(1, 5)
               from members in Gen.Shuffle(MemberNamePool)
                   .Select(m => m.Take(memberCount).ToArray())
               from hasAnnotation in Gen.Elements(true, false)
               select new EnumImplicitConversionScenario(name, members, hasAnnotation);
    }

    public static Arbitrary<EnumImplicitConversionScenario> ArbitraryScenario()
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

    private static CompilationUnitNode BuildAst(EnumImplicitConversionScenario scenario)
    {
        // Build enum declaration with integer values.
        var members = new List<EnumMemberNode>();
        for(var i = 0; i < scenario.MemberNames.Count; i++)
        {
            members.Add(MakeEnumMember(scenario.MemberNames[i], i));
        }

        var enumDecl = new EnumDeclarationNode
        {
            Name = Tok(scenario.EnumName),
            Modifiers = ImmutableArray<ModifierNode>.Empty,
            TypeAnnotation = scenario.UsesExplicitTypeAnnotation ? MakeIntTypeRef() : null,
            Members = MakeEnumMemberList(members),
        };

        var enumTypeDecl = new EnumTypeTypeDeclarationNode
        {
            EnumDeclaration = enumDecl,
        };

        // Build a class with Main that assigns enum member to underlying type variable.
        // int x = EnumName.FirstMember;  (implicit conversion enum → int)
        var firstMember = scenario.MemberNames[0];
        var memberAccessExpr = MakeMemberAccessExpression(scenario.EnumName, firstMember);

        var varDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = MakeIntTypeRef(),
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

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var classTypeDecl = new BodyTypeTypeDeclarationNode
        {
            TypeBody = classDecl,
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(enumTypeDecl, classTypeDecl)
                .AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8: Implicit conversion to underlying type
    //
    // **Validates: Requirements 7.1, 7.2**
    //
    // For any enum value, assigning it to a variable of the underlying type
    // SHALL be accepted without error.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        Arbitrary = new[] { typeof(EnumImplicitConversionPropertyTests) },
        MaxTest = 100)]
    public Property EnumToUnderlyingType_IsAcceptedWithoutError(
        EnumImplicitConversionScenario scenario)
    {
        var ast = BuildAst(scenario);
        var (model, buildDiagnostics) = BuildModelWithDiagnostics(ast);
        var tcDiagnostics = RunTypeChecker(model);

        // Combine all diagnostics and filter to errors, ignoring entry point.
        var allErrors = buildDiagnostics.Concat(tcDiagnostics)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value != "MIST_TC_0017")
            .ToList();

        if(allErrors.Count > 0)
        {
            return false.ToProperty()
                .Label($"Expected no errors for implicit conversion of {scenario.EnumName} to int, " +
                       $"got: [{string.Join("; ", allErrors.Select(e => $"{e.Id.Value}: {e.Message}"))}]");
        }

        return true.ToProperty()
            .Label($"Enum '{scenario.EnumName}' implicitly converts to underlying type without error");
    }
}