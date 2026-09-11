using System;

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
            result = $"SafeRead={v}";
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
        return $"before={before}, after={after}";
    }
}
