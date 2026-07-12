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
    /// Creates an empty type shell for the given CLR type.
    /// The shell has identity (name, namespace, scope, origin) but no members
    /// or resolved references.
    /// </summary>
    public static MistType CreateShell(Type clrType, string namespaceName)
    {
        var mistName = NameFormatTranslator.ClrNameToMistName(clrType.Name);
        var scope = new Scope(null);

        var shell = clrType switch
        {
            { IsInterface: true } => (MistType)new AbstractType(mistName, namespaceName, scope, null, null),
            { IsEnum: true } => new EnumType(mistName, namespaceName, scope, null, null),
            { IsValueType: true } => new StructType(mistName, namespaceName, scope, null, null),
            _ => new ClassType(mistName, namespaceName, scope, null, null!),
        };

        shell.Origin = TypeOrigin.Ambient;

        // Static classes in CLR are abstract + sealed.
        if(clrType.IsAbstract && clrType.IsSealed && shell is MemberType memberType)
        {
            memberType.Modifiers = new MemberModifiers { IsStatic = true };
        }

        return shell;
    }
}