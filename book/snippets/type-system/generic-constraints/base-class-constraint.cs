using System;

namespace Snippet;

internal class Animal
{
    public string Species;

    public Animal(string species)
    {
        Species = species;
    }

    public string GetSpecies()
    {
        return Species;
    }
}
internal class AnimalHandler<T>
    where T : Animal
{
    public T Animal;

    public AnimalHandler(T animal)
    {
        Animal = animal;
    }

    public string GetSpeciesName()
    {
        return Animal.GetSpecies();
    }
}
