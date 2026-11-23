using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

// Closed generic slots can hold tuples or a declared Clone without IClonable; neither may take the unchanged fallback.
internal static class GenericCloneEmitter
{
    public static bool NeedsHelper(IMistType type)
    {
        return type switch
        {
            TypeParameter => true,
            UnionType union => union.Constituents.Any(NeedsHelper),
            ITupleType tuple => tuple.ElementTypes.Any(NeedsHelper),
            TypeAlias { AliasedType: { } aliased } => NeedsHelper(aliased),
            RefinedType refined => NeedsHelper(refined.BaseType),
            _ => false,
        };
    }

    public static void Emit(ConcreteType owner, CSharpWriter writer)
    {
        var parameters = new HashSet<string>(StringComparer.Ordinal);

        for(MistType? enclosing = owner; enclosing is not null; enclosing = enclosing.DeclaringType)
        {
            if(enclosing is IHasTypeParameters generic)
            {
                parameters.UnionWith(generic.TypeParametersList.Select(parameter => parameter.Name));
            }
        }

        var parameter = "TValue";

        while(parameters.Contains(parameter))
        {
            parameter = "_" + parameter;
        }

        writer.WriteBlankLine();
        var source = """
            private static TValue __CloneSlot<TValue>(TValue value)
            {
                if(value is null) return value;
                if(value is Standard.IClonable<TValue> clonable) return clonable.Clone();
                return (TValue)__CloneBox(value, typeof(TValue));
            }

            private static object __CloneBox(object value, System.Type declaredType)
            {
                if(value is null) return null;
                var type = System.Nullable.GetUnderlyingType(declaredType) ?? declaredType;
                var contract = typeof(Standard.IClonable<>).MakeGenericType(type);
                if(contract.IsInstanceOfType(value))
                {
                    return __InvokeClone(contract.GetMethod("Clone"), value);
                }

                var method = type.GetMethod("Clone", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, System.Type.EmptyTypes);
                if(method is not null) return __InvokeClone(method, value);
                if(type.IsValueType && value is System.Runtime.CompilerServices.ITuple)
                {
                    foreach(var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        field.SetValue(value, __CloneBox(field.GetValue(value), field.FieldType));
                    }
                    return value;
                }

                if(__Unchangeable(type)) return value;
                throw new System.InvalidOperationException($"Cannot copy changeable generic slot of type '{type}'.");
            }

            private static object __InvokeClone(System.Reflection.MethodInfo method, object value)
            {
                try { return method.Invoke(value, null); }
                catch(System.Reflection.TargetInvocationException error) when(error.InnerException is { } cause)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cause).Throw();
                    throw;
                }
            }

            private static bool __Unchangeable(System.Type type)
            {
                if(type == typeof(string) || type.IsPrimitive || type.IsEnum) return true;
                if(!type.IsValueType) return false;
                foreach(var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                {
                    if(!__Unchangeable(field.FieldType)) return false;
                }
                return true;
            }
            """;

        foreach(var line in source.Replace("TValue", parameter, StringComparison.Ordinal).Split('\n'))
        {
            writer.WriteLine(line);
        }
    }
}