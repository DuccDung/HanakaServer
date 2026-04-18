using HanakaServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers.Public
{
    [ApiController]
    [Route("api/public/exchanges")]
    [AllowAnonymous]
    public class PublicExchangesController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IConfiguration _config;

        public PublicExchangesController(PickleballDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        private string? ToAbsoluteUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            var baseUrl = (_config["PublicBaseUrl"] ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl)) return url;

            if (!url.StartsWith("/"))
            {
                url = "/" + url;
            }

            return $"{baseUrl}{url}";
        }

        [HttpGet]
        public async Task<IActionResult> GetPaged(
            [FromQuery] string? query = "",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            page = page <= 0 ? 1 : page;
            pageSize = pageSize <= 0 ? 10 : pageSize;
            pageSize = pageSize > 50 ? 50 : pageSize;

            var exchangeQuery = _db.Exchanges
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(query))
            {
                var keyword = query.Trim();
                exchangeQuery = exchangeQuery.Where(x =>
                    x.LeftClubName.Contains(keyword) ||
                    x.RightClubName.Contains(keyword) ||
                    (x.LocationText != null && x.LocationText.Contains(keyword)) ||
                    (x.ScoreText != null && x.ScoreText.Contains(keyword)));
            }

            exchangeQuery = exchangeQuery
                .OrderByDescending(x => x.MatchTime ?? x.CreatedAt)
                .ThenByDescending(x => x.ExchangeId);

            var total = await exchangeQuery.CountAsync();

            var items = await exchangeQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.ExchangeId,
                    x.ExternalId,
                    x.LeftClubName,
                    x.LeftLogoUrl,
                    x.LeftW,
                    x.LeftL,
                    x.LeftD,
                    x.RightClubName,
                    x.RightLogoUrl,
                    x.ScoreText,
                    x.TimeTextRaw,
                    x.MatchTime,
                    x.AgoText,
                    x.LocationText,
                    x.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                page,
                pageSize,
                total,
                items = items.Select(x => new
                {
                    x.ExchangeId,
                    x.ExternalId,
                    x.LeftClubName,
                    LeftLogoUrl = ToAbsoluteUrl(x.LeftLogoUrl),
                    x.LeftW,
                    x.LeftL,
                    x.LeftD,
                    x.RightClubName,
                    RightLogoUrl = ToAbsoluteUrl(x.RightLogoUrl),
                    x.ScoreText,
                    x.TimeTextRaw,
                    x.MatchTime,
                    x.AgoText,
                    x.LocationText,
                    x.CreatedAt
                })
            });
        }

        [HttpGet("{exchangeId:long}")]
        public async Task<IActionResult> GetById(long exchangeId)
        {
            var item = await _db.Exchanges
                .AsNoTracking()
                .Where(x => x.ExchangeId == exchangeId)
                .Select(x => new
                {
                    x.ExchangeId,
                    x.ExternalId,
                    x.LeftClubName,
                    x.LeftLogoUrl,
                    x.LeftW,
                    x.LeftL,
                    x.LeftD,
                    x.RightClubName,
                    x.RightLogoUrl,
                    x.ScoreText,
                    x.TimeTextRaw,
                    x.MatchTime,
                    x.AgoText,
                    x.LocationText,
                    x.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (item == null)
            {
                return NotFound(new { message = "Khong tim thay tran giao luu." });
            }

            return Ok(new
            {
                item.ExchangeId,
                item.ExternalId,
                item.LeftClubName,
                LeftLogoUrl = ToAbsoluteUrl(item.LeftLogoUrl),
                item.LeftW,
                item.LeftL,
                item.LeftD,
                item.RightClubName,
                RightLogoUrl = ToAbsoluteUrl(item.RightLogoUrl),
                item.ScoreText,
                item.TimeTextRaw,
                item.MatchTime,
                item.AgoText,
                item.LocationText,
                item.CreatedAt
            });
        }
    }
}
