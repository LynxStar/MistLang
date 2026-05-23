using System;

namespace Snippet;

interface Readable
{
    string Read();
}
interface Writable
{
    void Write(string data);
}
interface ReadWriteStream : Readable, Writable
{
    void Flush();
}
