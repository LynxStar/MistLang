using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class CandidateSetMixedDepthOverloadTests
{
    [Fact]
    public void MethodGroupBeatsAnInterveningFieldAndKeepsTheFartherOverload()
    {
        const string name = "Pick";
        var valueType = HandBuiltModel.Class("Value");
        var nearer = new Method(name, valueType, [new Parameter("value", valueType)], DeclarationFragments.Public)
        {
            DeclaringType = HandBuiltModel.Class("Receiver"),
        };
        var farther = new Method(name, valueType,
            [new Parameter("first", valueType), new Parameter("second", valueType)], DeclarationFragments.Public)
        {
            DeclaringType = HandBuiltModel.Class("Ancestor"),
        };
        var field = new Field(name, valueType, OwnershipKind.Unrestricted, DeclarationFragments.Public);
        var candidates = new CandidateSet();
        candidates.Add(new MethodGroupResult([nearer]), depth: 0);
        candidates.Add(new FieldResult(field, HandBuiltModel.Class("Intermediate"), []), depth: 1);
        candidates.Add(new MethodGroupResult([farther]), depth: 2);

        var group = Assert.IsType<MethodGroupResult>(candidates.Resolve());

        Assert.Equal(2, group.Methods.Count);
        Assert.Contains(group.Methods, method => ReferenceEquals(method, nearer));
        Assert.Contains(group.Methods, method => ReferenceEquals(method, farther));
    }
}