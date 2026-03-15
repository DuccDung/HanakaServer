using HanakaServer.Data;
using HanakaServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers.Admin
{
    [Route("api/admin/links")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminLinksController : ControllerBase
    {
        private readonly PickleballDbContext _db;

        public AdminLinksController(PickleballDbContext db)
        {
            _db = db;
        }

        // GET: /api/admin/links?type=guide
        [HttpGet]
        public async Task<IActionResult> GetList([FromQuery] string? type = null)
        {
            var q = _db.Links.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(type))
            {
                type = type.Trim();
                q = q.Where(x => x.Type == type);
            }

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

        // GET: /api/admin/links/{id}
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetDetail(long id)
        {
            var item = await _db.Links
                .AsNoTracking()
                .Where(x => x.LinkId == id)
                .Select(x => new
                {
                    x.LinkId,
                    Link = x.Url,
                    x.Type
                })
                .FirstOrDefaultAsync();

            if (item == null)
                return NotFound(new { message = "Không tìm thấy link." });

            return Ok(item);
        }

        // POST: /api/admin/links
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] LinkCreateUpdateRequest req)
        {
            var link = (req.Link ?? "").Trim();
            var type = (req.Type ?? "").Trim();

            if (string.IsNullOrWhiteSpace(link))
                return BadRequest(new { message = "Vui lòng nhập Link." });

            if (string.IsNullOrWhiteSpace(type))
                return BadRequest(new { message = "Vui lòng nhập Type." });

            var entity = new Link
            {
                Url = link,
                Type = type
            };

            _db.Links.Add(entity);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                entity.LinkId,
                Link = entity.Url,
                entity.Type
            });
        }

        // PUT: /api/admin/links/{id}
        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] LinkCreateUpdateRequest req)
        {
            var entity = await _db.Links.FirstOrDefaultAsync(x => x.LinkId == id);
            if (entity == null)
                return NotFound(new { message = "Không tìm thấy link." });

            var link = (req.Link ?? "").Trim();
            var type = (req.Type ?? "").Trim();

            if (string.IsNullOrWhiteSpace(link))
                return BadRequest(new { message = "Vui lòng nhập Link." });

            if (string.IsNullOrWhiteSpace(type))
                return BadRequest(new { message = "Vui lòng nhập Type." });

            entity.Url = link;
            entity.Type = type;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                entity.LinkId,
                Link = entity.Url,
                entity.Type
            });
        }

        // DELETE: /api/admin/links/{id}
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id)
        {
            var entity = await _db.Links.FirstOrDefaultAsync(x => x.LinkId == id);
            if (entity == null)
                return NotFound(new { message = "Không tìm thấy link." });

            _db.Links.Remove(entity);
            await _db.SaveChangesAsync();

            return Ok(new { ok = true });
        }

        public class LinkCreateUpdateRequest
        {
            public string? Link { get; set; }
            public string? Type { get; set; }
        }
    }
}