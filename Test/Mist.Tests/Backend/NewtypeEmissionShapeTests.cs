using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.GenericDefaultShapes;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class NewtypeEmissionShapeTests
{
    private const string SequentialLayout =
        "[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]";

    private readonly GenericDefaultShapes shapes = new();

    [Fact]
    public void AnIntrinsicSource_IsAReadonlyStructHoldingItsValueWithExplicitCastsBothWays()
    {
        var lines = Lines(Emit(Newtype("Meters", shapes.Integer)));

        Assert.Equal("public readonly struct Meters", lines[0]);
        Assert.Contains("public readonly int Value;", lines);
        Assert.Contains("public Meters(int value)", lines);
        Assert.Contains(lines, line => line.StartsWith("public static explicit operator Meters(int value)", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("public static explicit operator int(Meters wrapped)", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("implicit", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, "public struct Spot")]
    [InlineData(true, "public readonly struct Spot")]
    public void AStructSource_IsDuplicatedUnderSequentialLayout_ReadonlyOnlyWhenItsSourceIs(bool readonlySource, string header)
    {
        var lines = Lines(Emit(Newtype("Spot", Point(readonlySource))));

        Assert.Equal(SequentialLayout, lines[0]);
        Assert.Equal(header, lines[1]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASourcesMembers_AreDuplicated_WithEveryConstructorRenamedToTheNewtype(bool structSource)
    {
        var text = Emit(Newtype("Spot", structSource ? Point() : Node()));

        Assert.Contains("public int X;", text, StringComparison.Ordinal);
        Assert.Contains("public Spot(int p0)", text, StringComparison.Ordinal);
        Assert.Contains("public Spot(int p0, int p1)", text, StringComparison.Ordinal);
        Assert.DoesNotContain(structSource ? "Point(" : "Node(", text, StringComparison.Ordinal);
        Assert.Contains("public int Size { get; set; }", text, StringComparison.Ordinal);
        Assert.Contains("public int this[int position]", text, StringComparison.Ordinal);
        Assert.Contains("public int Twice()", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AClassSource_IsAPlainClassWithNoLayoutAttribute(bool readonlySource)
    {
        var lines = Lines(Emit(Newtype("Spot", Node(readonlySource))));

        Assert.Equal("public class Spot", lines[0]);
        Assert.DoesNotContain(lines, line => line.Contains("StructLayout", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASourcesCastOperators_AreNotDuplicated(bool structSource)
    {
        var source = structSource ? Point() : Node();
        _ = HandBuiltModel.Conversion(source, source, shapes.Integer);
        _ = HandBuiltModel.Conversion(source, shapes.Integer, source, isImplicit: false);

        Assert.DoesNotContain("operator", Emit(Newtype("Spot", source)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Internal", "Public", "internal ")]
    [InlineData("Public", "Internal", "public ")]
    [InlineData("Private", "Public", "private ")]
    public void TheEmittedAccessibility_IsTheNewtypesOwnNotItsSources(string newtypeAccess, string sourceAccess, string expected)
    {
        var access = Enum.Parse<Accessibility>(newtypeAccess);
        var point = Point();
        point.Modifiers = point.Modifiers with { Accessibility = Enum.Parse<Accessibility>(sourceAccess) };
        var integer = shapes.Integer;
        integer.Modifiers = integer.Modifiers with { Accessibility = Enum.Parse<Accessibility>(sourceAccess) };

        Assert.Contains($"{expected}struct Spot", Lines(Emit(Newtype("Spot", point, access))));
        Assert.Equal($"{expected}readonly struct Meters", Lines(Emit(Newtype("Meters", integer, access)))[0]);
    }

    private ConcreteType Point(bool isReadonly = false)
    {
        return Members(shapes.Type("Point", isStruct: true), isReadonly);
    }

    private ConcreteType Node(bool isReadonly = false)
    {
        return Members(shapes.Type("Node"), isReadonly);
    }

    private ConcreteType Members(ConcreteType source, bool isReadonly)
    {
        var integer = shapes.Integer;
        var visible = new MemberModifiers { Accessibility = Accessibility.Public };
        source.Modifiers = source.Modifiers with { IsReadonly = isReadonly };
        _ = Slot(source, "X", integer);
        _ = Constructor(source, integer);
        _ = Constructor(source, integer, integer);
        source.PropertiesList.Add(new Property("Size", integer, visible)
        {
            DeclaringType = source,
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = new PropertyAccessor { IsAuto = true },
        });
        source.IndexersList.Add(new Indexer(integer, [new Parameter("position", integer)], visible)
        {
            DeclaringType = source,
            Getter = new PropertyAccessor { IsAuto = true },
        });
        source.MethodsList.Add(new Method("Twice", integer, [], visible)
        {
            DeclaringType = source,
            DeclarationNode = HandBuiltModel.Node(60),
            Scope = new Scope(source.Scope),
            Body = HandBuiltModel.Block(),
        });
        return source;
    }

    private static Newtype Newtype(string name, IMistType source, Accessibility access = Accessibility.Public)
    {
        var newtype = HandBuiltModel.Newtype(name, source);
        newtype.Modifiers = new MemberModifiers { Accessibility = access };
        return newtype;
    }

    private static string Emit(Newtype newtype)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        NewtypeEmitter.Emit(newtype, writer, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString();
    }

    private static string[] Lines(string text)
    {
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}