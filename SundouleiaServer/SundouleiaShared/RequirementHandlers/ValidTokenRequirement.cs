using Microsoft.AspNetCore.Authorization;

namespace SundouleiaShared.RequirementHandlers;

/// <summary>
///     A requirement for a valid token.
/// </summary>
public class ValidTokenRequirement : IAuthorizationRequirement
{ }