using Standard;

namespace Snippet;

internal class Animal
{
    public string Species;

    public Animal(string species)
    {
        this.Species = "";
        this.Species = species;
    }

    public string GetSpecies()
    {
        return this.Species;
    }
}

internal class AnimalHandler<T>
    where T : Snippet.Animal
{
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public T Animal;

    public AnimalHandler(T animal)
    {
        this.Animal = animal;
    }

    public string GetSpeciesName()
    {
        return this.Animal.GetSpecies();
    }
}
