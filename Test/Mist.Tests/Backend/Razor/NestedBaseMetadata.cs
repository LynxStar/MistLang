using System.Reflection;
using System.Reflection.Emit;

namespace Mist.Tests.Backend.Razor;

internal static class NestedBaseMetadata
{
    public static void Define(ModuleBuilder module, Type componentBase, Type parameter, Type fragment)
    {
        var textBase = module.DefineType("Vendor.NestedTextBase`1", TypeAttributes.Public, componentBase);
        var textArgument = textBase.DefineGenericParameters("T")[0];
        Property(textBase, "Keys", typeof(Dictionary<,>.KeyCollection).MakeGenericType(textArgument, typeof(string)), parameter);
        _ = textBase.CreateType();

        var outer = module.DefineType("Vendor.MatrixOuter", TypeAttributes.Public);
        DefineInner(outer, componentBase, parameter, fragment, [], enclosingGeneric: false, genericInner: false);
        DefineInner(outer, componentBase, parameter, fragment, ["V"], enclosingGeneric: false, genericInner: true);
        var middle = outer.DefineNestedType("Middle", TypeAttributes.NestedPublic);
        DefineInner(middle, componentBase, parameter, fragment, [], enclosingGeneric: false, genericInner: false);
        DefineInner(middle, componentBase, parameter, fragment, ["V"], enclosingGeneric: false, genericInner: true);
        _ = middle.CreateType();
        _ = outer.CreateType();

        var box = module.DefineType("Vendor.MatrixBox`1", TypeAttributes.Public);
        _ = box.DefineGenericParameters("T");
        DefineInner(box, componentBase, parameter, fragment, ["T"], enclosingGeneric: true, genericInner: false);
        DefineInner(box, componentBase, parameter, fragment, ["T", "V"], enclosingGeneric: true, genericInner: true);
        var genericMiddle = box.DefineNestedType("Middle`1", TypeAttributes.NestedPublic);
        _ = genericMiddle.DefineGenericParameters("T", "U");
        DefineInner(genericMiddle, componentBase, parameter, fragment, ["T", "U"], enclosingGeneric: true, genericInner: false);
        DefineInner(genericMiddle, componentBase, parameter, fragment, ["T", "U", "V"], enclosingGeneric: true, genericInner: true);
        _ = genericMiddle.CreateType();
        _ = box.CreateType();
    }

    private static void DefineInner(TypeBuilder owner, Type componentBase, Type parameter, Type fragment, string[] names, bool enclosingGeneric, bool genericInner)
    {
        var inner = owner.DefineNestedType(genericInner ? "GenericInner`1" : "Inner", TypeAttributes.NestedPublic, componentBase);
        var arguments = names.Length == 0 ? [] : inner.DefineGenericParameters(names);
        if(enclosingGeneric)
        {
            arguments[0].SetGenericParameterAttributes(GenericParameterAttributes.NotNullableValueTypeConstraint | GenericParameterAttributes.DefaultConstructorConstraint);
            arguments[0].SetBaseTypeConstraint(typeof(ValueType));
        }

        var value = enclosingGeneric ? arguments[0] : typeof(string);
        Property(inner, "Value", value, parameter);
        Property(inner, "Optional", enclosingGeneric ? typeof(Nullable<>).MakeGenericType(value) : typeof(int?), parameter);
        Property(inner, "Items", value.MakeArrayType(), parameter);
        Property(inner, "Values", typeof(List<>).MakeGenericType(value), parameter);
        Property(inner, "Template", fragment.MakeGenericType(value), parameter);
        if(arguments.Length > 0)
        {
            if(genericInner)
            {
                Property(inner, "Other", arguments[^1], parameter);
            }

            if(arguments.Length - (genericInner ? 1 : 0) == 2)
            {
                Property(inner, "MiddleValue", arguments[1], parameter);
            }
        }

        _ = inner.CreateType();
    }

    private static void Property(TypeBuilder owner, string name, Type type, Type attribute)
    {
        var property = owner.DefineProperty(name, PropertyAttributes.None, type, null);
        var getter = owner.DefineMethod("get_" + name, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, type, Type.EmptyTypes);
        var il = getter.GetILGenerator();
        _ = il.DeclareLocal(type);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ret);
        property.SetGetMethod(getter);
        property.SetCustomAttribute(new CustomAttributeBuilder(attribute.GetConstructor(Type.EmptyTypes)!, []));
    }
}