// Feature: enums, Property 6: Invalid member access produces error
// **Validates: Requirements 5.2**

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
/// Describes a scenario for testing invalid enum member access.
/// </summary>
public sealed class EnumInvalidMemberAccessScenario
{
    public string EnumName { get; }
    public IReadOnlyList<string> MemberNames { get; }
    public string InvalidMemberName { get; }

    public EnumInvalidMemberAccessScenario(
        string enumName,
        IReadOnlyList<string> memberNames,
        string invalidMemberName)
    {
        EnumName = enumName;
        MemberNames = memberNames;
        InvalidMemberName = invalidMemberName;
    }

    public override string ToString() =>
        $"{EnumName}.{InvalidMemberName} (valid members: {string.Join(", ", MemberNames)})";
}

/// <summary>
/// Property-based tests for invalid enum member access.
///
/// **Validates: Requirements 5.2**
///
/// For any enum and any identifier that is NOT a member of that enum,
/// accessing EnumName.InvalidName SHALL produce a diagnostic error (MIST_SB_0082).
/// </summary>
public class EnumInvalidMemberAccessPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    private static Storm.Runtime.Token Tok(string lexeme) =>
        new(kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);

    private static ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast)
    {
        var sourceText = new SourceText("test.mist", "");
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return Builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
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
        ["North", "South", "East", "West", "Red", "Green", "Blue"];

    /// <summary>
    /// Names that are guaranteed NOT to be in the MemberNamePool.
    /// </summary>
    private static readonly string[] InvalidNames =
        ["Unknown", "Missing", "Bogus", "Fake", "Invalid", "None", "Undefined"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    private static Gen<EnumInvalidMemberAccessScenario> GenScenario()
    {
        return from name in Gen.Elements(EnumNames)
               from memberCount in Gen.Choose(1, 5)
               from members in Gen.Shuffle(MemberNamePool)
                   .Select(m => m.Take(memberCount).ToArray())
               from invalidName in Gen.Elements(InvalidNames)
               select new EnumInvalidMemberAccessScenario(name, members, invalidName);
    }

    public static Arbitrary<EnumInvalidMemberAccessScenario> ArbitraryScenario() =>
        GenScenario().ToArbitrary();

    // ─── AST construction helpers ────────────────────────────────────────

    private static ExpressionNode MakeIntLiteralExpression(string value)
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = Tok(value),
        };
        return MistAstGenerator.WrapPrimary(primary);
    }

    private static EnumMemberNode MakeEnumMember(string name, int index) =>
        new()
        {
            Name = Tok(name),
            Value = MakeIntLiteralExpression(index.ToString()),
        };

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

    private static CompilationUnitNode BuildAst(EnumInvalidMemberAccessScenario scenario)
    {
        // Build enum declaration with integer values.
        var members = new List<EnumMemberNode>();
        for (var i = 0; i < scenario.MemberNames.Count; i++)
        {
            members.Add(MakeEnumMember(scenario.MemberNames[i], i));
        }

        var enumDecl = new EnumDeclarationNode
        {
            Name = Tok(scenario.EnumName),
            Modifiers = ImmutableArray<ModifierNode>.Empty,
            TypeAnnotation = null,
            Members = MakeEnumMemberList(members),
        };

        var enumTypeDecl = new EnumTypeTypeDeclarationNode
        {
            EnumDeclaration = enumDecl,
        };

        // Build a class with Main that accesses an invalid member.
        var accessExpr = MakeMemberAccessExpression(scenario.EnumName, scenario.InvalidMemberName);

        var exprStmt = new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode
            {
                Expression = accessExpr,
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
                    Statements = ImmutableArray.Create<StatementNode>(exprStmt),
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
            Items = ImmutableArray.Create<TypeDeclarationNode>(enumTypeDecl, classTypeDecl)
                .AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: Invalid member access produces error
    //
    // **Validates: Requirements 5.2**
    //
    // For any enum and any identifier that is NOT a member of that enum,
    // accessing EnumName.InvalidName SHALL produce a MIST_SB_0082 diagnostic.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        Arbitrary = new[] { typeof(EnumInvalidMemberAccessPropertyTests) },
        MaxTest = 100)]
    public Property InvalidEnumMemberAccess_ProducesDiagnostic(
        EnumInvalidMemberAccessScenario scenario)
    {
        var ast = BuildAst(scenario);
        var model = BuildModel(ast);

        // The MIST_SB_0082 diagnostic is emitted during semantic model
        // construction (Pass 2) when resolving the member access expression.
        // Check both the semantic model build diagnostics and type checker.
        var buildDiagnostics = new DiagnosticBag();
        var sourceText = new SourceText("test.mist", "");
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            buildDiagnostics);

        var allDiagnostics = buildDiagnostics.ToImmutableArray();

        // Also run type checker for completeness.
        var tcDiagnostics = RunTypeChecker(model);
        var combined = allDiagnostics.AddRange(tcDiagnostics);

        var hasExpectedError = combined.Any(d =>
            d.Id.Value == "MIST_SB_0082" &&
            d.Severity == DiagnosticSeverity.Error);

        if (!hasExpectedError)
            return false.ToProperty()
                .Label($"Expected MIST_SB_0082 for {scenario.EnumName}.{scenario.InvalidMemberName}, " +
                       $"got: [{string.Join("; ", combined.Select(e => $"{e.Id.Value}: {e.Message}"))}]");

        return true.ToProperty()
            .Label($"{scenario.EnumName}.{scenario.InvalidMemberName} correctly produced MIST_SB_0082");
    }
}
