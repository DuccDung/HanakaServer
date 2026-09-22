(function () {
    const menuItems = [
        { key: "rules", label: "Luật Chơi", icon: "document-text-outline", href: "/PickleballWeb/Rules" },
        { key: "guide", label: "Hướng Dẫn", icon: "map-outline", href: "/PickleballWeb/Guide" },
        { key: "members", label: "T.Viên", icon: "people-outline", href: "/PickleballWeb/Members" },
        { key: "club", label: "CLB", icon: "shield-outline", href: "/PickleballWeb/Clubs" },
        { key: "coach", label: "HL Viên", icon: "school-outline", href: "/PickleballWeb/Coaches" },
        { key: "court", label: "Sân Bãi", icon: "location-outline", href: "/PickleballWeb/Courts" },
        { key: "ref", label: "Trọng Tài", icon: "flag-outline", href: "/PickleballWeb/Referees" },
        { key: "tournament", label: "Giải Đấu", icon: "trophy-outline", href: "/PickleballWeb/Tournaments" },
        { key: "exchange", label: "Giao Lưu", icon: "people-circle-outline", href: "/PickleballWeb/Exchanges" },
        { key: "match", label: "Trận Đấu", icon: "tennisball-outline", href: "/PickleballWeb/Matches" }
    ];

    const state = {
        banners: [],
        bannerIndex: 0,
        bannerTimer: null,
        guideLink: "#tournaments",
        session: null
    };

    const prefersReducedMotion =
        typeof window !== "undefined" &&
        typeof window.matchMedia === "function" &&
        window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    function qs(selector, root) {
        return (root || document).querySelector(selector);
    }

    function qsa(selector, root) {
        return Array.from((root || document).querySelectorAll(selector));
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function trimToEmpty(value) {
        return String(value ?? "").trim();
    }

    function normalizeAvatarUrl(value) {
        var normalized = trimToEmpty(value);
        if (!normalized || normalized === "null" || normalized === "undefined") {
            return "";
        }

        try {
            var resolved = new URL(normalized, window.location.origin);
            var isLocalPreviewHost = /^(localhost|127\.0\.0\.1)$/i.test(window.location.hostname);
            var isUploadAsset = resolved.pathname.toLowerCase().startsWith("/uploads/");

            if (isLocalPreviewHost && isUploadAsset && resolved.origin !== window.location.origin) {
                return window.location.origin + resolved.pathname + resolved.search;
            }

            return resolved.href;
        } catch (error) {
            return normalized;
        }
    }

    function toCount(value) {
        const number = Number(value);
        if (!Number.isFinite(number) || number < 0) {
            return 0;
        }

        return Math.round(number);
    }

    function padCount(value) {
        return String(toCount(value)).padStart(2, "0");
    }

    function setText(selector, value) {
        qsa(selector).forEach(function (node) {
            node.textContent = value;
        });
    }

    function setStat(name, value) {
        setText(`[data-stat="${name}"]`, padCount(value));
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
            return "Chưa có lịch";
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
            return "Chưa có lịch";
        }

        return new Intl.DateTimeFormat("vi-VN", {
            day: "2-digit",
            month: "2-digit",
            year: "numeric",
            hour: "2-digit",
            minute: "2-digit"
        }).format(date);
    }

    function formatTournamentFee(amount, currency) {
        const value = Number(amount);
        if (!Number.isFinite(value) || value <= 0) {
            return "Miễn phí";
        }

        const currencyCode = trimToEmpty(currency).toUpperCase() || "VND";
        return `${new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 0 }).format(value)} ${currencyCode}`;
    }

    function tournamentStatusInfo(item) {
        const rawStatus = trimToEmpty(item?.status).toUpperCase();
        const registeredCount = toCount(item?.registeredCount);
        const expectedTeams = toCount(item?.expectedTeams);
        const deadline = parseDate(item?.registerDeadline);
        const suppliedText = trimToEmpty(item?.statusText) || trimToEmpty(item?.stateText);

        if (rawStatus === "OPEN" && expectedTeams > 0 && registeredCount >= expectedTeams) {
            return { text: "Đã đủ đội", className: "is-full", canRegister: false };
        }

        if (rawStatus === "OPEN" && deadline && deadline < new Date()) {
            return { text: "Hết hạn đăng ký", className: "is-closed", canRegister: false };
        }

        if (rawStatus === "OPEN") {
            return { text: suppliedText || "Đang mở đăng ký", className: "is-open", canRegister: true };
        }

        if (rawStatus === "ACTIVE" || rawStatus === "ONGOING") {
            return { text: suppliedText || "Đang diễn ra", className: "is-active", canRegister: false };
        }

        if (rawStatus === "COMPLETED" || rawStatus === "FINISHED") {
            return { text: suppliedText || "Đã kết thúc", className: "is-finished", canRegister: false };
        }

        if (rawStatus === "CLOSED") {
            return { text: suppliedText || "Đã đóng đăng ký", className: "is-closed", canRegister: false };
        }

        return { text: suppliedText || rawStatus || "Đang cập nhật", className: "is-neutral", canRegister: false };
    }

    function tournamentTypeText(item) {
        if (item?.isRelay) {
            const details = ["Đội tiếp sức"];
            const teamSize = toCount(item.relayTeamSize);
            const targetScore = toCount(item.relayTargetScore);
            if (teamSize > 0) details.push(`${teamSize} người`);
            if (targetScore > 0) details.push(`đích ${targetScore}`);
            return details.join(" · ");
        }

        const explicitLabel = trimToEmpty(item?.tournamentTypeLabel);
        if (explicitLabel) {
            return explicitLabel;
        }

        const gameType = trimToEmpty(item?.gameType).toUpperCase();
        const gender = trimToEmpty(item?.genderCategory).toUpperCase();
        const gameLabel = gameType === "SINGLE" ? "Đơn" : "Đôi";
        const genderLabels = { MEN: "Nam", WOMEN: "Nữ", MIXED: "Nam Nữ", OPEN: "Mở rộng" };
        return `${gameLabel} ${genderLabels[gender] || "Mở rộng"}`;
    }

    function tournamentDescription(item) {
        const content = trimToEmpty(item?.content)
            .replace(/<[^>]*>/g, " ")
            .replace(/\s+/g, " ")
            .trim();

        return content || "Xem thông tin, thể lệ và danh sách đăng ký của giải đấu.";
    }

    function tournamentMediaMarkup(item, statusInfo) {
        const imageUrl = normalizeAvatarUrl(item?.bannerUrl);
        const title = trimToEmpty(item?.title) || "Giải đấu Hanaka Sport";
        const imageMarkup = imageUrl
            ? `<img src="${escapeHtml(imageUrl)}" alt="${escapeHtml(title)}" loading="lazy" data-tournament-image>`
            : "";

        return [
            '<div class="data-card__media tournament-card__media">',
            '<div class="tournament-card__fallback" aria-hidden="true">',
            '<span class="tournament-card__fallback-icon"><ion-icon name="trophy-outline"></ion-icon></span>',
            '<span>Hanaka Sport</span>',
            "</div>",
            imageMarkup,
            '<div class="tournament-card__media-overlay">',
            `<span class="tournament-card__status ${escapeHtml(statusInfo.className)}">${escapeHtml(statusInfo.text)}</span>`,
            item?.isRelay ? '<span class="tournament-card__relay"><ion-icon name="git-compare-outline"></ion-icon> Tiếp sức</span>' : "",
            "</div>",
            "</div>"
        ].join("");
    }

    function bindTournamentImageFallbacks(container) {
        qsa("[data-tournament-image]", container).forEach(function (image) {
            function showFallback() {
                image.hidden = true;
                image.closest(".tournament-card__media")?.classList.add("is-image-fallback");
            }

            image.addEventListener("error", showFallback, { once: true });
            if (image.complete && image.naturalWidth === 0) {
                showFallback();
            }
        });
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

        return `<div class="media-placeholder"><span>${escapeHtml(fallbackText || "Hanaka Sport")}</span></div>`;
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

    function avatarMarkup(name, avatarUrl) {
        const src = trimToEmpty(avatarUrl);

        if (src) {
            return `<span class="team-avatar"><img src="${escapeHtml(src)}" alt="${escapeHtml(name)}" loading="lazy"></span>`;
        }

        return `<span class="team-avatar">${escapeHtml(initials(name))}</span>`;
    }

    async function fetchJson(url) {
        const response = await fetch(url, {
            headers: { Accept: "application/json" },
            cache: "no-store",
            hanakaLoading: "silent"
        });

        if (!response.ok) {
            throw new Error(`Request failed: ${response.status}`);
        }

        return response.json();
    }

    async function requestJson(url, options) {
        const response = await fetch(url, Object.assign({
            credentials: "same-origin",
            headers: {
                Accept: "application/json",
                "Content-Type": "application/json"
            }
        }, options || {}));

        const contentType = response.headers.get("content-type") || "";
        const payload = contentType.includes("application/json")
            ? await response.json().catch(function (error) { if (response.ok || error?.name === "AbortError" || error?.name === "TimeoutError") throw error; return null; })
            : await response.text().catch(function (error) { if (response.ok || error?.name === "AbortError" || error?.name === "TimeoutError") throw error; return ""; });

        if (!response.ok) {
            const message = typeof payload === "string"
                ? payload
                : trimToEmpty(payload && (payload.message || payload.title));

            throw new Error(message || `Request failed: ${response.status}`);
        }

        return payload;
    }

    function buildMenuHtml() {
        return menuItems.map(function (item) {
            const href = item.guideLink ? state.guideLink : item.href;
            const finalHref = buildSafeHref(href, item.href);
            const attrs = isExternalHref(finalHref) ? ' target="_blank" rel="noreferrer"' : "";

            return [
                `<a class="menu-card${item.guideLink ? " is-guide-link" : ""}" href="${escapeHtml(finalHref)}"${attrs}>`,
                `<span class="menu-card__icon"><ion-icon name="${escapeHtml(item.icon)}"></ion-icon></span>`,
                `<span class="menu-card__label">${escapeHtml(item.label)}</span>`,
                "</a>"
            ].join("");
        }).join("");
    }

    function renderMenus() {
        const html = buildMenuHtml();

        qsa("[data-menu-grid]").forEach(function (node) {
            node.innerHTML = html;
        });

        qsa(".is-guide-link").forEach(function (node) {
            const href = buildSafeHref(state.guideLink, "#tournaments");
            node.setAttribute("href", href);

            if (isExternalHref(href)) {
                node.setAttribute("target", "_blank");
                node.setAttribute("rel", "noreferrer");
            } else {
                node.removeAttribute("target");
                node.removeAttribute("rel");
            }
        });
    }

    function loadingCardMarkup() {
        return [
            '<article class="loading-card">',
            '<div class="loading-card__media"></div>',
            '<div class="loading-card__line loading-card__line--short"></div>',
            '<div class="loading-card__line"></div>',
            '<div class="loading-card__line loading-card__line--medium"></div>',
            "</article>"
        ].join("");
    }

    function renderSkeletons() {
        const tournamentList = qs("[data-tournament-list]");
        const courtList = qs("[data-court-list]");
        const videoList = qs("[data-video-list]");

        if (tournamentList) {
            tournamentList.innerHTML = new Array(3).fill("").map(loadingCardMarkup).join("");
        }

        if (courtList) {
            courtList.innerHTML = new Array(2).fill("").map(loadingCardMarkup).join("");
        }

        if (videoList) {
            videoList.innerHTML = new Array(2).fill("").map(loadingCardMarkup).join("");
        }
    }

    function renderEmptyState(container, message) {
        if (!container) {
            return;
        }

        container.innerHTML = `<div class="empty-state">${escapeHtml(message)}</div>`;
    }

    function pickFeaturedTournament(items) {
        const list = Array.isArray(items) ? items.slice() : [];
        const now = new Date();

        const upcoming = list
            .filter(function (item) {
                const date = parseDate(item.startTime);
                return date && date >= now;
            })
            .sort(function (a, b) {
                return parseDate(a.startTime) - parseDate(b.startTime);
            });

        if (upcoming.length > 0) {
            return upcoming[0];
        }

        return list[0] || null;
    }

    function renderNextTournament(item) {
        if (!item) {
            setText("[data-next-title]", "Chưa có giải đấu nổi bật");
            setText("[data-next-date]", "Chưa có lịch");
            setText("[data-next-status]", "Public");
            setText("[data-next-location]", "Hanaka Sport");
            setText("[data-next-slots]", "00 đội");
            setText("[data-next-matches]", "00 trận");
            return;
        }

        const status = trimToEmpty(item.statusText) || trimToEmpty(item.stateText) || trimToEmpty(item.status) || "Public";
        const location = trimToEmpty(item.locationText) || trimToEmpty(item.areaText) || "Hanaka Sport";

        setText("[data-next-title]", trimToEmpty(item.title) || "Giải đấu Hanaka Sport");
        setText("[data-next-date]", formatDateTime(item.startTime || item.createdAt));
        setText("[data-next-status]", status);
        setText("[data-next-location]", location);
        setText("[data-next-slots]", `${toCount(item.expectedTeams)} đội`);
        setText("[data-next-matches]", `${toCount(item.matchesCount)} trận`);
    }

    function renderHeroBanner(item) {
        const media = qs("[data-hero-banner-media]");
        const title = trimToEmpty(item?.title) || "Hanaka Sport";
        const description = trimToEmpty(item?.title)
            ? "Ảnh nổi bật đang được lấy trực tiếp từ hệ thống."
            : "Ảnh nổi bật sẽ tự động lấy từ hệ thống và hiển thị tại đây.";

        setText("[data-hero-banner-title]", title);
        setText("[data-hero-banner-caption]", description);

        if (media) {
            media.innerHTML = mediaMarkup(item?.imageUrl, title, "Ảnh nổi bật");
        }
    }

    function updateBannerView() {
        const track = qs("[data-banner-track]");
        const current = state.banners[state.bannerIndex] || null;

        if (track) {
            track.style.transform = `translateX(-${state.bannerIndex * 100}%)`;
        }

        qsa("[data-banner-dots] .dot").forEach(function (dot, index) {
            dot.classList.toggle("is-active", index === state.bannerIndex);
        });

        setText("[data-banner-title]", trimToEmpty(current?.title) || "Ảnh nổi bật");
        renderHeroBanner(current);
    }

    function restartBannerTimer() {
        window.clearInterval(state.bannerTimer);

        if (prefersReducedMotion || state.banners.length < 2) {
            return;
        }

        state.bannerTimer = window.setInterval(function () {
            setBannerIndex(state.bannerIndex + 1);
        }, 4500);
    }

    function setBannerIndex(index) {
        if (state.banners.length === 0) {
            return;
        }

        const max = state.banners.length;
        state.bannerIndex = ((index % max) + max) % max;
        updateBannerView();
        restartBannerTimer();
    }

    function renderBanners(items) {
        const list = Array.isArray(items) ? items.filter(Boolean) : [];
        const track = qs("[data-banner-track]");
        const dots = qs("[data-banner-dots]");

        state.banners = list;
        state.bannerIndex = 0;
        setStat("banners", list.length);

        if (!track || !dots) {
            return;
        }

        if (list.length === 0) {
            track.innerHTML = [
                '<article class="banner-slide">',
                '<div class="banner-slide__media media-placeholder"><span>Chưa có ảnh nổi bật từ hệ thống</span></div>',
                '<div class="banner-slide__overlay">',
                "<span>Hanaka Sport</span>",
                "<strong>Ảnh nổi bật sẽ hiển thị khi hệ thống trả dữ liệu.</strong>",
                "</div>",
                "</article>"
            ].join("");

            dots.innerHTML = "";
            renderHeroBanner(null);
            setText("[data-banner-title]", "Ảnh nổi bật");
            return;
        }

        track.innerHTML = list.map(function (item, index) {
            return [
                '<article class="banner-slide">',
                `<div class="banner-slide__media">${mediaMarkup(item.imageUrl, item.title || "Ảnh nổi bật Hanaka Sport", "Ảnh nổi bật Hanaka Sport")}</div>`,
                '<div class="banner-slide__overlay">',
                `<span>Ảnh ${String(index + 1).padStart(2, "0")}</span>`,
                `<strong>${escapeHtml(trimToEmpty(item.title) || "Hanaka Sport")}</strong>`,
                "</div>",
                "</article>"
            ].join("");
        }).join("");

        dots.innerHTML = list.map(function (_, index) {
            return `<button class="dot${index === 0 ? " is-active" : ""}" type="button" aria-label="Chuyển tới ảnh nổi bật ${index + 1}" data-dot-index="${index}"></button>`;
        }).join("");

        updateBannerView();
        restartBannerTimer();
    }

    function renderTournaments(items, total) {
        const container = qs("[data-tournament-list]");
        const list = Array.isArray(items) ? items : [];
        const count = total || list.length;

        setStat("tournaments", count);
        renderNextTournament(pickFeaturedTournament(items));

        if (!container) {
            return;
        }

        if (list.length === 0) {
            renderEmptyState(container, "Chưa có giải đấu public để hiển thị.");
            return;
        }

        container.innerHTML = list.map(function (item) {
            const statusInfo = tournamentStatusInfo(item);
            const registeredCount = toCount(item.registeredCount);
            const expectedTeams = toCount(item.expectedTeams);
            const progress = expectedTeams > 0 ? Math.min(100, Math.round((registeredCount / expectedTeams) * 100)) : 0;
            const location = [trimToEmpty(item.locationText), trimToEmpty(item.areaText)].filter(Boolean).join(" · ") || "Địa điểm đang cập nhật";
            const organizer = trimToEmpty(item.organizer) || "Hanaka Sport";
            const formatText = trimToEmpty(item.formatText) || trimToEmpty(item.playoffType) || "Theo thể lệ giải";
            const teamCountText = expectedTeams > 0 ? `${registeredCount}/${expectedTeams} đội` : `${registeredCount} đội`;
            const detailHref = buildSafeHref(`/PickleballWeb/Tournament/${item.tournamentId}`, "/PickleballWeb/Tournaments");

            return [
                `<a class="data-card tournament-card" href="${escapeHtml(detailHref)}" aria-label="Xem giải ${escapeHtml(trimToEmpty(item.title) || "Hanaka Sport")}">`,
                tournamentMediaMarkup(item, statusInfo),
                '<div class="data-card__body tournament-card__body">',
                '<div class="tournament-card__organizer"><ion-icon name="shield-checkmark-outline"></ion-icon><span>' + escapeHtml(organizer) + "</span></div>",
                `<h3>${escapeHtml(trimToEmpty(item.title) || "Giải đấu Hanaka Sport")}</h3>`,
                `<p class="tournament-card__description">${escapeHtml(tournamentDescription(item))}</p>`,
                '<div class="tournament-card__details">',
                '<div class="tournament-card__detail"><ion-icon name="calendar-outline"></ion-icon><span><small>Ngày thi đấu</small><strong>' + escapeHtml(formatDate(item.startTime || item.createdAt)) + "</strong></span></div>",
                '<div class="tournament-card__detail"><ion-icon name="time-outline"></ion-icon><span><small>Hạn đăng ký</small><strong>' + escapeHtml(formatDate(item.registerDeadline)) + "</strong></span></div>",
                '<div class="tournament-card__detail is-wide"><ion-icon name="location-outline"></ion-icon><span><small>Địa điểm</small><strong>' + escapeHtml(location) + "</strong></span></div>",
                "</div>",
                '<div class="tournament-card__tags">',
                '<span><ion-icon name="tennisball-outline"></ion-icon>' + escapeHtml(tournamentTypeText(item)) + "</span>",
                '<span><ion-icon name="layers-outline"></ion-icon>' + escapeHtml(formatText) + "</span>",
                "</div>",
                '<div class="tournament-card__capacity">',
                '<div class="tournament-card__capacity-row"><span>Đội đăng ký</span><strong>' + escapeHtml(teamCountText) + "</strong></div>",
                '<div class="tournament-card__progress" aria-hidden="true"><span style="width:' + progress + '%"></span></div>',
                "</div>",
                '<div class="tournament-card__footer">',
                '<div class="tournament-card__fee"><small>Phí đăng ký</small><strong>' + escapeHtml(formatTournamentFee(item.registrationFeeAmount, item.registrationFeeCurrency)) + "</strong></div>",
                '<span class="tournament-card__action ' + (statusInfo.canRegister ? "is-register" : "") + '"><span>' + (statusInfo.canRegister ? "Đăng ký ngay" : "Xem chi tiết") + '</span><ion-icon name="arrow-forward-outline"></ion-icon></span>',
                "</div>",
                "</div>",
                "</a>"
            ].join("");
        }).join("");

        bindTournamentImageFallbacks(container);
    }

    function renderCourts(items, total) {
        const container = qs("[data-court-list]");
        const list = Array.isArray(items) ? items.slice(0, 4) : [];
        const count = total || list.length;

        setStat("courts", count);

        if (!container) {
            return;
        }

        if (list.length === 0) {
            renderEmptyState(container, "Chưa có sân bãi public để hiển thị.");
            return;
        }

        container.innerHTML = list.map(function (item) {
            const images = Array.isArray(item.images) ? item.images.filter(Boolean).slice(0, 2) : [];
            while (images.length < 2) {
                images.push("");
            }

            const area = trimToEmpty(item.areaText) || "Chưa cập nhật khu vực";
            const manager = trimToEmpty(item.managerName) || "Hanaka Sport";
            const phone = trimToEmpty(item.phone);

            return [
                `<a class="data-card court-card" href="${escapeHtml(buildSafeHref(`/PickleballWeb/Court/${item.courtId}`, "/PickleballWeb/Courts"))}">`,
                '<div class="court-card__images">',
                images.map(function (imageUrl, index) {
                    if (imageUrl) {
                        return `<div class="court-thumb"><img src="${escapeHtml(imageUrl)}" alt="${escapeHtml((item.courtName || "Sân") + " " + (index + 1))}" loading="lazy"></div>`;
                    }

                    return '<div class="court-thumb court-thumb--fallback">Ảnh sân</div>';
                }).join(""),
                "</div>",
                '<div class="data-card__body">',
                `<h3>${escapeHtml(trimToEmpty(item.courtName) || "Sân Hanaka Sport")}</h3>`,
                `<p>${escapeHtml(area)}</p>`,
                '<div class="inline-meta">',
                `<span>${escapeHtml(manager)}</span>`,
                `<span>${escapeHtml(phone || "Liên hệ trong ứng dụng")}</span>`,
                "</div>",
                "</div>",
                "</a>"
            ].join("");
        }).join("");
    }

    function renderVideos(items, total) {
        const container = qs("[data-video-list]");
        const list = Array.isArray(items) ? items.slice(0, 3) : [];
        const count = total || list.length;

        setStat("videos", count);

        if (!container) {
            return;
        }

        if (list.length === 0) {
            renderEmptyState(container, "Chưa có video trận đấu để hiển thị.");
            return;
        }

        container.innerHTML = list.map(function (item) {
            const team1Name = trimToEmpty(item.team1Name) || trimToEmpty(item.team1Player1Name) || "Đội 1";
            const team2Name = trimToEmpty(item.team2Name) || trimToEmpty(item.team2Player1Name) || "Đội 2";
            const status = trimToEmpty(item.roundLabel) || trimToEmpty(item.groupName) || "Video trận đấu";
            const meta = [formatDateTime(item.startAt || item.createdAt), trimToEmpty(item.courtText)]
                .filter(Boolean)
                .join(" · ");
            const href = buildSafeHref(item.videoUrl, "#videos");
            const attrs = href !== "#videos" && isExternalHref(href) ? ' target="_blank" rel="noreferrer"' : "";

            return [
                '<article class="data-card video-card">',
                `<div class="data-card__media">${mediaMarkup(item.tournamentBannerUrl, item.tournamentTitle || "Video Hanaka Sport", "Video trận đấu")}</div>`,
                '<div class="data-card__body">',
                '<div class="meta-row">',
                `<span class="badge">${escapeHtml(status)}</span>`,
                `<span class="muted">${escapeHtml(meta || "Hanaka Sport")}</span>`,
                "</div>",
                `<h3>${escapeHtml(trimToEmpty(item.tournamentTitle) || "Trận đấu Hanaka Sport")}</h3>`,
                '<div class="team-stack">',
                '<div class="team-row">',
                avatarMarkup(team1Name, item.team1Player1Avatar || item.team1Player2Avatar),
                `<span class="team-name">${escapeHtml(team1Name)}</span>`,
                `<span class="team-score">${escapeHtml(String(toCount(item.scoreTeam1)))}</span>`,
                "</div>",
                '<div class="team-row">',
                avatarMarkup(team2Name, item.team2Player1Avatar || item.team2Player2Avatar),
                `<span class="team-name">${escapeHtml(team2Name)}</span>`,
                `<span class="team-score">${escapeHtml(String(toCount(item.scoreTeam2)))}</span>`,
                "</div>",
                "</div>",
                href !== "#videos"
                    ? `<a class="video-link" href="${escapeHtml(href)}"${attrs}><ion-icon name="play-circle-outline"></ion-icon><span>Mở video</span></a>`
                    : '<span class="video-link"><ion-icon name="play-circle-outline"></ion-icon><span>Video sẽ được cập nhật</span></span>',
                "</div>",
                "</article>"
            ].join("");
        }).join("");
    }

    function renderLinks(items) {
        const section = qs("[data-links-section]");
        const container = qs("[data-link-stack]");
        const links = Array.isArray(items) ? items : [];
        const youtube = links.find(function (item) {
            return trimToEmpty(item.type).toLowerCase() === "youtube";
        });
        const zalo = links.find(function (item) {
            return trimToEmpty(item.type).toLowerCase() === "zalo";
        });

        state.guideLink = buildSafeHref(youtube?.link, "#tournaments");
        renderMenus();

        const cards = [];

        if (youtube?.link) {
            cards.push({
                icon: "logo-youtube",
                title: "Video hướng dẫn",
                subtitle: "Mở nhanh phần hướng dẫn đang liên kết từ ứng dụng.",
                href: youtube.link
            });
        }

        if (zalo?.link) {
            cards.push({
                icon: "people-circle-outline",
                title: "Nhóm cộng đồng",
                subtitle: "Tham gia kết nối với người chơi trong hệ thống.",
                href: zalo.link
            });
        }

        if (!section || !container) {
            return;
        }

        if (cards.length === 0) {
            section.hidden = true;
            container.innerHTML = "";
            return;
        }

        section.hidden = false;
        container.innerHTML = cards.map(function (card) {
            const href = buildSafeHref(card.href, "#tournaments");
            const attrs = isExternalHref(href) ? ' target="_blank" rel="noreferrer"' : "";

            return [
                `<a class="link-tile" href="${escapeHtml(href)}"${attrs}>`,
                '<div class="link-tile__copy">',
                `<strong>${escapeHtml(card.title)}</strong>`,
                `<span>${escapeHtml(card.subtitle)}</span>`,
                "</div>",
                `<ion-icon name="${escapeHtml(card.icon)}"></ion-icon>`,
                "</a>"
            ].join("");
        }).join("");
    }

    function bindBannerControls() {
        const prev = qs("[data-banner-prev]");
        const next = qs("[data-banner-next]");
        const stage = qs("[data-banner-stage]");

        if (prev) {
            prev.addEventListener("click", function () {
                setBannerIndex(state.bannerIndex - 1);
            });
        }

        if (next) {
            next.addEventListener("click", function () {
                setBannerIndex(state.bannerIndex + 1);
            });
        }

        document.addEventListener("click", function (event) {
            const dot = event.target.closest("[data-dot-index]");
            if (!dot) {
                return;
            }

            const index = Number(dot.getAttribute("data-dot-index"));
            if (Number.isFinite(index)) {
                setBannerIndex(index);
            }
        });

        if (stage && !prefersReducedMotion) {
            stage.addEventListener("mouseenter", function () {
                window.clearInterval(state.bannerTimer);
            });

            stage.addEventListener("mouseleave", function () {
                restartBannerTimer();
            });
        }
    }

    function initReveal() {
        const items = qsa(".reveal");
        if (items.length === 0) {
            return;
        }

        if (prefersReducedMotion || !("IntersectionObserver" in window)) {
            items.forEach(function (item) {
                item.classList.add("is-visible");
            });
            return;
        }

        const observer = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    entry.target.classList.add("is-visible");
                    observer.unobserve(entry.target);
                }
            });
        }, {
            threshold: 0.16,
            rootMargin: "0px 0px -10% 0px"
        });

        items.forEach(function (item) {
            observer.observe(item);
        });
    }

    function initTabBar() {
        const links = qsa("[data-tab-link]");
        if (links.length === 0) {
            return;
        }

        const sectionToKey = {
            "home-feed": "home",
            "tournaments": "tournaments",
            "courts": "courts"
        };

        function setActive(key) {
            links.forEach(function (link) {
                link.classList.toggle("is-active", link.getAttribute("data-tab-link") === key);
            });
        }

        links.forEach(function (link) {
            link.addEventListener("click", function () {
                setActive(link.getAttribute("data-tab-link"));
            });
        });

        if (!("IntersectionObserver" in window)) {
            return;
        }

        const observer = new IntersectionObserver(function (entries) {
            const visible = entries
                .filter(function (entry) { return entry.isIntersecting; })
                .sort(function (a, b) { return b.intersectionRatio - a.intersectionRatio; })[0];

            if (!visible) {
                return;
            }

            const key = sectionToKey[visible.target.id];
            if (key) {
                setActive(key);
            }
        }, {
            threshold: 0.42
        });

        Object.keys(sectionToKey).forEach(function (id) {
            const node = document.getElementById(id);
            if (node) {
                observer.observe(node);
            }
        });
    }

    function updateAuthEntry() {
        const avatarLink = qs(".app-bar__actions .avatar-icon");
        const actionLinks = qsa(".app-bar__actions .round-icon");
        const notificationLink = actionLinks.length > 0 ? actionLinks[0] : null;
        const settingsLink = actionLinks.length > 1 ? actionLinks[1] : null;
        const session = state.session;
        const isAuthenticated = !!(session && session.isAuthenticated && session.user);

        function setAvatarFallback() {
            if (!avatarLink) {
                return;
            }

            avatarLink.classList.remove("has-image");
            avatarLink.innerHTML = '<ion-icon name="person-circle-outline"></ion-icon>';
        }

        function setAvatarImage(url, altText) {
            if (!avatarLink) {
                return;
            }

            var src = normalizeAvatarUrl(url);
            if (!src) {
                setAvatarFallback();
                return;
            }

            var img = document.createElement("img");
            img.className = "avatar-icon__image";
            img.alt = trimToEmpty(altText) || "Tài khoản";
            img.src = src;
            img.addEventListener("error", function () {
                setAvatarFallback();
            }, { once: true });

            avatarLink.classList.add("has-image");
            avatarLink.replaceChildren(img);
        }

        if (avatarLink) {
            if (isAuthenticated) {
                avatarLink.href = "/PickleballWeb/Account";
                avatarLink.setAttribute("aria-label", trimToEmpty(session.user.fullName) || "Tài khoản");
                avatarLink.title = trimToEmpty(session.user.fullName) || "Tài khoản";
                setAvatarImage(session.user.avatarUrl, session.user.fullName);
            } else if (session && session.isAuthenticated === false) {
                avatarLink.href = "/PickleballWeb/Login?returnUrl=" + encodeURIComponent("/PickleballWeb/Account");
                avatarLink.setAttribute("aria-label", "Đăng nhập");
                avatarLink.title = "Đăng nhập";
                setAvatarFallback();
            } else {
                avatarLink.href = "/PickleballWeb/Account";
                avatarLink.setAttribute("aria-label", "Tài khoản");
                avatarLink.title = "Tài khoản";
                setAvatarFallback();
            }
        }

        if (notificationLink) {
            notificationLink.hidden = false;
            notificationLink.href = "/PickleballWeb/Notifications";
            notificationLink.setAttribute("aria-label", "Thông báo");
            notificationLink.title = "Thông báo";
        }

        if (settingsLink) {
            settingsLink.hidden = false;
            settingsLink.href = "/PickleballWeb/Settings";
            settingsLink.setAttribute("aria-label", "Cai dat");
            settingsLink.title = "Cai dat";
        }
    }

    async function loadAuthSession() {
        try {
            const payload = await window.HanakaWebSession.read({
                hanakaLoading: "silent"
            });

            state.session = payload;
        } catch (error) {
            // Keep the last confirmed identity; a failed read does not sign the user out.
            if (window.HanakaWebSession.isAborted(error)) return;
        }

        updateAuthEntry();
    }

    function bindAuthActions() {
        updateAuthEntry();
    }

    async function loadLandingPage() {
        const results = await Promise.allSettled([
            fetchJson("/api/public/banners"),
            fetchJson("/api/public/tournaments?page=1&pageSize=6"),
            fetchJson("/api/public/courts?page=1&pageSize=6"),
            fetchJson("/api/videos/videos?tab=suggested&page=1&pageSize=4"),
            fetchJson("/api/links")
        ]);

        const bannersPayload = results[0].status === "fulfilled" ? results[0].value : null;
        const tournamentsPayload = results[1].status === "fulfilled" ? results[1].value : null;
        const courtsPayload = results[2].status === "fulfilled" ? results[2].value : null;
        const videosPayload = results[3].status === "fulfilled" ? results[3].value : null;
        const linksPayload = results[4].status === "fulfilled" ? results[4].value : null;

        const banners = Array.isArray(bannersPayload?.items) ? bannersPayload.items : [];
        const tournaments = Array.isArray(tournamentsPayload?.items) ? tournamentsPayload.items : [];
        const courts = Array.isArray(courtsPayload?.items) ? courtsPayload.items : [];
        const videos = Array.isArray(videosPayload?.items) ? videosPayload.items : [];
        const links = Array.isArray(linksPayload?.items) ? linksPayload.items : [];

        renderLinks(links);
        renderBanners(banners);
        renderTournaments(tournaments, tournamentsPayload?.total);
        renderCourts(courts, courtsPayload?.total);
        renderVideos(videos, videosPayload?.total);
    }

    document.addEventListener("DOMContentLoaded", function () {
        if (!qs(".landing-app")) {
            return;
        }

        setStat("banners", 0);
        setStat("tournaments", 0);
        setStat("courts", 0);
        setStat("videos", 0);
        renderMenus();
        renderSkeletons();
        renderHeroBanner(null);
        renderNextTournament(null);
        bindBannerControls();
        bindAuthActions();
        initReveal();
        initTabBar();

        Promise.allSettled([
            loadLandingPage(),
            loadAuthSession()
        ]).then(function (results) {
            if (results[0].status !== "fulfilled") {
                renderLinks([]);
                renderBanners([]);
                renderTournaments([], 0);
                renderCourts([], 0);
                renderVideos([], 0);
            }
        });
    });
})();
