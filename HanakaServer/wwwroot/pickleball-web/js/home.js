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
        guideLink: "#tournaments"
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
            cache: "no-store"
        });

        if (!response.ok) {
            throw new Error(`Request failed: ${response.status}`);
        }

        return response.json();
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
            ? "Banner nổi bật đang được lấy trực tiếp từ hệ thống."
            : "Banner sẽ tự động lấy từ hệ thống và hiển thị tại đây.";

        setText("[data-hero-banner-title]", title);
        setText("[data-hero-banner-caption]", description);

        if (media) {
            media.innerHTML = mediaMarkup(item?.imageUrl, title, "Banner nổi bật");
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

        setText("[data-banner-title]", trimToEmpty(current?.title) || "Banner nổi bật");
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
                '<div class="banner-slide__media media-placeholder"><span>Chưa có banner từ hệ thống</span></div>',
                '<div class="banner-slide__overlay">',
                "<span>Hanaka Sport</span>",
                "<strong>Banner sẽ hiển thị khi API public trả dữ liệu.</strong>",
                "</div>",
                "</article>"
            ].join("");

            dots.innerHTML = "";
            renderHeroBanner(null);
            setText("[data-banner-title]", "Banner nổi bật");
            return;
        }

        track.innerHTML = list.map(function (item, index) {
            return [
                '<article class="banner-slide">',
                `<div class="banner-slide__media">${mediaMarkup(item.imageUrl, item.title || "Banner Hanaka Sport", "Banner Hanaka Sport")}</div>`,
                '<div class="banner-slide__overlay">',
                `<span>Banner ${String(index + 1).padStart(2, "0")}</span>`,
                `<strong>${escapeHtml(trimToEmpty(item.title) || "Hanaka Sport")}</strong>`,
                "</div>",
                "</article>"
            ].join("");
        }).join("");

        dots.innerHTML = list.map(function (_, index) {
            return `<button class="dot${index === 0 ? " is-active" : ""}" type="button" aria-label="Chuyển tới banner ${index + 1}" data-dot-index="${index}"></button>`;
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
            const status = trimToEmpty(item.statusText) || trimToEmpty(item.stateText) || trimToEmpty(item.status) || "Public";
            const dateText = formatDate(item.startTime || item.createdAt);
            const location = [trimToEmpty(item.locationText), trimToEmpty(item.areaText)].filter(Boolean).join(" · ") || "Hanaka Sport";
            const description = trimToEmpty(item.content) || `Tối đa ${toCount(item.expectedTeams)} đội và ${toCount(item.matchesCount)} trận.`;

            return [
                '<article class="data-card tournament-card">',
                `<div class="data-card__media">${mediaMarkup(item.bannerUrl, item.title || "Giải đấu Hanaka Sport", "Giải đấu Hanaka Sport")}</div>`,
                '<div class="data-card__body">',
                '<div class="meta-row">',
                `<span class="badge">${escapeHtml(status)}</span>`,
                `<span class="muted">${escapeHtml(dateText)}</span>`,
                "</div>",
                `<h3>${escapeHtml(trimToEmpty(item.title) || "Giải đấu Hanaka Sport")}</h3>`,
                `<p>${escapeHtml(description)}</p>`,
                '<div class="inline-meta">',
                `<span>${escapeHtml(location)}</span>`,
                `<span>${escapeHtml(`${toCount(item.expectedTeams)} đội`)}</span>`,
                "</div>",
                "</div>",
                "</article>"
            ].join("");
        }).join("");
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
                '<article class="data-card court-card">',
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
                `<span>${escapeHtml(phone || "Liên hệ tại app")}</span>`,
                "</div>",
                "</div>",
                "</article>"
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
            const status = trimToEmpty(item.roundLabel) || trimToEmpty(item.groupName) || "Match video";
            const meta = [formatDateTime(item.startAt || item.createdAt), trimToEmpty(item.courtText)]
                .filter(Boolean)
                .join(" · ");
            const href = buildSafeHref(item.videoUrl, "#videos");
            const attrs = href !== "#videos" && isExternalHref(href) ? ' target="_blank" rel="noreferrer"' : "";

            return [
                '<article class="data-card video-card">',
                `<div class="data-card__media">${mediaMarkup(item.tournamentBannerUrl, item.tournamentTitle || "Video Hanaka Sport", "Match video")}</div>`,
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
        initReveal();
        initTabBar();

        loadLandingPage().catch(function () {
            renderLinks([]);
            renderBanners([]);
            renderTournaments([], 0);
            renderCourts([], 0);
            renderVideos([], 0);
        });
    });
})();
