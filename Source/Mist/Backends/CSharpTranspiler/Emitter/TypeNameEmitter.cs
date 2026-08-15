using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Maps ProgramModel types to their C# source representation.
/// </summary>
internal static class TypeNameEmitter
{
    /// <summary>
    /// The C# spelling of a Mist type.
    /// <para>
    /// A dependent type argument emits its DOMAIN, whether or not its value is known. Both
    /// carriers would otherwise emit something that is not a C# type -- a <see cref="LiftedType"/>
    /// is named for the value it holds, so the fallback arm would emit the <c>3</c> of
    /// <c>Array&lt;int, 3&gt;</c>, and an <see cref="UnknownValueType"/> is named <c>unknown</c>.
    /// C# has neither singleton types nor a spelling for "some int I cannot name", so the domain
    /// is the only faithful projection of either. Reached whenever a dependent argument becomes an
    /// expression's type, which today means an array length read through <c>arr.Length</c>.
    /// </para>
    /// <para>
    /// <see cref="UnknownType"/> is the separate case and keeps emitting <c>object</c>: it is the
    /// user-facing top type, with no domain to fall back on.
    /// </para>
    /// </summary>
    public static string Emit(IMistType type)
    {
        return type switch
        {
            ConstructedType constructed => EmitConstructed(constructed),
            LiftedType lifted => Emit(lifted.Type),
            UnknownValueType unknownValue => Emit(unknownValue.Domain),
            MemberType memberType => MapMemberType(memberType),
            ErrorType => "/* error */",
            UnknownType => "object",
            _ => type.Name,
        };
    }

    private static string EmitConstructed(ConstructedType constructed)
    {
        // Standard.Array<ElementType, Length> → elementType[]
        if(constructed.Definition.FullyQualifiedName == WellKnownTypes.Array.FullyQualifiedName)
        {
            var elementType = Emit(constructed.TypeArguments[0]);
            return $"{elementType}[]";
        }

        // Generic types: Definition<Arg1, Arg2, ...>
        var baseName = MistType.StripArity(constructed.Definition.Name);
        var args = string.Join(", ", constructed.TypeArguments.Select(Emit));

        if(constructed.Definition is MemberType { DeclaringType: MemberType declaring })
        {
            return $"{Qualified(declaring)}.{baseName}<{args}>";
        }

        return $"{baseName}<{args}>";
    }

    private static string MapMemberType(MemberType memberType)
    {
        // Map well-known Standard namespace types to C# keywords.
        if(memberType.NamespaceName == "Standard")
        {
            return memberType.Name switch
            {
                "Int32" => "int",
                "Int64" => "long",
                "UInt32" => "uint",
                "UInt64" => "ulong",
                "Int16" => "short",
                "UInt16" => "ushort",
                "Byte" => "byte",
                "SByte" => "sbyte",
                "Single" => "float",
                "Double" => "double",
                "Decimal" => "decimal",
                "Boolean" => "bool",
                "Char" => "char",
                "String" => "string",
                "Void" => "void",
                _ => memberType.Name,
            };
        }

        if(ShouldQualify(memberType))
        {
            return Qualified(memberType);
        }

        return memberType.Name;
    }

    /// <summary>
    /// Built segment by segment because <see cref="IMistType.FullyQualifiedName"/> keeps the arity
    /// marker a generic segment is registered under, leaving a nesting path that is not a C# name.
    /// </summary>
    private static string Qualified(MemberType type)
    {
        var segment = type.TypeParametersList.Count > 0
            ? $"{MistType.StripArity(type.Name)}<{string.Join(", ", type.TypeParametersList.Select(p => p.Name))}>"
            : type.Name;

        if(type.DeclaringType is MemberType declaring)
        {
            return $"{Qualified(declaring)}.{segment}";
        }

        return string.IsNullOrEmpty(type.NamespaceName)
            ? segment
            : $"{type.NamespaceName}.{segment}"
            ;
    }

    /// <summary>
    /// Whether a type must be emitted by its fully qualified name. A bare name is
    /// resolved by C# against the enclosing namespace first, so a cross-project
    /// reference such as <c>Simpliest.Program</c> emitted as <c>Program</c> inside
    /// <c>namespace AllFeaturesV2;</c> silently binds to <c>AllFeaturesV2.Program</c>.
    /// </summary>
    private static bool ShouldQualify(MemberType memberType)
    {
        // The Standard namespace is handled above and maps onto C# keywords and
        // System types reached through the emitted usings.
        if(string.IsNullOrEmpty(memberType.NamespaceName)
            || memberType.NamespaceName.StartsWith("Standard.", StringComparison.Ordinal))
        {
            return false;
        }

        // Ambient types carry CLR namespaces that the emitted usings already cover, and
        // synthetic types have no declaration to qualify against. Only types declared in
        // Mist source are ours to qualify.
        if(!memberType.Assembly.HasMistSource)
        {
            return false;
        }

        // Generic definitions keep their arity-encoded name (`Array<,>`) and are
        // rendered through EmitConstructed instead.
        return !memberType.Name.Contains('<');
    }
}