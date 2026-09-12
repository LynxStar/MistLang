using Standard;

namespace Snippet;

public sealed record class MixedResult(Union_Int32_String Value)
{
    public static readonly MixedResult Success = new MixedResult(0);
    public static readonly MixedResult ErrorMsg = new MixedResult("error");

    public static implicit operator Union_Int32_String(MixedResult e) => e.Value;

    public static explicit operator MixedResult?(Union_Int32_String value)
    {
        if(Equals(value, MixedResult.Success.Value)) return MixedResult.Success;
        if(Equals(value, MixedResult.ErrorMsg.Value)) return MixedResult.ErrorMsg;
        return null;
    }

    public override string ToString()
    {
        if(ReferenceEquals(this, MixedResult.Success)) return "Success";
        if(ReferenceEquals(this, MixedResult.ErrorMsg)) return "ErrorMsg";
        return Value.ToString();
    }
}
