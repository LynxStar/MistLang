using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits one extension block as the C# static class that carries its members.
/// <para>
/// Separate from <see cref="TypeEmitter"/> because a block is not a type: it declares none, is
/// registered by receiver rather than by name, and its members become STATIC methods on a class the
/// user never wrote. Sharing the type path would mean teaching every arm of it about a declaration
/// that has no accessibility, no base, no nested types and no conformances.
/// </para>
/// </summary>
internal static class ExtensionBlockEmitter
{
    public static void Emit(
        ExtensionBlock block, CSharpWriter writer, DiagnosticBag diagnostics, ref bool anyEmitted)
    {
        if(block.ResolvedReceiverType is not { } receiver)
        {
            return;
        }

        if(anyEmitted)
        {
            writer.WriteBlankLine();
        }

        anyEmitted = true;

        // NON-GENERIC, always. C# requires an extension method's containing class to be non-generic,
        // so a block's own type parameters cannot live here; they are hoisted onto each member below.
        writer.WriteLine($"public static class {block.CarrierName}");
        using(writer.Block())
        {
            var first = true;

            foreach(var method in block.MethodsList)
            {
                EmitMember(method, block, receiver, writer, diagnostics, ref first);
            }

            foreach(var property in block.PropertiesList)
            {
                EmitProperty(property, block, receiver, writer, diagnostics, ref first);
            }
        }
    }

    private static void EmitMember(
        Method method,
        ExtensionBlock block,
        IMistType receiver,
        CSharpWriter writer,
        DiagnosticBag diagnostics,
        ref bool first)
    {
        Separate(writer, ref first);

        var returnType = OptionalUnion.IsVoid(method.ReturnType)
            ? "void"
            : TypeNameEmitter.Emit(method.ReturnType);

        writer.WriteLine(
            $"public static {returnType} {method.Name}{TypeParameters(method, block)}"
            + $"({Parameters(method.ParametersList, method.Modifiers.IsStatic, block, receiver, diagnostics)})");

        using(writer.Block())
        {
            if(method.Body is not null)
            {
                BodyEmitter.EmitBlock(method.Body, writer, diagnostics);
            }
        }
    }

    /// <summary>
    /// A property becomes a <c>get_</c> method, because C# has no extension property. The call site
    /// has to be reclassified from an access to an invocation to match, which is the lookup half of
    /// this layer rather than the emission half.
    /// </summary>
    private static void EmitProperty(
        Property property,
        ExtensionBlock block,
        IMistType receiver,
        CSharpWriter writer,
        DiagnosticBag diagnostics,
        ref bool first)
    {
        if(property.Getter is not { } getter)
        {
            return;
        }

        Separate(writer, ref first);

        writer.WriteLine(
            $"public static {TypeNameEmitter.Emit(property.Type)} get_{property.Name}"
            + $"{TypeParameters(method: null, block)}"
            + $"({Parameters([], property.Modifiers.IsStatic, block, receiver, diagnostics)})");

        using(writer.Block())
        {
            if(getter.Body is { } body)
            {
                BodyEmitter.EmitBlock(body, writer, diagnostics);
            }
            else if(getter.ExpressionBody is { } expression)
            {
                writer.Write("return ");
                BodyEmitter.EmitExpression(expression, writer, diagnostics);
                writer.Append(";");
                writer.AppendLine();
            }
        }
    }

    /// <summary>
    /// The receiver, <c>this</c>-annotated, ahead of everything the member declared -- except on a
    /// static member, which is namespaced under the type rather than called on an instance and so
    /// takes no receiver at all.
    /// </summary>
    private static string Parameters(
        IReadOnlyList<Parameter> declared,
        bool isStatic,
        ExtensionBlock block,
        IMistType receiver,
        DiagnosticBag diagnostics)
    {
        var rest = ParameterEmitter.Emit(declared, diagnostics);

        if(isStatic)
        {
            return rest;
        }

        var self = $"this {TypeNameEmitter.Emit(receiver)} {block.ReceiverParameterName}";

        return rest.Length == 0 ? self : $"{self}, {rest}";
    }

    /// <summary>
    /// The block's type parameters and the member's own, in that order. They cannot go on the class,
    /// so every member restates them; a member declared inside a generic block is generic in both.
    /// </summary>
    private static string TypeParameters(Method? method, ExtensionBlock block)
    {
        var names = new List<string>();

        foreach(var parameter in block.TypeParametersList)
        {
            names.Add(parameter.Name);
        }

        if(method is not null)
        {
            foreach(var parameter in method.TypeParametersList)
            {
                names.Add(parameter.Name);
            }
        }

        return names.Count == 0 ? "" : $"<{string.Join(", ", names)}>";
    }

    private static void Separate(CSharpWriter writer, ref bool first)
    {
        if(!first)
        {
            writer.WriteBlankLine();
        }

        first = false;
    }
}