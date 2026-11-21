using System.Collections.Immutable;
using Mist.ProgramAnalysis.Facts;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal static class HeldNames
{
    // A value keeps the name it was recorded under while that name still holds it. Once the program writes that name, the value is
    // spelled by a holder that still holds it, one the use reads before the first bound; with none, the recorded name stays.
    public static IReadOnlyDictionary<Value, string> Of(Bindings bindings, IEnumerable<Value> values, IReadOnlyList<string> read)
    {
        var named = bindings.Order.SelectMany(holder => Spellings(bindings, holder, 0).Select(name => (Holder: holder, Name: name))).ToList();
        var names = ImmutableDictionary.CreateBuilder<Value, string>();
        foreach(var value in values.Distinct())
        {
            var written = named.Where(entry => entry.Name == value.DisplayName).ToList();
            if(written.Count == 0 || written.Any(entry => ReferenceEquals(bindings.Get(entry.Holder), value)))
            {
                continue;
            }

            var holding = named.Where(entry => ReferenceEquals(bindings.Get(entry.Holder), value)).Select(entry => entry.Name).ToList();
            if(holding.Count > 0)
            {
                names[value] = holding.FirstOrDefault(read.Contains) ?? holding[0];
            }
        }

        return names.ToImmutable();
    }

    private static IEnumerable<string> Spellings(Bindings bindings, object holder, int depth)
    {
        return holder switch
        {
            LocalVariable { IsDiscard: false } local => [local.Name],
            Field field => [field.Name],
            Property property => [property.Name],
            MemberHolder { Member: string or Field or Property or TypeParameter } member when depth < 4 => bindings.Order
                .Where(receiver => ReferenceEquals(bindings.Get(receiver), member.Receiver))
                .SelectMany(receiver => Spellings(bindings, receiver, depth + 1))
                .Select(receiver => $"{receiver}.{Member(member)}"),
            _ => [],
        };
    }

    private static string Member(MemberHolder holder)
    {
        return holder.Member switch
        {
            Field field => field.Name,
            Property property => property.Name,
            TypeParameter length when Equals(FactReading.LengthMember(holder.Receiver.Type), length) => "Length",
            TypeParameter parameter => parameter.Name,
            var member => (string)member,
        };
    }
}