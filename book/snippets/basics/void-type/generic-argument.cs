using Standard;

using Snippet;

static Snippet.Result<Standard.Void> SuccessResult()
{
    return new Snippet.Result<Standard.Void>(Standard.Void.Instance, true);
}
Snippet.Result<Standard.Void> success = SuccessResult();
System.Console.WriteLine($"IsSuccess: {success.IsSuccess()}");

namespace Snippet
{
    public class Result<T>
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        private T value;
        private bool success;

        public Result(T value, bool success)
        {
            this.success = default(bool);
            this.value = value;
            this.success = success;
        }

        public bool IsSuccess()
        {
            return this.success;
        }
    }
}
