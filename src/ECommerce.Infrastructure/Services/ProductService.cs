using ECommerce.Application.Abstractions;
using ECommerce.Domain;
using ECommerce.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Infrastructure.Services;

public class ProductService(AppDbContext context) : IProductService
{
    public async Task<IEnumerable<Product>> GetProductsAsync(string? category, decimal? minPrice, decimal? maxPrice)
    {
        var query = context.Products.AsQueryable();

        if (!string.IsNullOrEmpty(category))
            query = query.Where(p => p.Category == category);

        if (minPrice.HasValue)
            query = query.Where(p => p.Price >= minPrice.Value);

        if (maxPrice.HasValue)
            query = query.Where(p => p.Price <= maxPrice.Value);

        return await query.ToListAsync();
    }

    public async Task CreateProductAsync(Product product)
    {
        if (string.IsNullOrWhiteSpace(product.SKU))
        {
            throw new ArgumentException("SKU повинен бути заповнений", nameof(product.SKU));
        }

        var existing = await context.Products.AnyAsync(p => p.SKU == product.SKU);
        if (existing)
        {
            throw new InvalidOperationException("SKU має бути унікальним");
        }

        context.Products.Add(product);
        await context.SaveChangesAsync();
    }
}