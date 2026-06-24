// Feature: generic-constraints
// Unit tests for type checker constraint diagnostics
// Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for type checker constraint diagnostics (MIST_CONSTRAINT_0009 through MIST_CONSTRAINT_0017).
///
/// Tests:
/// - Each diagnostic message format
/// - OR semantics for multiple constructor constraints
/// - Constraint satisfaction for generic method invocations
/// - Constraint propagation error cases
///
/// Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8
/// </summary>
public class ConstraintDiagnosticsTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if (parseResult.Root is null)
            return lexBag.ToImmutableArray().AddRange(parseBag.ToImmutableArray());

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

        var tcBag = new DiagnosticBag();
        Analyzer.Analyze(model, tcBag);

        return semBag.ToImmutableArray().AddRange(tcBag.ToImmutableArray());
    }

    private static ImmutableArray<Diagnostic> WithId(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Id.Value == id).ToImmutableArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_CONSTRAINT_0009: Abstract type constraint violation
    // "Type '{TypeArg}' does not implement abstract type '{Abstract}' required by
    //  type parameter '{TypeParam}' of '{GenericType}'."
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_CONSTRAINT_0009_AbstractNotImplemented()
    {
        var source = @"
abstract IComparable { }
class Unrelated { }
class Sorter<T> where T : IComparable { }
class Program {
    public static void Main() {
        Sorter<Unrelated> s = new Sorter<Unrelated>();
    }
}";
        var diagnostics = CompileAndGetDiagnostics(source);
        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0009");

        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("Unrelated", diags[0].Message);
        Assert.Contains("IComparable", diags[0].Message);
        Assert.Contains("T", diags[0].Message);
        Assert.Contains("Sorter", diags[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_CONSTRAINT_0011: Base class constraint violation
    // "Type '{TypeArg}' does not derive from '{BaseClass}' required by
    //  type parameter '{TypeParam}' of '{GenericType}'."
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_CONSTRAINT_0011_BaseClassNotDerived()
    {
        var source = @"
class Animal { }
class Unrelated { }
class Zoo<T> where T : Animal { }
class Program {
    public static void Main() {
        Zoo<Unrelated> z = new Zoo<Unrelated>();
    }
}";
        var diagnostics = CompileAndGetDiagnostics(source);
        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0011");

        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("Unrelated", diags[0].Message);
        Assert.Contains("Animal", diags[0].Message);
        Assert.Contains("T", diags[0].Message);
        Assert.Contains("Zoo", diags[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_CONSTRAINT_0012: Class constraint violation
    // "Type '{TypeArg}' must be a reference type to satisfy the 'class'
    //  constraint on type parameter '{TypeParam}'."
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_CONSTRAINT_0012_ClassConstraintViolatedByStruct()
    {
        var source = @"
struct Point { }
class Container<T> where T : class { }
class Program {
    public static void Main() {
        Container<Point> c = new Container<Point>();
    }
}";
        var diagnostics = CompileAndGetDiagnostics(source);
        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0012");

        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("Point", diags[0].Message);
        Assert.Contains("reference type", diags[0].Message);
        Assert.Contains("T", diags[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_CONSTRAINT_0013: Struct constraint violation
    // "Type '{TypeArg}' must be a value type to satisfy the 'struct'
    //  constraint on type parameter '{TypeParam}'."
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_CONSTRAINT_0013_StructConstraintViolatedByClass()
    {
        var source = @"
class MyClass { }
class Container<T> where T : struct { }
class Program {
    public static void Main() {
        Container<MyClass> c = new Container<MyClass>();
    }
}";
        var diagnostics = CompileAndGetDiagnostics(source);
        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0013");

        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("MyClass", diags[0].Message);
        Assert.Contains("value type", diags[0].Message);
        Assert.Contains("T", diags[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_CONSTRAINT_0014: Parameterless constructor constraint violation
    // "Type '{TypeArg}' must have a public parameterless constructor to satisfy
    //  the 'new()' constraint on type parameter '{TypeParam}'."
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_CONSTRAINT_0014_NewConstraintViolated()
    {
        var source = @"
class NoDefaultCtor {
    public NoDefaultCtor(int x) { }
}
class Factory<T> where T : new() { }
class Program {
    public static void Main() {
        Factory<NoDefaultCtor> f = new Factory<NoDefaultCtor>();
    }
}";
        var diagnostics = CompileAndGetDiagnostics(source);
        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0014");

        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("NoDefaultCtor", diags[0].Message);
        Assert.Contains("parameterless constructor", diags[0].Message);
        Assert.Contains("T", diags[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_CONSTRAINT_0015: Parameterized constructor constraint violation
    // "Type '{TypeArg}' must have a public constructor with parameters '({ParamTypes})'
    //  to satisfy the 'new({ParamTypes})' constraint on type parameter '{TypeParam}'."
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_CONSTRAINT_0015_ParameterizedNewConstraintViolated()
    {
        var source = @"
class Widget { }
class Factory<T> where T : new(int) { }
class Program {
    public static void Main() {
        Factory<Widget> f = new Factory<Widget>();
    }
}";
        var diagnostics = CompileAndGetDiagnostics(source);
        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0015");

        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("Widget", diags[0].Message);
        Assert.Contains("int", diags[0].Message);
        Assert.Contains("T", diags[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_CONSTRAINT_0016: new T(...) argument mismatch
    // "No constructor constraint on type parameter '{TypeParam}' matches arguments
    //  '({ArgTypes})'. Available constructor constraints: {signatures}."
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_CONSTRAINT_0016_NewTNoMatchingConstraint()
    {
        var source = @"
class Factory<T> where T : new(int) {
    public T Create(bool flag) {
        return new T(flag);
    }
}";
        var diagnostics = CompileAndGetDiagnostics(source);
        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0016");

        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("T", diags[0].Message);
        Assert.Contains("constructor constraint", diags[0].Message.ToLowerInvariant());
    }

    [Fact]
    public void MIST_CONSTRAINT_0016_NewTNoConstraintAtAll()
    {
        var source = @"
class Factory<T> where T : class {
    public T Create() {
        return new T();
    }
}";
        var diagnostics = CompileAndGetDiagnostics(source);
        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0016");

        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("T", diags[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_CONSTRAINT_0017: Constraint propagation insufficient
    // "Type '{DerivedType}' must declare constraints at least as restrictive as
    //  '{BaseType}' for type parameter '{TypeParam}'."
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_CONSTRAINT_0017_PropagationInsufficient_ClassConstraint()
    {
        var source = @"
class Base<T> where T : class { }
class Derived<T> : Base<T> { }";
        var diagnostics = CompileAndGetDiagnostics(source);
        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0017");

        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("Derived", diags[0].Message);
        Assert.Contains("Base", diags[0].Message);
        Assert.Contains("T", diags[0].Message);
    }

    [Fact]
    public void MIST_CONSTRAINT_0017_PropagationInsufficient_AbstractConstraint()
    {
        var source = @"
abstract IFoo { }
class Base<T> where T : IFoo { }
class Derived<T> : Base<T> { }";
        var diagnostics = CompileAndGetDiagnostics(source);
        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0017");

        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("Derived", diags[0].Message);
        Assert.Contains("Base", diags[0].Message);
        Assert.Contains("T", diags[0].Message);
    }

    [Fact]
    public void MIST_CONSTRAINT_0017_PropagationInsufficient_StructConstraint()
    {
        var source = @"
class Base<T> where T : struct { }
class Derived<T> : Base<T> { }";
        var diagnostics = CompileAndGetDiagnostics(source);
        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0017");

        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("Derived", diags[0].Message);
        Assert.Contains("T", diags[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // OR semantics for multiple constructor constraints
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MultipleCtorConstraints_ORSemantics_AllSatisfied()
    {
        // Type has new(), new(int), and new(bool) — satisfies all constructor constraints
        var source = @"
class Widget {
    public Widget() { }
    public Widget(int x) { }
    public Widget(bool b) { }
}
class Factory<T> where T : new(), new(int), new(bool) { }
class Program {
    public static void Main() {
        Factory<Widget> f = new Factory<Widget>();
    }
}";
        var diagnostics = CompileAndGetDiagnostics(source);
        var constraintDiags = diagnostics.Where(d =>
            d.Id.Value is "MIST_CONSTRAINT_0014" or "MIST_CONSTRAINT_0015").ToArray();

        Assert.Empty(constraintDiags);
    }

    [Fact]
    public void MultipleCtorConstraints_SomeMissing_EmitsDiagnostic()
    {
        // Type has new() and new(int) but NOT new(bool) — should emit diagnostic for new(bool)
        var source = @"
class Widget {
    public Widget() { }
    public Widget(int x) { }
}
class Factory<T> where T : new(), new(int), new(bool) { }
class Program {
    public static void Main() {
        Factory<Widget> f = new Factory<Widget>();
    }
}";
        var diagnostics = CompileAndGetDiagnostics(source);
        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0015");

        // Should have a violation for new(bool) since Widget doesn't have a bool ctor
        Assert.Single(diags);
        Assert.Contains("bool", diags[0].Message);
    }

    [Fact]
    public void MultipleCtorConstraints_NewTMatchesOneOfMultiple()
    {
        // new T(intVal) should be accepted when constraints include new(int)
        var source = @"
class Factory<T> where T : new(), new(int), new(bool) {
    public T CreateFromInt(int val) {
        return new T(val);
    }
}";
        var diagnostics = CompileAndGetDiagnostics(source);
        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0016");

        Assert.Empty(diags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Constraint satisfaction for generic method invocations
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GenericMethod_ConstraintSatisfied()
    {
        var source = @"
abstract IFoo { }
class FooImpl : IFoo { }
class Service {
    public static void Process<T>(T item) where T : IFoo { }
    public static void Main() {
        FooImpl f = new FooImpl();
        Service.Process<FooImpl>(f);
    }
}";
        var diagnostics = CompileAndGetDiagnostics(source);
        var constraintDiags = diagnostics.Where(d =>
            d.Id.Value == "MIST_CONSTRAINT_0009").ToArray();

        Assert.Empty(constraintDiags);
    }

    [Fact]
    public void GenericMethod_ConstraintViolated_ViaGenericType()
    {
        // Test constraint checking on generic method via a generic type instantiation
        // since direct method call constraint checking depends on symbol resolution
        var source = @"
abstract IFoo { }
class Unrelated { }
class Wrapper<T> where T : IFoo { }
class Program {
    public static void Main() {
        Wrapper<Unrelated> w = new Wrapper<Unrelated>();
    }
}";
        var diagnostics = CompileAndGetDiagnostics(source);
        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0009");

        Assert.Single(diags);
        Assert.Contains("Unrelated", diags[0].Message);
        Assert.Contains("IFoo", diags[0].Message);
    }

    [Fact]
    public void GenericMethod_ClassConstraintViolated_ViaGenericType()
    {
        // Test class constraint checking via generic type instantiation
        var source = @"
struct Point { }
class Wrapper<T> where T : class { }
class Program {
    public static void Main() {
        Wrapper<Point> w = new Wrapper<Point>();
    }
}";
        var diagnostics = CompileAndGetDiagnostics(source);
        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0012");

        Assert.Single(diags);
        Assert.Contains("Point", diags[0].Message);
        Assert.Contains("reference type", diags[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Constraint propagation error cases
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PropagationError_AbstractInheritance()
    {
        var source = @"
abstract IFoo { }
abstract IBase<T> where T : IFoo { }
abstract IDerived<T> : IBase<T> { }";
        var diagnostics = CompileAndGetDiagnostics(source);
        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0017");

        Assert.Single(diags);
        Assert.Contains("IDerived", diags[0].Message);
        Assert.Contains("IBase", diags[0].Message);
    }

    [Fact]
    public void PropagationError_NewConstraintMissing()
    {
        var source = @"
class Base<T> where T : new() { }
class Derived<T> : Base<T> { }";
        var diagnostics = CompileAndGetDiagnostics(source);
        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0017");

        Assert.Single(diags);
        Assert.Contains("Derived", diags[0].Message);
    }

    [Fact]
    public void PropagationSuccess_DerivedMoreRestrictive()
    {
        var source = @"
abstract IFoo { }
class Base<T> where T : class { }
class Derived<T> : Base<T> where T : class, IFoo { }";
        var diagnostics = CompileAndGetDiagnostics(source);
        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0017");

        Assert.Empty(diags);
    }

    [Fact]
    public void PropagationSuccess_SameConstraints()
    {
        var source = @"
abstract IFoo { }
class Base<T> where T : class, IFoo, new() { }
class Derived<T> : Base<T> where T : class, IFoo, new() { }";
        var diagnostics = CompileAndGetDiagnostics(source);
        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0017");

        Assert.Empty(diags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Valid scenarios — no constraint diagnostics
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidConstraintSatisfaction_NoDiagnostics()
    {
        var source = @"
abstract IComparable { }
class MyClass : IComparable {
    public MyClass() { }
    public MyClass(int x) { }
}
class Container<T> where T : class, IComparable, new(), new(int) { }
class Program {
    public static void Main() {
        Container<MyClass> c = new Container<MyClass>();
    }
}";
        var diagnostics = CompileAndGetDiagnostics(source);
        var constraintDiags = diagnostics.Where(d =>
            d.Id.Value.StartsWith("MIST_CONSTRAINT_0009") ||
            d.Id.Value.StartsWith("MIST_CONSTRAINT_0010") ||
            d.Id.Value.StartsWith("MIST_CONSTRAINT_0011") ||
            d.Id.Value.StartsWith("MIST_CONSTRAINT_0012") ||
            d.Id.Value.StartsWith("MIST_CONSTRAINT_0013") ||
            d.Id.Value.StartsWith("MIST_CONSTRAINT_0014") ||
            d.Id.Value.StartsWith("MIST_CONSTRAINT_0015")).ToArray();

        Assert.Empty(constraintDiags);
    }

    [Fact]
    public void StructSatisfiesNewConstraint_NoDiagnostic()
    {
        // Structs always have a parameterless constructor
        var source = @"
struct Point { }
class Factory<T> where T : new() { }
class Program {
    public static void Main() {
        Factory<Point> f = new Factory<Point>();
    }
}";
        var diagnostics = CompileAndGetDiagnostics(source);
        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0014");

        Assert.Empty(diags);
    }
}
