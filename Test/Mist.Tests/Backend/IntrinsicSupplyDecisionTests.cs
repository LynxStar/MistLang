using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

using CompilationUnit = Mist.ProgramModel.Model.CompilationUnit;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class IntrinsicSupplyDecisionTests
{
    private static readonly MemberModifiers Instance = new() { IsIntrinsic = true, Accessibility = Accessibility.Public };

    private static readonly MemberModifiers Static = Instance with { IsStatic = true };

    private readonly HandBuiltModel built = new();

    [Fact]
    public void AMethodIsMetadataOnlyWhenTheBackingTypeHasItsNameStaticnessAndArity()
    {
        var math = IntrinsicType("Math");
        var abs = AddMethod(math, "Abs", Static, 1);
        var instanceAbs = AddMethod(math, "Abs", Instance, 1);
        var binaryAbs = AddMethod(math, "Abs", Static, 2);
        var magnitude = AddMethod(math, "Magnitude", Static, 1);

        var audit = Audit(math);

        Assert.Equal(IntrinsicSupply.Kind.Metadata, KindOf(audit, abs));
        Assert.Equal(IntrinsicSupply.Kind.Missing, KindOf(audit, instanceAbs));
        Assert.Equal(IntrinsicSupply.Kind.Missing, KindOf(audit, binaryAbs));
        Assert.Equal(IntrinsicSupply.Kind.Missing, KindOf(audit, magnitude));
    }

    [Fact]
    public void AConstructorIsMetadataOnlyWhenTheBackingTypeHasAPublicOneOfItsArity()
    {
        var text = IntrinsicType("String");
        var fromCharacters = new Constructor("String", text, Parameters(1), Instance) { DeclaringType = text };
        var parameterless = new Constructor("String", text, Parameters(0), Instance) { DeclaringType = text };
        text.ConstructorsList.Add(fromCharacters);
        text.ConstructorsList.Add(parameterless);

        var audit = Audit(text);

        Assert.Equal(IntrinsicSupply.Kind.Metadata, KindOf(audit, fromCharacters));
        Assert.Equal(IntrinsicSupply.Kind.Missing, KindOf(audit, parameterless));
    }

    [Fact]
    public void APropertyIsMissingWhenTheBackingTypeLacksItOrNoTypeBacksItsDeclarer()
    {
        var text = IntrinsicType("String");
        var length = AddProperty(text, "Length");
        var size = AddProperty(text, "Size");
        var widget = IntrinsicType("Widget");
        var widgetSize = AddProperty(widget, "Size");

        var audit = Audit(text, widget);

        Assert.Equal(IntrinsicSupply.Kind.Metadata, KindOf(audit, length));
        Assert.Equal(IntrinsicSupply.Kind.Missing, KindOf(audit, size));
        Assert.Equal(IntrinsicSupply.Kind.Missing, KindOf(audit, widgetSize));
    }

    [Fact]
    public void ANestedTypeTheClrDoesNotNestIsCheckedAgainstWhatTheClrReturnsWhereMistReturnsIt()
    {
        var text = IntrinsicType("String");
        var enumerator = IntrinsicType("Enumerator", text);
        var cursor = IntrinsicType("Cursor", text);
        _ = AddMethod(text, "GetEnumerator", Instance, 0, enumerator);
        var moveNext = AddMethod(enumerator, "MoveNext", Instance, 0);
        var current = AddProperty(enumerator, "Current");
        var peek = AddMethod(enumerator, "Peek", Instance, 0);
        var unreturned = AddMethod(cursor, "MoveNext", Instance, 0);

        var audit = Audit(text);

        Assert.Equal(IntrinsicSupply.Kind.Metadata, KindOf(audit, moveNext));
        Assert.Equal(IntrinsicSupply.Kind.Metadata, KindOf(audit, current));
        Assert.Equal(IntrinsicSupply.Kind.Missing, KindOf(audit, peek));
        Assert.Equal(IntrinsicSupply.Kind.Missing, KindOf(audit, unreturned));
    }

    [Fact]
    public void AnArrayEnumeratorMemberIsSuppliedOnlyWhenTheGeneratedStructDeclaresIt()
    {
        var array = IntrinsicType("Array<,>");
        var enumerator = IntrinsicType("Enumerator", array);
        var moveNext = AddMethod(enumerator, "MoveNext", Instance, 0);
        var reset = AddMethod(enumerator, "Reset", Instance, 0);
        var current = AddProperty(enumerator, "Current");
        var index = AddProperty(enumerator, "Index");

        var audit = Audit(array);

        Assert.Equal(IntrinsicSupply.Kind.Respelled, KindOf(audit, moveNext));
        Assert.Equal(IntrinsicSupply.Kind.Missing, KindOf(audit, reset));
        Assert.Equal(IntrinsicSupply.Kind.Respelled, KindOf(audit, current));
        Assert.Equal(IntrinsicSupply.Kind.Missing, KindOf(audit, index));
    }

    [Fact]
    public void AMissingPropertyIsUnsuppliedOnlyWhileItsAuditIsInScope()
    {
        var text = IntrinsicType("String");
        var length = AddProperty(text, "Length");
        var size = AddProperty(text, "Size");

        using(IntrinsicSupply.Enter(Model(text), ClrType))
        {
            Assert.True(IntrinsicSupply.Supplies(length));
            Assert.False(IntrinsicSupply.Supplies(size));
        }

        Assert.True(IntrinsicSupply.Supplies(size));
    }

    private static Type? ClrType(string metadataName)
    {
        return metadataName switch
        {
            "System.String" => typeof(string),
            "System.Math" => typeof(Math),
            _ => null,
        };
    }

    private static IntrinsicSupply.Kind KindOf(IReadOnlyList<IntrinsicSupply.Entry> audit, object member)
    {
        return Assert.Single(audit, entry => ReferenceEquals(entry.Member, member)).Kind;
    }

    private static ClassType IntrinsicType(string name, MistType? declaringType = null)
    {
        var type = new ClassType(name, "Standard", new Scope(null), declaringType, null)
        {
            Modifiers = Instance,
        };
        declaringType?.NestedTypes.Add(type);
        return type;
    }

    private Method AddMethod(ClassType owner, string name, MemberModifiers modifiers, int arity, MistType? returnType = null)
    {
        var method = new Method(name, returnType ?? built.Type("Int32"), Parameters(arity), modifiers) { DeclaringType = owner };
        owner.MethodsList.Add(method);
        return method;
    }

    private Property AddProperty(ClassType owner, string name)
    {
        var property = new Property(name, built.Type("Int32"), Instance) { DeclaringType = owner };
        owner.PropertiesList.Add(property);
        return property;
    }

    private List<Parameter> Parameters(int arity)
    {
        return [.. Enumerable.Range(0, arity).Select(index => new Parameter($"p{index}", built.Type("Int32")))];
    }

    private IReadOnlyList<IntrinsicSupply.Entry> Audit(params ClassType[] types)
    {
        return IntrinsicSupply.Audit(Model(types), ClrType);
    }

    private ProgramModelClass Model(params ClassType[] types)
    {
        var standard = built.Model.AddStandardAssembly();
        var unit = new CompilationUnit { SourcePath = "Standard.mist" };
        unit.Types.AddRange(types);
        standard.CompilationUnits.Add(unit);
        return built.Model;
    }
}