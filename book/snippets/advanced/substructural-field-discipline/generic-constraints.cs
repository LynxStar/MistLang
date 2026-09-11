using System;

namespace Snippets;

public class FileHandle
{
    private int Fd;

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
        Console.WriteLine($"FileHandle({Fd}).Drop()");
    }
}

public class LinearBox<T>
{
    private T Value;

    public LinearBox(T value)
    {
        this.Value = value;
    }

    public T Drop()
    {
        Console.WriteLine("LinearBox<T>.Drop()");
        return Value;
    }
}

public class Program
{
    public static void Main()
    {
        Snippets.LinearBox<Snippets.FileHandle> box = new Snippets.LinearBox<Snippets.FileHandle>(new Snippets.FileHandle(42));
        try
        {
            Console.WriteLine("Boxed resource created");
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
