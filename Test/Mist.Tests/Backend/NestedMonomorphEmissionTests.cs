using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class NestedMonomorphEmissionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void MethodSpecializationRetainsOuterPackAndRestoresItsScope(int width)
    {
        var outer = Pack("Ts");
        var inner = Pack("Us");
        var integer = new ClassType("Int32", "Standard", null, null, null);
        var method = new Method("Count", integer,
            [new Parameter("args", new TupleType([new SpreadType(inner)])) { IsFlattened = true }],
            new MemberModifiers { Accessibility = Accessibility.Public })
        {
            Body = new Block(null!)
            {
                Statements = [new ReturnStatement(
                    new MemberAccessExpression(null!, new TypeReferenceExpression(null!, outer), "Length", integer), integer)],
            },
        };
        method.TypeParametersList.Add(inner);
        var enclosing = Monomorph.For([outer], [2])!;
        using var outerScope = enclosing.Enter();
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        using(Monomorph.For([inner], [width])!.Enter())
        {
            MethodEmitter.Emit(method, false, writer, diagnostics);
            Assert.Equal(2, Monomorph.InScope()!.Expansion(outer).Count);
            Assert.Equal(width, Monomorph.InScope()!.Expansion(inner).Count);
            Assert.Empty(Monomorph.InScope()!.Expansion(outer).Select(p => p.Name)
                .Intersect(Monomorph.InScope()!.Expansion(inner).Select(p => p.Name)));
        }

        Assert.Same(enclosing, Monomorph.InScope());
        Assert.Contains("return 2;", writer.ToString());
        Assert.Contains($"Count_{width}", writer.ToString());
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    private static TypeParameter Pack(string name)
    {
        return new TypeParameter(name, 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };
    }
}