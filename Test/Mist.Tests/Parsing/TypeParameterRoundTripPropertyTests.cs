// Feature: dependent-type-refinement-constraints, Property 1: TypeParameter Round-Trip (Grammar)

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for TypeParameter round-trip across all three variants.
///
/// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 9.1, 9.2, 9.3**
///
/// For all valid type parameter forms (Name:PrimitiveKeyword, Name:Identifier, Name),
/// parsing the pretty-printed form produces an equivalent AST node with the same
/// parameter name and constraint.
/// </summary>
public class TypeParameterRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Token helper ────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    // ─── Test data model ─────────────────────────────────────────────────

#pragma warning disable CA1034
    public record TypeParameterRoundTripTestCase(
        string Description,
        CompilationUnitNode Ast)
    {
        public override string ToString() => Description;
    }
#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // TypeParameter AST node generators — all three variants
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The allowed primitive types for dependent type parameters.
    /// </summary>
    private static readonly PrimitiveTypeNode[] DependentPrimitiveTypeNodes =
    [
        new IntTypePrimitiveTypeNode(),
        new UintTypePrimitiveTypeNode(),
        new LongTypePrimitiveTypeNode(),
        new UlongTypePrimitiveTypeNode(),
        new ShortTypePrimitiveTypeNode(),
        new UshortTypePrimitiveTypeNode(),
        new ByteTypePrimitiveTypeNode(),
        new SbyteTypePrimitiveTypeNode(),
        new BoolTypePrimitiveTypeNode(),
        new CharTypePrimitiveTypeNode(),
    ];

    private static readonly string[] ParamNames =
        ["Size", "Count", "Capacity", "Length", "Width", "Height", "Depth", "Index"];

    private static readonly string[] ConstraintIdentifiers =
        ["ZeroOrPositiveInt", "PositiveInt", "NonNegative", "MyConstraint", "BoundedSize", "ValidIndex"];

    private static readonly string[] RegularParamNames = ["T", "U", "V", "W", "TValue", "TKey"];
    private static readonly string[] ClassNames = ["Foo", "Bar", "Container", "Holder", "Matrix"];

    // ─── Variant 1: Name:PrimitiveKeyword ────────────────────────────────

    /// <summary>Generates a dependent type parameter with a primitive keyword constraint (e.g., Size:int).</summary>
    private static Gen<TypeParameterNode> GenDependentPrimitiveTypeParameter()
    {
        return from name in Gen.Elements(ParamNames)
               from valueType in Gen.Elements(DependentPrimitiveTypeNodes)
               select (TypeParameterNode)new DependentTypeParameterTypeParameterNode
               {
                   Name = Tok(name),
                   ValueType = valueType,
               };
    }

    // ─── Variant 2: Name:Identifier ─────────────────────────────────────

    /// <summary>Generates a dependent type parameter with an identifier constraint (e.g., Length:ZeroOrPositiveInt).</summary>
    private static Gen<TypeParameterNode> GenNamedConstraintTypeParameter()
    {
        return from name in Gen.Elements(ParamNames)
               from constraintName in Gen.Elements(ConstraintIdentifiers)
               select (TypeParameterNode)new NamedConstraintDependentTypeParameterTypeParameterNode
               {
                   Name = Tok(name),
                   ConstraintName = Tok(constraintName),
               };
    }

    // ─── Variant 3: Name (no constraint) ────────────────────────────────

    /// <summary>Generates a regular type parameter with no constraint (e.g., T).</summary>
    private static Gen<TypeParameterNode> GenRegularTypeParameter()
    {
        return Gen.Elements(RegularParamNames)
            .Select(name => (TypeParameterNode)new RegularTypeParameterTypeParameterNode
            {
                Name = Tok(name),
            });
    }

    // ─── Any single TypeParameter ────────────────────────────────────────

    private static Gen<TypeParameterNode> GenAnyTypeParameter()
    {
        return Gen.OneOf(
            GenDependentPrimitiveTypeParameter(),
            GenNamedConstraintTypeParameter(),
            GenRegularTypeParameter());
    }

    // ─── TypeParameterList generators ────────────────────────────────────

    /// <summary>
    /// Generates a TypeParameterListNode with 1–3 parameters, mixing all three variants.
    /// </summary>
    private static Gen<TypeParameterListNode> GenTypeParameterList()
    {
        return Gen.Choose(1, 3).SelectMany(count =>
        {
            return Gen.ArrayOf(GenAnyTypeParameter(), count)
                .Select(parameters => new TypeParameterListNode
                {
                    TypeParameter = parameters[0],
                    TypeParameters = parameters.Skip(1).ToImmutableArray(),
                });
        });
    }

    /// <summary>
    /// Generates a TypeParameterListNode with exactly one parameter of each variant.
    /// Ensures all three variants appear in a single list.
    /// </summary>
    private static Gen<TypeParameterListNode> GenAllThreeVariantsList()
    {
        return from dep in GenDependentPrimitiveTypeParameter()
               from named in GenNamedConstraintTypeParameter()
               from regular in GenRegularTypeParameter()
               select new TypeParameterListNode
               {
                   TypeParameter = dep,
                   TypeParameters = ImmutableArray.Create(named, regular),
               };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test case generators — class declarations with type parameters
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Generates a class with a mixed type parameter list.</summary>
    private static Gen<TypeParameterRoundTripTestCase> GenMixedClassTestCase()
    {
        return from className in Gen.Elements(ClassNames)
               from typeParams in GenTypeParameterList()
               select MakeClassTestCase(className, typeParams);
    }

    /// <summary>Generates a class with all three variants present.</summary>
    private static Gen<TypeParameterRoundTripTestCase> GenAllVariantsClassTestCase()
    {
        return from className in Gen.Elements(ClassNames)
               from typeParams in GenAllThreeVariantsList()
               select MakeClassTestCase(className, typeParams);
    }

    private static TypeParameterRoundTripTestCase MakeClassTestCase(
        string className,
        TypeParameterListNode typeParams)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = Tok(className),
            TypeParams = typeParams,
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var cu = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        return new TypeParameterRoundTripTestCase(
            $"class {className}<{DescribeTypeParams(typeParams)}>", cu);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Combined test case generator
    // ═══════════════════════════════════════════════════════════════════════

    private static Gen<TypeParameterRoundTripTestCase> GenTestCase()
    {
        return Gen.OneOf(
            GenMixedClassTestCase(),
            GenAllVariantsClassTestCase());
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<TypeParameterRoundTripTestCase> ArbitraryTypeParameterRoundTripTestCase() =>
        GenTestCase().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: TypeParameter Round-Trip (Grammar)
    //
    // **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 9.1, 9.2, 9.3**
    //
    // For all valid type parameter forms (Name:PrimitiveKeyword, Name:Identifier,
    // Name), pretty-printing → parsing → pretty-printing SHALL produce identical
    // output, and the reparsed AST SHALL contain structurally equivalent
    // TypeParameter nodes.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TypeParameterRoundTripPropertyTests) })]
    public Property RoundTrip_TypeParameterPreservesAllThreeVariants(TypeParameterRoundTripTestCase testCase)
    {
        // Step 1: Pretty-print the generated AST to source text
        var printed1 = MistPrettyPrinter.Print(testCase.Ast);
        if (string.IsNullOrWhiteSpace(printed1))
            return false.ToProperty().Label("printed1 was empty");

        // Step 2: Parse the printed source text back to an AST
        var sourceText = new Mist.Common.Text.SourceText("test.mist", printed1);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
            return false.ToProperty()
                .Label($"Parse errors: {string.Join("; ", errors.Select(e => e.Message))}\nSource:\n{printed1}");

        var reparsed = result.Root as CompilationUnitNode;
        if (reparsed is null)
            return false.ToProperty().Label($"Reparsed root was null.\nSource:\n{printed1}");

        // Step 3: Pretty-print the reparsed AST
        var printed2 = MistPrettyPrinter.Print(reparsed, printed1);

        // Step 4: Compare the two printed outputs — they must be identical
        if (printed1 != printed2)
            return false.ToProperty()
                .Label($"Pretty-print mismatch.\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");

        // Step 5: Verify structural equivalence of TypeParameter nodes
        var originalParams = ExtractTypeParameters(testCase.Ast);
        var reparsedParams = ExtractTypeParameters(reparsed);

        if (originalParams.Count != reparsedParams.Count)
            return false.ToProperty()
                .Label($"TypeParameter count mismatch: {originalParams.Count} vs {reparsedParams.Count}");

        for (var i = 0; i < originalParams.Count; i++)
        {
            var orig = originalParams[i];
            var repr = reparsedParams[i];

            if (orig.GetType() != repr.GetType())
                return false.ToProperty()
                    .Label($"TypeParameter[{i}] type mismatch: {orig.GetType().Name} vs {repr.GetType().Name}\nSource:\n{printed1}");

            if (orig.GetName().Lexeme != repr.GetName().Lexeme)
                return false.ToProperty()
                    .Label($"TypeParameter[{i}] name mismatch: '{orig.GetName().Lexeme}' vs '{repr.GetName().Lexeme}'");

            // Verify constraint equivalence for dependent variants
            var constraintCheck = CheckConstraintEquivalence(orig, repr);
            if (constraintCheck is not null)
                return false.ToProperty().Label(constraintCheck);
        }

        return true.ToProperty();
    }

    // ─── Helper: extract TypeParameter nodes from a CompilationUnit ──────

    private static List<TypeParameterNode> ExtractTypeParameters(CompilationUnitNode cu)
    {
        var result = new List<TypeParameterNode>();

        foreach (var item in cu.Items)
        {
            if (item is not TypeItemTopLevelItemNode typeItem) continue;

            TypeParameterListNode? typeParams = typeItem.TypeDeclaration switch
            {
                ClassTypeTypeDeclarationNode classType => classType.ClassDeclaration.TypeParams,
                StructTypeTypeDeclarationNode structType => structType.StructDeclaration.TypeParams,
                InterfaceTypeTypeDeclarationNode ifaceType => ifaceType.InterfaceDeclaration.TypeParams,
                _ => null,
            };

            if (typeParams is null) continue;

            result.Add(typeParams.TypeParameter);
            result.AddRange(typeParams.TypeParameters);
        }

        return result;
    }

    /// <summary>
    /// Checks that the constraint portion of two TypeParameterNodes is equivalent.
    /// Returns null if equivalent, or an error message string if not.
    /// </summary>
    private static string? CheckConstraintEquivalence(TypeParameterNode orig, TypeParameterNode repr)
    {
        switch (orig)
        {
            case DependentTypeParameterTypeParameterNode depOrig:
                var depRepr = (DependentTypeParameterTypeParameterNode)repr;
                if (depOrig.ValueType.GetType() != depRepr.ValueType.GetType())
                    return $"DependentTypeParameter constraint type mismatch: {depOrig.ValueType.GetType().Name} vs {depRepr.ValueType.GetType().Name}";
                break;

            case NamedConstraintDependentTypeParameterTypeParameterNode namedOrig:
                var namedRepr = (NamedConstraintDependentTypeParameterTypeParameterNode)repr;
                if (namedOrig.ConstraintName.Lexeme != namedRepr.ConstraintName.Lexeme)
                    return $"NamedConstraintDependentTypeParameter constraint mismatch: '{namedOrig.ConstraintName.Lexeme}' vs '{namedRepr.ConstraintName.Lexeme}'";
                break;

            case RegularTypeParameterTypeParameterNode:
                // No constraint to compare
                break;
        }

        return null;
    }

    // ─── Description helper ──────────────────────────────────────────────

    private static string DescribeTypeParams(TypeParameterListNode typeParams)
    {
        var parts = new List<string> { DescribeParam(typeParams.TypeParameter) };
        foreach (var tp in typeParams.TypeParameters)
            parts.Add(DescribeParam(tp));
        return string.Join(", ", parts);
    }

    private static string DescribeParam(TypeParameterNode tp) => tp switch
    {
        DependentTypeParameterTypeParameterNode dep => $"{dep.Name.Lexeme}:primitive",
        NamedConstraintDependentTypeParameterTypeParameterNode named => $"{named.Name.Lexeme}:{named.ConstraintName.Lexeme}",
        RegularTypeParameterTypeParameterNode reg => reg.Name.Lexeme,
        _ => "?",
    };
}
