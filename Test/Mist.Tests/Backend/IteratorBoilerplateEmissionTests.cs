using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class IteratorBoilerplateEmissionTests
{
    private const string ObjectCurrent = "object System.Collections.IEnumerator.Current => Current;";
    private const string Reset = "public void Reset() { throw new System.NotSupportedException(); }";
    private const string Dispose = "public void Dispose() { }";
    private const string NonGenericGetEnumerator = "System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();";

    private static readonly MistAssembly Source = new("Probe", MistAssemblyKind.Self);

    private readonly HandBuiltModel built = new();
    private readonly AbstractType enumerator = Protocol("IEnumerator<>");
    private readonly AbstractType enumerable = Protocol("IEnumerable<>");

    [Fact]
    public void AnEnumeratorInheritedThroughAUserAbstract_GetsTheClrAdapters()
    {
        var cursor = Abstract("ICursor", Closed(enumerator, built.Type("Int32")));

        Assert.Equal([ObjectCurrent, Reset, Dispose], Emit(Conformer(cursor)));
    }

    [Fact]
    public void AnEnumerableInheritedThroughAGenericAbstract_TakesItsElementFromTheReachedSubstitution()
    {
        var keyed = Abstract("IKeyed<,>");
        _ = Parameter(keyed, "K", 0);
        keyed.BaseAbstractTypes.Add(Closed(enumerable, Parameter(keyed, "V", 1)));

        var table = Conformer(Closed(keyed, built.Type("Int32"), built.Type("String")));

        Assert.Equal([GenericGetEnumerator("string"), NonGenericGetEnumerator], Emit(table));
    }

    [Fact]
    public void AnEnumerableTwoAbstractsAway_ClosesThroughEachLevel()
    {
        var sequence = Abstract("ISeq<>");
        sequence.BaseAbstractTypes.Add(Closed(enumerable, Parameter(sequence, "T", 0)));
        var wordList = Abstract("IWords", Closed(sequence, built.Type("String")));

        Assert.Equal([GenericGetEnumerator("string"), NonGenericGetEnumerator], Emit(Conformer(wordList)));
    }

    [Theory]
    [InlineData("two-level")]
    [InlineData("diamond")]
    [InlineData("listed directly as well")]
    public void EachEnumeratorAdapterIsEmittedOnce(string shape)
    {
        var cursor = Abstract("ICursor", Closed(enumerator, built.Type("Int32")));

        var conformer = shape switch
        {
            "two-level" => Conformer(Abstract("IPeekable", cursor)),
            "diamond" => Conformer(Abstract("IPeekable", cursor), Abstract("IRewound", Closed(enumerator, built.Type("Int32")))),
            _ => Conformer(cursor, Closed(enumerator, built.Type("Int32"))),
        };

        Assert.Equal([ObjectCurrent, Reset, Dispose], Emit(conformer));
    }

    [Fact]
    public void AnEnumerableReachedThroughBothArmsOfADiamondAndDirectly_IsAdaptedOnce()
    {
        var sequence = Abstract("ISeq<>");
        sequence.BaseAbstractTypes.Add(Closed(enumerable, Parameter(sequence, "T", 0)));
        var named = Abstract("INamed", Closed(enumerable, built.Type("String")));

        var words = Conformer(Closed(sequence, built.Type("String")), named, Closed(enumerable, built.Type("String")));

        Assert.Equal([GenericGetEnumerator("string"), NonGenericGetEnumerator], Emit(words));
    }

    [Fact]
    public void ADirectEnumeratorConformer_KeepsItsAdapters()
    {
        Assert.Equal([ObjectCurrent, Reset, Dispose], Emit(Conformer(Closed(enumerator, built.Type("Int32")))));
    }

    [Fact]
    public void AGenericDirectEnumerableConformer_KeepsItsOwnParameterAsTheElement()
    {
        var box = HandBuiltModel.Class("Box<>");
        var parameter = new TypeParameter("T", 0, box.Scope, box);
        box.TypeParametersList.Add(parameter);
        box.BaseAbstractTypes.Add(Closed(enumerable, parameter));

        Assert.Equal([GenericGetEnumerator("T"), NonGenericGetEnumerator], Emit(box));
    }

    [Fact]
    public void AnUnrelatedAbstract_ReceivesNoAdapters()
    {
        var shape = Abstract("IShape", Abstract("IDrawable"));

        Assert.Empty(Emit(Conformer(shape)));
    }

    private static string GenericGetEnumerator(string element)
    {
        return $"System.Collections.Generic.IEnumerator<{element}> System.Collections.Generic.IEnumerable<{element}>.GetEnumerator() => GetEnumerator();";
    }

    private static List<string> Emit(ClassType conformer)
    {
        var writer = new CSharpWriter();
        var any = false;

        IteratorBoilerplateEmitter.Emit(conformer, writer, ref any);

        return writer.ToString().Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
    }

    private static AbstractType Protocol(string name)
    {
        var protocol = new AbstractType(name, "Standard", new Scope(null), null, null);
        protocol.TypeParametersList.Add(new TypeParameter("TElement", 0, protocol.Scope, protocol));
        return protocol;
    }

    private static AbstractType Abstract(string name, params MistType[] bases)
    {
        var abstractType = new AbstractType(name, "Probe", new Scope(null), null, null) { Assembly = Source };
        abstractType.BaseAbstractTypes.AddRange(bases);
        return abstractType;
    }

    private static TypeParameter Parameter(AbstractType owner, string name, int ordinal)
    {
        var parameter = new TypeParameter(name, ordinal, owner.Scope, owner);
        owner.TypeParametersList.Add(parameter);
        return parameter;
    }

    private static ConstructedType Closed(AbstractType definition, params IMistType[] arguments)
    {
        return new ConstructedType(definition, arguments);
    }

    private static ClassType Conformer(params MistType[] bases)
    {
        var conformer = HandBuiltModel.Class("Thing");
        conformer.BaseAbstractTypes.AddRange(bases);
        return conformer;
    }
}