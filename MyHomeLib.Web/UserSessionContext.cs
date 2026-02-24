namespace MyHomeLib.Web;

public sealed class UserSessionContext(IHttpContextAccessor httpContextAccessor)
{
    public string UserId { get; } = httpContextAccessor.HttpContext?.Request.Cookies[UserSessionCookie.CookieName] ?? string.Empty;
}
