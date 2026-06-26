// Feature: optional-defaultability, Property 7: Default constraint satisfaction
// **Validates: Requirements 5.6, 9.2**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for default constraint satisfaction at generic instantiation sites.
///
/// Property 7: For any generic instantiation <c>G&lt;A&gt;</c> where <c>G</c> declares
/// <c>where T : default</c>, if <c>A</c> is a defaultable type the instantiation SHALL
/// produce no diagnostic, and if <c>A</c> is non-defaultable the compiler SHALL emit
/// <c>MIST_DEFAULT_CONSTRAINT_NOT_SATISFIED</c>.
/// </summary>
public class DefaultConstraintSatisfactionPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_dflt_cstr_pbt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EmitToDisk: false,
                EnabledAnalyzerIds: ImmutableArray<string>.Empty);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static bool HasDiagnostic(ImmutableArray<Diagnostic> diagnostics, string id) =>
        diagnostics.Any(d => d.Id.Value == id);

    // ─── Scenario types ──────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A defaultable type argument used to instantiate a generic with <c>where T : default</c>.
    /// Should produce no constraint-violation diagnostic.
    /// </summary>
    public sealed record DefaultableTypeArgScenario(
        string TypeArgName,
        string TypeArgDeclaration,
        DefaultableKind Kind)
    {
        public override string ToString() =>
            $"defaultable {Kind}: {TypeArgName}";
    }

    /// <summary>
    /// A non-defaultable type argument used to instantiate a generic with <c>where T : default</c>.
    /// Should produce <c>MIST_DEFAULT_CONSTRAINT_NOT_SATISFIED</c>.
    /// </summary>
    public sealed record NonDefaultableTypeArgScenario(
        string TypeArgName,
        string TypeArgDeclaration,
        NonDefaultableKind Kind)
    {
        public override string ToString() =>
            $"non-defaultable {Kind}: {TypeArgName}";
    }

    /// <summary>The kind of defaultable type used as a type argument.</summary>
    public enum DefaultableKind
    {
        Primitive,
        ClassNoConstructorAllDefaultableFields,
    }

    /// <summary>The kind of non-defaultable type used as a type argument.</summary>
    public enum NonDefaultableKind
    {
        ClassWithConstructorNoDefaultBlock,
        AbstractType,
    }

#pragma warning restore CA1034

    // ─── Name and type pools ─────────────────────────────────────────────

    private static readonly string[] PrimitiveTypes =
        ["int", "string", "bool", "float", "double"];

    private static readonly string[] ClassNames =
        ["Widget", "Config", "Gadget", "Sensor", "Agent"];

    private static readonly string[] AbstractNames =
        ["IRenderer", "ILogger", "IService", "IHandler"];

    private static readonly string[] FieldNames =
        ["alpha", "beta", "gamma", "delta"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a defaultable type argument scenario — either a primitive type
    /// or a class with no constructor and all-defaultable fields.
    /// </summary>
    private static Gen<DefaultableTypeArgScenario> GenDefaultableTypeArgScenario()
    {
        var primitiveGen =
            from primType in Gen.Elements(PrimitiveTypes)
            select new DefaultableTypeArgScenario(
                TypeArgName: primType,
                TypeArgDeclaration: "", // primitives need no declaration
                Kind: DefaultableKind.Primitive);

        var classGen =
            from className in Gen.Elements(ClassNames)
            from fieldCount in Gen.Choose(1, 3)
            from fieldTypes in Gen.ArrayOf(Gen.Elements(PrimitiveTypes), fieldCount)
            let fields = fieldTypes.Select((ft, i) =>
                $"    public {ft} {FieldNames[i]};").ToArray()
            let decl = $"public class {className}\n{{\n{string.Join("\n", fields)}\n}}"
            select new DefaultableTypeArgScenario(
                TypeArgName: className,
                TypeArgDeclaration: decl,
                Kind: DefaultableKind.ClassNoConstructorAllDefaultableFields);

        return Gen.OneOf(primitiveGen, classGen);
    }

    /// <summary>
    /// Generates a non-defaultable type argument scenario — a class with a constructor
    /// and no default block, or an abstract type.
    /// </summary>
    private static Gen<NonDefaultableTypeArgScenario> GenNonDefaultableTypeArgScenario()
    {
        var classWithCtorGen =
            from className in Gen.Elements(ClassNames)
            from fieldCount in Gen.Choose(1, 3)
            from fieldTypes in Gen.ArrayOf(Gen.Elements(PrimitiveTypes), fieldCount)
            let fields = fieldTypes.Select((ft, i) =>
                $"    public {ft} {FieldNames[i]};").ToArray()
            let assignments = fieldTypes.Select((_, i) =>
                $"        this.{FieldNames[i]} = {FieldNames[i]};").ToArray()
            let ctorParams = fieldTypes.Select((ft, i) =>
                $"{ft} {FieldNames[i]}").ToArray()
            let decl = $"public class {className}\n{{\n" +
                       $"{string.Join("\n", fields)}\n\n" +
                       $"    public {className}({string.Join(", ", ctorParams)})\n    {{\n" +
                       $"{string.Join("\n", assignments)}\n    }}\n}}"
            select new NonDefaultableTypeArgScenario(
                TypeArgName: className,
                TypeArgDeclaration: decl,
                Kind: NonDefaultableKind.ClassWithConstructorNoDefaultBlock);

        var abstractGen =
            from abstractName in Gen.Elements(AbstractNames)
            let decl = $"public abstract {abstractName}\n{{\n}}"
            select new NonDefaultableTypeArgScenario(
                TypeArgName: abstractName,
                TypeArgDeclaration: decl,
                Kind: NonDefaultableKind.AbstractType);

        return Gen.OneOf(classWithCtorGen, abstractGen);
    }

    // ─── Arbitrary registrations ─────────────────────────────────────────

    public static Arbitrary<DefaultableTypeArgScenario> ArbitraryDefaultableTypeArg() =>
        GenDefaultableTypeArgScenario().ToArbitrary();

    public static Arbitrary<NonDefaultableTypeArgScenario> ArbitraryNonDefaultableTypeArg() =>
        GenNonDefaultableTypeArgScenario().ToArbitrary();

    // ─── Source generation helpers ───────────────────────────────────────

    /// <summary>
    /// Builds a full Mist source containing a generic class with <c>where T : default</c>
    /// and an instantiation site using the given type argument.
    /// </summary>
    private static string BuildSource(string typeArgName, string typeArgDeclaration)
    {
        return $@"namespace TestProgram;

{typeArgDeclaration}

public class Container<T> where T : default
{{
    public T Value;
}}

public class Consumer
{{
    public static Container<{typeArgName}> Make()
    {{
        return new Container<{typeArgName}>();
    }}
}}
";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7a: Defaultable type argument satisfies the default constraint.
    //
    // **Validates: Requirements 5.6, 9.2**
    //
    // For any generic instantiation G<A> where G declares `where T : default`,
    // if A is a defaultable type the instantiation SHALL produce no
    // MIST_DEFAULT_CONSTRAINT_NOT_SATISFIED diagnostic.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 7a: Instantiating a generic with <c>where T : default</c> using a
    /// defaultable type argument produces no constraint-violation diagnostic.
    ///
    /// **Validates: Requirements 5.6**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(DefaultConstraintSatisfactionPropertyTests) },
        MaxTest = 100)]
    public Property DefaultableTypeArg_SatisfiesConstraint_NoDiagnostic(
        DefaultableTypeArgScenario scenario)
    {
        var source = BuildSource(scenario.TypeArgName, scenario.TypeArgDeclaration);
        var diagnostics = CompileAndGetDiagnostics(source);

        var hasConstraintViolation = HasDiagnostic(diagnostics, "MIST_DEFAULT_CONSTRAINT_NOT_SATISFIED");

        return (!hasConstraintViolation)
            .ToProperty()
            .Label($"Expected no MIST_DEFAULT_CONSTRAINT_NOT_SATISFIED for defaultable type '{scenario.TypeArgName}'. " +
                   $"Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7b: Non-defaultable type argument violates the default constraint.
    //
    // **Validates: Requirements 5.6, 9.2**
    //
    // For any generic instantiation G<A> where G declares `where T : default`,
    // if A is non-defaultable the compiler SHALL emit
    // MIST_DEFAULT_CONSTRAINT_NOT_SATISFIED.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 7b: Instantiating a generic with <c>where T : default</c> using a
    /// non-defaultable type argument emits <c>MIST_DEFAULT_CONSTRAINT_NOT_SATISFIED</c>.
    ///
    /// **Validates: Requirements 5.6, 9.2**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(DefaultConstraintSatisfactionPropertyTests) },
        MaxTest = 100)]
    public Property NonDefaultableTypeArg_ViolatesConstraint_EmitsDiagnostic(
        NonDefaultableTypeArgScenario scenario)
    {
        var source = BuildSource(scenario.TypeArgName, scenario.TypeArgDeclaration);
        var diagnostics = CompileAndGetDiagnostics(source);

        var hasConstraintViolation = HasDiagnostic(diagnostics, "MIST_DEFAULT_CONSTRAINT_NOT_SATISFIED");

        return hasConstraintViolation
            .ToProperty()
            .Label($"Expected MIST_DEFAULT_CONSTRAINT_NOT_SATISFIED for non-defaultable type '{scenario.TypeArgName}' ({scenario.Kind}). " +
                   $"Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}