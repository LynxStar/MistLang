// Feature: unknown-type (Phase B, Task 8.2)
// Validates Requirements 3.6, 7.1, 7.2, 7.3 — the semantic model builder resolves
// `UnknownSingleTypeRefSingleTypeRefNode` references to the `UnknownType.Instance`
// singleton, both as a bare type and nested inside `TypeArgumentList` at any depth.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for the integration point added in Task 8.1: the
/// <c>UnknownSingleTypeRefSingleTypeRefNode</c> case in
/// <c>SemanticModelBuilder.ResolveTypeReference</c> that returns
/// <see cref="UnknownType.Instance"/>.
///
/// Each test:
/// 1. Builds an AST with minimal user-defined generic classes (<c>List&lt;T&gt;</c>,
///    <c>Map&lt;K, V&gt;</c>, <c>Optional&lt;T&gt;</c>) because the stdlib does not
///    auto-load those generic collections.
/// 2. Declares a local variable inside a <c>Holder</c> class method whose declared
///    type references <c>unknown</c> in one or more positions. Locals are chosen
///    deliberately over fields/properties/indexers: the storage ban from Task 11
///    is not yet in force, but locals are permitted per Req 7.4 regardless, so
///    the tests remain valid after Task 11 lands.
/// 3. Asserts the resolved <c>DeclaredType</c> has the expected shape and that
///    every <c>unknown</c> occurrence is reference-equal to
///    <see cref="UnknownType.Instance"/>.
///
/// The tests deliberately do not care about any <c>MIST_TC</c> assignability
/// diagnostics that may arise from the placeholder integer-literal initializers;
/// only the structural resolution of the declared type is under test.
/// </summary>
public class UnknownTypeResolutionTests
{
    private readonly SemanticModelBuilder builder = new();

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

    private ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast)
    {
        var compilation = MakeCompilation();
        var diagnostics = new DiagnosticBag();
        return builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    // ─── AST construction ────────────────────────────────────────────────

    private static TypeParameterListNode MakeTypeParamList(params string[] names)
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

    private static TypeArgumentListNode MakeTypeArgList(params TypeReferenceNode[] typeRefs)
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

    private static TypeReferenceNode MakeUnknownTypeRef()
    {
        return new TypeReferenceNode { First = new UnknownSingleTypeRefSingleTypeRefNode { UNKNOWN = MakeToken("unknown") } };
    }

    private static TypeReferenceNode MakeStringTypeRef()
    {
        return new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new StringTypePrimitiveTypeNode() } };
    }

    /// <summary>
    /// Builds a minimal <c>int</c>-literal initializer expression. The initializer
    /// is required by <c>TypedVariableDeclarationVariableDeclarationStatementNode</c>
    /// but its type is irrelevant — only the declared type of the local is under
    /// test. Any resulting assignability diagnostics are ignored.
    /// </summary>
    private static ExpressionNode MakeIntLiteralInitializer()
    {
        var primary = new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = MakeToken("0") };
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

    private static StatementNode MakeTypedLocal(string name, TypeReferenceNode typeRef)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = typeRef,
                Name = MakeToken(name),
                Initializer = MakeIntLiteralInitializer(),
            },
        };
    }

    private static TypeBodyDeclarationNode MakeGenericClass(string name, params string[] typeParams)
    {
        return new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken(name),
            TypeParams = MakeTypeParamList(typeParams),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
    }

    /// <summary>
    /// Builds a <c>Holder</c> class with a single method <c>M()</c> whose body
    /// declares one local variable of the supplied type. The local is named
    /// <c>"x"</c>.
    /// </summary>
    private static TypeBodyDeclarationNode MakeHolderWithLocal(TypeReferenceNode localType)
    {
        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(MakeTypedLocal("x", localType)),
                },
            },
        };

        return new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Holder"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };
    }

    private static CompilationUnitNode MakeCompilationUnit(params TypeBodyDeclarationNode[] classes)
    {
        var typeDecls = classes
            .Select(c => (TypeDeclarationNode)new BodyTypeTypeDeclarationNode { TypeBody = c })
            .ToImmutableArray();
        return new CompilationUnitNode { Items = typeDecls.AsTopLevelItems() };
    }

    /// <summary>
    /// Looks up the declared type of the local <c>"x"</c> that every test plants
    /// inside <c>Holder.M()</c>.
    /// </summary>
    private static IType GetLocalXType(ISemanticModel model)
    {
        var local = model.AllSymbols.FirstOrDefault(
            s => s.Name == "x" && s.Kind == SymbolKind.LocalVariable);
        Assert.NotNull(local);
        Assert.NotNull(local!.DeclaredType);
        return local.DeclaredType!;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.6 — A bare `unknown` type reference resolves to
    // `UnknownType.Instance`.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Resolves_Bare_Unknown_To_Instance()
    {
        // class Holder { void M() { unknown x = 0; } }
        var holder = MakeHolderWithLocal(MakeUnknownTypeRef());
        var ast = MakeCompilationUnit(holder);
        var model = BuildModel(ast);

        var localType = GetLocalXType(model);

        // Reference equality — UnknownType is a singleton; every `unknown`
        // reference in source must resolve to the same .Instance.
        Assert.Same(UnknownType.Instance, localType);
        _ = Assert.IsType<UnknownType>(localType);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.1 / 7.3 — `unknown` is permitted as a type argument and
    // produces a `ConstructedType` whose argument at the expected position is
    // `UnknownType.Instance`.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Resolves_List_Of_Unknown_With_Unknown_TypeArgument()
    {
        // class List<T> { }
        // class Holder { void M() { List<unknown> x = 0; } }
        var listDecl = MakeGenericClass("List", "T");
        var holder = MakeHolderWithLocal(
            MakeNamedTypeRef("List", MakeTypeArgList(MakeUnknownTypeRef())));
        var ast = MakeCompilationUnit(listDecl, holder);

        var model = BuildModel(ast);
        var localType = GetLocalXType(model);

        var constructed = Assert.IsType<ConstructedType>(localType);
        var listType = Assert.IsType<ClassType>(constructed.GenericDefinition);
        Assert.Equal("List", listType.DisplayName);

        _ = Assert.Single(constructed.TypeArguments);
        Assert.Same(UnknownType.Instance, constructed.TypeArguments[0]);
    }

    [Fact]
    public void Resolves_Map_String_Unknown_With_Unknown_Second_TypeArgument()
    {
        // class Map<K, V> { }
        // class Holder { void M() { Map<string, unknown> x = 0; } }
        var mapDecl = MakeGenericClass("Map", "K", "V");
        var holder = MakeHolderWithLocal(
            MakeNamedTypeRef("Map", MakeTypeArgList(MakeStringTypeRef(), MakeUnknownTypeRef())));
        var ast = MakeCompilationUnit(mapDecl, holder);

        var model = BuildModel(ast);
        var localType = GetLocalXType(model);

        var constructed = Assert.IsType<ConstructedType>(localType);
        var mapType = Assert.IsType<ClassType>(constructed.GenericDefinition);
        Assert.Equal("Map", mapType.DisplayName);

        Assert.Equal(2, constructed.TypeArguments.Count);

        // First position — `string`. Resolution may route through the stdlib or
        // the legacy primitive fallback depending on environment; either way the
        // DisplayName is "string" and it is NOT UnknownType. Comparing on
        // DisplayName matches the existing ConstructedTypeResolutionPropertyTests.
        Assert.NotSame(UnknownType.Instance, constructed.TypeArguments[0]);
        Assert.Equal("string", constructed.TypeArguments[0].DisplayName);

        // Second position — `unknown`. Must be reference-equal to the singleton.
        Assert.Same(UnknownType.Instance, constructed.TypeArguments[1]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.2 — nested `unknown` occurrences at arbitrary depth.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Resolves_Optional_List_Unknown_Nested()
    {
        // class Optional<T> { }
        // class List<T> { }
        // class Holder { void M() { Optional<List<unknown>> x = 0; } }
        var optionalDecl = MakeGenericClass("Optional", "T");
        var listDecl = MakeGenericClass("List", "T");
        var holder = MakeHolderWithLocal(
            MakeNamedTypeRef(
                "Optional",
                MakeTypeArgList(
                    MakeNamedTypeRef(
                        "List",
                        MakeTypeArgList(MakeUnknownTypeRef())))));
        var ast = MakeCompilationUnit(optionalDecl, listDecl, holder);

        var model = BuildModel(ast);
        var localType = GetLocalXType(model);

        // Outer: ConstructedType(Optional, [inner])
        var outer = Assert.IsType<ConstructedType>(localType);
        var optionalType = Assert.IsType<ClassType>(outer.GenericDefinition);
        Assert.Equal("Optional", optionalType.DisplayName);
        _ = Assert.Single(outer.TypeArguments);

        // Inner: ConstructedType(List, [UnknownType.Instance])
        var inner = Assert.IsType<ConstructedType>(outer.TypeArguments[0]);
        var listType = Assert.IsType<ClassType>(inner.GenericDefinition);
        Assert.Equal("List", listType.DisplayName);
        _ = Assert.Single(inner.TypeArguments);
        Assert.Same(UnknownType.Instance, inner.TypeArguments[0]);
    }
}