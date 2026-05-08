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
/// Unit tests for constraint resolution and validation in the semantic model.
///
/// Tests:
/// 1. Constraint resolution — verify TypeParameterType.Constraints is correctly populated
/// 2. Validation diagnostics — verify each validation rule emits the correct diagnostic
/// 3. Backward compatibility — unconstrained generics still work without diagnostics
/// 4. Constrained member access — members from constrained interfaces/traits/base classes are accessible
///
/// Requirements: 3.9, 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 10.9, 10.10
/// </summary>
public class ConstraintResolutionTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private readonly SemanticModelBuilder _builder = new();

    /// <summary>
    /// Lexes, parses, and builds the semantic model from source.
    /// Returns the model and diagnostics.
    /// </summary>
    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.NotNull(parseResult.Root);

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = _builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        return (model, semBag.ToImmutableArray());
    }

    /// <summary>
    /// Builds the model and asserts no errors were produced.
    /// </summary>
    private ISemanticModel BuildWithoutErrors(string source)
    {
        var (model, diagnostics) = BuildModel(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(errors);
        return model;
    }

    private static ImmutableArray<Diagnostic> WithId(
        ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Id.Value == id).ToImmutableArray();
    }

    /// <summary>
    /// Finds a TypeParameterType by name from the semantic model's symbols.
    /// </summary>
    private static TypeParameterType? FindTypeParameter(ISemanticModel model, string typeName, string typeParamName)
    {
        var typeSymbol = model.AllSymbols.FirstOrDefault(s => s.Name == typeName);
        if (typeSymbol?.DeclaredType is ClassType classType)
            return classType.TypeParameters.OfType<TypeParameterType>().FirstOrDefault(tp => tp.Name == typeParamName);
        if (typeSymbol?.DeclaredType is StructType structType)
            return structType.TypeParameters.OfType<TypeParameterType>().FirstOrDefault(tp => tp.Name == typeParamName);
        if (typeSymbol?.DeclaredType is InterfaceType interfaceType)
            return interfaceType.TypeParameters.OfType<TypeParameterType>().FirstOrDefault(tp => tp.Name == typeParamName);
        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 1. Constraint Resolution — TypeParameterType.Constraints is populated
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ClassConstraint_SetsHasClassConstraint()
    {
        var source = "class Box<T> where T : class { }";
        var model = BuildWithoutErrors(source);

        var tp = FindTypeParameter(model, "Box", "T");
        Assert.NotNull(tp);
        Assert.NotNull(tp!.Constraints);
        Assert.True(tp.Constraints!.HasClassConstraint);
        Assert.False(tp.Constraints.HasStructConstraint);
    }

    [Fact]
    public void StructConstraint_SetsHasStructConstraint()
    {
        var source = "class Box<T> where T : struct { }";
        var model = BuildWithoutErrors(source);

        var tp = FindTypeParameter(model, "Box", "T");
        Assert.NotNull(tp);
        Assert.NotNull(tp!.Constraints);
        Assert.True(tp.Constraints!.HasStructConstraint);
        Assert.False(tp.Constraints.HasClassConstraint);
    }

    [Fact]
    public void ParameterlessConstructorConstraint_AddsEmptyParamList()
    {
        var source = "class Factory<T> where T : new() { }";
        var model = BuildWithoutErrors(source);

        var tp = FindTypeParameter(model, "Factory", "T");
        Assert.NotNull(tp);
        Assert.NotNull(tp!.Constraints);
        Assert.Single(tp.Constraints!.ConstructorConstraints);
        Assert.Empty(tp.Constraints.ConstructorConstraints[0]);
    }

    [Fact]
    public void ParameterizedConstructorConstraint_SingleParam_AddsIntParamList()
    {
        var source = "class Factory<T> where T : new(int) { }";
        var model = BuildWithoutErrors(source);

        var tp = FindTypeParameter(model, "Factory", "T");
        Assert.NotNull(tp);
        Assert.NotNull(tp!.Constraints);
        Assert.Single(tp.Constraints!.ConstructorConstraints);

        var paramTypes = tp.Constraints.ConstructorConstraints[0];
        Assert.Single(paramTypes);
        Assert.Equal("int", paramTypes[0].DisplayName);
    }

    [Fact]
    public void ParameterizedConstructorConstraint_MultipleParams()
    {
        var source = "class Factory<T> where T : new(int, string) { }";
        var model = BuildWithoutErrors(source);

        var tp = FindTypeParameter(model, "Factory", "T");
        Assert.NotNull(tp);
        Assert.NotNull(tp!.Constraints);
        Assert.Single(tp.Constraints!.ConstructorConstraints);

        var paramTypes = tp.Constraints.ConstructorConstraints[0];
        Assert.Equal(2, paramTypes.Count);
        Assert.Equal("int", paramTypes[0].DisplayName);
        Assert.Equal("string", paramTypes[1].DisplayName);
    }

    [Fact]
    public void InterfaceConstraint_PopulatesInterfaceConstraints()
    {
        var source = @"
interface IComparable { }
class Sorter<T> where T : IComparable { }
";
        var model = BuildWithoutErrors(source);

        var tp = FindTypeParameter(model, "Sorter", "T");
        Assert.NotNull(tp);
        Assert.NotNull(tp!.Constraints);
        Assert.Single(tp.Constraints!.InterfaceConstraints);
        Assert.Equal("IComparable", tp.Constraints.InterfaceConstraints[0].DisplayName);
    }

    [Fact]
    public void BaseClassConstraint_PopulatesBaseClassConstraint()
    {
        var source = @"
class Animal { }
class Zoo<T> where T : Animal { }
";
        var model = BuildWithoutErrors(source);

        var tp = FindTypeParameter(model, "Zoo", "T");
        Assert.NotNull(tp);
        Assert.NotNull(tp!.Constraints);
        Assert.NotNull(tp.Constraints!.BaseClassConstraint);
        Assert.Equal("Animal", tp.Constraints.BaseClassConstraint!.DisplayName);
    }

    [Fact]
    public void TraitConstraint_PopulatesTraitConstraints()
    {
        var source = @"
trait Printable {
    void Print();
}
class Printer<T> where T : Printable { }
";
        var model = BuildWithoutErrors(source);

        var tp = FindTypeParameter(model, "Printer", "T");
        Assert.NotNull(tp);
        Assert.NotNull(tp!.Constraints);
        Assert.Single(tp.Constraints!.TraitConstraints);
        Assert.Equal("Printable", tp.Constraints.TraitConstraints[0].DisplayName);
    }

    [Fact]
    public void MultipleConstructorConstraints_AllStored()
    {
        var source = "class MultiFactory<T> where T : new(), new(int), new(bool) { }";
        var model = BuildWithoutErrors(source);

        var tp = FindTypeParameter(model, "MultiFactory", "T");
        Assert.NotNull(tp);
        Assert.NotNull(tp!.Constraints);
        Assert.Equal(3, tp.Constraints!.ConstructorConstraints.Count);

        // new()
        Assert.Empty(tp.Constraints.ConstructorConstraints[0]);
        // new(int)
        Assert.Single(tp.Constraints.ConstructorConstraints[1]);
        Assert.Equal("int", tp.Constraints.ConstructorConstraints[1][0].DisplayName);
        // new(bool)
        Assert.Single(tp.Constraints.ConstructorConstraints[2]);
        Assert.Equal("bool", tp.Constraints.ConstructorConstraints[2][0].DisplayName);
    }

    [Fact]
    public void MultipleConstraintKinds_AllResolved()
    {
        var source = @"
interface IFoo { }
class Container<T> where T : class, IFoo, new() { }
";
        var model = BuildWithoutErrors(source);

        var tp = FindTypeParameter(model, "Container", "T");
        Assert.NotNull(tp);
        Assert.NotNull(tp!.Constraints);
        Assert.True(tp.Constraints!.HasClassConstraint);
        Assert.Single(tp.Constraints.InterfaceConstraints);
        Assert.Equal("IFoo", tp.Constraints.InterfaceConstraints[0].DisplayName);
        Assert.Single(tp.Constraints.ConstructorConstraints);
        Assert.Empty(tp.Constraints.ConstructorConstraints[0]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Validation Diagnostics
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ClassAndStruct_EmitsMIST_CONSTRAINT_0002()
    {
        var source = "class Box<T> where T : class, struct { }";
        var (_, diagnostics) = BuildModel(source);

        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0002");
        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("mutually exclusive", diags[0].Message);
        Assert.Contains("T", diags[0].Message);
    }

    [Fact]
    public void StructAndNew_EmitsMIST_CONSTRAINT_0003_Warning()
    {
        var source = "class Box<T> where T : struct, new() { }";
        var (_, diagnostics) = BuildModel(source);

        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0003");
        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Warning, diags[0].Severity);
        Assert.Contains("redundant", diags[0].Message);
        Assert.Contains("T", diags[0].Message);
    }

    [Fact]
    public void MultipleBaseClasses_EmitsMIST_CONSTRAINT_0004()
    {
        var source = @"
class Animal { }
class Vehicle { }
class Hybrid<T> where T : Animal, Vehicle { }
";
        var (_, diagnostics) = BuildModel(source);

        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0004");
        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("At most one base class", diags[0].Message);
        Assert.Contains("T", diags[0].Message);
    }

    [Fact]
    public void StructAsBaseClass_EmitsMIST_CONSTRAINT_0005()
    {
        var source = @"
struct Point { }
class Container<T> where T : Point { }
";
        var (_, diagnostics) = BuildModel(source);

        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0005");
        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("Struct", diags[0].Message);
        Assert.Contains("Point", diags[0].Message);
    }

    [Fact]
    public void BaseClassAndStruct_EmitsMIST_CONSTRAINT_0006()
    {
        var source = @"
class Animal { }
class Container<T> where T : Animal, struct { }
";
        var (_, diagnostics) = BuildModel(source);

        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0006");
        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("incompatible", diags[0].Message);
        Assert.Contains("struct", diags[0].Message);
    }

    [Fact]
    public void UndefinedTypeParam_EmitsMIST_CONSTRAINT_0001()
    {
        var source = "class Box<T> where U : class { }";
        var (_, diagnostics) = BuildModel(source);

        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0001");
        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("U", diags[0].Message);
        Assert.Contains("not defined", diags[0].Message);
    }

    [Fact]
    public void DuplicateWhereClauses_EmitsMIST_CONSTRAINT_0007()
    {
        var source = "class Box<T> where T : class where T : struct { }";
        var (_, diagnostics) = BuildModel(source);

        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0007");
        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("multiple", diags[0].Message);
        Assert.Contains("T", diags[0].Message);
    }

    [Fact]
    public void UnresolvableType_EmitsMIST_CONSTRAINT_0008()
    {
        var source = "class Box<T> where T : NonExistentType { }";
        var (_, diagnostics) = BuildModel(source);

        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0008");
        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("could not be resolved", diags[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. Backward Compatibility — unconstrained generics work without diagnostics
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UnconstrainedGenericClass_NoDiagnostics()
    {
        var source = "class Box<T> { }";
        var (model, diagnostics) = BuildModel(source);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(errors);

        var tp = FindTypeParameter(model, "Box", "T");
        Assert.NotNull(tp);
        Assert.Null(tp!.Constraints);
    }

    [Fact]
    public void UnconstrainedGenericStruct_NoDiagnostics()
    {
        var source = "struct Wrapper<T> { }";
        var (model, diagnostics) = BuildModel(source);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(errors);

        var tp = FindTypeParameter(model, "Wrapper", "T");
        Assert.NotNull(tp);
        Assert.Null(tp!.Constraints);
    }

    [Fact]
    public void UnconstrainedGenericInterface_NoDiagnostics()
    {
        var source = "interface IReadable<T> { }";
        var (model, diagnostics) = BuildModel(source);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(errors);

        var tp = FindTypeParameter(model, "IReadable", "T");
        Assert.NotNull(tp);
        Assert.Null(tp!.Constraints);
    }

    [Fact]
    public void UnconstrainedGenericMethod_NoDiagnostics()
    {
        var source = "class Service { public T Get<T>() { } }";
        var (_, diagnostics) = BuildModel(source);

        // No constraint-related diagnostics
        var constraintDiags = diagnostics.Where(d => d.Id.Value.StartsWith("MIST_CONSTRAINT")).ToArray();
        Assert.Empty(constraintDiags);
    }

    [Fact]
    public void MultipleUnconstrainedTypeParams_NoDiagnostics()
    {
        var source = "class Pair<K, V> { }";
        var (model, diagnostics) = BuildModel(source);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(errors);

        var tpK = FindTypeParameter(model, "Pair", "K");
        var tpV = FindTypeParameter(model, "Pair", "V");
        Assert.NotNull(tpK);
        Assert.NotNull(tpV);
        Assert.Null(tpK!.Constraints);
        Assert.Null(tpV!.Constraints);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Constrained Member Access
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void InterfaceConstraint_MembersAccessible()
    {
        var source = @"
interface IGreetable {
    string Greet();
}
class Greeter<T> where T : IGreetable {
    public string DoGreet(T item) {
        return item.Greet();
    }
}
";
        var (_, diagnostics) = BuildModel(source);

        // No MIST_UNDEFINED_MEMBER diagnostic should be emitted for item.Greet()
        var undefinedMemberDiags = diagnostics.Where(d =>
            d.Id.Value.Contains("UNDEFINED_MEMBER") ||
            d.Id.Value.Contains("UNDEFINED")).ToArray();

        // Filter to only those that mention "Greet" — we don't want false positives
        var greetDiags = undefinedMemberDiags.Where(d => d.Message.Contains("Greet")).ToArray();
        Assert.Empty(greetDiags);
    }

    [Fact]
    public void TraitConstraint_MembersAccessible()
    {
        var source = @"
trait Printable {
    void Print();
}
class Printer<T> where T : Printable {
    public void DoPrint(T item) {
        item.Print();
    }
}
";
        var (_, diagnostics) = BuildModel(source);

        // No MIST_UNDEFINED_MEMBER diagnostic should be emitted for item.Print()
        var printDiags = diagnostics.Where(d =>
            d.Message.Contains("Print") &&
            (d.Id.Value.Contains("UNDEFINED_MEMBER") || d.Id.Value.Contains("UNDEFINED"))).ToArray();
        Assert.Empty(printDiags);
    }

    [Fact]
    public void BaseClassConstraint_MembersAccessible()
    {
        var source = @"
class Animal {
    public string Name() { return ""animal""; }
}
class Zoo<T> where T : Animal {
    public string GetName(T item) {
        return item.Name();
    }
}
";
        var (_, diagnostics) = BuildModel(source);

        // No MIST_UNDEFINED_MEMBER diagnostic should be emitted for item.Name()
        var nameDiags = diagnostics.Where(d =>
            d.Message.Contains("Name") &&
            (d.Id.Value.Contains("UNDEFINED_MEMBER") || d.Id.Value.Contains("UNDEFINED"))).ToArray();
        Assert.Empty(nameDiags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Additional edge cases
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ConstraintsOnStruct_ResolvedCorrectly()
    {
        var source = @"
interface IFoo { }
struct Wrapper<T> where T : IFoo { }
";
        var model = BuildWithoutErrors(source);

        var tp = FindTypeParameter(model, "Wrapper", "T");
        Assert.NotNull(tp);
        Assert.NotNull(tp!.Constraints);
        Assert.Single(tp.Constraints!.InterfaceConstraints);
    }

    [Fact]
    public void ConstraintsOnInterface_ResolvedCorrectly()
    {
        var source = @"
interface IFoo { }
interface IRepository<T> where T : IFoo { }
";
        var model = BuildWithoutErrors(source);

        var tp = FindTypeParameter(model, "IRepository", "T");
        Assert.NotNull(tp);
        Assert.NotNull(tp!.Constraints);
        Assert.Single(tp.Constraints!.InterfaceConstraints);
    }

    [Fact]
    public void MultipleTypeParams_EachConstrainedIndependently()
    {
        var source = @"
interface IFoo { }
class Pair<K, V> where K : class where V : IFoo { }
";
        var model = BuildWithoutErrors(source);

        var tpK = FindTypeParameter(model, "Pair", "K");
        Assert.NotNull(tpK);
        Assert.NotNull(tpK!.Constraints);
        Assert.True(tpK.Constraints!.HasClassConstraint);
        Assert.Empty(tpK.Constraints.InterfaceConstraints);

        var tpV = FindTypeParameter(model, "Pair", "V");
        Assert.NotNull(tpV);
        Assert.NotNull(tpV!.Constraints);
        Assert.False(tpV.Constraints!.HasClassConstraint);
        Assert.Single(tpV.Constraints.InterfaceConstraints);
    }

    [Fact]
    public void StructAndParameterizedNew_NoRedundancyWarning()
    {
        // struct + new(int) should NOT emit MIST_CONSTRAINT_0003 (only new() is redundant)
        var source = "class Box<T> where T : struct, new(int) { }";
        var (_, diagnostics) = BuildModel(source);

        var diags = WithId(diagnostics, "MIST_CONSTRAINT_0003");
        Assert.Empty(diags);
    }

    [Fact]
    public void ValidConstraints_NoDiagnostics()
    {
        var source = @"
interface IComparable { }
class Sorter<T> where T : class, IComparable, new() { }
";
        var (_, diagnostics) = BuildModel(source);

        var constraintDiags = diagnostics.Where(d => d.Id.Value.StartsWith("MIST_CONSTRAINT")).ToArray();
        Assert.Empty(constraintDiags);
    }
}
