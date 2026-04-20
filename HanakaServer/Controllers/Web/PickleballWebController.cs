using HanakaServer.ViewModels.PickleballWeb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
        public IActionResult Notifications()
        {
            return View("Page", BuildPage(
                title: "Thông Báo",
                eyebrow: "Lịch thi đấu của bạn",
                description: "Theo dõi các thông báo trận đấu sắp diễn ra giống với màn hình thông báo trong ứng dụng.",
                pageKind: "notifications",
                icon: "notifications-outline",
                showSearch: false,
                activeTab: "home"));
        }

        [HttpGet]
        public IActionResult Settings()
        {
            return View("Page", BuildPage(
                title: "Cài Đặt",
                eyebrow: "Tùy biến tài khoản",
                description: "Màn hình cài đặt theo phong cách app với các lối tắt tài khoản, an toàn cộng đồng và thông tin ứng dụng.",
                pageKind: "settings",
                icon: "settings-outline",
                showSearch: false,
                activeTab: "home"));
        }

        [HttpGet]
        public IActionResult Videos()
        {
            return View("Page", BuildPage(
                title: "Videos",
                eyebrow: "Match video",
                description: "Danh sách video trận đấu, phân loại theo tab và cách hiển thị gần với ứng dụng mobile.",
                pageKind: "videos",
                icon: "play-circle-outline",
                showSearch: false,
                activeTab: "videos"));
        }

        [HttpGet("/PickleballWeb/Video/{id:long}")]
        public IActionResult VideoDetail(long id)
        {
            return View("Page", BuildPage(
                title: "Xem video",
                eyebrow: "Video player",
                description: "Màn hình xem video trận đấu theo phong cách app Hanaka Sport.",
                pageKind: "video-player",
                icon: "play-circle-outline",
                showSearch: false,
                activeTab: "videos",
                entityId: id,
                backHref: "/PickleballWeb/Videos",
                backLabel: "Videos"));
        }

        [HttpGet]
        public IActionResult Chats()
        {
            return View("Page", BuildPage(
                title: "Trò chuyện",
                eyebrow: "Tin nhắn CLB",
                description: "Danh sách phòng chat câu lạc bộ và trạng thái tin nhắn gần nhất giống luồng app.",
                pageKind: "chat-list",
                icon: "chatbubbles-outline",
                showSearch: false,
                activeTab: "chat"));
        }

        [HttpGet("/PickleballWeb/Chat/{id:long}")]
        public IActionResult ChatRoom(long id)
        {
            return View("Page", BuildPage(
                title: "Chat CLB",
                eyebrow: "Phòng chat",
                description: "Màn hình tin nhắn CLB với luồng xem và gửi tin cơ bản trên web.",
                pageKind: "chat-room",
                icon: "chatbubbles-outline",
                showSearch: false,
                activeTab: "chat",
                entityId: id,
                backHref: "/PickleballWeb/Chats",
                backLabel: "Trò chuyện"));
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
                title: "Cách Chấm Trình",
                eyebrow: "Hanaka rating",
                description: "Quy định chấm trình, điểm exp và cách hệ thống Hanaka Sport điều chỉnh mức điểm người chơi.",
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
            return View("ClubDetail", BuildDetailPage(
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

        [HttpGet("/PickleballWeb/Tournament/{id:long}/Registrations")]
        public IActionResult TournamentRegistrations(long id)
        {
            return View("Detail", BuildDetailPage(
                title: "Danh Sách Đăng Ký",
                eyebrow: "Public registrations",
                description: "Danh sách vận động viên, trạng thái ghép cặp và chỉ số đăng ký công khai của giải đấu.",
                pageKind: "tournament-registrations",
                entityId: id,
                backHref: $"/PickleballWeb/Tournament/{id}",
                backLabel: "Chi tiết",
                activeTab: "tournaments"));
        }

        [HttpGet("/PickleballWeb/Tournament/{id:long}/Rule")]
        public IActionResult TournamentRule(long id)
        {
            return View("Detail", BuildDetailPage(
                title: "Thể Lệ Giải",
                eyebrow: "Tournament rule",
                description: "Thể lệ công khai, quy định tham gia và hướng dẫn thi đấu của giải.",
                pageKind: "tournament-rule-page",
                entityId: id,
                backHref: $"/PickleballWeb/Tournament/{id}",
                backLabel: "Chi tiết",
                activeTab: "tournaments"));
        }

        [HttpGet("/PickleballWeb/Tournament/{id:long}/Schedule")]
        public IActionResult TournamentSchedule(long id)
        {
            return View("Detail", BuildDetailPage(
                title: "Lịch Thi Đấu",
                eyebrow: "Tournament schedule",
                description: "Theo dõi vòng đấu, bảng đấu, sân thi đấu và diễn biến trận từ giải đấu.",
                pageKind: "tournament-schedule-page",
                entityId: id,
                backHref: $"/PickleballWeb/Tournament/{id}",
                backLabel: "Chi tiết",
                activeTab: "tournaments"));
        }

        [HttpGet("/PickleballWeb/Tournament/{id:long}/Standings")]
        public IActionResult TournamentStandings(long id)
        {
            return View("Detail", BuildDetailPage(
                title: "Bảng Xếp Hạng",
                eyebrow: "Tournament standings",
                description: "Theo dõi thứ hạng các đội, điểm số và hiệu số theo từng vòng đấu công khai.",
                pageKind: "tournament-standings-page",
                entityId: id,
                backHref: $"/PickleballWeb/Tournament/{id}",
                backLabel: "Chi tiết",
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
            string activeTab = "",
            long? entityId = null,
            string backHref = "/",
            string backLabel = "Trang chủ")
        {
            return new PickleballWebPageViewModel
            {
                Title = title,
                Eyebrow = eyebrow,
                Description = description,
                PageKind = pageKind,
                EntityId = entityId,
                BackHref = backHref,
                BackLabel = backLabel,
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
            string backLabel = "Danh sách",
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
                BackLabel = backLabel,
                ActiveTab = activeTab
            };
        }
    }
}
