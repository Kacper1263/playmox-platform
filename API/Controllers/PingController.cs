using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PlaymoxAPI.Controllers;

[AllowAnonymous]
[ApiController]
[Route("[controller]")]
[Route("api/[controller]")]
public class PingController : Controller
{
    [HttpGet]
    public IActionResult Ping()
    {
        return Ok("Pong");
    }
}