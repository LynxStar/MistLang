// Feature: generics-type-arguments, Property 1: Generic syntax round-trip
// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 2.1, 2.2, 2.3, 2.4, 3.1, 3.2, 3.3, 3.4, 3.5, 4.1, 4.2, 4.3, 4.4, 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 5.10**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

public class GenericRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
    }

#pragma warning disable CA1034
    public record GenericRoundTripTestCase(
        string Description,
        CompilationUnitNode Ast)
    {
        public override string ToString()
        {
            return Description;
        }
    }
#pragma warning restore CA1034

    private static readonly string[] TypeParamNames = ["T", "U", "V", "W"];
    private static readonly string[] ClassNames = ["Box", "Pair", "Container", "Wrapper"];
    private static readonly string[] StructNames = ["ValueHolder", "Entry", "Cell"];
    private static readonly string[] AbstractNames = ["IContainer", "IReadable", "IWritable"];
    private static readonly string[] MethodNames = ["Identity", "Transform", "Convert"];
    private static readonly string[] FieldNames = ["value", "item", "data"];

    public static Gen<TypeParameterListNode> GenTypeParameterList()
    {
        return Gen.Choose(1, 4).SelectMany(count =>
        {
            var names = TypeParamNames.Take(count).ToArray();
            var first = new RegularTypeParameterTypeParameterNode { Name = Tok(names[0]) };
            var rest = names.Skip(1)
                .Select(n => (TypeParameterNode)new RegularTypeParameterTypeParameterNode { Name = Tok(n) })
                .ToImmutableArray();

            return Gen.Constant(new TypeParameterListNode
            {
                TypeParameter = first,
                TypeParameters = rest,
            });
        });
    }

    public static Gen<TypeArgumentListNode> GenTypeArgumentList()
    {
        return Gen.Choose(1, 3).SelectMany(count => Gen.ArrayOf(MistTypeGenerator.GenPrimitiveTypeRef(), count)
                .Select(args => new TypeArgumentListNode
                {
                    TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = args[0] },
                    TypeArguments = args.Skip(1).Select(t => (TypeArgumentNode)new TypeReferenceTypeArgumentTypeArgumentNode { Type = t }).ToImmutableArray(),
                }));
    }

    private static Gen<BaseListNode> GenBaseList()
    {
        return from name in Gen.Elements("Base", "Parent", "Root")
               from hasTypeArgs in Gen.Elements(true, false)
               from typeArgs in hasTypeArgs
                   ? GenTypeArgumentList().Select(a => (TypeArgumentListNode?)a)
                   : Gen.Constant((TypeArgumentListNode?)null)
               select new BaseListNode
               {
                   First = new BaseTypeRefNode { Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(name) }, TypeArgs = typeArgs },
                   BaseTypeRefs = ImmutableArray<BaseTypeRefNode>.Empty,
               };
    }

    private static Gen<BaseListNode> GenAbstractBaseList()
    {
        var genEntry = from name in Gen.Elements("IFoo", "IBar", "IBaz")
                       from hasTypeArgs in Gen.Elements(true, false)
                       from typeArgs in hasTypeArgs
                           ? GenTypeArgumentList().Select(a => (TypeArgumentListNode?)a)
                           : Gen.Constant((TypeArgumentListNode?)null)
                       select new BaseTypeRefNode { Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(name) }, TypeArgs = typeArgs };

        var gen1 = genEntry.Select(e => new[] { e });
        var gen2 = from e1 in genEntry
                   from e2 in genEntry
                   select new[] { e1, e2 };

        return Gen.OneOf(gen1, gen2).Select(MakeBaseListFromEntries);
    }

    private static BaseListNode MakeBaseListFromEntries(BaseTypeRefNode[] entries)
    {
        return new BaseListNode
        {
            First = entries[0],
            BaseTypeRefs = entries.Skip(1).ToImmutableArray(),
        };
    }

    private static Gen<TypeBodyDeclarationNode> GenGenericClassDecl()
    {
        return from name in Gen.Elements(ClassNames)
               from typeParams in GenTypeParameterList()
               from hasBase in Gen.Elements(true, false)
               from baseList in hasBase
                   ? GenBaseList().Select(b => (BaseListNode?)b)
                   : Gen.Constant((BaseListNode?)null)
               from memberCount in Gen.Choose(0, 2)
               from members in GenMemberList(memberCount)
               select MakeGenericClass(name, typeParams, baseList, members);
    }

    private static TypeBodyDeclarationNode MakeGenericClass(
        string name,
        TypeParameterListNode typeParams,
        BaseListNode? baseList,
        ImmutableArray<MemberDeclarationNode> members)
    {
        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = Tok(name),
            TypeParams = typeParams,
            Members = members,
        };

        if(baseList is not null)
        {
            classDecl.BaseClause = baseList;
            classDecl.BaseList = baseList;
        }

        return classDecl;
    }

    private static Gen<TypeBodyDeclarationNode> GenGenericStructDecl()
    {
        return from name in Gen.Elements(StructNames)
               from typeParams in GenTypeParameterList()
               from hasAbstracts in Gen.Elements(true, false)
               from abstracts in hasAbstracts
                   ? GenAbstractBaseList().Select(b => (BaseListNode?)b)
                   : Gen.Constant((BaseListNode?)null)
               from memberCount in Gen.Choose(0, 1)
               from members in GenMemberList(memberCount)
               select MakeGenericStruct(name, typeParams, abstracts, members);
    }

    private static TypeBodyDeclarationNode MakeGenericStruct(
        string name,
        TypeParameterListNode typeParams,
        BaseListNode? abstracts,
        ImmutableArray<MemberDeclarationNode> members)
    {
        var structDecl = new TypeBodyDeclarationNode
        {
            Keyword = new StructKeywordTypeKeywordNode(),
            Name = Tok(name),
            TypeParams = typeParams,
            Members = members,
        };

        if(abstracts is not null)
        {
            structDecl.BaseList = abstracts;
        }

        return structDecl;
    }

    private static Gen<TypeBodyDeclarationNode> GenGenericAbstractDecl()
    {
        return from name in Gen.Elements(AbstractNames)
               from typeParams in GenTypeParameterList()
               from hasAbstracts in Gen.Elements(true, false)
               from abstracts in hasAbstracts
                   ? GenAbstractBaseList().Select(b => (BaseListNode?)b)
                   : Gen.Constant((BaseListNode?)null)
               from methodCount in Gen.Choose(0, 2)
               from methods in GenAbstractMethodList(methodCount)
               select MakeGenericAbstract(name, typeParams, abstracts, methods);
    }

    private static TypeBodyDeclarationNode MakeGenericAbstract(
        string name,
        TypeParameterListNode typeParams,
        BaseListNode? abstracts,
        ImmutableArray<MemberDeclarationNode> members)
    {
        var abstractDecl = new TypeBodyDeclarationNode
        {
            Keyword = new AbstractKeywordTypeKeywordNode(),
            Name = Tok(name),
            TypeParams = typeParams,
            Members = members,
        };

        if(abstracts is not null)
        {
            abstractDecl.BaseList = abstracts;
        }

        return abstractDecl;
    }

    private static Gen<MethodDeclarationNode> GenGenericMethodDecl()
    {
        return from methodName in Gen.Elements(MethodNames)
               from methodTypeParams in GenTypeParameterList()
               from returnType in MistTypeGenerator.GenReturnTypeRef()
               from hasParam in Gen.Elements(true, false)
               from paramType in MistTypeGenerator.GenPrimitiveTypeRef()
               let parameters = hasParam
                   ? new ParameterListNode
                   {
                       Parameter = new ParameterNode
                       {
                           Type = paramType,
                           Name = Tok("arg"),
                       },
                   }
                   : null
               select new MethodDeclarationNode
               {
                   Name = Tok(methodName),
                   TypeParams = methodTypeParams,
                   ReturnType = returnType,
                   Parameters = parameters,
                   Body = new CallableBlockBodyCallableBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
                   Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
               };
    }

    private static Gen<MethodDeclarationNode> GenGenericAbstractMethodDecl()
    {
        return from methodName in Gen.Elements(MethodNames)
               from hasMethodTypeParams in Gen.Elements(true, false)
               from methodTypeParams in hasMethodTypeParams
                   ? GenTypeParameterList().Select(tp => (TypeParameterListNode?)tp)
                   : Gen.Constant((TypeParameterListNode?)null)
               from returnType in MistTypeGenerator.GenReturnTypeRef()
               select new MethodDeclarationNode
               {
                   Name = Tok(methodName),
                   TypeParams = methodTypeParams,
                   ReturnType = returnType,
                   Parameters = null,
                   Body = new CallableNoBodyCallableBodyNode(),
               };
    }

    private static Gen<ImmutableArray<MemberDeclarationNode>> GenMemberList(int count)
    {
        if(count == 0)
        {
            return Gen.Constant(ImmutableArray<MemberDeclarationNode>.Empty);
        }

        var memberGen = Gen.OneOf(
            GenFieldMember(),
            GenGenericMethodDecl().Select(m =>
                (MemberDeclarationNode)new MethodMemberMemberDeclarationNode { Method = m }));

        return Gen.ArrayOf(memberGen, count)
            .Select(members => members.ToImmutableArray());
    }

    private static Gen<MemberDeclarationNode> GenFieldMember()
    {
        return from fieldName in Gen.Elements(FieldNames)
               from type in MistTypeGenerator.GenPrimitiveTypeRef()
               select (MemberDeclarationNode)new FieldMemberMemberDeclarationNode
               {
                   Field = new FieldDeclarationNode
                   {
                       Name = Tok(fieldName),
                       Type = type,
                       Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                   },
               };
    }

    private static Gen<ImmutableArray<MemberDeclarationNode>> GenAbstractMethodList(int count)
    {
        if(count == 0)
        {
            return Gen.Constant(ImmutableArray<MemberDeclarationNode>.Empty);
        }

        var methodGen = GenGenericAbstractMethodDecl().Select(m =>
            (MemberDeclarationNode)new MethodMemberMemberDeclarationNode { Method = m });

        return Gen.ArrayOf(methodGen, count)
            .Select(members => members.ToImmutableArray());
    }

    private static Gen<ExpressionNode> GenGenericMethodCall()
    {
        return from targetName in Gen.Elements("obj", "self", "item")
               from methodName in Gen.Elements("Get", "Convert", "Map")
               from typeArgs in GenTypeArgumentList()
               select MakeGenericMethodCallExpr(targetName, methodName, typeArgs);
    }

    private static ExpressionNode MakeGenericMethodCallExpr(
        string target, string methodName, TypeArgumentListNode typeArgs)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(target) };
        var suffix = new MethodCallSuffixPostfixSuffixNode
        {
            Name = Tok(methodName),
            TypeArgs = typeArgs,
            Arguments = null,
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(suffix),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = unary } };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static Gen<GenericRoundTripTestCase> GenGenericClassTestCase()
    {
        return GenGenericClassDecl().Select(classDecl =>
        {
            var cu = new CompilationUnitNode
            {
                Items = ImmutableArray.Create<TypeDeclarationNode>(
                    new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
            };
            var desc = $"class {classDecl.Name.Lexeme}<...>";
            return new GenericRoundTripTestCase(desc, cu);
        });
    }

    private static Gen<GenericRoundTripTestCase> GenGenericStructTestCase()
    {
        return GenGenericStructDecl().Select(structDecl =>
        {
            var cu = new CompilationUnitNode
            {
                Items = ImmutableArray.Create<TypeDeclarationNode>(
                    new BodyTypeTypeDeclarationNode { TypeBody = structDecl }).AsTopLevelItems(),
            };
            var desc = $"struct {structDecl.Name.Lexeme}<...>";
            return new GenericRoundTripTestCase(desc, cu);
        });
    }

    private static Gen<GenericRoundTripTestCase> GenGenericAbstractTestCase()
    {
        return GenGenericAbstractDecl().Select(abstractDecl =>
        {
            var cu = new CompilationUnitNode
            {
                Items = ImmutableArray.Create<TypeDeclarationNode>(
                    new BodyTypeTypeDeclarationNode { TypeBody = abstractDecl }).AsTopLevelItems(),
            };
            var desc = $"abstract {abstractDecl.Name.Lexeme}<...>";
            return new GenericRoundTripTestCase(desc, cu);
        });
    }

    private static Gen<GenericRoundTripTestCase> GenGenericMethodCallTestCase()
    {
        return from className in Gen.Elements(ClassNames)
               from callExpr in GenGenericMethodCall()
               select MakeMethodCallTestCase(className, callExpr);
    }

    private static GenericRoundTripTestCase MakeMethodCallTestCase(
        string className, ExpressionNode callExpr)
    {
        var statements = ImmutableArray.Create<StatementNode>(
            new ExpressionStatementStatementNode
            {
                ExpressionStatement = new ExpressionStatementNode { Expression = callExpr },
            });

        var method = new MethodDeclarationNode
        {
            Name = Tok("Run"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new CallableBlockBodyCallableBodyNode { Block = new BlockNode { Statements = statements } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = Tok(className),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = method }),
        };

        var cu = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };

        return new GenericRoundTripTestCase($"class {className} with generic method calls", cu);
    }

    private static Gen<GenericRoundTripTestCase> GenTestCase()
    {
        return Gen.OneOf(
            GenGenericClassTestCase(),
            GenGenericStructTestCase(),
            GenGenericAbstractTestCase(),
            GenGenericMethodCallTestCase());
    }

    public static Arbitrary<GenericRoundTripTestCase> ArbitraryGenericRoundTripTestCase()
    {
        return GenTestCase().ToArbitrary();
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(GenericRoundTripPropertyTests) })]
    public Property RoundTrip_PrintParsePrintPreservesGenericSyntax(GenericRoundTripTestCase testCase)
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

        var printed2 = MistPrettyPrinter.Print(reparsed);

        return (printed1 == printed2).ToProperty()
            .Label($"Mismatch.\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}