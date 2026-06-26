using System.Collections.Immutable;
using System.Linq;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for Task 11 of the Mist Standard Library spec: the
/// semantic model builder registers <c>OperatorDeclaration</c> AST nodes
/// as <see cref="Symbol"/> instances on the containing type (Pass 1) and
/// wires up the return type + parameter signatures (Pass 2) so that
/// overload resolution in Task 14 can retrieve them like any other method.
///
/// Validates:
/// * Requirement 4.6 — an operator declaration becomes a
///   <see cref="SymbolKind.Method"/> symbol named
///   <c>"operator&lt;op&gt;"</c>, keyed for later overload resolution by
///   operator symbol and parameter types.
/// </summary>
public class OperatorDeclarationRegistrationTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private readonly SemanticModelBuilder _builder = new();

    /// <summary>
    /// Lexes, parses, and semantically analyses <paramref name="source"/>.
    /// Returns the built semantic model.
    /// </summary>
    private ISemanticModel BuildModel(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, "Lex should not produce errors");

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, "Parse should not produce errors");
        Assert.NotNull(parseResult.Root);

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        return _builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);
    }

    // ─── Sub-task 11.1 — operator on a user struct registers a Method symbol ─

    /// <summary>
    /// An <c>operator +</c> declaration on a non-intrinsic user struct
    /// registers a <see cref="SymbolKind.Method"/> symbol whose name is
    /// <c>"operator+"</c>, whose <see cref="Symbol.DeclaredType"/> is the
    /// return type, whose parameter types match the declared parameters
    /// (retrieved via <see cref="ParameterSignature.Extract"/> — the same
    /// path overload resolution will follow in Task 14), and whose
    /// <see cref="Symbol.IsIntrinsic"/> is <c>false</c> (no
    /// <c>intrinsic</c> modifier present).
    /// </summary>
    [Fact]
    public void Task11_1_UserStructOperatorPlus_RegistersAsMethodSymbolWithCorrectShape()
    {
        // A minimal user struct with a non-intrinsic `operator +`. Bool is used
        // for the return type so the assertion is robust against how primitive
        // keywords print ("bool" vs "Bool") as this evolves across tasks.
        var source =
            "public struct Vec { " +
            "public static Vec operator +(Vec a, Vec b) { return a; } " +
            "}";

        var model = BuildModel(source);

        // (a) The operator is registered on the struct's member scope (i.e.,
        //     it's reachable as a member of Vec, not at global scope).
        var vecSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == "Vec" && s.Kind == SymbolKind.Struct);
        Assert.NotNull(vecSymbol);
        var vecType = vecSymbol!.DeclaredType as StructType;
        Assert.NotNull(vecType);

        var opSymbol = vecType!.MemberScope.Members
            .FirstOrDefault(m => m.Name == "operator+") as Symbol;
        Assert.NotNull(opSymbol);

        // (b) Operator name is "operator+" with SymbolKind.Method.
        Assert.Equal("operator+", opSymbol!.Name);
        Assert.Equal(SymbolKind.Method, opSymbol.Kind);

        // (c) No `intrinsic` modifier on the declaration.
        Assert.False(opSymbol.IsIntrinsic,
            "Operator without 'intrinsic' modifier should have IsIntrinsic == false.");

        // (d) DeclaredType is the return type. The return type `Vec` resolves
        //     to the same StructType instance registered in the model.
        Assert.Same(vecType, opSymbol.DeclaredType);

        // (e) Parameter types are extractable via ParameterSignature.Extract
        //     (the same mechanism OverloadResolver uses in Task 14). Both
        //     parameter types match the declared `Vec`.
        var paramTypes = ParameterSignature.Extract(opSymbol, model);
        Assert.Equal(2, paramTypes.Count);
        Assert.Same(vecType, paramTypes[0]);
        Assert.Same(vecType, paramTypes[1]);
    }
}