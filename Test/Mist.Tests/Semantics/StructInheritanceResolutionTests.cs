// Feature: struct-inheritance, Task 7.1: Unit tests for semantic model struct inheritance resolution
// **Validates: Requirements 1.2, 1.3, 1.5, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 3.3**

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
/// Unit tests for struct inheritance resolution in the semantic model builder.
///
/// Tests:
/// 1. BaseType is populated for single-level inheritance
/// 2. BaseType chain for multi-level inheritance (A : B : C)
/// 3. Generic struct inheritance (Derived&lt;T&gt; : Base&lt;T&gt;)
/// 4. MIST_STRUCT_CIRCULAR_INHERITANCE fires for cycles
/// 5. MIST_STRUCT_MAX_DEPTH fires for deep chains
/// 6. MIST_STRUCT_INVALID_BASE fires for class/interface as base
/// 7. MIST_STRUCT_MULTIPLE_INHERITANCE fires for multiple structs in list
/// 8. MIST_STRUCT_FIELD_HIDING fires for duplicate field names
///
/// Requirements: 1.2, 1.3, 1.5, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 3.3
/// </summary>
public class StructInheritanceResolutionTests
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
    /// Finds a StructType by name from the semantic model's symbols.
    /// </summary>
    private static StructType? FindStructType(ISemanticModel model, string name)
    {
        var symbol = model.AllSymbols.FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.Struct);
        return symbol?.DeclaredType as StructType;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 1. Single-Level Inheritance — BaseType is populated
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SingleLevelInheritance_SetsBaseType()
    {
        var source = @"
struct A {
    int X;
}
struct B : A {
    int Y;
}
";
        var model = BuildWithoutErrors(source);

        var structB = FindStructType(model, "B");
        Assert.NotNull(structB);
        Assert.NotNull(structB!.BaseType);
        Assert.Equal("A", structB.BaseType!.DisplayName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Multi-Level Inheritance — BaseType chain
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MultiLevelInheritance_SetsBaseTypeChain()
    {
        var source = @"
struct A {
    int X;
}
struct B : A {
    int Y;
}
struct C : B {
    int Z;
}
";
        var model = BuildWithoutErrors(source);

        var structC = FindStructType(model, "C");
        Assert.NotNull(structC);
        Assert.NotNull(structC!.BaseType);
        Assert.Equal("B", structC.BaseType!.DisplayName);

        var structB = FindStructType(model, "B");
        Assert.NotNull(structB);
        Assert.NotNull(structB!.BaseType);
        Assert.Equal("A", structB.BaseType!.DisplayName);

        // Verify ancestor chain from C
        var ancestors = structC.GetAncestorChain();
        Assert.Equal(2, ancestors.Count);
        Assert.Equal("B", ancestors[0].DisplayName);
        Assert.Equal("A", ancestors[1].DisplayName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. Generic Struct Inheritance — BaseType is populated
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GenericStructInheritance_SetsBaseType()
    {
        var source = @"
struct Base<T> {
    T Value;
}
struct Derived<T> : Base<T> {
    int Extra;
}
";
        var model = BuildWithoutErrors(source);

        var structDerived = FindStructType(model, "Derived");
        Assert.NotNull(structDerived);
        Assert.NotNull(structDerived!.BaseType);

        // The base type should be a constructed type based on Base
        // (either ConstructedType wrapping Base<T> or the raw StructType "Base")
        var baseType = structDerived.BaseType;
        Assert.Contains("Base", baseType!.DisplayName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Circular Inheritance — emits MIST_STRUCT_CIRCULAR_INHERITANCE
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CircularInheritance_EmitsDiagnostic()
    {
        var source = @"
struct A : B {
    int X;
}
struct B : A {
    int Y;
}
";
        var (_, diagnostics) = BuildModel(source);

        var diags = WithId(diagnostics, "MIST_STRUCT_CIRCULAR_INHERITANCE");
        Assert.NotEmpty(diags);
        Assert.All(diags, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public void SelfInheritance_EmitsDiagnostic()
    {
        var source = @"
struct A : A {
    int X;
}
";
        var (_, diagnostics) = BuildModel(source);

        var diags = WithId(diagnostics, "MIST_STRUCT_CIRCULAR_INHERITANCE");
        Assert.NotEmpty(diags);
        Assert.All(diags, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 5. Max Depth Exceeded — emits MIST_STRUCT_MAX_DEPTH
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MaxDepthExceeded_EmitsDiagnostic()
    {
        // Generate a chain of 66 structs (exceeds the 64-level limit)
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("struct S0 { int X; }");
        for (var i = 1; i <= 65; i++)
        {
            sb.AppendLine($"struct S{i} : S{i - 1} {{ int F{i}; }}");
        }

        var (_, diagnostics) = BuildModel(sb.ToString());

        var diags = WithId(diagnostics, "MIST_STRUCT_MAX_DEPTH");
        Assert.NotEmpty(diags);
        Assert.All(diags, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 6. Class/Interface as Base — emits MIST_STRUCT_INVALID_BASE
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ClassAsBase_EmitsDiagnostic()
    {
        var source = @"
class MyClass { }
struct S : MyClass {
    int X;
}
";
        var (_, diagnostics) = BuildModel(source);

        var diags = WithId(diagnostics, "MIST_STRUCT_INVALID_BASE");
        Assert.NotEmpty(diags);
        Assert.All(diags, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 7. Multiple Structs in Base List — emits MIST_STRUCT_MULTIPLE_INHERITANCE
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MultipleStructsInBaseList_EmitsDiagnostic()
    {
        var source = @"
struct A {
    int X;
}
struct B {
    int Y;
}
struct S : A, B {
    int Z;
}
";
        var (_, diagnostics) = BuildModel(source);

        var diags = WithId(diagnostics, "MIST_STRUCT_MULTIPLE_INHERITANCE");
        Assert.NotEmpty(diags);
        Assert.All(diags, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 8. Field Hiding — emits MIST_STRUCT_FIELD_HIDING
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FieldHiding_EmitsDiagnostic()
    {
        var source = @"
struct Base {
    int X;
}
struct Derived : Base {
    int X;
}
";
        var (_, diagnostics) = BuildModel(source);

        var diags = WithId(diagnostics, "MIST_STRUCT_FIELD_HIDING");
        Assert.NotEmpty(diags);
        Assert.All(diags, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public void FieldHiding_AncestorField_EmitsDiagnostic()
    {
        // Field hiding should also detect collisions with grandparent fields
        var source = @"
struct A {
    int X;
}
struct B : A {
    int Y;
}
struct C : B {
    int X;
}
";
        var (_, diagnostics) = BuildModel(source);

        var diags = WithId(diagnostics, "MIST_STRUCT_FIELD_HIDING");
        Assert.NotEmpty(diags);
        Assert.All(diags, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }
}
