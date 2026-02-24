namespace MyHomeLib.Web;

public sealed class UserSessionContext
{
    public string UserId { get; private set; } = string.Empty;

    public bool IsLoaded => !string.IsNullOrWhiteSpace(UserId);

    public void SetUserId(string userId) => UserId = userId;
}
