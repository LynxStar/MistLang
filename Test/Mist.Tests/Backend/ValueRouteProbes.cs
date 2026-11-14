using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// The hand-built declarations and nodes the value-route emission tests share: a value-routed class and struct,
/// an ordinary generic, and the call, test and block shapes a body writes them in.
/// </summary>
internal static class ValueRouteProbes
{
    public static ClassType Ring(HandBuiltSlots slots, out TypeParameter capacity)
    {
        var ring = (ClassType)HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        ring.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        TypeParameterDomainResolver.ProjectValues(ring);
        capacity = ring.TypeParametersList[0];
        return ring;
    }

    public static StructType Cell(HandBuiltSlots slots, out TypeParameter n)
    {
        var cell = (StructType)HandBuiltSlots.Generic(HandBuiltModel.Struct("Cell<>"), "N", slots.Int);
        cell.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        TypeParameterDomainResolver.ProjectValues(cell);
        n = cell.TypeParametersList[0];
        return cell;
    }

    public static ClassType Generic(string name)
    {
        var type = HandBuiltModel.Class(name);
        type.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        type.TypeParametersList.Add(new TypeParameter("T", 0, type.Scope, type));
        return type;
    }

    public static StructType GenericStruct(string name, bool constructed = false)
    {
        var type = HandBuiltModel.Struct(name);
        type.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        var t = new TypeParameter("T", 0, type.Scope, type);
        type.TypeParametersList.Add(t);
        type.FieldsList.Add(new Field("item", t, OwnershipKind.Unrestricted, new MemberModifiers { Accessibility = Accessibility.Public }) { DeclaringType = type });

        if(constructed)
        {
            type.ConstructorsList.Add(new Constructor(name, type, [new Parameter("value", t)], new MemberModifiers { Accessibility = Accessibility.Public }) { DeclaringType = type });
        }

        return type;
    }

    public static Method Static(string name, IMistType returns, params Parameter[] parameters)
    {
        return new Method(name, returns, [.. parameters], new MemberModifiers { IsStatic = true }) { Scope = new Scope(null) };
    }

    public static Method Inferring(string name, IMistType returns, TypeParameter slot, params Parameter[] parameters)
    {
        var method = Static(name, returns, parameters);
        method.TypeParametersList.Add(slot);
        slot.DeclaringMethod = method;
        return method;
    }

    public static MemberAccessExpression Count(HandBuiltSlots slots, string receiver = "p")
    {
        return new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Local(UnknownType.Instance, receiver), "Count", slots.Int);
    }

    public static SlotCapture Capture(HandBuiltSlots slots, Expression initializer, string written = "p.Count")
    {
        return new SlotCapture(written, slots.Int, slots.Int, initializer, written.Length);
    }

    public static InvocationExpression Call(
        Method method, IReadOnlyList<IMistType> typeArguments, IReadOnlyList<Expression> arguments, IReadOnlyList<IMistType> parameterTypes,
        IReadOnlyList<SlotCapture>? captures = null)
    {
        return new InvocationExpression(
            HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method, typeArguments), arguments, method.ReturnType, method, parameterTypes)
        {
            Captures = captures ?? [],
        };
    }

    public static InvocationExpression Through(
        Expression receiver, Method method, IReadOnlyList<IMistType> typeArguments, IReadOnlyList<Expression> arguments, IReadOnlyList<IMistType> parameterTypes,
        IReadOnlyList<SlotCapture>? captures = null)
    {
        var access = new MemberAccessExpression(HandBuiltModel.Node(), receiver, method.Name, method.ReturnType, typeArguments);
        return new InvocationExpression(HandBuiltModel.Node(), access, arguments, method.ReturnType, method, parameterTypes) { Captures = captures ?? [] };
    }

    public static IsPatternExpression Is(HandBuiltSlots slots, Expression scrutinee, Pattern pattern)
    {
        return new IsPatternExpression(HandBuiltModel.Node(), scrutinee, pattern, slots.Refinements.Bool);
    }

    public static TypeTestPattern Tested(IMistType type, string? binding, params SlotCapture[] captures)
    {
        return new TypeTestPattern(HandBuiltModel.Node(), type, binding is null ? null : new LocalVariable(binding, type)) { Captures = captures };
    }

    public static SwitchExpression Switch(HandBuiltSlots slots, Expression scrutinee, Pattern first)
    {
        return new SwitchExpression(HandBuiltModel.Node(), scrutinee,
        [
            new SwitchArm(HandBuiltModel.Node(), first, null, HandBuiltModel.Literal(slots.Int, 1L)),
            new SwitchArm(HandBuiltModel.Node(), new CatchAllPattern(HandBuiltModel.Node(), UnknownType.Instance, null), null, HandBuiltModel.Literal(slots.Int, 0L)),
        ], slots.Int);
    }

    public static string Emit(Expression expression)
    {
        return PropertyPatternEmissionTests.Emit(expression);
    }

    public static List<string> Statements(params Statement[] statements)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        BodyEmitter.ResetTemporaries();
        BodyEmitter.EmitBlock(HandBuiltModel.Block(statements), writer, diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.ToImmutableArray().Select(diagnostic => diagnostic.Message)));
        return [.. writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim())];
    }
}