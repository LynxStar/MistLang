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

        MistType shell;
        if(clrType.IsInterface)
        {
            shell = new AbstractType(mistName, namespaceName, scope, null, null);
        }
        else if(clrType.IsEnum)
        {
            shell = new EnumType(mistName, namespaceName, scope, null, null);
        }
        else if(clrType.IsValueType)
        {
            shell = new StructType(mistName, namespaceName, scope, null, null);
        }
        else
        {
            shell = new ClassType(mistName, namespaceName, scope, null, null!);
        }

        shell.Origin = TypeOrigin.Ambient;
        return shell;
    }
}
