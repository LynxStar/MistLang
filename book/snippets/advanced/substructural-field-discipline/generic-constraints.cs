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

    public int GetFd()
    {
        return this.Fd;
    }

    public void Drop()
    {
        System.Console.WriteLine(System.FormattableString.Invariant($"FileHandle({this.Fd}).Drop()"));
    }
}

public class LinearBox<T>
{
    [System.Diagnostics.CodeAnalysis.AllowNull]
    private T Value;

    public LinearBox(T value)
    {
        this.Value = value;
    }

    public T Drop()
    {
        System.Console.WriteLine("LinearBox<T>.Drop()");
        return this.Value;
    }
}

public class Program
{
    public static void Main()
    {
        Snippets.LinearBox<Snippets.FileHandle> box = new Snippets.LinearBox<Snippets.FileHandle>(new Snippets.FileHandle(42));
        try
        {
            System.Console.WriteLine("Boxed resource created");
        }
        finally
        {
            (box.Drop()).Drop();
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
