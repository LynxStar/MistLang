using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Unit tests for parsing the <c>unknown</c> keyword.
///
/// Positive tests validate that <c>unknown</c> parses to an
/// <see cref="UnknownSingleTypeRefSingleTypeRefNode"/> in every legal type-reference
/// position (local variable type, top-level type argument, nested type argument).
///
/// Negative tests validate that the lexer produces the <c>UNKNOWN</c> token
/// rather than an identifier, so that <c>unknown</c> cannot be used as the name
/// of a class, local variable, method, namespace, or generic type parameter.
///
/// Validates: Requirements 1.3, 1.4 of the unknown-type feature.
/// </summary>
public class UnknownTypeParserTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Parse helpers ──────────────────────────────────────────────────────

    private static (CompilationUnitNode? Root, bool HasErrors) ParseSource(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var hasErrors = bag.ToImmutableArray()
            .Any(d => d.Severity == DiagnosticSeverity.Error);

        return (result.Root as CompilationUnitNode, hasErrors);
    }

    /// <summary>
    /// Extracts the first method body's first statement as a typed-variable
    /// declaration, asserting along the way that the AST has the expected shape.
    /// Used by positive tests that declare a local variable inside
    /// <c>class T { public void M() { &lt;stmt&gt; } }</c>.
    /// </summary>
    private static TypedVariableDeclarationVariableDeclarationStatementNode GetFirstLocalDecl(CompilationUnitNode cu)
    {
        var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.ClassDeclaration.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);
        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(block!.Statements[0]);
        return Assert.IsType<TypedVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);
    }

    /// <summary>
    /// Returns the type arguments of a <see cref="TypeArgumentListNode"/> in
    /// source order. The Storm-generated node stores the first type reference
    /// in the singular <c>TypeReference</c> property and the remaining
    /// references in the <c>TypeReferences</c> array; this helper reunites them.
    /// </summary>
    private static IReadOnlyList<TypeReferenceNode> GetTypeArgs(TypeArgumentListNode list)
    {
        var result = new List<TypeReferenceNode> { list.GetTypeReference() };
        result.AddRange(list.GetTypeReferences());
        return result;
    }

    // ─── Positive tests — `unknown` in legal type-reference positions ──────

    [Fact]
    public void Parses_Unknown_As_Local_Variable_Type()
    {
        // `unknown x = 1;` inside a method body. The local's declared type
        // reference must resolve to UnknownSingleTypeRefSingleTypeRefNode.
        var source = "class T { public void M() { unknown x = 1; } }";
        var (cu, hasErrors) = ParseSource(source);

        Assert.False(hasErrors);
        Assert.NotNull(cu);

        var varDecl = GetFirstLocalDecl(cu!);
        Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(varDecl.Type.First);
    }

    [Fact]
    public void Parses_Unknown_As_Type_Argument_In_List()
    {
        // `List<unknown> xs = ...;` — the type argument at position 0 of
        // the named-type reference must be UnknownSingleTypeRefSingleTypeRefNode.
        var source = "class T { public void M() { List<unknown> xs = 0; } }";
        var (cu, hasErrors) = ParseSource(source);

        Assert.False(hasErrors);
        Assert.NotNull(cu);

        var varDecl = GetFirstLocalDecl(cu!);
        var named = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(varDecl.Type.First);
        Assert.NotNull(named.TypeArgs);
        var typeArgs = GetTypeArgs(named.TypeArgs!);
        Assert.Single(typeArgs);
        Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(typeArgs[0].First);
    }

    [Fact]
    public void Parses_Unknown_In_Map_Second_Type_Argument()
    {
        // `Map<string, unknown> m = ...;` — first type argument is the
        // primitive `string`, second is `unknown`.
        var source = "class T { public void M() { Map<string, unknown> m = 0; } }";
        var (cu, hasErrors) = ParseSource(source);

        Assert.False(hasErrors);
        Assert.NotNull(cu);

        var varDecl = GetFirstLocalDecl(cu!);
        var named = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(varDecl.Type.First);
        Assert.NotNull(named.TypeArgs);
        var typeArgs = GetTypeArgs(named.TypeArgs!);
        Assert.Equal(2, typeArgs.Count);

        var firstPrim = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(typeArgs[0].First);
        Assert.IsType<StringTypePrimitiveTypeNode>(firstPrim.PrimitiveType);

        Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(typeArgs[1].First);
    }

    [Fact]
    public void Parses_Nested_Unknown_In_Optional_List()
    {
        // `Optional<List<unknown>> o = ...;` — the outer type argument is
        // itself a named-type reference whose only type argument is `unknown`.
        var source = "class T { public void M() { Optional<List<unknown>> o = 0; } }";
        var (cu, hasErrors) = ParseSource(source);

        Assert.False(hasErrors);
        Assert.NotNull(cu);

        var varDecl = GetFirstLocalDecl(cu!);
        var outer = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(varDecl.Type.First);
        Assert.NotNull(outer.TypeArgs);
        var outerArgs = GetTypeArgs(outer.TypeArgs!);
        Assert.Single(outerArgs);

        var inner = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(outerArgs[0].First);
        Assert.NotNull(inner.TypeArgs);
        var innerArgs = GetTypeArgs(inner.TypeArgs!);
        Assert.Single(innerArgs);

        Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(innerArgs[0].First);
    }

    // ─── Negative tests — `unknown` cannot be an identifier ────────────────
    //
    // The Storm-generated parser currently does not emit a diagnostic for
    // every syntactic failure: when a rule alternative fails to match, the
    // parser backtracks silently and the enclosing construct is not produced.
    // Each negative test therefore asserts that the AST shape expected of a
    // valid analogous source is *absent*, which is the observable difference
    // between "parsed successfully as an identifier" and "rejected because
    // `unknown` is a reserved keyword token." The analogous valid source is
    // shown in a comment above each test for clarity.

    [Fact]
    public void Rejects_Class_Named_Unknown()
    {
        // Valid analog: `class Foo { }` — produces a single ClassTypeTypeDeclarationNode
        // named `Foo`. If `unknown` were a normal identifier, this would parse identically.
        // Because `unknown` is a keyword token, the ClassDeclaration rule's IDENTIFIER
        // requirement fails after CLASS, and no type declaration is produced.
        var (cuInvalid, _) = ParseSource("class unknown { }");
        Assert.NotNull(cuInvalid);
        Assert.Empty(cuInvalid!.GetTypes());

        // Sanity check: the analogous source with a non-keyword identifier succeeds.
        var (cuValid, _) = ParseSource("class Foo { }");
        Assert.NotNull(cuValid);
        Assert.Single(cuValid!.GetTypes());
    }

    [Fact]
    public void Rejects_Local_Variable_Named_Unknown()
    {
        // Valid analog: `class T { public void M() { var x = 1; } }` — a single class
        // with a method whose body has one statement. With `unknown` in place of `x`,
        // the VAR IDENTIFIER requirement fails and the entire enclosing class is
        // rejected (no error recovery in v1).
        var (cuInvalid, _) = ParseSource("class T { public void M() { var unknown = 1; } }");
        Assert.NotNull(cuInvalid);
        Assert.Empty(cuInvalid!.GetTypes());

        var (cuValid, _) = ParseSource("class T { public void M() { var x = 1; } }");
        Assert.NotNull(cuValid);
        Assert.Single(cuValid!.GetTypes());
    }

    [Fact]
    public void Rejects_Method_Named_Unknown()
    {
        // Valid analog: `class T { public void M() { } }` — MethodDeclaration requires
        // IDENTIFIER after the return type. `void unknown()` matches the return type
        // but fails at the name position.
        var (cuInvalid, _) = ParseSource("class T { public void unknown() { } }");
        Assert.NotNull(cuInvalid);
        Assert.Empty(cuInvalid!.GetTypes());

        var (cuValid, _) = ParseSource("class T { public void M() { } }");
        Assert.NotNull(cuValid);
        Assert.Single(cuValid!.GetTypes());
    }

    [Fact]
    public void Rejects_Namespace_Named_Unknown()
    {
        // Valid analog: `namespace Geometry;` — NamespaceDeclaration's QualifiedIdentifier
        // requires IDENTIFIER tokens only.
        var (cuInvalid, _) = ParseSource("namespace unknown;");
        Assert.NotNull(cuInvalid);
        Assert.Null(cuInvalid!.GetNamespace());

        var (cuValid, _) = ParseSource("namespace Geometry;");
        Assert.NotNull(cuValid);
        Assert.NotNull(cuValid!.GetNamespace());
    }

    [Fact]
    public void Rejects_Generic_Parameter_Named_Unknown()
    {
        // Valid analog: `class Foo<T> { }` — TypeParameter is `name:IDENTIFIER`,
        // so `<unknown>` causes the generic parameter list to fail, and the
        // class declaration as a whole is rejected because it would otherwise
        // attempt to parse `<unknown>` as something else (e.g., a stray `<`).
        var (cuInvalid, _) = ParseSource("class Foo<unknown> { }");
        Assert.NotNull(cuInvalid);
        Assert.Empty(cuInvalid!.GetTypes());

        var (cuValid, _) = ParseSource("class Foo<T> { }");
        Assert.NotNull(cuValid);
        Assert.Single(cuValid!.GetTypes());
    }
}
