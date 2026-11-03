using PlaymoxAPI.BLL;
using PlaymoxAPI.Extensions.Auth;
using PlaymoxAPI.Services.Proxmox;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PlaymoxAPI.Controllers.Admin;

[BaseAuth]
[ApiController]
[Route("api/admin/proxmox-diagnostics")]
public class ProxmoxDiagnosticsController : ControllerBase
{
    private readonly ProxmoxAutomationClient proxmoxAutomationClient;
    private readonly PlaymoxContext dbContext;

    public ProxmoxDiagnosticsController(ProxmoxAutomationClient proxmoxAutomationClient, PlaymoxContext dbContext)
    {
        this.proxmoxAutomationClient = proxmoxAutomationClient;
        this.dbContext = dbContext;
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("vm/{vmId:int}")]
    public async Task<IActionResult> GetVmDiagnostics(int vmId, CancellationToken cancellationToken)
    {
        var isManaged = await dbContext.VirtualGameInstances
            .AsNoTracking()
            .AnyAsync(x => x.VmId == vmId, cancellationToken);
        if (!isManaged)
        {
            return NotFound("VM is not managed by Playmox.");
        }

        var diagnostics = await proxmoxAutomationClient.GetVmDiagnosticsAsync(vmId, cancellationToken);
        return Ok(diagnostics);
    }
}
