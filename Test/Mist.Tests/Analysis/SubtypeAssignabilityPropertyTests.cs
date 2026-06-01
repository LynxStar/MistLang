// Feature: class-inheritance, Property 6: Subtype assignability
// **Validates: Requirements 5.1, 5.3, 5.4**

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

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying subtype assignability (derived assignable to base).
///
/// Property 6: For any class hierarchy of depth 1 or more, assigning a derived class
/// value to a variable, parameter, or return type declared as any of its ancestor
/// classes shall be accepted by the type checker without diagnostics.
/// </summary>
public class SubtypeAssignabilityPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token MakeToken(string lexeme, int start = 0)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static BaseListNode MakeBaseList(string name)
    {
        var baseTypeRef = new BaseTypeRefNode { Name = MakeToken(name) };
        return new BaseListNode
        {
            First = baseTypeRef,
            Rest = baseTypeRef,
            BaseTypeRefs = ImmutableArray<BaseTypeRefNode>.Empty
        };
    }

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast, string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        return _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        _analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    // ─── Expression builders ─────────────────────────────────────────────

    private static ExpressionNode WrapPrimaryAsExpression(PrimaryNode primary)
    {
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
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

    private static ExpressionNode MakeNewObjectExpression(string typeName)
    {
        var primary = new ObjectCreationExpressionPrimaryNode
        {
            Type = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken(typeName),
                Span = new Storm.Runtime.SourceSpan(0, typeName.Length),
            },
            Arguments = null,
        };
        return WrapPrimaryAsExpression(primary);
    }

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(name),
        };
        return WrapPrimaryAsExpression(primary);
    }

    // ─── Statement builders ──────────────────────────────────────────────

    /// <summary>
    /// Creates a variable declaration: BaseType varName = new DerivedType();
    /// </summary>
    private static StatementNode MakeNamedTypeVarDecl(string declaredTypeName, string varName, string initializedTypeName)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = MakeToken(declaredTypeName),
                        Span = new Storm.Runtime.SourceSpan(0, declaredTypeName.Length),
                    },
                } },
                Name = MakeToken(varName),
                Initializer = MakeNewObjectExpression(initializedTypeName),
            },
        };
    }

    private static StatementNode MakeExpressionStatement(ExpressionNode expr)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode
            {
                Expression = expr,
            },
        };
    }

    private static StatementNode MakeReturnStatement(ExpressionNode value)
    {
        return new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode
            {
                Value = value,
            },
        };
    }

    // ─── Class declaration builders ──────────────────────────────────────

    private static ClassDeclarationNode MakeClassDecl(
        string name,
        string? baseClassName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if (baseClassName is not null)
        {
            var _baseList1 = MakeBaseList(baseClassName);

            classDecl.BaseClause = _baseList1;

            classDecl.BaseList = _baseList1;
        }

        return classDecl;
    }

    // ─── Test data models ────────────────────────────────────────────────

    /// <summary>
    /// Represents a class hierarchy and an assignment scenario to test.
    /// </summary>
#pragma warning disable CA1034
    public record SubtypeScenario(
        ImmutableArray<(string Name, string? BaseName)> Classes,
        string DerivedClassName,
        string BaseClassName,
        AssignmentKind Kind)
    {
        public override string ToString()
        {
            var classesStr = string.Join(", ",
                Classes.Select(c => c.BaseName is not null ? $"{c.Name} : {c.BaseName}" : c.Name));
            return $"[{classesStr}] {Kind}: {DerivedClassName} → {BaseClassName}";
        }
    }

    public enum AssignmentKind
    {
        VariableDeclaration,
        MethodParameter,
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNamePool =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    /// <summary>
    /// Generates a valid class hierarchy of depth 1-3 (2-4 classes in a chain)
    /// and picks a derived-to-base assignment pair.
    /// </summary>
    public static Gen<SubtypeScenario> GenSubtypeScenario()
    {
        return from depth in Gen.Choose(1, 3)
               from classNames in Gen.Shuffle(ClassNamePool).Select(n => n.Take(depth + 1).ToArray())
               from kind in Gen.Elements(AssignmentKind.VariableDeclaration, AssignmentKind.MethodParameter)
               from scenario in BuildSubtypeScenario(classNames, kind)
               select scenario;
    }

    private static Gen<SubtypeScenario> BuildSubtypeScenario(string[] classNames, AssignmentKind kind)
    {
        // Build a linear chain: classNames[0] is root, classNames[1] : classNames[0], etc.
        var classes = new List<(string Name, string? BaseName)>();
        classes.Add((classNames[0], null));
        for (var i = 1; i < classNames.Length; i++)
        {
            classes.Add((classNames[i], classNames[i - 1]));
        }

        // Pick a derived class and one of its ancestors for the assignment
        // derivedIdx must be > baseIdx to ensure derived-to-base relationship
        return from derivedIdx in Gen.Choose(1, classNames.Length - 1)
               from baseIdx in Gen.Choose(0, derivedIdx - 1)
               select new SubtypeScenario(
                   classes.ToImmutableArray(),
                   classNames[derivedIdx],
                   classNames[baseIdx],
                   kind);
    }

    /// <summary>FsCheck Arbitrary registration for SubtypeScenario.</summary>
    public static Arbitrary<SubtypeScenario> ArbitrarySubtypeScenario() =>
        GenSubtypeScenario().ToArbitrary();

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds a CompilationUnitNode from a SubtypeScenario.
    /// Creates the class hierarchy and a Program class with Main that performs
    /// the derived-to-base assignment.
    /// </summary>
    private static CompilationUnitNode BuildAst(SubtypeScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // Create the class hierarchy
        foreach (var (name, baseName) in scenario.Classes)
        {
            var classDecl = MakeClassDecl(name, baseName);
            typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        }

        // Create the Program class with Main that performs the assignment
        var mainStatements = BuildAssignmentStatements(scenario);

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = mainStatements } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var programMembers = new List<MemberDeclarationNode>
        {
            new MethodMemberMemberDeclarationNode { Method = mainMethod },
        };

        // For MethodParameter scenarios, add a helper method that accepts a base-typed parameter
        if (scenario.Kind == AssignmentKind.MethodParameter)
        {
            var paramMethod = BuildAcceptBaseMethod(scenario.BaseClassName);
            programMembers.Add(new MethodMemberMemberDeclarationNode { Method = paramMethod });
        }

        var programClass = MakeClassDecl("Program", members: programMembers.ToImmutableArray());
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass });

        return new CompilationUnitNode
        {
            Items = (typeDecls.ToImmutableArray()).AsTopLevelItems(),
        };
    }

    private static ImmutableArray<StatementNode> BuildAssignmentStatements(SubtypeScenario scenario)
    {
        var statements = new List<StatementNode>();

        switch (scenario.Kind)
        {
            case AssignmentKind.VariableDeclaration:
                // BaseType x = new DerivedType();
                statements.Add(MakeNamedTypeVarDecl(
                    scenario.BaseClassName, "x", scenario.DerivedClassName));
                break;

            case AssignmentKind.MethodParameter:
                // var d = new DerivedType();
                // Program.AcceptBase(d);
                statements.Add(MakeNamedTypeVarDecl(
                    scenario.DerivedClassName, "d", scenario.DerivedClassName));

                // Call Program.AcceptBase(d) — pass derived instance to base-typed parameter
                var callExpr = MakeStaticMethodCallExpression("Program", "AcceptBase",
                    MakeIdentifierExpression("d"));
                statements.Add(MakeExpressionStatement(callExpr));
                break;
        }

        return statements.ToImmutableArray();
    }

    /// <summary>
    /// Creates a static method: public static void AcceptBase(BaseType param) { }
    /// </summary>
    private static MethodDeclarationNode BuildAcceptBaseMethod(string baseClassName)
    {
        var param = new ParameterNode
        {
            Type = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode
                {
                    IDENTIFIER = MakeToken(baseClassName),
                    Span = new Storm.Runtime.SourceSpan(0, baseClassName.Length),
                },
            } },
            Name = MakeToken("param"),
        };

        return new MethodDeclarationNode
        {
            Name = MakeToken("AcceptBase"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = new ParameterListNode { Parameter = param },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };
    }

    /// <summary>
    /// Creates an expression: target.methodName(arg) — a static method call with one argument.
    /// </summary>
    private static ExpressionNode MakeStaticMethodCallExpression(
        string target, string methodName, ExpressionNode arg)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(target),
        };
        var suffix = new MethodCallSuffixPostfixSuffixNode
        {
            Name = MakeToken(methodName),
            Arguments = new ArgumentListNode { Argument = new ValueArgumentArgumentNode { Expression = arg } },
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

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 6: For any class hierarchy of depth 1 or more, assigning a derived
    /// class value to a variable or parameter declared as any of its ancestor classes
    /// shall be accepted by the type checker without a MIST_TC_0001 diagnostic.
    ///
    /// **Validates: Requirements 5.1, 5.3, 5.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(SubtypeAssignabilityPropertyTests) }, MaxTest = 100)]
    public bool DerivedToBaseAssignment_ProducesNoTypeMismatchDiagnostic(SubtypeScenario scenario)
    {
        var ast = BuildAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // Filter to only MIST_TC_0001 diagnostics (type mismatch / cannot assign)
        var typeMismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0001")
            .ToList();

        // No type mismatch diagnostic should be emitted for valid derived-to-base assignments
        return typeMismatchDiags.Count == 0;
    ;
    }

}
