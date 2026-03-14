namespace HanakaServer.Dtos
{
    public class SendClubMessageRequestDto
    {
        public string MessageType { get; set; } = "TEXT"; // TEXT, IMAGE...
        public string? Content { get; set; }
        public string? MediaUrl { get; set; }
        public long? ReplyToId { get; set; }
    }

    public class ClubChatRoomItemDto
    {
        public long ClubId { get; set; }
        public string ClubName { get; set; } = string.Empty;
        public string? ClubCoverUrl { get; set; }
        public string? AreaText { get; set; }
        public int MembersCount { get; set; }

        public string? LastMessagePreview { get; set; }
        public string? LastMessageType { get; set; }
        public DateTime? LastMessageAt { get; set; }

        public long? LastSenderUserId { get; set; }
        public string? LastSenderName { get; set; }
        public string? LastSenderAvatarUrl { get; set; }
    }
}