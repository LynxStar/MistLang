using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class CompositeClosingRecordTests
{
    private readonly HandBuiltModel model = new();
    private readonly TypeParameter t = new("T", 0, new Scope(null), null);
    private readonly ClassType argument = HandBuiltModel.Class("Widget");
    private readonly CompositeApplicationRegistry registry = new();

    [Theory]
    [InlineData("union", "Union")]
    [InlineData("tuple", "Tuple")]
    [InlineData("array", "Array")]
    [InlineData("generic", "TypeArgument")]
    [InlineData("nested", "Union")]
    public void AClosedParameterIsRecordedWithTheCompositeItFills(string shape, string kind)
    {
        var text = model.Type("String");
        var open = shape switch
        {
            "union" => model.Interner.GetOrCreateUnionType([t, text]),
            "tuple" => model.Interner.GetOrCreateTupleType([t, text]),
            "array" => model.Interner.GetOrCreateConstructedType(model.Type("Array<,>"), [t, UnknownType.Instance]),
            "generic" => model.Interner.GetOrCreateConstructedType(model.Type("List<>"), [t]),
            _ => model.Interner.GetOrCreateFunctionType([], model.Interner.GetOrCreateUnionType([t, text])),
        };

        registry.RecordClosing(open, [t], [argument], HandBuiltModel.Node(37), null);

        var application = Assert.Single(registry.Applications, each => each.Parts.Contains(argument));
        Assert.Equal(kind, application.Kind.ToString());
        Assert.Equal([argument], application.Parts);
    }

    [Fact]
    public void WhatTheAliasWroteItselfIsLeftToTheAlias()
    {
        var open = model.Interner.GetOrCreateUnionType([model.Type("Int32"), model.Type("String")]);

        registry.RecordClosing(open, [t], [argument], HandBuiltModel.Node(37), null);

        Assert.Empty(registry.Applications);
    }
}