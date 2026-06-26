// Feature: class-inheritance, Property 7: Non-assignability of incompatible class types
// **Validates: Requirements 5.2, 5.5**

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

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying non-assignability of incompatible class types.
///
/// Property 7: For any two class types where neither is an ancestor of the other
/// (including the case where a base class value is assigned to a derived class variable),
/// the type checker shall emit a diagnostic error indicating the types are not assignable.
/// </summary>
public class NonAssignabilityPropertyTests
{
    private readonly SemanticModelBuilder builder = new();
    private readonly TypeCheckerAnalyzer analyzer = new();

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
        return builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        analyzer.Analyze(model, bag);
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

    // ─── Statement builders ──────────────────────────────────────────────

    /// <summary>
    /// Creates a variable declaration: DeclaredType varName = new InitializedType();
    /// </summary>
    private static StatementNode MakeNamedTypeVarDecl(string declaredTypeName, string varName, string initializedTypeName)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode
                {
                    First = new NamedSingleTypeRefSingleTypeRefNode
                    {
                        Name = new QualifiedIdentifierNode
                        {
                            IDENTIFIER = MakeToken(declaredTypeName),
                            Span = new Storm.Runtime.SourceSpan(0, declaredTypeName.Length),
                        },
                    }
                },
                Name = MakeToken(varName),
                Initializer = MakeNewObjectExpression(initializedTypeName),
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

        if(baseClassName is not null)
        {
            var _baseList1 = MakeBaseList(baseClassName);

            classDecl.BaseClause = _baseList1;

            classDecl.BaseList = _baseList1;
        }

        return classDecl;
    }

    // ─── Test data models ────────────────────────────────────────────────

    /// <summary>
    /// Represents an incompatible assignment scenario to test.
    /// </summary>
#pragma warning disable CA1034
    public record IncompatibleScenario(
        ImmutableArray<(string Name, string? BaseName)> Classes,
        string DeclaredTypeName,
        string InitializedTypeName,
        IncompatibleKind Kind)
    {
        public override string ToString()
        {
            var classesStr = string.Join(", ",
                Classes.Select(c => c.BaseName is not null ? $"{c.Name} : {c.BaseName}" : c.Name));
            return $"[{classesStr}] {Kind}: {DeclaredTypeName} x = new {InitializedTypeName}()";
        }
    }

    public enum IncompatibleKind
    {
        /// <summary>Two classes with no inheritance relationship.</summary>
        UnrelatedClasses,
        /// <summary>Assigning a base class instance to a derived class variable.</summary>
        BaseToDerived,
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNamePool =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta"];

    /// <summary>
    /// Generates an incompatible assignment scenario: either two unrelated classes
    /// or a base-to-derived assignment.
    /// </summary>
    public static Gen<IncompatibleScenario> GenIncompatibleScenario()
    {
        return Gen.OneOf(GenUnrelatedScenario(), GenBaseToDerivedScenario());
    }

    /// <summary>
    /// Generates a scenario with two unrelated classes where one is assigned to the other.
    /// Creates two independent class hierarchies and attempts cross-assignment.
    /// </summary>
    private static Gen<IncompatibleScenario> GenUnrelatedScenario()
    {
        return from names in Gen.Shuffle(ClassNamePool).Select(n => n.Take(4).ToArray())
               from scenario in BuildUnrelatedScenario(names)
               select scenario;
    }

    private static Gen<IncompatibleScenario> BuildUnrelatedScenario(string[] names)
    {
        // Build two independent chains:
        // Chain 1: names[0] (root), names[1] : names[0]
        // Chain 2: names[2] (root), names[3] : names[2]
        // Then assign across chains (names[1] variable = new names[3]() or vice versa)
        var classes = new List<(string Name, string? BaseName)>
        {
            (names[0], null),
            (names[1], names[0]),
            (names[2], null),
            (names[3], names[2]),
        };

        // Pick one class from each chain for the cross-assignment
        return from declIdx in Gen.Elements(0, 1)
               from initIdx in Gen.Elements(2, 3)
               select new IncompatibleScenario(
                   classes.ToImmutableArray(),
                   names[declIdx],
                   names[initIdx],
                   IncompatibleKind.UnrelatedClasses);
    }

    /// <summary>
    /// Generates a scenario where a base class instance is assigned to a derived class variable.
    /// This is the reverse of valid subtype assignability and should be rejected.
    /// </summary>
    private static Gen<IncompatibleScenario> GenBaseToDerivedScenario()
    {
        return from depth in Gen.Choose(1, 3)
               from names in Gen.Shuffle(ClassNamePool).Select(n => n.Take(depth + 1).ToArray())
               from scenario in BuildBaseToDerivedScenario(names)
               select scenario;
    }

    private static Gen<IncompatibleScenario> BuildBaseToDerivedScenario(string[] names)
    {
        // Build a linear chain: names[0] is root, names[1] : names[0], etc.
        var classes = new List<(string Name, string? BaseName)>
        {
            (names[0], null)
        };
        for(var i = 1; i < names.Length; i++)
        {
            classes.Add((names[i], names[i - 1]));
        }

        // Pick a base class and a derived class, then assign base → derived (invalid)
        // derivedIdx > baseIdx ensures derived is below base in the chain
        return from derivedIdx in Gen.Choose(1, names.Length - 1)
               from baseIdx in Gen.Choose(0, derivedIdx - 1)
               select new IncompatibleScenario(
                   classes.ToImmutableArray(),
                   names[derivedIdx],   // variable type = derived
                   names[baseIdx],      // initialized type = base (invalid: base → derived)
                   IncompatibleKind.BaseToDerived);
    }

    /// <summary>FsCheck Arbitrary registration for IncompatibleScenario.</summary>
    public static Arbitrary<IncompatibleScenario> ArbitraryIncompatibleScenario()
    {
        return GenIncompatibleScenario().ToArbitrary();
    }

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds a CompilationUnitNode from an IncompatibleScenario.
    /// Creates the class hierarchy and a Program class with Main that performs
    /// the incompatible assignment.
    /// </summary>
    private static CompilationUnitNode BuildAst(IncompatibleScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // Create the class hierarchy
        foreach(var (name, baseName) in scenario.Classes)
        {
            var classDecl = MakeClassDecl(name, baseName);
            typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        }

        // Create the Program class with Main that performs the incompatible assignment
        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl(scenario.DeclaredTypeName, "x", scenario.InitializedTypeName));

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

        var programMembers = ImmutableArray.Create<MemberDeclarationNode>(
            new MethodMemberMemberDeclarationNode { Method = mainMethod });

        var programClass = MakeClassDecl("Program", members: programMembers);
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass });

        return new CompilationUnitNode
        {
            Items = typeDecls.ToImmutableArray().AsTopLevelItems(),
        };
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 7: For any two class types where neither is an ancestor of the other
    /// (including the case where a base class value is assigned to a derived class variable),
    /// the type checker shall emit a MIST_TC_0001 diagnostic indicating the types are
    /// not assignable.
    ///
    /// **Validates: Requirements 5.2, 5.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NonAssignabilityPropertyTests) }, MaxTest = 100)]
    public bool IncompatibleAssignment_ProducesTypeMismatchDiagnostic(IncompatibleScenario scenario)
    {
        var ast = BuildAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // Filter to only MIST_TC_0001 diagnostics (type mismatch / cannot assign)
        var typeMismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0001")
            .ToList();

        // At least one type mismatch diagnostic should be emitted for incompatible assignments
        return typeMismatchDiags.Count > 0;
        ;
    }

}