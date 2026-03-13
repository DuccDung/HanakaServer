using HanakaServer.Data;
using HanakaServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers.Admin
{
    [Route("api/admin/courts")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminCourtsController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;

        public AdminCourtsController(
            PickleballDbContext db,
            IWebHostEnvironment env,
            IConfiguration config)
        {
            _db = db;
            _env = env;
            _config = config;
        }

        private string? ToAbsoluteUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (url.StartsWith("http://") || url.StartsWith("https://")) return url;
            return (_config["PublicBaseUrl"] ?? "").TrimEnd('/') + url;
        }

        private async Task<List<string>> SaveCourtImages(List<IFormFile> files)
        {
            var result = new List<string>();
            if (files == null || files.Count == 0) return result;

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var uploadsDir = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "courts");
            Directory.CreateDirectory(uploadsDir);

            foreach (var file in files)
            {
                if (file == null || file.Length == 0) continue;

                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!allowed.Contains(ext))
                    throw new InvalidOperationException($"File {file.FileName} không đúng định dạng. Chỉ hỗ trợ jpg, jpeg, png, webp.");

                var fileName = $"court_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{ext}";
                var filePath = Path.Combine(uploadsDir, fileName);

                await using (var stream = System.IO.File.Create(filePath))
                {
                    await file.CopyToAsync(stream);
                }

                result.Add($"/uploads/courts/{fileName}");
            }

            return result;
        }

        private void TryDeletePhysicalFile(string? relativeUrl)
        {
            if (string.IsNullOrWhiteSpace(relativeUrl)) return;
            if (!relativeUrl.StartsWith("/")) return;

            var fullPath = Path.Combine(_env.WebRootPath ?? "wwwroot", relativeUrl.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
            if (System.IO.File.Exists(fullPath))
            {
                try { System.IO.File.Delete(fullPath); } catch { }
            }
        }

        // GET: /api/admin/courts?q=...
        [HttpGet]
        public async Task<IActionResult> GetList([FromQuery] string? q = null)
        {
            var query = _db.Courts
                .AsNoTracking()
                .Include(x => x.CourtImages)
                .AsQueryable();

            q = (q ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(x =>
                    x.CourtName.Contains(q) ||
                    (x.AreaText != null && x.AreaText.Contains(q)) ||
                    (x.ManagerName != null && x.ManagerName.Contains(q)) ||
                    (x.Phone != null && x.Phone.Contains(q)));
            }

            var items = await query
                .OrderByDescending(x => x.CourtId)
                .Select(x => new
                {
                    x.CourtId,
                    x.ExternalId,
                    x.CourtName,
                    x.AreaText,
                    x.ManagerName,
                    x.Phone,
                    x.CreatedAt,
                    ImageCount = x.CourtImages.Count,
                    FirstImage = x.CourtImages
                        .OrderBy(i => i.SortOrder)
                        .ThenBy(i => i.CourtImageId)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault()
                })
                .ToListAsync();

            var mapped = items.Select(x => new
            {
                x.CourtId,
                x.ExternalId,
                x.CourtName,
                x.AreaText,
                x.ManagerName,
                x.Phone,
                x.CreatedAt,
                x.ImageCount,
                FirstImage = ToAbsoluteUrl(x.FirstImage)
            });

            return Ok(new { items = mapped });
        }

        // GET: /api/admin/courts/{id}
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetDetail(long id)
        {
            var court = await _db.Courts
                .AsNoTracking()
                .Include(x => x.CourtImages.OrderBy(i => i.SortOrder))
                .FirstOrDefaultAsync(x => x.CourtId == id);

            if (court == null)
                return NotFound(new { message = "Không tìm thấy sân." });

            return Ok(new
            {
                court.CourtId,
                court.ExternalId,
                court.CourtName,
                court.AreaText,
                court.ManagerName,
                court.Phone,
                court.CreatedAt,
                Images = court.CourtImages
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.CourtImageId)
                    .Select(x => new
                    {
                        x.CourtImageId,
                        ImageUrl = ToAbsoluteUrl(x.ImageUrl),
                        x.SortOrder
                    })
                    .ToList()
            });
        }

        // POST: /api/admin/courts
        [HttpPost]
        [RequestSizeLimit(50_000_000)]
        public async Task<IActionResult> Create(
            [FromForm] string courtName,
            [FromForm] string? areaText,
            [FromForm] string? managerName,
            [FromForm] string? phone,
            [FromForm] string? externalId,
            [FromForm] List<IFormFile>? imageFiles
        )
        {
            courtName = (courtName ?? "").Trim();
            areaText = areaText?.Trim();
            managerName = managerName?.Trim();
            phone = phone?.Trim();
            externalId = externalId?.Trim();

            if (string.IsNullOrWhiteSpace(courtName))
                return BadRequest(new { message = "Vui lòng nhập tên sân." });

            if (!string.IsNullOrWhiteSpace(externalId))
            {
                var existedExternal = await _db.Courts.AnyAsync(x => x.ExternalId == externalId);
                if (existedExternal)
                    return BadRequest(new { message = "ExternalId đã tồn tại." });
            }

            var imageUrls = await SaveCourtImages(imageFiles ?? new List<IFormFile>());

            var entity = new Court
            {
                CourtName = courtName,
                AreaText = areaText,
                ManagerName = managerName,
                Phone = phone,
                ExternalId = string.IsNullOrWhiteSpace(externalId) ? null : externalId,
                CreatedAt = DateTime.UtcNow
            };

            _db.Courts.Add(entity);
            await _db.SaveChangesAsync();

            if (imageUrls.Count > 0)
            {
                var images = imageUrls.Select((url, index) => new CourtImage
                {
                    CourtId = entity.CourtId,
                    ImageUrl = url,
                    SortOrder = index
                }).ToList();

                _db.CourtImages.AddRange(images);
                await _db.SaveChangesAsync();
            }

            return Ok(new
            {
                entity.CourtId,
                entity.CourtName
            });
        }

        // PUT: /api/admin/courts/{id}
        // keepImageIdsCsv = "1,2,5"
        [HttpPut("{id:long}")]
        [RequestSizeLimit(50_000_000)]
        public async Task<IActionResult> Update(
            long id,
            [FromForm] string courtName,
            [FromForm] string? areaText,
            [FromForm] string? managerName,
            [FromForm] string? phone,
            [FromForm] string? externalId,
            [FromForm] string? keepImageIdsCsv,
            [FromForm] List<IFormFile>? imageFiles
        )
        {
            var entity = await _db.Courts
                .Include(x => x.CourtImages)
                .FirstOrDefaultAsync(x => x.CourtId == id);

            if (entity == null)
                return NotFound(new { message = "Không tìm thấy sân." });

            courtName = (courtName ?? "").Trim();
            areaText = areaText?.Trim();
            managerName = managerName?.Trim();
            phone = phone?.Trim();
            externalId = externalId?.Trim();

            if (string.IsNullOrWhiteSpace(courtName))
                return BadRequest(new { message = "Vui lòng nhập tên sân." });

            if (!string.IsNullOrWhiteSpace(externalId))
            {
                var existedExternal = await _db.Courts.AnyAsync(x => x.ExternalId == externalId && x.CourtId != id);
                if (existedExternal)
                    return BadRequest(new { message = "ExternalId đã tồn tại." });
            }

            entity.CourtName = courtName;
            entity.AreaText = areaText;
            entity.ManagerName = managerName;
            entity.Phone = phone;
            entity.ExternalId = string.IsNullOrWhiteSpace(externalId) ? null : externalId;

            var keepIds = new HashSet<long>();
            if (!string.IsNullOrWhiteSpace(keepImageIdsCsv))
            {
                keepIds = keepImageIdsCsv
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => long.TryParse(x.Trim(), out var v) ? v : 0)
                    .Where(x => x > 0)
                    .ToHashSet();
            }

            var toDelete = entity.CourtImages
                .Where(x => !keepIds.Contains(x.CourtImageId))
                .ToList();

            if (toDelete.Count > 0)
            {
                foreach (var img in toDelete)
                {
                    TryDeletePhysicalFile(img.ImageUrl);
                }
                _db.CourtImages.RemoveRange(toDelete);
            }

            var newUrls = await SaveCourtImages(imageFiles ?? new List<IFormFile>());

            int sortStart = entity.CourtImages
                .Where(x => keepIds.Contains(x.CourtImageId))
                .Select(x => x.SortOrder)
                .DefaultIfEmpty(-1)
                .Max() + 1;

            foreach (var url in newUrls)
            {
                _db.CourtImages.Add(new CourtImage
                {
                    CourtId = entity.CourtId,
                    ImageUrl = url,
                    SortOrder = sortStart++
                });
            }

            await _db.SaveChangesAsync();

            return Ok(new { ok = true });
        }

        // DELETE: /api/admin/courts/{id}
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id)
        {
            var entity = await _db.Courts
                .Include(x => x.CourtImages)
                .FirstOrDefaultAsync(x => x.CourtId == id);

            if (entity == null)
                return NotFound(new { message = "Không tìm thấy sân." });

            foreach (var img in entity.CourtImages)
            {
                TryDeletePhysicalFile(img.ImageUrl);
            }

            _db.CourtImages.RemoveRange(entity.CourtImages);
            _db.Courts.Remove(entity);

            await _db.SaveChangesAsync();

            return Ok(new { ok = true });
        }
    }
}