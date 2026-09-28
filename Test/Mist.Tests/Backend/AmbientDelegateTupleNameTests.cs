using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class DelegateTupleNameCursorTests
{
    [Fact]
    public void NameCountIsCheckedAgainstTheUnsubstitutedType()
    {
        var parameter = typeof(EchoTuple<>).GetGenericArguments()[0];
        var names = TupleElementNames.OfNames(["X", "Y"], parameter);

        Assert.Null(names);
        Assert.NotNull(TupleElementNames.OfNames(["X", "Y"], typeof((int, int))));
        Assert.Null(TupleElementNames.OfNames(["X"], typeof((int, int))));
    }

    [Fact]
    public void LongTupleSlotsIncludeTheRestAndLeaveTheFollowingSibling()
    {
        var type = typeof((EchoTuple<(int, int, int, int, int, int, int, int)>, (int, int)));
        var names = TupleElementNames.OfNames(
            ["Handler", "Window", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", null, "Low", "High"],
            type);

        Assert.NotNull(names);
        Assert.Collection(names.Take(2),
            name => Assert.Equal("Handler", name),
            name => Assert.Equal("Window", name));
        names.Skip(9);
        Assert.Collection(names.Take(2),
            name => Assert.Equal("Low", name),
            name => Assert.Equal("High", name));
    }
}

[Trait("Tier", "2")]
public sealed class AmbientDelegateTupleNameTests
{
    private readonly Scope scope = new(null);
    private readonly TypeInterner interner = new();
    private readonly AmbientTypeResolver resolver;

    public AmbientDelegateTupleNameTests()
    {
        var wellKnown = new Dictionary<string, IMistType>
        {
            ["System.Int32"] = new StructType("Int32", "Standard", null, null, null),
            ["System.Boolean"] = new StructType("Boolean", "Standard", null, null, null),
            ["System.Void"] = new StructType("Void", "Standard", null, null, null),
            ["System.String"] = new ClassType("String", "Standard", null, null, null),
        };
        wellKnown["System.Array"] = new ClassType("Array<,>", "Standard", null, null, null);
        wellKnown["System.Func`2"] = new ClassType("Func<,>", "Standard", null, null, null);
        var list = new ClassType("List<>", "System.Collections.Generic", null, null, null);
        resolver = new AmbientTypeResolver(
            new Dictionary<string, DeclaredType> { ["System.Collections.Generic.List`1"] = list },
            wellKnown, interner, new DiagnosticBag());
    }

    [Fact]
    public void DeclarationNamesDecorateParametersAndResultAtEachDepth()
    {
        var function = Assert.IsType<FunctionType>(resolver.Resolve(typeof(NamedSignature), scope));
        var point = Named(function.ParameterTypes[0], "X", "Y");
        var nested = Named(function.ParameterTypes[1], null, "Last");
        _ = Named(nested.ElementTypes[0], "Inner", null);
        var result = Named(function.ReturnType, "Pair", "Tail");
        _ = Named(result.ElementTypes[0], "Left", "Right");
        _ = Named(result.ElementTypes[1], null, "Label");

        Assert.Contains(point.Fields, field => field.Name == "Item1");
        Assert.Contains(point.Fields, field => field.Name == "X");
        Assert.Equal(2, point.Underlying.ElementTypes.Count);
    }

    [Fact]
    public void DeclarationNamesFlattenLongTuplesInParametersAndResults()
    {
        var function = Assert.IsType<FunctionType>(resolver.Resolve(typeof(LongNamedSignature), scope));

        _ = Named(Assert.Single(function.ParameterTypes), "A", "B", "C", "D", "E", "F", "G", "H");
        _ = Named(function.ReturnType, "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight");
    }

    [Fact]
    public void UseSiteNamesSubstituteRepeatedAndResultParametersWithoutCrossContamination()
    {
        var first = Project(nameof(DelegateTupleNameUses.EchoOne));
        var second = Project(nameof(DelegateTupleNameUses.EchoTwo));
        var firstParameter = Named(first.ParameterTypes[0], "X", "Y");
        Assert.Same(firstParameter, first.ParameterTypes[1]);
        Assert.Same(firstParameter, first.ReturnType);
        var nested = Named(first.ParameterTypes[2], "Value", "Edge");
        Assert.Same(firstParameter, nested.ElementTypes[0]);
        _ = Named(nested.ElementTypes[1], "Fixed", null);

        var secondParameter = Named(second.ParameterTypes[0], "Up", "Down");
        Assert.Same(secondParameter, second.ReturnType);
        Assert.Same(firstParameter.Underlying, secondParameter.Underlying);
        Assert.NotSame(first, second);
        Assert.DoesNotContain(secondParameter.Fields, field => field.Name == "X");
        Assert.Contains(secondParameter.Fields, field => field.Name == "Item2");
    }

    [Fact]
    public void DelegateParametersWinOverSameNamedParametersInTheCallerScope()
    {
        var method = typeof(DelegateTupleNameUses).GetMethod(nameof(DelegateTupleNameUses.EchoOne))!;
        var function = Assert.IsType<FunctionType>(resolver.Resolve(
            method.ReturnType, new ShadowingScope(),
            TupleElementNames.Of(method.ReturnParameter, method.ReturnType)));

        _ = Named(function.ParameterTypes[0], "X", "Y");
    }

    [Fact]
    public void DistinctGenericParametersUseTheirOwnArgumentPositions()
    {
        var function = Project(nameof(DelegateTupleNameUses.Convert));
        var input = Named(function.ParameterTypes[0], "First", null);
        Assert.Same(input, function.ParameterTypes[1]);
        var result = Named(function.ReturnType, null, "Last");
        _ = Named(result.ElementTypes[0], "Inner", null);
        Assert.NotSame(input, result);
    }

    [Fact]
    public void UseSiteNamesPassThroughNestedGenericAndArrayShapes()
    {
        var function = Project(nameof(DelegateTupleNameUses.Deep));
        var input = Assert.IsType<ConstructedType>(Assert.Single(function.ParameterTypes));
        var array = Assert.IsType<ConstructedType>(Assert.Single(input.TypeArguments));
        var tuple = Named(array.TypeArguments[0], "X", "Y");
        var output = Assert.IsType<ConstructedType>(function.ReturnType);
        var outputArray = Assert.IsType<ConstructedType>(Assert.Single(output.TypeArguments));

        Assert.Same(tuple, outputArray.TypeArguments[0]);
        Assert.Same(input, output);
    }

    [Fact]
    public void AnUnavailableNestedSignatureTypeStillExcludesTheDelegate()
    {
        resolver.MarkUnavailable(["System.Collections.Generic.List`1"]);

        _ = Assert.IsType<ErrorType>(ResolveResult(
            typeof(DelegateTupleNameUses).GetMethod(nameof(DelegateTupleNameUses.Deep))!));
    }

    [Fact]
    public void LongUseSiteTupleKeepsItsNamesAndTheFollowingSiblingCursor()
    {
        var method = typeof(DelegateTupleNameUses).GetMethod(nameof(DelegateTupleNameUses.LongWithSibling))!;
        var tuple = Assert.IsType<NamedTupleType>(ResolveResult(method));
        var function = Assert.IsType<FunctionType>(tuple.ElementTypes[0]);
        var argument = Named(function.ParameterTypes[0], "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight");

        Assert.Same(argument, function.ReturnType);
        Assert.Equal(8, argument.Underlying.ElementTypes.Count);
        _ = Named(tuple.ElementTypes[1], "Low", "High");
    }

    [Fact]
    public void FrameworkPredicateUsesItsGenericArgumentNamesAndLeavesSiblingAligned()
    {
        var method = typeof(DelegateTupleNameUses).GetMethod(nameof(DelegateTupleNameUses.PredicateWithSibling))!;
        var tuple = Assert.IsType<NamedTupleType>(ResolveResult(method));
        var function = Assert.IsType<FunctionType>(tuple.ElementTypes[0]);

        _ = Named(Assert.Single(function.ParameterTypes), "X", "Y");
        _ = Named(tuple.ElementTypes[1], "Low", "High");
    }

    [Fact]
    public void UnnamedAndAbsentMetadataRemainStructuralAndResultNullabilityIsPreserved()
    {
        var plain = Assert.IsType<FunctionType>(resolver.Resolve(typeof(UnnamedSignature), scope));
        _ = Assert.IsType<TupleType>(Assert.Single(plain.ParameterTypes));
        _ = Assert.IsType<TupleType>(plain.ReturnType);

        var optional = Assert.IsType<FunctionType>(resolver.Resolve(typeof(OptionalSignature), scope));
        var required = Assert.IsType<FunctionType>(resolver.Resolve(typeof(RequiredSignature), scope));
        _ = Assert.IsType<UnionType>(optional.ReturnType);
        _ = Assert.IsType<ClassType>(required.ReturnType);

        var standardFunc = Assert.IsType<ConstructedType>(resolver.Resolve(typeof(Func<(int, int), bool>), scope));
        _ = Assert.IsType<TupleType>(standardFunc.TypeArguments[0]);
    }

    private FunctionType Project(string methodName)
    {
        var method = typeof(DelegateTupleNameUses).GetMethod(methodName)!;
        return Assert.IsType<FunctionType>(ResolveResult(method));
    }

    private MistType ResolveResult(System.Reflection.MethodInfo method)
    {
        return resolver.Resolve(
            method.ReturnType, scope, TupleElementNames.Of(method.ReturnParameter, method.ReturnType));
    }

    private static NamedTupleType Named(IMistType type, params string?[] names)
    {
        var tuple = Assert.IsType<NamedTupleType>(type);
        Assert.Equal(names, tuple.ElementNames);
        return tuple;
    }

    private sealed class ShadowingScope : Scope
    {
        public ShadowingScope() : base(null)
        {
        }

        protected override LookupResult LookupSelf(LookupRequest request)
        {
            return request.Name == "T"
                ? new TypeResult(new ClassType("Wrong", "Test", null, null, null))
                : base.LookupSelf(request);
        }
    }
}