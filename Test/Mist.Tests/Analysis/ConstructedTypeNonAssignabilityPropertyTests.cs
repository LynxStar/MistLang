// Feature: generics-type-arguments, Property 11: Constructed type non-assignability
// **Validates: Requirements 11.2**

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
/// Property-based tests verifying constructed type non-assignability.
///
/// Property 11: For any two ConstructedType instances built from the same generic
/// definition but with at least one differing type argument (e.g., List&lt;int&gt;
/// and List&lt;string&gt;), assignment from one to the other SHALL cause the type
/// checker to emit a diagnostic error.
/// </summary>
public class ConstructedTypeNonAssignabilityPropertyTests
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

    // ─── AST construction helpers ────────────────────────────────────────

    private static TypeParameterListNode MakeTypeParamList(string[] names)
    {
        var first = new RegularTypeParameterTypeParameterNode { Name = MakeToken(names[0]) };
        var rest = names.Skip(1)
            .Select(n => (TypeParameterNode)new RegularTypeParameterTypeParameterNode { Name = MakeToken(n) })
            .ToImmutableArray();
        return new TypeParameterListNode
        {
            TypeParameter = first,
            TypeParameters = rest,
        };
    }

    private static TypeArgumentListNode MakeTypeArgList(TypeReferenceNode[] typeRefs)
    {
        return new TypeArgumentListNode
        {
            TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = typeRefs[0] },
            TypeArguments = typeRefs.Skip(1).Select(t => (TypeArgumentNode)new TypeReferenceTypeArgumentTypeArgumentNode { Type = t }).ToImmutableArray(),
        };
    }

    private static TypeReferenceNode MakeNamedTypeRef(string name, TypeArgumentListNode? typeArgs = null)
    {
        return new NamedTypeRefTypeReferenceNode
        {
            Name = new QualifiedIdentifierNode { IDENTIFIER = MakeToken(name) },
            TypeArgs = typeArgs,
        };
    }

    private static TypeReferenceNode MakePrimitiveTypeRef(string primitiveName)
    {
        PrimitiveTypeNode primitiveNode = primitiveName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };
        return new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = primitiveNode };
    }

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
        return new ExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeNewGenericObjectExpression(string typeName, TypeArgumentListNode typeArgs)
    {
        var primary = new ObjectCreationExpressionPrimaryNode
        {
            Type = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken(typeName),
                Span = new Storm.Runtime.SourceSpan(0, typeName.Length),
            },
            TypeArgs = typeArgs,
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

    private static StatementNode MakeTypedVarDecl(TypeReferenceNode typeRef, string varName, ExpressionNode initializer)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = typeRef,
                Name = MakeToken(varName),
                Initializer = initializer,
            },
        };
    }

    // ─── Test data models ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Scenario: A generic class constructed with two different type arguments.
    /// Assignment from one to the other should be rejected.
    /// e.g., class Box&lt;T&gt; { }
    ///       Box&lt;int&gt; a = new Box&lt;int&gt;();
    ///       Box&lt;string&gt; b = a;  // ERROR
    /// </summary>
    public record NonAssignabilityScenario(
        string GenericTypeName,
        string SourceType,
        string TargetType)
    {
        public override string ToString()
        {
            return $"class {GenericTypeName}<T> {{ }} " +
                   $"{GenericTypeName}<{SourceType}> a = new {GenericTypeName}<{SourceType}>(); " +
                   $"{GenericTypeName}<{TargetType}> b = a; // ERROR";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] GenericNamePool = ["Box", "Container", "Wrapper", "Holder"];
    private static readonly string[] PrimitivePool = ["int", "string", "bool", "float"];

    public static Gen<NonAssignabilityScenario> GenNonAssignabilityScenario()
    {
        return from genericName in Gen.Elements(GenericNamePool)
               from sourceType in Gen.Elements(PrimitivePool)
               from targetType in Gen.Elements(PrimitivePool)
               where sourceType != targetType
               select new NonAssignabilityScenario(genericName, sourceType, targetType);
    }

    public static Arbitrary<NonAssignabilityScenario> ArbitraryNonAssignabilityScenario() =>
        GenNonAssignabilityScenario().ToArbitrary();

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds: class GenericName&lt;T&gt; { }
    ///         class Program { static void Main() {
    ///             GenericName&lt;sourceType&gt; a = new GenericName&lt;sourceType&gt;();
    ///             GenericName&lt;targetType&gt; b = a;  // should produce MIST_TC_0001
    ///         } }
    /// </summary>
    private static CompilationUnitNode BuildAst(NonAssignabilityScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // Generic class
        var genericClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken(scenario.GenericTypeName),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = genericClassDecl });

        // Main body
        // GenericName<sourceType> a = new GenericName<sourceType>();
        var sourceTypeArgList = MakeTypeArgList([MakePrimitiveTypeRef(scenario.SourceType)]);
        var sourceTypeRef = MakeNamedTypeRef(scenario.GenericTypeName, sourceTypeArgList);
        var newSourceExpr = MakeNewGenericObjectExpression(scenario.GenericTypeName, sourceTypeArgList);
        var varDeclA = MakeTypedVarDecl(sourceTypeRef, "a", newSourceExpr);

        // GenericName<targetType> b = a;  // different type argument — should fail
        var targetTypeArgList = MakeTypeArgList([MakePrimitiveTypeRef(scenario.TargetType)]);
        var targetTypeRef = MakeNamedTypeRef(scenario.GenericTypeName, targetTypeArgList);
        var varDeclB = MakeTypedVarDecl(targetTypeRef, "b", MakeIdentifierExpression("a"));

        var mainStatements = ImmutableArray.Create(varDeclA, varDeclB);

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = mainStatements } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var programClass = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass });

        return new CompilationUnitNode { Items = (typeDecls.ToImmutableArray()).AsTopLevelItems() };
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 11: For any two ConstructedType instances built from the same generic
    /// definition but with at least one differing type argument, assignment from one
    /// to the other SHALL cause the type checker to emit a MIST_TC_0001 diagnostic error.
    ///
    /// **Validates: Requirements 11.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ConstructedTypeNonAssignabilityPropertyTests) }, MaxTest = 100)]
    public bool DifferentTypeArgs_ProducesTypeMismatchDiagnostic(NonAssignabilityScenario scenario)
    {
        var ast = BuildAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // Filter to MIST_TC_0001 diagnostics (type mismatch / cannot assign)
        var typeMismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0001")
            .ToList();

        // At least one type mismatch diagnostic should be emitted for incompatible
        // constructed type assignments (same definition, different type arguments)
        return typeMismatchDiags.Count > 0;
    }
}
