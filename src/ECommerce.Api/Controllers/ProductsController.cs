using ECommerce.Application.Abstractions;
using ECommerce.Domain;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Product>>> GetProducts(
        [FromQuery] string? category, 
        [FromQuery] decimal? minPrice, 
        [FromQuery] decimal? maxPrice)
    {
        var products = await _productService.GetProductsAsync(category, minPrice, maxPrice);
        return Ok(products);
    }

    [HttpPost]
    public async Task<ActionResult<Product>> CreateProduct([FromBody] Product product)
    {
        try
        {
            if (product.Id == Guid.Empty)
                product.Id = Guid.NewGuid();

            await _productService.CreateProductAsync(product);
            return Created($"/api/products/{product.Id}", product);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}