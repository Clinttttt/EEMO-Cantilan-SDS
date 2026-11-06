namespace EEMOCantilanSDS.Application.Dtos.Notifications;

public sealed record InboxItemDto(Guid Id, string Title, string Body, string? Link, DateTime CreatedAt, bool Read);

/// <summary>The latest messages for the signed-in person, and how many of all their messages are unread.</summary>
public sealed record InboxPageDto(IReadOnlyList<InboxItemDto> Items, int Unread);
