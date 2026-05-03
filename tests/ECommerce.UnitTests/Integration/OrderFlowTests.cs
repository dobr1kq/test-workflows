using System.Net.Http.Json;
using ECommerce.Domain;
using ECommerce.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

public class OrderFlowTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public OrderFlowTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-User-Id", "test-user");
    }

    [Fact]
    public async Task GetProducts_ShouldReturnSuccessAndNotEmptyList()
    {
        var response = await _client.GetAsync("/api/products");

        response.EnsureSuccessStatusCode();
        var products = await response.Content.ReadFromJsonAsync<List<Product>>();
        Assert.NotNull(products);
        Assert.NotEmpty(products!);
    }

    [Fact]
    public async Task Checkout_WithoutItems_ShouldReturnBadRequest()
    {
        var userId = $"empty-user-{Guid.NewGuid()}";
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", userId);

        var response = await client.PostAsync("/api/cart/checkout", null);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FullOrderFlow_ShouldSucceed()
    {
        var userId = $"flow-user-{Guid.NewGuid()}";
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", userId);

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Integration Product",
            Price = 12.34m,
            StockQuantity = 10,
            SKU = Guid.NewGuid().ToString()
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Products.Add(product);
            await context.SaveChangesAsync();
        }

        var addItemResponse = await client.PostAsJsonAsync("/api/cart/items", new { ProductId = product.Id, Quantity = 1 });
        Assert.True(addItemResponse.IsSuccessStatusCode);

        var checkoutResponse = await client.PostAsync("/api/cart/checkout", null);
        Assert.True(checkoutResponse.IsSuccessStatusCode);

        var orders = await client.GetFromJsonAsync<List<Order>>("/api/orders");
        Assert.NotNull(orders);
        Assert.Contains(orders!, o => o.Items.Any(i => i.ProductId == product.Id));
    }

    [Fact]
    public async Task AddItem_ShouldFail_WhenStockInsufficient()
    {
        var userId = $"stock-user-{Guid.NewGuid()}";
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", userId);

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Limited Stock",
            Price = 100m,
            StockQuantity = 2,
            SKU = Guid.NewGuid().ToString()
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Products.Add(product);
            await context.SaveChangesAsync();
        }

        var addItemResponse = await client.PostAsJsonAsync("/api/cart/items", new { ProductId = product.Id, Quantity = 10 });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, addItemResponse.StatusCode);
        var error = await addItemResponse.Content.ReadAsStringAsync();
        Assert.Contains("Недостатньо товару", error);
    }
}

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // In EF Core 9+, AddDbContext stores the options action as
            // IDbContextOptionsConfiguration<T> services that are applied
            // when building DbContextOptions. Remove all of them plus the
            // typed options to fully replace the provider.
            var toRemove = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(IDbContextOptionsConfiguration<AppDbContext>))
                .ToList();

            foreach (var d in toRemove)
                services.Remove(d);

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase("IntegrationTestDb"));
        });
    }
}
