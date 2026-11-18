using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

namespace RealtimeChatAPI.Controllers
{
    [ApiController]
    [Route("api/audio")]
    public class AudioController : ControllerBase
    {
        private const long MaxAudioSizeBytes = 10 * 1024 * 1024;
        private readonly GridFSBucket _audio;

        public AudioController(IMongoClient mongoClient)
        {
            _audio = new GridFSBucket(
                mongoClient.GetDatabase("Websocket"),
                new GridFSBucketOptions { BucketName = "chatAudio" });
        }

        [HttpPost]
        [RequestSizeLimit(MaxAudioSizeBytes + 64 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxAudioSizeBytes + 64 * 1024)]
        public async Task<IActionResult> Upload([FromForm] IFormFile? file, CancellationToken cancellationToken)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "Bản ghi âm không hợp lệ." });
            }

            if (file.Length > MaxAudioSizeBytes)
            {
                return BadRequest(new { error = "Tin nhắn thoại phải nhỏ hơn hoặc bằng 10 MB." });
            }

            var contentType = NormalizeContentType(file.ContentType);
            if (contentType == null)
            {
                return BadRequest(new { error = "Định dạng âm thanh không được hỗ trợ." });
            }

            await using var stream = file.OpenReadStream();
            var header = new byte[12];
            var bytesRead = await stream.ReadAsync(header.AsMemory(), cancellationToken);
            var extension = GetAudioExtension(header, bytesRead, contentType);
            if (extension == null)
            {
                return BadRequest(new { error = "Dữ liệu tải lên không phải tệp âm thanh hợp lệ." });
            }

            stream.Position = 0;
            var fileName = $"{ObjectId.GenerateNewId()}{extension}";
            var options = new GridFSUploadOptions
            {
                Metadata = new BsonDocument("contentType", contentType)
            };
            var audioId = await _audio.UploadFromStreamAsync(fileName, stream, options, cancellationToken);

            return CreatedAtAction(
                nameof(Get),
                new { id = audioId.ToString() },
                new { id = audioId.ToString(), url = $"/api/audio/{audioId}" });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
        {
            if (!ObjectId.TryParse(id, out var audioId))
            {
                return NotFound();
            }

            try
            {
                var stream = await _audio.OpenDownloadStreamAsync(audioId, cancellationToken: cancellationToken);
                var contentType = stream.FileInfo.Metadata?
                    .GetValue("contentType", "application/octet-stream")
                    .AsString ?? "application/octet-stream";
                return File(stream, contentType);
            }
            catch (GridFSFileNotFoundException)
            {
                return NotFound();
            }
        }

        private static string? NormalizeContentType(string? contentType) =>
            contentType?.Split(';', 2)[0].Trim().ToLowerInvariant() switch
            {
                "audio/webm" => "audio/webm",
                "audio/ogg" => "audio/ogg",
                "audio/mp4" => "audio/mp4",
                "audio/mpeg" => "audio/mpeg",
                "audio/wav" or "audio/x-wav" => "audio/wav",
                _ => null
            };

        private static string? GetAudioExtension(byte[] header, int bytesRead, string contentType)
        {
            var isValid = contentType switch
            {
                "audio/webm" => bytesRead >= 4
                    && header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3,
                "audio/ogg" => bytesRead >= 4
                    && header[0] == 'O' && header[1] == 'g' && header[2] == 'g' && header[3] == 'S',
                "audio/mp4" => bytesRead >= 8
                    && header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p',
                "audio/mpeg" => bytesRead >= 3
                    && header[0] == 'I' && header[1] == 'D' && header[2] == '3'
                    || bytesRead >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0,
                "audio/wav" => bytesRead >= 12
                    && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
                    && header[8] == 'W' && header[9] == 'A' && header[10] == 'V' && header[11] == 'E',
                _ => false
            };

            if (!isValid)
            {
                return null;
            }

            return contentType switch
            {
                "audio/webm" => ".webm",
                "audio/ogg" => ".ogg",
                "audio/mp4" => ".m4a",
                "audio/mpeg" => ".mp3",
                "audio/wav" => ".wav",
                _ => null
            };
        }
    }
}
