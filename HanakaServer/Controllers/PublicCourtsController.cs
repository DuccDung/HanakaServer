using System;
using System.Linq;
using System.Threading.Tasks;
using HanakaServer.Data;
using HanakaServer.Dtos.Courts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/public/courts")]
    public class PublicCourtsController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IConfiguration _config;

        public PublicCourtsController(PickleballDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        private string GetBaseUrl()
        {
            return (_config["PublicBaseUrl"] ?? "").TrimEnd('/');
        }

        private string? ToAbsoluteUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            var baseUrl = GetBaseUrl();
            if (string.IsNullOrWhiteSpace(baseUrl)) return url;

            if (!url.StartsWith("/")) url = "/" + url;
            return baseUrl + url;
        }

        /// <summary>
        /// GET /api/public/courts?query=&page=1&pageSize=10
        /// API public list sân cho app
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetPaged(
            [FromQuery] string? query = "",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            page = page <= 0 ? 1 : page;
            pageSize = pageSize <= 0 ? 10 : pageSize;
            pageSize = pageSize > 50 ? 50 : pageSize;

            var courtQuery = _db.Courts
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(query))
            {
                var keyword = query.Trim();

                courtQuery = courtQuery.Where(x =>
                    x.CourtName.Contains(keyword) ||
                    (x.AreaText != null && x.AreaText.Contains(keyword)) ||
                    (x.ManagerName != null && x.ManagerName.Contains(keyword)) ||
                    (x.Phone != null && x.Phone.Contains(keyword)));
            }

            courtQuery = courtQuery.OrderBy(x => x.CourtName);

            var total = await courtQuery.CountAsync();

            var courtsRaw = await courtQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.CourtId,
                    x.ExternalId,
                    x.CourtName,
                    x.AreaText,
                    x.ManagerName,
                    x.Phone
                })
                .ToListAsync();

            var courtIds = courtsRaw.Select(x => x.CourtId).ToList();

            var imagesRaw = await _db.CourtImages
                .AsNoTracking()
                .Where(x => courtIds.Contains(x.CourtId))
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.CourtImageId)
                .Select(x => new
                {
                    x.CourtId,
                    x.ImageUrl
                })
                .ToListAsync();

            var items = courtsRaw.Select(c => new CourtListItemDto
            {
                CourtId = c.CourtId,
                ExternalId = c.ExternalId,
                CourtName = c.CourtName,
                AreaText = c.AreaText,
                ManagerName = c.ManagerName,
                Phone = c.Phone,
                Images = imagesRaw
                    .Where(img => img.CourtId == c.CourtId)
                    .Select(img => ToAbsoluteUrl(img.ImageUrl) ?? "")
                    .Where(url => !string.IsNullOrWhiteSpace(url))
                    .ToList()
            }).ToList();

            var result = new CourtPagedResponseDto
            {
                Page = page,
                PageSize = pageSize,
                Total = total,
                Items = items
            };

            return Ok(result);
        }

        /// <summary>
        /// GET /api/public/courts/{courtId}
        /// API public lấy chi tiết 1 sân
        /// </summary>
        [HttpGet("{courtId:long}")]
        public async Task<IActionResult> GetById([FromRoute] long courtId)
        {
            var courtRaw = await _db.Courts
                .AsNoTracking()
                .Where(x => x.CourtId == courtId)
                .Select(x => new
                {
                    x.CourtId,
                    x.ExternalId,
                    x.CourtName,
                    x.AreaText,
                    x.ManagerName,
                    x.Phone
                })
                .FirstOrDefaultAsync();

            if (courtRaw == null)
            {
                return NotFound(new { message = "Không tìm thấy sân." });
            }

            var images = await _db.CourtImages
                .AsNoTracking()
                .Where(x => x.CourtId == courtId)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.CourtImageId)
                .Select(x => x.ImageUrl)
                .ToListAsync();

            var result = new CourtDetailDto
            {
                CourtId = courtRaw.CourtId,
                ExternalId = courtRaw.ExternalId,
                CourtName = courtRaw.CourtName,
                AreaText = courtRaw.AreaText,
                ManagerName = courtRaw.ManagerName,
                Phone = courtRaw.Phone,
                Images = images
                    .Select(x => ToAbsoluteUrl(x) ?? "")
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList()
            };

            return Ok(result);
        }
    }
}