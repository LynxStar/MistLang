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

// A case names the route that selects the filler, then what the filler's bound is: the slot's symbol under the
// same comparison, a strict comparison where the slot's is inclusive, or a same-spelled symbol the slot never names.
[Trait("Tier", "2")]
public sealed class SymbolicContractTests
{
    private const string Strengthened = "MIST0283";
    private const string Unproven = "MIST0284";

    [Theory]
    [InlineData("direct", "same", "")]
    [InlineData("direct", "strict", Strengthened)]
    [InlineData("direct", "unrelated", Strengthened)]
    [InlineData("inherited", "same", "")]
    [InlineData("inherited", "strict", Strengthened)]
    [InlineData("inherited", "unrelated", Strengthened)]
    [InlineData("mapped", "same", "")]
    [InlineData("mapped", "strict", Strengthened)]
    [InlineData("mapped", "unrelated", Strengthened)]
    [InlineData("default", "same", "")]
    [InlineData("default", "strict", Strengthened)]
    [InlineData("default", "unrelated", Strengthened)]
    [InlineData("generic", "same", "")]
    [InlineData("generic", "strict", Strengthened)]
    [InlineData("generic", "unrelated", Strengthened)]
    [InlineData("property", "same", "")]
    [InlineData("property", "strict", Strengthened)]
    [InlineData("property", "unrelated", Unproven + "," + Strengthened)]
    public void ASelectedFillerMeetsTheSlotOnlyOverTheSymbolsItAnswers(string route, string bound, string expected)
    {
        var fragment = route == "generic" ? Generic(bound) : Build(route, bound);
        var found = fragment.Run(new RefinementAnalyzer()).Select(diagnostic => diagnostic.Number.Value).Order(StringComparer.Ordinal);
        Assert.Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal), found);
    }

    private static DeclarationFragments Build(string route, string bound)
    {
        var fragment = new DeclarationFragments();
        var integer = fragment.Built.Type("Int32");
        var contract = fragment.Abstract("IBook");
        var concrete = fragment.Class("Book");
        var owner = route == "inherited" ? fragment.Class("Shelf") : concrete;
        var other = fragment.Class("Other");
        var promised = Count(contract, "Count", integer);
        var answering = Count(owner, route == "mapped" ? "Size" : "Count", integer);
        var unrelated = Count(other, "Count", integer);
        var named = bound == "unrelated" ? unrelated : answering;
        var kind = bound == "strict" ? ComparisonKind.LessThan : ComparisonKind.LessThanOrEqual;

        if(route == "inherited")
        {
            concrete.BaseType = owner;
        }

        if(route == "property")
        {
            _ = Slot(contract, Index(integer, promised, ComparisonKind.LessThanOrEqual));
            _ = Slot(owner, Index(integer, named, kind));
            concrete.BaseAbstractTypes.Add(contract);
            return fragment;
        }

        var take = new Method("Take", fragment.Built.Type("Void"), [new Parameter("p0", Index(integer, promised, ComparisonKind.LessThanOrEqual))],
            DeclarationFragments.Public)
        {
            DeclarationNode = HandBuiltModel.Node(20),
            DeclaringType = contract,
            Scope = new Scope(contract.Scope),
            Body = route == "default" ? HandBuiltModel.Block() : null,
        };
        contract.MethodsList.Add(take);
        var filler = DeclarationFragments.Method(owner, route == "mapped" ? "Accept" : "Take", fragment.Built.Type("Void"), Index(integer, named, kind));

        if(route == "mapped")
        {
            concrete.ConformancesList.Add(new ConformanceDeclaration(concrete, contract, "Refusal",
                [new MethodMapping(filler, take, contract), new PropertyMapping(answering, promised, contract)], [], HandBuiltModel.Node(40)));
        }
        else
        {
            concrete.BaseAbstractTypes.Add(contract);
        }

        return fragment;
    }

    private static DeclarationFragments Generic(string bound)
    {
        var fragment = new DeclarationFragments();
        var integer = fragment.Built.Type("Int32");
        var contract = fragment.Abstract("IBuffer");
        var size = Dependent(contract, contract.Scope, "N", integer);
        var concrete = fragment.Class("Ring");
        var length = Dependent(concrete, concrete.Scope, "M", integer);
        var spelledAlike = Dependent(concrete, concrete.Scope, "M", integer);
        var named = bound == "unrelated" ? spelledAlike : length;
        var kind = bound == "strict" ? ComparisonKind.LessThan : ComparisonKind.LessThanOrEqual;
        concrete.BaseAbstractTypes.Add(fragment.Built.Interner.GetOrCreateConstructedType(contract, [length]));
        _ = DeclarationFragments.Method(contract, "Put", fragment.Built.Type("Void"),
            Refine(integer, new TypeReferenceExpression(HandBuiltModel.Node(), size), ComparisonKind.LessThanOrEqual));
        _ = DeclarationFragments.Method(concrete, "Put", fragment.Built.Type("Void"),
            Refine(integer, new TypeReferenceExpression(HandBuiltModel.Node(), named), kind));
        return fragment;
    }

    private static Property Count(MemberType owner, string name, IMistType integer)
    {
        var property = new Property(name, integer, DeclarationFragments.Public) { DeclaringType = owner, Getter = new PropertyAccessor() };
        owner.PropertiesList.Add(property);
        return property;
    }

    private static Property Slot(MemberType owner, IMistType type)
    {
        var property = new Property("Slot", type, DeclarationFragments.Public)
        {
            DeclaringType = owner,
            Getter = new PropertyAccessor(),
            Setter = new PropertyAccessor(),
        };
        owner.PropertiesList.Add(property);
        return property;
    }

    private static TypeParameter Dependent(IHasTypeParameters owner, Scope scope, string name, ClassType integer)
    {
        var parameter = new TypeParameter(name, owner.TypeParametersList.Count, scope, null) { IsDependent = true, Domain = integer };
        owner.TypeParametersList.Add(parameter);
        return parameter;
    }

    private static RefinedType Index(IMistType integer, Property bound, ComparisonKind kind)
    {
        return Refine(integer, new PropertyReferenceExpression(HandBuiltModel.Node(), bound), kind);
    }

    private static RefinedType Refine(IMistType integer, Expression bound, ComparisonKind kind)
    {
        return HandBuiltRefinements.RefinePattern(integer, new ConjunctionPattern(HandBuiltModel.Node(), integer,
        [
            new ComparisonPattern(HandBuiltModel.Node(), integer, ComparisonKind.GreaterThanOrEqual, HandBuiltModel.Literal(integer, 0L)),
            new ComparisonPattern(HandBuiltModel.Node(), integer, kind, bound),
        ]), null);
    }
}