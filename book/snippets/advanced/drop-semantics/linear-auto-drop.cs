using Standard;

namespace Snippets;

public class FileHandle
{
    private int Fd;

    public FileHandle(int fd)
    {
        this.Fd = default(int);
        this.Fd = fd;
    }

    public void Drop()
    {
        System.Console.WriteLine(System.FormattableString.Invariant($"FileHandle({this.Fd}) closed"));
    }

    public int GetFd()
    {
        return this.Fd;
    }
}

public class Program
{
    public static void Main()
    {
        Snippets.FileHandle f = new Snippets.FileHandle(42);
        try
        {
            System.Console.WriteLine(System.FormattableString.Invariant($"Using handle {f.GetFd()}"));
        }
        finally
        {
            f.Drop();
        }
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
