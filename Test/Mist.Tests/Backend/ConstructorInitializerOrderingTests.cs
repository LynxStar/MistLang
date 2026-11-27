using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class ConstructorInitializerOrderingTests
{
    [Fact]
    [Trait("Tier", "1")]
    public void Apply_ConstructsOnceAndWritesEachMemberBeforeReadingTheNextValue()
    {
        var node = HandBuiltModel.Node();
        var integer = new GenericDefaultShapes().Integer;
        var product = new TypeParameter("T", 0, new Scope(null), null);
        product.ConstraintsList.Add(new ConstructorConstraint([integer]));
        var first = new LocalReferenceExpression(node, new LocalVariable("first", integer) { IsRef = true });
        var second = new LocalReferenceExpression(node, new LocalVariable("second", integer) { IsRef = true });
        var allocation = new NewExpression(node, product, [HandBuiltModel.Literal(integer, 7L)], null,
            [new(node, "Width", first, integer), new(node, "Height", second, integer)], [integer]);

        var emitted = ConstructorInitializers.Apply(allocation, expression => expression switch
        {
            NewExpression { Initializer.Count: 0 } => "__construct_T_0(7)",
            _ when ReferenceEquals(expression, first) => "first",
            _ when ReferenceEquals(expression, second) => "second",
            _ => throw new InvalidOperationException("Unexpected initializer render input."),
        });

        Assert.Equal(
            "((System.Func<T, int, T>)((__constructed1, __initialized1) => { __constructed1.Height = __initialized1; return __constructed1; }))("
            + "((System.Func<T, int, T>)((__constructed0, __initialized0) => { __constructed0.Width = __initialized0; return __constructed0; }))("
            + "__construct_T_0(7), first), second)",
            emitted);
    }
}