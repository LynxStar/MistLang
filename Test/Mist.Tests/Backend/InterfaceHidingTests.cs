using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class InterfaceHidingTests
{
    private readonly HandBuiltModel built = new();
    private readonly AbstractType enumerator = Protocol("IEnumerator<>");

    [Theory]
    [InlineData("Dispose")]
    [InlineData("Reset")]
    public void RestatingAMemberTheClrEnumeratorInherits_Hides(string name)
    {
        var cursor = Abstract("ICursor", new ConstructedType(enumerator, [built.Type("Int32")]));

        Assert.True(InterfaceHiding.Hides(Declare(cursor, name, Void()), cursor));
    }

    [Fact]
    public void RestatingAMemberTheStandardEnumeratorDeclares_Hides()
    {
        enumerator.MethodsList.Add(new Method("MoveNext", built.Type("Boolean"), [], default));
        var cursor = Abstract("ICursor", new ConstructedType(enumerator, [built.Type("Int32")]));

        Assert.True(InterfaceHiding.Hides(Declare(cursor, "MoveNext", built.Type("Boolean")), cursor));
    }

    [Theory]
    [InlineData("Peek", 0)]
    [InlineData("Dispose", 1)]
    public void AMemberNoReachedInterfaceDeclares_HidesNothing(string name, int parameters)
    {
        var cursor = Abstract("ICursor", new ConstructedType(enumerator, [built.Type("Int32")]));
        var method = Declare(cursor, name, Void());
        method.ParametersList.AddRange(Enumerable.Range(0, parameters).Select(i => new Parameter($"p{i}", built.Type("Int32"))));

        Assert.False(InterfaceHiding.Hides(method, cursor));
    }

    [Fact]
    public void AParameterClosedThroughTheBase_HidesTheMemberOfTheSameSpelling()
    {
        var equatable = Equatable();
        var restating = Abstract("IRestates", new ConstructedType(equatable, [built.Type("Int32")]));
        var method = Declare(restating, "Equals", built.Type("Boolean"));
        method.ParametersList.Add(new Parameter("other", built.Type("Int32")));

        Assert.True(InterfaceHiding.Hides(method, restating));
    }

    [Fact]
    public void AnAbstractsOwnTypeParameter_DoesNotHideAMemberOverAnotherType()
    {
        var equatable = Equatable();
        var overloads = Abstract("IOverloads<>", new ConstructedType(equatable, [built.Type("Int32")]));
        var own = new TypeParameter("T", 0, overloads.Scope, overloads);
        overloads.TypeParametersList.Add(own);
        var method = Declare(overloads, "Equals", built.Type("Boolean"));
        method.ParametersList.Add(new Parameter("other", own));

        Assert.False(InterfaceHiding.Hides(method, overloads));
    }

    [Theory]
    [InlineData(true, "new void Dispose();")]
    [InlineData(false, "void Dispose();")]
    public void AnInterfaceSlot_IsMarkedNewOnlyWhereItHides(bool hides, string expected)
    {
        var cursor = Abstract("ICursor");
        var writer = new CSharpWriter();

        TypeEmitter.EmitInterfaceMethod(Declare(cursor, "Dispose", Void()), writer, new DiagnosticBag(), hides);

        Assert.Equal(expected, writer.ToString().Trim());
    }

    [Theory]
    [InlineData("Int32")]
    [InlineData("Boolean")]
    public void RestatingAPropertyTheStandardEnumeratorDeclares_Hides(string propertyType)
    {
        _ = DeclareProperty(enumerator, "Current", enumerator.TypeParametersList[0]);
        var cursor = Abstract("ICursor", new ConstructedType(enumerator, [built.Type("Int32")]));

        Assert.True(InterfaceHiding.Hides(DeclareProperty(cursor, "Current", built.Type(propertyType)), cursor));
    }

    [Theory]
    [InlineData("Peek")]
    [InlineData("current")]
    public void APropertyNoReachedInterfaceDeclares_HidesNothing(string name)
    {
        _ = DeclareProperty(enumerator, "Current", enumerator.TypeParametersList[0]);
        var cursor = Abstract("ICursor", new ConstructedType(enumerator, [built.Type("Int32")]));

        Assert.False(InterfaceHiding.Hides(DeclareProperty(cursor, name, built.Type("Int32")), cursor));
    }

    [Fact]
    public void RestatingAPropertyThroughAMistAbstract_Hides()
    {
        var parent = Abstract("IBase");
        _ = DeclareProperty(parent, "Value", built.Type("Int32"));
        var child = Abstract("IDer", Abstract("IMiddle", parent));

        Assert.True(InterfaceHiding.Hides(DeclareProperty(child, "Value", built.Type("Boolean")), child));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnInterfaceProperty_IsMarkedNewOnlyWhereItHides(bool hasBody)
    {
        var property = DeclareProperty(Abstract("ICursor"), "Current", built.Type("Int32"));

        if(hasBody)
        {
            property.Getter!.ExpressionBody = HandBuiltModel.Literal(built.Type("Int32"), 7L);
        }

        var hiding = new CSharpWriter();
        var bare = new CSharpWriter();
        var unspecified = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        var accessors = hasBody ? "\n{\n    get => 7;\n}" : " { get; }";

        PropertyEmitter.Emit(property, false, false, hiding, diagnostics, hides: true);
        PropertyEmitter.Emit(property, false, false, bare, diagnostics, hides: false);
        PropertyEmitter.Emit(property, false, false, unspecified, diagnostics);

        Assert.Equal($"public new int Current{accessors}", hiding.ToString().Trim().Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Equal($"public int Current{accessors}", bare.ToString().Trim().Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Equal(bare.ToString(), unspecified.ToString());
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    private static Property DeclareProperty(AbstractType owner, string name, IMistType type)
    {
        var property = new Property(name, type, default)
        {
            DeclaringType = owner,
            Getter = new PropertyAccessor(),
        };
        owner.PropertiesList.Add(property);
        return property;
    }

    private AbstractType Equatable()
    {
        var equatable = new AbstractType("IEquatable<>", "System", new Scope(null), null, null) { Assembly = MistAssembly.Ambient };
        var parameter = new TypeParameter("T", 0, equatable.Scope, equatable);
        equatable.TypeParametersList.Add(parameter);
        equatable.MethodsList.Add(new Method("Equals", built.Type("Boolean"), [new Parameter("other", parameter)], default));
        return equatable;
    }

    private ClassType Void()
    {
        return built.Type("Void");
    }

    private static Method Declare(AbstractType owner, string name, IMistType returns)
    {
        var method = new Method(name, returns, [], default) { DeclaringType = owner };
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
        var abstractType = new AbstractType(name, "Probe", new Scope(null), null, null)
        {
            Assembly = new MistAssembly("Probe", MistAssemblyKind.Self),
        };
        abstractType.BaseAbstractTypes.AddRange(bases);
        return abstractType;
    }
}