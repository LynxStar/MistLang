// Feature: associated-types, Task 5.1: T.SlotName resolution in generic contexts
// **Validates: Requirements 5.1, 5.2, 5.3**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Tests.Helpers;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests verifying that T.SlotName resolves correctly in generic contexts
/// when T is constrained by an abstract type declaring associated type slots.
/// </summary>
public class AssociatedTypeGenericResolutionTests
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
        if(parseResult.Root is null)
        {
            return lexDiag.ToImmutableArray().AddRange(parseDiag.ToImmutableArray());
        }

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
    public void TSlotName_AsParameterType_IsAccepted()
    {
        // T.Element used as a parameter type in a generic method
        var source = @"
abstract IContainer {
    type Element;
}

class TestClass {
    void Process<T>(elem: T.Element) where T : IContainer {
    }
}
";
        var diagnostics = CompileAndGetAllDiagnostics(source);

        var typeErrors = diagnostics.Where(d =>
            d.Id.Value == "MIST_UNDEFINED_TYPE" ||
            d.Id.Value == "MIST_AT_0004").ToList();

        Assert.Empty(typeErrors);
    }

    [Fact]
    public void TSlotName_AsReturnType_IsAccepted()
    {
        // T.Product used as a return type in a generic method
        var source = @"
abstract IFactory {
    type Product;
}

class TestClass {
    T.Product Create<T>() where T : IFactory {
    }
}
";
        var diagnostics = CompileAndGetAllDiagnostics(source);

        var typeErrors = diagnostics.Where(d =>
            d.Id.Value == "MIST_UNDEFINED_TYPE" ||
            d.Id.Value == "MIST_AT_0004").ToList();

        Assert.Empty(typeErrors);
    }

    [Fact]
    public void TSlotName_AsLocalVariableType_IsAccepted()
    {
        // T.Element used as a local variable type
        var source = @"
abstract IContainer {
    type Element;
}

class TestClass {
    void UseContainer<T>(container: T) where T : IContainer {
        T.Element x = container;
    }
}
";
        var diagnostics = CompileAndGetAllDiagnostics(source);

        var typeErrors = diagnostics.Where(d =>
            d.Id.Value == "MIST_UNDEFINED_TYPE" ||
            d.Id.Value == "MIST_AT_0004").ToList();

        Assert.Empty(typeErrors);
    }

    [Fact]
    public void TSlotName_WithConstrainedSlot_AllowsMemberAccess()
    {
        // Member access on a value of type T.Element where Element : IStringable
        var source = @"
abstract IStringable {
    int Length();
}

abstract IContainer {
    type Element : IStringable;
}

class TestClass {
    int GetLength<T>(elem: T.Element) where T : IContainer {
        return elem.Length();
    }
}
";
        var diagnostics = CompileAndGetAllDiagnostics(source);

        // Should not have undefined member errors for Length() on T.Element
        var memberErrors = diagnostics.Where(d =>
            d.Id.Value == "MIST_UNDEFINED_TYPE" ||
            d.Id.Value == "MIST_AT_0004" ||
            (d.Message.Contains("Length") && d.Id.Value.Contains("UNDEFINED"))).ToList();

        Assert.Empty(memberErrors);
    }
}