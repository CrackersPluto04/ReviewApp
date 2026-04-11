using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ReviewController : ControllerBase
{
    private readonly AppDbContext _context;

    public ReviewController(AppDbContext context)
    {
        _context = context;
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreateReview(ReviewMediaDto request)
    {
        // Implementation for creating a review will go here
        return Ok();
    }
}
