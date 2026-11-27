using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class CandidateSetOverloadPathTests
{
    [Fact]
    public void DistinctInheritedOverloadsKeepTheirOrderedConstructionPaths()
    {
        var baseType = HandBuiltModel.Class("Base<>");
        var baseValue = new TypeParameter("TBase", 0, baseType.Scope, baseType);
        baseType.TypeParametersList.Add(baseValue);

        var middle = HandBuiltModel.Class("Middle<,>");
        var middleValue = new TypeParameter("TMiddle", 0, middle.Scope, middle);
        var forwardedValue = new TypeParameter("TForwarded", 1, middle.Scope, middle);
        middle.TypeParametersList.AddRange([middleValue, forwardedValue]);
        var baseConstruction = new ConstructedType(baseType, [forwardedValue]);
        middle.BaseType = baseConstruction;

        var reading = HandBuiltModel.Class("Reading");
        var label = HandBuiltModel.Class("Label");
        var middleConstruction = new ConstructedType(middle, [reading, label]);
        var leaf = HandBuiltModel.Class("Leaf", middleConstruction);

        var own = Map(leaf, HandBuiltModel.Class("LocalValue"));
        var nearer = Map(middle, middleValue);
        var farther = Map(baseType, baseValue);
        var candidates = new CandidateSet();
        candidates.Add(new MethodGroupResult([own]), depth: 0);
        candidates.Add(new MethodGroupResult([nearer], new Dictionary<Method, IReadOnlyList<ConstructedType>>
        {
            [nearer] = [middleConstruction],
        }), depth: 1);
        candidates.Add(new MethodGroupResult([farther], new Dictionary<Method, IReadOnlyList<ConstructedType>>
        {
            [farther] = [middleConstruction, baseConstruction],
        }), depth: 2);

        var group = Assert.IsType<MethodGroupResult>(candidates.Resolve());

        Assert.Equal(3, group.Methods.Count);
        Assert.Contains(own, group.Methods);
        Assert.Contains(nearer, group.Methods);
        Assert.Contains(farther, group.Methods);
        var paths = group.Paths;
        Assert.NotNull(paths);
        Assert.Equal<ConstructedType>([middleConstruction], paths[nearer]);
        Assert.Equal<ConstructedType>([middleConstruction, baseConstruction], paths[farther]);
        Assert.DoesNotContain(own, paths.Keys);
    }

    private static Method Map(MemberType owner, IMistType type)
    {
        return new Method("Map", type, [new Parameter("value", type)], DeclarationFragments.Public)
        {
            DeclaringType = owner,
        };
    }
}