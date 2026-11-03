using System.Security.Claims;
using PlaymoxAPI.BLL.BO.Platform;
using PlaymoxAPI.Extensions.Auth;
using PlaymoxAPI.Services.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PlaymoxAPI.Controllers;

[BaseAuth]
[ApiController]
[Route("api/instances")]
public class InstancesController : ControllerBase
{
    private readonly InstanceLifecycleService instanceLifecycleService;

    public InstancesController(InstanceLifecycleService instanceLifecycleService)
    {
        this.instanceLifecycleService = instanceLifecycleService;
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var userContext = GetUserContext();
        var instances = await instanceLifecycleService.GetInstancesForUserAsync(
            userContext.UserId,
            userContext.IsAdmin,
            cancellationToken);
        return Ok(instances);
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateVmRequest request, CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return BadRequest("Request payload is required.");
        }

        var userContext = GetUserContext();
        try
        {
            var result = await instanceLifecycleService.EnqueueCreateVmAsync(userContext.UserId, request, cancellationToken);
            return Accepted(new JobAcceptedResponse
            {
                JobId = result.JobId,
                VirtualGameInstanceId = result.InstanceId,
                Status = result.Status,
                AlreadyExisted = result.AlreadyExisted
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpPost("{id:long}/start")]
    public Task<IActionResult> Start(long id, [FromBody] OperationRequest? request, CancellationToken cancellationToken)
        => EnqueueOperation(id, ProvisioningOperation.StartVm, request?.IdempotencyKey, cancellationToken);

    [Authorize]
    [HttpPost("{id:long}/stop")]
    public Task<IActionResult> Stop(long id, [FromBody] OperationRequest? request, CancellationToken cancellationToken)
        => EnqueueOperation(id, ProvisioningOperation.StopVm, request?.IdempotencyKey, cancellationToken);

    [Authorize]
    [HttpPost("{id:long}/force-stop")]
    public async Task<IActionResult> ForceStop(long id, CancellationToken cancellationToken)
    {
        var userContext = GetUserContext();
        try
        {
            var result = await instanceLifecycleService.ForceStopInstanceAsync(
                userContext.UserId,
                userContext.IsAdmin,
                id,
                cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpPost("{id:long}/shutdown")]
    public Task<IActionResult> Shutdown(long id, [FromBody] OperationRequest? request, CancellationToken cancellationToken)
        => EnqueueOperation(id, ProvisioningOperation.ShutdownVm, request?.IdempotencyKey, cancellationToken);

    [Authorize]
    [HttpPost("{id:long}/restart")]
    public Task<IActionResult> Restart(long id, [FromBody] OperationRequest? request, CancellationToken cancellationToken)
        => EnqueueOperation(id, ProvisioningOperation.RestartVm, request?.IdempotencyKey, cancellationToken);

    [Authorize]
    [HttpDelete("{id:long}")]
    public Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
        => EnqueueOperation(id, ProvisioningOperation.DeleteVm, null, cancellationToken);

    [Authorize]
    [HttpGet("{id:long}/game-runtime")]
    public async Task<IActionResult> GetGameRuntime(long id, CancellationToken cancellationToken)
    {
        var userContext = GetUserContext();
        try
        {
            var runtime = await instanceLifecycleService.GetGameRuntimeInfoAsync(
                userContext.UserId,
                userContext.IsAdmin,
                id,
                cancellationToken);
            return Ok(runtime);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpGet("{id:long}/ssh-preview")]
    public async Task<IActionResult> GetSshPreview(long id, [FromQuery] int tailLines = 250, CancellationToken cancellationToken = default)
    {
        var userContext = GetUserContext();
        try
        {
            var preview = await instanceLifecycleService.GetSshTerminalPreviewAsync(
                userContext.UserId,
                userContext.IsAdmin,
                id,
                tailLines,
                cancellationToken);
            return Ok(preview);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpGet("{id:long}/live-command-stream")]
    public async Task<IActionResult> GetLiveCommandStream(long id, CancellationToken cancellationToken = default)
    {
        var userContext = GetUserContext();
        try
        {
            var stream = await instanceLifecycleService.GetLiveCommandStreamAsync(
                userContext.UserId,
                userContext.IsAdmin,
                id,
                cancellationToken);
            return Ok(stream);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpPost("{id:long}/game/reinstall")]
    public async Task<IActionResult> ReinstallGameServer(long id, [FromBody] OperationRequest? request, CancellationToken cancellationToken)
    {
        var userContext = GetUserContext();
        try
        {
            var result = await instanceLifecycleService.EnqueueGameReinstallAsync(
                userContext.UserId,
                userContext.IsAdmin,
                id,
                request?.IdempotencyKey,
                cancellationToken);
            return Accepted(new JobAcceptedResponse
            {
                JobId = result.JobId,
                VirtualGameInstanceId = result.InstanceId,
                Status = result.Status,
                AlreadyExisted = result.AlreadyExisted
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpGet("{id:long}/game/config")]
    public async Task<IActionResult> GetGameConfiguration(long id, [FromQuery] bool refresh = false, CancellationToken cancellationToken = default)
    {
        var userContext = GetUserContext();
        try
        {
            var response = await instanceLifecycleService.GetGameConfigurationAsync(
                userContext.UserId,
                userContext.IsAdmin,
                id,
                refresh,
                cancellationToken);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpPut("{id:long}/game/config")]
    public async Task<IActionResult> UpdateGameConfiguration(long id, [FromBody] UpdateGameConfigurationRequest request, CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return BadRequest("Request payload is required.");
        }

        var userContext = GetUserContext();
        try
        {
            var result = await instanceLifecycleService.EnqueueGameConfigurationUpdateAsync(
                userContext.UserId,
                userContext.IsAdmin,
                id,
                request.ConfigurationJson,
                request.IdempotencyKey,
                cancellationToken);
            return Accepted(new JobAcceptedResponse
            {
                JobId = result.JobId,
                VirtualGameInstanceId = result.InstanceId,
                Status = result.Status,
                AlreadyExisted = result.AlreadyExisted
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpGet("{id:long}/game/backup")]
    public async Task<IActionResult> DownloadGameBackup(long id, CancellationToken cancellationToken)
    {
        var userContext = GetUserContext();
        try
        {
            var backup = await instanceLifecycleService.CreateGameBackupAsync(
                userContext.UserId,
                userContext.IsAdmin,
                id,
                cancellationToken);
            return File(backup.Content, backup.ContentType, backup.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpPost("{id:long}/game/restore")]
    [RequestSizeLimit(500 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 500 * 1024 * 1024)]
    public async Task<IActionResult> RestoreGameBackup(long id, [FromForm] RestoreGameBackupRequest request, CancellationToken cancellationToken)
    {
        if (request?.Archive == null || request.Archive.Length == 0)
        {
            return BadRequest("Backup archive file is required.");
        }

        if (request.Archive.Length > 500L * 1024 * 1024)
        {
            return BadRequest("Backup archive is too large. Max size is 500MB.");
        }

        var tempArchivePath = Path.Combine(
            Path.GetTempPath(),
            $"playmox-restore-{id}-{Guid.NewGuid():N}.tmp");

        await using (var stream = request.Archive.OpenReadStream())
        await using (var file = new FileStream(
                         tempArchivePath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         1024 * 1024,
                         FileOptions.SequentialScan))
        {
            await stream.CopyToAsync(file, cancellationToken);
        }

        var userContext = GetUserContext();
        try
        {
            await instanceLifecycleService.RestoreGameBackupAsync(
                userContext.UserId,
                userContext.IsAdmin,
                id,
                tempArchivePath,
                request.Archive.Length,
                cancellationToken);
            return Ok("Backup restored.");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        finally
        {
            try
            {
                if (System.IO.File.Exists(tempArchivePath))
                {
                    System.IO.File.Delete(tempArchivePath);
                }
            }
            catch
            {
                // Best-effort cleanup of temporary upload file.
            }
        }
    }

    [Authorize]
    [HttpPost("{id:long}/reset-password")]
    public async Task<IActionResult> ResetPassword(long id, [FromBody] ResetPasswordRequest? request, CancellationToken cancellationToken)
    {
        var userContext = GetUserContext();
        try
        {
            var result = await instanceLifecycleService.EnqueuePasswordResetAsync(
                userContext.UserId,
                userContext.IsAdmin,
                id,
                request?.Password,
                request?.IdempotencyKey,
                cancellationToken);
            return Accepted(new JobAcceptedResponse
            {
                JobId = result.JobId,
                VirtualGameInstanceId = result.InstanceId,
                Status = result.Status,
                AlreadyExisted = result.AlreadyExisted
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpPut("{id:long}/resources")]
    public async Task<IActionResult> UpdateResources(long id, [FromBody] UpdateVmResourcesRequest request, CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return BadRequest("Request payload is required.");
        }

        var userContext = GetUserContext();
        try
        {
            var result = await instanceLifecycleService.EnqueueResourceUpdateAsync(
                userContext.UserId,
                userContext.IsAdmin,
                id,
                request,
                cancellationToken);
            return Accepted(new JobAcceptedResponse
            {
                JobId = result.JobId,
                VirtualGameInstanceId = result.InstanceId,
                Status = result.Status,
                AlreadyExisted = result.AlreadyExisted
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private async Task<IActionResult> EnqueueOperation(long id, ProvisioningOperation operation, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var userContext = GetUserContext();
        try
        {
            var result = await instanceLifecycleService.EnqueueVmOperationAsync(
                userContext.UserId,
                userContext.IsAdmin,
                id,
                operation,
                idempotencyKey,
                cancellationToken);
            return Accepted(new JobAcceptedResponse
            {
                JobId = result.JobId,
                VirtualGameInstanceId = result.InstanceId,
                Status = result.Status,
                AlreadyExisted = result.AlreadyExisted
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
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

public class JobAcceptedResponse
{
    public long JobId { get; set; }
    public long? VirtualGameInstanceId { get; set; }
    public string Status { get; set; } = nameof(ProvisioningJobStatus.Queued);
    public bool AlreadyExisted { get; set; }
}

public class OperationRequest
{
    public string? IdempotencyKey { get; set; }
}

public class ResetPasswordRequest : OperationRequest
{
    public string? Password { get; set; }
}

public class UpdateGameConfigurationRequest : OperationRequest
{
    public string ConfigurationJson { get; set; } = "{}";
}

public class RestoreGameBackupRequest
{
    public IFormFile? Archive { get; set; }
}
