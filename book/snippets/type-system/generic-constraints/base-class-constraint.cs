class Animal
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

class AnimalHandler<T>
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
