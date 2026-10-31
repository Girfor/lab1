namespace Campus.AccessControl.Api.Models;

public sealed record CreateAccessPassRequest(
    Guid PersonId,
    string BadgeCode,
    DateTimeOffset ValidUntil,
    bool IsTest = false);

public sealed record AccessPass(
    Guid Id,
    Guid PersonId,
    string BadgeCode,
    DateTimeOffset ValidUntil,
    bool IsActive,
    bool IsTest);
