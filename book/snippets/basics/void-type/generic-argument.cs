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

// Using Result<MistVoid> to represent success with no value:
public static Result<MistVoid> SuccessResult()
{
    return new Result<MistVoid>(MistVoid.Instance, true);
}

var success = SuccessResult();
Console.WriteLine($"IsSuccess: {success.IsSuccess()}");
