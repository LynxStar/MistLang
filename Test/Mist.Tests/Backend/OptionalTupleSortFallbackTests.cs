using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class OptionalTupleSortFallbackTests
{
    [Fact]
    public void Order_UsesTheDefaultComparerOnlyForTheOptionalTuple()
    {
        var scope = new Scope(null);
        var list = new ClassType("List<>", "Standard", scope, null, null);
        list.TypeParametersList.Add(new TypeParameter("T", 0, list.Scope, list));

        var text = new ClassType("String", "Standard", scope, null, null);
        var number = new StructType("Int32", "Standard", scope, null, null);
        var absence = new StructType("Void", "Standard", scope, null, null);
        var tuple = new TupleType([text, number]);
        var optionalTuple = new UnionType([tuple, absence]);

        Assert.Equal(
            "MistInvariant.Elements<(string, int)>(System.StringComparer.InvariantCulture, System.Collections.Generic.Comparer<int>.Default)",
            InvariantCulture.Order(new ConstructedType(list, [tuple])));
        Assert.Null(InvariantCulture.Order(new ConstructedType(list, [optionalTuple])));
    }
}