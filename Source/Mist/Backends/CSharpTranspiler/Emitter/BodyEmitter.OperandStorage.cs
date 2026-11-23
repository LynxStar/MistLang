using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static partial class BodyEmitter
{
    /// <summary>
    /// Whether the expression is written as a bare <c>new T[n]</c> or <c>stackalloc T[n]</c>. Its
    /// last token is a rank specifier, so a <c>*</c> after it extends the type to a pointer and a
    /// <c>[</c> to a jagged one, which is why <see cref="EmitSubExpression"/> parenthesizes it as an
    /// operand. A filled allocation is already a call and needs nothing.
    /// </summary>
    private static bool IsBareStorage(Expression expression)
    {
        return expression switch
        {
            ArrayCreationExpression creation => StorageStamping.Fill(creation.ElementType, onStack: false) is null,
            StackallocExpression allocation => StorageStamping.Fill(allocation.ElementType, onStack: true) is null,
            ResultIdentityExpression identity => IsBareStorage(identity.Operand),
            AsExpression { Kind: AsConversionKind.Identity } identity => IsBareStorage(identity.Operand),
            _ => false,
        };
    }
}