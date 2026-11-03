using Standard;
using System.Collections.Generic;

using Snippet;

{
    var __e1309 = "Mist Meetup";
    MistContext.Push<string>(__e1309);
    try
    {
        Snippet.TicketDesk<Const_1, Const_2> desk = new Snippet.TicketDesk<Const_1, Const_2>();
        try
        {
            Snippet.UserId ada = ((Snippet.UserId)(42));
            Snippet.UserId grace = ((Snippet.UserId)(7));
            Snippet.UserId linus = ((Snippet.UserId)(11));
            System.Collections.Generic.Dictionary<Snippet.UserId, string> requests = new System.Collections.Generic.Dictionary<Snippet.UserId, string>();
            requests.Add(ada, "A1");
            requests.Add(grace, "A2");
            requests.Add(linus, "A1");
            foreach (System.Collections.Generic.KeyValuePair<Snippet.UserId, string> request in requests)
            {
                {
                    if ((request.Value is string and var __e1310 and var seat && System.Text.RegularExpressions.Regex.IsMatch(__e1310, "\\A(?:" + "[A-Z][1-9]" + "(?#\n(?#))\\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant)))
                    {
                        System.Console.WriteLine(desk.Reserve(request.Key, seat));
                    }
                }
            }
        }
        finally
        {
            desk.Drop();
        }
    }
    finally
    {
        MistContext.Pop<string>();
    }
}

namespace Snippet
{
    #pragma warning disable CS0660, CS0661
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal readonly struct UserId : Standard.IClonable<Snippet.UserId>
    {
        public readonly int Value;

        public UserId(int value)
        {
            Value = value;
        }

        public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private UserId(int p0, Mist.Emission.CSharp.__StructInit marker)
        {
            this.Value = p0;
        }

        internal static UserId __FromParts(int p0, Mist.Emission.CSharp.__StructInit marker)
        {
            return new UserId(p0, default(Mist.Emission.CSharp.__StructInit));
        }

        public Snippet.UserId Clone()
            => new Snippet.UserId(Value);

        public bool IsEven()
        {
            return this % 2 == 0;
        }

        public static implicit operator long(Snippet.UserId value)
            => (long)(value.Value);

        public static implicit operator float(Snippet.UserId value)
            => (float)(value.Value);

        public static implicit operator double(Snippet.UserId value)
            => (double)(value.Value);

        public static implicit operator decimal(Snippet.UserId value)
            => (decimal)(value.Value);

        public static implicit operator int(Snippet.UserId value)
            => value.Value;

        public static explicit operator Snippet.UserId(int value)
            => new Snippet.UserId(value);

        public static Snippet.UserId operator +(Snippet.UserId a, Snippet.UserId b)
            => new Snippet.UserId((a.Value + b.Value));

        public static Snippet.UserId operator -(Snippet.UserId a, Snippet.UserId b)
            => new Snippet.UserId((a.Value - b.Value));

        public static Snippet.UserId operator *(Snippet.UserId a, Snippet.UserId b)
            => new Snippet.UserId((a.Value * b.Value));

        public static Standard.Result__Snippet_UserId__Standard_DivisionError operator /(Snippet.UserId a, Snippet.UserId b)
        {
            var inspected = Standard.ResultArithmetic.DivideChecked(a.Value, b.Value).Inspect();
            if (inspected.IsTuple_2_Boolean_DivisionError)
            {
                return Standard.Result__Snippet_UserId__Standard_DivisionError.Failure(inspected.AsTuple_2_Boolean_DivisionError.Item2);
            }
            return Standard.Result__Snippet_UserId__Standard_DivisionError.Success(new Snippet.UserId(inspected.AsTuple_2_Boolean_Int32.Item2));
        }

        public static Standard.Result__Snippet_UserId__Standard_DivisionError operator %(Snippet.UserId a, Snippet.UserId b)
        {
            var inspected = Standard.ResultArithmetic.RemainderChecked(a.Value, b.Value).Inspect();
            if (inspected.IsTuple_2_Boolean_DivisionError)
            {
                return Standard.Result__Snippet_UserId__Standard_DivisionError.Failure(inspected.AsTuple_2_Boolean_DivisionError.Item2);
            }
            return Standard.Result__Snippet_UserId__Standard_DivisionError.Success(new Snippet.UserId(inspected.AsTuple_2_Boolean_Int32.Item2));
        }

        public static bool operator <(Snippet.UserId a, Snippet.UserId b)
            => (a.Value < b.Value);

        public static bool operator >(Snippet.UserId a, Snippet.UserId b)
            => (a.Value > b.Value);

        public static bool operator <=(Snippet.UserId a, Snippet.UserId b)
            => (a.Value <= b.Value);

        public static bool operator >=(Snippet.UserId a, Snippet.UserId b)
            => (a.Value >= b.Value);

        public static bool operator ==(Snippet.UserId a, Snippet.UserId b)
            => (a.Value == b.Value);

        public static bool operator !=(Snippet.UserId a, Snippet.UserId b)
            => (a.Value != b.Value);

        public static Snippet.UserId operator -(Snippet.UserId a)
            => new Snippet.UserId((-a.Value));
    }
    #pragma warning restore CS0660, CS0661

    internal class TicketDesk<TRows, TDepth>
        where TRows : IConstIntegral
        where TDepth : IConstIntegral
    {
        private System.Collections.Generic.List<string> bookings = new System.Collections.Generic.List<string>();
        public static int Rows => ((int)TRows.Value);
        public static int Depth => ((int)TDepth.Value);

        public string Reserve(Snippet.UserId guest, string seat)
        {
            var eventName = (string)MistContext.Peek<string>();
            string label = seat;
            if (label[0] - 'A' >= ((int)TRows.Value) || label[1] - '1' >= ((int)TDepth.Value))
            {
                return "Seat outside the venue";
            }
            if (this.bookings.Count >= ((int)TRows.Value) * ((int)TDepth.Value))
            {
                return "Sold out";
            }
            if (this.bookings.Contains(seat))
            {
                return "Seat already reserved";
            }
            this.bookings.Add(seat);
            return $"{eventName}: guest {guest}, seat {seat}";
        }

        public void Drop()
        {
            System.Console.WriteLine(System.FormattableString.Invariant($"Desk closed with {this.bookings.Count} reservations"));
        }

        public TicketDesk()
        {
        }

        public static TicketDesk<TRows, TDepth> __CreateDefault()
        {
            return new TicketDesk<TRows, TDepth>();
        }
    }
}
