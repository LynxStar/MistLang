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
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void OuterPackTypeValuesCannotBeShadowedByDeclaredMethodParameters(int width, bool nested)
    {
        var outer = Pack("Ts");
        var inner = Pack("Us");
        var type = new ClassType("Box", "Probe", null, null, null);
        type.TypeParametersList.Add(outer);
        var owner = type;
        if(nested)
        {
            owner = new ClassType("Inner", "Probe", type.Scope, type, null);
            owner.TypeParametersList.Add(new TypeParameter("T2", 0, owner.Scope, owner));
            type.NestedTypes.Add(owner);
        }

        var first = new TypeParameter("T1", 0, new Scope(null), null);
        var last = new TypeParameter("T3", 2, new Scope(null), null);
        var runtimeType = new ClassType("Type", "Standard", null, null, null);
        var result = SizedSequenceTypes.ArrayOf(runtimeType, 2);
        var method = new Method("Types", result,
            [new Parameter("first", first),
                new Parameter("args", new TupleType([new SpreadType(inner)])) { IsFlattened = true },
                new Parameter("last", last)], default)
        {
            DeclaringType = owner,
            Body = new Block(null!)
            {
                Statements = [new ReturnStatement(new TypeReferenceExpression(null!, outer), result)],
            },
        };
        method.TypeParametersList.AddRange([first, inner, last]);
        owner.MethodsList.Add(method);
        var enclosing = MonomorphSet.TypeSpecialization(type, [2])!;
        using var outerScope = enclosing.Enter();
        var specialized = Monomorph.For(method.TypeParametersList, [width])!;
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        using(specialized.Enter())
        {
            MethodEmitter.Emit(method, false, writer, diagnostics);
        }

        var outerNames = enclosing.Expansion(outer).Select(parameter => parameter.Name).ToArray();
        var methodParameters = specialized.Parameters(method.TypeParametersList);
        Assert.Empty(outerNames.Intersect(methodParameters.Concat(owner.TypeParametersList)
            .Select(parameter => parameter.Name)));
        Assert.Same(first, methodParameters[0]);
        Assert.Same(last, methodParameters[^1]);
        Assert.Contains($"Types_{width}<{string.Join(", ", methodParameters.Select(parameter => parameter.Name))}>", writer.ToString());
        Assert.Contains($"return new System.Type[] {{ typeof({outerNames[0]}), typeof({outerNames[1]}) }};", writer.ToString());
        Assert.Empty(diagnostics.ToImmutableArray());
    }

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