using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SundouleiaAPI.Hub;
using SundouleiaServer.Hubs;
using SundouleiaShared.Utils;

namespace SundouleiaServer.Controllers;

/// <summary>
///     Handles all messages sent out to clients via external services (Like discord)
/// </summary>
[Route("/msgc")]
[Authorize(Policy = "Internal")]
public class ClientMessageController : Controller
{
    // Declare private variables for logger and hub context
    private ILogger<ClientMessageController> _logger;
    private IHubContext<SundouleiaHub, ISundouleiaHub> _hubContextMain;

    public ClientMessageController(ILogger<ClientMessageController> logger, IHubContext<SundouleiaHub, ISundouleiaHub> hubContext)
    {
        _logger = logger;
        _hubContextMain = hubContext;
    }

    [Route("sendMessage")]
    [HttpPost]
    public async Task<IActionResult> SendMessage([FromBody] ClientMessage msg)
    {
        if(msg is null)
        {
            _logger.LogError("Received a null message");
            return Empty;
        }

        // If no UID, send the message to all online users
        if (string.IsNullOrEmpty(msg.UID))
        {
            _logger.LogInformation($"Sending Message of severity {msg.Severity} to all online users: {msg.Message}");
            await _hubContextMain.Clients.All.Callback_ServerMessage(msg.Severity, msg.Message).ConfigureAwait(false);
        }
        // If there is a UID, send the message to the specific user
        else
        {
            _logger.LogInformation($"Sending Message of severity {msg.Severity} to user {msg.UID}: {msg.Message}");
            await _hubContextMain.Clients.User(msg.UID).Callback_ServerMessage(msg.Severity, msg.Message).ConfigureAwait(false);
        }
        return Empty;
    }

    // Forces all users to reconnect to the main server. (fixing any prone internal reconnection errors.
    [Route("forceHardReconnect")]
    [HttpPost]
    public async Task<IActionResult> ForceHardReconnect([FromBody] HardReconnectMessage msg)
    {
        if(msg is null)
            return Empty;

        _logger.LogInformation($"Sending Message of severity { msg.Severity} to all online users: {msg.Message}");
        await _hubContextMain.Clients.All.Callback_HardReconnectMessage(msg.Severity, msg.Message, msg.State).ConfigureAwait(false);

        return Empty;
    }

    // Can add more creative things here to manage clients externally from discord to help lighten burden on manual moderation.
}