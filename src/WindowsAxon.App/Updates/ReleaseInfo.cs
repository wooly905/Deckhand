namespace WindowsAxon.App.Updates;

public sealed record ReleaseInfo(Version Version, Uri PageUrl, string ExeName, Uri ExeUrl, Uri Sha256Url);
