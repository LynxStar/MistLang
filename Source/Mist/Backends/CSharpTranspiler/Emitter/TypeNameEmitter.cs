using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Maps ProgramModel types to their C# source representation.
/// </summary>
internal static class TypeNameEmitter
{
    public static string Emit(IMistType type)
    {
        return type switch
        {
            ConstructedType constructed => EmitConstructed(constructed),
            MemberType memberType => MapMemberType(memberType),
            ErrorType => "/* error */",
            UnknownType => "object",
            _ => type.Name,
        };
    }

    private static string EmitConstructed(ConstructedType constructed)
    {
        // Standard.Array<ElementType, Length> → elementType[]
        if(constructed.Definition.NamespaceName == "Standard" && constructed.Definition.Name == "Array<,>")
        {
            var elementType = Emit(constructed.TypeArguments[0]);
            return $"{elementType}[]";
        }

        // Generic types: Definition<Arg1, Arg2, ...>
        var defName = constructed.Definition.Name;
        var arityIndex = defName.IndexOf('<');
        var baseName = arityIndex >= 0 ? defName[..arityIndex] : defName;
        var args = string.Join(", ", constructed.TypeArguments.Select(Emit));
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

        return memberType.Name;
    }
}