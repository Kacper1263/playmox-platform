using PlaymoxAPI.BLL;
using PlaymoxAPI.BLL.DTO.Platform;
using PlaymoxAPI.Extensions.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PlaymoxAPI.Controllers;

[BaseAuth]
[ApiController]
[Route("api/game-definitions")]
public class GameDefinitionsController : ControllerBase
{
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        using var dbContext = new PlaymoxContext();
        var definitions = await dbContext.GameDefinitions
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);

        return Ok(definitions.Select(x => Config.Mapper.Map<GameDefinitionDto>(x)));
    }
}
