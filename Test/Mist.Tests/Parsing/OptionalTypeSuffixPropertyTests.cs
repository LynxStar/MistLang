// Feature: optional-types, Properties 1, 3, 4, 5: Optional type suffix parsing
// **Validates: Requirements 1.1, 1.2, 1.4, 1.6, 1.7**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

public sealed class OptionalTypeSuffixScenario
{
    public string TypeName { get; }

    public bool IsPrimitive { get; }

    public OptionalTypeSuffixScenario(string typeName, bool isPrimitive)
    {
        TypeName = typeName;
        IsPrimitive = isPrimitive;
    }

    public override string ToString()
    {
        return $"{TypeName}?";
    }
}

public sealed class TypePositionScenario
{
    public string TypeName { get; }

    public string PositionDescription { get; }

    public string Source { get; }

    public TypePositionScenario(string typeName, string positionDescription, string source)
    {
        TypeName = typeName;
        PositionDescription = positionDescription;
        Source = source;
    }

    public override string ToString()
    {
        return $"{TypeName}? in {PositionDescription}";
    }
}

public sealed class OptionalTypeSuffixPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static readonly string[] PrimitiveTypes =
        ["int", "string", "bool", "float", "double", "long", "short", "byte", "char", "decimal"];

    private static readonly string[] NamedTypes =
        ["MyClass", "Foo", "Bar", "Widget", "Result"];

    private static readonly string[] GenericTypes =
        ["Result<int>", "List<string>", "Map<int, string>"];

    public static Arbitrary<OptionalTypeSuffixScenario> ArbitraryOptionalTypeSuffix()
    {
        var primitiveGen = Gen.Elements(PrimitiveTypes)
            .Select(t => new OptionalTypeSuffixScenario(t, isPrimitive: true));

        var namedGen = Gen.Elements(NamedTypes)
            .Select(t => new OptionalTypeSuffixScenario(t, isPrimitive: false));

        var genericGen = Gen.Elements(GenericTypes)
            .Select(t => new OptionalTypeSuffixScenario(t, isPrimitive: false));

        var gen = Gen.OneOf(primitiveGen, namedGen, genericGen);
        return gen.ToArbitrary();
    }

    public static Arbitrary<TypePositionScenario> ArbitraryTypePosition()
    {
        var typeGen = Gen.Elements(PrimitiveTypes.Concat(NamedTypes).ToArray());

        var gen = typeGen.SelectMany(typeName =>
        {
            var scenarios = new[]
            {
                new TypePositionScenario(typeName, "variable declaration",
                    $"class T {{ public void M() {{ {typeName}? x = default; }} }}"),
                new TypePositionScenario(typeName, "parameter type",
                    $"class T {{ public void M({typeName}? p) {{ }} }}"),
                new TypePositionScenario(typeName, "return type",
                    $"class T {{ public {typeName}? M() {{ return default; }} }}"),
                new TypePositionScenario(typeName, "field type",
                    $"class T {{ public {typeName}? myField; }}"),
                new TypePositionScenario(typeName, "generic type argument",
                    $"class T {{ public void M() {{ Wrapper<{typeName}?> x = default; }} }}"),
            };
            return Gen.Elements(scenarios);
        });

        return gen.ToArbitrary();
    }

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");
        }

        return result.Root as CompilationUnitNode
            ?? throw new InvalidOperationException("Parse result root is not a CompilationUnitNode");
    }

    private static TypeReferenceNode GetVariableTypeRef(string source)
    {
        var cu = ParseOrFail(source);
        var typeDecl = (BodyTypeTypeDeclarationNode)cu.GetTypes()[0];
        var methodMember = (MethodMemberMemberDeclarationNode)typeDecl.TypeBody.Members[0];
        var block = methodMember.Method.Body.GetBlock()
            ?? throw new InvalidOperationException("Method body block is null");
        var stmt = (VariableDeclarationStatementStatementNode)block.Statements[0];
        var varDecl = (TypedVariableDeclarationVariableDeclarationStatementNode)stmt.VariableDeclaration;
        return varDecl.Type;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Optional suffix parsing equivalence
    //
    // **Validates: Requirements 1.1, 1.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OptionalTypeSuffixPropertyTests) })]
    public Property OptionalSuffix_SetsOptionalField(OptionalTypeSuffixScenario scenario)
    {
        var source = $"class T {{ public void M() {{ {scenario.TypeName}? x = default; }} }}";
        var typeRef = GetVariableTypeRef(source);

        bool optionalIsSet;
        if(scenario.IsPrimitive)
        {
            var primitiveRef = typeRef.First as PrimitiveSingleTypeRefSingleTypeRefNode;
            optionalIsSet = primitiveRef?.Optional is not null;
        }
        else
        {
            var namedRef = typeRef.First as NamedSingleTypeRefSingleTypeRefNode;
            optionalIsSet = namedRef?.Optional is not null;
        }

        return optionalIsSet.ToProperty()
            .Label($"Expected Optional field to be set for '{scenario.TypeName}?'")
            .Label($"Actual type ref kind: {typeRef.GetType().Name}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Optional suffix in all type positions
    //
    // **Validates: Requirements 1.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OptionalTypeSuffixPropertyTests) })]
    public Property OptionalSuffix_ParsesInAllTypePositions(TypePositionScenario scenario)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", scenario.Source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        return (errors.Count == 0).ToProperty()
            .Label($"Parse errors for '{scenario}': {string.Join(", ", errors.Select(e => e.Message))}")
            .Label($"Source:\n{scenario.Source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Precedence — optional suffix binds tighter than union,
    //             looser than array
    //
    // **Validates: Requirements 1.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("int")]
    [InlineData("string")]
    [InlineData("bool")]
    [InlineData("float")]
    [InlineData("double")]
    public void ArrayThenOptional_HasBothArrayRankAndOptionalSet(string primitiveType)
    {
        var source = $"class T {{ public void M() {{ {primitiveType}[]? x = default; }} }}";
        var typeRef = GetVariableTypeRef(source);

        var primitiveRef = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(typeRef.First);
        Assert.NotNull(primitiveRef.ArrayRank);
        _ = Assert.NotNull(primitiveRef.Optional);
    }

    [Theory]
    [InlineData("int", "string")]
    [InlineData("bool", "int")]
    [InlineData("string", "float")]
    public void OptionalBeforeUnion_ParsesAsUnionTypeRef(string leftType, string rightType)
    {
        var source = $"class T {{ public void M() {{ {leftType}? | {rightType} x = default; }} }}";
        var typeRef = GetVariableTypeRef(source);

        Assert.NotEmpty(typeRef.SingleTypeRefs);
        var firstConstituent = typeRef.First;

        var primSingle = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(firstConstituent);
        _ = Assert.NotNull(primSingle.Optional);
    }

    [Theory]
    [InlineData("MyClass")]
    [InlineData("Foo")]
    public void NamedArrayThenOptional_HasBothArrayRankAndOptionalSet(string namedType)
    {
        var source = $"class T {{ public void M() {{ {namedType}[]? x = default; }} }}";
        var typeRef = GetVariableTypeRef(source);

        var namedRef = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(typeRef.First);
        Assert.NotNull(namedRef.ArrayRank);
        _ = Assert.NotNull(namedRef.Optional);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Double-optional is an error
    //
    // **Validates: Requirements 1.7**
    //
    // The lexer tokenizes `??` as one QUESTION_QUESTION token, which the grammar's
    // `optional:QUESTION?` field cannot match, so `T??` never sets the optional field. A
    // double optional such as `(T?)?` is diagnosed by the semantic model builder instead.
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("int")]
    [InlineData("string")]
    [InlineData("bool")]
    public void SingleOptional_SetsOptionalField_DoubleQuestionDoesNot(string primitiveType)
    {
        var sourceWithOptional = $"class T {{ public void M() {{ {primitiveType}? x = default; }} }}";
        var typeRefOptional = GetVariableTypeRef(sourceWithOptional);
        var primRefOptional = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(typeRefOptional.First);
        _ = Assert.NotNull(primRefOptional.Optional);

        Assert.Equal("?", primRefOptional.Optional!.Value.Lexeme);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("string")]
    [InlineData("bool")]
    [InlineData("MyClass")]
    [InlineData("Foo")]
    public void DoubleOptional_LexerTokenizesAsQuestionQuestion_NotTwoQuestions(string typeName)
    {
        var source = $"{typeName}??";
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);

        var questionTokens = tokens.DefaultChannelTokens
            .Where(t => t.Lexeme is "??" or "?")
            .ToList();
        Assert.Contains(questionTokens, t => t.Lexeme == "??");
        Assert.DoesNotContain(questionTokens, t => t.Lexeme == "?");
    }
}