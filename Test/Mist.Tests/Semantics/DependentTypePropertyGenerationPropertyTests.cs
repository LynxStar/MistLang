// Feature: dependent-types, Property 8: Dependent Property Generation

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying that for types with N dependent type parameters,
/// the semantic model builder generates exactly N implicit public static get-only
/// properties with correct names and types.
///
/// **Validates: Requirements 7.1, 7.2, 7.5**
///
/// For any type with N dependent type parameters, the SemanticModelBuilder SHALL
/// generate exactly N implicit public static get-only properties, each with the
/// correct name (matching the parameter name) and the correct type (matching the
/// Dependent_Type_Annotation).
/// </summary>
public class DependentTypePropertyGenerationPropertyTests
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

    // ─── Allowed primitive types for dependent parameters ────────────────

    private static readonly (PrimitiveTypeNode Node, string Keyword)[] AllowedPrimitiveTypes =
    [
        (new IntTypePrimitiveTypeNode(), "int"),
        (new UintTypePrimitiveTypeNode(), "uint"),
        (new LongTypePrimitiveTypeNode(), "long"),
        (new UlongTypePrimitiveTypeNode(), "ulong"),
        (new ShortTypePrimitiveTypeNode(), "short"),
        (new UshortTypePrimitiveTypeNode(), "ushort"),
        (new ByteTypePrimitiveTypeNode(), "byte"),
        (new SbyteTypePrimitiveTypeNode(), "sbyte"),
        (new BoolTypePrimitiveTypeNode(), "bool"),
        (new CharTypePrimitiveTypeNode(), "char"),
    ];

    private static readonly string[] TypeNames = ["Container", "Buffer", "Matrix", "Holder", "Wrapper"];
    private static readonly string[] ParamNames = ["Size", "Count", "Capacity", "Length", "Width", "Height", "Depth", "Rows", "Cols", "Rank"];

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a single dependent type parameter with its name and value type.
    /// </summary>
#pragma warning disable CA1034
    public record DependentParam(string Name, int TypeIndex)
    {
        public PrimitiveTypeNode TypeNode => AllowedPrimitiveTypes[TypeIndex].Node;
        public string TypeKeyword => AllowedPrimitiveTypes[TypeIndex].Keyword;
        public override string ToString()
        {
            return $"{Name}:{TypeKeyword}";
        }
    }

    /// <summary>
    /// Represents a scenario: a type declaration with 1-4 dependent type parameters.
    /// </summary>
    public record PropertyGenerationScenario(string TypeName, IReadOnlyList<DependentParam> Params)
    {
        public override string ToString()
        {
            return $"class {TypeName}<{string.Join(", ", Params.Select(p => p.ToString()))}>";
        }
    }

    private static Gen<PropertyGenerationScenario> GenPropertyGenerationScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from paramCount in Gen.Choose(1, 4)
               from params_ in GenDistinctParams(paramCount)
               select new PropertyGenerationScenario(typeName, params_);
    }

    private static Gen<IReadOnlyList<DependentParam>> GenDistinctParams(int count)
    {
        // Use distinct parameter names from the ParamNames array
        return from typeIndices in Gen.ArrayOf(Gen.Choose(0, AllowedPrimitiveTypes.Length - 1), count)
               let names = ParamNames.Take(count).ToArray()
               select (IReadOnlyList<DependentParam>)names.Zip(typeIndices, (n, ti) => new DependentParam(n, ti)).ToList();
    }

    public static Arbitrary<PropertyGenerationScenario> ArbitraryPropertyGenerationScenario()
    {
        return GenPropertyGenerationScenario().ToArbitrary();
    }

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// Property 8: For any type with N dependent type parameters, the semantic model
    /// builder SHALL generate exactly N implicit public static get-only properties,
    /// each with the correct name (matching the parameter name) and the correct type
    /// (matching the Dependent_Type_Annotation).
    ///
    /// **Validates: Requirements 7.1, 7.2, 7.5**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(DependentTypePropertyGenerationPropertyTests) })]
    public bool DependentParams_GenerateCorrectImplicitProperties(PropertyGenerationScenario scenario)
    {
        // Build AST: class TypeName<Param1:Type1, Param2:Type2, ...> { }
        var firstParam = new DependentTypeParameterTypeParameterNode
        {
            Name = MakeToken(scenario.Params[0].Name),
            ValueType = scenario.Params[0].TypeNode,
        };

        var additionalParams = scenario.Params.Skip(1)
            .Select(p => (TypeParameterNode)new DependentTypeParameterTypeParameterNode
            {
                Name = MakeToken(p.Name),
                ValueType = p.TypeNode,
            })
            .ToImmutableArray();

        var typeParamList = new TypeParameterListNode
        {
            TypeParameter = firstParam,
            TypeParameters = additionalParams,
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken(scenario.TypeName),
            TypeParams = typeParamList,
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // Find the class symbol in the model
        var classSymbol = model.AllSymbols.FirstOrDefault(s => s.Name == scenario.TypeName && s.Kind == SymbolKind.Class);
        if(classSymbol is null)
        {
            return false;
        }

        if(classSymbol.DeclaredType is not ClassType classType)
        {
            return false;
        }

        // Get all property members from the type's member scope
        var propertyMembers = classType.MemberScope.Members
            .Where(m => m.Kind == SymbolKind.Property)
            .ToList();

        // Verify exactly N properties are generated (one per dependent parameter)
        if(propertyMembers.Count != scenario.Params.Count)
        {
            return false;
        }

        // Verify each dependent parameter has a corresponding property with correct name and type
        foreach(var param in scenario.Params)
        {
            var prop = propertyMembers.FirstOrDefault(m => m.Name == param.Name);
            if(prop is null)
            {
                return false;
            }

            // Verify the property has the correct value type (matching the annotation)
            if(prop.DeclaredType is null)
            {
                return false;
            }

            if(prop.DeclaredType.DisplayName != param.TypeKeyword)
            {
                return false;
            }

            // Verify the property is static and get-only via PropertyMetadata
            var symbol = prop as Symbol;
            if(symbol?.PropertyMetadata is null)
            {
                return false;
            }

            if(!symbol.PropertyMetadata.HasGet)
            {
                return false;
            }

            if(symbol.PropertyMetadata.HasSet || symbol.PropertyMetadata.HasInit)
            {
                return false;
            }

            if(!symbol.PropertyMetadata.IsStatic)
            {
                return false;
            }
        }

        return true;
    }
}