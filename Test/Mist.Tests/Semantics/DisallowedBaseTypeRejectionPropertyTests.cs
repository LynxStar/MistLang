// Feature: dependent-type-refinement-constraints, Property 3: Disallowed Base Type Rejection

using System.Collections.Immutable;
using System.Linq;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying that named constraints whose refinement base type
/// is NOT in AllowedDependentTypeAnnotations produce the MIST_DT_0001 diagnostic.
///
/// **Validates: Requirements 2.2**
///
/// For all NamedConstraintDependentTypeParameter nodes where the identifier resolves
/// to a RefinementType whose BaseType is NOT in AllowedDependentTypeAnnotations,
/// the SemanticModelBuilder emits MIST_DT_0001.
///
/// Disallowed primitives tested: float, double, decimal.
/// These are comparable (so refinement predicates like >= are valid on them),
/// but they are not in the allowed set for dependent type parameters.
/// </summary>
public class DisallowedBaseTypeRejectionPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private readonly SemanticModelBuilder _builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private ImmutableArray<Diagnostic> BuildAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if(parseResult.Root is null)
        {
            return parseBag.ToImmutableArray();
        }

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        _builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        return semBag.ToImmutableArray();
    }

    // ─── Disallowed base types (comparable but not in AllowedDependentTypeAnnotations) ──

    private static readonly (string Keyword, string Literal)[] DisallowedBaseTypes =
    [
        ("float", "0.0"),
        ("double", "0.0"),
        ("decimal", "0.0"),
    ];

    private static readonly string[] TypeNames = ["Foo", "Container", "Buffer", "Matrix", "Holder"];
    private static readonly string[] ParamNames = ["Size", "Count", "Capacity", "Length", "Width"];
    private static readonly string[] RefinementNames = ["MyRef", "BigVal", "PositiveNum", "Bounded", "Threshold"];
    private static readonly string[] Operators = [">=", "<=", ">", "<"];

    // ─── Test data model ─────────────────────────────────────────────────

#pragma warning disable CA1034
    public record DisallowedBaseTypeScenario(
        string TypeName,
        string ParamName,
        string RefinementName,
        string BaseTypeKeyword,
        string Literal,
        string Operator)
    {
        public string Source =>
            $"type {RefinementName} = {BaseTypeKeyword} {Operator} {Literal};\nclass {TypeName}<{ParamName}:{RefinementName}> {{ }}";

        public override string ToString() =>
            $"type {RefinementName} = {BaseTypeKeyword} {Operator} {Literal}; class {TypeName}<{ParamName}:{RefinementName}>";
    }
#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<DisallowedBaseTypeScenario> GenDisallowedBaseTypeScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from paramName in Gen.Elements(ParamNames)
               from refinementName in Gen.Elements(RefinementNames)
               from baseTypeIndex in Gen.Choose(0, DisallowedBaseTypes.Length - 1)
               from op in Gen.Elements(Operators)
               let baseType = DisallowedBaseTypes[baseTypeIndex]
               select new DisallowedBaseTypeScenario(
                   typeName, paramName, refinementName,
                   baseType.Keyword, baseType.Literal, op);
    }

    public static Arbitrary<DisallowedBaseTypeScenario> ArbitraryDisallowedBaseTypeScenario() =>
        GenDisallowedBaseTypeScenario().ToArbitrary();

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// Property 3: For all NamedConstraintDependentTypeParameter nodes where the
    /// identifier resolves to a RefinementType whose BaseType is NOT in
    /// AllowedDependentTypeAnnotations, the SemanticModelBuilder emits MIST_DT_0001.
    ///
    /// **Validates: Requirements 2.2**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(DisallowedBaseTypeRejectionPropertyTests) })]
    public bool DisallowedBaseType_EmitsMistDt0001(DisallowedBaseTypeScenario scenario)
    {
        var diagnostics = BuildAndGetDiagnostics(scenario.Source);

        // Verify MIST_DT_0001 is emitted
        var dt0001Diagnostics = diagnostics
            .Where(d => d.Id == SemanticModelBuilder.DependentTypeDisallowedAnnotation)
            .ToList();

        return dt0001Diagnostics.Count >= 1
            && dt0001Diagnostics.Any(d => d.Message.Contains(scenario.BaseTypeKeyword));
    }
}