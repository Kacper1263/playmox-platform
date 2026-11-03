using PlaymoxAPI.BLL;
using PlaymoxAPI.BLL.BO.Platform;
using PlaymoxAPI.BLL.DTO.Platform;
using PlaymoxAPI.Extensions.Auth;
using PlaymoxAPI.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PlaymoxAPI.Controllers.Admin;

[BaseAuth]
[ApiController]
[Route("api/admin/proxmox-settings")]
public class ProxmoxSettingsController : ControllerBase
{
    [Authorize(Roles = "Admin")]
    [HttpGet]
    public IActionResult Get()
    {
        using var dbContext = new PlaymoxContext();
        var settings = dbContext.ProxmoxSettings.OrderBy(x => x.Id).FirstOrDefault();
        if (settings == null)
        {
            return Ok(new ProxmoxSettingsDto());
        }

        return Ok(ToDto(settings));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut]
    public IActionResult Upsert([FromBody] UpsertProxmoxSettingsApiModel model)
    {
        if (model == null)
        {
            return BadRequest("Configuration payload is required.");
        }

        if (string.IsNullOrWhiteSpace(model.ProxmoxUrl) ||
            string.IsNullOrWhiteSpace(model.ProxmoxNode) ||
            string.IsNullOrWhiteSpace(model.ApiUser) ||
            string.IsNullOrWhiteSpace(model.ApiTokenId) ||
            string.IsNullOrWhiteSpace(model.Storage) ||
            string.IsNullOrWhiteSpace(model.Bridge))
        {
            return BadRequest("All Proxmox configuration fields are required.");
        }

        if (model.TemplateVmid <= 0)
        {
            return BadRequest("Template VMID must be greater than 0.");
        }

        if (!Uri.TryCreate(model.ProxmoxUrl, UriKind.Absolute, out _))
        {
            return BadRequest("Proxmox URL must be a valid absolute URL.");
        }

        using var dbContext = new PlaymoxContext();
        var settings = dbContext.ProxmoxSettings.OrderBy(x => x.Id).FirstOrDefault();
        if (settings == null)
        {
            settings = new ProxmoxSettings();
            dbContext.ProxmoxSettings.Add(settings);
        }

        settings.ProxmoxUrl = model.ProxmoxUrl.Trim();
        settings.ProxmoxNode = model.ProxmoxNode.Trim();
        settings.ApiUser = model.ApiUser.Trim();
        settings.ApiTokenId = model.ApiTokenId.Trim();
        settings.TemplateVmid = model.TemplateVmid;
        settings.Storage = model.Storage.Trim();
        settings.Bridge = model.Bridge.Trim();
        settings.ExternalAddress = model.ExternalAddress?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(model.ApiTokenSecret))
        {
            settings.ApiTokenSecret = SecureSecret.Encrypt(model.ApiTokenSecret.Trim());
        }

        dbContext.SaveChanges();
        return Ok(ToDto(settings));
    }

    private static ProxmoxSettingsDto ToDto(ProxmoxSettings settings)
    {
        return new ProxmoxSettingsDto
        {
            Id = settings.Id,
            ProxmoxUrl = settings.ProxmoxUrl,
            ProxmoxNode = settings.ProxmoxNode,
            ApiUser = settings.ApiUser,
            ApiTokenId = settings.ApiTokenId,
            ApiTokenSecret = string.Empty,
            HasApiTokenSecret = !string.IsNullOrWhiteSpace(settings.ApiTokenSecret),
            TemplateVmid = settings.TemplateVmid,
            Storage = settings.Storage,
            Bridge = settings.Bridge,
            ExternalAddress = settings.ExternalAddress
        };
    }
}

public class UpsertProxmoxSettingsApiModel
{
    public string ProxmoxUrl { get; set; } = string.Empty;
    public string ProxmoxNode { get; set; } = string.Empty;
    public string ApiUser { get; set; } = string.Empty;
    public string ApiTokenId { get; set; } = string.Empty;
    public string ApiTokenSecret { get; set; } = string.Empty;
    public int TemplateVmid { get; set; }
    public string Storage { get; set; } = string.Empty;
    public string Bridge { get; set; } = string.Empty;
    public string ExternalAddress { get; set; } = string.Empty;
}
