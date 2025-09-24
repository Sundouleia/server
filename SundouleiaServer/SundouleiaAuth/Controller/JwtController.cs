using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis.Extensions.Core.Abstractions;
using SundouleiaAPI.Hub;
using SundouleiaAuth.Services;
using SundouleiaShared.Data;
using SundouleiaShared.Models;
using SundouleiaShared.Services;
using SundouleiaShared.Utils;
using SundouleiaShared.Utils.Configuration;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace SundouleiaAuth.Controllers;

[Route(AuthRoutes.Auth)]
public class JwtController : Controller
{
    private readonly ILogger<JwtController> _logger;
    private readonly IHttpContextAccessor _accessor;
    private readonly IConfigurationService<AuthServiceConfig> _configuration;
    private readonly SundouleiaDbContext _dbContext;
    private readonly IRedisDatabase _redis;
    private readonly SecretKeyAuthService _keyAuthService;

    public JwtController(
        ILogger<JwtController> logger,
        IHttpContextAccessor accessor,
        IConfigurationService<AuthServiceConfig> configuration,
        SundouleiaDbContext dbContext,
        IRedisDatabase redisDb,
        SecretKeyAuthService keyAuthService)
    {
        _logger = logger;
        _accessor = accessor;
        _configuration = configuration;
        _dbContext = dbContext;
        _redis = redisDb;
        _keyAuthService = keyAuthService;
    }

    /// <summary>
    ///     Create a temporary access token via the callers hashed name and contentId.
    /// </summary>
    [AllowAnonymous]
    [HttpPost(AuthRoutes.Auth_TempToken)]
    public async Task<IActionResult> CreateTemporaryToken(string charaIdent, string contentId)
    {
        _logger.LogInformation($"CreateTemporaryToken:SUCCESS:{charaIdent}");
        return await AuthenticateInternal(charaIdent, contentID: contentId).ConfigureAwait(false);
    }



    /// <summary> 
    ///     Create a new token for a User. <b>(Allows Anonymous Access)</b> <para />
    /// </summary>
    [AllowAnonymous]
    [HttpPost(AuthRoutes.Auth_CreateIdent)]
    public async Task<IActionResult> CreateToken(string charaIdent, string authKey, string ensurePrimary)
    {
        bool forceMain = string.Equals(ensurePrimary, "True", StringComparison.OrdinalIgnoreCase);
        return await AuthenticateInternal(charaIdent, forceMain, authKey).ConfigureAwait(false);
    }

    /// <summary>
    ///     Renews a token for a User. <b>(Requires Authenticated Access)</b> <para />
    /// </summary>
    [Authorize(Policy = "Authenticated")]
    [HttpGet(AuthRoutes.Auth_RenewToken)]
    public async Task<IActionResult> RenewToken()
    {
        try
        {
            // Extract info from content claims.
            string uid = HttpContext.User.Claims.Single(p => string.Equals(p.Type, SundouleiaClaimTypes.Uid, StringComparison.Ordinal))!.Value;
            string ident = HttpContext.User.Claims.Single(p => string.Equals(p.Type, SundouleiaClaimTypes.CharaIdent, StringComparison.Ordinal))!.Value;
            string alias = HttpContext.User.Claims.SingleOrDefault(p => string.Equals(p.Type, SundouleiaClaimTypes.Alias))?.Value ?? string.Empty;

            // Need to determine if this connection is banned from using the service. If the UID is valid, we can also assume there is a valid auth and reputation.

            // First, check if the user Identity is banned. If this is true, it means that the character they are connecting with has been banned.
            // It will detect this even if they have removed their primary account and are creating a new one. This will be bound to the ID of the character.
            if (await _dbContext.BannedUsers.AsNoTracking().AnyAsync(u => u.CharacterIdentification == ident).ConfigureAwait(false))
            {
                // If the identity is banned, we need to ensure that the accountRep is also banned.
                await EnsureBanFromDetectedIdentBan(uid, ident);
                return Unauthorized("This Character is banned from Sundouleia.");
            }

            // If the Identity is not banned, see if they are banned via their reputation.
            if (_dbContext.Auth.AsNoTracking().SingleOrDefault(a => a.UserUID == uid)?.AccountUserUID is { } accountUid)
            {
                // If we are marked as banned, ban all other methods of access and return unauthorized.
                if (await _dbContext.AccountReputation.AsNoTracking().SingleOrDefaultAsync(r => r.UserUID == accountUid) is { IsBanned: true })
                {
                    await EnsureBanFromDetectedRepBan(uid, ident, accountUid);
                    return Unauthorized("Your account is banned from using the service.");
                }
            }

            // Otherwise, not banned, so log success and create a new jwt from the ID.
            _logger.LogInformation($"RenewToken:SUCCESS:{uid}:{ident}");
            return CreateJwtFromId(uid, ident, alias);
        }
        catch (Exception ex)
        {
            // Log the error and return an Unauthorized result if we catch an exception.
            _logger.LogError(ex, "RenewToken:FAILURE");
            return Unauthorized("Unknown error while renewing authentication token");
        }
    }

    private async Task<IActionResult> AuthenticateInternal(string charaIdent, bool forceMain = false, string? auth = null, string? contentID = null)
    {
        try
        {            
            // If this is empty, fail regardless of type.
            if (string.IsNullOrEmpty(charaIdent))
                return BadRequest("No CharaIdent");

            _logger.LogInformation($"Authenticating {charaIdent}");

            // Try the contentId (Temp Access for One-Time Generation) first.
            if (!string.IsNullOrEmpty(contentID))
                return CreateTempAccessJwtFromId(contentID, charaIdent);
            
            
            // Otherwise, validate a legitimate authentication. (Meaning we MUST know the auth key)
            if (string.IsNullOrEmpty(auth))
            {
                _logger.LogWarning($"Authenticate:NO AUTHKEY PROVIDED:{charaIdent}");
                return Unauthorized("No Secret Key provided.");
            }

            // Obtain IP only to authorize authentication attempts. Do not store it anywhere.
            // I hate storing data like this at all.
            _logger.LogInformation($"Attempting to authenticate: {auth}");
            SecretKeyAuthReply authResult = await _keyAuthService.AuthorizeAsync(_accessor.GetIpAddress(), auth);

            // EDGE CASE CONDITIONS AHOY:
            _logger.LogDebug($"AuthResult: {authResult}");

            // If Ident is banned, return an unauthorized result.
            if (await _dbContext.BannedUsers.AsNoTracking().AnyAsync(u => u.CharacterIdentification == charaIdent).ConfigureAwait(false))
            {
                // If the identity is banned, we need to ensure that the accountRep is also banned.
                await EnsureBanFromDetectedIdentBan(authResult.Uid, charaIdent);
                _logger.LogWarning($"Authenticate:IDENTBAN:{authResult.Uid}:{charaIdent}");
                return Unauthorized("Your character is banned from using the service.");
            }

            // If unsuccessful but not TempBanned, then UID was invalid.
            if (!authResult.Success && !authResult.TempBan)
            {
                _logger.LogWarning($"Authenticate:INVALID:{(authResult?.Uid ?? "NOUID")}:{charaIdent}");
                return Unauthorized("The provided secret key is invalid. Verify your accounts existence and/or recover the secret key.");
            }

            // If unsuccessful due to temp ban, return that the user is temp banned.
            if (!authResult.Success && authResult.TempBan)
            {
                _logger.LogWarning($"Authenticate:TEMPBAN:{(authResult?.Uid ?? "NOUID")}:{charaIdent}");
                return Unauthorized("Due to an excessive amount of failed authentication attempts you are temporarily banned. Check your Secret Key configuration and try connecting again in 5 minutes.");
            }

            // If banned via Reputation detection, ensure other elements of this connection are banned!
            if (authResult.Permaban)
            {
                await EnsureBanFromDetectedRepBan(authResult.Uid, charaIdent, authResult.AccountUid);
                _logger.LogWarning($"Authenticate:UIDBAN:{authResult.Uid}:{charaIdent}");
                return Unauthorized("You are permanently banned.");
            }

            // If they are not yet disconnected from the redis database, do not authenticate a duplicate.
            if (!string.IsNullOrEmpty(await _redis.GetAsync<string>("SundouleiaHub:UID:" + authResult.Uid)))
            {
                _logger.LogWarning($"Authenticate:DUPLICATE:{authResult.Uid}:{charaIdent}");
                return Unauthorized("Already logged in to this account. Reconnect in 60 seconds. If you keep seeing this issue, restart your game.");
            }

            // if the user is not already logged in, set the redis key to the identifier.
            _logger.LogInformation($"Authenticate:SUCCESS:{authResult.Uid}:{charaIdent}");
            return CreateJwtFromId(authResult.Uid, charaIdent, authResult.Alias ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Authenticate:UNKNOWN");
            return Unauthorized("Unknown internal server error during authentication");
        }
    }

    /// <summary>
    ///     Method to create a temporary JWT token from a provided user ID, character identity, and alias. <para />
    ///     For One-Time AccountGeneration.
    /// </summary>
    private IActionResult CreateTempAccessJwtFromId(string charaIdent, string localConentId)
    {
        // Tokens last for 6 hours before refresh.
        JwtSecurityToken token = CreateJwt(new List<Claim>
        {
            new Claim(SundouleiaClaimTypes.CharaIdent, charaIdent),
            new Claim(SundouleiaClaimTypes.Expires, DateTime.UtcNow.AddHours(6).Ticks.ToString(CultureInfo.InvariantCulture)), // the expiration claim
            new Claim(SundouleiaClaimTypes.AccessType, "LocalContent")
        });
        return Content(token.RawData);
    }

    /// <summary>
    ///     Method to create a JWT token from a provided user ID, character identity, and alias.
    /// </summary>
    private IActionResult CreateJwtFromId(string uid, string charaIdent, string alias)
    {
        // create a new token from the provided claims.
        JwtSecurityToken token = CreateJwt(new List<Claim>()
        {
            new Claim(SundouleiaClaimTypes.Uid, uid),                 // the UID claim
            new Claim(SundouleiaClaimTypes.CharaIdent, charaIdent),   // the character identifier claim
            new Claim(SundouleiaClaimTypes.Alias, alias),             // the UID alias claim
            new Claim(SundouleiaClaimTypes.Expires, DateTime.UtcNow.AddHours(6).Ticks.ToString(CultureInfo.InvariantCulture)), // the expiration claim
            new Claim(SundouleiaClaimTypes.AccessType, "SecretKey")
        });
        return Content(token.RawData);
    }

    /// <summary>
    ///     Method to create a JWT token from a provided set of claims.
    /// </summary>
    private JwtSecurityToken CreateJwt(IEnumerable<Claim> authClaims)
    {
        // create the authentication signing key using the configuration value for the JWT.
        SymmetricSecurityKey authSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_configuration.GetValue<string>(nameof(SundouleiaConfigBase.Jwt))));

        // generate the token via a new securityTokenDescriptor:
        SecurityTokenDescriptor token = new SecurityTokenDescriptor()
        {
            // set the subject to the AuthClaims provided.
            Subject = new ClaimsIdentity(authClaims),
            // set the signing credentials to the auth signing key.
            SigningCredentials = new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256Signature),
            // set the expiration date to the provided expiration date by the AuthClaims.
            Expires = new(long.Parse(authClaims.First(f => string.Equals(f.Type, SundouleiaClaimTypes.Expires, StringComparison.Ordinal)).Value!, CultureInfo.InvariantCulture), DateTimeKind.Utc),
        };

        // set the handler to the new JWT security token handler and create the token.
        JwtSecurityTokenHandler handler = new JwtSecurityTokenHandler();
        // create a new jwt SecurityToken from the token generated, and return that.
        return handler.CreateJwtSecurityToken(token);
    }

    /// <summary>
    ///     Ensure ban across the board if detected from user identity.
    /// </summary>
    private async Task EnsureBanFromDetectedIdentBan(string uid, string charaIdent)
    {
        Auth? matchedAuth = _dbContext.Auth.SingleOrDefault(a => a.UserUID == uid);
        if (matchedAuth is null)
        {
            _logger.LogWarning("AccountAuthNotFound??:{id}:{ident}", uid, charaIdent);
            return;
        }

        string accountUid = matchedAuth.AccountUserUID;
        if (await _dbContext.AccountReputation.SingleOrDefaultAsync(r => r.UserUID == accountUid) is { } reputation && !reputation.IsBanned)
        {
            reputation.IsBanned = true;
            await _dbContext.SaveChangesAsync();
        }

        // Attach the banned discord ID to the ban if the auth claim exists too (have to do separately because people are weird and try to get around bans)
        if (await _dbContext.AccountClaimAuth.AsNoTracking().Include(a => a.User).FirstOrDefaultAsync(c => c.User!.UID == accountUid) is { } authClaim)
        {
            if (!_dbContext.BannedRegistrations.AsNoTracking().Any(c => c.DiscordId == authClaim.DiscordId.ToString()))
                _dbContext.BannedRegistrations.Add(new BannedRegistrations() { DiscordId = authClaim.DiscordId.ToString() });
            await _dbContext.SaveChangesAsync();
        }
    }

    /// <summary>
    ///     Ensure ban across the board if detected true from account rep.
    /// </summary>
    private async Task EnsureBanFromDetectedRepBan(string uid, string charaIdent, string accountUid)
    {
        // Ensure that this new account they tried to make also gets account banned.
        if (!_dbContext.BannedUsers.AsNoTracking().Any(c => c.CharacterIdentification == charaIdent))
        {
            _dbContext.BannedUsers.Add(new Banned()
            {
                CharacterIdentification = charaIdent,
                UserUID = uid,
                Reason = $"Auto-Banned CharacterIdent ({uid})",
            });
            await _dbContext.SaveChangesAsync();
        }

        // Ensure discord gets re-banned if they tried making a discord alt.
        if (await _dbContext.AccountClaimAuth.AsNoTracking().Include(a => a.User).FirstOrDefaultAsync(c => c.User!.UID == accountUid) is { } authClaim)
        {
            if (!_dbContext.BannedRegistrations.AsNoTracking().Any(c => c.DiscordId == authClaim.DiscordId.ToString()))
                _dbContext.BannedRegistrations.Add(new BannedRegistrations() { DiscordId = authClaim.DiscordId.ToString() });
            await _dbContext.SaveChangesAsync();
        }
    }
}