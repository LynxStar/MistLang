using Standard;

namespace Snippet;

public class Connection
{
    public string Handle;
    public string Label;
    public int RetryCount;

    public Connection(string handle, string label)
    {
        this.Handle = "";
        this.Label = "";
        this.RetryCount = default(int);
        this.Handle = handle;
        this.Label = label;
        this.RetryCount = 0;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"Connection(handle={Handle}, label={Label}, retries={RetryCount})");
    }
}

public class JaggedOwnershipDemo
{
    public static void Run()
    {
        Snippet.Connection conn = new Snippet.Connection("socket-1", "primary");
        conn.Label = "renamed";
        conn.RetryCount = 3;
        conn.Handle = "socket-2";
        System.Console.WriteLine($"jagged={conn.Describe()}");
    }

    public JaggedOwnershipDemo()
    {
    }

    public static JaggedOwnershipDemo __CreateDefault()
    {
        return new JaggedOwnershipDemo();
    }
}
