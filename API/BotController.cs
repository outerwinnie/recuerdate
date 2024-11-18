[ApiController]
[Route("api/[controller]")]
public class BotController : ControllerBase
{
    private readonly Program _bot;

    public BotController(Program bot)
    {
        _bot = bot;
    }

    [HttpPost("send")]
    public async Task<IActionResult> SendImage()
    {
        try
        {
            await _bot.HandleSendCommandFromApiAsync();
            return Ok("Image sent successfully.");
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }
}