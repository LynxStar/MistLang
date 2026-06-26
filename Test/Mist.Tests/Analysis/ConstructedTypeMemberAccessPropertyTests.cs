// Feature: generics-type-arguments, Property 9: Constructed type member access substitution
// **Validates: Requirements 9.3, 11.5**

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

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying constructed type member access substitution.
///
/// Property 9: For any member access on a ConstructedType (e.g., accessing a field or
/// calling a method on a Box&lt;int&gt; value), the type checker SHALL substitute each
/// type parameter in the member's type with the corresponding type argument from the
/// constructed type. For example, if Box&lt;T&gt; has a field of type T and the
/// constructed type is Box&lt;int&gt;, the field's effective type SHALL be int.
/// </summary>
public class ConstructedTypeMemberAccessPropertyTests
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
        return new TypeReferenceNode
        {
            First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode { IDENTIFIER = MakeToken(name) },
                TypeArgs = typeArgs,
            }
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
        return new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitiveNode } };
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
        return new AssignmentFormExpressionNode { Assignment = assign };
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

    /// <summary>
    /// Creates an expression: identifier.fieldName (member access).
    /// </summary>
    private static ExpressionNode MakeMemberAccessExpression(string identifier, string fieldName)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(identifier),
        };
        var suffix = new MemberAccessSuffixPostfixSuffixNode
        {
            Name = MakeToken(fieldName),
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

    /// <summary>
    /// Creates a variable declaration: TypeRef varName = initializer;
    /// </summary>
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
    /// Scenario: A generic class with a field of type T, constructed with a concrete type.
    /// We access the field and assign it to a variable of the concrete type.
    /// e.g., class Box&lt;T&gt; { public T value; }
    ///       Box&lt;int&gt; b = new Box&lt;int&gt;();
    ///       int v = b.value;
    /// </summary>
    public record MemberAccessScenario(
        string GenericClassName,
        string FieldName,
        string ConcreteType)
    {
        public override string ToString()
        {
            return $"class {GenericClassName}<T> {{ public T {FieldName}; }} " +
                   $"{GenericClassName}<{ConcreteType}> b = new {GenericClassName}<{ConcreteType}>(); " +
                   $"{ConcreteType} v = b.{FieldName};";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] GenericNamePool = ["Box", "Container", "Wrapper", "Holder"];
    private static readonly string[] FieldNamePool = ["value", "data", "item", "content"];
    private static readonly string[] PrimitivePool = ["int", "string", "bool", "float"];

    public static Gen<MemberAccessScenario> GenMemberAccessScenario()
    {
        return from genericName in Gen.Elements(GenericNamePool)
               from fieldName in Gen.Elements(FieldNamePool)
               from concreteType in Gen.Elements(PrimitivePool)
               select new MemberAccessScenario(genericName, fieldName, concreteType);
    }

    public static Arbitrary<MemberAccessScenario> ArbitraryMemberAccessScenario()
    {
        return GenMemberAccessScenario().ToArbitrary();
    }

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds a CompilationUnitNode from a MemberAccessScenario.
    /// Creates a generic class with a field of type T, then a Program class with Main
    /// that constructs the generic type with a concrete argument and accesses the field,
    /// assigning it to a variable of the concrete type.
    /// </summary>
    private static CompilationUnitNode BuildAst(MemberAccessScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // Create the generic class: class GenericName<T> { public T fieldName; }
        var fieldMember = new FieldMemberMemberDeclarationNode
        {
            Field = new FieldDeclarationNode
            {
                Name = MakeToken(scenario.FieldName),
                Type = MakeNamedTypeRef("T"),
                Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            },
        };

        var genericClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken(scenario.GenericClassName),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray.Create<MemberDeclarationNode>(fieldMember),
        };
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = genericClassDecl });

        // Build type argument list for the concrete type
        var typeArgList = MakeTypeArgList([MakePrimitiveTypeRef(scenario.ConcreteType)]);

        // Main body:
        //   GenericName<concreteType> b = new GenericName<concreteType>();
        //   concreteType v = b.fieldName;
        var constructedTypeRef = MakeNamedTypeRef(scenario.GenericClassName, typeArgList);
        var newExpr = MakeNewGenericObjectExpression(scenario.GenericClassName, typeArgList);
        var varDeclB = MakeTypedVarDecl(constructedTypeRef, "b", newExpr);

        var memberAccessExpr = MakeMemberAccessExpression("b", scenario.FieldName);
        var concreteTypeRef = MakePrimitiveTypeRef(scenario.ConcreteType);
        var varDeclV = MakeTypedVarDecl(concreteTypeRef, "v", memberAccessExpr);

        var mainStatements = ImmutableArray.Create(varDeclB, varDeclV);

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

        var programClass = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass });

        return new CompilationUnitNode
        {
            Items = (typeDecls.ToImmutableArray()).AsTopLevelItems(),
        };
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 9: For any member access on a ConstructedType, the type checker SHALL
    /// substitute each type parameter in the member's type with the corresponding type
    /// argument. Accessing a field of type T on a Box&lt;int&gt; should yield int,
    /// and assigning it to an int variable should produce no type mismatch diagnostic.
    ///
    /// **Validates: Requirements 9.3, 11.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ConstructedTypeMemberAccessPropertyTests) }, MaxTest = 100)]
    public bool ConstructedTypeMemberAccess_SubstitutesTypeParameters(MemberAccessScenario scenario)
    {
        var ast = BuildAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // Filter to MIST_TC_0001 diagnostics (type mismatch / cannot assign)
        var typeMismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0001")
            .ToList();

        // No type mismatch diagnostic should be emitted — the field's type T is substituted
        // with the concrete type argument, so assigning it to a variable of that type is valid.
        return typeMismatchDiags.Count == 0;
    }
}