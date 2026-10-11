using Standard;

namespace Snippet;

public interface IGreetable
{
    string Name();

    string Greet()
    {
        return $"Hello, I am {this.Name()}";
    }
}

public class FriendlyDog : Snippet.IGreetable
{
    private string dogName;

    public FriendlyDog(string dogName)
    {
        this.dogName = "";
        this.dogName = dogName;
    }

    public string Name()
    {
        return dogName;
    }

    public string Greet()
    {
        return $"Hello, I am {this.Name()}";
    }
}
