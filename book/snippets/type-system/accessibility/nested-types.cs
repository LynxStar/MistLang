using System;

namespace Snippet;

private class Outer
{
    private int secret;

    public Outer(int secret)
    {
        this.secret = secret;
    }

    private class Inner
    {
        private Outer owner;

        public Inner(Outer owner)
        {
            this.owner = owner;
        }

        public int ReadSecret()
        {
            return owner.secret;
        }
    }

    public int ExposeViaInner()
    {
        var inner = new Inner(this);
        return inner.ReadSecret();
    }
}
