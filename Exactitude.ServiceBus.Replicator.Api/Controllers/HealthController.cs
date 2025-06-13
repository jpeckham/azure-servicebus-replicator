using Microsoft.AspNetCore.Mvc;

namespace Exactitude.ServiceBus.Replicator.Api.Controllers;

[ApiController]
[Route("/")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { status = "healthy" });
    }
}