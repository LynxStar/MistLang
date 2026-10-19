using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static class UnionCarrierEmitter
{
    public static void Emit(
        string name, UnionType union, IReadOnlyDictionary<string, UnionType> carriers, CSharpWriter writer)
    {
        writer.WriteLine("[System.Runtime.CompilerServices.CompilerGenerated]");
        writer.WriteLine($"public readonly struct {name}");

        using(writer.Block())
        {
            writer.WriteLine("private readonly byte _tag;");
            writer.WriteLine("private readonly object _value;");
            writer.WriteBlankLine();
            writer.WriteLine("public object Payload => _value;");
            writer.WriteBlankLine();
            writer.WriteLine($"private {name}(byte tag, object value)");

            using(writer.Block())
            {
                writer.WriteLine("_tag = tag;");
                writer.WriteLine("_value = value;");
            }

            for(var i = 0; i < union.Constituents.Count; i++)
            {
                EmitAccessors(union, union.Constituents[i], i + 1, writer);
            }

            for(var i = 0; i < union.Constituents.Count; i++)
            {
                EmitEntry(name, union, union.Constituents[i], i + 1, writer);
            }

            // A union widens into any union holding every constituent of it, as the binder already
            // admits; the carrier is where C# can be told so once, rather than at each use site.
            foreach(var (narrowName, narrow) in carriers)
            {
                if(narrowName == name || !IsNarrower(narrow, union))
                {
                    continue;
                }

                var arms = narrow.Constituents.Select((constituent, i) =>
                {
                    var constituentName = UnionCarrierNames.ConstituentName(constituent, narrow);
                    var rewrapped = $"new {name}({TagOf(union, constituent)}, value.As{constituentName})";
                    return i == narrow.Constituents.Count - 1 ? rewrapped : $"value.Is{constituentName} ? {rewrapped}";
                });

                writer.WriteBlankLine();
                writer.WriteLine($"public static implicit operator {name}({narrowName} value)");
                writer.WriteLine($"    => {string.Join(" : ", arms)};");
            }
        }
    }

    private static void EmitAccessors(UnionType union, IMistType constituent, int tag, CSharpWriter writer)
    {
        var constituentName = UnionCarrierNames.ConstituentName(constituent, union);
        var typeName = TypeNameEmitter.Emit(constituent);

        writer.WriteBlankLine();
        writer.WriteLine($"public bool Is{constituentName} => _tag == {tag};");
        writer.WriteBlankLine();
        writer.WriteLine($"public {typeName} As{constituentName} => _tag == {tag}");
        writer.WriteLine($"    ? ({typeName})_value");
        writer.WriteLine(
            $"    : throw new System.InvalidOperationException(\"Union does not hold {constituentName}\");");
        writer.WriteBlankLine();
        writer.WriteLine(
            $"public {TypeNameEmitter.EmitOptional(constituent)} "
            + $"{UnionCarrierNames.NarrowingAccessor(constituent, union)} "
            + $"=> _tag == {tag} ? ({typeName})_value : null;");
    }

    private static void EmitEntry(string name, UnionType union, IMistType constituent, int tag, CSharpWriter writer)
    {
        var typeName = TypeNameEmitter.Emit(constituent);

        writer.WriteBlankLine();
        writer.WriteLine(UnionEntry.SharesSpelling(union, constituent)
            ? $"public static {name} {UnionCarrierNames.EntryFactory(constituent, union)}({typeName} value)"
            : $"public static implicit operator {name}({typeName} value)");
        writer.WriteLine($"    => new {name}({tag}, value);");
    }

    private static bool IsNarrower(UnionType narrow, UnionType wide)
    {
        return narrow.Constituents.Count < wide.Constituents.Count
            && narrow.Constituents.All(constituent => TagOf(wide, constituent) > 0);
    }

    private static int TagOf(UnionType union, IMistType constituent)
    {
        var name = UnionCarrierNames.ConstituentName(constituent, union);

        for(var i = 0; i < union.Constituents.Count; i++)
        {
            if(union.Constituents[i].FullyQualifiedName == constituent.FullyQualifiedName
                && UnionCarrierNames.ConstituentName(union.Constituents[i], union) == name)
            {
                return i + 1;
            }
        }

        return 0;
    }
}