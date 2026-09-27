using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// An enum as a record carrying its underlying value, with one static singleton per variant.
/// <para>
/// NOT A C# ENUM, and it cannot be one. A Mist enum wraps an arbitrary readonly type -- a string, a
/// struct, a class, a union -- where a CLR enum admits only integral types, so even the integral case
/// goes through this shape rather than splitting the lowering in two.
/// </para>
/// <para>
/// A VARIANT IS NAMED BY WHICH SINGLETON IT IS, not by what its value happens to equal, and the two
/// come apart whenever a base type discards what it is constructed with. Matching on the value there
/// makes every variant answer with the first one's name, silently, on a program both pipelines
/// otherwise agree on. <see cref="EmitFromUnderlying"/> is the one that matches on VALUE, correctly:
/// it is handed a value and has nothing else to go on.
/// </para>
/// </summary>
internal static class EnumEmitter
{
    public static void Emit(EnumType type, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var accessibility = MemberModifierEmitter.Spell(type.Modifiers.Accessibility);
        var underlying = TypeNameEmitter.Emit(type.UnderlyingType!);

        // Sealed because the variants ARE the type's population: a C#-side subclass could mint
        // values no Mist variant names, which every emitted comparison chain assumes cannot exist.
        AttributeEmitter.EmitLines(type.DeclaredAttributesList, writer, diagnostics);
        writer.WriteLine($"{accessibility}sealed record class {type.Name}({underlying} Value)");
        using(writer.Block())
        {
            // A variant with no value was reported during construction, so the compilation is
            // already failing and Roslyn is never reached. Skipped rather than written as a
            // sentinel, which would add a C# error on generated code to a Mist error that names
            // the variant. Declared(type) keeps every other walk consistent with this one.
            foreach(var member in Declared(type))
            {
                writer.Write($"public static readonly {type.Name} {member.Name} = new {type.Name}(");
                BodyEmitter.EmitExpression(member.Value!, writer, diagnostics);
                writer.Append(");");
                writer.AppendLine();
            }

            writer.WriteBlankLine();
            writer.WriteLine(
                $"public static implicit operator {underlying}({type.Name} e) => e.Value;");

            writer.WriteBlankLine();
            EmitFromUnderlying(type, underlying, writer);

            writer.WriteBlankLine();
            EmitToString(type, writer);

            if(type.MembersList.Find(member => member.IsDefault) is { } fallback)
            {
                writer.WriteBlankLine();
                writer.WriteLine(
                    $"public static {type.Name} {DefaultSynthesis.FactoryName}() => {Q(type, fallback)};");
            }
        }
    }

    private static void EmitFromUnderlying(EnumType type, string underlying, CSharpWriter writer)
    {
        writer.WriteLine($"public static explicit operator {type.Name}?({underlying} value)");
        using(writer.Block())
        {
            foreach(var member in Declared(type))
            {
                writer.WriteLine(
                    $"if(Equals(value, {Q(type, member)}.Value)) return {Q(type, member)};");
            }

            writer.WriteLine("return null;");
        }
    }

    private static void EmitToString(EnumType type, CSharpWriter writer)
    {
        writer.WriteLine("public override string ToString()");
        using(writer.Block())
        {
            foreach(var member in Declared(type))
            {
                writer.WriteLine(
                    $"if(ReferenceEquals(this, {Q(type, member)})) return \"{member.Name}\";");
            }

            writer.WriteLine($"return {InvariantCulture.ObjectText("Value", type.UnderlyingType!)};");
        }
    }

    /// <summary>
    /// A singleton reference qualified by the enum, so a variant named after something already in
    /// scope -- the conversion operator's <c>value</c> parameter being the live case -- reads the
    /// static field and not the nearer name.
    /// </summary>
    private static string Q(EnumType type, EnumMember member)
    {
        return $"{type.Name}.{member.Name}";
    }

    private static IEnumerable<EnumMember> Declared(EnumType type)
    {
        return type.MembersList.Where(member => member.Value is not null);
    }
}