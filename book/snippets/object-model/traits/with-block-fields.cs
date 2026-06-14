using System;
using System.Text.RegularExpressions;

namespace Snippet;

interface Printable
{
    string Name { get; }
    int Id { get; }
}
public class Product : Printable
{
    public string Title { get; set; }

    public int Sku { get; set; }

    public Product(string title, int sku)
    {
        this.Title = title;
        this.Sku = sku;
    }

    string Printable.Name => this.Title;

    int Printable.Id => this.Sku;
}
