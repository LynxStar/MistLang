using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

/// <summary>
/// Attributes written in Mist source, from declaration through to emitted C#.
/// <para>
/// ASSERTED HERE RATHER THAN BY samples/Attributes, WHICH CANNOT SEE THIS. That sample proves its
/// attributes only by compiling -- no reflection, no output line reflecting any of them -- so its
/// stdout reads identically whether all fourteen are emitted or all fourteen are dropped, which is
/// what they were before this. The fixture declares its own Attribute base in namespace System so
/// that the suffix filter has the fully qualified name it tests against, which keeps every case here
/// free of any CLR assembly load.
/// </para>
/// </summary>
public sealed class AttributeEmissionTests : ProgramModelProbe
{
    private readonly ProgramModelClass model;

    public AttributeEmissionTests()
        : base("MistAttributes_")
    {
        model = Build(Source, out _);
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    private const string Source = """
        namespace System;

        public class Attribute
        {
        }

        public class Range
        {
        }

        public class RangeAttribute : Attribute
        {
        }

        public class MarkAttribute : Attribute
        {
        }

        public class ProxyAttribute : Attribute
        {
        }

        public class TypedAttribute<T> : Attribute
        {
        }

        public class Box<T>
        {
        }

        [Mark]
        public class OnClass
        {
            [Mark]
            public int Field;

            [Mark]
            public int Prop { get; }

            [Mark]
            public int Method([Mark] int given)
            {
                return given;
            }

            [Mark]
            OnClass()
            {
            }

            [Mark]
            public int this[int index]
            {
                get { return index; }
            }
        }

        [Mark]
        public struct OnStruct
        {
            public int x;
        }

        [Mark]
        public enum OnEnum
        {
            First
        }

        [Mark, Proxy]
        public class OneList
        {
        }

        [Mark]
        [Proxy]
        public class TwoLists
        {
        }

        public class Args
        {
            [Range(0, 100)]
            public int Scored;

            [Mark(Label = "x")]
            public int Named;

            [Proxy(int)]
            public int Primitive;

            [Proxy(Box<int>)]
            public int Constructed;

            [Typed<int>]
            public int GenericAttribute;
        }
        """;

    private ConcreteType Class(string name)
    {
        return Assert.IsType<ConcreteType>(model.TypesByFQN[$"System.{name}"], exactMatch: false);
    }

    /// <summary>
    /// Newlines are normalized because <see cref="CSharpWriter"/> writes the platform's, and what is
    /// under test is which attributes reach the output and in what form -- never which bytes end a
    /// line.
    /// </summary>
    private static string Render(IReadOnlyList<DeclaredAttribute> attributes)
    {
        var writer = new CSharpWriter();
        AttributeEmitter.EmitLines(attributes, writer, new DiagnosticBag());

        return writer.ToString().ReplaceLineEndings("\n");
    }

    private string RenderFieldOf(string typeName, string fieldName)
    {
        return Render(Class(typeName).FieldsList.Single(field => field.Name == fieldName).DeclaredAttributesList);
    }

    [Fact]
    public void ClassDeclaration_CarriesItsAttribute()
    {
        Assert.Equal("[System.MarkAttribute]\n", Render(Class("OnClass").DeclaredAttributesList));
    }

    [Fact]
    public void StructDeclaration_CarriesItsAttribute()
    {
        Assert.Equal("[System.MarkAttribute]\n", Render(Class("OnStruct").DeclaredAttributesList));
    }

    [Fact]
    public void EnumDeclaration_CarriesItsAttribute()
    {
        var enumType = Assert.IsType<EnumType>(model.TypesByFQN["System.OnEnum"]);

        Assert.Equal("[System.MarkAttribute]\n", Render(enumType.DeclaredAttributesList));
    }

    [Fact]
    public void FieldDeclaration_CarriesItsAttribute()
    {
        Assert.Equal("[System.MarkAttribute]\n", RenderFieldOf("OnClass", "Field"));
    }

    [Fact]
    public void PropertyDeclaration_CarriesItsAttribute()
    {
        Assert.Equal(
            "[System.MarkAttribute]\n", Render(Class("OnClass").PropertiesList.Single().DeclaredAttributesList));
    }

    [Fact]
    public void MethodDeclaration_CarriesItsAttribute()
    {
        var method = Class("OnClass").MethodsList.Single(candidate => candidate.Name == "Method");

        Assert.Equal("[System.MarkAttribute]\n", Render(method.DeclaredAttributesList));
    }

    [Fact]
    public void ConstructorDeclaration_CarriesItsAttribute()
    {
        Assert.Equal(
            "[System.MarkAttribute]\n", Render(Class("OnClass").ConstructorsList.Single().DeclaredAttributesList));
    }

    [Fact]
    public void IndexerDeclaration_CarriesItsAttribute()
    {
        Assert.Equal(
            "[System.MarkAttribute]\n", Render(Class("OnClass").IndexersList.Single().DeclaredAttributesList));
    }

    /// <summary>
    /// The one target with no line of its own, so it is the one whose emitted form comes from
    /// <see cref="ParameterEmitter"/> instead.
    /// </summary>
    [Fact]
    public void ParameterDeclaration_CarriesItsAttributeInline()
    {
        var method = Class("OnClass").MethodsList.Single(candidate => candidate.Name == "Method");

        Assert.Equal(
            "[System.MarkAttribute] int given",
            ParameterEmitter.Emit(method.ParametersList, new DiagnosticBag()));
    }

    [Fact]
    public void OneListOfTwo_AndTwoListsOfOne_EmitAlike()
    {
        const string expected = "[System.MarkAttribute]\n[System.ProxyAttribute]\n";

        Assert.Equal(expected, Render(Class("OneList").DeclaredAttributesList));
        Assert.Equal(expected, Render(Class("TwoLists").DeclaredAttributesList));
    }

    [Fact]
    public void PositionalArguments_KeepTheirOrder()
    {
        Assert.Equal("[System.RangeAttribute(0, 100)]\n", RenderFieldOf("Args", "Scored"));
    }

    [Fact]
    public void NamedArgument_KeepsItsName()
    {
        Assert.Equal("[System.MarkAttribute(Label = \"x\")]\n", RenderFieldOf("Args", "Named"));
    }

    [Fact]
    public void PrimitiveTypeArgument_BecomesTypeof()
    {
        Assert.Equal("[System.ProxyAttribute(typeof(int))]\n", RenderFieldOf("Args", "Primitive"));
    }

    [Fact]
    public void ConstructedTypeArgument_BecomesTypeof()
    {
        Assert.Equal("[System.ProxyAttribute(typeof(Box<int>))]\n", RenderFieldOf("Args", "Constructed"));
    }

    [Fact]
    public void GenericAttribute_KeepsItsTypeArgument()
    {
        Assert.Equal("[TypedAttribute<int>]\n", RenderFieldOf("Args", "GenericAttribute"));
    }

    /// <summary>
    /// The written name resolves -- to a class that is not an attribute at all -- so a first-hit-wins
    /// rule binds it and nothing downstream objects. Both halves are asserted, since the case is only
    /// a case while the wrong type is genuinely reachable.
    /// </summary>
    [Fact]
    public void WrittenNameThatIsNotAnAttribute_LosesToTheSuffixedOne()
    {
        var applied = Assert.Single(Class("Args").FieldsList
            .Single(field => field.Name == "Scored").DeclaredAttributesList);

        Assert.NotNull(model.TypesByFQN["System.Range"]);
        Assert.Equal("System.RangeAttribute", applied.AttributeType.FullyQualifiedName);
    }

    [Fact]
    public void DeclarationWithNoAttributes_EmitsNothing()
    {
        Assert.Equal("", Render(Class("Box<>").DeclaredAttributesList));
    }
}