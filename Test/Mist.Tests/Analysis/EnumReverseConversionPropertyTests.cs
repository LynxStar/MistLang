// Feature: enums, Property 9: Reverse conversion rejected
// **Validates: Requirements 7.3**

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
/// Describes a scenario for testing reverse conversion rejection (underlying → enum).
/// </summary>
public sealed class EnumReverseConversionScenario
{
    public string EnumName { get; }
    public IReadOnlyList<string> MemberNames { get; }
    public bool UsesExplicitTypeAnnotation { get; }

    public EnumReverseConversionScenario(
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
/// Property-based tests for reverse conversion rejection.
///
/// **Validates: Requirements 7.3**
///
/// For any enum with underlying type T, assigning a value of type T to a
/// variable of the enum type SHALL produce a type error.
/// </summary>
public class EnumReverseConversionPropertyTests
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

    private static Gen<EnumReverseConversionScenario> GenScenario()
    {
        return from name in Gen.Elements(EnumNames)
               from memberCount in Gen.Choose(1, 5)
               from members in Gen.Shuffle(MemberNamePool)
                   .Select(m => m.Take(memberCount).ToArray())
               from hasAnnotation in Gen.Elements(true, false)
               select new EnumReverseConversionScenario(name, members, hasAnnotation);
    }

    public static Arbitrary<EnumReverseConversionScenario> ArbitraryScenario()
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

    private static CompilationUnitNode BuildAst(EnumReverseConversionScenario scenario)
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

        // Build a class with Main that assigns int literal to enum-typed variable.
        // EnumName x = 42;  (reverse conversion: int → enum, should be rejected)
        var intLiteral = MakeIntLiteralExpression("42");

        var varDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = MakeEnumTypeRef(scenario.EnumName),
                Name = Tok("x"),
                Initializer = intLiteral,
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
    // Property 9: Reverse conversion rejected
    //
    // **Validates: Requirements 7.3**
    //
    // For any enum with underlying type T, assigning a value of type T to
    // a variable of the enum type SHALL produce a type error.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        Arbitrary = new[] { typeof(EnumReverseConversionPropertyTests) },
        MaxTest = 100)]
    public Property UnderlyingTypeToEnum_ProducesTypeError(
        EnumReverseConversionScenario scenario)
    {
        var ast = BuildAst(scenario);
        var (model, buildDiagnostics) = BuildModelWithDiagnostics(ast);
        var tcDiagnostics = RunTypeChecker(model);

        // Combine all diagnostics.
        var combined = buildDiagnostics.AddRange(tcDiagnostics);

        // Look for the reverse conversion error (MIST_SB_0083) or a general
        // type mismatch error that indicates the assignment was rejected.
        var hasReverseConversionError = combined.Any(d =>
            d.Severity == DiagnosticSeverity.Error &&
            (d.Id.Value == "MIST_SB_0083" || d.Id.Value == "MIST_TC_0001"));

        if(!hasReverseConversionError)
        {
            return false.ToProperty()
                .Label($"Expected type error for assigning int to {scenario.EnumName}, " +
                       $"got: [{string.Join("; ", combined.Where(d => d.Severity == DiagnosticSeverity.Error).Select(e => $"{e.Id.Value}: {e.Message}"))}]");
        }

        return true.ToProperty()
            .Label($"Reverse conversion (int → {scenario.EnumName}) correctly rejected");
    }
}