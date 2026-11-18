using MongoDB.Bson;
using MongoDB.Driver;
using RealtimeChatAPI.Models;

namespace RealtimeChatAPI.Services
{
    public interface IChatService
    {
        Task<List<ChatMessage>> GetMessageHistoryAsync(string roomId, int limit = 50);
        Task SaveMessageAsync(ChatMessage message);
        Task<ChatRoom> GetOrCreateRoomAsync(string roomId);
        Task<ChatRoom> UpdateRoomNameAsync(string roomId, string displayName);
        Task<List<ChatMember>> GetRoomMembersAsync(string roomId);
        Task UpsertRoomMemberAsync(string roomId, string nickname);
        Task UpdateMemberLastSeenAsync(string roomId, string nickname);
        Task UpdateMemberAvatarAsync(string roomId, string nickname, string avatar);
        Task<List<ChatReaction>> ToggleMessageReactionAsync(string roomId, string messageId, string nickname, string emoji);
    }

    public class ChatService : IChatService
    {
        private readonly IMongoCollection<ChatMessage> _messagesCollection;
        private readonly IMongoCollection<ChatRoom> _roomsCollection;
        private readonly IMongoCollection<ChatMember> _membersCollection;

        public ChatService(IMongoClient mongoClient, string databaseName = "Websocket")
        {
            var database = mongoClient.GetDatabase(databaseName);
            _messagesCollection = database.GetCollection<ChatMessage>("message");
            _roomsCollection = database.GetCollection<ChatRoom>("rooms");
            _membersCollection = database.GetCollection<ChatMember>("members");

            // Create index for faster queries
            try
            {
                var indexKeysDefinition = Builders<ChatMessage>.IndexKeys.Ascending(m => m.RoomId);
                _messagesCollection.Indexes.CreateOne(new CreateIndexModel<ChatMessage>(indexKeysDefinition));

                var roomIndexKeys = Builders<ChatRoom>.IndexKeys.Ascending(r => r.RoomId);
                var roomIndexOptions = new CreateIndexOptions { Unique = true };
                _roomsCollection.Indexes.CreateOne(new CreateIndexModel<ChatRoom>(roomIndexKeys, roomIndexOptions));

                var memberIndexKeys = Builders<ChatMember>.IndexKeys
                    .Ascending(member => member.RoomId)
                    .Ascending(member => member.Nickname);
                _membersCollection.Indexes.CreateOne(new CreateIndexModel<ChatMember>(memberIndexKeys));
            }
            catch (MongoCommandException ex) when (ex.Code == 48)
            {
                // Index already exists
            }
        }


        public async Task<List<ChatMessage>> GetMessageHistoryAsync(string roomId, int limit = 50)
        {
            var messages = await _messagesCollection
                .Find(m => m.RoomId == roomId)
                .SortByDescending(m => m.Timestamp)
                .Limit(limit)
                .ToListAsync();

            messages.Reverse(); // Return oldest first
            return messages;
        }

        public async Task SaveMessageAsync(ChatMessage message)
        {
            message.Timestamp = DateTime.UtcNow;
            message.MessageId = ObjectId.GenerateNewId().ToString();
            message.Id = ObjectId.Parse(message.MessageId);
            await _messagesCollection.InsertOneAsync(message);
        }

        public async Task<List<ChatReaction>> ToggleMessageReactionAsync(
            string roomId,
            string messageId,
            string nickname,
            string emoji)
        {
            var filter = Builders<ChatMessage>.Filter
                .Eq(message => message.RoomId, roomId)
                & Builders<ChatMessage>.Filter.Eq(message => message.MessageId, messageId);

            var message = await _messagesCollection.Find(filter).FirstOrDefaultAsync();
            if (message == null)
            {
                throw new ArgumentException("Tin nhắn không tồn tại.", nameof(messageId));
            }

            var existingReaction = message.Reactions.FirstOrDefault(reaction =>
                reaction.Nickname == nickname && reaction.Emoji == emoji);

            if (existingReaction != null)
            {
                message.Reactions.Remove(existingReaction);
            }
            else
            {
                message.Reactions.Add(new ChatReaction
                {
                    Nickname = nickname,
                    Emoji = emoji,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _messagesCollection.ReplaceOneAsync(filter, message);
            return message.Reactions;
        }

        public async Task<ChatRoom> GetOrCreateRoomAsync(string roomId)
        {
            var room = await _roomsCollection.Find(r => r.RoomId == roomId).FirstOrDefaultAsync();
            if (room != null)
            {
                return room.DisplayName == "Phòng chat"
                    ? await UpdateRoomNameAsync(roomId, "Hội ae rok")
                    : room;
            }

            room = new ChatRoom
            {
                RoomId = roomId,
                DisplayName = "Phòng chat",
                UpdatedAt = DateTime.UtcNow
            };

            await _roomsCollection.InsertOneAsync(room);
            return room;
        }

        public async Task<ChatRoom> UpdateRoomNameAsync(string roomId, string displayName)
        {
            var normalizedName = displayName.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                throw new ArgumentException("Room display name cannot be empty.", nameof(displayName));
            }

            var update = Builders<ChatRoom>.Update
                .Set(r => r.DisplayName, normalizedName)
                .Set(r => r.UpdatedAt, DateTime.UtcNow);

            var options = new UpdateOptions { IsUpsert = true };
            await _roomsCollection.UpdateOneAsync(
                r => r.RoomId == roomId,
                update,
                options);

            return await _roomsCollection.Find(r => r.RoomId == roomId).FirstOrDefaultAsync();
        }

        public async Task<List<ChatMember>> GetRoomMembersAsync(string roomId)
        {
            return await _membersCollection
                .Find(member => member.RoomId == roomId)
                .SortBy(member => member.Nickname)
                .ToListAsync();
        }

        public async Task UpsertRoomMemberAsync(string roomId, string nickname)
        {
            var filter = Builders<ChatMember>.Filter
                .Eq(member => member.RoomId, roomId)
                & Builders<ChatMember>.Filter.Eq(member => member.Nickname, nickname);
            var update = Builders<ChatMember>.Update
                .Set(member => member.LastSeen, DateTime.UtcNow)
                .SetOnInsert(member => member.RoomId, roomId)
                .SetOnInsert(member => member.Nickname, nickname);

            await _membersCollection.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });
        }

        public async Task UpdateMemberLastSeenAsync(string roomId, string nickname)
        {
            var filter = Builders<ChatMember>.Filter
                .Eq(member => member.RoomId, roomId)
                & Builders<ChatMember>.Filter.Eq(member => member.Nickname, nickname);
            var update = Builders<ChatMember>.Update.Set(member => member.LastSeen, DateTime.UtcNow);

            await _membersCollection.UpdateOneAsync(filter, update);
        }

        public async Task UpdateMemberAvatarAsync(string roomId, string nickname, string avatar)
        {
            var filter = Builders<ChatMember>.Filter
                .Eq(member => member.RoomId, roomId)
                & Builders<ChatMember>.Filter.Eq(member => member.Nickname, nickname);
            var update = Builders<ChatMember>.Update.Set(member => member.Avatar, avatar);

            await _membersCollection.UpdateOneAsync(filter, update);
        }
    }
}
