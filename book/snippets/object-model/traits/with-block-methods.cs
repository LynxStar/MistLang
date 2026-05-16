namespace GenericTraits;

interface Writable
{
    void Write(string data);
}

public class Logger
{
    public Logger()
    {
    }

    public void Log(string data)
    {
    }

    void Writable.Write(string data) => this.Log(data);
}
