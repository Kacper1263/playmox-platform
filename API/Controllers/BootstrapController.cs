using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PlaymoxAPI.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/bootstrap")]
public class BootstrapController : ControllerBase
{
    [HttpGet("proxmox-script")]
    public IActionResult DownloadProxmoxBootstrapScript()
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Bootstrap", "bootstrap-playmox-proxmox.sh");
        if (!System.IO.File.Exists(scriptPath))
        {
            return NotFound("Bootstrap script file is not available.");
        }

        var scriptText = System.IO.File.ReadAllText(scriptPath);
        var normalizedScriptText = scriptText.Replace("\r\n", "\n");
        var scriptBytes = System.Text.Encoding.UTF8.GetBytes(normalizedScriptText);
        return File(scriptBytes, "text/x-shellscript", "bootstrap-playmox-proxmox.sh");
    }
}
