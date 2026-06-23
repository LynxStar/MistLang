using System;
using System.Text.RegularExpressions;

namespace Snippet;

public class Product
{
    public string Title { get; set; }

    public int Sku { get; set; }

    public Product(string title, int sku)
    {
        this.Title = title;
        this.Sku = sku;
    }
}
