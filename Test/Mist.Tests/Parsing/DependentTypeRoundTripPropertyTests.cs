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

public class DependentTypeRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
    }

#pragma warning disable CA1034
    public record DependentTypeRoundTripTestCase(
        string Description,
        CompilationUnitNode Ast)
    {
        public override string ToString()
        {
            return Description;
        }
    }
#pragma warning restore CA1034

    /// <summary>Dependent type parameters exclude float, double, decimal and string.</summary>
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

    private static Gen<PrimitiveTypeNode> GenDependentPrimitiveType()
    {
        return Gen.Elements(DependentPrimitiveTypeNodes);
    }

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

    private static Gen<TypeParameterNode> GenRegularTypeParameter()
    {
        return Gen.Elements(TypeParamNames)
            .Select(name => (TypeParameterNode)new RegularTypeParameterTypeParameterNode
            {
                Name = Tok(name),
            });
    }

    private static Gen<TypeParameterListNode> GenDependentTypeParameterList()
    {
        return Gen.Choose(1, 3).SelectMany(count =>
        {
            if(count == 1)
            {
                return GenDependentTypeParameter().Select(dep =>
                    new TypeParameterListNode
                    {
                        TypeParameter = dep,
                        TypeParameters = ImmutableArray<TypeParameterNode>.Empty,
                    });
            }

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

    private static Gen<TypeArgumentNode> GenIntegerLiteralTypeArgument()
    {
        return Gen.Elements("1", "5", "10", "42", "100", "255", "1024")
            .Select(v => (TypeArgumentNode)new IntegerLiteralTypeArgumentTypeArgumentNode
            {
                Literal = Tok(v),
            });
    }

    private static Gen<TypeArgumentNode> GenNegatedIntegerTypeArgument()
    {
        return Gen.Elements("1", "5", "10", "42", "100")
            .Select(v => (TypeArgumentNode)new NegatedIntegerTypeArgumentTypeArgumentNode
            {
                Literal = Tok(v),
            });
    }

    private static Gen<TypeArgumentNode> GenHexLiteralTypeArgument()
    {
        return Gen.Elements("0xFF", "0x1A", "0x00", "0xDEAD")
            .Select(v => (TypeArgumentNode)new HexLiteralTypeArgumentTypeArgumentNode
            {
                Literal = Tok(v),
            });
    }

    private static Gen<TypeArgumentNode> GenBinaryLiteralTypeArgument()
    {
        return Gen.Elements("0b1010", "0b0001", "0b1111", "0b0000")
            .Select(v => (TypeArgumentNode)new BinaryLiteralTypeArgumentTypeArgumentNode
            {
                Literal = Tok(v),
            });
    }

    private static Gen<TypeArgumentNode> GenBooleanLiteralTypeArgument()
    {
        return Gen.Elements(
            new TrueLiteralTypeArgumentTypeArgumentNode(),
            (TypeArgumentNode)new FalseLiteralTypeArgumentTypeArgumentNode());
    }

    private static Gen<TypeArgumentNode> GenCharLiteralTypeArgument()
    {
        return Gen.Elements("'x'", "'A'", "'z'", "'0'", "'\\n'")
            .Select(v => (TypeArgumentNode)new CharLiteralTypeArgumentTypeArgumentNode
            {
                Literal = Tok(v),
            });
    }

    private static Gen<TypeArgumentNode> GenTypeReferenceTypeArgument()
    {
        return MistTypeGenerator.GenPrimitiveTypeRef()
            .Select(typeRef => (TypeArgumentNode)new TypeReferenceTypeArgumentTypeArgumentNode
            {
                Type = typeRef,
            });
    }

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

    private static Gen<TypeArgumentListNode> GenDependentTypeArgumentList()
    {
        return Gen.Choose(1, 3).SelectMany(count =>
        {
            if(count == 1)
            {
                return GenDependentValueTypeArgument().Select(arg =>
                    new TypeArgumentListNode
                    {
                        TypeArgument = arg,
                        TypeArguments = ImmutableArray<TypeArgumentNode>.Empty,
                    });
            }

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
        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = Tok(className),
            TypeParams = typeParams,
            Members = members,
        };

        var cu = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };

        return new DependentTypeRoundTripTestCase($"class {className}<...dependent...>", cu);
    }

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
        var structDecl = new TypeBodyDeclarationNode
        {
            Keyword = new StructKeywordTypeKeywordNode(),
            Name = Tok(structName),
            TypeParams = typeParams,
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var cu = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = structDecl }).AsTopLevelItems(),
        };

        return new DependentTypeRoundTripTestCase($"struct {structName}<...dependent...>", cu);
    }

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
        var abstractDecl = new TypeBodyDeclarationNode
        {
            Keyword = new AbstractKeywordTypeKeywordNode(),
            Name = Tok(abstractName),
            TypeParams = typeParams,
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var cu = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = abstractDecl }).AsTopLevelItems(),
        };

        return new DependentTypeRoundTripTestCase($"abstract {abstractName}<...dependent...>", cu);
    }

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
        var fieldType = new TypeReferenceNode
        {
            First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode
                {
                    IDENTIFIER = Tok(fieldTypeName),
                    IDENTIFIERs = ImmutableArray<Storm.Runtime.Token>.Empty,
                },
                TypeArgs = typeArgs,
            }
        };

        var field = new FieldDeclarationNode
        {
            Name = Tok(fieldName),
            Type = fieldType,
            Modifiers = ImmutableArray<ModifierNode>.Empty,
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = Tok(className),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = field }),
        };

        var cu = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };

        return new DependentTypeRoundTripTestCase(
            $"class {className} with field {fieldTypeName}<...dep args...>", cu);
    }

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
        var declClass = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = Tok(declClassName),
            TypeParams = typeParams,
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var fieldType = new TypeReferenceNode
        {
            First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode
                {
                    IDENTIFIER = Tok(fieldTypeName),
                    IDENTIFIERs = ImmutableArray<Storm.Runtime.Token>.Empty,
                },
                TypeArgs = typeArgs,
            }
        };

        var field = new FieldDeclarationNode
        {
            Name = Tok(fieldName),
            Type = fieldType,
            Modifiers = ImmutableArray<ModifierNode>.Empty,
        };

        var usageClass = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = Tok(usageClassName),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = field }),
        };

        var cu = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = declClass },
                new BodyTypeTypeDeclarationNode { TypeBody = usageClass }).AsTopLevelItems(),
        };

        return new DependentTypeRoundTripTestCase(
            $"class {declClassName}<...dep...> + class {usageClassName} using it", cu);
    }

    private static Gen<ImmutableArray<MemberDeclarationNode>> GenFieldMembers(int count)
    {
        if(count == 0)
        {
            return Gen.Constant(ImmutableArray<MemberDeclarationNode>.Empty);
        }

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

    private static Gen<DependentTypeRoundTripTestCase> GenTestCase()
    {
        return Gen.OneOf(
            GenDependentClassTestCase(),
            GenDependentStructTestCase(),
            GenDependentAbstractTestCase(),
            GenDependentTypeArgumentUsageTestCase(),
            GenCombinedDeclAndUsageTestCase());
    }

    public static Arbitrary<DependentTypeRoundTripTestCase> ArbitraryDependentTypeRoundTripTestCase()
    {
        return GenTestCase().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 2.1, 2.2, 2.3,
    // 2.4, 2.5, 2.6, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(DependentTypeRoundTripPropertyTests) })]
    public Property RoundTrip_PrintParsePrintPreservesDependentTypeSyntax(DependentTypeRoundTripTestCase testCase)
    {
        var printed1 = MistPrettyPrinter.Print(testCase.Ast);
        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label("printed1 was empty");
        }

        var sourceText = new Mist.Common.Source.SourceText("test.mist", printed1);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            return false.ToProperty()
                .Label($"Parse errors: {string.Join("; ", errors.Select(e => e.Message))}\nSource:\n{printed1}");
        }

        if(result.Root is not CompilationUnitNode reparsed)
        {
            return false.ToProperty().Label($"Reparsed root was null.\nSource:\n{printed1}");
        }

        var printed2 = MistPrettyPrinter.Print(reparsed, printed1);

        return (printed1 == printed2).ToProperty()
            .Label($"Mismatch.\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}