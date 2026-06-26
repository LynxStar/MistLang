// Feature: generic-constraints
// Unit tests for C# emission of generic constraints
// Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8, 8.9

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for C# emission of generic constraints.
///
/// Tests:
/// - Standard constraint emission for each constraint kind
/// - Factory delegate lowering for parameterized constructor constraints
/// - Call-site transformation with factory lambdas
/// - Multiple parameterized constructor constraints emit multiple delegates
/// - new T() with parameterless constraint emits standard new T()
/// - Constraint ordering is correct regardless of source order
///
/// Requirements: 8.1-8.9
/// </summary>
public sealed class ConstraintEmissionTests : IDisposable
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private static readonly SemanticModelBuilder Builder = new();
    private readonly string outputDir;

    public ConstraintEmissionTests()
    {
        outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistConstraintEmitTests_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private string CompileAndEmit(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if(parseResult.Root is null)
        {
            return "";
        }

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        // Clean output dir
        foreach(var file in Directory.GetFiles(outputDir))
        {
            File.Delete(file);
        }

        var emitBag = new DiagnosticBag();
        CSharpEmitter.Emit(model, outputDir, "TestProject", emitBag);

        var csFile = Directory.GetFiles(outputDir, "*.cs").FirstOrDefault();
        return csFile is not null ? File.ReadAllText(csFile) : "";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Standard constraint emission for each constraint kind
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ClassConstraint_EmitsClassKeyword()
    {
        var source = @"
class Container<T> where T : class { }
class Program {
    public static void Main() { }
}";
        var cs = CompileAndEmit(source);
        Assert.Contains("where T : class", cs);
    }

    [Fact]
    public void StructConstraint_EmitsStructKeyword()
    {
        var source = @"
class Container<T> where T : struct { }
class Program {
    public static void Main() { }
}";
        var cs = CompileAndEmit(source);
        Assert.Contains("where T : struct", cs);
    }

    [Fact]
    public void AbstractConstraint_EmitsAbstractName()
    {
        var source = @"
abstract IFoo { }
class Container<T> where T : IFoo { }
class Program {
    public static void Main() { }
}";
        var cs = CompileAndEmit(source);
        Assert.Contains("where T : IFoo", cs);
    }

    [Fact]
    public void BaseClassConstraint_EmitsClassName()
    {
        var source = @"
class Animal { }
class Container<T> where T : Animal { }
class Program {
    public static void Main() { }
}";
        var cs = CompileAndEmit(source);
        Assert.Contains("where T : Animal", cs);
    }

    [Fact]
    public void ParameterlessNewConstraint_EmitsNewParens()
    {
        var source = @"
class Container<T> where T : new() { }
class Program {
    public static void Main() { }
}";
        var cs = CompileAndEmit(source);
        Assert.Contains("where T : new()", cs);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Factory delegate lowering for parameterized constructor constraints
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParameterizedNewConstraint_EmitsFactoryDelegateField()
    {
        var source = @"
class Factory<T> where T : new(int) { }
class Program {
    public static void Main() { }
}";
        var cs = CompileAndEmit(source);
        // Should have a Func<int, T> field
        Assert.Matches(new Regex(@"private\s+readonly\s+Func<int,\s*T>\s+_factory"), cs);
    }

    [Fact]
    public void ParameterizedNewConstraint_EmitsFactoryConstructor()
    {
        var source = @"
class Factory<T> where T : new(int) { }
class Program {
    public static void Main() { }
}";
        var cs = CompileAndEmit(source);
        // Should have a constructor accepting Func<int, T>
        Assert.Matches(new Regex(@"public\s+Factory\(Func<int,\s*T>"), cs);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Call-site transformation with factory lambdas
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CallSite_ParameterizedConstraint_EmitsFactoryLambda()
    {
        var source = @"
class Factory<T> where T : new(int) { }
class MyClass {
    public MyClass(int x) { }
}
class Program {
    public static void Main() {
        Factory<MyClass> f = new Factory<MyClass>();
    }
}";
        var cs = CompileAndEmit(source);
        // Call site should include a lambda for the factory
        Assert.Matches(new Regex(@"new\s+Factory<MyClass>\(.*=>.*new\s+MyClass"), cs);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Multiple parameterized constructor constraints emit multiple delegates
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MultipleParameterizedConstraints_EmitsMultipleFactoryFields()
    {
        var source = @"
class MultiFactory<T> where T : new(), new(int), new(bool) { }
class Program {
    public static void Main() { }
}";
        var cs = CompileAndEmit(source);
        // Should have new() as native constraint
        Assert.Contains("where T : new()", cs);
        // Should have factory fields for int and bool variants
        Assert.Matches(new Regex(@"Func<int,\s*T>"), cs);
        Assert.Matches(new Regex(@"Func<bool,\s*T>"), cs);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // new T() with parameterless constraint emits standard new T()
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParameterlessNew_EmitsStandardNewT()
    {
        var source = @"
class Creator<T> where T : new() {
    public T Create() {
        return new T();
    }
}
class Program {
    public static void Main() { }
}";
        var cs = CompileAndEmit(source);
        // Should emit standard new T() — not a factory invocation
        Assert.Contains("new T()", cs);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Constraint ordering is correct regardless of source order
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ConstraintOrdering_NewFirst_EmitsNewLast()
    {
        var source = @"
abstract IFoo { }
class Container<T> where T : new(), IFoo, class { }
class Program {
    public static void Main() { }
}";
        var cs = CompileAndEmit(source);
        // Extract the where clause
        var match = Regex.Match(cs, @"where T : (.+)");
        Assert.True(match.Success, "Expected where clause in output");
        var constraints = match.Groups[1].Value.Trim();
        // class should be first, IFoo in middle, new() last
        Assert.StartsWith("class", constraints);
        Assert.EndsWith("new()", constraints);
    }

    [Fact]
    public void ConstraintOrdering_BaseClassAfterInterface_EmitsBaseClassBeforeInterface()
    {
        var source = @"
abstract IFoo { }
class Animal { }
class Container<T> where T : IFoo, Animal, new() { }
class Program {
    public static void Main() { }
}";
        var cs = CompileAndEmit(source);
        var match = Regex.Match(cs, @"where T : (.+)");
        Assert.True(match.Success, "Expected where clause in output");
        var constraints = match.Groups[1].Value.Trim();
        // Animal (base class) should come before IFoo (interface)
        var animalIdx = constraints.IndexOf("Animal", StringComparison.Ordinal);
        var ifooIdx = constraints.IndexOf("IFoo", StringComparison.Ordinal);
        var newIdx = constraints.IndexOf("new()", StringComparison.Ordinal);
        Assert.True(animalIdx < ifooIdx, $"Animal should come before IFoo: '{constraints}'");
        Assert.True(ifooIdx < newIdx, $"IFoo should come before new(): '{constraints}'");
    }

    [Fact]
    public void MultipleWhereClauses_EmitsSeparateCSharpWhereClauses()
    {
        var source = @"
class Container<T, U> where T : class where U : struct { }
class Program {
    public static void Main() { }
}";
        var cs = CompileAndEmit(source);
        Assert.Contains("where T : class", cs);
        Assert.Contains("where U : struct", cs);
    }

    [Fact]
    public void MethodConstraints_EmitsWhereClauseOnMethod()
    {
        var source = @"
abstract IFoo { }
class Program {
    public static void DoWork<T>(T item) where T : IFoo { }
    public static void Main() { }
}";
        var cs = CompileAndEmit(source);
        Assert.Contains("where T : IFoo", cs);
    }
}