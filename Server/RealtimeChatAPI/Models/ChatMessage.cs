using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace RealtimeChatAPI.Models
{
    [BsonIgnoreExtraElements]
    public class ChatMessage
    {
        [BsonId]
        public ObjectId Id { get; set; }

        [BsonElement("messageId")]
        public string MessageId { get; set; } = string.Empty;

        [BsonElement("nickname")]
        public string Nickname { get; set; } = string.Empty;

        [BsonElement("content")]
        public string Content { get; set; } = string.Empty;

        [BsonElement("messageType")]
        public string MessageType { get; set; } = "text";

        [BsonElement("imageId")]
        public string? ImageId { get; set; }

        [BsonElement("timestamp")]
        public DateTime Timestamp { get; set; }

        [BsonElement("roomId")]
        public string RoomId { get; set; } = string.Empty;

        [BsonElement("reactions")]
        public List<ChatReaction> Reactions { get; set; } = new();
    }

    public class ChatReaction
    {
        [BsonElement("nickname")]
        public string Nickname { get; set; } = string.Empty;

        [BsonElement("emoji")]
        public string Emoji { get; set; } = string.Empty;

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; }
    }
}
