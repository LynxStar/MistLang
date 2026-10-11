using Standard;

namespace Snippet;

public class Robot : Snippet.IFullyDescribable
{
    private string model;
    private int version;

    public Robot(string model, int version)
    {
        this.model = "";
        this.version = default(int);
        this.model = model;
        this.version = version;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"Robot: {model} v{version}");
    }

    public string DetailedDescribe()
    {
        return System.FormattableString.Invariant($"Robot model {model}, version {version}, fully operational");
    }
}
