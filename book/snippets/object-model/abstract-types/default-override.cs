using Standard;

namespace Snippet;

public class FormalCat : Snippet.IGreetable
{
    private string catName;

    public FormalCat(string catName)
    {
        this.catName = catName;
    }

    public string Name()
    {
        return catName;
    }

    public string Greet()
    {
        return $"Good day. I am {this.Name()}, and I require attention.";
    }
}
