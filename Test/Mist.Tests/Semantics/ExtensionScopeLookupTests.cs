using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class ExtensionScopeLookupTests
{
    [Theory]
    [InlineData("InstanceAndStatic", true)]
    [InlineData("InstanceOnly", true)]
    [InlineData("StaticOnly", false)]
    [InlineData("None", false)]
    public void TheReceiverAnswersOnlyARequestThatAdmitsInstanceState(string visibility, bool answers)
    {
        var block = Block(receiverName: "self");

        var found = Lookup(block, "self", visibility);

        if(answers)
        {
            Assert.Same(block.Receiver, Assert.IsType<VariableResult>(found).Variable);
        }
        else
        {
            _ = Assert.IsType<NotFoundResult>(found);
        }
    }

    [Theory]
    [InlineData("InstanceAndStatic")]
    [InlineData("InstanceOnly")]
    [InlineData("StaticOnly")]
    [InlineData("None")]
    public void ABlockTypeParameterAnswersWhateverTheRequestAdmits(string visibility)
    {
        var block = Block(receiverName: "self");
        var parameter = TypeParameter(block, "T");

        var found = Lookup(block, "T", visibility);

        Assert.Same(parameter, Assert.IsType<TypeResult>(found).Type);
    }

    [Theory]
    [InlineData("InstanceAndStatic", new[] { 0, 1, 2 })]
    [InlineData("InstanceOnly", new[] { 0, 1 })]
    [InlineData("StaticOnly", new[] { 2 })]
    [InlineData("None", new int[0])]
    public void SiblingOverloadsAnswerAsOneGroupOfTheOnesTheRequestAdmits(string visibility, int[] expected)
    {
        var block = Block(receiverName: "self");
        Method[] overloads =
        [
            Sibling(block, "Foo", isStatic: false, HandBuiltModel.Class("A")),
            Sibling(block, "Foo", isStatic: false, HandBuiltModel.Class("B")),
            Sibling(block, "Foo", isStatic: true, HandBuiltModel.Class("C")),
        ];
        _ = Sibling(block, "Bar", isStatic: true, HandBuiltModel.Class("A"));

        var found = Lookup(block, "Foo", visibility);

        if(expected.Length == 0)
        {
            _ = Assert.IsType<NotFoundResult>(found);
            return;
        }

        Assert.Equal(expected.Select(index => overloads[index]), Assert.IsType<MethodGroupResult>(found).Methods);
    }

    [Theory]
    [InlineData("InstanceAndStatic", true)]
    [InlineData("StaticOnly", false)]
    public void TheReceiverAndASiblingOfItsNameAreAmbiguousOnlyWhereTheReceiverIsVisible(string visibility, bool ambiguous)
    {
        var block = Block(receiverName: "self");
        var sibling = Sibling(block, "self", isStatic: true, HandBuiltModel.Class("A"));

        var found = Lookup(block, "self", visibility);

        if(ambiguous)
        {
            var candidates = Assert.IsType<AmbiguousResult>(found).Candidates;
            Assert.Equal(2, candidates.Count);
            Assert.Same(block.Receiver, Assert.Single(candidates.OfType<VariableResult>()).Variable);
            Assert.Same(sibling, Assert.Single(Assert.Single(candidates.OfType<MethodGroupResult>()).Methods));
        }
        else
        {
            Assert.Same(sibling, Assert.Single(Assert.IsType<MethodGroupResult>(found).Methods));
        }
    }

    [Theory]
    [InlineData("InstanceAndStatic", true)]
    [InlineData("None", false)]
    public void ATypeParameterAndASiblingOfItsNameAreAmbiguousOnlyWhereMembersAreAdmitted(string visibility, bool ambiguous)
    {
        var block = Block(receiverName: "self");
        var parameter = TypeParameter(block, "T");
        var sibling = Sibling(block, "T", isStatic: true, HandBuiltModel.Class("A"));

        var found = Lookup(block, "T", visibility);

        if(ambiguous)
        {
            var candidates = Assert.IsType<AmbiguousResult>(found).Candidates;
            Assert.Equal(2, candidates.Count);
            Assert.Same(parameter, Assert.Single(candidates.OfType<TypeResult>()).Type);
            Assert.Same(sibling, Assert.Single(Assert.Single(candidates.OfType<MethodGroupResult>()).Methods));
        }
        else
        {
            Assert.Same(parameter, Assert.IsType<TypeResult>(found).Type);
        }
    }

    [Theory]
    [InlineData("InstanceAndStatic", true)]
    [InlineData("StaticOnly", false)]
    public void TheReceiverAndATypeParameterOfItsNameAreAmbiguousOnlyWhereTheReceiverIsVisible(string visibility, bool ambiguous)
    {
        var block = Block(receiverName: "T");
        var parameter = TypeParameter(block, "T");

        var found = Lookup(block, "T", visibility);

        if(ambiguous)
        {
            var candidates = Assert.IsType<AmbiguousResult>(found).Candidates;
            Assert.Equal(2, candidates.Count);
            Assert.Same(block.Receiver, Assert.Single(candidates.OfType<VariableResult>()).Variable);
            Assert.Same(parameter, Assert.Single(candidates.OfType<TypeResult>()).Type);
        }
        else
        {
            Assert.Same(parameter, Assert.IsType<TypeResult>(found).Type);
        }
    }

    private static ExtensionBlock Block(string receiverName)
    {
        var receiverType = HandBuiltModel.Class("Holder");
        return new ExtensionBlock(receiverName, "Probe", new ExtensionScope(null), null)
        {
            ResolvedReceiverType = receiverType,
            Receiver = new LocalVariable(receiverName, receiverType),
        };
    }

    private static TypeParameter TypeParameter(ExtensionBlock block, string name)
    {
        var parameter = new TypeParameter(name, block.TypeParametersList.Count, block.Scope, null);
        block.TypeParametersList.Add(parameter);
        return parameter;
    }

    private static Method Sibling(ExtensionBlock block, string name, bool isStatic, IMistType parameterType)
    {
        var method = new Method(name, ErrorType.Instance, [new Parameter("value", parameterType)],
            DeclarationFragments.Public with { IsStatic = isStatic })
        {
            DeclaringBlock = block,
        };
        block.MethodsList.Add(method);
        return method;
    }

    private static LookupResult Lookup(ExtensionBlock block, string name, string visibility)
    {
        return block.Scope.Lookup(new LookupRequest(name, Enum.Parse<MemberVisibility>(visibility), null));
    }
}