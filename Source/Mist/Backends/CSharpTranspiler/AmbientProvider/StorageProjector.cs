using System.Reflection;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal static class StorageProjector
{
    public static void Project(ConcreteType concreteType, Type clrType, AmbientTypeResolver resolver)
    {
        var fields = new List<StorageField>();
        IMistType? baseType = null;
        var complete = false;
        try
        {
            complete = !clrType.Assembly.GetCustomAttributesData().Any(attribute =>
                attribute.AttributeType.FullName == "System.Runtime.CompilerServices.ReferenceAssemblyAttribute");
            complete &= !clrType.GetCustomAttributesData().Any(attribute =>
                attribute.AttributeType.FullName == "System.Runtime.CompilerServices.InlineArrayAttribute");
            if(clrType.BaseType is { FullName: not "System.Object" and not "System.ValueType" } baseClrType)
            {
                baseType = Resolve(baseClrType, concreteType.Scope, resolver);
                complete &= IsResolved(baseType);
            }

            foreach(var field in clrType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var type = Resolve(field.FieldType, concreteType.Scope, resolver);
                complete &= IsResolved(type);
                var visible = concreteType.FieldsList.Find(projected => !projected.Modifiers.IsStatic && projected.Name == field.Name);
                fields.Add(new StorageField(field.Name, type, visible));
                complete &= !field.GetCustomAttributesData().Any(attribute =>
                    attribute.AttributeType.FullName == "System.Runtime.CompilerServices.FixedBufferAttribute");
            }
        }
        catch(Exception exception) when(GenericConstraintProjector.IsMetadataFailure(exception))
        {
            complete = false;
        }

        concreteType.ImportedStorage = new StorageLayout(complete, fields, baseType);
    }

    private static IMistType Resolve(Type clrType, Scope scope, AmbientTypeResolver resolver)
    {
        try
        {
            // By-ref field storage cannot inherit the value semantics of its referenced element.
            return clrType.IsByRef || clrType.IsPointer || clrType.IsFunctionPointer
                ? ErrorType.Instance
                : resolver.ResolveStorage(clrType, scope);
        }
        catch(Exception exception) when(GenericConstraintProjector.IsMetadataFailure(exception))
        {
            return ErrorType.Instance;
        }
    }

    private static bool IsResolved(IMistType type)
    {
        return type switch
        {
            ErrorType or NotProjectedType => false,
            IUnionType union => union.Constituents.All(IsResolved),
            _ => true,
        };
    }
}