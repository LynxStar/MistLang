using System;

namespace Snippet;

public class Measurement
{
    private string unit;

    public Measurement(string unit)
    {
        this.unit = unit;
    }
}
public class Weight : Measurement
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

    public static explicit operator Kelvin(Weight w)
    {
        return new Kelvin(w.GetValue());
    }
}
