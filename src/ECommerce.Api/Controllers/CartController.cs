using ECommerce.Application.Abstractions;
using ECommerce.Domain;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CartController : ControllerBase
{
    private readonly ICartService _cartService;

    public CartController(ICartService cartService)
    {
        _cartService = cartService;
    }

    private string GetUserId() 
    {
        if (!Request.Headers.TryGetValue("X-User-Id", out var userId))
        {
            return "default-user";
        }
        return userId!;
    }

    [HttpGet]
    public async Task<ActionResult<Cart>> GetCart()
    {
        var cart = await _cartService.GetCartByUserIdAsync(GetUserId());
        return Ok(cart);
    }

    [HttpPost("items")]
    public async Task<IActionResult> AddItem([FromBody] AddItemRequest request)
    {
        try
        {
            await _cartService.AddItemAsync(GetUserId(), request.ProductId, request.Quantity);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("items/{productId}")]
    public async Task<IActionResult> UpdateQuantity(Guid productId, [FromBody] UpdateQuantityRequest request)
    {
        try
        {
            await _cartService.UpdateQuantityAsync(GetUserId(), productId, request.Quantity);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("items/{productId}")]
    public async Task<IActionResult> RemoveItem(Guid productId)
    {
        await _cartService.RemoveItemAsync(GetUserId(), productId);
        return NoContent();
    }

    [HttpPost("checkout")]
    public async Task<ActionResult<Order>> Checkout()
    {
        try
        {
            var order = await _cartService.CheckoutAsync(GetUserId());
            return Ok(order);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}

public record AddItemRequest(Guid ProductId, int Quantity);
public record UpdateQuantityRequest(int Quantity);