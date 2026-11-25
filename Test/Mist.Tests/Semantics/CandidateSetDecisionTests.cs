using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class CandidateSetDecisionTests
{
    [Fact]
    public void ANearerMethodGroupAnswersOverAFartherField()
    {
        var owner = HandBuiltModel.Class("Owner");
        var method = Method(owner, HandBuiltModel.Class("Arg"));
        var candidates = new CandidateSet();
        candidates.Add(new MethodGroupResult([method]), depth: 0);
        candidates.Add(new FieldResult(Field(), owner, []), depth: 1);

        var group = Assert.IsType<MethodGroupResult>(candidates.Resolve());

        Assert.Same(method, Assert.Single(group.Methods));
    }

    [Fact]
    public void AMethodGroupAndAFieldAtTheSameNearestDepthAreAmbiguous()
    {
        var owner = HandBuiltModel.Class("Owner");
        var method = Method(owner, HandBuiltModel.Class("Arg"));
        var field = new FieldResult(Field(), owner, []);
        var candidates = new CandidateSet();
        candidates.Add(new MethodGroupResult([method]), depth: 1);
        candidates.Add(field, depth: 1);
        candidates.Add(new PropertyResult(new Property("Foo", ErrorType.Instance, DeclarationFragments.Public), owner, []), depth: 2);

        var ambiguous = Assert.IsType<AmbiguousResult>(candidates.Resolve());

        Assert.Equal(2, ambiguous.Candidates.Count);
        Assert.Contains(field, ambiguous.Candidates);
        Assert.Same(method, Assert.Single(Assert.Single(ambiguous.Candidates.OfType<MethodGroupResult>()).Methods));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void ASameSignatureMergeKeepsTheInheritedClassMethodUnlessEitherSideIsAbstract(
        bool nearerIsAbstract, bool fartherIsAbstract, bool nearerStands)
    {
        var built = new HandBuiltModel();
        var argument = HandBuiltModel.Class("Arg");
        var nearer = Method(Owner("Near", nearerIsAbstract), argument);
        var farther = Method(Owner("Far", fartherIsAbstract), argument);
        var generic = HandBuiltModel.Class("Far<>");
        generic.TypeParametersList.Add(new TypeParameter("T", 0, generic.Scope, generic));
        var path = new[] { built.Interner.GetOrCreateConstructedType(generic, [built.Type("Int32")]) };
        var candidates = new CandidateSet();
        candidates.Add(new MethodGroupResult([nearer]), depth: 0);
        candidates.Add(new MethodGroupResult([farther], new Dictionary<Method, IReadOnlyList<ConstructedType>> { [farther] = path }), depth: 1);

        var group = Assert.IsType<MethodGroupResult>(candidates.Resolve());

        Assert.Same(nearerStands ? nearer : farther, Assert.Single(group.Methods));
        if(nearerStands)
        {
            Assert.Null(group.Paths);
        }
        else
        {
            Assert.Same(path, Assert.Single(group.Paths!).Value);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DistinctOrUnresolvedSignaturesOfOneArityAreBothKept(bool unresolved)
    {
        var owner = HandBuiltModel.Class("Owner");
        IMistType nearerParameter = unresolved ? ErrorType.Instance : HandBuiltModel.Class("Arg");
        IMistType fartherParameter = unresolved ? ErrorType.Instance : HandBuiltModel.Class("Other");
        var nearer = Method(owner, nearerParameter);
        var farther = Method(owner, fartherParameter);
        var candidates = new CandidateSet();
        candidates.Add(new MethodGroupResult([nearer]), depth: 0);
        candidates.Add(new MethodGroupResult([farther]), depth: 1);

        var group = Assert.IsType<MethodGroupResult>(candidates.Resolve());

        Assert.Equal(2, group.Methods.Count);
        Assert.Contains(nearer, group.Methods);
        Assert.Contains(farther, group.Methods);
    }

    private static MemberType Owner(string name, bool isAbstract)
    {
        return isAbstract
            ? new AbstractType(name, "Probe", new Scope(null), null, null)
            : HandBuiltModel.Class(name);
    }

    private static Method Method(MemberType declaringType, IMistType parameterType)
    {
        return new Method("Foo", ErrorType.Instance, [new Parameter("value", parameterType)], DeclarationFragments.Public)
        {
            DeclaringType = declaringType,
        };
    }

    private static Field Field()
    {
        return new Field("Foo", ErrorType.Instance, OwnershipKind.Unrestricted, DeclarationFragments.Public);
    }
}