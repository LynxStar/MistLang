using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Creates Mist type shells from CLR types. Handles kind dispatch
/// (class/struct/interface/enum) and scope creation for generics.
/// </summary>
internal static class TypeProjector
{
    /// <summary>
    /// Creates an empty type shell for a top-level CLR type.
    /// The shell has identity (name, namespace, scope, origin) but no members
    /// or resolved references.
    /// </summary>
    public static MistType CreateShell(Type clrType, string namespaceName)
    {
        return CreateShell(clrType, namespaceName, declaringType: null, parentScope: null);
    }

    /// <summary>
    /// Creates an empty type shell for a CLR type, optionally nested inside a declaring type.
    /// For nested types, the shell's scope is parented to the declaring type's scope.
    /// </summary>
    public static MistType CreateShell(Type clrType, string namespaceName, MistType? declaringType, Scope? parentScope)
    {
        var mistName = NameFormatTranslator.ClrNameToMistName(clrType.Name);
        var scope = new Scope(parentScope);

        var shell = clrType switch
        {
            { IsInterface: true } => (MistType)new AbstractType(mistName, namespaceName, scope, declaringType, null),
            { IsEnum: true } => new EnumType(mistName, namespaceName, scope, declaringType, null),
            { IsValueType: true } => new StructType(mistName, namespaceName, scope, declaringType, null),
            _ => new ClassType(mistName, namespaceName, scope, declaringType, null!),
        };

        shell.Origin = TypeOrigin.Ambient;

        if(shell is MemberType memberType)
        {
            memberType.Modifiers = new MemberModifiers
            {
                IsStatic = clrType.IsAbstract && clrType.IsSealed,
                IsReadonly = clrType.IsValueType && HasCustomAttribute(
                    clrType,
                    "System.Runtime.CompilerServices.IsReadOnlyAttribute"),
            };
        }

        return shell;
    }

    private static bool HasCustomAttribute(Type clrType, string fullyQualifiedName)
    {
        return clrType.GetCustomAttributesData()
            .Any(attribute => attribute.AttributeType.FullName == fullyQualifiedName);
    }
}