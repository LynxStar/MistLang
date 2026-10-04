using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

internal static class SizedSequenceTypes
{
    public static readonly ClassType IntegerType = PropertyPatternEmissionTests.Type("Int32", "Standard");
    public static readonly ClassType StringType = PropertyPatternEmissionTests.Type("String", "Standard");
    public static readonly ClassType BooleanType = PropertyPatternEmissionTests.Type("Boolean", "Standard");
    public static readonly ClassType VoidType = PropertyPatternEmissionTests.Type("Void", "Standard");
    private static readonly ClassType ArrayDefinition = PropertyPatternEmissionTests.Type("Array<,>", "Standard");
    private static readonly ClassType SpanDefinition = PropertyPatternEmissionTests.Type("Span<,>", "Standard");
    private static readonly ClassType ReadOnlySpanDefinition = PropertyPatternEmissionTests.Type("ReadOnlySpan<,>", "Standard");

    public static ConstructedType ArrayOf(IMistType element, long? length)
    {
        return new ConstructedType(ArrayDefinition, [element, Length(length)]);
    }

    public static ConstructedType SpanOf(IMistType element, long? length)
    {
        return new ConstructedType(SpanDefinition, [element, Length(length)]);
    }

    public static ConstructedType ReadOnlySpanOf(IMistType element, long? length)
    {
        return new ConstructedType(ReadOnlySpanDefinition, [element, Length(length)]);
    }

    public static TupleType TupleOf(params IMistType[] elements)
    {
        return new TupleType(elements);
    }

    public static UnionType OptionalOf(IMistType inner)
    {
        return new UnionType([inner, VoidType]);
    }

    public static TypeAlias AliasOf(string name, IMistType aliased)
    {
        return new TypeAlias(name, "Probe", null, null, null!) { AliasedType = aliased };
    }

    public static ConstructedType BoxOf(long length)
    {
        var box = new ClassType("Box<>", "Probe", new Scope(null), null, null!);
        return new ConstructedType(box, [new LiftedType(IntegerType, length)]);
    }

    private static IMistType Length(long? length)
    {
        return length is { } known ? new LiftedType(IntegerType, known) : new UnknownValueType(IntegerType);
    }
}