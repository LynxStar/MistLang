using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// A parameterized associated type slot erases to its bound, so the interface asks for the bound and
// the implementor returns its own nested type: an explicit implementation forwards one to the other,
// and only in return position.
[Trait("Tier", "2")]
public sealed class SlotBridgeEmissionTests
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };
    private static readonly MistAssembly Source = new("Probe", MistAssemblyKind.Self);

    private readonly HandBuiltModel built = new();

    [Theory]
    [InlineData(false, "Probe.ITransform<int, string> Probe.IConverter<int>.ConvertToString(int input) => this.ConvertToString(input);")]
    [InlineData(true, "Probe.ITransform<int, string> Probe.IConverter<int>.ConvertToString(int input) => this.ConvertToString(input).GetAwaiter().GetResult();")]
    public void ASlotReturn_IsBridgedAsItsBoundClosedByTheConformedArguments(bool promoted, string expected)
    {
        var (converter, slot) = Converter(declaration: null);
        _ = Member(converter, "ConvertToString", Converted(slot), new Parameter("input", converter.TypeParametersList[0]));
        var (implementor, nested) = Implementor(converter);
        var source = Member(implementor, "ConvertToString", new ConstructedType(nested, [built.Type("String")]), new Parameter("input", built.Type("Int32")));
        if(promoted)
        {
            built.Model.TaskDrops.RecordDrop(source);
        }

        var (bridge, diagnostics) = Bridge(implementor);

        Assert.Equal(expected, bridge);
        Assert.Empty(diagnostics.Admitted);
    }

    [Fact]
    public void ASlotTypedParameter_IsRefusedAtTheAbstract_AndLeavesNothingToBridge()
    {
        var declaration = new BodyTypeTypeDeclarationNode
        {
            Span = new Storm.Runtime.SourceSpan(7, 5),
            Origin = new ParsedOrigin(new SourceText("converter.mist", new string(' ', 20))),
        };
        var (converter, slot) = Converter(declaration);
        _ = Member(converter, "Apply", built.Type("String"),
            new Parameter("seed", converter.TypeParametersList[0]), new Parameter("converter", Converted(slot)));
        var (implementor, nested) = Implementor(converter);
        _ = Member(implementor, "Apply", built.Type("String"),
            new Parameter("seed", built.Type("Int32")), new Parameter("converter", new ConstructedType(nested, [built.Type("String")])));

        var (bridge, diagnostics) = Bridge(implementor);

        Assert.Empty(bridge);
        var refusal = Assert.Single(diagnostics.Admitted);
        Assert.Equal(BackendDiagnostics.ParameterizedTypeSlotInParameter.Number, refusal.Number);
        Assert.Equal(DiagnosticSeverity.Error, refusal.Severity);
        var range = Assert.IsType<SourceRange>(refusal.Location);
        Assert.Equal((7, 5), (range.Offset, range.Length));
        Assert.Equal("converter", Assert.IsType<NameValue>(refusal.Arguments[0]).Name);
        Assert.Equal("Apply", Assert.IsType<NameValue>(refusal.Arguments[2]).Name);
        Assert.Contains("'IConverter.Apply'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("slot 'Converted'", refusal.Message, StringComparison.Ordinal);
    }

    private static (AbstractType Converter, TypeSlot Slot) Converter(BodyTypeTypeDeclarationNode? declaration)
    {
        var transform = new AbstractType("ITransform<>", "Probe", new Scope(null), null, null) { Assembly = Source };
        transform.TypeParametersList.Add(new TypeParameter("TIn", 0, transform.Scope, transform));
        transform.TypeParametersList.Add(new TypeParameter("TOut", 1, transform.Scope, transform));

        var converter = new AbstractType("IConverter<>", "Probe", new Scope(null), null, declaration) { Assembly = Source };
        var input = new TypeParameter("TIn", 0, converter.Scope, converter);
        converter.TypeParametersList.Add(input);
        var slot = new TypeSlot("Converted<>", 0, converter.Scope, converter);
        var output = new TypeParameter("TOut", 0, converter.Scope, slot);
        slot.TypeParametersList.Add(output);
        slot.ConstraintsList.Add(new TypeConstraint(new ConstructedType(transform, [input, output])));
        converter.TypeSlotsList.Add(slot);
        return (converter, slot);
    }

    private ConstructedType Converted(TypeSlot slot)
    {
        return new ConstructedType(slot, [built.Type("String")]);
    }

    private (ClassType Implementor, ClassType Nested) Implementor(AbstractType converter)
    {
        var implementor = HandBuiltModel.Class("IntConverter");
        implementor.BaseAbstractTypes.Add(new ConstructedType(converter, [built.Type("Int32")]));
        var nested = new ClassType("Converted<>", "Probe", new Scope(implementor.Scope), implementor, null);
        nested.TypeParametersList.Add(new TypeParameter("TOut", 0, nested.Scope, nested));
        implementor.NestedTypes.Add(nested);
        return (implementor, nested);
    }

    private static Method Member(MemberType owner, string name, IMistType result, params Parameter[] parameters)
    {
        var method = new Method(name, result, [.. parameters], Public) { DeclaringType = owner };
        owner.MethodsList.Add(method);
        return method;
    }

    private (string Bridge, DiagnosticBag Diagnostics) Bridge(ClassType implementor)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);
        var any = false;

        using(AsyncPromotion.Enter(built.Model))
        {
            SlotBridgeEmitter.Emit(implementor, writer, diagnostics, ref any);
        }

        return (writer.ToString().Trim(), diagnostics);
    }
}