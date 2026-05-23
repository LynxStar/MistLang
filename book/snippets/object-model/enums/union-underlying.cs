using Standard;
using System;

namespace EnumDemo;

public record class MixedResult(Union_Int32_String Value)
{
    public static readonly MixedResult Success = new MixedResult(0);
    public static readonly MixedResult ErrorMsg = new MixedResult("error");

    public static implicit operator Union_Int32_String(MixedResult e) => e.Value;
}
