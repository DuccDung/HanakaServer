(function () {
    function qs(selector, root) {
        return (root || document).querySelector(selector);
    }

    function qsa(selector, root) {
        return Array.from((root || document).querySelectorAll(selector));
    }

    function trimToEmpty(value) {
        return String(value ?? "").trim();
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function parseDate(value) {
        if (!value) {
            return null;
        }

        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? null : date;
    }

    function formatDate(value) {
        const date = parseDate(value);
        if (!date) {
            return "Chưa cập nhật";
        }

        return new Intl.DateTimeFormat("vi-VN", {
            day: "2-digit",
            month: "2-digit",
            year: "numeric"
        }).format(date);
    }

    function formatDateTime(value) {
        const date = parseDate(value);
        if (!date) {
            return "Chưa cập nhật";
        }

        return new Intl.DateTimeFormat("vi-VN", {
            day: "2-digit",
            month: "2-digit",
            year: "numeric",
            hour: "2-digit",
            minute: "2-digit"
        }).format(date);
    }

    function formatDateOrDash(value) {
        const date = parseDate(value);
        if (!date) {
            return "-";
        }

        return new Intl.DateTimeFormat("vi-VN", {
            day: "2-digit",
            month: "2-digit",
            year: "numeric"
        }).format(date);
    }

    function formatDateTimeOrDash(value) {
        const date = parseDate(value);
        if (!date) {
            return "-";
        }

        return new Intl.DateTimeFormat("vi-VN", {
            day: "2-digit",
            month: "2-digit",
            year: "numeric",
            hour: "2-digit",
            minute: "2-digit"
        }).format(date);
    }

    function toNumber(value) {
        const number = Number(value);
        return Number.isFinite(number) ? number : 0;
    }

    function formatDecimal(value) {
        const number = Number(value);
        return Number.isFinite(number) ? number.toFixed(1) : "0.0";
    }

    function formatScore(value) {
        const number = Number(value);
        return Number.isFinite(number) ? number.toFixed(2) : "0.00";
    }

    function initials(name) {
        const words = trimToEmpty(name).split(/\s+/).filter(Boolean).slice(0, 2);
        if (words.length === 0) {
            return "HS";
        }

        return words.map(function (word) {
            return word.charAt(0);
        }).join("").toUpperCase();
    }

    function buildSafeHref(value, fallback) {
        const href = trimToEmpty(value);

        if (!href) {
            return fallback || "#";
        }

        if (/^(javascript:|data:)/i.test(href)) {
            return fallback || "#";
        }

        if (
            href.startsWith("#") ||
            href.startsWith("/") ||
            /^https?:\/\//i.test(href) ||
            /^mailto:/i.test(href) ||
            /^tel:/i.test(href)
        ) {
            return href;
        }

        return href;
    }

    function isExternalHref(href) {
        return /^https?:\/\//i.test(href) || /^mailto:/i.test(href) || /^tel:/i.test(href);
    }

    function normalizeMediaUrl(value) {
        const url = trimToEmpty(value);

        if (!url) {
            return "";
        }

        if (url.startsWith("/")) {
            return `${window.location.origin}${url}`;
        }

        try {
            const parsed = new URL(url, window.location.origin);
            if (parsed.pathname.startsWith("/uploads/") && parsed.origin !== window.location.origin) {
                return `${window.location.origin}${parsed.pathname}${parsed.search}`;
            }

            return parsed.toString();
        } catch (_error) {
            return url;
        }
    }

    function mediaMarkup(url, alt, fallbackText) {
        const src = normalizeMediaUrl(url);

        if (src) {
            return `<img src="${escapeHtml(src)}" alt="${escapeHtml(alt)}" loading="lazy">`;
        }

        return [
            '<div class="list-card__media">',
            '<div class="identity__avatar" style="width:100%;height:100%;border-radius:0;box-shadow:none;">',
            escapeHtml(fallbackText || initials(alt)),
            "</div>",
            "</div>"
        ].join("");
    }

    function avatarMarkup(name, avatarUrl, className) {
        const src = normalizeMediaUrl(avatarUrl);
        const cssClass = className || "identity__avatar";

        if (src) {
            return `<span class="${escapeHtml(cssClass)}"><img src="${escapeHtml(src)}" alt="${escapeHtml(name)}" loading="lazy"></span>`;
        }

        return `<span class="${escapeHtml(cssClass)}">${escapeHtml(initials(name))}</span>`;
    }

    async function fetchJson(url) {
        const response = await fetch(url, {
            headers: { Accept: "application/json" },
            cache: "no-store"
        });

        if (!response.ok) {
            throw new Error(`Request failed: ${response.status}`);
        }

        return response.json();
    }

    function totalText(total, noun) {
        const number = toNumber(total);
        return `${number} ${noun}`;
    }

    function metaChip(label, icon) {
        const safeLabel = escapeHtml(label);
        if (icon) {
            return `<span class="meta-chip"><ion-icon name="${escapeHtml(icon)}"></ion-icon>${safeLabel}</span>`;
        }

        return `<span class="meta-chip">${safeLabel}</span>`;
    }

    function statusText(item) {
        return trimToEmpty(item?.statusText) ||
            trimToEmpty(item?.stateText) ||
            trimToEmpty(item?.status) ||
            trimToEmpty(item?.tournamentStatus) ||
            "Hanaka Sport";
    }

    function locationText(item) {
        return [trimToEmpty(item?.locationText), trimToEmpty(item?.areaText)]
            .filter(Boolean)
            .join(" · ");
    }

    function detailSection(title, body) {
        return [
            '<section class="detail-section">',
            `<h2 class="detail-section__title">${escapeHtml(title)}</h2>`,
            body,
            "</section>"
        ].join("");
    }

    function htmlToPlainText(html, emptyText) {
        const source = trimToEmpty(html);
        if (!source) {
            return emptyText || "Chưa cập nhật";
        }

        const container = document.createElement("div");
        container.innerHTML = source
            .replace(/<br\s*\/?>/gi, "\n")
            .replace(/<\/p>/gi, "\n")
            .replace(/<\/div>/gi, "\n")
            .replace(/<li>/gi, "• ")
            .replace(/<\/li>/gi, "\n");

        const plain = trimToEmpty(container.textContent || container.innerText || "");
        return plain || emptyText || "Chưa cập nhật";
    }

    function achievementLabel(item) {
        const type = trimToEmpty(item?.achievementType).toUpperCase();
        if (type === "FIRST") {
            return "Giải Nhất";
        }

        if (type === "SECOND") {
            return "Giải Nhì";
        }

        if (type === "THIRD") {
            return "Giải Ba";
        }

        return trimToEmpty(item?.achievementLabel) || "Thành tích";
    }

    function achievementIcon(item) {
        const type = trimToEmpty(item?.achievementType).toUpperCase();
        if (type === "FIRST") {
            return "trophy";
        }

        if (type === "SECOND") {
            return "medal";
        }

        if (type === "THIRD") {
            return "ribbon";
        }

        return "award";
    }

    function achievementColor(item) {
        const type = trimToEmpty(item?.achievementType).toUpperCase();
        if (type === "FIRST") {
            return "#F59E0B";
        }

        if (type === "SECOND") {
            return "#9CA3AF";
        }

        if (type === "THIRD") {
            return "#D97706";
        }

        return "#2563EB";
    }

    function simpleRows(items, valueGetter) {
        if (!Array.isArray(items) || items.length === 0) {
            return '<div class="detail-note">Chưa có dữ liệu để hiển thị.</div>';
        }

        return [
            '<div class="detail-list">',
            items.map(function (item) {
                return valueGetter(item);
            }).join(""),
            "</div>"
        ].join("");
    }

    function renderMemberCard(item) {
        const href = buildSafeHref(`/PickleballWeb/Member/${item.userId}`, "/PickleballWeb/Members");
        const chips = [
            item.verified ? metaChip("Đã xác thực", "shield-checkmark-outline") : "",
            trimToEmpty(item.city) ? metaChip(item.city, "location-outline") : "",
            trimToEmpty(item.gender) ? metaChip(item.gender, "person-outline") : "",
            item.ratingSingle != null ? metaChip(`Single ${formatDecimal(item.ratingSingle)}`) : "",
            item.ratingDouble != null ? metaChip(`Double ${formatDecimal(item.ratingDouble)}`) : ""
        ].filter(Boolean).join("");

        return [
            `<a class="list-card" href="${escapeHtml(href)}">`,
            '<div class="list-card__body">',
            '<div class="identity">',
            avatarMarkup(item.fullName, item.avatarUrl),
            '<div class="identity__copy">',
            `<strong>${escapeHtml(trimToEmpty(item.fullName) || "Thành viên Hanaka")}</strong>`,
            `<span>${escapeHtml(trimToEmpty(item.ratingUpdatedAt) ? `Cập nhật rating ${formatDate(item.ratingUpdatedAt)}` : "Hồ sơ người chơi công khai")}</span>`,
            "</div>",
            "</div>",
            `<div class="list-card__meta">${chips}</div>`,
            "</div>",
            "</a>"
        ].join("");
    }

    function renderClubCard(item) {
        const href = buildSafeHref(`/PickleballWeb/Club/${item.clubId}`, "/PickleballWeb/Clubs");
        const desc = trimToEmpty(item.areaText) || "Chưa cập nhật khu vực hoạt động";

        return [
            `<a class="list-card" href="${escapeHtml(href)}">`,
            item.coverUrl
                ? `<div class="list-card__media"><img src="${escapeHtml(item.coverUrl)}" alt="${escapeHtml(item.clubName || "Câu lạc bộ")}" loading="lazy"></div>`
                : '<div class="list-card__media"></div>',
            '<div class="list-card__body">',
            trimToEmpty(item.allowChallenge) || item.allowChallenge === true
                ? `<span class="list-card__badge">${item.allowChallenge ? "Khiêu chiến" : "Câu lạc bộ"}</span>`
                : '<span class="list-card__badge">Câu lạc bộ</span>',
            `<h2 class="list-card__title">${escapeHtml(trimToEmpty(item.clubName) || "CLB Hanaka Sport")}</h2>`,
            `<p class="list-card__desc">${escapeHtml(desc)}</p>`,
            `<div class="list-card__meta">${[
                metaChip(`${toNumber(item.membersCount)} thành viên`, "people-outline"),
                metaChip(`${formatDecimal(item.ratingAvg)} sao`, "star-outline"),
                metaChip(`${toNumber(item.matchesPlayed)} trận`, "tennisball-outline")
            ].join("")}</div>`,
            "</div>",
            "</a>"
        ].join("");
    }

    function renderCoachCard(item) {
        const href = buildSafeHref(`/PickleballWeb/Coach/${item.coachId}`, "/PickleballWeb/Coaches");

        return [
            `<a class="list-card" href="${escapeHtml(href)}">`,
            '<div class="list-card__body">',
            '<div class="identity">',
            avatarMarkup(item.fullName, item.avatarUrl),
            '<div class="identity__copy">',
            `<strong>${escapeHtml(trimToEmpty(item.fullName) || "Huấn luyện viên")}</strong>`,
            `<span>${escapeHtml(trimToEmpty(item.coachType) || "COACH")}</span>`,
            "</div>",
            "</div>",
            `<div class="list-card__meta">${[
                trimToEmpty(item.city) ? metaChip(item.city, "location-outline") : "",
                metaChip(`Single ${formatDecimal(item.levelSingle)}`),
                metaChip(`Double ${formatDecimal(item.levelDouble)}`),
                item.verified ? metaChip("Đã xác minh", "ribbon-outline") : ""
            ].filter(Boolean).join("")}</div>`,
            "</div>",
            "</a>"
        ].join("");
    }

    function renderRefereeCard(item) {
        const href = buildSafeHref(`/PickleballWeb/Referee/${item.refereeId}`, "/PickleballWeb/Referees");

        return [
            `<a class="list-card" href="${escapeHtml(href)}">`,
            '<div class="list-card__body">',
            '<div class="identity">',
            avatarMarkup(item.fullName, item.avatarUrl),
            '<div class="identity__copy">',
            `<strong>${escapeHtml(trimToEmpty(item.fullName) || "Trọng tài")}</strong>`,
            `<span>${escapeHtml(trimToEmpty(item.refereeType) || "REFEREE")}</span>`,
            "</div>",
            "</div>",
            `<div class="list-card__meta">${[
                trimToEmpty(item.city) ? metaChip(item.city, "location-outline") : "",
                metaChip(`Single ${formatDecimal(item.levelSingle)}`),
                metaChip(`Double ${formatDecimal(item.levelDouble)}`),
                item.verified ? metaChip("Đã xác minh", "shield-checkmark-outline") : ""
            ].filter(Boolean).join("")}</div>`,
            "</div>",
            "</a>"
        ].join("");
    }

    function renderCourtCard(item) {
        const href = buildSafeHref(`/PickleballWeb/Court/${item.courtId}`, "/PickleballWeb/Courts");
        const imageUrl = Array.isArray(item.images) && item.images.length > 0 ? item.images[0] : "";

        return [
            `<a class="list-card" href="${escapeHtml(href)}">`,
            imageUrl
                ? `<div class="list-card__media"><img src="${escapeHtml(imageUrl)}" alt="${escapeHtml(item.courtName || "Sân bãi")}" loading="lazy"></div>`
                : '<div class="list-card__media"></div>',
            '<div class="list-card__body">',
            '<span class="list-card__badge">Sân bãi</span>',
            `<h2 class="list-card__title">${escapeHtml(trimToEmpty(item.courtName) || "Sân Hanaka Sport")}</h2>`,
            `<p class="list-card__desc">${escapeHtml(trimToEmpty(item.areaText) || "Chưa cập nhật khu vực")}</p>`,
            `<div class="list-card__meta">${[
                trimToEmpty(item.managerName) ? metaChip(item.managerName, "person-outline") : "",
                trimToEmpty(item.phone) ? metaChip(item.phone, "call-outline") : "",
                metaChip(`${Array.isArray(item.images) ? item.images.length : 0} ảnh`, "images-outline")
            ].filter(Boolean).join("")}</div>`,
            "</div>",
            "</a>"
        ].join("");
    }

    function renderTournamentCard(item) {
        const href = buildSafeHref(`/PickleballWeb/Tournament/${item.tournamentId}`, "/PickleballWeb/Tournaments");
        const dateText = formatDateTime(item.startTime || item.createdAt);
        const desc = trimToEmpty(item.content) || "Giải đấu công khai đang mở thông tin trên hệ thống Hanaka Sport.";

        return [
            `<a class="list-card" href="${escapeHtml(href)}">`,
            item.bannerUrl
                ? `<div class="list-card__media"><img src="${escapeHtml(item.bannerUrl)}" alt="${escapeHtml(item.title || "Giải đấu")}" loading="lazy"></div>`
                : '<div class="list-card__media"></div>',
            '<div class="list-card__body">',
            `<span class="list-card__badge">${escapeHtml(statusText(item))}</span>`,
            `<h2 class="list-card__title">${escapeHtml(trimToEmpty(item.title) || "Giải đấu Hanaka")}</h2>`,
            `<p class="list-card__desc">${escapeHtml(desc)}</p>`,
            `<div class="list-card__meta">${[
                metaChip(dateText, "calendar-outline"),
                locationText(item) ? metaChip(locationText(item), "location-outline") : "",
                metaChip(`${toNumber(item.expectedTeams)} đội`, "people-outline"),
                metaChip(`${toNumber(item.matchesCount)} trận`, "tennisball-outline")
            ].filter(Boolean).join("")}</div>`,
            "</div>",
            "</a>"
        ].join("");
    }

    function renderExchangeCard(item) {
        const href = buildSafeHref(`/PickleballWeb/Exchange/${item.exchangeId}`, "/PickleballWeb/Exchanges");
        const leftForm = `${toNumber(item.leftW)}W · ${toNumber(item.leftD)}D · ${toNumber(item.leftL)}L`;
        const rightForm = trimToEmpty(item.agoText) || "Hanaka Sport";

        return [
            `<a class="list-card" href="${escapeHtml(href)}">`,
            '<div class="list-card__body">',
            '<span class="list-card__badge">Giao lưu CLB</span>',
            '<div class="scoreboard">',
            '<div class="scoreboard__club">',
            avatarMarkup(item.leftClubName, item.leftLogoUrl, "logo-chip"),
            `<strong>${escapeHtml(trimToEmpty(item.leftClubName) || "CLB A")}</strong>`,
            `<span>${escapeHtml(leftForm)}</span>`,
            "</div>",
            `<div class="scoreboard__score">${escapeHtml(trimToEmpty(item.scoreText) || "VS")}</div>`,
            '<div class="scoreboard__club">',
            avatarMarkup(item.rightClubName, item.rightLogoUrl, "logo-chip"),
            `<strong>${escapeHtml(trimToEmpty(item.rightClubName) || "CLB B")}</strong>`,
            `<span>${escapeHtml(rightForm)}</span>`,
            "</div>",
            "</div>",
            `<div class="list-card__meta">${[
                trimToEmpty(item.locationText) ? metaChip(item.locationText, "location-outline") : "",
                trimToEmpty(item.timeTextRaw) ? metaChip(item.timeTextRaw, "time-outline") : "",
                item.matchTime ? metaChip(formatDateTime(item.matchTime), "calendar-outline") : ""
            ].filter(Boolean).join("")}</div>`,
            "</div>",
            "</a>"
        ].join("");
    }

    function renderMatchCard(item) {
        const href = buildSafeHref(`/PickleballWeb/Match/${item.matchId}`, "/PickleballWeb/Matches");
        const matchTitle = trimToEmpty(item.tournamentTitle) || "Trận đấu Hanaka Sport";

        return [
            `<a class="list-card" href="${escapeHtml(href)}">`,
            item.tournamentBannerUrl
                ? `<div class="list-card__media"><img src="${escapeHtml(item.tournamentBannerUrl)}" alt="${escapeHtml(matchTitle)}" loading="lazy"></div>`
                : '<div class="list-card__media"></div>',
            '<div class="list-card__body">',
            `<span class="list-card__badge">${escapeHtml(trimToEmpty(item.roundLabel) || "Match video")}</span>`,
            `<h2 class="list-card__title">${escapeHtml(matchTitle)}</h2>`,
            `<p class="list-card__desc">${escapeHtml(`${trimToEmpty(item.team1Name) || "Đội 1"} vs ${trimToEmpty(item.team2Name) || "Đội 2"}`)}</p>`,
            `<div class="stats-grid list-card__stats"><div class="stat-box"><small>Số đội dự kiến</small><strong>${escapeHtml(String(toNumber(item.expectedTeams)))}</strong></div><div class="stat-box"><small>Đã đăng ký</small><strong>${escapeHtml(String(toNumber(item.registeredCount)))}</strong></div><div class="stat-box"><small>Đã ghép cặp</small><strong>${escapeHtml(String(toNumber(item.pairedCount)))}</strong></div><div class="stat-box"><small>Số trận</small><strong>${escapeHtml(String(toNumber(item.matchesCount)))}</strong></div></div>`,
            `<div class="list-card__meta">${[
                metaChip(`${toNumber(item.scoreTeam1)} - ${toNumber(item.scoreTeam2)}`, "stats-chart-outline"),
                trimToEmpty(item.groupName) ? metaChip(item.groupName, "albums-outline") : "",
                trimToEmpty(item.courtText) ? metaChip(item.courtText, "location-outline") : "",
                item.startAt ? metaChip(formatDateTime(item.startAt), "calendar-outline") : ""
            ].filter(Boolean).join("")}</div>`,
            "</div>",
            "</a>"
        ].join("");
    }

    function renderGuideCard(title, desc, href, icon) {
        const finalHref = buildSafeHref(href, "#");
        const attrs = isExternalHref(finalHref) ? ' target="_blank" rel="noreferrer"' : "";

        return [
            `<a class="list-card" href="${escapeHtml(finalHref)}"${attrs}>`,
            '<div class="list-card__body">',
            `<span class="list-card__badge">${escapeHtml(title)}</span>`,
            `<h2 class="list-card__title">${escapeHtml(title)}</h2>`,
            `<p class="list-card__desc">${escapeHtml(desc)}</p>`,
            `<div class="list-card__meta">${metaChip(finalHref.replace(/^https?:\/\//i, ""), icon)}</div>`,
            "</div>",
            "</a>"
        ].join("");
    }

    function renderRulesPage() {
        return [
            '<article class="detail-card">',
            '<span class="list-card__badge">Luật nhanh</span>',
            '<div class="stats-grid">',
            '<div class="stat-box"><small>Giao bóng</small><strong>Chéo sân, 1 lần giao</strong></div>',
            '<div class="stat-box"><small>Hai lần nảy</small><strong>Mỗi bên phải để bóng nảy 1 lần</strong></div>',
            '<div class="stat-box"><small>Kitchen</small><strong>Không volley trong vùng non-volley</strong></div>',
            '<div class="stat-box"><small>Tính điểm</small><strong>Thường 11 điểm và hơn 2 điểm</strong></div>',
            "</div>",
            "</article>",
            detailSection("Những điều cần nhớ", [
                '<div class="detail-list">',
                '<div class="detail-row"><strong>1. Bắt đầu pha bóng</strong><span>Người giao đứng sau vạch cuối sân và đưa bóng thấp dưới thắt lưng.</span></div>',
                '<div class="detail-row"><strong>2. Two-bounce rule</strong><span>Sau giao bóng, mỗi bên phải để bóng nảy một lần rồi mới được volley.</span></div>',
                '<div class="detail-row"><strong>3. Kitchen</strong><span>Không đập vô-lê khi chân đang chạm vùng cấm trước lưới.</span></div>',
                '<div class="detail-row"><strong>4. Xác định điểm</strong><span>Điểm số và thể thức chi tiết có thể khác theo từng giải, cần xem thêm thể lệ riêng.</span></div>',
                "</div>"
            ].join("")),
            detailSection("Đi tiếp trong app", [
                '<div class="actions-row">',
                '<a class="action-link" href="/PickleballWeb/Tournaments">Xem thể lệ từng giải</a>',
                '<a class="action-link action-link--soft" href="/PickleballWeb/Guide">Mở hướng dẫn</a>',
                "</div>"
            ].join(""))
        ].join("");
    }

    async function renderGuidePage() {
        let links = [];

        try {
            const payload = await fetchJson("/api/links");
            links = Array.isArray(payload?.items) ? payload.items : [];
        } catch (error) {
            links = [];
        }

        const builtIn = [
            {
                title: "Hỗ trợ",
                desc: "Liên hệ đội ngũ Hanaka Sport để được hỗ trợ nhanh.",
                href: "/support",
                icon: "mail-outline"
            },
            {
                title: "Chính sách",
                desc: "Xem chính sách quyền riêng tư và các hướng dẫn nền tảng.",
                href: "/policy/index",
                icon: "shield-outline"
            }
        ];

        const remoteCards = links.map(function (item) {
            const type = trimToEmpty(item.type).toLowerCase();
            const title = type === "youtube"
                ? "Video hướng dẫn"
                : type === "zalo"
                    ? "Nhóm cộng đồng"
                    : trimToEmpty(item.type) || "Liên kết";

            return {
                title: title,
                desc: `Mở nhanh liên kết ${trimToEmpty(item.type) || "công khai"} đang có trong hệ thống.`,
                href: item.link,
                icon: type === "youtube"
                    ? "logo-youtube"
                    : type === "zalo"
                        ? "chatbubble-ellipses-outline"
                        : "link-outline"
            };
        });

        return builtIn.concat(remoteCards).map(function (item) {
            return renderGuideCard(item.title, item.desc, item.href, item.icon);
        }).join("");
    }

    function formatMemberScore(value) {
        const number = Number(value);
        return Number.isFinite(number) ? number.toFixed(2) : "0.00";
    }

    function renderMembersAppItem(item) {
        const href = buildSafeHref(`/PickleballWeb/Member/${item.userId}`, "/PickleballWeb/Members");
        const fullName = trimToEmpty(item.fullName) || "Thành viên Hanaka";
        const city = trimToEmpty(item.city) || "-";
        const gender = trimToEmpty(item.gender) || "-";
        const verified = !!item.verified;

        const avatar = trimToEmpty(item.avatarUrl)
            ? [
                '<span class="members-app-item__avatar">',
                `<img src="${escapeHtml(item.avatarUrl)}" alt="${escapeHtml(fullName)}" loading="lazy">`,
                "</span>"
            ].join("")
            : [
                '<span class="members-app-item__avatar-fallback">',
                '<ion-icon name="person-circle-outline"></ion-icon>',
                "</span>"
            ].join("");

        return [
            `<a class="members-app-item" href="${escapeHtml(href)}">`,
            avatar,
            '<div class="members-app-item__mid">',
            `<h2 class="members-app-item__name">${escapeHtml(fullName)}</h2>`,
            '<div class="members-app-item__meta">',
            `<span>ID: ${escapeHtml(item.userId)}</span>`,
            `<span>${escapeHtml(city)}</span>`,
            "</div>",
            '<div class="members-app-item__sub">',
            `<span class="members-app-item__gender">${escapeHtml(gender)}</span>`,
            `<span class="members-app-item__verified ${verified ? "is-verified" : "is-unverified"}">${verified ? "Đã xác thực" : "Chưa xác thực"}</span>`,
            "</div>",
            "</div>",
            '<div class="members-app-item__right">',
            `<span class="members-app-item__score">${formatMemberScore(item.ratingSingle)}</span>`,
            `<span class="members-app-item__score">${formatMemberScore(item.ratingDouble)}</span>`,
            "</div>",
            "</a>"
        ].join("");
    }

    async function initMembersPage(root) {
        const form = qs("[data-members-search-form]", root);
        const input = qs("[data-members-search-input]", root);
        const list = qs("[data-members-list]", root);
        const errorBox = qs("[data-members-error]", root);
        const errorText = qs("[data-members-error-text]", root);
        const retryButton = qs("[data-members-retry]", root);
        const emptyState = qs("[data-members-empty]", root);
        const loadingState = qs("[data-members-loading]", root);
        const loadingMoreState = qs("[data-members-loading-more]", root);
        const sentinel = qs("[data-members-sentinel]", root);

        if (!form || !input || !list || !errorBox || !errorText || !retryButton || !emptyState || !loadingState || !loadingMoreState || !sentinel) {
            return;
        }

        const state = {
            query: "",
            page: 1,
            pageSize: 20,
            total: 0,
            items: [],
            loading: false,
            errorMessage: ""
        };

        function canLoadMore() {
            return state.items.length < state.total;
        }

        function render() {
            if (state.items.length > 0) {
                list.innerHTML = state.items.map(function (item) {
                    return renderMembersAppItem(item);
                }).join("");
            } else if (!state.loading) {
                list.innerHTML = "";
            }

            errorBox.hidden = !state.errorMessage;
            errorText.textContent = state.errorMessage;
            loadingState.hidden = !(state.loading && state.items.length === 0);
            loadingMoreState.hidden = !(state.loading && state.items.length > 0);
            emptyState.hidden = state.loading || !!state.errorMessage || state.items.length > 0;
            sentinel.hidden = state.loading || !canLoadMore();
        }

        async function fetchMembers(reset) {
            if (state.loading) {
                return;
            }

            if (!reset && !canLoadMore()) {
                return;
            }

            state.loading = true;
            if (reset) {
                state.errorMessage = "";
            }
            render();

            try {
                const nextPage = reset ? 1 : state.page;
                const payload = await fetchJson(`/api/users/members?page=${nextPage}&pageSize=${state.pageSize}&query=${encodeURIComponent(state.query)}`);
                const nextItems = Array.isArray(payload?.items) ? payload.items : [];

                state.total = Number(payload?.total) || 0;

                if (reset) {
                    state.items = nextItems;
                    state.page = 2;
                } else {
                    state.items = state.items.concat(nextItems);
                    state.page += 1;
                }
            } catch (error) {
                if (reset) {
                    state.items = [];
                    state.page = 1;
                    state.total = 0;
                    state.errorMessage = "Không tải được danh sách thành viên.";
                }
            } finally {
                state.loading = false;
                render();
            }
        }

        form.addEventListener("submit", function (event) {
            event.preventDefault();
            state.query = trimToEmpty(input.value);
            fetchMembers(true);
        });

        retryButton.addEventListener("click", function () {
            fetchMembers(true);
        });

        if ("IntersectionObserver" in window) {
            const observer = new IntersectionObserver(function (entries) {
                entries.forEach(function (entry) {
                    if (entry.isIntersecting && !state.loading && canLoadMore()) {
                        fetchMembers(false);
                    }
                });
            }, {
                rootMargin: "180px 0px"
            });

            observer.observe(sentinel);
        }

        await fetchMembers(true);
    }

    const listConfigs = {
        rules: {
            noun: "nguyên tắc",
            staticLoader: async function () {
                return {
                    total: 6,
                    html: renderRulesPage()
                };
            }
        },
        guide: {
            noun: "điểm chạm",
            staticLoader: async function () {
                const html = await renderGuidePage();
                const total = (html.match(/class="list-card"/g) || []).length;
                return { total: total, html: html };
            }
        },
        members: {
            noun: "thành viên",
            pageSize: 18,
            fetch: function (state) {
                return fetchJson(`/api/users/members?page=${state.page}&pageSize=${state.pageSize}&query=${encodeURIComponent(state.query)}`);
            },
            getItems: function (payload) { return Array.isArray(payload?.items) ? payload.items : []; },
            getTotal: function (payload) { return payload?.total ?? 0; },
            renderItem: renderMemberCard
        },
        clubs: {
            noun: "câu lạc bộ",
            pageSize: 14,
            defaultFilter: "all",
            filters: [
                { key: "all", label: "Tất cả" },
                { key: "challenging", label: "Khiêu chiến" }
            ],
            fetch: function (state) {
                const path = state.filter === "challenging" ? "/api/clubs/challenging" : "/api/clubs";
                return fetchJson(`${path}?page=${state.page}&pageSize=${state.pageSize}&keyword=${encodeURIComponent(state.query)}`);
            },
            getItems: function (payload) { return Array.isArray(payload?.items) ? payload.items : []; },
            getTotal: function (payload) { return payload?.total ?? 0; },
            renderItem: renderClubCard
        },
        coaches: {
            noun: "HL viên",
            pageSize: 18,
            fetch: function (state) {
                return fetchJson(`/api/coaches?page=${state.page}&pageSize=${state.pageSize}&query=${encodeURIComponent(state.query)}`);
            },
            getItems: function (payload) { return Array.isArray(payload?.items) ? payload.items : []; },
            getTotal: function (payload) { return payload?.total ?? 0; },
            renderItem: renderCoachCard
        },
        courts: {
            noun: "sân bãi",
            pageSize: 18,
            fetch: function (state) {
                return fetchJson(`/api/public/courts?page=${state.page}&pageSize=${state.pageSize}&query=${encodeURIComponent(state.query)}`);
            },
            getItems: function (payload) { return Array.isArray(payload?.items) ? payload.items : []; },
            getTotal: function (payload) { return payload?.total ?? 0; },
            renderItem: renderCourtCard
        },
        referees: {
            noun: "trọng tài",
            pageSize: 18,
            fetch: function (state) {
                return fetchJson(`/api/referees?page=${state.page}&pageSize=${state.pageSize}&query=${encodeURIComponent(state.query)}`);
            },
            getItems: function (payload) { return Array.isArray(payload?.items) ? payload.items : []; },
            getTotal: function (payload) { return payload?.total ?? 0; },
            renderItem: renderRefereeCard
        },
        tournaments: {
            noun: "giải đấu",
            pageSize: 14,
            fetch: function (state) {
                return fetchJson(`/api/public/tournaments?page=${state.page}&pageSize=${state.pageSize}&query=${encodeURIComponent(state.query)}`);
            },
            getItems: function (payload) { return Array.isArray(payload?.items) ? payload.items : []; },
            getTotal: function (payload) { return payload?.total ?? 0; },
            getHasMore: function (payload, state) { return Boolean(payload?.hasNextPage) || (state.page * state.pageSize < (payload?.total ?? 0)); },
            renderItem: renderTournamentCard
        },
        exchanges: {
            noun: "kèo giao lưu",
            pageSize: 14,
            fetch: function (state) {
                return fetchJson(`/api/public/exchanges?page=${state.page}&pageSize=${state.pageSize}&query=${encodeURIComponent(state.query)}`);
            },
            getItems: function (payload) { return Array.isArray(payload?.items) ? payload.items : []; },
            getTotal: function (payload) { return payload?.total ?? 0; },
            renderItem: renderExchangeCard
        },
        matches: {
            noun: "trận đấu",
            pageSize: 14,
            defaultFilter: "all",
            filters: [
                { key: "all", label: "Tất cả" },
                { key: "suggested", label: "Nổi bật" },
                { key: "live", label: "Hôm nay" }
            ],
            fetch: function (state) {
                return fetchJson(`/api/videos/videos?tab=${encodeURIComponent(state.filter)}&page=${state.page}&pageSize=${state.pageSize}`);
            },
            getItems: function (payload) { return Array.isArray(payload?.items) ? payload.items : []; },
            getTotal: function (payload) { return payload?.total ?? 0; },
            getHasMore: function (payload) { return Boolean(payload?.hasMore); },
            renderItem: renderMatchCard
        }
    };

    function renderFilterButtons(root, config, state, onChange) {
        const container = qs("[data-page-filters]", root);
        if (!container || !Array.isArray(config.filters) || config.filters.length === 0) {
            return;
        }

        container.hidden = false;
        container.innerHTML = config.filters.map(function (item) {
            const activeClass = item.key === state.filter ? " is-active" : "";
            return `<button class="page-filter${activeClass}" type="button" data-page-filter="${escapeHtml(item.key)}">${escapeHtml(item.label)}</button>`;
        }).join("");

        qsa("[data-page-filter]", container).forEach(function (button) {
            button.addEventListener("click", function () {
                const nextFilter = trimToEmpty(button.getAttribute("data-page-filter"));
                if (!nextFilter || nextFilter === state.filter) {
                    return;
                }

                state.filter = nextFilter;
                state.page = 1;
                onChange();
            });
        });
    }

    function renderPageEmpty(root, title, message) {
        const list = qs("[data-page-list]", root);
        if (!list) {
            return;
        }

        list.innerHTML = [
            '<article class="page-empty">',
            `<strong>${escapeHtml(title)}</strong>`,
            `<p>${escapeHtml(message)}</p>`,
            "</article>"
        ].join("");
    }

    async function initListPage(root) {
        const kind = trimToEmpty(root.getAttribute("data-page-kind"));
        if (!kind) {
            return;
        }

        const config = listConfigs[kind];
        if (!config) {
            return;
        }

        const totalTextNode = qs("[data-page-total-text]", root);
        const listNode = qs("[data-page-list]", root);
        const moreButton = qs("[data-page-more]", root);
        const searchForm = qs("[data-page-search-form]", root);
        const searchInput = qs("[data-page-search-input]", root);

        const state = {
            page: 1,
            pageSize: config.pageSize || 12,
            query: "",
            filter: config.defaultFilter || ""
        };

        if (config.staticLoader) {
            try {
                const result = await config.staticLoader();
                if (totalTextNode) {
                    totalTextNode.textContent = totalText(result.total, config.noun);
                }
                if (listNode) {
                    listNode.innerHTML = result.html;
                }
            } catch (error) {
                renderPageEmpty(root, "Không thể tải nội dung", "Vui lòng thử lại sau.");
            }
            return;
        }

        async function loadPage(reset) {
            if (!listNode) {
                return;
            }

            const pageToLoad = reset ? 1 : state.page;
            moreButton && (moreButton.disabled = true);

            try {
                const payload = await config.fetch({
                    page: pageToLoad,
                    pageSize: state.pageSize,
                    query: state.query,
                    filter: state.filter
                });

                const items = config.getItems(payload);
                const html = items.length > 0
                    ? items.map(config.renderItem).join("")
                    : "";

                if (totalTextNode) {
                    totalTextNode.textContent = totalText(config.getTotal(payload), config.noun);
                }

                if (reset || pageToLoad === 1) {
                    listNode.innerHTML = html || "";
                } else {
                    listNode.insertAdjacentHTML("beforeend", html);
                }

                if (!html) {
                    renderPageEmpty(root, "Chưa có dữ liệu phù hợp", "Hệ thống chưa có mục công khai khớp với điều kiện bạn đang tìm.");
                }

                const hasMore = typeof config.getHasMore === "function"
                    ? config.getHasMore(payload, { page: pageToLoad, pageSize: state.pageSize, query: state.query, filter: state.filter })
                    : (pageToLoad * state.pageSize < config.getTotal(payload));

                if (moreButton) {
                    moreButton.hidden = !hasMore;
                    moreButton.disabled = false;
                }

                state.page = pageToLoad;
                renderFilterButtons(root, config, state, function () {
                    loadPage(true);
                });
            } catch (error) {
                renderPageEmpty(root, "Không thể tải dữ liệu", "Vui lòng kiểm tra lại kết nối hoặc thử lại sau.");
                if (totalTextNode) {
                    totalTextNode.textContent = "Tải dữ liệu thất bại";
                }
                if (moreButton) {
                    moreButton.hidden = true;
                    moreButton.disabled = false;
                }
            }
        }

        if (searchForm && searchInput) {
            searchForm.addEventListener("submit", function (event) {
                event.preventDefault();
                state.query = trimToEmpty(searchInput.value);
                state.page = 1;
                loadPage(true);
            });
        }

        if (moreButton) {
            moreButton.addEventListener("click", function () {
                state.page += 1;
                loadPage(false);
            });
        }

        renderFilterButtons(root, config, state, function () {
            loadPage(true);
        });
        await loadPage(true);
    }

    function renderAchievements(items) {
        if (!Array.isArray(items) || items.length === 0) {
            return '<div class="detail-note">Chưa có thành tích được công khai.</div>';
        }

        return [
            '<div class="detail-list">',
            items.slice(0, 8).map(function (item) {
                const title = trimToEmpty(item.title || item.tournamentName || item.achievementLabel) || "Thành tích";
                const subtitle = trimToEmpty(item.note) || formatDate(item.date || item.achievedAt || item.createdAt);
                return `<div class="detail-row"><strong>${escapeHtml(title)}</strong><span>${escapeHtml(subtitle)}</span></div>`;
            }).join(""),
            "</div>"
        ].join("");
    }

    function renderRatingHistory(items) {
        if (!Array.isArray(items) || items.length === 0) {
            return '<div class="detail-note">Chưa có lịch sử điểm trình.</div>';
        }

        return [
            '<div class="detail-list">',
            items.slice(0, 8).map(function (item) {
                const label = `${formatDecimal(item.ratingSingle)} / ${formatDecimal(item.ratingDouble)}`;
                const subtitle = `${formatDate(item.ratedAt)} · ${trimToEmpty(item.ratedByName) || "Hệ thống"}`;
                return `<div class="detail-row"><strong>${escapeHtml(label)}</strong><span>${escapeHtml(subtitle)}</span></div>`;
            }).join(""),
            "</div>"
        ].join("");
    }

    function renderMemberDetail(data) {
        const detail = data.detail;

        return [
            '<article class="detail-card">',
            '<div class="identity">',
            avatarMarkup(detail.fullName, detail.avatarUrl),
            '<div class="identity__copy">',
            `<strong>${escapeHtml(trimToEmpty(detail.fullName) || "Thành viên Hanaka")}</strong>`,
            `<span>${escapeHtml(trimToEmpty(detail.city) || "Hanaka Sport")}</span>`,
            "</div>",
            "</div>",
            `<div class="detail-chip-row">${[
                detail.verified ? metaChip("Đã xác thực", "shield-checkmark-outline") : "",
                detail.gender ? metaChip(detail.gender, "person-outline") : "",
                detail.email ? metaChip(detail.email, "mail-outline") : "",
                detail.phone ? metaChip(detail.phone, "call-outline") : ""
            ].filter(Boolean).join("")}</div>`,
            '<div class="stats-grid">',
            `<div class="stat-box"><small>Single</small><strong>${formatDecimal(detail.ratingSingle)}</strong></div>`,
            `<div class="stat-box"><small>Double</small><strong>${formatDecimal(detail.ratingDouble)}</strong></div>`,
            `<div class="stat-box"><small>Ngày tham gia</small><strong>${escapeHtml(formatDate(detail.createdAt))}</strong></div>`,
            `<div class="stat-box"><small>Rating cập nhật</small><strong>${escapeHtml(formatDate(detail.ratingUpdatedAt))}</strong></div>`,
            "</div>",
            trimToEmpty(detail.bio) ? `<div class="detail-note">${escapeHtml(detail.bio)}</div>` : "",
            "</article>",
            detailSection("Thành tích", renderAchievements(data.achievements)),
            detailSection("Lịch sử điểm trình", renderRatingHistory(data.ratingHistory))
        ].join("");
    }

    function renderCoachLikeDetail(item, roleLabel, areaLabel) {
        return [
            '<article class="detail-card">',
            '<div class="identity">',
            avatarMarkup(item.fullName, item.avatarUrl),
            '<div class="identity__copy">',
            `<strong>${escapeHtml(trimToEmpty(item.fullName) || roleLabel)}</strong>`,
            `<span>${escapeHtml(trimToEmpty(item.city) || "Hanaka Sport")}</span>`,
            "</div>",
            "</div>",
            `<div class="detail-chip-row">${[
                item.verified ? metaChip("Đã xác minh", "ribbon-outline") : "",
                trimToEmpty(item.gender) ? metaChip(item.gender, "person-outline") : "",
                trimToEmpty(item.email) ? metaChip(item.email, "mail-outline") : "",
                trimToEmpty(item.phone) ? metaChip(item.phone, "call-outline") : ""
            ].filter(Boolean).join("")}</div>`,
            '<div class="stats-grid">',
            `<div class="stat-box"><small>Single</small><strong>${formatDecimal(item.levelSingle)}</strong></div>`,
            `<div class="stat-box"><small>Double</small><strong>${formatDecimal(item.levelDouble)}</strong></div>`,
            `<div class="stat-box"><small>${escapeHtml(areaLabel)}</small><strong>${escapeHtml(trimToEmpty(item.teachingArea || item.workingArea) || "Chưa cập nhật")}</strong></div>`,
            `<div class="stat-box"><small>Rating cập nhật</small><strong>${escapeHtml(formatDate(item.ratingUpdatedAt))}</strong></div>`,
            "</div>",
            trimToEmpty(item.introduction || item.bio)
                ? `<div class="detail-note">${escapeHtml(trimToEmpty(item.introduction || item.bio))}</div>`
                : "",
            "</article>",
            detailSection("Thành tích", renderAchievements(item.userAchievements)),
            detailSection("Lịch sử điểm trình", renderRatingHistory(item.ratingHistory)),
            trimToEmpty(item.achievements)
                ? detailSection("Ghi chú chuyên môn", `<div class="detail-note">${escapeHtml(item.achievements)}</div>`)
                : ""
        ].join("");
    }

    function renderCoachNativeSection(key, title, bodyHtml, isOpen) {
        return [
            `<section class="coach-native-section ${isOpen ? "is-open" : ""}" data-coach-section="${escapeHtml(key)}">`,
            `<button class="coach-native-section__header" type="button" data-coach-section-toggle="${escapeHtml(key)}">`,
            `<span>${escapeHtml(title)}</span>`,
            `<ion-icon name="${isOpen ? "chevron-up" : "chevron-forward"}"></ion-icon>`,
            "</button>",
            `<div class="coach-native-section__body" ${isOpen ? "" : "hidden"}>`,
            bodyHtml,
            "</div>",
            "</section>"
        ].join("");
    }

    function renderCoachRatingHistory(items) {
        if (!Array.isArray(items) || items.length === 0) {
            return '<div class="coach-native-empty">Chưa có lịch sử điểm trình</div>';
        }

        return [
            '<div class="coach-native-history-list">',
            items.map(function (item) {
                return [
                    '<article class="coach-native-history-item">',
                    `<h3>${escapeHtml(formatDateTimeOrDash(item?.ratedAt))}</h3>`,
                    '<div class="coach-native-history-item__scores">',
                    '<div class="coach-native-history-item__score">',
                    '<span>Điểm đơn</span>',
                    `<strong>${escapeHtml(item?.ratingSingle != null ? Number(item.ratingSingle).toFixed(2) : "0.00")}</strong>`,
                    "</div>",
                    '<div class="coach-native-history-item__score">',
                    '<span>Điểm đôi</span>',
                    `<strong>${escapeHtml(item?.ratingDouble != null ? Number(item.ratingDouble).toFixed(2) : "0.00")}</strong>`,
                    "</div>",
                    "</div>",
                    `<p>Người chấm: <strong>${escapeHtml(trimToEmpty(item?.ratedByName) || "Hệ thống")}</strong></p>`,
                    `<p>Ghi chú: <strong>${escapeHtml(trimToEmpty(item?.note) || "—")}</strong></p>`,
                    "</article>"
                ].join("");
            }).join(""),
            "</div>"
        ].join("");
    }

    function renderCoachUserAchievements(items) {
        if (!Array.isArray(items) || items.length === 0) {
            return '<div class="coach-native-empty">Chưa có thành tích thi đấu</div>';
        }

        return [
            '<div class="coach-native-achievement-list">',
            items.map(function (item) {
                const title = trimToEmpty(item?.title) || trimToEmpty(item?.tournamentName) || trimToEmpty(item?.tournament?.title) || "Thành tích";
                const dateValue = item?.date || item?.achievedAt || item?.createdAt || item?.tournament?.startTime;
                const canOpenTournament = Number(item?.tournamentId) > 0;
                const tagName = canOpenTournament ? "a" : "article";
                const href = canOpenTournament ? buildSafeHref(`/PickleballWeb/Tournament/${item.tournamentId}`, "#") : "";

                return [
                    `<${tagName} class="coach-native-achievement-card" ${canOpenTournament ? `href="${escapeHtml(href)}"` : ""}>`,
                    '<div class="coach-native-achievement-card__icon">',
                    `<ion-icon name="${escapeHtml(achievementIcon(item))}" style="color:${escapeHtml(achievementColor(item))}"></ion-icon>`,
                    "</div>",
                    '<div class="coach-native-achievement-card__content">',
                    `<h3>${escapeHtml(title)}</h3>`,
                    `<p class="coach-native-achievement-card__label">${escapeHtml(achievementLabel(item))}</p>`,
                    `<p class="coach-native-achievement-card__date">${escapeHtml(formatDateOrDash(dateValue))}</p>`,
                    "</div>",
                    `<ion-icon class="coach-native-achievement-card__arrow" name="${canOpenTournament ? "chevron-forward" : "trophy-outline"}"></ion-icon>`,
                    `</${tagName}>`
                ].join("");
            }).join(""),
            "</div>"
        ].join("");
    }

    function renderCoachDetail(item) {
        const introduction = htmlToPlainText(item?.introduction, "Chưa cập nhật");
        const teachingArea = htmlToPlainText(item?.teachingArea, "Chưa cập nhật");
        const achievements = htmlToPlainText(item?.achievements, "Chưa cập nhật");

        return [
            '<div class="coach-native-detail" data-coach-native-detail>',
            '<article class="coach-native-card">',
            '<div class="coach-native-card__profile">',
            trimToEmpty(item?.avatarUrl)
                ? `<img class="coach-native-card__avatar" src="${escapeHtml(normalizeMediaUrl(item.avatarUrl))}" alt="${escapeHtml(trimToEmpty(item?.fullName) || "Huấn luyện viên")}" loading="lazy">`
                : '<ion-icon class="coach-native-card__avatar-fallback" name="person-circle-outline"></ion-icon>',
            `<h2>${escapeHtml(trimToEmpty(item?.fullName) || "—")}</h2>`,
            `<p class="coach-native-card__verify ${item?.verified ? "is-verified" : "is-unverified"}">${escapeHtml(item?.verified ? "Hồ sơ HLV đã xác thực" : "Hồ sơ HLV chưa xác thực")}</p>`,
            `<p class="coach-native-card__user-verify ${item?.userVerified ? "is-verified" : "is-unverified"}">${escapeHtml(item?.userVerified ? "Tài khoản người dùng đã xác thực" : "Tài khoản người dùng chưa xác thực")}</p>`,
            "</div>",
            '<div class="coach-native-card__scorebox">',
            '<div class="coach-native-card__scorecol">',
            "<span>Điểm đơn</span>",
            `<strong>${escapeHtml(formatScore(item?.levelSingle))}</strong>`,
            "</div>",
            '<div class="coach-native-card__divider"></div>',
            '<div class="coach-native-card__scorecol">',
            "<span>Điểm đôi</span>",
            `<strong>${escapeHtml(formatScore(item?.levelDouble))}</strong>`,
            "</div>",
            "</div>",
            `<p class="coach-native-card__updated">Cập nhật điểm gần nhất: ${escapeHtml(formatDateTimeOrDash(item?.ratingUpdatedAt))}</p>`,
            '<div class="coach-native-card__fields">',
            `<div class="coach-native-card__field"><span>Giới tính</span><strong>${escapeHtml(trimToEmpty(item?.gender) || "—")}</strong></div>`,
            `<div class="coach-native-card__field"><span>Tỉnh/Thành</span><strong>${escapeHtml(trimToEmpty(item?.city) || "—")}</strong></div>`,
            `<div class="coach-native-card__field"><span>Email</span><strong>${escapeHtml(trimToEmpty(item?.email) || "—")}</strong></div>`,
            `<div class="coach-native-card__field"><span>Số điện thoại</span><strong>${escapeHtml(trimToEmpty(item?.phone) || "—")}</strong></div>`,
            `<div class="coach-native-card__field"><span>Ngày sinh</span><strong>${escapeHtml(formatDateOrDash(item?.birthOfDate))}</strong></div>`,
            `<div class="coach-native-card__field"><span>Giới thiệu cá nhân</span><p>${escapeHtml(trimToEmpty(item?.bio) || "—")}</p></div>`,
            "</div>",
            "</article>",
            renderCoachNativeSection("intro", "Giới thiệu giảng dạy", `<div class="coach-native-section__content"><p>${escapeHtml(introduction)}</p></div>`, true),
            renderCoachNativeSection("teaching-area", "Khu vực giảng dạy", `<div class="coach-native-section__content"><p>${escapeHtml(teachingArea)}</p></div>`, true),
            renderCoachNativeSection("coach-achievements", "Thành tích / chứng chỉ huấn luyện", `<div class="coach-native-section__content"><p>${escapeHtml(achievements)}</p></div>`, true),
            renderCoachNativeSection("rating-history", "Lịch sử điểm trình", renderCoachRatingHistory(item?.ratingHistory), false),
            renderCoachNativeSection("user-achievements", "Thành tích thi đấu", renderCoachUserAchievements(item?.userAchievements), true),
            "</div>"
        ].join("");
    }

    function renderRefereeDetail(item) {
        const introduction = htmlToPlainText(item?.introduction, "Ch\u01b0a c\u1eadp nh\u1eadt");
        const workingArea = htmlToPlainText(item?.workingArea, "Ch\u01b0a c\u1eadp nh\u1eadt");
        const achievements = htmlToPlainText(item?.achievements, "Ch\u01b0a c\u1eadp nh\u1eadt");

        return [
            '<div class="coach-native-detail" data-coach-native-detail>',
            '<article class="coach-native-card">',
            '<div class="coach-native-card__profile">',
            trimToEmpty(item?.avatarUrl)
                ? `<img class="coach-native-card__avatar" src="${escapeHtml(normalizeMediaUrl(item.avatarUrl))}" alt="${escapeHtml(trimToEmpty(item?.fullName) || "Tr\u1ecdng t\u00e0i")}" loading="lazy">`
                : '<ion-icon class="coach-native-card__avatar-fallback" name="person-circle-outline"></ion-icon>',
            `<h2>${escapeHtml(trimToEmpty(item?.fullName) || "\u2014")}</h2>`,
            `<p class="coach-native-card__verify ${item?.verified ? "is-verified" : "is-unverified"}">${escapeHtml(item?.verified ? "H\u1ed3 s\u01a1 tr\u1ecdng t\u00e0i \u0111\u00e3 x\u00e1c th\u1ef1c" : "H\u1ed3 s\u01a1 tr\u1ecdng t\u00e0i ch\u01b0a x\u00e1c th\u1ef1c")}</p>`,
            `<p class="coach-native-card__user-verify ${item?.userVerified ? "is-verified" : "is-unverified"}">${escapeHtml(item?.userVerified ? "T\u00e0i kho\u1ea3n ng\u01b0\u1eddi d\u00f9ng \u0111\u00e3 x\u00e1c th\u1ef1c" : "T\u00e0i kho\u1ea3n ng\u01b0\u1eddi d\u00f9ng ch\u01b0a x\u00e1c th\u1ef1c")}</p>`,
            "</div>",
            '<div class="coach-native-card__scorebox">',
            '<div class="coach-native-card__scorecol">',
            "<span>\u0110i\u1ec3m \u0111\u01a1n</span>",
            `<strong>${escapeHtml(formatScore(item?.levelSingle))}</strong>`,
            "</div>",
            '<div class="coach-native-card__divider"></div>',
            '<div class="coach-native-card__scorecol">',
            "<span>\u0110i\u1ec3m \u0111\u00f4i</span>",
            `<strong>${escapeHtml(formatScore(item?.levelDouble))}</strong>`,
            "</div>",
            "</div>",
            `<p class="coach-native-card__updated">C\u1eadp nh\u1eadt \u0111i\u1ec3m g\u1ea7n nh\u1ea5t: ${escapeHtml(formatDateTimeOrDash(item?.ratingUpdatedAt))}</p>`,
            '<div class="coach-native-card__fields">',
            `<div class="coach-native-card__field"><span>Gi\u1edbi t\u00ednh</span><strong>${escapeHtml(trimToEmpty(item?.gender) || "\u2014")}</strong></div>`,
            `<div class="coach-native-card__field"><span>T\u1ec9nh/Th\u00e0nh</span><strong>${escapeHtml(trimToEmpty(item?.city) || "\u2014")}</strong></div>`,
            `<div class="coach-native-card__field"><span>Email</span><strong>${escapeHtml(trimToEmpty(item?.email) || "\u2014")}</strong></div>`,
            `<div class="coach-native-card__field"><span>S\u1ed1 \u0111i\u1ec7n tho\u1ea1i</span><strong>${escapeHtml(trimToEmpty(item?.phone) || "\u2014")}</strong></div>`,
            `<div class="coach-native-card__field"><span>Ng\u00e0y sinh</span><strong>${escapeHtml(formatDateOrDash(item?.birthOfDate))}</strong></div>`,
            `<div class="coach-native-card__field"><span>Gi\u1edbi thi\u1ec7u c\u00e1 nh\u00e2n</span><p>${escapeHtml(trimToEmpty(item?.bio) || "\u2014")}</p></div>`,
            "</div>",
            "</article>",
            renderCoachNativeSection("intro", "Gi\u1edbi thi\u1ec7u", `<div class="coach-native-section__content"><p>${escapeHtml(introduction)}</p></div>`, true),
            renderCoachNativeSection("working-area", "Khu v\u1ef1c l\u00e0m vi\u1ec7c", `<div class="coach-native-section__content"><p>${escapeHtml(workingArea)}</p></div>`, true),
            renderCoachNativeSection("referee-achievements", "Th\u00e0nh t\u00edch / ch\u1ee9ng ch\u1ec9 tr\u1ecdng t\u00e0i", `<div class="coach-native-section__content"><p>${escapeHtml(achievements)}</p></div>`, true),
            renderCoachNativeSection("rating-history", "L\u1ecbch s\u1eed \u0111i\u1ec3m tr\u00ecnh", renderCoachRatingHistory(item?.ratingHistory), false),
            renderCoachNativeSection("user-achievements", "Th\u00e0nh t\u00edch thi \u0111\u1ea5u", renderCoachUserAchievements(item?.userAchievements), true),
            "</div>"
        ].join("");
    }

    function renderClubDetail(data) {
        const detail = data.detail;
        const owner = detail.owner;
        const members = Array.isArray(data.members) ? data.members : [];

        return [
            '<article class="detail-card">',
            trimToEmpty(detail.coverUrl)
                ? `<div class="list-card__media"><img src="${escapeHtml(detail.coverUrl)}" alt="${escapeHtml(detail.clubName || "Câu lạc bộ")}" loading="lazy"></div>`
                : "",
            '<div class="identity">',
            avatarMarkup(detail.clubName, detail.coverUrl, "logo-chip"),
            '<div class="identity__copy">',
            `<strong>${escapeHtml(trimToEmpty(detail.clubName) || "CLB Hanaka")}</strong>`,
            `<span>${escapeHtml(trimToEmpty(detail.areaText) || "Chưa cập nhật khu vực")}</span>`,
            "</div>",
            "</div>",
            `<div class="detail-chip-row">${[
                detail.allowChallenge ? metaChip("Đang bật khiêu chiến", "flash-outline") : metaChip("CLB thường", "shield-outline"),
                metaChip(`${toNumber(detail.membersCount)} thành viên`, "people-outline"),
                owner?.fullName ? metaChip(`Owner ${owner.fullName}`) : ""
            ].filter(Boolean).join("")}</div>`,
            '<div class="stats-grid">',
            `<div class="stat-box"><small>Rating CLB</small><strong>${formatDecimal(detail.ratingAvg)}</strong></div>`,
            `<div class="stat-box"><small>Reviews</small><strong>${toNumber(detail.reviewsCount)}</strong></div>`,
            `<div class="stat-box"><small>Thắng / Hòa / Thua</small><strong>${toNumber(detail.matchesWin)} / ${toNumber(detail.matchesDraw)} / ${toNumber(detail.matchesLoss)}</strong></div>`,
            `<div class="stat-box"><small>Ngày tạo</small><strong>${escapeHtml(formatDate(detail.createdAt))}</strong></div>`,
            "</div>",
            detail.overview?.introduction
                ? `<div class="detail-note">${escapeHtml(detail.overview.introduction)}</div>`
                : "",
            "</article>",
            owner ? detailSection("Chủ nhiệm CLB", [
                '<div class="identity">',
                avatarMarkup(owner.fullName, owner.avatarUrl),
                '<div class="identity__copy">',
                `<strong>${escapeHtml(owner.fullName)}</strong>`,
                `<span>${escapeHtml(`Single ${formatDecimal(owner.ratingSingle)} · Double ${formatDecimal(owner.ratingDouble)}`)}</span>`,
                "</div>",
                "</div>"
            ].join("")) : "",
            detailSection("Thành viên nổi bật", members.length > 0
                ? [
                    '<div class="detail-list">',
                    members.slice(0, 8).map(function (member) {
                        return [
                            '<div class="detail-row">',
                            `<strong>${escapeHtml(trimToEmpty(member.fullName) || "Thành viên")}</strong>`,
                            `<span>${escapeHtml(`${trimToEmpty(member.memberRole) || "MEMBER"} · ${formatDecimal(member.ratingDouble || member.ratingSingle)}`)}</span>`,
                            "</div>"
                        ].join("");
                    }).join(""),
                    "</div>"
                ].join("")
                : '<div class="detail-note">Chưa có thành viên công khai.</div>')
        ].join("");
    }

    function renderCourtDetail(item) {
        const images = Array.isArray(item.images) ? item.images : [];

        return [
            '<article class="detail-card">',
            images.length > 0
                ? `<div class="list-card__media"><img src="${escapeHtml(images[0])}" alt="${escapeHtml(item.courtName || "Sân bãi")}" loading="lazy"></div>`
                : "",
            '<div class="identity">',
            avatarMarkup(item.courtName, images[0] || "", "logo-chip"),
            '<div class="identity__copy">',
            `<strong>${escapeHtml(trimToEmpty(item.courtName) || "Sân Hanaka")}</strong>`,
            `<span>${escapeHtml(trimToEmpty(item.areaText) || "Chưa cập nhật khu vực")}</span>`,
            "</div>",
            "</div>",
            `<div class="detail-chip-row">${[
                trimToEmpty(item.managerName) ? metaChip(item.managerName, "person-outline") : "",
                trimToEmpty(item.phone) ? metaChip(item.phone, "call-outline") : "",
                metaChip(`${images.length} ảnh`, "images-outline")
            ].filter(Boolean).join("")}</div>`,
            "</article>",
            detailSection("Bộ ảnh sân", images.length > 0
                ? `<div class="detail-list">${images.map(function (url, index) {
                    return `<div class="detail-row"><strong>Ảnh ${index + 1}</strong><span>${escapeHtml(url)}</span></div>`;
                }).join("")}</div>`
                : '<div class="detail-note">Sân này chưa có ảnh công khai.</div>'),
            detailSection("Liên hệ", [
                '<div class="actions-row">',
                trimToEmpty(item.phone)
                    ? `<a class="action-link" href="${escapeHtml(buildSafeHref(`tel:${item.phone}`, "#"))}">Gọi quản lý sân</a>`
                    : "",
                '<a class="action-link action-link--soft" href="/PickleballWeb/Courts">Quay về danh sách sân</a>',
                "</div>"
            ].join(""))
        ].join("");
    }

    function renderTournamentMatches(rounds) {
        if (!Array.isArray(rounds) || rounds.length === 0) {
            return '<div class="detail-note">Chưa có dữ liệu vòng đấu công khai.</div>';
        }

        const rows = [];

        rounds.slice(0, 6).forEach(function (round) {
            const groups = Array.isArray(round.groups) ? round.groups : [];
            groups.forEach(function (group) {
                const matches = Array.isArray(group.matches) ? group.matches : [];
                matches.slice(0, 3).forEach(function (match) {
                    rows.push([
                        '<div class="detail-row">',
                        `<strong>${escapeHtml(`${trimToEmpty(round.roundLabel) || "Round"} · ${trimToEmpty(group.groupName) || "Group"}`)}</strong>`,
                        `<span>${escapeHtml(`${trimToEmpty(match.team1?.displayName) || "Đội 1"} ${toNumber(match.scoreTeam1)} - ${toNumber(match.scoreTeam2)} ${trimToEmpty(match.team2?.displayName) || "Đội 2"}`)}</span>`,
                        "</div>"
                    ].join(""));
                });
            });
        });

        if (rows.length === 0) {
            return '<div class="detail-note">Vòng đấu đã tạo nhưng chưa có trận công khai.</div>';
        }

        return `<div class="detail-list">${rows.join("")}</div>`;
    }

    function renderTournamentDetail(data) {
        const detail = data.detail;
        const regs = data.registrations || {};
        const ruleHtml = trimToEmpty(data.rule?.tournamentRule);
        const rounds = Array.isArray(data.rounds?.rounds) ? data.rounds.rounds : [];

        return [
            '<article class="detail-card">',
            trimToEmpty(detail.bannerUrl)
                ? `<div class="list-card__media"><img src="${escapeHtml(detail.bannerUrl)}" alt="${escapeHtml(detail.title || "Giải đấu")}" loading="lazy"></div>`
                : "",
            `<span class="list-card__badge">${escapeHtml(statusText(detail))}</span>`,
            `<h2 class="list-card__title">${escapeHtml(trimToEmpty(detail.title) || "Giải đấu Hanaka")}</h2>`,
            `<p class="list-card__desc">${escapeHtml(trimToEmpty(detail.content) || "Giải đấu đang được công khai trên hệ thống Hanaka Sport.")}</p>`,
            '<div class="stats-grid">',
            `<div class="stat-box"><small>Thời gian</small><strong>${escapeHtml(formatDateTime(detail.startTime))}</strong></div>`,
            `<div class="stat-box"><small>Hạn đăng ký</small><strong>${escapeHtml(formatDateTime(detail.registerDeadline))}</strong></div>`,
            `<div class="stat-box"><small>Đội dự kiến</small><strong>${toNumber(detail.expectedTeams)}</strong></div>`,
            `<div class="stat-box"><small>Số trận</small><strong>${toNumber(detail.matchesCount)}</strong></div>`,
            "</div>",
            `<div class="detail-chip-row">${[
                locationText(detail) ? metaChip(locationText(detail), "location-outline") : "",
                trimToEmpty(detail.organizer) ? metaChip(detail.organizer, "business-outline") : "",
                trimToEmpty(detail.gameType) ? metaChip(detail.gameType, "tennisball-outline") : "",
                trimToEmpty(detail.formatText) ? metaChip(detail.formatText, "layers-outline") : ""
            ].filter(Boolean).join("")}</div>`,
            "</article>",
            detailSection("Đăng ký công khai", [
                '<div class="stats-grid">',
                `<div class="stat-box"><small>Đăng ký thành công</small><strong>${toNumber(regs.counts?.success)}</strong></div>`,
                `<div class="stat-box"><small>Đang chờ ghép cặp</small><strong>${toNumber(regs.counts?.waiting)}</strong></div>`,
                `<div class="stat-box"><small>Còn chỗ</small><strong>${toNumber(regs.counts?.capacityLeft)}</strong></div>`,
                `<div class="stat-box"><small>Số round</small><strong>${rounds.length}</strong></div>`,
                "</div>"
            ].join("")),
            detailSection("Lịch trận", renderTournamentMatches(rounds)),
            detailSection("Thể lệ", ruleHtml
                ? `<div class="page-richtext">${ruleHtml}</div>`
                : '<div class="detail-note">Giải đấu này chưa công khai thể lệ chi tiết.</div>')
        ].join("");
    }

    function renderExchangeDetail(item) {
        return [
            '<article class="detail-card">',
            '<span class="list-card__badge">Giao lưu CLB</span>',
            '<div class="scoreboard">',
            '<div class="scoreboard__club">',
            avatarMarkup(item.leftClubName, item.leftLogoUrl, "logo-chip"),
            `<strong>${escapeHtml(trimToEmpty(item.leftClubName) || "CLB A")}</strong>`,
            `<span>${escapeHtml(`${toNumber(item.leftW)}W · ${toNumber(item.leftD)}D · ${toNumber(item.leftL)}L`)}</span>`,
            "</div>",
            `<div class="scoreboard__score">${escapeHtml(trimToEmpty(item.scoreText) || "VS")}</div>`,
            '<div class="scoreboard__club">',
            avatarMarkup(item.rightClubName, item.rightLogoUrl, "logo-chip"),
            `<strong>${escapeHtml(trimToEmpty(item.rightClubName) || "CLB B")}</strong>`,
            `<span>${escapeHtml(trimToEmpty(item.agoText) || "Hanaka Sport")}</span>`,
            "</div>",
            "</div>",
            `<div class="detail-chip-row">${[
                trimToEmpty(item.locationText) ? metaChip(item.locationText, "location-outline") : "",
                trimToEmpty(item.timeTextRaw) ? metaChip(item.timeTextRaw, "time-outline") : "",
                item.matchTime ? metaChip(formatDateTime(item.matchTime), "calendar-outline") : ""
            ].filter(Boolean).join("")}</div>`,
            "</article>"
        ].join("");
    }

    function renderMatchDetail(payload) {
        const tournament = payload.tournament || {};
        const match = payload.match || {};
        const round = payload.round || {};
        const group = payload.group || {};
        const videoHref = buildSafeHref(match.videoUrl, "");

        return [
            '<article class="detail-card">',
            `<span class="list-card__badge">${escapeHtml(trimToEmpty(round.roundLabel) || "Match center")}</span>`,
            `<h2 class="list-card__title">${escapeHtml(trimToEmpty(tournament.title) || "Trận đấu Hanaka")}</h2>`,
            '<div class="scoreboard">',
            '<div class="scoreboard__club">',
            avatarMarkup(match.team1?.displayName, match.team1?.player1?.avatar, "logo-chip"),
            `<strong>${escapeHtml(trimToEmpty(match.team1?.displayName) || "Đội 1")}</strong>`,
            `<span>${escapeHtml(trimToEmpty(match.team1?.player1?.name) || "")}</span>`,
            "</div>",
            `<div class="scoreboard__score">${escapeHtml(`${toNumber(match.scoreTeam1)} - ${toNumber(match.scoreTeam2)}`)}</div>`,
            '<div class="scoreboard__club">',
            avatarMarkup(match.team2?.displayName, match.team2?.player1?.avatar, "logo-chip"),
            `<strong>${escapeHtml(trimToEmpty(match.team2?.displayName) || "Đội 2")}</strong>`,
            `<span>${escapeHtml(trimToEmpty(match.team2?.player1?.name) || "")}</span>`,
            "</div>",
            "</div>",
            `<div class="detail-chip-row">${[
                trimToEmpty(group.groupName) ? metaChip(group.groupName, "albums-outline") : "",
                trimToEmpty(match.courtText) ? metaChip(match.courtText, "location-outline") : "",
                trimToEmpty(match.addressText) ? metaChip(match.addressText, "map-outline") : "",
                match.startAt ? metaChip(formatDateTime(match.startAt), "calendar-outline") : ""
            ].filter(Boolean).join("")}</div>`,
            videoHref
                ? `<div class="actions-row"><a class="action-link" href="${escapeHtml(videoHref)}" target="_blank" rel="noreferrer">Mở video trận đấu</a></div>`
                : "",
            "</article>",
            detailSection("Thông tin giải đấu", [
                '<div class="detail-list">',
                `<div class="detail-row"><strong>Giải đấu</strong><span>${escapeHtml(trimToEmpty(tournament.title) || "Hanaka Sport")}</span></div>`,
                `<div class="detail-row"><strong>Trạng thái</strong><span>${escapeHtml(statusText(tournament))}</span></div>`,
                `<div class="detail-row"><strong>Địa điểm</strong><span>${escapeHtml(locationText(tournament) || "Chưa cập nhật")}</span></div>`,
                "</div>"
            ].join(""))
        ].join("");
    }

    function pad2(value) {
        return String(value).padStart(2, "0");
    }

    function formatSlashDate(value) {
        const date = parseDate(value);
        if (!date) {
            return "-";
        }

        return `${pad2(date.getDate())}/${pad2(date.getMonth() + 1)}/${date.getFullYear()}`;
    }

    function formatSlashDateTime(value) {
        const date = parseDate(value);
        if (!date) {
            return "-";
        }

        return `${formatSlashDate(date)} ${pad2(date.getHours())}:${pad2(date.getMinutes())}`;
    }

    function formatClock(value) {
        const date = parseDate(value);
        if (!date) {
            return "--:--";
        }

        return `${pad2(date.getHours())}:${pad2(date.getMinutes())}`;
    }

    function formatFlexibleNumber(value) {
        const number = Number(value);
        if (!Number.isFinite(number)) {
            return "-";
        }

        if (Math.abs(number - Math.trunc(number)) < 0.000001) {
            return String(Math.trunc(number));
        }

        return number.toFixed(2).replace(/\.?0+$/, "");
    }

    function normalizeRichHtml(value) {
        const html = trimToEmpty(value);
        if (!html || html === "<p><br></p>") {
            return "";
        }

        return html;
    }

    function tournamentGameTypeLabel(value) {
        const type = trimToEmpty(value).toUpperCase();

        if (type === "DOUBLE") {
            return "\u0110\u00f4i";
        }

        if (type === "SINGLE") {
            return "\u0110\u01a1n";
        }

        if (type === "MIXED") {
            return "\u0110\u00f4i h\u1ed7n h\u1ee3p";
        }

        return trimToEmpty(value) || "-";
    }

    function formatSignedNumber(value) {
        const number = Number(value);
        if (!Number.isFinite(number)) {
            return "0";
        }

        if (number > 0) {
            return `+${number}`;
        }

        return String(number);
    }

    function buildTournamentTeamName(item) {
        const names = [
            trimToEmpty(item?.player1?.name),
            trimToEmpty(item?.player2?.name)
        ].filter(Boolean);

        if (names.length > 0) {
            return names.join(" / ");
        }

        return `\u0110\u0103ng k\u00fd #${toNumber(item?.regIndex)}`;
    }

    function buildTournamentRegistrationMeta(item) {
        const parts = [];

        if (trimToEmpty(item?.regCode)) {
            parts.push(trimToEmpty(item.regCode));
        }

        if (item?.regTime) {
            parts.push(formatSlashDateTime(item.regTime));
        }

        parts.push(
            item?.success
                ? "\u0110\u00e3 gh\u00e9p c\u1eb7p"
                : item?.waitingPair
                    ? "\u0110ang ch\u1edd gh\u00e9p"
                    : "\u0110\u0103ng k\u00fd"
        );

        return parts.join(" \u00b7 ");
    }

    function tournamentSectionEmpty(text) {
        return `<div class="tournament-native-empty">${escapeHtml(text)}</div>`;
    }

    function renderTournamentInfoLine(label, value, boldValue) {
        return [
            '<p class="tournament-native-line">',
            `${escapeHtml(label)}: `,
            `<strong class="${boldValue ? "is-bold" : ""}">${escapeHtml(value)}</strong>`,
            "</p>"
        ].join("");
    }

    function renderTournamentActionLink(label, icon, href) {
        return [
            `<a class="tournament-native-action" href="${escapeHtml(buildSafeHref(href, "#"))}">`,
            `<ion-icon name="${escapeHtml(icon)}"></ion-icon>`,
            `<span>${escapeHtml(label)}</span>`,
            "</a>"
        ].join("");
    }

    function renderTournamentRegistrationCard(item) {
        const title = buildTournamentTeamName(item);
        const badgeText = item?.success
            ? "\u0110\u00e3 gh\u00e9p c\u1eb7p"
            : item?.waitingPair
                ? "\u0110ang ch\u1edd gh\u00e9p"
                : "\u0110\u0103ng k\u00fd";
        const badgeClass = item?.success
            ? "is-success"
            : item?.waitingPair
                ? "is-waiting"
                : "";
        const avatarUrl = item?.player1?.avatar || item?.player2?.avatar || "";

        return [
            '<article class="tournament-registration-card">',
            avatarMarkup(title, avatarUrl, "tournament-registration-card__avatar"),
            '<div class="tournament-registration-card__copy">',
            `<h4>${escapeHtml(title)}</h4>`,
            `<p>${escapeHtml(buildTournamentRegistrationMeta(item))}</p>`,
            "</div>",
            `<span class="tournament-registration-card__badge ${badgeClass}">${escapeHtml(badgeText)}</span>`,
            "</article>"
        ].join("");
    }

    function renderTournamentRegistrationList(title, items, emptyText) {
        const list = Array.isArray(items) ? items : [];

        return [
            '<section class="tournament-registration-list">',
            '<div class="tournament-registration-list__head">',
            `<h4>${escapeHtml(title)}</h4>`,
            `<span>${list.length}</span>`,
            "</div>",
            list.length > 0
                ? `<div class="tournament-registration-list__body">${list.slice(0, 4).map(renderTournamentRegistrationCard).join("")}</div>`
                : tournamentSectionEmpty(emptyText),
            list.length > 4
                ? `<p class="tournament-registration-list__more">+${list.length - 4} \u0111\u0103ng k\u00fd kh\u00e1c</p>`
                : "",
            "</section>"
        ].join("");
    }

    function renderTournamentRegistrationsSection(detail, registrations, rounds) {
        const counts = registrations?.counts || {};
        const successItems = Array.isArray(registrations?.successItems) ? registrations.successItems : [];
        const waitingItems = Array.isArray(registrations?.waitingItems) ? registrations.waitingItems : [];
        const registeredCount = detail?.registeredCount != null
            ? detail.registeredCount
            : toNumber(counts.success) + toNumber(counts.waiting);
        const pairedCount = detail?.pairedCount != null
            ? detail.pairedCount
            : toNumber(counts.success);

        return [
            '<section id="tournament-registrations" class="tournament-native-block tournament-native-block--spacious">',
            '<h3 class="tournament-native-section-title">\u0110\u0103ng k\u00fd c\u00f4ng khai</h3>',
            '<div class="tournament-native-stats">',
            `<div class="tournament-native-stat"><small>Th\u00e0nh vi\u00ean \u0111\u00e3 \u0111\u0103ng k\u00fd</small><strong>${escapeHtml(String(registeredCount))}</strong></div>`,
            `<div class="tournament-native-stat"><small>Th\u00e0nh vi\u00ean \u0111\u00e3 gh\u00e9p c\u1eb7p</small><strong>${escapeHtml(String(pairedCount))}</strong></div>`,
            `<div class="tournament-native-stat"><small>\u0110\u1ed9i th\u00e0nh c\u00f4ng</small><strong>${escapeHtml(String(toNumber(counts.success)))}</strong></div>`,
            `<div class="tournament-native-stat"><small>C\u00f2n ch\u1ed7</small><strong>${escapeHtml(String(toNumber(counts.capacityLeft)))}</strong></div>`,
            `<div class="tournament-native-stat"><small>\u0110ang ch\u1edd gh\u00e9p</small><strong>${escapeHtml(String(toNumber(counts.waiting)))}</strong></div>`,
            `<div class="tournament-native-stat"><small>S\u1ed1 v\u00f2ng \u0111\u1ea5u</small><strong>${escapeHtml(String(Array.isArray(rounds) ? rounds.length : 0))}</strong></div>`,
            "</div>",
            '<div class="tournament-registration-grid">',
            renderTournamentRegistrationList(
                "\u0110\u00e3 gh\u00e9p c\u1eb7p",
                successItems,
                "Ch\u01b0a c\u00f3 \u0111\u1ed9i n\u00e0o \u0111\u01b0\u1ee3c x\u00e1c nh\u1eadn."
            ),
            renderTournamentRegistrationList(
                "\u0110ang ch\u1edd gh\u00e9p",
                waitingItems,
                "Hi\u1ec7n kh\u00f4ng c\u00f2n \u0111\u0103ng k\u00fd ch\u1edd gh\u00e9p."
            ),
            "</div>",
            "</section>"
        ].join("");
    }

    function buildTournamentRoundItems(rounds) {
        return (Array.isArray(rounds) ? rounds : []).map(function (round, index) {
            return {
                key: String(round?.tournamentRoundMapId || round?.roundKey || `round-${index + 1}`),
                label: trimToEmpty(round?.roundLabel) || `V\u00f2ng ${index + 1}`,
                round: round
            };
        });
    }

    function renderTournamentRoundTabs(groupName, items) {
        if (!Array.isArray(items) || items.length === 0) {
            return "";
        }

        return [
            `<div class="tournament-native-tabs" data-tournament-tab-group="${escapeHtml(groupName)}">`,
            items.map(function (item, index) {
                return `<button class="tournament-native-tab ${index === 0 ? "is-active" : ""}" type="button" data-tournament-tab-target="${escapeHtml(item.key)}">${escapeHtml(item.label)}</button>`;
            }).join(""),
            "</div>"
        ].join("");
    }

    function renderTournamentScheduleMatch(match, index) {
        const teamA = trimToEmpty(match?.team1?.displayName) || "\u0110\u1ed9i ch\u01b0a x\u00e1c \u0111\u1ecbnh";
        const teamB = trimToEmpty(match?.team2?.displayName) || "\u0110\u1ed9i ch\u01b0a x\u00e1c \u0111\u1ecbnh";
        const hasWinner = !!match?.winnerRegistrationId || !!match?.winner || trimToEmpty(match?.winnerTeam);
        const isWinnerA = hasWinner && match?.winnerRegistrationId === match?.team1RegistrationId;
        const isWinnerB = hasWinner && match?.winnerRegistrationId === match?.team2RegistrationId;
        const videoHref = trimToEmpty(match?.videoUrl) ? buildSafeHref(match.videoUrl, "#") : "";
        const matchHref = buildSafeHref(`/PickleballWeb/Match/${match?.matchId}`, "#");
        const courtText = trimToEmpty(match?.addressText) || trimToEmpty(match?.courtText) || "Ch\u01b0a c\u1eadp nh\u1eadt";

        return [
            `<div class="tournament-match-card ${hasWinner ? "is-finished" : ""}">`,
            `<div class="tournament-match-card__index ${hasWinner ? "is-finished" : ""}">${index + 1}</div>`,
            '<div class="tournament-match-card__body">',
            `<p class="tournament-match-card__meta">#${escapeHtml(String(match?.matchId || index + 1))} (${escapeHtml(formatClock(match?.startAt))}; S\u00e2n: ${escapeHtml(courtText)})</p>`,
            '<div class="tournament-match-card__teams">',
            '<div class="tournament-match-card__teamnames">',
            `<strong class="${isWinnerA ? "is-winner" : ""}">${escapeHtml(teamA)}</strong>`,
            `<strong class="${isWinnerB ? "is-winner" : ""}">${escapeHtml(teamB)}</strong>`,
            "</div>",
            '<div class="tournament-match-card__scores">',
            `<span class="${isWinnerA ? "is-winner" : ""}">${escapeHtml(String(toNumber(match?.scoreTeam1)))}</span>`,
            `<span class="${isWinnerB ? "is-winner" : ""}">${escapeHtml(String(toNumber(match?.scoreTeam2)))}</span>`,
            "</div>",
            "</div>",
            '<div class="tournament-match-card__actions">',
            videoHref
                ? `<a class="tournament-match-card__action is-video" href="${escapeHtml(videoHref)}" target="_blank" rel="noreferrer"><ion-icon name="play-circle-outline"></ion-icon><span>Xem video</span></a>`
                : '<span class="tournament-match-card__action is-disabled"><ion-icon name="play-circle-outline"></ion-icon><span>Xem video</span></span>',
            `<a class="tournament-match-card__action is-strong" href="${escapeHtml(matchHref)}"><ion-icon name="flag-outline"></ion-icon><span>Di\u1ec5n bi\u1ebfn</span></a>`,
            "</div>",
            "</div>",
            "</div>"
        ].join("");
    }

    function renderTournamentScheduleGroup(roundKey, group, groupIndex) {
        const matches = Array.isArray(group?.matches) ? group.matches : [];
        const groupName = trimToEmpty(group?.groupName) || String(groupIndex + 1);
        const groupId = `${roundKey}-${group?.tournamentRoundGroupId || groupIndex + 1}`;

        return [
            `<article class="tournament-schedule-group is-open" data-tournament-group="${escapeHtml(groupId)}">`,
            `<button class="tournament-schedule-group__header" type="button" data-tournament-group-toggle="${escapeHtml(groupId)}">`,
            '<span class="tournament-schedule-group__left">',
            '<ion-icon class="tournament-schedule-group__caret" name="chevron-down"></ion-icon>',
            '<span class="tournament-schedule-group__copy">',
            `<strong>B\u1ea3ng ${escapeHtml(groupName)}</strong>`,
            `<small>${escapeHtml(String(matches.length))} tr\u1eadn</small>`,
            "</span>",
            "</span>",
            '<ion-icon name="ellipsis-horizontal"></ion-icon>',
            "</button>",
            `<div class="tournament-schedule-group__body">`,
            matches.length > 0
                ? matches.map(function (match, index) {
                    return renderTournamentScheduleMatch(match, index);
                }).join("")
                : tournamentSectionEmpty("V\u00f2ng n\u00e0y ch\u01b0a c\u00f3 tr\u1eadn \u0111\u1ea5u c\u00f4ng khai."),
            "</div>",
            "</article>"
        ].join("");
    }

    function renderTournamentScheduleSection(detail, rounds) {
        const roundItems = buildTournamentRoundItems(rounds);

        return [
            '<section id="tournament-schedule" class="tournament-native-subscreen">',
            '<div class="tournament-native-subscreen__head">',
            '<h3 class="tournament-native-section-title">L\u1ecbch thi \u0111\u1ea5u</h3>',
            '<p class="tournament-native-section-caption">Theo d\u00f5i b\u1ea3ng \u0111\u1ea5u, s\u00e2n thi \u0111\u1ea5u v\u00e0 k\u1ebft qu\u1ea3 t\u1eebng tr\u1eadn.</p>',
            "</div>",
            '<div class="tournament-native-meta-row">',
            '<div class="tournament-native-meta-item">',
            '<ion-icon name="git-branch-outline"></ion-icon>',
            `<span>${escapeHtml(trimToEmpty(detail?.playoffType) || "Ch\u01b0a c\u1eadp nh\u1eadt")}</span>`,
            "</div>",
            '<div class="tournament-native-meta-item is-right">',
            '<ion-icon name="people-outline"></ion-icon>',
            `<span><strong>${escapeHtml(String(toNumber(detail?.expectedTeams)))}</strong> \u0111\u1ed9i - <strong>${escapeHtml(String(toNumber(detail?.matchesCount)))}</strong> tr\u1eadn \u0111\u1ea5u</span>`,
            "</div>",
            "</div>",
            renderTournamentRoundTabs("schedule", roundItems),
            roundItems.length > 0
                ? roundItems.map(function (item, index) {
                    const groups = Array.isArray(item.round?.groups) ? item.round.groups : [];

                    return [
                        `<div class="tournament-native-panel ${index === 0 ? "is-active" : ""}" data-tournament-panel-group="schedule" data-tournament-panel-key="${escapeHtml(item.key)}" ${index === 0 ? "" : "hidden"}>`,
                        groups.length > 0
                            ? groups.map(function (group, groupIndex) {
                                return renderTournamentScheduleGroup(item.key, group, groupIndex);
                            }).join("")
                            : tournamentSectionEmpty("\u0110ang ch\u1edd c\u1eadp nh\u1eadt l\u1ecbch thi \u0111\u1ea5u cho v\u00f2ng n\u00e0y."),
                        "</div>"
                    ].join("");
                }).join("")
                : tournamentSectionEmpty("Gi\u1ea3i \u0111\u1ea5u n\u00e0y ch\u01b0a c\u00f3 l\u1ecbch thi \u0111\u1ea5u c\u00f4ng khai."),
            "</section>"
        ].join("");
    }

    function renderTournamentStandingsGroup(group) {
        const rows = Array.isArray(group?.rows) ? group.rows : [];

        return [
            '<article class="tournament-standing-card">',
            `<h4>${escapeHtml(trimToEmpty(group?.groupName) || "B\u1ea3ng \u0111\u1ea5u")}</h4>`,
            '<div class="tournament-standing-table">',
            '<div class="tournament-standing-table__head">',
            '<span class="is-team">\u0110\u1ed9i</span>',
            '<span>Th\u1eafng</span>',
            '<span>\u0110i\u1ec3m</span>',
            '<span>HS\u1ed1</span>',
            '<span>H\u1ea1ng</span>',
            "</div>",
            rows.length > 0
                ? rows.map(function (row) {
                    const isTop = toNumber(row?.rank) === 1;

                    return [
                        `<div class="tournament-standing-table__row ${isTop ? "is-top" : ""}">`,
                        `<strong class="is-team">${escapeHtml(trimToEmpty(row?.teamName) || "\u0110\u1ed9i thi \u0111\u1ea5u")}</strong>`,
                        `<span>${escapeHtml(String(toNumber(row?.wins)))}</span>`,
                        `<span>${escapeHtml(String(toNumber(row?.points)))}</span>`,
                        `<span>${escapeHtml(formatSignedNumber(row?.scoreDiff))}</span>`,
                        `<span>${escapeHtml(String(toNumber(row?.rank)))}</span>`,
                        "</div>"
                    ].join("");
                }).join("")
                : tournamentSectionEmpty("Ch\u01b0a c\u00f3 d\u1eef li\u1ec7u x\u1ebfp h\u1ea1ng."),
            "</div>",
            "</article>"
        ].join("");
    }

    function renderTournamentStandingsSection(rounds, standingsByRoundMapId) {
        const roundItems = buildTournamentRoundItems(rounds).map(function (item) {
            return {
                key: item.key,
                label: item.label,
                standing: standingsByRoundMapId ? standingsByRoundMapId[item.key] : null
            };
        }).filter(function (item) {
            return !!item.standing;
        });

        return [
            '<section id="tournament-standings" class="tournament-native-subscreen">',
            '<div class="tournament-native-subscreen__head">',
            '<h3 class="tournament-native-section-title">B\u1ea3ng x\u1ebfp h\u1ea1ng</h3>',
            '<p class="tournament-native-section-caption">T\u1ed5ng h\u1ee3p \u0111i\u1ec3m, tr\u1eadn th\u1eafng v\u00e0 hi\u1ec7u s\u1ed1 c\u1ee7a t\u1eebng b\u1ea3ng \u0111\u1ea5u.</p>',
            "</div>",
            renderTournamentRoundTabs("standings", roundItems),
            roundItems.length > 0
                ? roundItems.map(function (item, index) {
                    const groups = Array.isArray(item.standing?.groups) ? item.standing.groups : [];

                    return [
                        `<div class="tournament-native-panel ${index === 0 ? "is-active" : ""}" data-tournament-panel-group="standings" data-tournament-panel-key="${escapeHtml(item.key)}" ${index === 0 ? "" : "hidden"}>`,
                        groups.length > 0
                            ? groups.map(renderTournamentStandingsGroup).join("")
                            : tournamentSectionEmpty("V\u00f2ng n\u00e0y ch\u01b0a c\u00f3 d\u1eef li\u1ec7u x\u1ebfp h\u1ea1ng."),
                        "</div>"
                    ].join("");
                }).join("")
                : tournamentSectionEmpty("Gi\u1ea3i \u0111\u1ea5u n\u00e0y ch\u01b0a c\u00f3 b\u1ea3ng x\u1ebfp h\u1ea1ng c\u00f4ng khai."),
            "</section>"
        ].join("");
    }

    async function loadTournamentNativeDetail(id) {
        return {
            detail: await fetchJson(`/api/public/tournaments/${id}`)
        };
    }

    function renderTournamentNativeDetail(data) {
        const detail = data?.detail || {};
        const contentHtml = normalizeRichHtml(detail?.content);
        const bannerUrl = normalizeMediaUrl(detail?.bannerUrl);

        return [
            '<div class="tournament-native-detail">',
            bannerUrl
                ? `<img class="tournament-native-detail__banner" src="${escapeHtml(bannerUrl)}" alt="${escapeHtml(trimToEmpty(detail?.title) || "Gi\u1ea3i \u0111\u1ea5u")}" loading="lazy">`
                : '<div class="tournament-native-detail__banner tournament-native-detail__banner--fallback"><ion-icon name="trophy-outline"></ion-icon></div>',
            '<div class="tournament-native-detail__body">',
            `<h2 class="tournament-native-detail__title">${escapeHtml(trimToEmpty(detail?.title) || "Chi ti\u1ebft gi\u1ea3i \u0111\u1ea5u")}</h2>`,
            '<div class="tournament-native-detail__info">',
            renderTournamentInfoLine("Ng\u00e0y", formatSlashDateTime(detail?.startTime), true),
            renderTournamentInfoLine("H\u1ea1n \u0111\u0103ng k\u00fd", formatSlashDateTime(detail?.registerDeadline), true),
            renderTournamentInfoLine("Th\u1ec3 th\u1ee9c", trimToEmpty(detail?.playoffType) || "-", true),
            renderTournamentInfoLine("Gi\u1ea3i", tournamentGameTypeLabel(detail?.gameType), true),
            '<div class="tournament-native-two-col">',
            renderTournamentInfoLine("Gi\u1edbi h\u1ea1n tr\u00ecnh \u0111\u01a1n t\u1ed1i \u0111a", formatFlexibleNumber(detail?.singleLimit), true),
            renderTournamentInfoLine("C\u1eb7p t\u1ed1i \u0111a", formatFlexibleNumber(detail?.doubleLimit), true),
            "</div>",
            renderTournamentInfoLine("\u0110\u1ecba \u0111i\u1ec3m", trimToEmpty(detail?.locationText) || "-", true),
            '<div class="tournament-native-two-col">',
            renderTournamentInfoLine("S\u1ed1 \u0111\u1ed9i d\u1ef1 ki\u1ebfn", String(toNumber(detail?.expectedTeams)), true),
            renderTournamentInfoLine("S\u1ed1 tr\u1eadn thi \u0111\u1ea5u", String(toNumber(detail?.matchesCount)), true),
            "</div>",
            '<div class="tournament-native-two-col">',
            renderTournamentInfoLine("T\u00ecnh tr\u1ea1ng", trimToEmpty(detail?.statusText) || trimToEmpty(detail?.status) || "-", true),
            renderTournamentInfoLine("D\u1ea1ng", trimToEmpty(detail?.formatText) || "-", true),
            "</div>",
            renderTournamentInfoLine("\u0110\u01a1n v\u1ecb t\u1ed5 ch\u1ee9c", trimToEmpty(detail?.organizer) || "-", false),
            renderTournamentInfoLine("Ng\u01b0\u1eddi t\u1ea1o gi\u1ea3i", trimToEmpty(detail?.creatorName) || "-", true),
            detail?.registeredCount != null
                ? renderTournamentInfoLine("Th\u00e0nh vi\u00ean \u0111\u00e3 \u0111\u0103ng k\u00fd", String(detail.registeredCount), true)
                : "",
            detail?.pairedCount != null
                ? renderTournamentInfoLine("Th\u00e0nh vi\u00ean \u0111\u00e3 gh\u00e9p c\u1eb7p", String(detail.pairedCount), true)
                : "",
            "</div>",
            '<section id="tournament-content" class="tournament-native-block">',
            '<h3 class="tournament-native-section-title">N\u1ed9i dung</h3>',
            contentHtml
                ? `<div class="page-richtext tournament-native-richtext">${contentHtml}</div>`
                : '<p class="tournament-native-empty-text">Ch\u01b0a c\u00f3 n\u1ed9i dung gi\u1ea3i \u0111\u1ea5u.</p>',
            "</section>",
            '<p class="tournament-native-caps">QU\u1ea2N L\u00dd GI\u1ea2I \u0110\u1ea4U</p>',
            '<div class="tournament-native-actions">',
            renderTournamentActionLink("Danh s\u00e1ch \u0111\u0103ng k\u00fd", "list", `/PickleballWeb/Tournament/${detail?.tournamentId}/Registrations`),
            renderTournamentActionLink("Th\u1ec3 l\u1ec7 gi\u1ea3i", "hammer", `/PickleballWeb/Tournament/${detail?.tournamentId}/Rule`),
            renderTournamentActionLink("L\u1ecbch thi \u0111\u1ea5u", "calendar", `/PickleballWeb/Tournament/${detail?.tournamentId}/Schedule`),
            renderTournamentActionLink("B\u1ea3ng x\u1ebfp h\u1ea1ng", "stats-chart", `/PickleballWeb/Tournament/${detail?.tournamentId}/Standings`),
            "</div>",
            "</div>",
            "</div>"
        ].join("");
    }

    function normalizeSearchText(value) {
        return String(value || "")
            .toLowerCase()
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "");
    }

    function registrationInitial(name) {
        const words = trimToEmpty(name).split(/\s+/).filter(Boolean);
        const lastWord = words.length > 0 ? words[words.length - 1] : "?";
        return lastWord.charAt(0).toUpperCase() || "?";
    }

    function renderRegistrationAvatar(name, avatarUrl, className) {
        const src = normalizeMediaUrl(avatarUrl);
        const cssClass = className || "tournament-registration-page__avatar";

        if (src) {
            return `<span class="${escapeHtml(cssClass)}"><img src="${escapeHtml(src)}" alt="${escapeHtml(name)}" loading="lazy"></span>`;
        }

        return `<span class="${escapeHtml(cssClass)}">${escapeHtml(registrationInitial(name))}</span>`;
    }

    function buildTournamentRegistrationPageItems(registrations) {
        const successItems = Array.isArray(registrations?.successItems) ? registrations.successItems : [];
        const waitingItems = Array.isArray(registrations?.waitingItems) ? registrations.waitingItems : [];
        const merged = successItems.concat(waitingItems);

        return merged.map(function (item) {
            const player1 = item?.player1 || {};
            const player2 = item?.player2 || {};
            const player2Resolved = item?.player2
                ? player2
                : {
                    name: "Ch\u1edd gh\u00e9p",
                    avatar: "",
                    level: 0,
                    verified: false,
                    isGuest: true
                };

            return {
                id: String(item?.registrationId || Math.random()),
                index: toNumber(item?.regIndex),
                regCode: trimToEmpty(item?.regCode),
                regTime: formatSlashDateTime(item?.regTime),
                points: item?.points ?? 0,
                success: !!item?.success,
                waitingPair: !!item?.waitingPair,
                player1: {
                    name: trimToEmpty(player1?.name) || "-",
                    avatar: player1?.avatar || "",
                    level: player1?.level ?? 0,
                    verified: !!player1?.verified,
                    isGuest: !!player1?.isGuest
                },
                player2: {
                    name: trimToEmpty(player2Resolved?.name) || "-",
                    avatar: player2Resolved?.avatar || "",
                    level: player2Resolved?.level ?? 0,
                    verified: !!player2Resolved?.verified,
                    isGuest: !!player2Resolved?.isGuest
                }
            };
        });
    }

    function renderTournamentRegistrationPlayer(player) {
        const name = trimToEmpty(player?.name) || "-";
        const level = formatFlexibleNumber(player?.level);
        const verified = !!player?.verified;
        const guest = !!player?.isGuest;

        return [
            '<div class="tournament-registration-page__player">',
            '<div class="tournament-registration-page__avatar-ring">',
            renderRegistrationAvatar(name, player?.avatar || "", "tournament-registration-page__avatar"),
            "</div>",
            `<strong>${escapeHtml(name)}</strong>`,
            `<span>(${escapeHtml(level)})</span>`,
            verified
                ? '<em class="is-verified">\u0110\u00e3 x\u00e1c th\u1ef1c</em>'
                : `<em class="is-pill">${escapeHtml(guest ? "Kh\u00e1ch" : "Ch\u1edd x\u00e1c th\u1ef1c")}</em>`,
            "</div>"
        ].join("");
    }

    function renderTournamentRegistrationRow(item) {
        const searchText = normalizeSearchText([
            item?.regCode,
            item?.regTime,
            item?.player1?.name,
            item?.player2?.name
        ].join(" "));

        return [
            `<article class="tournament-registration-page__item" data-registration-search="${escapeHtml(searchText)}">`,
            '<div class="tournament-registration-page__item-head">',
            `<strong>${escapeHtml(String(toNumber(item?.index)))}</strong>`,
            `<p>M\u00e3 \u0111k: <span>${escapeHtml(trimToEmpty(item?.regCode) || "-")}</span> ${escapeHtml(trimToEmpty(item?.regTime) || "")}</p>`,
            "</div>",
            '<div class="tournament-registration-page__grid">',
            renderTournamentRegistrationPlayer(item?.player1),
            renderTournamentRegistrationPlayer(item?.player2),
            `<div class="tournament-registration-page__points">${escapeHtml(formatFlexibleNumber(item?.points))}</div>`,
            "</div>",
            "</article>"
        ].join("");
    }

    async function loadTournamentRegistrationsPage(id) {
        const results = await Promise.allSettled([
            fetchJson(`/api/public/tournaments/${id}`),
            fetchJson(`/api/public/tournaments/${id}/registrations`),
            fetchJson(`/api/links?type=zalo`)
        ]);

        if (results[1].status !== "fulfilled") {
            throw new Error("tournament-registrations");
        }

        return {
            detail: results[0].status === "fulfilled" ? results[0].value : null,
            registrations: results[1].value,
            links: results[2].status === "fulfilled" ? results[2].value : null
        };
    }

    function renderTournamentRegistrationsPage(data) {
        const items = buildTournamentRegistrationPageItems(data?.registrations);
        const counts = data?.registrations?.counts || {};
        const detail = data?.detail || {};
        const zaloItem = Array.isArray(data?.links?.items)
            ? data.links.items.find(function (item) {
                return trimToEmpty(item?.type).toLowerCase() === "zalo";
            })
            : null;
        const zaloHref = trimToEmpty(zaloItem?.link) ? buildSafeHref(zaloItem.link, "#") : "";
        const capacityLeft = counts?.capacityLeft ?? detail?.expectedTeams ?? 0;

        return [
            '<div class="tournament-registration-page">',
            zaloHref
                ? `<div class="tournament-registration-page__links"><a class="tournament-registration-page__link" href="${escapeHtml(zaloHref)}" target="_blank" rel="noreferrer"><ion-icon name="link-outline"></ion-icon><span>Link nh\u00f3m Zalo</span></a></div>`
                : "",
            '<div class="tournament-registration-page__stats">',
            `<div class="tournament-registration-page__badge is-green"><span>Th\u00e0nh c\u00f4ng</span><strong>${escapeHtml(String(toNumber(counts?.success)))}</strong></div>`,
            `<div class="tournament-registration-page__badge is-orange"><span>Ch\u1edd gh\u00e9p</span><strong>${escapeHtml(String(toNumber(counts?.waiting)))}</strong></div>`,
            `<div class="tournament-registration-page__badge is-grey"><span>C\u00f2n ch\u1ed7</span><strong>${escapeHtml(String(toNumber(capacityLeft)))}</strong></div>`,
            "</div>",
            '<div class="tournament-registration-page__search">',
            '<div class="tournament-registration-page__searchbox">',
            '<input type="search" placeholder="Nh\u1eadp t\u00ean, m\u00e3 \u0111\u0103ng k\u00fd \u0111\u1ec3 t\u00ecm ki\u1ebfm..." data-registration-search-input>',
            '<ion-icon name="search"></ion-icon>',
            "</div>",
            "</div>",
            '<div class="tournament-registration-page__tablehead">',
            '<span class="is-player">V\u0110V1</span>',
            '<span class="is-player">V\u0110V2</span>',
            '<span class="is-points">\u0110i\u1ec3m</span>',
            "</div>",
            `<div class="tournament-registration-page__list" data-registration-list>${items.map(renderTournamentRegistrationRow).join("")}</div>`,
            '<p class="tournament-registration-page__empty" data-registration-empty hidden>Kh\u00f4ng c\u00f3 d\u1eef li\u1ec7u \u0111\u0103ng k\u00fd.</p>',
            "</div>"
        ].join("");
    }

    async function loadTournamentRulePage(id) {
        return fetchJson(`/api/tournaments/${id}/rule`);
    }

    function renderTournamentRulePage(data) {
        const ruleHtml = normalizeRichHtml(data?.tournamentRule);

        return [
            '<div class="tournament-rule-page">',
            '<div class="tournament-rule-page__card">',
            ruleHtml
                ? `<div class="page-richtext tournament-native-richtext">${ruleHtml}</div>`
                : '<p class="tournament-native-empty-text">Ch\u01b0a c\u00f3 th\u1ec3 l\u1ec7 gi\u1ea3i.</p>',
            "</div>",
            "</div>"
        ].join("");
    }

    async function loadTournamentSchedulePage(id) {
        return fetchJson(`/api/tournaments/${id}/rounds-with-matches`);
    }

    function renderTournamentSchedulePage(data) {
        const tournament = data?.tournament || {};
        const rounds = Array.isArray(data?.rounds) ? data.rounds : [];
        const roundItems = buildTournamentRoundItems(rounds);

        return [
            '<div class="tournament-subpage tournament-subpage--schedule">',
            '<div class="tournament-native-meta-row tournament-native-meta-row--page">',
            '<div class="tournament-native-meta-item">',
            '<ion-icon name="git-branch-outline"></ion-icon>',
            `<span>${escapeHtml(trimToEmpty(tournament?.playoffType) || "Ch\u01b0a c\u1eadp nh\u1eadt")}</span>`,
            "</div>",
            '<div class="tournament-native-meta-item is-right">',
            '<ion-icon name="people-outline"></ion-icon>',
            `<span><strong>${escapeHtml(String(toNumber(tournament?.expectedTeams)))}</strong> \u0111\u1ed9i - <strong>${escapeHtml(String(toNumber(tournament?.matchesCount)))}</strong> tr\u1eadn \u0111\u1ea5u</span>`,
            "</div>",
            "</div>",
            renderTournamentRoundTabs("schedule", roundItems),
            roundItems.length > 0
                ? roundItems.map(function (item, index) {
                    const groups = Array.isArray(item.round?.groups) ? item.round.groups : [];

                    return [
                        `<div class="tournament-native-panel ${index === 0 ? "is-active" : ""}" data-tournament-panel-group="schedule" data-tournament-panel-key="${escapeHtml(item.key)}" ${index === 0 ? "" : "hidden"}>`,
                        groups.length > 0
                            ? groups.map(function (group, groupIndex) {
                                return renderTournamentScheduleGroup(item.key, group, groupIndex);
                            }).join("")
                            : tournamentSectionEmpty("V\u00f2ng \u0111\u1ea5u n\u00e0y ch\u01b0a c\u00f3 tr\u1eadn n\u00e0o."),
                        "</div>"
                    ].join("");
                }).join("")
                : tournamentSectionEmpty("Gi\u1ea3i \u0111\u1ea5u n\u00e0y ch\u01b0a c\u00f3 l\u1ecbch thi \u0111\u1ea5u c\u00f4ng khai."),
            "</div>"
        ].join("");
    }

    async function loadTournamentStandingsPage(id) {
        const roundsPayload = await fetchJson(`/api/tournaments/${id}/rounds-with-matches`);
        const rounds = Array.isArray(roundsPayload?.rounds) ? roundsPayload.rounds : [];
        const standingsByRoundMapId = Object.create(null);

        if (rounds.length > 0) {
            const standingResults = await Promise.allSettled(
                rounds.map(function (round) {
                    return fetchJson(`/api/tournaments/${id}/round-maps/${round.tournamentRoundMapId}/standings`);
                })
            );

            standingResults.forEach(function (result, index) {
                if (result.status === "fulfilled") {
                    standingsByRoundMapId[String(rounds[index]?.tournamentRoundMapId)] = result.value;
                }
            });
        }

        return {
            tournament: roundsPayload?.tournament || null,
            rounds: rounds,
            standingsByRoundMapId: standingsByRoundMapId
        };
    }

    function renderTournamentStandingsPage(data) {
        const rounds = Array.isArray(data?.rounds) ? data.rounds : [];
        const roundItems = buildTournamentRoundItems(rounds).map(function (item) {
            return {
                key: item.key,
                label: item.label,
                standing: data?.standingsByRoundMapId ? data.standingsByRoundMapId[item.key] : null
            };
        });

        return [
            '<div class="tournament-subpage tournament-subpage--standings">',
            renderTournamentRoundTabs("standings", roundItems),
            roundItems.length > 0
                ? roundItems.map(function (item, index) {
                    const groups = Array.isArray(item.standing?.groups) ? item.standing.groups : [];

                    return [
                        `<div class="tournament-native-panel ${index === 0 ? "is-active" : ""}" data-tournament-panel-group="standings" data-tournament-panel-key="${escapeHtml(item.key)}" ${index === 0 ? "" : "hidden"}>`,
                        groups.length > 0
                            ? groups.map(renderTournamentStandingsGroup).join("")
                            : tournamentSectionEmpty("Ch\u01b0a c\u00f3 d\u1eef li\u1ec7u b\u1ea3ng x\u1ebfp h\u1ea1ng cho v\u00f2ng n\u00e0y."),
                        "</div>"
                    ].join("");
                }).join("")
                : tournamentSectionEmpty("Ch\u01b0a c\u00f3 v\u00f2ng \u0111\u1ea5u n\u00e0o."),
            "</div>"
        ].join("");
    }

    const detailConfigs = {
        "member-detail": {
            load: async function (id) {
                const results = await Promise.allSettled([
                    fetchJson(`/api/users/${id}`),
                    fetchJson(`/api/users/${id}/achievements`),
                    fetchJson(`/api/users/${id}/rating-history`)
                ]);

                if (results[0].status !== "fulfilled") {
                    throw new Error("member-detail");
                }

                return {
                    detail: results[0].value,
                    achievements: Array.isArray(results[1].value?.items) ? results[1].value.items : [],
                    ratingHistory: Array.isArray(results[2].value?.items) ? results[2].value.items : []
                };
            },
            render: renderMemberDetail
        },
        "coach-detail": {
            load: function (id) { return fetchJson(`/api/coaches/${id}`); },
            render: function (item) { return renderCoachDetail(item, "Huấn luyện viên", "Khu vực dạy"); }
        },
        "referee-detail": {
            load: function (id) { return fetchJson(`/api/referees/${id}`); },
            render: function (item) { return renderCoachDetail(item, "Trọng tài", "Khu vực công tác"); }
        },
        "club-detail": {
            load: async function (id) {
                const results = await Promise.allSettled([
                    fetchJson(`/api/clubs/${id}/overview`),
                    fetchJson(`/api/clubs/${id}/members?page=1&pageSize=12`)
                ]);

                if (results[0].status !== "fulfilled") {
                    throw new Error("club-detail");
                }

                return {
                    detail: results[0].value,
                    members: Array.isArray(results[1].value?.items) ? results[1].value.items : []
                };
            },
            render: renderClubDetail
        },
        "court-detail": {
            load: function (id) { return fetchJson(`/api/public/courts/${id}`); },
            render: renderCourtDetail
        },
        "tournament-detail": {
            load: async function (id) {
                const results = await Promise.allSettled([
                    fetchJson(`/api/public/tournaments/${id}`),
                    fetchJson(`/api/public/tournaments/${id}/registrations`),
                    fetchJson(`/api/tournaments/${id}/rounds-with-matches`),
                    fetchJson(`/api/tournaments/${id}/rule`)
                ]);

                if (results[0].status !== "fulfilled") {
                    throw new Error("tournament-detail");
                }

                return {
                    detail: results[0].value,
                    registrations: results[1].status === "fulfilled" ? results[1].value : null,
                    rounds: results[2].status === "fulfilled" ? results[2].value : null,
                    rule: results[3].status === "fulfilled" ? results[3].value : null
                };
            },
            render: renderTournamentDetail
        },
        "exchange-detail": {
            load: function (id) { return fetchJson(`/api/public/exchanges/${id}`); },
            render: renderExchangeDetail
        },
        "match-detail": {
            load: function (id) { return fetchJson(`/api/tournaments/matches/${id}`); },
            render: renderMatchDetail
        }
    };

    detailConfigs["coach-detail"].render = renderCoachDetail;
    detailConfigs["referee-detail"].render = function (item) {
        return renderCoachLikeDetail(item, "Trá»ng tÃ i", "Khu vá»±c cÃ´ng tÃ¡c");
    };

    detailConfigs["referee-detail"].render = function (item) {
        return renderCoachLikeDetail(item, "Trọng tài", "Khu vực công tác");
    };

    detailConfigs["referee-detail"].render = renderRefereeDetail;
    detailConfigs["tournament-detail"].load = loadTournamentNativeDetail;
    detailConfigs["tournament-detail"].render = renderTournamentNativeDetail;
    detailConfigs["tournament-registrations"] = {
        load: loadTournamentRegistrationsPage,
        render: renderTournamentRegistrationsPage
    };
    detailConfigs["tournament-rule-page"] = {
        load: loadTournamentRulePage,
        render: renderTournamentRulePage
    };
    detailConfigs["tournament-schedule-page"] = {
        load: loadTournamentSchedulePage,
        render: renderTournamentSchedulePage
    };
    detailConfigs["tournament-standings-page"] = {
        load: loadTournamentStandingsPage,
        render: renderTournamentStandingsPage
    };

    function applyTournamentDetailShell(root, titleText, shareEnabled) {
        root.classList.add("detail-screen--tournament-native");

        const title = qs(".page-hero h1", root);
        const eyebrow = qs(".page-hero__eyebrow", root);
        const description = qs(".page-hero p", root);
        const backText = qs(".page-hero__back span", root);
        const tabbar = qs(".mobile-tabbar--page", root);
        const headerContainer = qs(".page-hero .mobile-web-screen__container", root);
        const oldAction = qs("[data-tournament-share]", root);

        if (title) {
            title.textContent = trimToEmpty(titleText) || "Chi ti\u1ebft gi\u1ea3i \u0111\u1ea5u";
        }

        if (eyebrow) {
            eyebrow.hidden = true;
        }

        if (description) {
            description.hidden = true;
        }

        if (backText) {
            backText.hidden = true;
        }

        if (tabbar) {
            tabbar.hidden = true;
        }

        if (oldAction) {
            oldAction.remove();
        }

        if (shareEnabled && headerContainer && !qs("[data-tournament-share]", headerContainer)) {
            const actionButton = document.createElement("button");
            actionButton.type = "button";
            actionButton.className = "page-hero__action";
            actionButton.setAttribute("aria-label", "Chia s\u1ebb");
            actionButton.setAttribute("data-tournament-share", "true");
            actionButton.innerHTML = '<ion-icon name="share-social"></ion-icon>';
            headerContainer.appendChild(actionButton);
        }
    }

    function applyCoachDetailShell(root, titleText) {
        root.classList.add("detail-screen--coach-native");

        const title = qs(".page-hero h1", root);
        const eyebrow = qs(".page-hero__eyebrow", root);
        const description = qs(".page-hero p", root);
        const backText = qs(".page-hero__back span", root);
        const tabbar = qs(".mobile-tabbar--page", root);

        if (title) {
            title.textContent = "ThÃ´ng tin HLV";
        }

        if (title) {
            title.textContent = "Thông tin HLV";
        }

        if (title) {
            title.textContent = trimToEmpty(titleText) || title.textContent;
        }

        if (eyebrow) {
            eyebrow.hidden = true;
        }

        if (description) {
            description.hidden = true;
        }

        if (backText) {
            backText.hidden = true;
        }

        if (tabbar) {
            tabbar.hidden = true;
        }
    }

    function initCoachDetailInteractions(root) {
        qsa("[data-coach-section-toggle]", root).forEach(function (button) {
            button.addEventListener("click", function () {
                const section = button.closest("[data-coach-section]");
                const body = qs(".coach-native-section__body", section);
                const icon = qs("ion-icon", button);
                const isOpen = section?.classList.contains("is-open");

                section?.classList.toggle("is-open", !isOpen);

                if (body) {
                    body.hidden = !!isOpen;
                }

                if (icon) {
                    icon.setAttribute("name", isOpen ? "chevron-forward" : "chevron-up");
                }
            });
        });
    }

    async function shareCurrentPage(titleText) {
        const shareUrl = window.location.href;

        if (navigator.share) {
            try {
                await navigator.share({
                    title: titleText,
                    text: titleText,
                    url: shareUrl
                });
                return;
            } catch (error) {
                if (error?.name === "AbortError") {
                    return;
                }
            }
        }

        if (navigator.clipboard?.writeText) {
            try {
                await navigator.clipboard.writeText(shareUrl);
                window.alert("\u0110\u00e3 sao ch\u00e9p li\u00ean k\u1ebft gi\u1ea3i \u0111\u1ea5u.");
                return;
            } catch (_error) {
                // Fallback handled below.
            }
        }

        window.prompt("Sao ch\u00e9p li\u00ean k\u1ebft gi\u1ea3i \u0111\u1ea5u:", shareUrl);
    }

    function initTournamentTabGroup(root, groupName) {
        const buttons = qsa(`[data-tournament-tab-group="${groupName}"] [data-tournament-tab-target]`, root);
        const panels = qsa(`[data-tournament-panel-group="${groupName}"]`, root);

        if (buttons.length === 0 || panels.length === 0) {
            return;
        }

        buttons.forEach(function (button) {
            button.addEventListener("click", function () {
                const target = trimToEmpty(button.getAttribute("data-tournament-tab-target"));

                buttons.forEach(function (item) {
                    item.classList.toggle("is-active", item === button);
                });

                panels.forEach(function (panel) {
                    const isActive = trimToEmpty(panel.getAttribute("data-tournament-panel-key")) === target;
                    panel.hidden = !isActive;
                    panel.classList.toggle("is-active", isActive);
                });
            });
        });
    }

    function initTournamentGroupToggles(root) {
        qsa("[data-tournament-group-toggle]", root).forEach(function (button) {
            button.addEventListener("click", function () {
                const target = trimToEmpty(button.getAttribute("data-tournament-group-toggle"));
                const group = qs(`[data-tournament-group="${target}"]`, root);
                const body = qs(".tournament-schedule-group__body", group);
                const caret = qs(".tournament-schedule-group__caret", button);
                const isOpen = group?.classList.contains("is-open");

                group?.classList.toggle("is-open", !isOpen);

                if (body) {
                    body.hidden = !!isOpen;
                }

                if (caret) {
                    caret.setAttribute("name", isOpen ? "chevron-forward" : "chevron-down");
                }
            });
        });
    }

    function initTournamentRegistrationSearch(root) {
        const input = qs("[data-registration-search-input]", root);
        const rows = qsa("[data-registration-search]", root);
        const empty = qs("[data-registration-empty]", root);

        if (!input || rows.length === 0) {
            return;
        }

        const applyFilter = function () {
            const query = normalizeSearchText(input.value);
            let visibleCount = 0;

            rows.forEach(function (row) {
                const haystack = trimToEmpty(row.getAttribute("data-registration-search"));
                const isVisible = !query || haystack.includes(query);
                row.hidden = !isVisible;

                if (isVisible) {
                    visibleCount += 1;
                }
            });

            if (empty) {
                empty.hidden = visibleCount !== 0;
            }
        };

        input.addEventListener("input", applyFilter);
        applyFilter();
    }

    function initTournamentDetailInteractions(root, data, kind) {
        const shareButton = qs("[data-tournament-share]", root);
        const titleText =
            trimToEmpty(data?.detail?.title) ||
            trimToEmpty(data?.tournament?.title) ||
            trimToEmpty(data?.title) ||
            "Chi ti\u1ebft gi\u1ea3i \u0111\u1ea5u";

        if (shareButton) {
            shareButton.onclick = function () {
                shareCurrentPage(titleText);
            };
        }

        if (kind === "tournament-rule-page") {
            const headerTitle = qs(".page-hero h1", root);
            if (headerTitle && trimToEmpty(data?.title)) {
                headerTitle.textContent = trimToEmpty(data.title);
            }
        }

        if (kind === "tournament-registrations") {
            initTournamentRegistrationSearch(root);
        }

        if (kind === "tournament-schedule-page" || kind === "tournament-standings-page") {
            initTournamentTabGroup(root, "schedule");
            initTournamentTabGroup(root, "standings");
            initTournamentGroupToggles(root);
        }
    }

    async function initDetailPage(root) {
        const kind = trimToEmpty(root.getAttribute("data-detail-kind"));
        const id = Number(root.getAttribute("data-detail-id"));
        const body = qs("[data-detail-body]", root);

        if (!kind || !Number.isFinite(id) || !body) {
            return;
        }

        const config = detailConfigs[kind];
        if (!config) {
            return;
        }

        if (kind === "coach-detail" || kind === "referee-detail") {
            applyCoachDetailShell(
                root,
                kind === "coach-detail" ? "Th\u00f4ng tin HLV" : "Th\u00f4ng tin Tr\u1ecdng t\u00e0i"
            );
        }

        if (
            kind === "tournament-detail" ||
            kind === "tournament-registrations" ||
            kind === "tournament-rule-page" ||
            kind === "tournament-schedule-page" ||
            kind === "tournament-standings-page"
        ) {
            applyTournamentDetailShell(
                root,
                kind === "tournament-registrations"
                    ? "Danh s\u00e1ch \u0111\u0103ng k\u00fd"
                    : kind === "tournament-rule-page"
                        ? "Th\u1ec3 l\u1ec7 gi\u1ea3i"
                        : kind === "tournament-schedule-page"
                            ? "L\u1ecbch thi \u0111\u1ea5u"
                            : kind === "tournament-standings-page"
                                ? "B\u1ea3ng x\u1ebfp h\u1ea1ng"
                                : "Chi ti\u1ebft gi\u1ea3i \u0111\u1ea5u",
                kind === "tournament-detail" || kind === "tournament-schedule-page"
            );
        }

        try {
            const data = await config.load(id);
            body.innerHTML = config.render(data);

            if (kind === "coach-detail" || kind === "referee-detail") {
                initCoachDetailInteractions(root);
            }

            if (
                kind === "tournament-detail" ||
                kind === "tournament-registrations" ||
                kind === "tournament-rule-page" ||
                kind === "tournament-schedule-page" ||
                kind === "tournament-standings-page"
            ) {
                initTournamentDetailInteractions(root, data, kind);
            }
        } catch (error) {
            body.innerHTML = [
                '<article class="page-empty">',
                "<strong>Không thể tải chi tiết</strong>",
                "<p>Thông tin công khai cho mục này hiện chưa sẵn sàng hoặc đã bị thay đổi.</p>",
                "</article>"
            ].join("");
        }
    }

    document.addEventListener("DOMContentLoaded", function () {
        const membersRoot = qs("[data-members-page]");
        if (membersRoot) {
            initMembersPage(membersRoot);
        }

        const listRoot = qs("[data-page-kind]");
        if (listRoot) {
            initListPage(listRoot);
        }

        const detailRoot = qs("[data-detail-kind]");
        if (detailRoot) {
            initDetailPage(detailRoot);
        }
    });
})();
