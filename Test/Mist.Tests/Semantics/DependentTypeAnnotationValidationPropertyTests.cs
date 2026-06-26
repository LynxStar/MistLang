// Feature: dependent-types, Property 2: Disallowed Dependent Type Annotations Produce Diagnostics

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying that disallowed dependent type annotations produce
/// the MIST_DT_0001 diagnostic.
///
/// **Validates: Requirements 4.2, 4.3, 4.4, 4.5, 4.6, 15.4**
///
/// For any type annotation that is not in the allowed set
/// {int, uint, long, ulong, short, ushort, byte, sbyte, bool, char},
/// using it as a Dependent_Type_Annotation SHALL cause the compiler to emit
/// diagnostic MIST_DT_0001.
/// </summary>
public class DependentTypeAnnotationValidationPropertyTests
{
    private readonly SemanticModelBuilder builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token MakeToken(string lexeme, int start = 0)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast,
        string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    // ─── Disallowed primitive type nodes ─────────────────────────────────

    /// <summary>
    /// Primitive type nodes that are NOT in the allowed set for dependent type annotations.
    /// These are: string, float, double, decimal.
    /// </summary>
    private static readonly PrimitiveTypeNode[] DisallowedPrimitiveTypeNodes =
    [
        new StringTypePrimitiveTypeNode(),
        new FloatTypePrimitiveTypeNode(),
        new DoubleTypePrimitiveTypeNode(),
        new DecimalTypePrimitiveTypeNode(),
    ];

    private static readonly string[] ClassNames = ["Container", "Buffer", "Matrix", "Holder", "Wrapper"];
    private static readonly string[] ParamNames = ["Size", "Count", "Capacity", "Length", "Width"];

    // ─── Test data model ─────────────────────────────────────────────────

#pragma warning disable CA1034
    public record DisallowedAnnotationScenario(
        string ClassName,
        string ParamName,
        PrimitiveTypeNode DisallowedType,
        string ExpectedTypeKeyword)
    {
        public override string ToString()
        {
            return $"class {ClassName}<{ParamName}:{ExpectedTypeKeyword}>";
        }
    }
#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<DisallowedAnnotationScenario> GenDisallowedAnnotationScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from paramName in Gen.Elements(ParamNames)
               from typeIndex in Gen.Choose(0, DisallowedPrimitiveTypeNodes.Length - 1)
               let disallowedType = DisallowedPrimitiveTypeNodes[typeIndex]
               let keyword = disallowedType switch
               {
                   StringTypePrimitiveTypeNode => "string",
                   FloatTypePrimitiveTypeNode => "float",
                   DoubleTypePrimitiveTypeNode => "double",
                   DecimalTypePrimitiveTypeNode => "decimal",
                   _ => "unknown",
               }
               select new DisallowedAnnotationScenario(className, paramName, disallowedType, keyword);
    }

    public static Arbitrary<DisallowedAnnotationScenario> ArbitraryDisallowedAnnotationScenario()
    {
        return GenDisallowedAnnotationScenario().ToArbitrary();
    }

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// Property 2: For any type annotation that is not in the allowed set
    /// {int, uint, long, ulong, short, ushort, byte, sbyte, bool, char},
    /// using it as a Dependent_Type_Annotation SHALL cause the compiler to emit
    /// diagnostic MIST_DT_0001.
    ///
    /// **Validates: Requirements 4.2, 4.3, 4.4, 4.5, 4.6, 15.4**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(DependentTypeAnnotationValidationPropertyTests) })]
    public bool DisallowedAnnotation_EmitsMistDt0001(DisallowedAnnotationScenario scenario)
    {
        // Build AST: class ClassName<ParamName:DisallowedType> { }
        var depParam = new DependentTypeParameterTypeParameterNode
        {
            Name = MakeToken(scenario.ParamName),
            ValueType = scenario.DisallowedType,
        };

        var typeParamList = new TypeParameterListNode
        {
            TypeParameter = depParam,
            TypeParameters = ImmutableArray<TypeParameterNode>.Empty,
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(scenario.ClassName),
            TypeParams = typeParamList,
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Verify MIST_DT_0001 is emitted
        var dt0001Diagnostics = diagnostics
            .Where(d => d.Id == SemanticModelBuilder.DependentTypeDisallowedAnnotation)
            .ToList();

        return dt0001Diagnostics.Count >= 1
            && dt0001Diagnostics.Any(d => d.Message.Contains(scenario.ExpectedTypeKeyword));
    }
}