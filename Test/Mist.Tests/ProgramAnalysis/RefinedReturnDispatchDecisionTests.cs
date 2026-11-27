using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class RefinedReturnDispatchDecisionTests
{
    private readonly DeclarationFragments fragment = new();
    private readonly HandBuiltRefinements h = new();
    private readonly AbstractType contract;
    private readonly ClassType factory;
    private readonly RefinedType positive;
    private readonly RefinedType nonnegative;

    public RefinedReturnDispatchDecisionTests()
    {
        contract = fragment.Abstract("IFactory");
        factory = fragment.Class("Factory");
        positive = h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, "PositiveInt");
        nonnegative = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 0L, "NonNegativeInt");
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AReturnClosesOverTheAbstractsArgument()
    {
        var t = Parameter(contract, "T");
        factory.BaseAbstractTypes.Add(Close(contract, positive));
        _ = DeclarationFragments.Method(contract, "Make", t);
        _ = Returning(factory, positive, h.Literal(1L));
        AssertJudgment(null);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(0, true)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(2, false)]
    public void TheFillerClosesThroughEveryGenericBase(int baseDepth, bool preserves)
    {
        var promised = preserves ? nonnegative : positive;
        var produced = preserves ? positive : nonnegative;
        factory.BaseAbstractTypes.Add(Close(contract, promised, produced));
        _ = DeclarationFragments.Method(contract, "Make", Parameter(contract, "TOut"), Parameter(contract, "TIn"));

        if(baseDepth == 0)
        {
            _ = ReturningArgument(factory, produced);
        }
        else
        {
            var baseType = fragment.Class("Base<>");
            var t = Parameter(baseType, "T");
            _ = ReturningArgument(baseType, t);
            if(baseDepth == 2)
            {
                var middle = fragment.Class("Middle<>");
                middle.BaseType = Close(baseType, Parameter(middle, "T"));
                baseType = middle;
            }

            factory.BaseType = Close(baseType, produced);
        }

        AssertJudgment(preserves ? null : "MIST0284");
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(true)]
    [InlineData(false)]
    public void GenericMethodsAnswerSlotsWithRenamedTypeParameters(bool preserves)
    {
        factory.BaseAbstractTypes.Add(contract);
        IMistType[] parameters = preserves ? [positive] : [];
        var slot = DeclarationFragments.Method(contract, "Make", positive, parameters);
        var filler = preserves ? ReturningArgument(factory, positive) : Returning(factory, h.Int, h.Literal(-1L));
        slot.TypeParametersList.Add(new TypeParameter("U", 0, slot.Scope, contract) { DeclaringMethod = slot });
        filler.TypeParametersList.Add(new TypeParameter("V", 0, filler.Scope, factory) { DeclaringMethod = filler });
        AssertJudgment(preserves ? null : "MIST0284");
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AnAbstractSlotReachedAlongBothSidesOfADiamondIsAnswered()
    {
        _ = DeclarationFragments.Method(contract, "Make", positive);
        var left = fragment.Abstract("ILeft");
        var right = fragment.Abstract("IRight");
        left.BaseAbstractTypes.Add(contract);
        right.BaseAbstractTypes.Add(contract);
        factory.BaseAbstractTypes.AddRange([left, right]);
        _ = Returning(factory, positive, h.Literal(1L));
        AssertJudgment(null);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("inherited", false, true)]
    [InlineData("mapped", false, true)]
    [InlineData("mapped", false, false)]
    [InlineData("unrelated mapping", false, true)]
    [InlineData("unrelated mapping", false, false)]
    [InlineData("default only", true, true)]
    [InlineData("direct", true, true)]
    [InlineData("direct", true, false)]
    [InlineData("inherited", true, true)]
    [InlineData("inherited", true, false)]
    [InlineData("mapped", true, true)]
    [InlineData("mapped", true, false)]
    [InlineData("unrelated mapping", true, true)]
    [InlineData("unrelated mapping", true, false)]
    public void TheSelectedMethodMustPreserveTheRefinedReturn(string route, bool hasDefault, bool preserves)
    {
        var slot = new Method("Make", positive, [], DeclarationFragments.Public)
        {
            DeclaringType = contract,
            Body = hasDefault ? DeclarationFragments.Returning(h.Literal(1L)) : null,
        };
        contract.MethodsList.Add(slot);

        if(route == "default only")
        {
            factory.BaseAbstractTypes.Add(contract);
        }
        else
        {
            var owner = route == "inherited" ? fragment.Class("Base") : factory;
            if(owner != factory)
            {
                factory.BaseType = owner;
            }

            var filler = new Method(route == "mapped" ? "Text" : "Make", preserves ? positive : h.Int, [], DeclarationFragments.Public)
            {
                DeclaringType = owner,
                Body = DeclarationFragments.Returning(h.Literal(preserves ? 2L : -1L)),
            };
            owner.MethodsList.Add(filler);
            switch(route)
            {
                case "mapped":
                    Map(contract, new MethodMapping(filler, slot, contract));
                    break;
                case "unrelated mapping":
                    var otherSlot = DeclarationFragments.Method(contract, "Text", h.Int);
                    var otherFiller = new Method("Text", h.Int, [], DeclarationFragments.Public)
                    { DeclaringType = factory, Body = DeclarationFragments.Returning(h.Literal(0L)) };
                    factory.MethodsList.Add(otherFiller);
                    Map(contract, new MethodMapping(otherFiller, otherSlot, contract));
                    break;
                default:
                    factory.BaseAbstractTypes.Add(contract);
                    break;
            }
        }

        AssertJudgment(preserves ? null : "MIST0284");
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(true)]
    [InlineData(false)]
    public void AnInheritedMethodPreservesARealReturnPromise(bool preserves)
    {
        var positiveReal = h.Refine(h.Double, ComparisonKind.GreaterThan, 0.0, "PositiveReal");
        var nonnegativeReal = h.Refine(h.Double, ComparisonKind.GreaterThanOrEqual, 0.0, "NonNegativeReal");
        var owner = fragment.Class("Base");
        factory.BaseType = owner;
        factory.BaseAbstractTypes.Add(contract);
        if(preserves)
        {
            _ = DeclarationFragments.Method(contract, "Make", nonnegativeReal, positiveReal);
            _ = ReturningArgument(owner, positiveReal);
        }
        else
        {
            _ = DeclarationFragments.Method(contract, "Make", positiveReal);
            owner.MethodsList.Add(new Method("Make", h.Double, [], DeclarationFragments.Public)
            { DeclaringType = owner, Body = DeclarationFragments.Returning(h.Literal(-1.0)) });
        }

        AssertJudgment(preserves ? null : "MIST0284");
    }

    [Theory]
    [Trait("Tier", "4")]
    [InlineData("NonNegativeInt", "PositiveInt", null)]
    [InlineData("PositiveInt", "NonNegativeInt", "MIST0284")]
    public void BindingAnInheritedMappedMethodSuppliesItsClosedReturnToAnalysis(string promised, string produced, string? expected)
    {
        var findings = RefinedReturnContractDecisionTests.Analyse($$"""
            type PositiveInt = int > 0;
            type NonNegativeInt = int >= 0;
            public abstract IFactory<TOut, TIn> { TOut Make(TIn value); }
            public class Base<T> { public T Text(T value) { return value; } }
            public class Factory : Base<{{produced}}> { }
            with Factory : IFactory<{{promised}}, {{produced}}> { Text as Make; }
            """);

        Assert.Equal(expected is null ? [] : new[] { expected }, findings.Select(finding => finding.Number.Value));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("direct", "nonnegative", "positive", "get", "field", null)]
    [InlineData("direct", "positive", "int", "get", "literal", null)]
    [InlineData("direct", "positive", "positive", "both", "field", null)]
    [InlineData("direct", "positive", "int", "set", "", null)]
    [InlineData("inherited", "nonnegative", "positive", "get", "auto", null)]
    [InlineData("mapped", "nonnegative", "positive", "get", "literal", null)]
    [InlineData("direct", "positive", "nonnegative", "get", "field", "MIST0284")]
    [InlineData("direct", "positive real", "double", "get", "literal", "MIST0284")]
    [InlineData("direct", "positive", "nonnegative", "both", "field", "MIST0284")]
    [InlineData("direct", "nonnegative", "positive", "both", "field", "MIST0283")]
    [InlineData("direct", "positive", "large", "set", "", "MIST0283")]
    [InlineData("inherited", "positive", "nonnegative", "get", "auto", "MIST0284")]
    [InlineData("mapped", "positive", "int", "get", "negative", "MIST0284")]
    public void APropertyMeetsTheContractInEachAccessorDirection(
        string route, string promise, string production, string accessors, string body, string? expected)
    {
        var promised = Type(promise);
        var produced = Type(production);
        var slotType = route == "mapped" ? Parameter(contract, "T") : promised;
        var slot = new Property("Value", slotType, DeclarationFragments.Public)
        {
            DeclaringType = contract,
            Getter = accessors != "set" ? new PropertyAccessor { IsAuto = true } : null,
            Setter = accessors != "get" ? new PropertyAccessor { IsAuto = true } : null,
        };
        contract.PropertiesList.Add(slot);

        var owner = factory;
        var fillerType = produced;
        if(route == "inherited")
        {
            owner = fragment.Class("Base<>");
            fillerType = Parameter(owner, "T");
            factory.BaseType = Close(owner, produced);
        }

        var field = new Field("Held", fillerType, default, DeclarationFragments.Public) { DeclaringType = owner };
        if(body == "field")
        {
            owner.FieldsList.Add(field);
        }

        var getter = body switch
        {
            "auto" => new PropertyAccessor { IsAuto = true },
            "field" => new PropertyAccessor { Body = DeclarationFragments.Returning(new FieldReferenceExpression(HandBuiltModel.Node(), field)) },
            "literal" => new PropertyAccessor { Body = DeclarationFragments.Returning(production == "double" ? h.Literal(1.0) : h.Literal(1L)) },
            "negative" => new PropertyAccessor { Body = DeclarationFragments.Returning(h.Literal(-1L)) },
            _ => null,
        };
        var filler = new Property("Value", fillerType, DeclarationFragments.Public)
        {
            DeclaringType = owner,
            Getter = getter,
            Setter = accessors == "get" ? null : new PropertyAccessor
            {
                Body = accessors == "set" ? HandBuiltModel.Block() : HandBuiltModel.Block(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(
                    new FieldReferenceExpression(HandBuiltModel.Node(), field), HandBuiltRefinements.Local(fillerType, "value")))),
            },
        };
        owner.PropertiesList.Add(filler);
        if(route == "mapped")
        {
            var closing = Close(contract, promised);
            Map(closing, new PropertyMapping(filler, slot, closing));
        }
        else
        {
            factory.BaseAbstractTypes.Add(contract);
        }

        AssertJudgment(expected);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("box", "positive", "positive", null)]
    [InlineData("nested box", "positive", "positive", null)]
    [InlineData("tuple", "positive", "positive", null)]
    [InlineData("function", "positive", "positive", null)]
    [InlineData("union", "positive", "positive", null)]
    [InlineData("enclosing", "positive", "positive", null)]
    [InlineData("box", "positive", "int", "MIST0284")]
    [InlineData("box", "nonnegative", "positive", "MIST0284")]
    [InlineData("nested box", "positive", "int", "MIST0284")]
    [InlineData("enclosing", "positive", "int", "MIST0284")]
    [InlineData("tuple", "positive", "int", "MIST0184")]
    [InlineData("tuple", "nonnegative", "positive", "MIST0184")]
    [InlineData("function", "positive", "int", "MIST0184")]
    [InlineData("union", "positive", "int", "MIST0184")]
    public void AnEmbeddedRefinementIsJudgedBySignatureAndReturn(string shape, string promise, string production, string? expected)
    {
        var box = fragment.Class("Box<>");
        _ = Parameter(box, "T");
        var outer = fragment.Class("Outer<>");
        _ = Parameter(outer, "T");
        var token = fragment.Class("Token", outer);
        IMistType embed(IMistType inner)
        {
            return shape switch
            {
                "box" => Close(box, inner),
                "nested box" => Close(box, Close(box, inner)),
                "tuple" => fragment.Built.Interner.GetOrCreateTupleType([inner, h.Int]),
                "function" => fragment.Built.Interner.GetOrCreateFunctionType([], inner),
                "union" => fragment.Built.Interner.GetOrCreateUnionType([inner, h.String]),
                "enclosing" => fragment.Built.Interner.GetOrCreateConstructedType(token, [], Close(outer, inner)),
                _ => throw new ArgumentOutOfRangeException(nameof(shape)),
            };
        }

        var promised = embed(Type(promise));
        var produced = embed(Type(production));
        factory.BaseAbstractTypes.Add(contract);
        _ = DeclarationFragments.Method(contract, "Make", promised, produced);
        _ = ReturningArgument(factory, produced);
        AssertJudgment(expected);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AStrongerSymbolicParameterBound_IsNotDiscarded()
    {
        factory.BaseAbstractTypes.Add(contract);
        var bound = new Field("Bound", h.Int, default, DeclarationFragments.Public with { IsReadonly = true }) { DeclaringType = factory };
        factory.FieldsList.Add(bound);
        var demanding = HandBuiltRefinements.RefinePattern(positive, h.Compare(h.Int, ComparisonKind.LessThan,
            new FieldReferenceExpression(HandBuiltModel.Node(), bound)), null);
        _ = DeclarationFragments.Method(contract, "Take", h.Built.Type("Void"), positive);
        _ = DeclarationFragments.Method(factory, "Take", h.Built.Type("Void"), demanding);

        var finding = Assert.Single(fragment.Run(new RefinementAnalyzer()));
        Assert.Equal("MIST0283", finding.Number.Value);
        Assert.Contains("< Bound", finding.Message, StringComparison.Ordinal);
    }

    private IMistType Type(string name)
    {
        return name switch
        {
            "int" => h.Int,
            "double" => h.Double,
            "large" => h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 2L, "LargeInt"),
            "positive real" => h.Refine(h.Double, ComparisonKind.GreaterThan, 0.0, "PositiveReal"),
            "positive" => positive,
            "nonnegative" => nonnegative,
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    private static Method ReturningArgument(MemberType owner, IMistType type)
    {
        var parameter = new Parameter("value", type);
        var value = HandBuiltRefinements.Read(new LocalVariable(parameter.Name, type) { Parameter = parameter });
        return Returning(owner, type, value, parameter);
    }

    private static Method Returning(MemberType owner, IMistType type, Expression value, params Parameter[] parameters)
    {
        var method = new Method("Make", type, [.. parameters], DeclarationFragments.Public)
        {
            DeclaringType = owner,
            Scope = new Scope(owner.Scope),
            DeclarationNode = HandBuiltModel.Node(20),
            Body = DeclarationFragments.Returning(value),
        };
        owner.MethodsList.Add(method);
        return method;
    }

    private static TypeParameter Parameter(MemberType type, string name)
    {
        var parameter = new TypeParameter(name, type.TypeParametersList.Count, type.Scope, type);
        type.TypeParametersList.Add(parameter);
        return parameter;
    }

    private ConstructedType Close(IMistType type, params IMistType[] arguments)
    {
        return fragment.Built.Interner.GetOrCreateConstructedType(type, arguments);
    }

    private void Map(IMistType target, MemberMapping mapping)
    {
        factory.ConformancesList.Add(new ConformanceDeclaration(factory, target, "Refusal", [mapping], [], HandBuiltModel.Node(40)));
    }

    private void AssertJudgment(string? expected)
    {
        var findings = fragment.Run(new ConformanceAnalyzer()).Concat(fragment.Run(new RefinementAnalyzer()));
        Assert.Equal(expected is null ? [] : new[] { expected }, findings.Select(finding => finding.Number.Value));
    }
}