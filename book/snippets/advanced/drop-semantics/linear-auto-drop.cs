using System;

namespace Snippets;

public class FileHandle
{
    private int Fd;

    public FileHandle(int fd)
    {
        this.Fd = fd;
    }

    public void Drop()
    {
        Console.WriteLine($"FileHandle({Fd}) closed");
    }

    public int GetFd()
    {
        return Fd;
    }
}

public class Program
{
    public static void Main()
    {
        Snippets.FileHandle f = new Snippets.FileHandle(42);
        try
        {
            Console.WriteLine($"Using handle {f.GetFd()}");
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
