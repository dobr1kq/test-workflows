using ECommerce.Domain; // Потрібно додати reference на Domain

namespace ECommerce.Application.Abstractions;

public interface IProductService
{
    Task<IEnumerable<Product>> GetProductsAsync(string? category, decimal? minPrice, decimal? maxPrice);
    Task CreateProductAsync(Product product);
}