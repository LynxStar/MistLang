using System.Reflection;
using System.Reflection.Emit;
using Mist.Backends.CSharpTranspiler.Razor;
using Mist.Driver.Contracts;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend.Razor;

/// <summary>
/// An <see cref="IAmbientTypeSource"/> over types emitted in memory under the ASP.NET Core names the
/// catalog reads, so a test sees component metadata without the reference pack being loaded.
/// </summary>
internal sealed class ComponentTypeSource(bool eventHandlers = true) : IAmbientTypeSource
{
    private const string Components = "Microsoft.AspNetCore.Components";
    private const MethodAttributes Accessor = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

    private static readonly Lazy<Assembly> Emitted = new(Emit);

    public List<string> Asked { get; } = [];

    public Type? FindClrType(string fullName)
    {
        Asked.Add(fullName);
        return !eventHandlers && fullName == $"{Components}.Web.EventHandlers" ? null : Emitted.Value.GetType(fullName);
    }

    public Type? FindClrType(string fullName, bool ignoreArity)
    {
        return FindClrType(fullName) ?? (ignoreArity
            ? Emitted.Value.GetTypes().FirstOrDefault(type => ClrTypeText.Name(type) == fullName)
            : null);
    }

    public void BuildAndRegister(ProgramModelClass programModel)
    {
    }

    public void Dispose()
    {
    }

    private static Assembly Emit()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(nameof(ComponentTypeSource)), AssemblyBuilderAccess.Run);
        AppDomain.CurrentDomain.AssemblyResolve += (_, request) => new AssemblyName(request.Name).Name == nameof(ComponentTypeSource) ? assembly : null;
        var module = assembly.DefineDynamicModule(nameof(ComponentTypeSource));

        var component = module.DefineType($"{Components}.IComponent", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract).CreateType();
        var parameter = DefineAttribute(module, $"{Components}.ParameterAttribute");
        var cascading = DefineAttribute(module, $"{Components}.CascadingParameterAttribute");
        var renderFragment = module.DefineType($"{Components}.RenderFragment", TypeAttributes.Public | TypeAttributes.Sealed).CreateType();
        var templated = module.DefineType($"{Components}.RenderFragment`1", TypeAttributes.Public | TypeAttributes.Sealed);
        _ = templated.DefineGenericParameters("TValue");
        var templatedType = templated.CreateType();

        var componentBase = module.DefineType($"{Components}.ComponentBase", TypeAttributes.Public | TypeAttributes.Abstract);
        componentBase.AddInterfaceImplementation(component);
        var componentBaseType = componentBase.CreateType();

        var layout = module.DefineType($"{Components}.LayoutComponentBase", TypeAttributes.Public | TypeAttributes.Abstract, componentBaseType);
        Property(layout, "Body", renderFragment, parameter);
        _ = layout.CreateType();

        var router = module.DefineType($"{Components}.Routing.Router", TypeAttributes.Public);
        router.AddInterfaceImplementation(component);
        Property(router, "NotFound", renderFragment, parameter);
        Property(router, "Found", templatedType.MakeGenericType(typeof(string)), parameter);
        Property(router, "Timeout", typeof(int?), cascading);
        Property(router, "Unbound", typeof(string), null);
        _ = router.CreateType();

        var virtualize = module.DefineType($"{Components}.Web.Virtualize`1", TypeAttributes.Public, componentBaseType);
        _ = virtualize.DefineGenericParameters("TItem");
        _ = virtualize.CreateType();

        var genericBase = module.DefineType($"{Components}.GenericBase`1", TypeAttributes.Public, componentBaseType);
        var genericParameter = genericBase.DefineGenericParameters("T")[0];
        Property(genericBase, "Value", genericParameter, parameter);
        Property(genericBase, "Items", genericParameter.MakeArrayType(), parameter);
        _ = genericBase.CreateType();

        var five = module.DefineType($"{Components}.Five`5", TypeAttributes.Public, componentBaseType);
        _ = five.DefineGenericParameters("T1", "T2", "T3", "T4", "T5");
        _ = five.CreateType();

        var mouse = module.DefineType($"{Components}.Web.MouseEventArgs", TypeAttributes.Public, typeof(EventArgs)).CreateType();
        var handler = module.DefineType($"{Components}.EventHandlerAttribute", TypeAttributes.Public | TypeAttributes.Sealed, typeof(Attribute));
        var handlerConstructor = handler.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard, [typeof(string), typeof(Type), typeof(bool), typeof(bool)]);
        var il = handlerConstructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Attribute).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
        var handlerType = handler.CreateType();

        var handlers = module.DefineType($"{Components}.Web.EventHandlers", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        handlers.SetCustomAttribute(new CustomAttributeBuilder(
            handlerType.GetConstructor([typeof(string), typeof(Type), typeof(bool), typeof(bool)])!, ["onclick", mouse, true, true]));
        _ = handlers.CreateType();

        return assembly;
    }

    private static Type DefineAttribute(ModuleBuilder module, string name)
    {
        var attribute = module.DefineType(name, TypeAttributes.Public | TypeAttributes.Sealed, typeof(Attribute));
        _ = attribute.DefineDefaultConstructor(MethodAttributes.Public);
        return attribute.CreateType();
    }

    private static void Property(TypeBuilder owner, string name, Type type, Type? attribute)
    {
        var property = owner.DefineProperty(name, PropertyAttributes.None, type, null);
        var getter = owner.DefineMethod("get_" + name, Accessor, type, Type.EmptyTypes);
        var il = getter.GetILGenerator();
        _ = il.DeclareLocal(type);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ret);
        property.SetGetMethod(getter);

        var setter = owner.DefineMethod("set_" + name, Accessor, null, [type]);
        setter.GetILGenerator().Emit(OpCodes.Ret);
        property.SetSetMethod(setter);

        if(attribute is not null)
        {
            property.SetCustomAttribute(new CustomAttributeBuilder(attribute.GetConstructor(Type.EmptyTypes)!, []));
        }
    }
}