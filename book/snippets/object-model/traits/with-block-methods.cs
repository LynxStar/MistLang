using System;

namespace GenericTraits;

interface Writable
{
    void Write(string data);
}
public class Logger : Writable
{
    public Logger()
    {
    }

    public void Log(string data)
    {
    }

    void Writable.Write(string data) => this.Log(data);

    public static Logger __CreateDefault()
    {
        var inst = new Logger();
        return inst;
    }
}
