using System.Globalization;
using System.Text;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// An operator or a conversion emitted as a static method that every use calls (runtime-dependent-values design,
/// <em>Identity by value: conversions and nested types</em>). A C# operator takes its operands and nothing else, and
/// must convert to or from, or take, the very type declaring it. A value-routed type's operator whose operands do not
/// carry every slot takes the values as the hidden leading parameters every static member takes, and each use passes
/// those of the closing it converts to or found the operator through, which is the one place that knows them. One that
/// names the declaring type only at another closing, such as an addition of a Ring at 1 and an int declared inside the
/// Ring at Capacity, is a method on either route, because C# refuses it as an operator. C# keeps two conversions from
/// one source apart by their target but two methods only by their parameters, so a conversion out of the declaring type
/// spells the target it was declared with in its name; one into the declaring type erases to that type at every closing.
/// </summary>
internal static class OperatorMethods
{
    public static bool Applies(Method? op)
    {
        if(op is not { Modifiers.IsIntrinsic: false, DeclaringType: MemberType declaring } || declaring is ConcreteType { IsIntrinsic: true })
        {
            return false;
        }

        var signature = op switch
        {
            CastOperator { CopiesClone: false, ClonesThrough: null } conversion => new List<IMistType> { conversion.TargetType, conversion.SourceParameter.Type },
            Operator { TypeParametersList.Count: 0 } => op.ParametersList.Select(parameter => parameter.Type).ToList(),
            _ => null,
        };

        return signature is not null
            && (!signature.Exists(type => IsDeclaring(type, declaring)) || SlotRoutes.ValueRouted(declaring).Any(slot => BodyEmitter.OperandRead(op, slot) is null));
    }

    public static string Name(Method op)
    {
        return op switch
        {
            CastOperator conversion => (conversion.IsImplicit ? "__Implicit" : "__Explicit") + TargetSuffix(Declared(conversion)),
            _ => $"__{IdentifierSpelling.Of(op.Name)}",
        };
    }

    private static string TargetSuffix(CastOperator conversion)
    {
        return BodyEmitter.Unaliased(conversion.TargetType) is var target
            && ReferenceEquals(target is ConstructedType constructed ? constructed.Definition : target, conversion.DeclaringType)
            ? ""
            : $"_{Escaped(conversion.TargetType.FullyQualifiedName)}";
    }

    private static CastOperator Declared(CastOperator conversion)
    {
        return conversion is { DeclarationNode: { } node, DeclaringType: ConcreteType declaring }
            ? declaring.CastOperatorsList.Find(declared => ReferenceEquals(declared.DeclarationNode, node)) ?? conversion
            : conversion;
    }

    private static string Escaped(string name)
    {
        var escaped = new StringBuilder();

        foreach(var character in name)
        {
            _ = character switch
            {
                _ when char.IsAsciiLetterOrDigit(character) => escaped.Append(character),
                '_' => escaped.Append("_u"),
                '.' => escaped.Append("_d"),
                _ => escaped.Append(CultureInfo.InvariantCulture, $"_x{(int)character:x4}"),
            };
        }

        return escaped.ToString();
    }

    public static string Call(Method op, IMistType owner, IEnumerable<string> arguments, DiagnosticBag? diagnostics)
    {
        var values = BodyEmitter.SlotValues(owner, diagnostics);

        return $"{TypeNameEmitter.Emit(owner)}.{Name(op)}({string.Join(", ", values.Concat(arguments))})";
    }

    public static IMistType Owner(Method op, IMistType? converted, TypeSubstitution closing)
    {
        var declaring = op.DeclaringType!;

        if(converted is not null
            && BodyEmitter.Unaliased(TypeNameEmitter.Closed(OptionalUnion.InnerType(converted) ?? converted)) is var written
            && ReferenceEquals(written is ConstructedType constructed ? constructed.Definition : written, declaring))
        {
            return written;
        }

        return TypeNameEmitter.Closed(closing.Apply(new ConstructedType(declaring, declaring.TypeParametersList), interner: null));
    }

    private static bool IsDeclaring(IMistType type, MemberType declaring)
    {
        return BodyEmitter.Unaliased(TypeNameEmitter.Closed(OptionalUnion.InnerType(type) ?? type)) switch
        {
            ConstructedType constructed => ReferenceEquals(constructed.Definition, declaring)
                && constructed.TypeArguments.Count == declaring.TypeParametersList.Count
                && constructed.TypeArguments.Select((argument, ordinal) => ReferenceEquals(argument, declaring.TypeParametersList[ordinal])).All(same => same),
            var bare => ReferenceEquals(bare, declaring),
        };
    }
}