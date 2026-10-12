using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
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
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AConformerDeclaringDispose_KeepsItInPlaceOfTheAdapter(bool inherited)
    {
        var protocol = Closed(enumerator, built.Type("Int32"));
        var cursor = Conformer(inherited ? Abstract("ICursor", protocol) : protocol);
        _ = Declare(cursor, "Dispose", Public);

        Assert.Equal([ObjectCurrent, Reset], Emit(cursor));
    }

    [Fact]
    public void AConformerDeclaringReset_KeepsItInPlaceOfTheRefusal()
    {
        var cursor = Conformer(Abstract("ICursor", Closed(enumerator, built.Type("Int32"))));
        _ = Declare(cursor, "Reset", Public);

        Assert.Equal([ObjectCurrent, Dispose], Emit(cursor));
    }

    [Fact]
    public void ADerivedConformerWhoseBaseClassDeclaresDispose_InheritsItInPlaceOfTheAdapter()
    {
        var resource = HandBuiltModel.Class("Resource");
        _ = Declare(resource, "Dispose", Public);
        var cursor = Conformer(Abstract("ICursor", Closed(enumerator, built.Type("Int32"))));
        cursor.BaseType = resource;

        Assert.Equal([ObjectCurrent, Reset], Emit(cursor));
    }

    [Fact]
    public void AMemberEmittedIntoTheConformerWithoutBeingDeclaredOnIt_KeepsItInPlaceOfTheAdapter()
    {
        var cursor = Conformer(Closed(enumerator, built.Type("Int32")));
        var flattened = Declare(HandBuiltModel.Class("Base"), "Dispose", Public with { Accessibility = Accessibility.Private });

        Assert.Equal([ObjectCurrent, Reset], Emit(cursor, [flattened]));
    }

    [Theory]
    [InlineData("parameterized")]
    [InlineData("generic")]
    [InlineData("private on the base class")]
    public void AMemberThatNeitherCollidesNorImplementsDispose_LeavesTheAdapter(string shape)
    {
        var resource = HandBuiltModel.Class("Resource");
        var cursor = Conformer(Closed(enumerator, built.Type("Int32")));
        cursor.BaseType = resource;
        var onBase = shape == "private on the base class";

        var dispose = Declare(onBase ? resource : cursor, "Dispose", onBase ? Public with { Accessibility = Accessibility.Private } : Public);
        if(shape == "parameterized")
        {
            dispose.ParametersList.Add(new Parameter("disposing", built.Type("Boolean")));
        }
        else if(shape == "generic")
        {
            dispose.TypeParametersList.Add(new TypeParameter("T", 0, new Scope(cursor.Scope), declaringType: null));
        }

        Assert.Equal([ObjectCurrent, Reset, Dispose], Emit(cursor));
    }

    private static string GenericGetEnumerator(string element)
    {
        return $"System.Collections.Generic.IEnumerator<{element}> System.Collections.Generic.IEnumerable<{element}>.GetEnumerator() => GetEnumerator();";
    }

    private static List<string> Emit(ClassType conformer, IReadOnlyList<Method>? declared = null)
    {
        var writer = new CSharpWriter();
        var any = false;

        IteratorBoilerplateEmitter.Emit(conformer, declared ?? conformer.MethodsList, writer, ref any);

        return writer.ToString().Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
    }

    private Method Declare(ClassType owner, string name, MemberModifiers modifiers)
    {
        var method = new Method(name, built.Type("Void"), [], modifiers) { DeclaringType = owner };
        owner.MethodsList.Add(method);
        return method;
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