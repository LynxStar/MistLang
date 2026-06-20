using System;

namespace Snippets;

public class FileHandle
{
    int Fd;

    public FileHandle(int fd)
    {
        this.Fd = fd;
    }

    public int GetFd()
    {
        return Fd;
    }

    public void Drop()
    {
        System.Console.WriteLine($"FileHandle({Fd}).Drop()");
    }
}
public class LinearBox<T>
{
    T Value;

    public LinearBox(T value)
    {
        this.Value = value;
    }

    public T Drop()
    {
        System.Console.WriteLine("LinearBox<T>.Drop()");
        return Value;
        ((dynamic)this.Value).Drop();
    }
}
public class Program
{
    public static void Main()
    {
        var box = new LinearBox<FileHandle>(new FileHandle(42));
        try
        {
            System.Console.WriteLine("Boxed resource created");
        }
        finally
        {
            (box.Drop()).Drop();
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
