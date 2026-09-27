using Standard;

namespace Snippet;

public class ReadonlyBlockDemo
{
    public int Value;

    public ReadonlyBlockDemo(int value)
    {
        this.Value = value;
    }

    public string SafeRead()
    {
        string result = "";
        {
            int v = Value;
            result = System.FormattableString.Invariant($"SafeRead={v}");
        }
        return result;
    }

    public string MixedMethod()
    {
        int before = Value;
        this.Value = Value + 1;
        int after = 0;
        {
            after = Value;
        }
        return System.FormattableString.Invariant($"before={before}, after={after}");
    }
}
