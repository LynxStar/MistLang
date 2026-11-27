using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class SpreadPropertyArityEmissionTests
{
    [Fact]
    public void EmptyPackEmitsOnlyTheGetOnlyAggregate()
    {
        Assert.Equal(["public System.ValueTuple S { get; }"], EmitMemberLines(0));
    }

    [Fact]
    public void SingletonPackEmitsOneGetterReadingTheAggregateElement()
    {
        Assert.Equal(
            ["public System.ValueTuple<T1> S { get; }", "public T1 S1 { get => S.Item1; }"],
            EmitMemberLines(1));
    }

    private static string[] EmitMemberLines(int width)
    {
        var owner = new ClassType("Holder<>", "Probe", null, null, null);
        var pack = new TypeParameter("Ts", 0, owner.Scope, owner) { PackKind = TypePackKind.Unbracketed };
        owner.TypeParametersList.Add(pack);
        owner.PropertiesList.Add(new Property("S", new TupleType([new SpreadType(pack)]), new MemberModifiers { Accessibility = Accessibility.Public })
        {
            DeclaringType = owner,
            Getter = new PropertyAccessor { IsAuto = true },
        });

        var monomorph = Monomorph.For(owner.TypeParametersList, [width]);
        Assert.NotNull(monomorph);
        using var specialization = monomorph.Enter();
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        TypeEmitter.EmitTypeBody(owner, new ConstraintClosureMap(), new VariadicUseRegistry(), writer, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray());
        var lines = writer.ToString().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return lines[2..^1];
    }
}