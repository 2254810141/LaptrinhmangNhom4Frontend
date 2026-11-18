using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using Microsoft.AspNetCore.Mvc;

namespace RealtimeChatAPI.Controllers
{
    [ApiController]
    [Route("api/images")]
    public class ImagesController : ControllerBase
    {
        private const long MaxImageSizeBytes = 5 * 1024 * 1024;
        private readonly GridFSBucket _images;

        public ImagesController(IMongoClient mongoClient)
        {
            _images = new GridFSBucket(
                mongoClient.GetDatabase("Websocket"),
                new GridFSBucketOptions { BucketName = "chatImages" });
        }

        [HttpPost]
        [RequestSizeLimit(MaxImageSizeBytes + 64 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxImageSizeBytes + 64 * 1024)]
        public async Task<IActionResult> Upload([FromForm] IFormFile? file, CancellationToken cancellationToken)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "Vui lòng chọn ảnh để tải lên." });
            }

            if (file.Length > MaxImageSizeBytes)
            {
                return BadRequest(new { error = "Ảnh phải nhỏ hơn hoặc bằng 5 MB." });
            }

            await using var stream = file.OpenReadStream();
            var header = new byte[12];
            var bytesRead = await stream.ReadAsync(header.AsMemory(), cancellationToken);
            var imageType = GetImageType(header, bytesRead);
            if (imageType == null)
            {
                return BadRequest(new { error = "Chỉ hỗ trợ ảnh PNG, JPG, GIF hoặc WebP hợp lệ." });
            }

            stream.Position = 0;
            var fileName = $"{ObjectId.GenerateNewId()}{imageType.Value.Extension}";
            var options = new GridFSUploadOptions
            {
                Metadata = new BsonDocument("contentType", imageType.Value.ContentType)
            };

            var imageId = await _images.UploadFromStreamAsync(
                fileName,
                stream,
                options,
                cancellationToken);

            return CreatedAtAction(
                nameof(Get),
                new { id = imageId.ToString() },
                new { id = imageId.ToString(), url = $"/api/images/{imageId}" });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
        {
            if (!ObjectId.TryParse(id, out var imageId))
            {
                return NotFound();
            }

            try
            {
                var stream = await _images.OpenDownloadStreamAsync(imageId, cancellationToken: cancellationToken);
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

        private static (string ContentType, string Extension)? GetImageType(byte[] header, int bytesRead)
        {
            if (bytesRead >= 8
                && header[0] == 0x89
                && header[1] == 0x50
                && header[2] == 0x4E
                && header[3] == 0x47
                && header[4] == 0x0D
                && header[5] == 0x0A
                && header[6] == 0x1A
                && header[7] == 0x0A)
            {
                return ("image/png", ".png");
            }

            if (bytesRead >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            {
                return ("image/jpeg", ".jpg");
            }

            if (bytesRead >= 6
                && header[0] == 'G'
                && header[1] == 'I'
                && header[2] == 'F'
                && header[3] == '8'
                && (header[4] == '7' || header[4] == '9')
                && header[5] == 'a')
            {
                return ("image/gif", ".gif");
            }

            if (bytesRead >= 12
                && header[0] == 'R'
                && header[1] == 'I'
                && header[2] == 'F'
                && header[3] == 'F'
                && header[8] == 'W'
                && header[9] == 'E'
                && header[10] == 'B'
                && header[11] == 'P')
            {
                return ("image/webp", ".webp");
            }

            return null;
        }
    }
}
