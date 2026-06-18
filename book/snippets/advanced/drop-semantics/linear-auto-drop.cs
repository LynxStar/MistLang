using System;

namespace Snippets;

public class FileHandle
{
    int Fd;

    public FileHandle(int fd)
    {
        this.Fd = fd;
    }

    public void Drop()
    {
        System.Console.WriteLine($"FileHandle({Fd}) closed");
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
        var f = new FileHandle(42);
        try
        {
            System.Console.WriteLine($"Using handle {f.GetFd()}");
        }
        finally
        {
            f.Drop();
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
