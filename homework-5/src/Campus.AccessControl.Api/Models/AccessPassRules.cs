namespace Campus.AccessControl.Api.Models;

public static class AccessPassRules
{
    public static bool IsValid(CreateAccessPassRequest request, DateTimeOffset now) =>
        request.PersonId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(request.BadgeCode) &&
        request.BadgeCode.Trim().Length <= 64 &&
        request.ValidUntil > now;
}
