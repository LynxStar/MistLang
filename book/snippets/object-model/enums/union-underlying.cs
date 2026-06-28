using System;

namespace Snippet;

public record class MixedResult(Union_Int32_String Value)
{
    public static readonly MixedResult Success = new MixedResult(0);
    public static readonly MixedResult ErrorMsg = new MixedResult("error");

    public static implicit operator Union_Int32_String(MixedResult e) => e.Value;

    public static explicit operator MixedResult?(Union_Int32_String value)
    {
        if (Equals(value, Success.Value)) return Success;
        else if (Equals(value, ErrorMsg.Value)) return ErrorMsg;
        return null;
    }

    public override string ToString()
    {
        if (ReferenceEquals(this, Success)) return "Success";
        else if (ReferenceEquals(this, ErrorMsg)) return "ErrorMsg";
        return Value.ToString();
    }
}
