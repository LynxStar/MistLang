using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class StorageProjectionTests
{
    [Fact]
    public void ClosedPackProjectionRetainsItsAggregateAndElement()
    {
        var (bundle, _, aggregate) = Bundle();
        var model = new HandBuiltModel();
        var context = new BuilderContext(model.Model, new DiagnosticBag(), new NullAmbientTypeProvider());
        var receiver = new ConstructedType(bundle,
            [new LiftedType(model.Type("Type"), new TypePackValue([model.Type("Int32"), model.Type("String")]))]);

        var result = Assert.IsType<FieldResult>(SpreadProjection.Find(receiver, bundle, "Item2", context));

        Assert.Same(aggregate, result.Field.StorageSource);
        Assert.Equal(1, result.Field.StorageElementIndex);
        Assert.Same(model.Type("String"), result.Field.Type);
    }

    [Fact]
    public void FixedArityProjectionRetainsItsAggregateAndElement()
    {
        var (bundle, pack, aggregate) = Bundle();
        pack.PackLength = 2;

        var result = Assert.IsType<FieldResult>(TypeMemberLookup.Find(bundle,
            LookupRequest.Member("Item2", MemberVisibility.InstanceOnly, null)));

        Assert.Same(aggregate, result.Field.StorageSource);
        Assert.Equal(1, result.Field.StorageElementIndex);
        Assert.Same(pack.ElementAt(1), result.Field.Type);
    }

    private static (StructType Bundle, TypeParameter Pack, Field Aggregate) Bundle()
    {
        var bundle = HandBuiltModel.Struct("Bundle<>");
        var pack = new TypeParameter("Ts", 0, bundle.Scope, bundle) { PackKind = TypePackKind.Unbracketed };
        bundle.TypeParametersList.Add(pack);
        var aggregate = new Field("Item", new TupleType([new SpreadType(pack)]), default,
            new MemberModifiers { Accessibility = Accessibility.Public });
        bundle.FieldsList.Add(aggregate);
        return (bundle, pack, aggregate);
    }
}