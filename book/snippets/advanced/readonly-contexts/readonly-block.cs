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
        var result = "";
        {
            var v = Value;
            result = $"SafeRead={v}";
        }
        return result;
    }

    public string MixedMethod()
    {
        var before = Value;
        this.Value = Value + 1;
        var after = 0;
        {
            after = Value;
        }
        return $"before={before}, after={after}";
    }
}
