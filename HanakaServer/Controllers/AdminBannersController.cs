using HanakaServer.Data;
using HanakaServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HanakaServer.Controllers.Admin
{
    [Route("api/admin/banners")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminBannersController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;

        public AdminBannersController(
            PickleballDbContext db,
            IWebHostEnvironment env,
            IConfiguration config)
        {
            _db = db;
            _env = env;
            _config = config;
        }

        // Helper: convert relative -> absolute để trả response
        private string? ToAbsoluteUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            return _config["PublicBaseUrl"] + url;
        }

        // Helper: normalize imageUrl về relative để lưu DB
        private string? NormalizeImageToRelative(string? imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl)) return null;

            imageUrl = imageUrl.Trim();

            // relative sẵn
            if (imageUrl.StartsWith("/")) return imageUrl;

            // absolute => lấy path
            if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
            {
                return uri.PathAndQuery;
            }

            // chuỗi lạ => giữ nguyên
            return imageUrl;
        }

        // GET: /api/admin/banners?status=ALL|ACTIVE|INACTIVE
        [HttpGet]
        public async Task<IActionResult> GetList([FromQuery] string status = "ALL")
        {
            var q = _db.Banners.AsNoTracking().AsQueryable();

            status = (status ?? "ALL").Trim().ToUpperInvariant();

            if (status == "ACTIVE")
                q = q.Where(x => x.IsActive);
            else if (status == "INACTIVE")
                q = q.Where(x => !x.IsActive);

            var items = await q
                .OrderBy(x => x.SortOrder)
                .ThenByDescending(x => x.BannerId)
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

            var mappedItems = items.Select(x => new
            {
                x.BannerId,
                x.BannerKey,
                x.Title,
                ImageUrl = ToAbsoluteUrl(x.ImageUrl),
                x.IsActive,
                x.SortOrder
            });

            return Ok(new { items = mappedItems });
        }

        // GET: /api/admin/banners/{id}
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetDetail(long id)
        {
            var x = await _db.Banners
                .AsNoTracking()
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

            if (x == null)
                return NotFound(new { message = "Không tìm thấy banner." });

            return Ok(new
            {
                x.BannerId,
                x.BannerKey,
                x.Title,
                ImageUrl = ToAbsoluteUrl(x.ImageUrl),
                x.IsActive,
                x.SortOrder
            });
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

            var exists = await _db.Banners.AnyAsync(x => x.BannerKey == bannerKey);
            if (exists)
                return BadRequest(new { message = "BannerKey đã tồn tại. Vui lòng chọn mã khác." });

            // Ưu tiên normalize imageUrl về relative
            string? finalUrl = NormalizeImageToRelative(imageUrl);

            // Nếu có upload file thì ưu tiên file
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
                ImageUrl = ToAbsoluteUrl(entity.ImageUrl),
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
            if (entity == null)
                return NotFound(new { message = "Không tìm thấy banner." });

            bannerKey = (bannerKey ?? "").Trim();
            title = (title ?? "").Trim();

            if (string.IsNullOrWhiteSpace(bannerKey))
                return BadRequest(new { message = "Vui lòng nhập Mã banner (BannerKey)." });

            if (string.IsNullOrWhiteSpace(title))
                return BadRequest(new { message = "Vui lòng nhập Tiêu đề." });

            var exists = await _db.Banners.AnyAsync(x => x.BannerKey == bannerKey && x.BannerId != id);
            if (exists)
                return BadRequest(new { message = "BannerKey đã tồn tại. Vui lòng chọn mã khác." });

            entity.BannerKey = bannerKey;
            entity.Title = title;
            entity.SortOrder = sortOrder;
            entity.IsActive = isActive;

            // Cập nhật ảnh:
            // - có file mới => ưu tiên file
            // - không có file => nếu có imageUrl thì normalize rồi lưu
            if (imageFile != null && imageFile.Length > 0)
            {
                entity.ImageUrl = await SaveBannerImage(imageFile);
            }
            else if (imageUrl != null)
            {
                var normalized = NormalizeImageToRelative(imageUrl);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    entity.ImageUrl = normalized;
                }
            }

            await _db.SaveChangesAsync();

            return Ok(new
            {
                entity.BannerId,
                entity.BannerKey,
                entity.Title,
                ImageUrl = ToAbsoluteUrl(entity.ImageUrl),
                entity.IsActive,
                entity.SortOrder
            });
        }

        // DELETE: /api/admin/banners/{id}
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id)
        {
            var entity = await _db.Banners.FirstOrDefaultAsync(x => x.BannerId == id);
            if (entity == null)
                return NotFound(new { message = "Không tìm thấy banner." });

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

            // Lưu relative vào DB
            return $"/uploads/banners/{fileName}";
        }
    }
}