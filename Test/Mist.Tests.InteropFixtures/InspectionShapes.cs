namespace Mist.Tests.InteropFixtures;

public class InspectionBase
{
    public int Tally()
    {
        return 1;
    }

    protected InspectionBase Duplicate()
    {
        return this;
    }
}

// What an inspection report has to say about a type: an inherited member, a protected one, an event and a
// finalizer, none of which the projection exposes the same way.
public class Inspected : InspectionBase
{
    public event Action? Changed;

    public static void Write(string text)
    {
        _ = text;
    }

    public void Touch()
    {
        Changed?.Invoke();
    }

    ~Inspected()
    {
        Changed = null;
    }
}

public static class Codec
{
    public static byte[] GetBytes(string text)
    {
        return new byte[text.Length];
    }

    public static unsafe int GetBytes(char* chars, int count)
    {
        return chars is null ? 0 : count;
    }
}