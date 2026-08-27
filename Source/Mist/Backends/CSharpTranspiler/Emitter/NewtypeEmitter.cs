using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// A newtype as a real C# type, because its distinctness must survive into the output: erasing
/// it to the source type would let C# accept every mixing Mist rejects. The shape splits by
/// source kind. An INTRINSIC source (int, string) has no Mist-declared members, so the newtype
/// emits as a readonly struct wrapping a <c>Value</c> field, with explicit conversion operators
/// carrying the casts. A USER-DEFINED source emits as a full duplicate of the source's members
/// under the newtype's name -- constructors renamed, everything else as declared -- so member
/// access and construction forward without any delegation layer. Cast operators are not
/// duplicated: they name their declaring type structurally, so a copy would not compile.
/// </summary>
internal static class NewtypeEmitter
{
    public static void Emit(Newtype type, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        switch(type.SourceType)
        {
            case ConcreteType { IsIntrinsic: true } intrinsicSource:
                EmitIntrinsicWrapper(type, intrinsicSource, writer);
                break;

            case ConcreteType userSource:
                EmitDuplicate(type, userSource, writer, diagnostics);
                break;

            default:
                break;
        }
    }

    private static void EmitIntrinsicWrapper(Newtype type, ConcreteType source, CSharpWriter writer)
    {
        var accessibility = AccessibilityKeyword(type);
        var name = type.Name;
        var valueType = TypeNameEmitter.Emit(source);

        writer.WriteLine($"{accessibility}readonly struct {name}");
        using(writer.Block())
        {
            writer.WriteLine($"public readonly {valueType} Value;");
            writer.WriteBlankLine();
            writer.WriteLine($"public {name}({valueType} value)");
            using(writer.Block())
            {
                writer.WriteLine("Value = value;");
            }

            writer.WriteBlankLine();
            writer.WriteLine($"public static explicit operator {name}({valueType} value) => new {name}(value);");
            writer.WriteLine($"public static explicit operator {valueType}({name} wrapped) => wrapped.Value;");
        }
    }

    private static void EmitDuplicate(Newtype type, ConcreteType source, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var inStruct = source is StructType;
        var inReadonlyType = source.IsReadonly;
        var readonlyModifier = inStruct && inReadonlyType ? "readonly " : "";
        var keyword = inStruct ? "struct" : "class";

        if(inStruct)
        {
            writer.WriteLine(
                "[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]");
        }

        writer.WriteLine($"{AccessibilityKeyword(type)}{readonlyModifier}{keyword} {type.Name}");
        using(writer.Block())
        {
            var anyEmitted = false;

            foreach(var field in source.FieldsList)
            {
                FieldEmitter.Emit(field, inReadonlyType, writer, diagnostics);
                anyEmitted = true;
            }

            foreach(var constructor in source.ConstructorsList)
            {
                TypeEmitter.SeparateMember(writer, ref anyEmitted);
                ConstructorEmitter.Emit(
                    constructor, source, writer, diagnostics, structInitNames: null, nameOverride: type.Name);
            }

            foreach(var property in source.PropertiesList)
            {
                TypeEmitter.SeparateMember(writer, ref anyEmitted);
                PropertyEmitter.Emit(property, inStruct, writer, diagnostics);
            }

            foreach(var indexer in source.IndexersList)
            {
                TypeEmitter.SeparateMember(writer, ref anyEmitted);
                IndexerEmitter.Emit(indexer, inStruct, writer, diagnostics);
            }

            foreach(var method in source.MethodsList)
            {
                TypeEmitter.SeparateMember(writer, ref anyEmitted);
                MethodEmitter.ReportReadonlyRefReturn(method, source.DeclarationNode, diagnostics);
                MethodEmitter.Emit(method, inStruct, writer, diagnostics);
            }
        }
    }

    private static string AccessibilityKeyword(Newtype type)
    {
        return type.Modifiers.Accessibility == Accessibility.Public ? "public " : "";
    }
}