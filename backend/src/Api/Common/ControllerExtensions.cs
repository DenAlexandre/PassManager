using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Mvc;

namespace PassManager.Api.Common;

public static class ControllerExtensions
{
    public static Guid GetUserId(this ControllerBase controller) =>
        Guid.Parse(controller.User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
