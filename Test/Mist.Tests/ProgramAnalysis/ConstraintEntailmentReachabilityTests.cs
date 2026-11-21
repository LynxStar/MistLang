using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class ConstraintEntailmentReachabilityTests
{
    private const string Refused = "MIST0178";

    private readonly HandBuiltModel built = new();

    [Theory]
    [InlineData(0)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(256)]
    public void BaseClassBoundReachesTheRequirementAtAnyDepth(int depth)
    {
        var required = HandBuiltModel.Class("C0");

        Assert.Null(Judge(Chain(required, depth), required));
    }

    [Fact]
    public void DeepBaseClassBoundDoesNotEntailAnUnrelatedRequirement()
    {
        var offered = Chain(HandBuiltModel.Class("C0"), 65);

        Assert.Equal(Refused, Judge(offered, HandBuiltModel.Class("Unrelated")));
    }

    [Fact]
    public void ParameterAndRefinementStepsDoNotLimitBaseClassReach()
    {
        var required = HandBuiltModel.Class("C0");
        var bounding = Parameter("V");
        bounding.ConstraintsList.Add(new TypeConstraint(HandBuiltModel.Refined(Chain(required, 64), "Narrowed")));

        Assert.Null(Judge(bounding, required));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CyclicClassBoundsTerminateWithoutEntailingAnUnrelatedRequirement(bool mutual)
    {
        var offered = HandBuiltModel.Class("Cycle");
        offered.BaseType = mutual ? HandBuiltModel.Class("Other", offered) : offered;

        Assert.Equal(Refused, Judge(offered, HandBuiltModel.Class("Unrelated")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GrowingGenericCycleTerminatesAndPreservesIndependentBounds(bool independentBound)
    {
        var required = Abstract("IRequired");
        var cycle = HandBuiltModel.Class("Cycle");
        var item = DeclarationFragments.Parameter(cycle, cycle.Scope, "Item");
        var wrapper = HandBuiltModel.Class("Wrapper");
        _ = DeclarationFragments.Parameter(wrapper, wrapper.Scope, "Item");
        cycle.BaseType = new ConstructedType(cycle, [new ConstructedType(wrapper, [item])]);
        var forwarded = Parameter("U");

        if(independentBound)
        {
            forwarded.ConstraintsList.Add(new TypeConstraint(required));
        }

        forwarded.ConstraintsList.Add(new TypeConstraint(new ConstructedType(cycle, [built.Type("Int32")])));

        Assert.Equal(independentBound ? null : Refused, Decide(forwarded, required));
    }

    [Fact]
    public void SeparateInheritancePathsCanReachDifferentConstructionsOfTheSameDeclaration()
    {
        var target = Abstract("ITarget");
        _ = DeclarationFragments.Parameter(target, target.Scope, "Item");
        var branch = Abstract("IBranch");
        var item = DeclarationFragments.Parameter(branch, branch.Scope, "Item");
        branch.BaseAbstractTypes.Add(new ConstructedType(target, [item]));
        var offered = HandBuiltModel.Class("Offered");
        offered.BaseAbstractTypes.Add(new ConstructedType(branch, [built.Type("Int32")]));
        offered.BaseAbstractTypes.Add(new ConstructedType(branch, [built.Type("String")]));

        Assert.Null(Judge(offered, new ConstructedType(target, [built.Type("Int32")])));
    }

    private string? Judge(IMistType offered, IMistType required)
    {
        var forwarded = Parameter("U");
        forwarded.ConstraintsList.Add(new TypeConstraint(offered));

        return Decide(forwarded, required);
    }

    private string? Decide(TypeParameter forwarded, IMistType required)
    {
        var parameter = Parameter("T");
        var closing = new TypeArgumentClosing(parameter, forwarded, HandBuiltModel.Node(), TypeSubstitution.Empty.With(parameter, forwarded));

        return ConstraintEntailment.Decide(closing, new TypeConstraint(required), built.Interner)?.Number.Value;
    }

    private static ClassType Chain(ClassType root, int depth)
    {
        var last = root;
        for(var step = 1; step <= depth; step++)
        {
            last = HandBuiltModel.Class($"C{step}", last);
        }

        return last;
    }

    private static TypeParameter Parameter(string name)
    {
        return new TypeParameter(name, 0, new Scope(null), null);
    }

    private static AbstractType Abstract(string name)
    {
        return new AbstractType(name, "Reachability", new Scope(null), null, null);
    }
}