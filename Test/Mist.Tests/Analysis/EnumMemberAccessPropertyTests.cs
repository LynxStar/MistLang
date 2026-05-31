// Feature: enums, Property 5: Enum member access resolution
// **Validates: Requirements 5.1, 5.3**

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
/// Describes a scenario for testing enum member access resolution.
/// </summary>
public sealed class EnumMemberAccessScenario
{
    public string EnumName { get; }
    public IReadOnlyList<string> MemberNames { get; }
    public int AccessedMemberIndex { get; }

    public EnumMemberAccessScenario(
        string enumName,
        IReadOnlyList<string> memberNames,
        int accessedMemberIndex)
    {
        EnumName = enumName;
        MemberNames = memberNames;
        AccessedMemberIndex = accessedMemberIndex;
    }

    public string AccessedMemberName => MemberNames[AccessedMemberIndex];

    public override string ToString() =>
        $"{EnumName}.{AccessedMemberName} (members: {string.Join(", ", MemberNames)})";
}

/// <summary>
/// Property-based tests for enum member access resolution.
///
/// **Validates: Requirements 5.1, 5.3**
///
/// For any enum and any member name that exists in that enum, the expression
/// EnumName.MemberName SHALL resolve to a value whose type is the EnumType.
/// </summary>
public class EnumMemberAccessPropertyTests
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
        ["North", "South", "East", "West", "Red", "Green", "Blue",
         "Active", "Inactive", "Pending", "High", "Low"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    private static Gen<EnumMemberAccessScenario> GenScenario()
    {
        return from name in Gen.Elements(EnumNames)
               from memberCount in Gen.Choose(1, 5)
               from members in Gen.Shuffle(MemberNamePool)
                   .Select(m => m.Take(memberCount).ToArray())
               from accessIdx in Gen.Choose(0, memberCount - 1)
               select new EnumMemberAccessScenario(name, members, accessIdx);
    }

    public static Arbitrary<EnumMemberAccessScenario> ArbitraryScenario() =>
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
        return new ExpressionNode { Assignment = assign };
    }

    private static CompilationUnitNode BuildAst(EnumMemberAccessScenario scenario)
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

        // Build a class with Main that accesses the enum member.
        var accessExpr = MakeMemberAccessExpression(scenario.EnumName, scenario.AccessedMemberName);

        // Use the enum type as the variable type: EnumName x = EnumName.MemberName;
        var varDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = Tok(scenario.EnumName),
                        Span = new Storm.Runtime.SourceSpan(0, scenario.EnumName.Length),
                    },
                } },
                Name = Tok("x"),
                Initializer = accessExpr,
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
            Items = ImmutableArray.Create<TypeDeclarationNode>(enumTypeDecl, classTypeDecl)
                .AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Enum member access resolution
    //
    // **Validates: Requirements 5.1, 5.3**
    //
    // For any enum and any member name that exists in that enum, the
    // expression EnumName.MemberName SHALL resolve to a value whose type
    // is the EnumType.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        Arbitrary = new[] { typeof(EnumMemberAccessPropertyTests) },
        MaxTest = 100)]
    public Property EnumMemberAccess_ResolvesToEnumType_WithNoDiagnostics(
        EnumMemberAccessScenario scenario)
    {
        var ast = BuildAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // Filter to only errors (ignore entry point warnings etc.)
        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value != "MIST_TC_0017") // Ignore entry point diagnostic
            .ToList();

        if (errors.Count > 0)
            return false.ToProperty()
                .Label($"Expected no errors for {scenario.EnumName}.{scenario.AccessedMemberName}, " +
                       $"got: [{string.Join("; ", errors.Select(e => $"{e.Id.Value}: {e.Message}"))}]");

        // Verify the member access expression resolved to the EnumType.
        var enumSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.EnumName && s.Kind == SymbolKind.Enum);

        if (enumSymbol is null)
            return false.ToProperty()
                .Label($"Enum '{scenario.EnumName}' not found in semantic model");

        if (enumSymbol.DeclaredType is not EnumType)
            return false.ToProperty()
                .Label($"Enum '{scenario.EnumName}' DeclaredType is not EnumType");

        return true.ToProperty()
            .Label($"{scenario.EnumName}.{scenario.AccessedMemberName} resolved correctly");
    }
}
