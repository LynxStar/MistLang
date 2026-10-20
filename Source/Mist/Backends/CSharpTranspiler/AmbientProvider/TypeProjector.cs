using System.Reflection;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Creates Mist type shells from CLR types. Handles kind dispatch
/// (class/struct/interface/enum) and scope creation for generics.
/// </summary>
internal static class TypeProjector
{

    private static string isReadOnlyAttribute = "System.Runtime.CompilerServices.IsReadOnlyAttribute";
    private static string isByRefLikeAttribute = "System.Runtime.CompilerServices.IsByRefLikeAttribute";

    /// <summary>
    /// Creates an empty type shell for a top-level CLR type.
    /// </summary>
    public static DeclaredType CreateShell(Type clrType, string namespaceName)
    {
        return CreateShell(clrType, namespaceName, declaringType: null, parentScope: null);
    }

    /// <summary>
    /// Creates an empty type shell for a CLR type, optionally nested inside a declaring type.
    /// For nested types, the shell's scope is parented to the declaring type's scope.
    /// </summary>
    public static DeclaredType CreateShell(Type clrType, string namespaceName, MistType? declaringType, Scope? parentScope)
    {
        var mistName = NameFormatTranslator.ClrNameToMistName(clrType.Name);

        // Each of these owns a TypeScope, built around itself, exactly as a source-declared type
        // does: a projected type's members have to be reachable by the same lookup rules.
        var shell = clrType switch
        {
            { IsInterface: true } => (DeclaredType)new AbstractType(mistName, namespaceName, parentScope, declaringType, null),
            { IsEnum: true } => new EnumType(mistName, namespaceName, parentScope, declaringType, null),
            { IsValueType: true } => new StructType(mistName, namespaceName, parentScope, declaringType, null),
            _ => new ClassType(mistName, namespaceName, parentScope, declaringType, null!),
        };

        shell.ClrMetadataName = clrType.FullName;

        if(shell is MemberType memberType)
        {
            var declarations = clrType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            memberType.UnprojectedMemberNames.UnionWith(declarations
                .Where(member => member is not PropertyInfo property || property.GetIndexParameters().Length == 0)
                .Select(member => MistType.StripArity(NameFormatTranslator.ClrNameToMistName(member.Name))));

            var isStaticClass = clrType.IsAbstract && clrType.IsSealed;

            memberType.Modifiers = new MemberModifiers
            {
                Accessibility = clrType.IsNested
                    ? ClrAccessibilityMap.FromNestedType(clrType)
                    : clrType.IsPublic ? Accessibility.Public : Accessibility.Private,
                IsSealed = clrType.IsSealed && !isStaticClass,
                IsStatic = isStaticClass,
                IsReadonly = clrType.IsValueType && HasCustomAttribute(clrType, isReadOnlyAttribute),
                IsRef = clrType.IsValueType && HasCustomAttribute(clrType, isByRefLikeAttribute),
            };
        }

        return shell;
    }

    private static bool HasCustomAttribute(Type clrType, string fullyQualifiedName)
    {
        return clrType
            .GetCustomAttributesData()
            .Any(attribute => attribute.AttributeType.FullName == fullyQualifiedName)
            ;
    }
}