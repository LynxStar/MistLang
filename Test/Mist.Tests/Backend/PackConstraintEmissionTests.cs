using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// Tier 1: the where clauses a specialization declares for one pack's elements, from a hand-built pack.
// C# has only per-parameter constraints, so a whole-pack clause becomes one per element, and each names
// ITS element where the clause names the pack (variadics 11.1): `where Ts : IRank<Ts>` at width two is
// `where T1 : IRank<T1>` and `where T2 : IRank<T2>`. A clause still naming the pack names a type
// parameter C# has nowhere in scope.
[Trait("Tier", "1")]
public sealed class PackConstraintEmissionTests
{
    private readonly AbstractType rank = Generic(new AbstractType("IRank", "Probe", new Scope(null), null, null));

    private readonly AbstractType keyed = Generic(new AbstractType("IKeyed", "Probe", new Scope(null), null, null));

    private readonly AbstractType named = new("INamed", "Probe", new Scope(null), null, null);

    private readonly ClassType box = Generic(HandBuiltModel.Class("Box"));

    private readonly TypeParameter key = new("K", 0, new Scope(null), null);

    private readonly TypeParameter pack = new("Ts", 1, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };

    [Theory]
    [InlineData("self", "where T1 : Probe.IRank<T1>", "where T2 : Probe.IRank<T2>")]
    [InlineData("nested", "where T1 : Probe.IRank<Probe.Box<T1>>", "where T2 : Probe.IRank<Probe.Box<T2>>")]
    [InlineData("spread", "where T1 : Probe.IRank<(T1, T2)>", "where T2 : Probe.IRank<(T1, T2)>")]
    [InlineData("sibling", "where T1 : Probe.IKeyed<K>", "where T2 : Probe.IKeyed<K>")]
    [InlineData("several", "where T1 : class, Probe.IRank<T1>, INamed, Probe.IKeyed<K>", "where T2 : class, Probe.IRank<T2>, INamed, Probe.IKeyed<K>")]
    public void EachElementsClause_NamesThatElementWhereTheClauseNamesThePack(string shape, string first, string second)
    {
        Constrain(shape);

        Assert.Equal([first, second], Clauses(2));
    }

    [Fact]
    public void AnEmptyWidth_DeclaresNoClause()
    {
        Constrain("self");

        Assert.Empty(Clauses(0));
    }

    [Fact]
    public void ASecondPack_NamesItsOwnElements()
    {
        var other = new TypeParameter("Us", 2, new Scope(null), null) { PackKind = TypePackKind.Bracketed };
        Constrain("self");
        other.ConstraintsList.Add(new TypeConstraint(new ConstructedType(rank, [other])));

        var monomorph = Monomorph.For([key, pack, other], [1, 2])!;

        Assert.Equal(
            ["where T1 : Probe.IRank<T1>", "where T2 : Probe.IRank<T2>", "where T3 : Probe.IRank<T3>"],
            Clauses(monomorph, [key, pack, other]));
    }

    [Fact]
    public void ThePacksOwnClause_IsLeftAsTheModelWroteIt()
    {
        Constrain("self");
        var declared = pack.ConstraintsList.Single();

        _ = Clauses(2);

        var kept = Assert.IsType<TypeConstraint>(Assert.Single(pack.ConstraintsList));
        Assert.Same(declared, kept);
        Assert.Same(pack, Assert.IsType<ConstructedType>(kept.ConstraintType).TypeArguments[0]);
    }

    private void Constrain(string shape)
    {
        switch(shape)
        {
            case "self":
                pack.ConstraintsList.Add(new TypeConstraint(new ConstructedType(rank, [pack])));
                break;

            case "nested":
                pack.ConstraintsList.Add(new TypeConstraint(new ConstructedType(rank, [new ConstructedType(box, [pack])])));
                break;

            case "spread":
                pack.ConstraintsList.Add(new TypeConstraint(new ConstructedType(rank, [new TupleType([new SpreadType(pack)])])));
                break;

            case "sibling":
                pack.ConstraintsList.Add(new TypeConstraint(new ConstructedType(keyed, [key])));
                break;

            case "several":
                pack.ConstraintsList.Add(new TypeConstraint(new ConstructedType(rank, [pack])));
                pack.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Class));
                pack.ConstraintsList.Add(new TypeConstraint(named));
                pack.ConstraintsList.Add(new TypeConstraint(new ConstructedType(keyed, [key])));
                break;
        }
    }

    private List<string> Clauses(int width)
    {
        return Clauses(Monomorph.For([key, pack], [width])!, [key, pack]);
    }

    private static List<string> Clauses(Monomorph monomorph, IReadOnlyList<TypeParameter> declared)
    {
        var writer = new CSharpWriter();

        using(monomorph.Enter())
        {
            ConstraintEmitter.Emit(monomorph.Parameters(declared), writer);
        }

        return [.. writer.ToString().Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)];
    }

    private static T Generic<T>(T type)
        where T : MemberType
    {
        _ = DeclarationFragments.Parameter(type, type.Scope, "Item");
        return type;
    }
}