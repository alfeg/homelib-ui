namespace MyHomeLib.Web.Services.TorrServe;

internal record TorrentAddRequest(string Action, string Link, string Title, bool SaveToDb);