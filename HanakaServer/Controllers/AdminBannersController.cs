using HanakaServer.Data;
using HanakaServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers.Admin
{
    [Route("api/admin/banners")]
    [ApiController]
    [Authorize(Roles = "Admin")] // dùng cookie Admin MVC của bạn
    public class AdminBannersController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IWebHostEnvironment _env;

        public AdminBannersController(PickleballDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        // GET: /api/admin/banners?status=ALL|ACTIVE|INACTIVE
        [HttpGet]
        public async Task<IActionResult> GetList([FromQuery] string status = "ALL")
        {
            var q = _db.Banners.AsNoTracking().AsQueryable();

            status = (status ?? "ALL").ToUpperInvariant();
            if (status == "ACTIVE") q = q.Where(x => x.IsActive);
            if (status == "INACTIVE") q = q.Where(x => !x.IsActive);

            var items = await q.OrderBy(x => x.SortOrder).ThenByDescending(x => x.BannerId)
                .Select(x => new
                {
                    x.BannerId,
                    x.BannerKey,
                    x.Title,
                    x.ImageUrl,
                    x.IsActive,
                    x.SortOrder
                })
                .ToListAsync();

            return Ok(new { items });
        }

        // GET: /api/admin/banners/{id}
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetDetail(long id)
        {
            var x = await _db.Banners.AsNoTracking()
                .Where(b => b.BannerId == id)
                .Select(b => new
                {
                    b.BannerId,
                    b.BannerKey,
                    b.Title,
                    b.ImageUrl,
                    b.IsActive,
                    b.SortOrder
                })
                .FirstOrDefaultAsync();

            if (x == null) return NotFound(new { message = "Không tìm thấy banner." });
            return Ok(x);
        }

        // POST: /api/admin/banners (multipart/form-data)
        [HttpPost]
        [RequestSizeLimit(10_000_000)]
        public async Task<IActionResult> Create(
            [FromForm] string bannerKey,
            [FromForm] string title,
            [FromForm] int sortOrder = 0,
            [FromForm] bool isActive = true,
            [FromForm] IFormFile? imageFile = null,
            [FromForm] string? imageUrl = null
        )
        {
            bannerKey = (bannerKey ?? "").Trim();
            title = (title ?? "").Trim();

            if (string.IsNullOrWhiteSpace(bannerKey))
                return BadRequest(new { message = "Vui lòng nhập Mã banner (BannerKey)." });

            if (string.IsNullOrWhiteSpace(title))
                return BadRequest(new { message = "Vui lòng nhập Tiêu đề." });

            // Unique key (khuyên)
            var exists = await _db.Banners.AnyAsync(x => x.BannerKey == bannerKey);
            if (exists) return BadRequest(new { message = "BannerKey đã tồn tại. Vui lòng chọn mã khác." });

            string finalUrl = (imageUrl ?? "").Trim();

            if (imageFile != null && imageFile.Length > 0)
            {
                finalUrl = await SaveBannerImage(imageFile);
            }

            if (string.IsNullOrWhiteSpace(finalUrl))
                return BadRequest(new { message = "Vui lòng chọn ảnh banner hoặc nhập ImageUrl." });

            var entity = new Banner
            {
                BannerKey = bannerKey,
                Title = title,
                ImageUrl = finalUrl,
                SortOrder = sortOrder,
                IsActive = isActive
            };

            _db.Banners.Add(entity);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                entity.BannerId,
                entity.BannerKey,
                entity.Title,
                entity.ImageUrl,
                entity.IsActive,
                entity.SortOrder
            });
        }

        // PUT: /api/admin/banners/{id} (multipart/form-data)
        [HttpPut("{id:long}")]
        [RequestSizeLimit(10_000_000)]
        public async Task<IActionResult> Update(
            long id,
            [FromForm] string bannerKey,
            [FromForm] string title,
            [FromForm] int sortOrder = 0,
            [FromForm] bool isActive = true,
            [FromForm] IFormFile? imageFile = null,
            [FromForm] string? imageUrl = null
        )
        {
            var entity = await _db.Banners.FirstOrDefaultAsync(x => x.BannerId == id);
            if (entity == null) return NotFound(new { message = "Không tìm thấy banner." });

            bannerKey = (bannerKey ?? "").Trim();
            title = (title ?? "").Trim();

            if (string.IsNullOrWhiteSpace(bannerKey))
                return BadRequest(new { message = "Vui lòng nhập Mã banner (BannerKey)." });

            if (string.IsNullOrWhiteSpace(title))
                return BadRequest(new { message = "Vui lòng nhập Tiêu đề." });

            // Unique key check (trừ chính nó)
            var exists = await _db.Banners.AnyAsync(x => x.BannerKey == bannerKey && x.BannerId != id);
            if (exists) return BadRequest(new { message = "BannerKey đã tồn tại. Vui lòng chọn mã khác." });

            entity.BannerKey = bannerKey;
            entity.Title = title;
            entity.SortOrder = sortOrder;
            entity.IsActive = isActive;

            // Image update:
            // - Nếu upload file mới: ưu tiên file
            // - Nếu không upload: nếu có imageUrl form thì set
            if (imageFile != null && imageFile.Length > 0)
            {
                entity.ImageUrl = await SaveBannerImage(imageFile);
            }
            else if (imageUrl != null) // cho phép cập nhật url thủ công
            {
                var url = imageUrl.Trim();
                if (!string.IsNullOrWhiteSpace(url))
                    entity.ImageUrl = url;
            }

            await _db.SaveChangesAsync();

            return Ok(new
            {
                entity.BannerId,
                entity.BannerKey,
                entity.Title,
                entity.ImageUrl,
                entity.IsActive,
                entity.SortOrder
            });
        }

        // DELETE: /api/admin/banners/{id}
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id)
        {
            var entity = await _db.Banners.FirstOrDefaultAsync(x => x.BannerId == id);
            if (entity == null) return NotFound(new { message = "Không tìm thấy banner." });

            _db.Banners.Remove(entity);
            await _db.SaveChangesAsync();

            return Ok(new { ok = true });
        }

        private async Task<string> SaveBannerImage(IFormFile file)
        {
            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext))
                throw new InvalidOperationException("Chỉ cho phép jpg, jpeg, png, webp.");

            var uploadsDir = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "banners");
            Directory.CreateDirectory(uploadsDir);

            var fileName = $"banner_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            await using (var stream = System.IO.File.Create(filePath))
            {
                await file.CopyToAsync(stream);
            }

            // tạo link public
            var scheme = Request.Scheme;
            var host = Request.Host.Value;
            return $"{scheme}://{host}/uploads/banners/{fileName}";
        }
    }
}