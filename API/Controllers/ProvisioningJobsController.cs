using System.Security.Claims;
using PlaymoxAPI.Extensions.Auth;
using PlaymoxAPI.Services.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PlaymoxAPI.Controllers;

[BaseAuth]
[ApiController]
[Route("api/jobs")]
public class ProvisioningJobsController : ControllerBase
{
    private readonly InstanceLifecycleService instanceLifecycleService;

    public ProvisioningJobsController(InstanceLifecycleService instanceLifecycleService)
    {
        this.instanceLifecycleService = instanceLifecycleService;
    }

    [Authorize]
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetJob(long id, CancellationToken cancellationToken)
    {
        var userContext = GetUserContext();
        var job = await instanceLifecycleService.GetJobAsync(userContext.UserId, userContext.IsAdmin, id, cancellationToken);
        if (job == null)
        {
            return NotFound("Job not found.");
        }

        return Ok(job);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] long? instanceId, CancellationToken cancellationToken)
    {
        var userContext = GetUserContext();
        var jobs = await instanceLifecycleService.GetJobsAsync(userContext.UserId, userContext.IsAdmin, instanceId, cancellationToken);
        return Ok(jobs);
    }

    private (long UserId, bool IsAdmin) GetUserContext()
    {
        var sid = User.Claims.SingleOrDefault(x => x.Type == ClaimTypes.Sid)?.Value;
        if (!long.TryParse(sid, out var userId))
        {
            throw new InvalidOperationException("Invalid authenticated user context.");
        }

        var isAdmin = User.Claims.Any(x => x.Type == ClaimTypes.Role &&
                                           string.Equals(x.Value, "Admin", StringComparison.OrdinalIgnoreCase));
        return (userId, isAdmin);
    }
}
