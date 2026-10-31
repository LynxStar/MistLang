using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Razor;

namespace Mist.Tests.Backend.Razor;

[Trait("Tier", "1")]
public sealed class NestedBaseDecisionTests
{
    public static TheoryData<int, string, bool> ParsingRows
    {
        get
        {
            var rows = new TheoryData<int, string, bool>();
            foreach(var depth in new[] { 1, 2 })
            {
                foreach(var mode in NestedBaseMatrix.Modes)
                {
                    foreach(var genericInner in new[] { false, true })
                    {
                        rows.Add(depth, mode, genericInner);
                    }
                }
            }

            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(ParsingRows))]
    public void ParsingKeepsArgumentsOnTheirContainingSegments(int depth, string mode, bool genericInner)
    {
        var parsed = CatalogTypeText.Parse(NestedBaseMatrix.BaseText(depth, mode, genericInner));
        var generic = mode != "NonGeneric";
        var expectedName = (generic ? "MatrixBox" : "MatrixOuter") + (depth == 2 ? ".Middle" : "") + (genericInner ? ".GenericInner" : ".Inner");
        var outer = generic ? NestedBaseMatrix.Argument(mode) : "";
        var middle = generic ? (mode == "Open" ? "U" : "int") : "";
        var own = genericInner ? (mode == "Open" ? "V" : "bool") : "";

        Assert.Equal(expectedName, parsed.Name);
        Assert.Equal(depth == 1 ? [outer, own] : [outer, middle, own], parsed.Segments.Select(segment => string.Join('|', segment.Arguments)));
        var expectedArguments = new List<string>();
        if(generic)
        {
            expectedArguments.Add(outer);
            if(depth == 2)
            {
                expectedArguments.Add(middle);
            }
        }

        if(genericInner)
        {
            expectedArguments.Add(own);
        }

        Assert.Equal(expectedArguments, parsed.Arguments);
    }

    [Theory]
    [InlineData("V", "T;W", "T|V")]
    [InlineData("T;V", "T;W", "T|V")]
    [InlineData(";string;bool", "T;W", "string|bool")]
    [InlineData("V", "T;U;W", "T|U|V")]
    [InlineData("U;V", "T;U;W", "T|U|V")]
    [InlineData("T;U;V", "T;U;W", "T|U|V")]
    [InlineData("string;int;bool", "T;U;W", "string|int|bool")]
    [InlineData("Dictionary<int, string>;int;bool", "T;U;W", "Dictionary<int, string>|int|bool")]
    [InlineData("Payload.Box<int>.Item<string>;bool", "T;W", "Payload.Box<int>.Item<string>|bool")]
    [InlineData(";V", ";W", "V")]
    [InlineData(";;", ";;", "")]
    [InlineData("string;int", "T;T", "string|int")]
    [InlineData("", "T;U;", "T|U")]
    [InlineData("string;int;", "T;U;", "string|int")]
    [InlineData(";;V", ";;W", "V")]
    public void MistArgumentsKeepImplicitEnclosingSlots(string argumentGroups, string scopeText, string expected)
    {
        var scopes = scopeText.Split(';').Select(scope => scope.Length == 0 ? ImmutableArray<string>.Empty : [.. scope.Split(',')]).ToImmutableArray();

        var segments = argumentGroups.Split(';')
            .Select(group => new CatalogTypeSegment("Type", group.Length == 0 ? [] : [.. group.Split('|')])).ToImmutableArray();
        var reference = new CatalogTypeText("Type", [.. segments.SelectMany(segment => segment.Arguments)], segments);

        var arguments = reference.ArgumentsFor(scopes);

        Assert.Equal(expected, string.Join('|', arguments));
    }

    [Theory]
    [InlineData("T", nameof(ParameterShape.Plain))]
    [InlineData("string", nameof(ParameterShape.String))]
    [InlineData("Dictionary<int, string>", nameof(ParameterShape.Plain))]
    [InlineData("Payload.Box<int>.Item<string>", nameof(ParameterShape.Plain))]
    public void NestedParameterFormsAreSubstitutedBeforeClassification(string argument, string shape)
    {
        var scalar = Enum.Parse<ParameterShape>(shape);
        var parameters = CatalogTypeText.Substitute(
            [
                new ParameterFacts("Value", "T", ParameterShape.Plain),
                new ParameterFacts("Optional", "T?", ParameterShape.Plain),
                new ParameterFacts("Items", "T[]", ParameterShape.Plain),
                new ParameterFacts("Values", "List<T>", ParameterShape.Plain),
                new ParameterFacts("Template", "RenderFragment<T>", ParameterShape.TemplatedFragment),
            ], ["T"], [argument]);

        Assert.Equal<ParameterFacts>(
            [
                new ParameterFacts("Value", argument, scalar),
                new ParameterFacts("Optional", argument + "?", scalar),
                new ParameterFacts("Items", argument + "[]", ParameterShape.Plain),
                new ParameterFacts("Values", "List<" + argument + ">", ParameterShape.Plain),
                new ParameterFacts("Template", "RenderFragment<" + argument + ">", ParameterShape.TemplatedFragment),
            ], parameters);
    }

    public static TheoryData<string, int, Type?> LookupRows => new()
    {
        { "Vendor.MatrixOuter.Inner", 0, typeof(MatrixOuter.Inner) },
        { "Vendor.MatrixOuter.Middle.Inner", 0, typeof(MatrixOuter.Middle.Inner) },
        { "Vendor.MatrixOuter.GenericInner", 1, typeof(MatrixOuter.GenericInner<>) },
        { "Vendor.MatrixOuter.Middle.GenericInner", 1, typeof(MatrixOuter.Middle.GenericInner<>) },
        { "Vendor.MatrixBox.Inner", 1, typeof(MatrixBox<>.Inner) },
        { "Vendor.MatrixBox.GenericInner", 2, typeof(MatrixBox<>.GenericInner<>) },
        { "Vendor.MatrixBox.Middle.Inner", 2, typeof(MatrixBox<>.Middle<>.Inner) },
        { "Vendor.MatrixBox.Middle.GenericInner", 3, typeof(MatrixBox<>.Middle<>.GenericInner<>) },
        { "Vendor.MatrixBox.Middle.GenericInner", 2, null },
        { "Vendor.MatrixBox.Missing.Inner", 3, null },
        { "Other.MatrixBox.Middle.Inner", 3, null },
    };

    [Theory]
    [MemberData(nameof(LookupRows))]
    public void ClrLookupTraversesDepthAndChecksTotalArity(string name, int arity, Type? expected)
    {
        var found = CatalogClrTypeLookup.Find(name, (candidate, _) => candidate switch
        {
            "Vendor.MatrixOuter" => typeof(MatrixOuter),
            "Vendor.MatrixBox" => typeof(MatrixBox<>),
            _ => null,
        }, arity);

        Assert.Equal(expected, found);
    }

    internal sealed class MatrixOuter
    {
        public sealed class Inner;
        public sealed class GenericInner<TInner>;
        public sealed class Middle
        {
            public sealed class Inner;
            public sealed class GenericInner<TInner>;
        }
    }

    internal sealed class MatrixBox<T>
    {
        public sealed class Inner;
        public sealed class GenericInner<TInner>;
        public sealed class Middle<TMiddle>
        {
            public sealed class Inner;
            public sealed class GenericInner<TInner>;
        }
    }
}