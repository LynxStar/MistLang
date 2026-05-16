using System;

namespace GenericTypes;

interface IGenericContainer<T>
{
    T GetItem();
}
