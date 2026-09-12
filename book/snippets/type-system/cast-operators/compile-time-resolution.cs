using Standard;

namespace Snippet;

public class Measurement
{
    private string unit;

    public Measurement(string unit)
    {
        this.unit = unit;
    }
}

public class Weight : Snippet.Measurement
{
    private double value;

    public Weight(double value, string unit) : base(unit)
    {
        this.value = value;
    }

    public double GetValue()
    {
        return value;
    }

    public static explicit operator Snippet.Kelvin(Snippet.Weight w)
    {
        return new Snippet.Kelvin(w.GetValue());
    }
}
