using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HanakaServer.ViewModels.PickleballWeb;

namespace HanakaServer.Controllers.Web
{
    [AllowAnonymous]
    public class PickleballWebController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Rules()
        {
            return View("Rules", BuildPage(
                title: "Luật Chơi",
                eyebrow: "Pickleball cơ bản",
                description: "Tóm tắt nhanh các quy tắc quan trọng để người chơi mới vẫn theo kịp nhịp thi đấu trong app.",
                pageKind: "rules",
                icon: "document-text-outline",
                showSearch: false,
                activeTab: "home"));
        }

        [HttpGet]
        public IActionResult Guide()
        {
            return View("Page", BuildPage(
                title: "Hướng Dẫn",
                eyebrow: "Kết nối nhanh",
                description: "Mở các kênh hướng dẫn, hỗ trợ và liên kết cộng đồng đang dùng trong hệ thống Hanaka Sport.",
                pageKind: "guide",
                icon: "map-outline",
                showSearch: false));
        }

        [HttpGet]
        public IActionResult Members()
        {
            return View("Members", BuildPage(
                title: "Thành Viên",
                eyebrow: "Cộng đồng người chơi",
                description: "Danh sách thành viên công khai với hồ sơ cơ bản, thành tích và mức điểm trình mới nhất.",
                pageKind: "members",
                icon: "people-outline",
                searchPlaceholder: "Tìm thành viên, số điện thoại, email..."));
        }

        [HttpGet]
        public IActionResult HanakaRatingInfo()
        {
            return View("HanakaRatingInfo", BuildPage(
                title: "CÃ¡ch Cháº¥m TrÃ¬nh",
                eyebrow: "Hanaka rating",
                description: "Quy Ä‘á»‹nh cháº¥m trÃ¬nh, Ä‘iá»ƒm exp vÃ  cÃ¡ch há»‡ thá»‘ng Hanaka Sport Ä‘iá»u chá»‰nh má»©c Ä‘iá»ƒm ngÆ°á»i chÆ¡i.",
                pageKind: "hanaka-rating-info",
                icon: "ribbon-outline",
                showSearch: false));
        }

        [HttpGet("/PickleballWeb/Member/{id:long}")]
        public IActionResult MemberDetail(long id)
        {
            return View("Detail", BuildDetailPage(
                title: "Chi Tiết Thành Viên",
                eyebrow: "Hồ sơ người chơi",
                description: "Thông tin hồ sơ, thành tích và lịch sử điểm trình của thành viên.",
                pageKind: "member-detail",
                entityId: id,
                backHref: "/PickleballWeb/Members"));
        }

        [HttpGet]
        public IActionResult Clubs()
        {
            return View("Page", BuildPage(
                title: "Câu Lạc Bộ",
                eyebrow: "Mạng lưới CLB",
                description: "Khám phá các câu lạc bộ pickleball đang hoạt động, thành viên và chế độ khiêu chiến.",
                pageKind: "clubs",
                icon: "shield-outline",
                searchPlaceholder: "Tìm câu lạc bộ hoặc khu vực..."));
        }

        [HttpGet("/PickleballWeb/Club/{id:long}")]
        public IActionResult ClubDetail(long id)
        {
            return View("Detail", BuildDetailPage(
                title: "Chi Tiết CLB",
                eyebrow: "Không gian đội nhóm",
                description: "Trang tổng quan câu lạc bộ, chủ nhiệm, thành viên và các chỉ số hoạt động.",
                pageKind: "club-detail",
                entityId: id,
                backHref: "/PickleballWeb/Clubs"));
        }

        [HttpGet]
        public IActionResult Coaches()
        {
            return View("Page", BuildPage(
                title: "HL Viên",
                eyebrow: "Đào tạo",
                description: "Danh sách huấn luyện viên với hồ sơ giảng dạy, khu vực hoạt động và thành tích nổi bật.",
                pageKind: "coaches",
                icon: "school-outline",
                searchPlaceholder: "Tìm huấn luyện viên hoặc khu vực dạy..."));
        }

        [HttpGet("/PickleballWeb/Coach/{id:long}")]
        public IActionResult CoachDetail(long id)
        {
            return View("Detail", BuildDetailPage(
                title: "Chi Tiết HL Viên",
                eyebrow: "Hồ sơ huấn luyện",
                description: "Thông tin giảng dạy, khu vực phụ trách, rating và thành tích của huấn luyện viên.",
                pageKind: "coach-detail",
                entityId: id,
                backHref: "/PickleballWeb/Coaches"));
        }

        [HttpGet]
        public IActionResult Courts()
        {
            return View("Page", BuildPage(
                title: "Sân Bãi",
                eyebrow: "Điểm chơi công khai",
                description: "Xem nhanh sân bãi, quản lý sân, số điện thoại liên hệ và thư viện ảnh công khai.",
                pageKind: "courts",
                icon: "location-outline",
                searchPlaceholder: "Tìm sân, người quản lý hoặc khu vực...",
                activeTab: "courts"));
        }

        [HttpGet("/PickleballWeb/Court/{id:long}")]
        public IActionResult CourtDetail(long id)
        {
            return View("Detail", BuildDetailPage(
                title: "Chi Tiết Sân",
                eyebrow: "Điểm chơi",
                description: "Ảnh sân, người quản lý, thông tin liên hệ và vị trí công khai của sân bãi.",
                pageKind: "court-detail",
                entityId: id,
                backHref: "/PickleballWeb/Courts",
                activeTab: "courts"));
        }

        [HttpGet]
        public IActionResult Referees()
        {
            return View("Page", BuildPage(
                title: "Trọng Tài",
                eyebrow: "Điều hành trận đấu",
                description: "Danh sách trọng tài với hồ sơ chuyên môn, khu vực làm việc và thành tích cá nhân.",
                pageKind: "referees",
                icon: "flag-outline",
                searchPlaceholder: "Tìm trọng tài hoặc khu vực làm việc..."));
        }

        [HttpGet("/PickleballWeb/Referee/{id:long}")]
        public IActionResult RefereeDetail(long id)
        {
            return View("Detail", BuildDetailPage(
                title: "Chi Tiết Trọng Tài",
                eyebrow: "Hồ sơ điều hành",
                description: "Thông tin chuyên môn, rating, khu vực công tác và thành tích của trọng tài.",
                pageKind: "referee-detail",
                entityId: id,
                backHref: "/PickleballWeb/Referees"));
        }

        [HttpGet]
        public IActionResult Tournaments()
        {
            return View("Page", BuildPage(
                title: "Giải Đấu",
                eyebrow: "Lịch thi đấu Hanaka",
                description: "Danh sách giải đấu công khai, thời gian tổ chức, địa điểm và chỉ số đăng ký hiện tại.",
                pageKind: "tournaments",
                icon: "trophy-outline",
                searchPlaceholder: "Tìm giải đấu, địa điểm hoặc ban tổ chức...",
                activeTab: "tournaments"));
        }

        [HttpGet("/PickleballWeb/Tournament/{id:long}")]
        public IActionResult TournamentDetail(long id)
        {
            return View("Detail", BuildDetailPage(
                title: "Chi Tiết Giải Đấu",
                eyebrow: "Theo dõi giải đấu",
                description: "Thể lệ, danh sách đăng ký, vòng đấu và thông tin công khai của giải đấu.",
                pageKind: "tournament-detail",
                entityId: id,
                backHref: "/PickleballWeb/Tournaments",
                activeTab: "tournaments"));
        }

        [HttpGet]
        public IActionResult Exchanges()
        {
            return View("Page", BuildPage(
                title: "Giao Lưu",
                eyebrow: "Thi đấu CLB",
                description: "Các kèo giao lưu và tỉ số giữa các câu lạc bộ được công khai từ hệ thống.",
                pageKind: "exchanges",
                icon: "people-circle-outline",
                searchPlaceholder: "Tìm giao lưu, CLB hoặc địa điểm..."));
        }

        [HttpGet("/PickleballWeb/Exchange/{id:long}")]
        public IActionResult ExchangeDetail(long id)
        {
            return View("Detail", BuildDetailPage(
                title: "Chi Tiết Giao Lưu",
                eyebrow: "Kèo CLB",
                description: "Thông tin tỉ số, thành tích hai đội và thời gian giao lưu giữa các câu lạc bộ.",
                pageKind: "exchange-detail",
                entityId: id,
                backHref: "/PickleballWeb/Exchanges"));
        }

        [HttpGet]
        public IActionResult Matches()
        {
            return View("Page", BuildPage(
                title: "Trận Đấu",
                eyebrow: "Match video",
                description: "Theo dõi các trận đấu công khai, video nổi bật, tỉ số và lịch thi đấu mới nhất.",
                pageKind: "matches",
                icon: "tennisball-outline",
                showSearch: false));
        }

        [HttpGet("/PickleballWeb/Match/{id:long}")]
        public IActionResult MatchDetail(long id)
        {
            return View("Detail", BuildDetailPage(
                title: "Chi Tiết Trận Đấu",
                eyebrow: "Match center",
                description: "Xem thông tin trận đấu, đội hình, điểm số và video nếu đã được cập nhật.",
                pageKind: "match-detail",
                entityId: id,
                backHref: "/PickleballWeb/Matches"));
        }

        private static PickleballWebPageViewModel BuildPage(
            string title,
            string eyebrow,
            string description,
            string pageKind,
            string icon,
            bool showSearch = true,
            string searchPlaceholder = "Tìm kiếm...",
            string activeTab = "")
        {
            return new PickleballWebPageViewModel
            {
                Title = title,
                Eyebrow = eyebrow,
                Description = description,
                PageKind = pageKind,
                BackHref = "/",
                BackLabel = "Trang chủ",
                Icon = icon,
                ShowSearch = showSearch,
                SearchPlaceholder = searchPlaceholder,
                ActiveTab = activeTab
            };
        }

        private static PickleballWebDetailPageViewModel BuildDetailPage(
            string title,
            string eyebrow,
            string description,
            string pageKind,
            long entityId,
            string backHref,
            string activeTab = "")
        {
            return new PickleballWebDetailPageViewModel
            {
                Title = title,
                Eyebrow = eyebrow,
                Description = description,
                PageKind = pageKind,
                EntityId = entityId,
                BackHref = backHref,
                BackLabel = "Danh sách",
                ActiveTab = activeTab
            };
        }
    }
}
