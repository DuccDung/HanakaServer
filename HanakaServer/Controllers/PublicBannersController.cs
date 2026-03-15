using HanakaServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HanakaServer.Controllers.Public
{
    [Route("api/public/banners")]
    [ApiController]
    [AllowAnonymous]
    public class PublicBannersController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IConfiguration _config;

        public PublicBannersController(PickleballDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        // Helper: convert relative -> absolute để trả ra cho mobile dùng luôn
        private string? ToAbsoluteUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            // nếu đã là absolute thì trả nguyên
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            var baseUrl = _config["PublicBaseUrl"]?.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl)) return url;

            return $"{baseUrl}{url}";
        }

        // GET: /api/public/banners
        [HttpGet]
        public async Task<IActionResult> GetActiveBanners()
        {
            var items = await _db.Banners
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenByDescending(x => x.BannerId)
                .Select(x => new
                {
                    x.BannerId,
                    x.BannerKey,
                    x.Title,
                    x.ImageUrl,
                    x.SortOrder
                })
                .ToListAsync();

            var mappedItems = items.Select(x => new
            {
                bannerId = x.BannerId,
                bannerKey = x.BannerKey,
                title = x.Title,
                imageUrl = ToAbsoluteUrl(x.ImageUrl),
                sortOrder = x.SortOrder
            });

            return Ok(new { items = mappedItems });
        }
        [HttpGet("/api/links")]
        public async Task<IActionResult> GetLinks([FromQuery] string? type = null)
        {
            var q = _db.Links.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(type))
                q = q.Where(x => x.Type == type);

            var items = await q
                .OrderByDescending(x => x.LinkId)
                .Select(x => new
                {
                    x.LinkId,
                    Link = x.Url,
                    x.Type
                })
                .ToListAsync();

            return Ok(new { items });
        }
    }
}