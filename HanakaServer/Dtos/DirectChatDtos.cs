namespace HanakaServer.Dtos
{
    public class CreateDirectChatRoomRequestDto
    {
        public long TargetUserId { get; set; }
    }

    public class SendDirectChatMessageRequestDto
    {
        public string MessageType { get; set; } = "text";
        public string? Content { get; set; }
        public string? MediaUrl { get; set; }
        public long? ReplyToMessageId { get; set; }
        public Guid? ClientMessageId { get; set; }
    }

    public class DirectChatBlockRequestDto
    {
        public long? RoomId { get; set; }
        public long? MessageId { get; set; }
        public string? Reason { get; set; }
        public string? Notes { get; set; }
    }
}
