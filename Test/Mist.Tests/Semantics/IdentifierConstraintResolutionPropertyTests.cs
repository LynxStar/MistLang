// Feature: dependent-type-refinement-constraints, Property 2: Identifier Constraint Resolution Correctness
// **Validates: Requirements 2.1**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for identifier constraint resolution correctness.
///
/// <b>Property 2: Identifier Constraint Resolution Correctness</b>
///
/// For all <c>NamedConstraintDependentTypeParameter</c> nodes where the identifier
/// resolves to a <see cref="RefinementType"/> whose <c>BaseType</c> is in
/// <c>AllowedDependentTypeAnnotations</c>, the resulting
/// <see cref="DependentTypeParameterType"/> has <c>ValueType</c> equal to that base type.
///
/// <b>Validates: Requirements 2.1</b>
/// </summary>
public class IdentifierConstraintResolutionPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();

    /// <summary>
    /// The set of allowed dependent type primitive base types that support
    /// relational refinement predicates. Bool is excluded because relational
    /// patterns are incompatible with bool (MIST_TC_0072), meaning a valid
    /// refinement type with bool as the base cannot be formed using the
    /// supported grammar.
    /// </summary>
    private static readonly string[] AllowedPrimitivesWithRelational =
        ["int", "uint", "long", "ulong", "short", "ushort", "byte", "sbyte", "char"];

    /// <summary>
    /// Lexes, parses, and builds the semantic model from source.
    /// Returns the model and all semantic diagnostics.
    /// </summary>
    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if(parseResult.Root is null)
            return (null!, ImmutableArray<Diagnostic>.Empty);

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        return (model, semBag.ToImmutableArray());
    }

    /// <summary>
    /// Finds a <see cref="DependentTypeParameterType"/> by the declaring type name
    /// and the dependent parameter name.
    /// </summary>
    private static DependentTypeParameterType? FindDependentTypeParameter(
        ISemanticModel model, string typeName, string paramName)
    {
        var typeSymbol = model.AllSymbols.FirstOrDefault(s => s.Name == typeName);
        IReadOnlyList<IType>? typeParams = typeSymbol?.DeclaredType switch
        {
            ClassType ct => ct.TypeParameters,
            StructType st => st.TypeParameters,
            _ => null,
        };

        return typeParams?.OfType<DependentTypeParameterType>()
            .FirstOrDefault(tp => tp.Name == paramName);
    }

    // ─── Scenario type ───────────────────────────────────────────────────

    /// <summary>
    /// A test scenario that generates a refinement type with an allowed primitive
    /// base type and a class using that refinement as a dependent type constraint.
    /// </summary>
#pragma warning disable CA1034
    public record ConstraintResolutionScenario(
        string BaseType,
        string RefinementName,
        string ClassName,
        string ParamName,
        bool IsStruct)
    {
        /// <summary>
        /// Generates the Mist source code for this scenario.
        /// Uses a relational predicate appropriate for the base type.
        /// </summary>
        public string ToSource()
        {
            // char uses a character literal comparand; numeric types use integer 0.
            var predicate = BaseType == "char" ? ">= 'a'" : ">= 0";
            var refinementDecl = $"type {RefinementName} = {BaseType} {predicate};";

            var typeKind = IsStruct ? "struct" : "class";
            var typeDecl = $"{typeKind} {ClassName}<{ParamName}:{RefinementName}> {{ }}";

            return $"{refinementDecl}\n{typeDecl}";
        }

        public override string ToString() =>
            $"{(IsStruct ? "struct" : "class")} {ClassName}<{ParamName}:{RefinementName}> " +
            $"where {RefinementName} = {BaseType} >= 0";
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] RefinementNames =
        ["PositiveInt", "ValidSize", "SafeLength", "NonNegative", "Bounded", "CheckedVal"];

    private static readonly string[] ClassNames =
        ["Container", "Buffer", "Storage", "Holder", "Wrapper", "Sized"];

    private static readonly string[] ParamNames =
        ["Size", "Length", "Count", "Capacity", "N", "Limit"];

    private static Gen<ConstraintResolutionScenario> GenScenario()
    {
        return from baseType in Gen.Elements(AllowedPrimitivesWithRelational)
               from refinementName in Gen.Elements(RefinementNames)
               from className in Gen.Elements(ClassNames)
               from paramName in Gen.Elements(ParamNames)
               from isStruct in Gen.Elements(true, false)
               select new ConstraintResolutionScenario(
                   baseType, refinementName, className, paramName, isStruct);
    }

    public static Arbitrary<ConstraintResolutionScenario> ArbitraryScenario() =>
        GenScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Identifier Constraint Resolution Correctness
    //
    // For all NamedConstraintDependentTypeParameter nodes where the identifier
    // resolves to a RefinementType whose BaseType is in
    // AllowedDependentTypeAnnotations, the resulting DependentTypeParameterType
    // has ValueType equal to that base type.
    //
    // **Validates: Requirements 2.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 200,
        Arbitrary = new[] { typeof(IdentifierConstraintResolutionPropertyTests) })]
    public Property IdentifierConstraint_ResolvesToCorrectBaseType(
        ConstraintResolutionScenario scenario)
    {
        var source = scenario.ToSource();
        var (model, diagnostics) = BuildModel(source);

        if(model is null)
        {
            return false.ToProperty()
                .Label($"Parse failed for source:\n{source}");
        }

        // Filter out diagnostics unrelated to constraint resolution.
        // MIST_TC_0072 is for incompatible refinement predicates (e.g., bool >= 0)
        // which shouldn't block resolution.
        var resolutionErrors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value != "MIST_TC_0072")
            .ToList();

        // Find the dependent type parameter on the declared type.
        var depParam = FindDependentTypeParameter(model, scenario.ClassName, scenario.ParamName);

        if(depParam is null)
        {
            return false.ToProperty()
                .Label($"DependentTypeParameterType not found for {scenario.ClassName}.{scenario.ParamName}\n"
                     + $"Source:\n{source}\n"
                     + $"Diagnostics: [{string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }

        // The ValueType should match the base primitive type.
        var valueTypeDisplayName = depParam.ValueType.DisplayName;
        var matchesBaseType = valueTypeDisplayName == scenario.BaseType;

        // ValueType should not be ErrorType (indicates resolution failure).
        var isNotError = depParam.ValueType is not ErrorType;

        var allPass = matchesBaseType && isNotError;

        return allPass.ToProperty()
            .Label($"scenario: {scenario}\n"
                 + $"expected ValueType='{scenario.BaseType}', got='{valueTypeDisplayName}'\n"
                 + $"isNotError={isNotError}\n"
                 + $"errors: [{string.Join("; ", resolutionErrors.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n"
                 + $"source:\n{source}");
    }

    // ─── Deterministic validation with all allowed primitives ────────────

    /// <summary>
    /// Validates that each allowed primitive base type with a compatible refinement
    /// predicate is correctly resolved when used as a refinement type constraint
    /// on a dependent parameter. Bool is excluded because it cannot form valid
    /// refinement types (relational patterns are incompatible).
    /// </summary>
    [Theory]
    [InlineData("int")]
    [InlineData("uint")]
    [InlineData("long")]
    [InlineData("ulong")]
    [InlineData("short")]
    [InlineData("ushort")]
    [InlineData("byte")]
    [InlineData("sbyte")]
    [InlineData("char")]
    public void AllAllowedPrimitives_ResolveCorrectly(string baseType)
    {
        var predicate = baseType == "char" ? ">= 'a'" : ">= 0";
        var source = $"type MyConstraint = {baseType} {predicate};\nclass Foo<N:MyConstraint> {{ }}";

        var (model, diagnostics) = BuildModel(source);
        Assert.NotNull(model);

        var depParam = FindDependentTypeParameter(model, "Foo", "N");
        Assert.NotNull(depParam);
        Assert.IsNotType<ErrorType>(depParam!.ValueType);
        Assert.Equal(baseType, depParam.ValueType.DisplayName);
    }
}