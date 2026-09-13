using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Types;

internal static class NewtypeConversions
{
    public static void Synthesize(ConcreteType clone)
    {
        if(clone.CloneSource is not { } source || !IsCopyable(source))
        {
            return;
        }

        clone.CastOperatorsList.Add(Copy(clone, source, clone, isImplicit: true));
        clone.CastOperatorsList.Add(Copy(source, clone, clone, isImplicit: false));
    }

    public static bool IsCopyable(ConcreteType source)
    {
        return source is StructType || source.Modifiers.IsReadonly;
    }

    private static CastOperator Copy(IMistType from, IMistType to, ConcreteType clone, bool isImplicit)
    {
        var modifiers = new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true };

        return new CastOperator(to, new Parameter("value", from), isImplicit, modifiers)
        {
            DeclaringType = clone,
            CopiesClone = true,
        };
    }
}