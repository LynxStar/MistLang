using Standard;

namespace Snippet;

public class GuideDog : Snippet.Dog
{
    private string handler;

    public GuideDog(string name, int age, string breed, string handler) : base(name, age, breed)
    {
        this.handler = "";
        this.handler = handler;
    }

    public string GetHandler()
    {
        return handler;
    }
}
