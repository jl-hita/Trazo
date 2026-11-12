namespace Trazo.Domain.Settings;

/// <summary>
/// A runtime setting stored in the database. Secret values are stored encrypted
/// (ASP.NET Core Data Protection) and are never returned by the API.
/// </summary>
public sealed class AppSetting
{
    public required string Key { get; set; }
    public required string Value { get; set; }
    public bool IsSecret { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
