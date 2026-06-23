// Feature: associated-types, Task 5.2: Invalid associated type reference diagnostic (MIST_AT_0004)
// **Validates: Requirements 5.4, 8.3**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Tests.Helpers;
using Xunit;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests verifying that MIST_AT_0004 is emitted when T.X is used in a type position
/// and X is not an associated type slot of any constraining interface/trait.
/// </summary>
public class AssociatedTypeInvalidRefDiagnosticTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    private static ImmutableArray<Diagnostic> CompileAndGetAllDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if (parseResult.Root is null)
            return lexDiag.ToImmutableArray().AddRange(parseDiag.ToImmutableArray());

        var (model, buildDiag) = StdlibTestHelper.BuildModelWithStdlib(
            parseResult.Root, source: source);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        var all = new List<Diagnostic>();
        all.AddRange(lexDiag.ToImmutableArray());
        all.AddRange(parseDiag.ToImmutableArray());
        all.AddRange(buildDiag);
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }

    [Fact]
    public void TDotX_WhereX_IsNotAssociatedTypeSlot_EmitsMIST_AT_0004_AsParameterType()
    {
        // T is constrained by IContainer which declares "Element" but NOT "Foo"
        var source = @"
abstract IContainer {
    type Element;
}

class TestClass {
    void Process<T>(T.Foo item) where T : IContainer {
    }
}
";
        var diagnostics = CompileAndGetAllDiagnostics(source);

        var at0004 = diagnostics.Where(d => d.Id.Value == "MIST_AT_0004").ToList();
        Assert.Single(at0004);
        Assert.Contains("Foo", at0004[0].Message);
        Assert.Contains("T", at0004[0].Message);
    }

    [Fact]
    public void TDotX_WhereX_IsNotAssociatedTypeSlot_EmitsMIST_AT_0004_AsReturnType()
    {
        // T is constrained by IFactory which declares "Product" but NOT "Widget"
        var source = @"
abstract IFactory {
    type Product;
}

class TestClass {
    T.Widget Create<T>() where T : IFactory {
    }
}
";
        var diagnostics = CompileAndGetAllDiagnostics(source);

        var at0004 = diagnostics.Where(d => d.Id.Value == "MIST_AT_0004").ToList();
        Assert.Single(at0004);
        Assert.Contains("Widget", at0004[0].Message);
        Assert.Contains("T", at0004[0].Message);
    }

    [Fact]
    public void TDotX_WhereX_IsNotAssociatedTypeSlot_EmitsMIST_AT_0004_AsLocalVariableType()
    {
        // T is constrained by IContainer which declares "Element" but NOT "Value"
        var source = @"
abstract IContainer {
    type Element;
}

class TestClass {
    void UseContainer<T>(T container) where T : IContainer {
        T.Value x = container;
    }
}
";
        var diagnostics = CompileAndGetAllDiagnostics(source);

        var at0004 = diagnostics.Where(d => d.Id.Value == "MIST_AT_0004").ToList();
        Assert.Single(at0004);
        Assert.Contains("Value", at0004[0].Message);
        Assert.Contains("T", at0004[0].Message);
    }

    [Fact]
    public void TDotX_WhereT_HasNoConstraints_EmitsMIST_AT_0004()
    {
        // T has no constraints — T.Foo should trigger MIST_AT_0004
        // because T is a type parameter but has no associated type "Foo"
        var source = @"
class TestClass {
    T.Foo Process<T>() {
    }
}
";
        var diagnostics = CompileAndGetAllDiagnostics(source);

        var at0004 = diagnostics.Where(d => d.Id.Value == "MIST_AT_0004").ToList();
        Assert.Single(at0004);
        Assert.Contains("Foo", at0004[0].Message);
    }

    [Fact]
    public void TDotX_WhereX_IsValidSlot_DoesNotEmitMIST_AT_0004()
    {
        // T is constrained by IContainer which declares "Element" — T.Element is valid
        var source = @"
abstract IContainer {
    type Element;
}

class TestClass {
    T.Element GetElement<T>() where T : IContainer {
    }
}
";
        var diagnostics = CompileAndGetAllDiagnostics(source);

        var at0004 = diagnostics.Where(d => d.Id.Value == "MIST_AT_0004").ToList();
        Assert.Empty(at0004);
    }

    [Fact]
    public void TDotX_WithMultipleConstraints_OnlyEmitsWhenSlotNotInAny()
    {
        // T is constrained by both IContainer and IFactory.
        // IContainer declares "Element", IFactory declares "Product".
        // T.Unknown should emit MIST_AT_0004 since neither declares "Unknown".
        var source = @"
abstract IContainer {
    type Element;
}

abstract IFactory {
    type Product;
}

class TestClass {
    T.Unknown Process<T>() where T : IContainer, IFactory {
    }
}
";
        var diagnostics = CompileAndGetAllDiagnostics(source);

        var at0004 = diagnostics.Where(d => d.Id.Value == "MIST_AT_0004").ToList();
        Assert.Single(at0004);
        Assert.Contains("Unknown", at0004[0].Message);
    }

    [Fact]
    public void TDotX_WithMultipleConstraints_DoesNotEmitWhenSlotInSecondInterface()
    {
        // T is constrained by both IContainer and IFactory.
        // T.Product should resolve from IFactory — no MIST_AT_0004.
        var source = @"
abstract IContainer {
    type Element;
}

abstract IFactory {
    type Product;
}

class TestClass {
    T.Product Process<T>() where T : IContainer, IFactory {
    }
}
";
        var diagnostics = CompileAndGetAllDiagnostics(source);

        var at0004 = diagnostics.Where(d => d.Id.Value == "MIST_AT_0004").ToList();
        Assert.Empty(at0004);
    }

    [Fact]
    public void DiagnosticMessage_FollowsExpectedFormat()
    {
        var source = @"
abstract IMyInterface {
    type Output;
}

class TestClass {
    T.Missing DoWork<T>() where T : IMyInterface {
    }
}
";
        var diagnostics = CompileAndGetAllDiagnostics(source);

        var at0004 = diagnostics.Where(d => d.Id.Value == "MIST_AT_0004").ToList();
        Assert.Single(at0004);

        var msg = at0004[0].Message;
        Assert.Equal(
            "Type parameter 'T' does not have an associated type 'Missing'; " +
            "'T' is not constrained by an interface or trait declaring 'Missing'.",
            msg);
        Assert.Equal(DiagnosticSeverity.Error, at0004[0].Severity);
    }
}
