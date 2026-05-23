using System;

namespace Snippet;

public class Result<T>
{
    private T value;

    private bool success;

    public Result(T value, bool success)
    {
        this.value = value;
        this.success = success;
    }

    public bool IsSuccess()
    {
        return success;
    }
}
