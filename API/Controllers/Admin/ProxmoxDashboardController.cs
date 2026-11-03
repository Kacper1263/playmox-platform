using PlaymoxAPI.Extensions.Auth;
using PlaymoxAPI.Services.Proxmox;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PlaymoxAPI.Controllers.Admin;

[BaseAuth]
[ApiController]
[Route("api/admin/proxmox-dashboard")]
public class ProxmoxDashboardController : ControllerBase
{
    private readonly AdminProxmoxDashboardService dashboardService;

    public ProxmoxDashboardController(AdminProxmoxDashboardService dashboardService)
    {
        this.dashboardService = dashboardService;
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> GetDashboard()
    {
        var dashboard = await dashboardService.GetDashboardAsync();
        return Ok(dashboard);
    }
}
