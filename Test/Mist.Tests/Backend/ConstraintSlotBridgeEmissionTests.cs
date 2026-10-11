using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// Tier 2: a generic slot `string Pick<T>(T value)` on the hand-built abstract IPicker and its answer
// `Pick<U>(U value)` on Picker, each constrained as a case names: what the interface declares, and
// whether an explicit implementation must forward to an answer whose constraints C# would not match.
[Trait("Tier", "2")]
public sealed class ConstraintSlotBridgeEmissionTests
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };
    private static readonly MistAssembly Source = new("Probe", MistAssemblyKind.Self);

    private readonly HandBuiltModel built = new();
    private readonly AbstractType shown = new("IShow", "Probe", new Scope(null), null, null) { Assembly = Source };

    [Theory]
    [InlineData("IShow", "", "string Probe.IPicker.Pick<T>(T value) => this.Pick<T>(value);")]
    [InlineData("IShow, class", "class", "string Probe.IPicker.Pick<T>(T value) => this.Pick<T>(value);")]
    [InlineData("IShow", "IShow", "")]
    [InlineData("", "", "")]
    [InlineData("new(Int32)", "", "")]
    public void AnAnswerRequiringLessThanItsSlot_IsReachedThroughAnExplicitImplementation(string promised, string required, string expected)
    {
        var (_, picker) = Declare(promised, required);

        Assert.Equal(expected, Bridge(picker));
    }

    [Fact]
    public void AnInterfaceSlotDeclaresItsMethodTypeParametersConstraints()
    {
        var (slot, _) = Declare("IShow, class", "");
        var writer = new CSharpWriter();

        TypeEmitter.EmitInterfaceMethod(slot, writer, new DiagnosticBag());

        Assert.Equal(["string Pick<T>(T value)", "where T : class, Probe.IShow;"], writer.ToString().Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));
    }

    private (Method Slot, ClassType Picker) Declare(string promised, string required)
    {
        var contract = new AbstractType("IPicker", "Probe", new Scope(null), null, null) { Assembly = Source };
        var slot = Generic(contract, "T", promised);
        var picker = HandBuiltModel.Class("Picker");
        picker.BaseAbstractTypes.Add(contract);
        _ = Generic(picker, "U", required);
        return (slot, picker);
    }

    private Method Generic(MemberType owner, string name, string constraints)
    {
        var parameter = new TypeParameter(name, 0, new Scope(owner.Scope), declaringType: null);
        var method = new Method("Pick", built.Type("String"), [new Parameter("value", parameter)], Public) { DeclaringType = owner };
        method.TypeParametersList.Add(parameter);
        owner.MethodsList.Add(method);

        foreach(var constraint in constraints.Split(", ", StringSplitOptions.RemoveEmptyEntries))
        {
            parameter.ConstraintsList.Add(constraint switch
            {
                "class" => new KeywordConstraint(TypeParameterConstraintKind.Class),
                "new(Int32)" => new ConstructorConstraint([built.Type("Int32")]),
                _ => new TypeConstraint(shown),
            });
        }

        return method;
    }

    private string Bridge(ClassType picker)
    {
        var writer = new CSharpWriter();
        var any = false;

        using(AsyncPromotion.Enter(built.Model))
        {
            ConstraintSlotBridgeEmitter.Emit(picker, writer, new DiagnosticBag(), ref any);
        }

        return writer.ToString().Trim();
    }
}