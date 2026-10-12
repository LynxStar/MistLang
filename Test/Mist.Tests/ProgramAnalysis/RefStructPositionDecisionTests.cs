using Mist.Generated;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class RefStructPositionDecisionTests
{
    private readonly HandBuiltModel model = new();
    private readonly StructType cursor = new("Cursor", "Probe", new Scope(null), null, null) { Modifiers = new MemberModifiers { IsRef = true } };

    [Theory]
    [InlineData("direct")]
    [InlineData("constructed")]
    [InlineData("refined")]
    [InlineData("aliased")]
    public void ARefStructIsSeenHoweverItIsNamed(string shape)
    {
        IMistType type = shape switch
        {
            "direct" => cursor,
            "constructed" => model.Interner.GetOrCreateConstructedType(GenericCursor(), [model.Type("Int32")]),
            "refined" => HandBuiltModel.Refined(cursor, "Positive"),
            _ => new TypeAlias("Buffer", "Probe", new Scope(null), null, new TypeAliasDeclarationNode()) { AliasedType = cursor },
        };

        Assert.True(RefStructs.Is(type));
    }

    [Fact]
    public void APlainStructIsNotARefStruct()
    {
        Assert.False(RefStructs.Is(HandBuiltModel.Struct("Point")));
    }

    [Theory]
    [InlineData("class", false, "MIST_PA_REF_STRUCT_FIELD")]
    [InlineData("struct", false, "MIST_PA_REF_STRUCT_FIELD")]
    [InlineData("class", true, "MIST_PA_REF_STRUCT_STATIC_STORAGE")]
    [InlineData("ref-struct", true, "MIST_PA_REF_STRUCT_STATIC_STORAGE")]
    public void AFieldThatWouldHoldARefStructOffTheStackIsRefused(string container, bool isStatic, string expected)
    {
        var field = new Field("Data", cursor, OwnershipKind.Unrestricted, new MemberModifiers { IsStatic = isStatic });

        Assert.Equal(expected, RefStructPositions.Field(Container(container), field)?.Id.Value);
    }

    [Fact]
    public void AnInstanceFieldOfAnotherRefStructIsAdmitted()
    {
        Assert.Null(RefStructPositions.Field(Container("ref-struct"), new Field("Data", cursor, OwnershipKind.Unrestricted, default)));
        Assert.Null(RefStructPositions.Field(Container("class"), new Field("Count", model.Type("Int32"), OwnershipKind.Unrestricted, default)));
    }

    [Theory]
    [InlineData("class", false, false, "MIST_PA_REF_STRUCT_PROPERTY")]
    [InlineData("class", false, true, "MIST_PA_REF_STRUCT_PROPERTY")]
    [InlineData("struct", true, false, "MIST_PA_REF_STRUCT_PROPERTY")]
    [InlineData("ref-struct", true, true, "MIST_PA_REF_STRUCT_STATIC_STORAGE")]
    public void APropertyThatWouldHoldARefStructOffTheStackIsRefused(string container, bool isStatic, bool isAuto, string expected)
    {
        Assert.Equal(expected, RefStructPositions.Property(Container(container), Property(isStatic, isAuto))?.Id.Value);
    }

    [Theory]
    [InlineData("ref-struct", false, true)]
    [InlineData("ref-struct", true, false)]
    [InlineData("abstract", false, false)]
    public void ARefStructPropertyWithNoHeapStorageIsAdmitted(string container, bool isStatic, bool isAuto)
    {
        Assert.Null(RefStructPositions.Property(Container(container), Property(isStatic, isAuto)));
    }

    [Theory]
    [InlineData("Tuple", "a tuple element")]
    [InlineData("Array", "an array element")]
    public void AnElementPositionIsNamedForItsComposite(string kind, string position)
    {
        Assert.Equal(position, RefStructPositions.Element(Enum.Parse<CompositeKind>(kind)).Position);
    }

    [Theory]
    [InlineData("abstract")]
    [InlineData("unknown")]
    public void ARefStructConvertedToWhatIsNotOneIsBoxed(string target)
    {
        var thing = new AbstractType("IThing", "Probe", new Scope(null), null, null);
        cursor.BaseAbstractTypes.Add(thing);
        IMistType to = target == "abstract" ? thing : UnknownType.Instance;

        Assert.Equal("MIST_PA_REF_STRUCT_BOXED", Assignability.Boxing(cursor, to, model.Interner)?.Id.Value);
    }

    [Theory]
    [InlineData("same")]
    [InlineData("union")]
    [InlineData("type-parameter")]
    [InlineData("plain-struct")]
    public void AConversionThatDoesNotBoxARefStructIsAdmitted(string shape)
    {
        var point = HandBuiltModel.Struct("Point");
        (IMistType From, IMistType To) conversion = shape switch
        {
            "same" => (cursor, cursor),
            "union" => (cursor, model.Interner.GetOrCreateUnionType([cursor, model.Type("String")])),
            "type-parameter" => (cursor, new TypeParameter("T", 0, new Scope(null), null)),
            _ => (point, UnknownType.Instance),
        };

        Assert.Null(Assignability.Boxing(conversion.From, conversion.To, model.Interner));
    }

    private static StructType GenericCursor()
    {
        var generic = new StructType("Cursor", "Probe", new Scope(null), null, null) { Modifiers = new MemberModifiers { IsRef = true } };
        _ = DeclarationFragments.Parameter(generic, generic.Scope, "T");
        return generic;
    }

    private static MemberType Container(string kind)
    {
        return kind switch
        {
            "class" => HandBuiltModel.Class("Holder"),
            "struct" => HandBuiltModel.Struct("Holder"),
            "abstract" => new AbstractType("IHolder", "Probe", new Scope(null), null, null),
            _ => new StructType("Holder", "Probe", new Scope(null), null, null) { Modifiers = new MemberModifiers { IsRef = true } },
        };
    }

    private Property Property(bool isStatic, bool isAuto)
    {
        return new Property("Data", cursor, new MemberModifiers { IsStatic = isStatic })
        {
            Getter = isAuto ? new PropertyAccessor { IsAuto = true } : null,
        };
    }
}