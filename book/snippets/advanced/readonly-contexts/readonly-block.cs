using Standard;

namespace Snippet;

public class ReadonlyBlockDemo
{
    public int Value;

    public ReadonlyBlockDemo(int value)
    {
        this.Value = default(int);
        this.Value = value;
    }

    public string SafeRead()
    {
        string result = "";
        {
            int v = this.Value;
            result = System.FormattableString.Invariant($"SafeRead={v}");
        }
        return result;
    }

    public string MixedMethod()
    {
        int before = this.Value;
        this.Value = this.Value + 1;
        int after = 0;
        {
            after = this.Value;
        }
        return System.FormattableString.Invariant($"before={before}, after={after}");
    }
}
