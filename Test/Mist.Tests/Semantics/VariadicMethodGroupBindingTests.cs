using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class VariadicMethodGroupBindingTests
{
    [Fact]
    public void SettleRecordsTheWidthFixedByTheTarget()
    {
        var f = new BindingFragments();
        var method = VariadicMethodGroupClosingTests.Describe(f.Built);
        var group = VariadicMethodGroupClosingTests.Group(method);
        var target = new FunctionType([f.Built.Type("Int32"), f.Built.Type("String")], f.Built.Type("Int32"));

        var settled = MethodGroupConversion.Settle([group], [new Parameter("f", target)], TypeSubstitution.Empty, f.Builder);

        Assert.Same(method, Assert.IsType<FunctionReferenceExpression>(Assert.Single(settled)).Method);
        Assert.Equal(new[] { 2 }, Assert.Single(f.Built.Model.VariadicUses.WidthsOf(method)));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void InferredCallLearnsItsResultFromTheVariadicGroupAndSettlesIt()
    {
        var f = new BindingFragments();
        var apply = f.Function("Apply", f.Built.Type("Int32"));
        var t = BindingFragments.Parameter(apply, "T");
        var r = BindingFragments.Parameter(apply, "R");
        apply.ParametersList.AddRange([new Parameter("value", t), new Parameter("f", new FunctionType([t], r))]);
        var describe = VariadicMethodGroupClosingTests.Describe(f.Built);
        var arguments = new Expression[] { HandBuiltModel.Literal(f.Built.Type("Int32"), 4), VariadicMethodGroupClosingTests.Group(describe) };

        var choice = OverloadResolution.Select([apply], arguments, null, null, f.Built.Interner);

        Assert.Same(apply, choice.Selected);
        Assert.Same(f.Built.Type("Int32"), choice.Substitution.Apply(r, f.Built.Interner));
        var settled = MethodGroupConversion.Settle(arguments, apply.ParametersList, choice.Substitution, f.Builder);
        Assert.Same(describe, Assert.IsType<FunctionReferenceExpression>(settled[1]).Method);
        Assert.Equal(new[] { 1 }, Assert.Single(f.Built.Model.VariadicUses.WidthsOf(describe)));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }
}