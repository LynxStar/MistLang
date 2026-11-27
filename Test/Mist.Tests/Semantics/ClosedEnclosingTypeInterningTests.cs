using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class ClosedEnclosingTypeInterningTests
{
    [Fact]
    public void EachClosedEnclosingKeepsItsOwnInternedNestedType()
    {
        var scope = new Scope(null);
        var outer = new ClassType("Outer<>", "Probe", scope, null, null);
        outer.TypeParametersList.Add(new TypeParameter("T", 0, outer.Scope, outer));
        var inner = new ClassType("Inner<>", "Probe", outer.Scope, outer, null);
        inner.TypeParametersList.Add(new TypeParameter("U", 0, inner.Scope, inner));

        var firstArgument = new ClassType("First", "Probe", scope, null, null);
        var secondArgument = new ClassType("Second", "Probe", scope, null, null);
        var innerArgument = new ClassType("Value", "Probe", scope, null, null);
        var firstOuter = new ConstructedType(outer, [firstArgument]);
        var secondOuter = new ConstructedType(outer, [secondArgument]);
        var interner = new TypeInterner();

        var firstInner = interner.GetOrCreateConstructedType(inner, [innerArgument], firstOuter);
        var secondInner = interner.GetOrCreateConstructedType(inner, [innerArgument], secondOuter);

        Assert.NotSame(firstInner, secondInner);
        Assert.Same(firstOuter, firstInner.Enclosing);
        Assert.Same(secondOuter, secondInner.Enclosing);
        Assert.Same(firstInner, interner.GetOrCreateConstructedType(inner, [innerArgument], firstOuter));
        Assert.Same(secondInner, interner.GetOrCreateConstructedType(inner, [innerArgument], secondOuter));
    }
}