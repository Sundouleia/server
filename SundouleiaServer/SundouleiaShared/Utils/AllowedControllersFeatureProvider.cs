using Microsoft.AspNetCore.Mvc.Controllers;
using System.Reflection;

namespace SundouleiaShared.Utils;

/// <summary>
///     These make me want to rip my hair out but they are managing to work now so I am satisfied.
/// </summary>
public class AllowedControllersFeatureProvider : ControllerFeatureProvider
{
    private readonly Type[] _allowedTypes;

    public AllowedControllersFeatureProvider(params Type[] allowedTypes)
    {
        _allowedTypes = allowedTypes;
    }

    protected override bool IsController(TypeInfo typeInfo)
    {
        return base.IsController(typeInfo) && _allowedTypes.Contains(typeInfo.AsType());
    }
}
