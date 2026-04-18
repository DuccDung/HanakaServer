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

    function toNumber(value) {
        const number = Number(value);
        return Number.isFinite(number) ? number : 0;
    }

    function formatDecimal(value) {
        const number = Number(value);
        return Number.isFinite(number) ? number.toFixed(1) : "0.0";
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

    function mediaMarkup(url, alt, fallbackText) {
        const src = trimToEmpty(url);

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
        const src = trimToEmpty(avatarUrl);
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

    function renderCoachDetail(item, roleLabel, areaLabel) {
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

        try {
            const data = await config.load(id);
            body.innerHTML = config.render(data);
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
