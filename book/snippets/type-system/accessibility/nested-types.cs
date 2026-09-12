using Standard;

namespace Snippet;

public class Outer
{
    private int secret;

    public Outer(int secret)
    {
        this.secret = secret;
    }

    public int ExposeViaInner()
    {
        Snippet.Outer.Inner inner = new Snippet.Outer.Inner(this);
        return inner.ReadSecret();
    }

    private class Inner
    {
        private Snippet.Outer owner;

        public Inner(Snippet.Outer owner)
        {
            this.owner = owner;
        }

        public int ReadSecret()
        {
            return owner.secret;
        }
    }
}
