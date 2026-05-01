using System.Collections.Immutable;
using System.Linq;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for Task 8 of the Mist Standard Library spec: the Pass 1
/// member-registration code propagates the <c>intrinsic</c> modifier and
/// the body-presence flag from AST nodes onto the resulting
/// <see cref="ClassType"/>/<see cref="StructType"/>/<see cref="Symbol"/>.
///
/// Validates:
/// * Requirement 5.11 — type-level <c>intrinsic</c> sets
///   <see cref="StructType.IsBackendBacked"/>/<see cref="ClassType.IsBackendBacked"/>.
/// * Requirement 5.1  — member-level <c>intrinsic</c> sets
///   <see cref="Symbol.IsIntrinsic"/>.
/// * Requirement 5.2  — a <c>MethodNoBody</c>/<c>OperatorNoBody</c> body
///   records <see cref="Symbol.HasBody"/> as <c>false</c> on the symbol.
/// </summary>
public class IntrinsicModifierPropagationTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private readonly SemanticModelBuilder _builder = new();

    /// <summary>
    /// Lexes and parses <paramref name="source"/>, then runs Pass 1 of the
    /// semantic model builder over the resulting AST. Returns the built
    /// <see cref="ISemanticModel"/>.
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

    // ─── Sub-task 8.1 — combined assertion from the spec ────────────────────

    [Fact]
    public void Task8_1_IntrinsicStructWithIntrinsicOperator_PropagatesBothFlags()
    {
        // The combined snippet from sub-task 8.1:
        //   `intrinsic public struct Int32 { intrinsic public static Int32 operator +(...); }`
        // produces
        //   * a StructType whose IsBackendBacked == true
        //   * an operator Symbol whose IsIntrinsic == true and HasBody == false.
        var source =
            "intrinsic public struct Int32 { " +
            "intrinsic public static Int32 operator +(Int32 a, Int32 b); " +
            "}";

        var model = BuildModel(source);

        // (a) The StructType for Int32 is marked as backend-backed.
        var structSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == "Int32" && s.Kind == SymbolKind.Struct);
        Assert.NotNull(structSymbol);
        var structType = structSymbol!.DeclaredType as StructType;
        Assert.NotNull(structType);
        Assert.True(structType!.IsBackendBacked,
            "Type-level 'intrinsic' should set StructType.IsBackendBacked.");

        // (b) The operator Symbol is registered inside the struct's scope
        //     with IsIntrinsic == true and HasBody == false.
        var plusOperator = structType.MemberScope.Members
            .FirstOrDefault(m => m.Name == "operator+") as Symbol;
        Assert.NotNull(plusOperator);
        Assert.Equal(SymbolKind.Method, plusOperator!.Kind);
        Assert.True(plusOperator.IsIntrinsic,
            "Member-level 'intrinsic' should set Symbol.IsIntrinsic.");
        Assert.False(plusOperator.HasBody,
            "An OperatorNoBody body should record Symbol.HasBody == false.");
    }

    // ─── Type-level intrinsic flag on its own ───────────────────────────────

    [Fact]
    public void IntrinsicPublicStruct_SetsIsBackendBackedOnStructType()
    {
        var source = "intrinsic public struct Int32 { }";
        var model = BuildModel(source);

        var structSymbol = model.AllSymbols.First(s => s.Name == "Int32" && s.Kind == SymbolKind.Struct);
        var structType = (StructType)structSymbol.DeclaredType!;
        Assert.True(structType.IsBackendBacked);
    }

    [Fact]
    public void IntrinsicPublicClass_SetsIsBackendBackedOnClassType()
    {
        var source = "intrinsic public class String { }";
        var model = BuildModel(source);

        var classSymbol = model.AllSymbols.First(s => s.Name == "String" && s.Kind == SymbolKind.Class);
        var classType = (ClassType)classSymbol.DeclaredType!;
        Assert.True(classType.IsBackendBacked);
    }

    [Fact]
    public void NonIntrinsicStruct_LeavesIsBackendBackedFalse()
    {
        var source = "public struct Point { }";
        var model = BuildModel(source);

        var structSymbol = model.AllSymbols.First(s => s.Name == "Point" && s.Kind == SymbolKind.Struct);
        var structType = (StructType)structSymbol.DeclaredType!;
        Assert.False(structType.IsBackendBacked);
    }

    [Fact]
    public void NonIntrinsicClass_LeavesIsBackendBackedFalse()
    {
        var source = "public class Vector { }";
        var model = BuildModel(source);

        var classSymbol = model.AllSymbols.First(s => s.Name == "Vector" && s.Kind == SymbolKind.Class);
        var classType = (ClassType)classSymbol.DeclaredType!;
        Assert.False(classType.IsBackendBacked);
    }

    // ─── Member-level intrinsic flag and HasBody on methods ─────────────────

    [Fact]
    public void IntrinsicMethodWithoutBody_SetsIsIntrinsicTrueAndHasBodyFalse()
    {
        var source =
            "intrinsic public struct Int32 { " +
            "intrinsic public Int32 Next(); " +
            "}";
        var model = BuildModel(source);

        var method = (Symbol)model.AllSymbols.First(s => s.Name == "Next" && s.Kind == SymbolKind.Method);
        Assert.True(method.IsIntrinsic);
        Assert.False(method.HasBody);
    }

    [Fact]
    public void OrdinaryMethodWithBlockBody_LeavesIsIntrinsicFalseAndHasBodyTrue()
    {
        var source =
            "public class Vector { " +
            "public void Reset() { } " +
            "}";
        var model = BuildModel(source);

        var method = (Symbol)model.AllSymbols.First(s => s.Name == "Reset" && s.Kind == SymbolKind.Method);
        Assert.False(method.IsIntrinsic);
        Assert.True(method.HasBody);
    }

    // ─── Member-level intrinsic flag and HasBody on constructors ────────────

    [Fact]
    public void IntrinsicConstructorWithoutBody_SetsIsIntrinsicTrueAndHasBodyFalse()
    {
        var source =
            "intrinsic public struct Int32 { " +
            "intrinsic public Int32(); " +
            "}";
        var model = BuildModel(source);

        var ctor = (Symbol)model.AllSymbols.First(s => s.Kind == SymbolKind.Constructor);
        Assert.True(ctor.IsIntrinsic);
        Assert.False(ctor.HasBody);
    }

    [Fact]
    public void OrdinaryConstructorWithBlockBody_LeavesIsIntrinsicFalseAndHasBodyTrue()
    {
        var source =
            "public class Vector { " +
            "public Vector() { } " +
            "}";
        var model = BuildModel(source);

        var ctor = (Symbol)model.AllSymbols.First(s => s.Kind == SymbolKind.Constructor);
        Assert.False(ctor.IsIntrinsic);
        Assert.True(ctor.HasBody);
    }

    // ─── Operator declarations with and without body ────────────────────────

    [Fact]
    public void OperatorWithBlockBody_LeavesIsIntrinsicFalseAndHasBodyTrue()
    {
        var source =
            "public struct Vec { " +
            "public static Vec operator +(Vec a, Vec b) { return a; } " +
            "}";
        var model = BuildModel(source);

        var vecSymbol = model.AllSymbols.First(s => s.Name == "Vec" && s.Kind == SymbolKind.Struct);
        var vecType = (StructType)vecSymbol.DeclaredType!;
        var op = (Symbol)vecType.MemberScope.Members.First(m => m.Name == "operator+");
        Assert.Equal(SymbolKind.Method, op.Kind);
        Assert.False(op.IsIntrinsic);
        Assert.True(op.HasBody);
    }

    [Fact]
    public void OperatorNoBody_RecordsHasBodyFalse()
    {
        var source =
            "public struct Vec { " +
            "public static Vec operator +(Vec a, Vec b); " +
            "}";
        var model = BuildModel(source);

        var vecSymbol = model.AllSymbols.First(s => s.Name == "Vec" && s.Kind == SymbolKind.Struct);
        var vecType = (StructType)vecSymbol.DeclaredType!;
        var op = (Symbol)vecType.MemberScope.Members.First(m => m.Name == "operator+");
        Assert.False(op.IsIntrinsic,
            "Operator declared without 'intrinsic' modifier should have IsIntrinsic == false.");
        Assert.False(op.HasBody);
    }
}
