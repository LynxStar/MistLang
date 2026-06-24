// Feature: dependent-types, Property 1: Parse–Print Round Trip

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
/// Property-based tests for dependent type parameter parse–print round trip.
///
/// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6,
/// 3.1, 3.2, 3.3, 3.4, 3.5, 3.6**
///
/// For any valid Mist AST containing dependent type parameter declarations and/or
/// dependent type arguments, pretty-printing the AST to source text and then parsing
/// that source text back and pretty-printing again SHALL produce identical output.
/// </summary>
public class DependentTypeRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Token helper ────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    // ─── Test data model ─────────────────────────────────────────────────

#pragma warning disable CA1034
    public record DependentTypeRoundTripTestCase(
        string Description,
        CompilationUnitNode Ast)
    {
        public override string ToString() => Description;
    }
#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // Dependent type AST node generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The allowed primitive types for dependent type parameters.
    /// Excludes float, double, decimal, and string.
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

    private static readonly string[] ParamNames = ["Size", "Count", "Capacity", "Length", "Width", "Height"];
    private static readonly string[] TypeParamNames = ["T", "U", "V", "W"];
    private static readonly string[] ClassNames = ["FixedBuffer", "Matrix", "Vector", "Container", "Holder"];
    private static readonly string[] StructNames = ["ValueBox", "Cell", "Entry", "Slot"];
    private static readonly string[] AbstractNames = ["ISized", "ICapacity", "IBounded", "IFixed"];
    private static readonly string[] FieldNames = ["data", "items", "buffer", "storage"];

    // ─── Dependent PrimitiveType generator ───────────────────────────────

    private static Gen<PrimitiveTypeNode> GenDependentPrimitiveType() =>
        Gen.Elements(DependentPrimitiveTypeNodes);

    // ─── TypeParameter generators ────────────────────────────────────────

    /// <summary>Generates a dependent type parameter (e.g., Size:int).</summary>
    private static Gen<TypeParameterNode> GenDependentTypeParameter()
    {
        return from name in Gen.Elements(ParamNames)
               from valueType in GenDependentPrimitiveType()
               select (TypeParameterNode)new DependentTypeParameterTypeParameterNode
               {
                   Name = Tok(name),
                   ValueType = valueType,
               };
    }

    /// <summary>Generates a regular type parameter (e.g., T).</summary>
    private static Gen<TypeParameterNode> GenRegularTypeParameter()
    {
        return Gen.Elements(TypeParamNames)
            .Select(name => (TypeParameterNode)new RegularTypeParameterTypeParameterNode
            {
                Name = Tok(name),
            });
    }

    /// <summary>
    /// Generates a TypeParameterListNode with at least one dependent type parameter.
    /// May include a mix of regular and dependent parameters.
    /// </summary>
    private static Gen<TypeParameterListNode> GenDependentTypeParameterList()
    {
        // Strategy: generate 1-3 parameters, ensuring at least one is dependent
        return Gen.Choose(1, 3).SelectMany(count =>
        {
            if (count == 1)
            {
                // Single dependent parameter
                return GenDependentTypeParameter().Select(dep =>
                    new TypeParameterListNode
                    {
                        TypeParameter = dep,
                        TypeParameters = ImmutableArray<TypeParameterNode>.Empty,
                    });
            }

            // Multiple parameters: first is dependent, rest are mixed
            var restGen = Gen.ArrayOf(
                Gen.OneOf(GenDependentTypeParameter(), GenRegularTypeParameter()),
                count - 1);

            return from first in GenDependentTypeParameter()
                   from rest in restGen
                   select new TypeParameterListNode
                   {
                       TypeParameter = first,
                       TypeParameters = rest.ToImmutableArray(),
                   };
        });
    }

    /// <summary>
    /// Generates a mixed TypeParameterListNode where the first parameter is regular
    /// and subsequent ones include at least one dependent.
    /// </summary>
    private static Gen<TypeParameterListNode> GenMixedTypeParameterList()
    {
        return from regular in GenRegularTypeParameter()
               from dep in GenDependentTypeParameter()
               from hasThird in Gen.Elements(true, false)
               from third in hasThird
                   ? Gen.OneOf(GenDependentTypeParameter(), GenRegularTypeParameter())
                       .Select(p => (TypeParameterNode?)p)
                   : Gen.Constant((TypeParameterNode?)null)
               select new TypeParameterListNode
               {
                   TypeParameter = regular,
                   TypeParameters = third is not null
                       ? ImmutableArray.Create(dep, third)
                       : ImmutableArray.Create(dep),
               };
    }

    // ─── TypeArgument generators ─────────────────────────────────────────

    /// <summary>Generates an integer literal type argument (e.g., 5, 42, 100).</summary>
    private static Gen<TypeArgumentNode> GenIntegerLiteralTypeArgument()
    {
        return Gen.Elements("1", "5", "10", "42", "100", "255", "1024")
            .Select(v => (TypeArgumentNode)new IntegerLiteralTypeArgumentTypeArgumentNode
            {
                Literal = Tok(v),
            });
    }

    /// <summary>Generates a negated integer type argument (e.g., -1, -100).</summary>
    private static Gen<TypeArgumentNode> GenNegatedIntegerTypeArgument()
    {
        return Gen.Elements("1", "5", "10", "42", "100")
            .Select(v => (TypeArgumentNode)new NegatedIntegerTypeArgumentTypeArgumentNode
            {
                Literal = Tok(v),
            });
    }

    /// <summary>Generates a hex literal type argument (e.g., 0xFF, 0x1A).</summary>
    private static Gen<TypeArgumentNode> GenHexLiteralTypeArgument()
    {
        return Gen.Elements("0xFF", "0x1A", "0x00", "0xDEAD")
            .Select(v => (TypeArgumentNode)new HexLiteralTypeArgumentTypeArgumentNode
            {
                Literal = Tok(v),
            });
    }

    /// <summary>Generates a binary literal type argument (e.g., 0b1010, 0b0001).</summary>
    private static Gen<TypeArgumentNode> GenBinaryLiteralTypeArgument()
    {
        return Gen.Elements("0b1010", "0b0001", "0b1111", "0b0000")
            .Select(v => (TypeArgumentNode)new BinaryLiteralTypeArgumentTypeArgumentNode
            {
                Literal = Tok(v),
            });
    }

    /// <summary>Generates a boolean literal type argument (true or false).</summary>
    private static Gen<TypeArgumentNode> GenBooleanLiteralTypeArgument()
    {
        return Gen.Elements(
            (TypeArgumentNode)new TrueLiteralTypeArgumentTypeArgumentNode(),
            (TypeArgumentNode)new FalseLiteralTypeArgumentTypeArgumentNode());
    }

    /// <summary>Generates a char literal type argument (e.g., 'x', 'A').</summary>
    private static Gen<TypeArgumentNode> GenCharLiteralTypeArgument()
    {
        return Gen.Elements("'x'", "'A'", "'z'", "'0'", "'\\n'")
            .Select(v => (TypeArgumentNode)new CharLiteralTypeArgumentTypeArgumentNode
            {
                Literal = Tok(v),
            });
    }

    /// <summary>Generates a type reference type argument (e.g., int, string, MyType).</summary>
    private static Gen<TypeArgumentNode> GenTypeReferenceTypeArgument()
    {
        return MistTypeGenerator.GenPrimitiveTypeRef()
            .Select(typeRef => (TypeArgumentNode)new TypeReferenceTypeArgumentTypeArgumentNode
            {
                Type = typeRef,
            });
    }

    /// <summary>Generates any dependent value type argument (not a type reference).</summary>
    private static Gen<TypeArgumentNode> GenDependentValueTypeArgument()
    {
        return Gen.OneOf(
            GenIntegerLiteralTypeArgument(),
            GenNegatedIntegerTypeArgument(),
            GenHexLiteralTypeArgument(),
            GenBinaryLiteralTypeArgument(),
            GenBooleanLiteralTypeArgument(),
            GenCharLiteralTypeArgument());
    }

    /// <summary>
    /// Generates a TypeArgumentListNode with at least one dependent value argument.
    /// May include a mix of type references and dependent values.
    /// </summary>
    private static Gen<TypeArgumentListNode> GenDependentTypeArgumentList()
    {
        return Gen.Choose(1, 3).SelectMany(count =>
        {
            if (count == 1)
            {
                return GenDependentValueTypeArgument().Select(arg =>
                    new TypeArgumentListNode
                    {
                        TypeArgument = arg,
                        TypeArguments = ImmutableArray<TypeArgumentNode>.Empty,
                    });
            }

            // Multiple arguments: first is a dependent value, rest are mixed
            var restGen = Gen.ArrayOf(
                Gen.OneOf(GenDependentValueTypeArgument(), GenTypeReferenceTypeArgument()),
                count - 1);

            return from first in GenDependentValueTypeArgument()
                   from rest in restGen
                   select new TypeArgumentListNode
                   {
                       TypeArgument = first,
                       TypeArguments = rest.ToImmutableArray(),
                   };
        });
    }

    /// <summary>
    /// Generates a TypeArgumentListNode with a type reference first, then dependent values.
    /// Represents usage like Bar&lt;string, true&gt;.
    /// </summary>
    private static Gen<TypeArgumentListNode> GenMixedTypeArgumentList()
    {
        return from typeArg in GenTypeReferenceTypeArgument()
               from depArg in GenDependentValueTypeArgument()
               from hasThird in Gen.Elements(true, false)
               from third in hasThird
                   ? Gen.OneOf(GenDependentValueTypeArgument(), GenTypeReferenceTypeArgument())
                       .Select(a => (TypeArgumentNode?)a)
                   : Gen.Constant((TypeArgumentNode?)null)
               select new TypeArgumentListNode
               {
                   TypeArgument = typeArg,
                   TypeArguments = third is not null
                       ? ImmutableArray.Create(depArg, third)
                       : ImmutableArray.Create(depArg),
               };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test case generators — class declarations with dependent type params
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a class with dependent type parameters (e.g., class FixedBuffer&lt;Size:int&gt; { }).
    /// </summary>
    private static Gen<DependentTypeRoundTripTestCase> GenDependentClassTestCase()
    {
        return from className in Gen.Elements(ClassNames)
               from typeParams in Gen.OneOf(GenDependentTypeParameterList(), GenMixedTypeParameterList())
               from memberCount in Gen.Choose(0, 1)
               from members in GenFieldMembers(memberCount)
               select MakeDependentClassTestCase(className, typeParams, members);
    }

    private static DependentTypeRoundTripTestCase MakeDependentClassTestCase(
        string className,
        TypeParameterListNode typeParams,
        ImmutableArray<MemberDeclarationNode> members)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = Tok(className),
            TypeParams = typeParams,
            Members = members,
        };

        var cu = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        return new DependentTypeRoundTripTestCase($"class {className}<...dependent...>", cu);
    }

    /// <summary>
    /// Generates a struct with dependent type parameters.
    /// </summary>
    private static Gen<DependentTypeRoundTripTestCase> GenDependentStructTestCase()
    {
        return from structName in Gen.Elements(StructNames)
               from typeParams in Gen.OneOf(GenDependentTypeParameterList(), GenMixedTypeParameterList())
               select MakeDependentStructTestCase(structName, typeParams);
    }

    private static DependentTypeRoundTripTestCase MakeDependentStructTestCase(
        string structName,
        TypeParameterListNode typeParams)
    {
        var structDecl = new StructDeclarationNode
        {
            Name = Tok(structName),
            TypeParams = typeParams,
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var cu = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new StructTypeTypeDeclarationNode { StructDeclaration = structDecl }).AsTopLevelItems(),
        };

        return new DependentTypeRoundTripTestCase($"struct {structName}<...dependent...>", cu);
    }

    /// <summary>
    /// Generates an abstract type with dependent type parameters.
    /// </summary>
    private static Gen<DependentTypeRoundTripTestCase> GenDependentAbstractTestCase()
    {
        return from abstractName in Gen.Elements(AbstractNames)
               from typeParams in Gen.OneOf(GenDependentTypeParameterList(), GenMixedTypeParameterList())
               select MakeDependentAbstractTestCase(abstractName, typeParams);
    }

    private static DependentTypeRoundTripTestCase MakeDependentAbstractTestCase(
        string abstractName,
        TypeParameterListNode typeParams)
    {
        var abstractDecl = new AbstractDeclarationNode
        {
            Name = Tok(abstractName),
            TypeParams = typeParams,
            Members = ImmutableArray<AbstractMemberDeclarationNode>.Empty,
        };

        var cu = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new AbstractTypeTypeDeclarationNode { AbstractDeclaration = abstractDecl }).AsTopLevelItems(),
        };

        return new DependentTypeRoundTripTestCase($"abstract {abstractName}<...dependent...>", cu);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test case generators — usages with dependent type arguments
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a class with a field whose type uses dependent type arguments
    /// (e.g., class Foo { FixedBuffer&lt;5&gt; data; }).
    /// </summary>
    private static Gen<DependentTypeRoundTripTestCase> GenDependentTypeArgumentUsageTestCase()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldTypeName in Gen.Elements("Buffer", "Grid", "Store")
               from typeArgs in Gen.OneOf(GenDependentTypeArgumentList(), GenMixedTypeArgumentList())
               from fieldName in Gen.Elements(FieldNames)
               select MakeDependentTypeArgUsageTestCase(className, fieldTypeName, typeArgs, fieldName);
    }

    private static DependentTypeRoundTripTestCase MakeDependentTypeArgUsageTestCase(
        string className,
        string fieldTypeName,
        TypeArgumentListNode typeArgs,
        string fieldName)
    {
        // Build a NamedTypeRef with type arguments: Buffer<5>
        var fieldType = (TypeReferenceNode)new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = Tok(fieldTypeName),
                IDENTIFIERs = ImmutableArray<Storm.Runtime.Token>.Empty,
            },
            TypeArgs = typeArgs,
        } };

        var field = new FieldDeclarationNode
        {
            Name = Tok(fieldName),
            Type = fieldType,
            Modifiers = ImmutableArray<ModifierNode>.Empty,
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok(className),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = field }),
        };

        var cu = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        return new DependentTypeRoundTripTestCase(
            $"class {className} with field {fieldTypeName}<...dep args...>", cu);
    }

    /// <summary>
    /// Generates a class with dependent type parameters AND a field using dependent type arguments.
    /// This tests both declaration and usage in the same compilation unit.
    /// </summary>
    private static Gen<DependentTypeRoundTripTestCase> GenCombinedDeclAndUsageTestCase()
    {
        return from declClassName in Gen.Elements(ClassNames)
               from typeParams in GenDependentTypeParameterList()
               from usageClassName in Gen.Elements("Client", "Consumer", "User")
               from fieldTypeName in Gen.Constant(declClassName)
               from typeArgs in GenDependentTypeArgumentList()
               from fieldName in Gen.Elements(FieldNames)
               select MakeCombinedTestCase(declClassName, typeParams, usageClassName, fieldTypeName, typeArgs, fieldName);
    }

    private static DependentTypeRoundTripTestCase MakeCombinedTestCase(
        string declClassName,
        TypeParameterListNode typeParams,
        string usageClassName,
        string fieldTypeName,
        TypeArgumentListNode typeArgs,
        string fieldName)
    {
        // Declaration: class FixedBuffer<Size:int> { }
        var declClass = new ClassDeclarationNode
        {
            Name = Tok(declClassName),
            TypeParams = typeParams,
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        // Usage: class Client { FixedBuffer<5> data; }
        var fieldType = (TypeReferenceNode)new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = Tok(fieldTypeName),
                IDENTIFIERs = ImmutableArray<Storm.Runtime.Token>.Empty,
            },
            TypeArgs = typeArgs,
        } };

        var field = new FieldDeclarationNode
        {
            Name = Tok(fieldName),
            Type = fieldType,
            Modifiers = ImmutableArray<ModifierNode>.Empty,
        };

        var usageClass = new ClassDeclarationNode
        {
            Name = Tok(usageClassName),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = field }),
        };

        var cu = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = declClass },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = usageClass }).AsTopLevelItems(),
        };

        return new DependentTypeRoundTripTestCase(
            $"class {declClassName}<...dep...> + class {usageClassName} using it", cu);
    }

    // ─── Field member generator ──────────────────────────────────────────

    private static Gen<ImmutableArray<MemberDeclarationNode>> GenFieldMembers(int count)
    {
        if (count == 0)
            return Gen.Constant(ImmutableArray<MemberDeclarationNode>.Empty);

        var fieldGen = from fieldName in Gen.Elements(FieldNames)
                       from type in MistTypeGenerator.GenPrimitiveTypeRef()
                       select (MemberDeclarationNode)new FieldMemberMemberDeclarationNode
                       {
                           Field = new FieldDeclarationNode
                           {
                               Name = Tok(fieldName),
                               Type = type,
                               Modifiers = ImmutableArray<ModifierNode>.Empty,
                           },
                       };

        return Gen.ArrayOf(fieldGen, count)
            .Select(members => members.ToImmutableArray());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Combined test case generator
    // ═══════════════════════════════════════════════════════════════════════

    private static Gen<DependentTypeRoundTripTestCase> GenTestCase()
    {
        return Gen.OneOf(
            GenDependentClassTestCase(),
            GenDependentStructTestCase(),
            GenDependentAbstractTestCase(),
            GenDependentTypeArgumentUsageTestCase(),
            GenCombinedDeclAndUsageTestCase());
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<DependentTypeRoundTripTestCase> ArbitraryDependentTypeRoundTripTestCase() =>
        GenTestCase().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Parse–Print Round Trip
    //
    // **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 2.1, 2.2, 2.3,
    // 2.4, 2.5, 2.6, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6**
    //
    // For any valid Mist AST containing dependent type parameter declarations
    // and/or dependent type arguments, pretty-printing → parsing → pretty-printing
    // SHALL produce identical output.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(DependentTypeRoundTripPropertyTests) })]
    public Property RoundTrip_PrintParsePrintPreservesDependentTypeSyntax(DependentTypeRoundTripTestCase testCase)
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
        return (printed1 == printed2).ToProperty()
            .Label($"Mismatch.\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}
