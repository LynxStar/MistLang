// Feature: enums, Property 7: Enum usable as type reference
// **Validates: Requirements 6.1, 6.2, 6.3, 6.4**

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
/// The type position being tested for enum type reference usage.
/// </summary>
public enum TypePosition
{
    /// <summary>Variable declaration: EnumName x = EnumName.Member;</summary>
    Variable,

    /// <summary>Method parameter: void M(EnumName p) { }</summary>
    Parameter,

    /// <summary>Method return type: EnumName M() { return EnumName.Member; }</summary>
    ReturnType,

    /// <summary>Field declaration: EnumName f;</summary>
    Field,
}

/// <summary>
/// Describes a scenario for testing enum as type reference.
/// </summary>
public sealed class EnumTypeReferenceScenario
{
    public string EnumName { get; }
    public IReadOnlyList<string> MemberNames { get; }
    public TypePosition Position { get; }

    public EnumTypeReferenceScenario(
        string enumName,
        IReadOnlyList<string> memberNames,
        TypePosition position)
    {
        EnumName = enumName;
        MemberNames = memberNames;
        Position = position;
    }

    public override string ToString()
    {
        return $"enum {EnumName} used as {Position} (members: {string.Join(", ", MemberNames)})";
    }
}

/// <summary>
/// Property-based tests for enum usable as type reference.
///
/// **Validates: Requirements 6.1, 6.2, 6.3, 6.4**
///
/// For any enum, using its name as a type annotation in variable declarations,
/// parameter types, return types, and field types SHALL be accepted without error.
/// </summary>
public class EnumTypeReferencePropertyTests
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

    private static Gen<EnumTypeReferenceScenario> GenScenario()
    {
        return from name in Gen.Elements(EnumNames)
               from memberCount in Gen.Choose(1, 4)
               from members in Gen.Shuffle(MemberNamePool)
                   .Select(m => m.Take(memberCount).ToArray())
               from position in Gen.Elements(
                   TypePosition.Variable,
                   TypePosition.Parameter,
                   TypePosition.ReturnType,
                   TypePosition.Field)
               select new EnumTypeReferenceScenario(name, members, position);
    }

    public static Arbitrary<EnumTypeReferenceScenario> ArbitraryScenario()
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

    private static CompilationUnitNode BuildAst(EnumTypeReferenceScenario scenario)
    {
        // Build enum declaration.
        var members = new List<EnumMemberNode>();
        for(var i = 0; i < scenario.MemberNames.Count; i++)
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

        // Build a class that uses the enum in the specified position.
        var firstMember = scenario.MemberNames[0];
        var memberAccessExpr = MakeMemberAccessExpression(scenario.EnumName, firstMember);
        var classMembers = ImmutableArray.CreateBuilder<MemberDeclarationNode>();

        switch(scenario.Position)
        {
            case TypePosition.Variable:
                {
                    // static void Main() { EnumName x = EnumName.FirstMember; }
                    var varDecl = new VariableDeclarationStatementStatementNode
                    {
                        VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
                        {
                            Type = MakeEnumTypeRef(scenario.EnumName),
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
                    classMembers.Add(new MethodMemberMemberDeclarationNode { Method = mainMethod });
                    break;
                }

            case TypePosition.Parameter:
                {
                    // static void UseEnum(EnumName p) { }
                    var param = new ParameterNode
                    {
                        Type = MakeEnumTypeRef(scenario.EnumName),
                        Name = Tok("p"),
                    };
                    var paramList = new ParameterListNode { Parameter = param };

                    var useMethod = new MethodDeclarationNode
                    {
                        Name = Tok("UseEnum"),
                        ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                        Parameters = paramList,
                        Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
                        Modifiers = ImmutableArray.Create<ModifierNode>(
                            new PublicModifierModifierNode(),
                            new StaticModifierModifierNode()),
                    };
                    classMembers.Add(new MethodMemberMemberDeclarationNode { Method = useMethod });

                    // static void Main() { }
                    var mainMethod = new MethodDeclarationNode
                    {
                        Name = Tok("Main"),
                        ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                        Parameters = null,
                        Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
                        Modifiers = ImmutableArray.Create<ModifierNode>(
                            new PublicModifierModifierNode(),
                            new StaticModifierModifierNode()),
                    };
                    classMembers.Add(new MethodMemberMemberDeclarationNode { Method = mainMethod });
                    break;
                }

            case TypePosition.ReturnType:
                {
                    // static EnumName GetValue() { return EnumName.FirstMember; }
                    var returnStmt = new ReturnStatementStatementNode
                    {
                        ReturnStatement = new ReturnStatementNode { Value = memberAccessExpr },
                    };

                    var getMethod = new MethodDeclarationNode
                    {
                        Name = Tok("GetValue"),
                        ReturnType = MakeEnumTypeRef(scenario.EnumName),
                        Parameters = null,
                        Body = new MethodBlockBodyMethodBodyNode
                        {
                            Block = new BlockNode
                            {
                                Statements = ImmutableArray.Create<StatementNode>(returnStmt),
                            },
                        },
                        Modifiers = ImmutableArray.Create<ModifierNode>(
                            new PublicModifierModifierNode(),
                            new StaticModifierModifierNode()),
                    };
                    classMembers.Add(new MethodMemberMemberDeclarationNode { Method = getMethod });

                    // static void Main() { }
                    var mainMethod = new MethodDeclarationNode
                    {
                        Name = Tok("Main"),
                        ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                        Parameters = null,
                        Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
                        Modifiers = ImmutableArray.Create<ModifierNode>(
                            new PublicModifierModifierNode(),
                            new StaticModifierModifierNode()),
                    };
                    classMembers.Add(new MethodMemberMemberDeclarationNode { Method = mainMethod });
                    break;
                }

            case TypePosition.Field:
                {
                    // EnumName f;
                    var fieldDecl = new FieldDeclarationNode
                    {
                        Name = Tok("f"),
                        Type = MakeEnumTypeRef(scenario.EnumName),
                        Modifiers = ImmutableArray<ModifierNode>.Empty,
                    };
                    classMembers.Add(new FieldMemberMemberDeclarationNode { Field = fieldDecl });

                    // static void Main() { }
                    var mainMethod = new MethodDeclarationNode
                    {
                        Name = Tok("Main"),
                        ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                        Parameters = null,
                        Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
                        Modifiers = ImmutableArray.Create<ModifierNode>(
                            new PublicModifierModifierNode(),
                            new StaticModifierModifierNode()),
                    };
                    classMembers.Add(new MethodMemberMemberDeclarationNode { Method = mainMethod });
                    break;
                }
        }

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = Tok("Program"),
            Members = classMembers.ToImmutable(),
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
    // Property 7: Enum usable as type reference
    //
    // **Validates: Requirements 6.1, 6.2, 6.3, 6.4**
    //
    // For any enum, using its name as a type annotation in variable
    // declarations, parameter types, return types, and field types SHALL
    // be accepted without error.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        Arbitrary = new[] { typeof(EnumTypeReferencePropertyTests) },
        MaxTest = 100)]
    public Property EnumAsTypeReference_IsAcceptedWithoutError(
        EnumTypeReferenceScenario scenario)
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
                .Label($"Expected no errors for enum '{scenario.EnumName}' as {scenario.Position}, " +
                       $"got: [{string.Join("; ", allErrors.Select(e => $"{e.Id.Value}: {e.Message}"))}]");
        }

        return true.ToProperty()
            .Label($"Enum '{scenario.EnumName}' accepted as {scenario.Position}");
    }
}