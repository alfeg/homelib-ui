namespace MyHomeLib.Web;

public static class UserSessionCookie
{
    public const string CookieName = "mhl_uid";

    public static string NewUserId() => Guid.NewGuid().ToString("N");
}
