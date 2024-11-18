using Microsoft.AspNetCore.Mvc;

namespace Recuerdense_Bot.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BotController : ControllerBase
{
    private readonly Program _bot;

    public BotController(Program bot)
    {
        _bot = bot;
    }

    // POST /api/bot/send
    [HttpPost("send")]
    public async Task<IActionResult> SendImage()
    {
        try
        {
            await _bot.PostRandomImageUrl();
            return Ok("Image sent successfully.");
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }
}