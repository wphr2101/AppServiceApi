using System.ComponentModel.DataAnnotations;
using CustomerOperations.Api.Help;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CustomerOperations.Api.Controllers;

public sealed class HelpQuestion
{
    [Required, StringLength(500, MinimumLength = 2)]
    public string Question { get; set; } = "";
}

[AllowAnonymous]
[ApiController]
[Route("api/help")]
public sealed class HelpController(LogisticsHelpService help) : ControllerBase
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(4096)]
    [EnableRateLimiting("assistant-questions")]
    public async Task<ActionResult<HelpAnswer>> Ask(HelpQuestion request, CancellationToken cancellationToken)
    {
        if (request.Question.Trim().Length < 2) return BadRequest(new { message = "Enter a question of 2 to 500 characters." });
        return Ok(await help.AskAsync(request.Question.Trim(), cancellationToken));
    }
}
