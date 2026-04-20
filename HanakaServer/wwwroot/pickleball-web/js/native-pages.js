(function () {
    function qs(selector, root) {
        return (root || document).querySelector(selector);
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

    function buildSafeHref(value, fallback) {
        var href = trimToEmpty(value);

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
            /^tel:/i.test(href) ||
            /^sms:/i.test(href)
        ) {
            return href;
        }

        return href;
    }

    function isExternalHref(href) {
        return /^(https?:\/\/|mailto:|tel:|sms:)/i.test(trimToEmpty(href));
    }

    function normalizeExternalHref(value) {
        var href = trimToEmpty(value);

        if (!href) {
            return "";
        }

        if (/^(https?:\/\/|mailto:|tel:|sms:)/i.test(href)) {
            return href;
        }

        return "https://" + href.replace(/^\/+/, "");
    }

    function normalizeMediaUrl(value) {
        var url = trimToEmpty(value);

        if (!url) {
            return "";
        }

        if (url.startsWith("/")) {
            return window.location.origin + url;
        }

        try {
            var parsed = new URL(url, window.location.origin);
            if (parsed.pathname.startsWith("/uploads/") && parsed.origin !== window.location.origin) {
                return window.location.origin + parsed.pathname + parsed.search;
            }

            return parsed.toString();
        } catch (_error) {
            return url;
        }
    }

    async function fetchJson(url) {
        var response = await fetch(url, {
            headers: { Accept: "application/json" },
            cache: "no-store"
        });

        if (!response.ok) {
            throw new Error("Request failed: " + response.status);
        }

        return response.json();
    }

    function parseDate(value) {
        if (!value) {
            return null;
        }

        var date = new Date(value);
        return Number.isNaN(date.getTime()) ? null : date;
    }

    function pad2(value) {
        return String(value).padStart(2, "0");
    }

    function formatDateTime(value) {
        var date = parseDate(value);
        if (!date) {
            return "";
        }

        return pad2(date.getDate()) + "/" + pad2(date.getMonth() + 1) + "/" + date.getFullYear() + " " +
            pad2(date.getHours()) + ":" + pad2(date.getMinutes());
    }

    function formatDateOnly(value) {
        var date = parseDate(value);
        if (!date) {
            return "";
        }

        return pad2(date.getDate()) + "/" + pad2(date.getMonth() + 1) + "/" + date.getFullYear();
    }

    function formatUpdatedTime(value) {
        var date = parseDate(value);
        if (!date) {
            return "Chua cap nhat";
        }

        return new Intl.DateTimeFormat("vi-VN", {
            day: "2-digit",
            month: "2-digit",
            year: "numeric",
            hour: "2-digit",
            minute: "2-digit"
        }).format(date);
    }

    function formatScore(value) {
        var number = Number(value || 0);
        if (!Number.isFinite(number)) {
            return "0";
        }

        return number % 1 === 0
            ? String(number)
            : number.toFixed(2).replace(/\.?0+$/, "");
    }

    function formatMemberScore(value) {
        var number = Number(value);
        return Number.isFinite(number) ? number.toFixed(2) : "0.00";
    }

    function ratingStars(value) {
        var full = Math.round(Number(value || 0));
        var html = [];

        for (var i = 0; i < 5; i += 1) {
            html.push('<ion-icon name="' + (i < full ? "star" : "star-outline") + '"></ion-icon>');
        }

        return html.join("");
    }

    function setupInfiniteObserver(sentinel, callback) {
        if (!sentinel || !("IntersectionObserver" in window)) {
            return null;
        }

        var observer = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    callback();
                }
            });
        }, {
            rootMargin: "180px 0px"
        });

        observer.observe(sentinel);
        return observer;
    }

    function setHeaderAction(root, options) {
        var action = qs("[data-native-page-action]", root);
        var spacer = qs("[data-native-page-spacer]", root);

        if (!action || !spacer) {
            return;
        }

        if (!options) {
            action.hidden = true;
            action.innerHTML = "";
            action.onclick = null;
            spacer.hidden = false;
            return;
        }

        action.hidden = false;
        action.innerHTML = options.html || "";
        action.onclick = options.onClick || null;
        spacer.hidden = true;
    }

    function setHeaderTitle(root, title) {
        var titleNode = qs(".native-page-header__title", root);
        if (titleNode) {
            titleNode.textContent = title;
        }
    }

    function setHeaderExtra(root, html) {
        var target = qs("[data-native-page-header-extra]", root);
        if (target) {
            target.innerHTML = html || "";
        }
    }

    function showAppOnlyAlert(message) {
        window.alert(message);
    }

    function getCommonRefs(root) {
        return {
            list: qs("[data-native-page-list]", root),
            loading: qs("[data-native-page-loading]", root),
            loadingMore: qs("[data-native-page-loading-more]", root),
            empty: qs("[data-native-page-empty]", root),
            emptyText: qs("[data-native-page-empty-text]", root),
            error: qs("[data-native-page-error]", root),
            errorText: qs("[data-native-page-error-text]", root),
            retry: qs("[data-native-page-retry]", root),
            sentinel: qs("[data-native-page-sentinel]", root)
        };
    }

    function renderEmptyState(refs, message) {
        if (refs.emptyText) {
            refs.emptyText.textContent = message;
        }
    }

    function toggleCommonState(refs, state) {
        if (!refs) {
            return;
        }

        if (refs.loading) {
            refs.loading.hidden = !(state.loading && state.itemsLength === 0);
        }

        if (refs.loadingMore) {
            refs.loadingMore.hidden = !(state.loading && state.itemsLength > 0);
        }

        if (refs.error) {
            refs.error.hidden = !state.error;
        }

        if (refs.errorText) {
            refs.errorText.textContent = state.error || "";
        }

        if (refs.retry) {
            refs.retry.hidden = !state.error;
        }

        if (refs.empty) {
            refs.empty.hidden = !!state.error || state.loading || state.itemsLength > 0;
        }

        if (refs.sentinel) {
            refs.sentinel.hidden = state.loading || !state.hasMore;
        }
    }

    function renderClubButton(item) {
        var status = trimToEmpty(item.myClubStatus).toUpperCase();

        if (status === "MANAGER") {
            return '<a class="native-club-card__button native-club-card__button--green" href="/PickleballWeb/Club/' + escapeHtml(item.clubId) + '">Quan ly</a>';
        }

        if (status === "MEMBER") {
            return '<span class="native-club-card__button native-club-card__button--green is-disabled">Thanh vien</span>';
        }

        if (status === "PENDING") {
            return '<span class="native-club-card__button native-club-card__button--amber is-disabled">Cho duyet</span>';
        }

        return '<button class="native-club-card__button native-club-card__button--red" type="button" data-club-join="' + escapeHtml(item.clubId) + '">Xin vao</button>';
    }

    function renderClubCard(item) {
        var coverUrl = trimToEmpty(item.coverUrl);
        var membersCount = Number(item.membersCount || 0);
        var areaText = trimToEmpty(item.areaText) || "Chua co khu vuc";
        var ratingAvg = Number(item.ratingAvg || 0);

        return [
            '<article class="native-club-card">',
            coverUrl
                ? '<img class="native-club-card__cover" src="' + escapeHtml(coverUrl) + '" alt="' + escapeHtml(item.clubName || "CLB") + '" loading="lazy">'
                : '<div class="native-club-card__cover native-club-card__cover--fallback"><ion-icon name="image-outline"></ion-icon></div>',
            '<div class="native-club-card__body">',
            '<h2 class="native-club-card__title">' + escapeHtml(trimToEmpty(item.clubName) || "CLB Hanaka") + (membersCount > 0 ? " (" + membersCount + " tv)" : "") + "</h2>",
            '<div class="native-club-card__rating"><span>' + ratingAvg.toFixed(1) + "</span><span class=\"native-club-card__stars\">" + ratingStars(ratingAvg) + '</span><span>(' + escapeHtml(item.reviewsCount || 0) + ' Danh gia)</span></div>',
            '<p class="native-club-card__meta">Khu vuc: ' + escapeHtml(areaText) + "</p>",
            '<div class="native-club-card__stats">',
            '<span>Tran: ' + escapeHtml(item.matchesPlayed || 0) + "</span>",
            '<span>Thang: ' + escapeHtml(item.matchesWin || 0) + "</span>",
            '<span>Hoa: ' + escapeHtml(item.matchesDraw || 0) + "</span>",
            '<span>Thua: ' + escapeHtml(item.matchesLoss || 0) + "</span>",
            "</div>",
            '<div class="native-club-card__actions">',
            renderClubButton(item),
            '<a class="native-club-card__button native-club-card__button--cyan" href="/PickleballWeb/Club/' + escapeHtml(item.clubId) + '">Xem chi tiet</a>',
            "</div>",
            "</div>",
            "</article>"
        ].join("");
    }

    function renderTableRow(item, index, options) {
        var avatarUrl = normalizeMediaUrl(item.avatarUrl);
        var mine = !!item.isMine;
        var badgeClass = options.kind === "referee" ? "native-table-row__badge native-table-row__badge--soft" : "native-table-row__badge";

        return [
            '<a class="native-table-row' + (mine ? " is-mine" : "") + '" href="' + escapeHtml(buildSafeHref(options.detailHref(item), "#")) + '">',
            '<div class="native-table-row__stt' + (mine ? " is-mine" : "") + '">' + escapeHtml(index + 1) + "</div>",
            avatarUrl
                ? '<span class="native-table-row__avatar"><img src="' + escapeHtml(avatarUrl) + '" alt="' + escapeHtml(item.fullName || options.emptyName) + '" loading="lazy"></span>'
                : '<span class="native-table-row__avatar native-table-row__avatar--fallback"><ion-icon name="person-outline"></ion-icon></span>',
            '<div class="native-table-row__mid">',
            '<div class="native-table-row__namewrap">',
            '<strong>' + escapeHtml(trimToEmpty(item.fullName) || options.emptyName) + "</strong>",
            mine ? '<span class="' + badgeClass + '">Toi</span>' : "",
            "</div>",
            '<span class="native-table-row__city">' + escapeHtml(trimToEmpty(item.city) || "Chua cap nhat") + "</span>",
            '<span class="native-table-row__status ' + (item.verified ? "is-good" : "is-bad") + '">' + (item.verified ? "Da xac thuc" : "Chua xac thuc") + "</span>",
            "</div>",
            '<div class="native-table-row__scores">',
            '<span class="native-table-row__scorebox">' + escapeHtml(formatScore(options.singleValue(item))) + "</span>",
            '<span class="native-table-row__scorebox">' + escapeHtml(formatScore(options.doubleValue(item))) + "</span>",
            "</div>",
            "</a>"
        ].join("");
    }

    function renderCourtCard(item) {
        var images = Array.isArray(item.images) ? item.images : [];
        var image1 = trimToEmpty(images[0]);
        var image2 = trimToEmpty(images[1]) || image1;

        function imageMarkup(url) {
            return url
                ? '<img class="native-court-card__image" src="' + escapeHtml(url) + '" alt="' + escapeHtml(item.courtName || "San") + '" loading="lazy">'
                : '<div class="native-court-card__image native-court-card__image--fallback"><ion-icon name="image-outline"></ion-icon></div>';
        }

        return [
            '<article class="native-court-card">',
            '<a class="native-court-card__images" href="/PickleballWeb/Court/' + escapeHtml(item.courtId) + '">',
            imageMarkup(image1),
            imageMarkup(image2),
            "</a>",
            '<div class="native-court-card__body">',
            '<div class="native-court-card__left">',
            '<h2>' + escapeHtml(trimToEmpty(item.courtName) || "San Hanaka") + "</h2>",
            '<p>Khu vuc: <strong>' + escapeHtml(trimToEmpty(item.areaText) || "Chua cap nhat") + "</strong></p>",
            '<p>Quan ly: <strong>' + escapeHtml(trimToEmpty(item.managerName) || "Chua cap nhat") + "</strong></p>",
            '<p>Dien thoai: <strong>' + escapeHtml(trimToEmpty(item.phone) || "Chua cap nhat") + "</strong></p>",
            "</div>",
            '<div class="native-court-card__actions">',
            '<a class="native-court-card__action" href="' + escapeHtml(buildSafeHref(item.phone ? "tel:" + item.phone : "#", "#")) + '"><ion-icon name="call"></ion-icon></a>',
            '<a class="native-court-card__action" href="' + escapeHtml(buildSafeHref(item.phone ? "sms:" + item.phone : "#", "#")) + '"><ion-icon name="chatbubble"></ion-icon></a>',
            "</div>",
            "</div>",
            "</article>"
        ].join("");
    }

    function tournamentStatusMap(status) {
        var normalized = trimToEmpty(status).toUpperCase();

        if (normalized === "OPEN") {
            return { text: "Dang mo dang ky", className: "is-open" };
        }

        if (normalized === "CLOSED") {
            return { text: "Da dong dang ky", className: "is-closed" };
        }

        if (normalized === "FINISHED") {
            return { text: "Da ket thuc", className: "is-finished" };
        }

        return { text: normalized || "Khong xac dinh", className: "is-draft" };
    }

    function renderTournamentCard(item) {
        var bannerUrl = trimToEmpty(item.bannerUrl);
        var gameTypeLabel = item.gameType === "DOUBLE"
            ? "Doi"
            : item.gameType === "SINGLE"
                ? "Don"
                : item.gameType === "MIXED"
                    ? "Doi hon hop"
                    : trimToEmpty(item.gameType) || "-";

        return [
            '<article class="native-tournament-card">',
            bannerUrl
                ? '<img class="native-tournament-card__banner" src="' + escapeHtml(bannerUrl) + '" alt="' + escapeHtml(item.title || "Giai dau") + '" loading="lazy">'
                : '<div class="native-tournament-card__banner native-tournament-card__banner--fallback"><ion-icon name="image-outline"></ion-icon></div>',
            '<a class="native-tournament-card__body" href="/PickleballWeb/Tournament/' + escapeHtml(item.tournamentId) + '">',
            '<h2>' + escapeHtml(trimToEmpty(item.title) || "Giai dau Hanaka") + "</h2>",
            '<p>Ngay: <strong>' + escapeHtml(formatDateTime(item.startTime) || "-") + "</strong></p>",
            '<p>Han dang ky: <strong>' + escapeHtml(formatDateTime(item.registerDeadline) || "-") + "</strong></p>",
            '<div class="native-tournament-card__split"><p>The thuc: <strong>' + escapeHtml(trimToEmpty(item.formatText) || "-") + "</strong></p><p>Giai: <strong>" + escapeHtml(gameTypeLabel) + "</strong></p></div>",
            '<div class="native-tournament-card__split"><p>Gioi han trinh don toi da: <strong>' + escapeHtml(item.singleLimit ?? 0) + "</strong></p><p>Cap toi da: <strong>" + escapeHtml(item.doubleLimit ?? 0) + "</strong></p></div>",
            '<p>Khu vuc: <strong>' + escapeHtml(trimToEmpty(item.areaText) || "-") + "</strong></p>",
            '<div class="native-tournament-card__split"><p>So doi du kien: <strong>' + escapeHtml(item.expectedTeams ?? 0) + "</strong></p><p>So tran thi dau: <strong>" + escapeHtml(item.matchesCount ?? 0) + "</strong></p></div>",
            '<p>Tinh trang: <strong>' + escapeHtml(trimToEmpty(item.stateText) || trimToEmpty(item.statusText) || trimToEmpty(item.status) || "-") + "</strong></p>",
            "</a>",
            "</article>"
        ].join("");
    }

    function renderChallengeClubCard(item) {
        var coverUrl = trimToEmpty(item.coverUrl);

        return [
            '<article class="native-exchange-card">',
            '<a href="/PickleballWeb/Club/' + escapeHtml(item.clubId) + '">',
            coverUrl
                ? '<img class="native-exchange-card__cover" src="' + escapeHtml(coverUrl) + '" alt="' + escapeHtml(item.clubName || "CLB") + '" loading="lazy">'
                : '<div class="native-exchange-card__cover native-exchange-card__cover--fallback"><ion-icon name="image-outline"></ion-icon></div>',
            "</a>",
            '<div class="native-exchange-card__body">',
            '<div class="native-exchange-card__badge"><ion-icon name="flash-outline"></ion-icon><span>Dang khieu chien</span></div>',
            '<h2>' + escapeHtml(trimToEmpty(item.clubName) || "Cau lac bo") + "</h2>",
            '<p><ion-icon name="location-outline"></ion-icon><span>' + escapeHtml(trimToEmpty(item.areaText) || "Chua co khu vuc") + "</span></p>",
            '<p><ion-icon name="people-outline"></ion-icon><span>' + escapeHtml(item.membersCount || 0) + ' thanh vien</span></p>',
            '<p><ion-icon name="time-outline"></ion-icon><span>Cap nhat: ' + escapeHtml(formatUpdatedTime(item.challengeUpdatedAt || item.updatedAt || item.createdAt)) + "</span></p>",
            '<div class="native-exchange-card__stats">',
            '<div><strong>' + escapeHtml(item.matchesPlayed || 0) + '</strong><span>Tran</span></div>',
            '<div><strong>' + escapeHtml(item.matchesWin || 0) + '</strong><span>Thang</span></div>',
            '<div><strong>' + escapeHtml(item.matchesDraw || 0) + '</strong><span>Hoa</span></div>',
            '<div><strong>' + escapeHtml(item.matchesLoss || 0) + '</strong><span>Thua</span></div>',
            "</div>",
            '<a class="native-exchange-card__detail" href="/PickleballWeb/Club/' + escapeHtml(item.clubId) + '">Xem chi tiet</a>',
            "</div>",
            "</article>"
        ].join("");
    }

    function renderMatchTournamentCard(item) {
        var statusInfo = tournamentStatusMap(item.status);
        var bannerUrl = trimToEmpty(item.bannerUrl);
        var location = [trimToEmpty(item.locationText), trimToEmpty(item.areaText)].filter(Boolean).join(" • ");

        return [
            '<article class="native-match-card">',
            bannerUrl
                ? '<img class="native-match-card__banner" src="' + escapeHtml(bannerUrl) + '" alt="' + escapeHtml(item.title || "Giai dau") + '" loading="lazy">'
                : '<div class="native-match-card__banner native-match-card__banner--fallback"><ion-icon name="image-outline"></ion-icon><span>Khong co banner</span></div>',
            '<a class="native-match-card__body" href="/PickleballWeb/Tournament/' + escapeHtml(item.tournamentId) + '">',
            '<div class="native-match-card__top">',
            '<div class="native-match-card__headcopy">',
            '<h2>' + escapeHtml(trimToEmpty(item.title) || "Giai dau Hanaka") + "</h2>",
            '<span>' + escapeHtml(trimToEmpty(item.gameType) || "-") + ' • ' + escapeHtml(trimToEmpty(item.formatText) || "Chua co the thuc") + "</span>",
            "</div>",
            '<span class="native-match-card__status ' + escapeHtml(statusInfo.className) + '">' + escapeHtml(trimToEmpty(item.statusText) || statusInfo.text) + "</span>",
            "</div>",
            '<p><ion-icon name="calendar-outline"></ion-icon><span>' + escapeHtml(formatDateTime(item.startTime) || "Chua co lich") + "</span></p>",
            item.registerDeadline
                ? '<p><ion-icon name="time-outline"></ion-icon><span>Han dang ky: ' + escapeHtml(formatDateTime(item.registerDeadline)) + "</span></p>"
                : "",
            location
                ? '<p><ion-icon name="location-outline"></ion-icon><span>' + escapeHtml(location) + "</span></p>"
                : "",
            '<div class="native-match-card__grid">',
            '<div><small>So doi du kien</small><strong>' + escapeHtml(item.expectedTeams ?? 0) + "</strong></div>",
            '<div><small>Da dang ky</small><strong>' + escapeHtml(item.registeredCount ?? 0) + "</strong></div>",
            '<div><small>Da ghep cap</small><strong>' + escapeHtml(item.pairedCount ?? 0) + "</strong></div>",
            '<div><small>So tran</small><strong>' + escapeHtml(item.matchesCount ?? 0) + "</strong></div>",
            "</div>",
            (trimToEmpty(item.organizer) || trimToEmpty(item.creatorName))
                ? '<span class="native-match-card__foot">' + escapeHtml([trimToEmpty(item.organizer), trimToEmpty(item.creatorName)].filter(Boolean).join(" • ")) + "</span>"
                : "",
            "</a>",
            "</article>"
        ].join("");
    }

    function renderGuideItem(item) {
        var type = trimToEmpty(item.type).toLowerCase();
        var title = trimToEmpty(item.title) || (
            type === "youtube" ? "Youtube" :
                type === "zalo" ? "Zalo" :
                    type === "facebook" ? "Facebook" :
                        type === "website" ? "Website" :
                            type === "phone" ? "Dien thoai" :
                                type === "email" ? "Email" : "Lien ket"
        );

        var url = normalizeExternalHref(item.link || item.url);
        var icon = type === "youtube"
            ? "logo-youtube"
            : type === "facebook"
                ? "logo-facebook"
                : type === "email"
                    ? "mail"
                    : type === "phone"
                        ? "call-outline"
                        : type === "website"
                            ? "globe-outline"
                            : type === "zalo"
                                ? ""
                                : "link-outline";

        return [
            '<a class="native-guide-card" href="' + escapeHtml(buildSafeHref(url, "#")) + '"' + (/^https?:\/\//i.test(url) ? ' target="_blank" rel="noreferrer"' : "") + '>',
            '<div class="native-guide-card__left">',
            '<span class="native-guide-card__icon">',
            type === "zalo"
                ? '<span class="native-guide-card__zalo">Zalo</span>'
                : '<ion-icon name="' + escapeHtml(icon || "link-outline") + '"></ion-icon>',
            "</span>",
            '<span class="native-guide-card__text">' + escapeHtml(title) + "</span>",
            "</div>",
            '<ion-icon name="chevron-forward-outline"></ion-icon>',
            "</a>"
        ].join("");
    }

    function initGuidePage(root) {
        var refs = getCommonRefs(root);

        setHeaderTitle(root, "Huong dan su dung, gioi thieu APP");
        setHeaderAction(root, null);
        setHeaderExtra(root, "");
        renderEmptyState(refs, "Chua co du lieu lien he");

        (async function () {
            try {
                var payload = await fetchJson("/api/links");
                var items = Array.isArray(payload && payload.items) ? payload.items : [];

                refs.list.innerHTML = [
                    '<section class="native-guide-section">',
                    '<h2>Thong tin Hanaka Sport</h2>',
                    '<div class="native-guide-list">',
                    items.length > 0
                        ? items.map(renderGuideItem).join("")
                        : "",
                    "</div>",
                    "</section>"
                ].join("");

                toggleCommonState(refs, {
                    loading: false,
                    itemsLength: items.length,
                    error: "",
                    hasMore: false
                });
            } catch (error) {
                refs.list.innerHTML = "";
                toggleCommonState(refs, {
                    loading: false,
                    itemsLength: 0,
                    error: "Khong tai duoc thong tin huong dan.",
                    hasMore: false
                });
            }
        })();
    }

    function initClubsPage(root) {
        var refs = getCommonRefs(root);
        var state = {
            query: "",
            page: 1,
            pageSize: 10,
            total: 0,
            items: [],
            loading: false,
            error: ""
        };

        setHeaderTitle(root, "Pickleball");
        setHeaderAction(root, {
            html: '<ion-icon name="add"></ion-icon>',
            onClick: function () {
                showAppOnlyAlert("Vui long dang nhap tren app de tao cau lac bo.");
            }
        });
        setHeaderExtra(root, [
            '<form class="native-inline-search" data-native-search-form>',
            '<label class="native-inline-search__box">',
            '<input type="search" placeholder="Tim kiem CLB..." autocomplete="off" data-native-search-input>',
            '<button type="submit" aria-label="Tim kiem"><ion-icon name="search"></ion-icon></button>',
            "</label>",
            "</form>",
            '<div class="native-inline-filter"><ion-icon name="location-outline"></ion-icon><span data-native-filter-text>Tat ca cau lac bo</span></div>'
        ].join(""));
        renderEmptyState(refs, "Khong co cau lac bo nao");

        var form = qs("[data-native-search-form]", root);
        var input = qs("[data-native-search-input]", root);
        var filterText = qs("[data-native-filter-text]", root);

        refs.list.addEventListener("click", function (event) {
            var button = event.target.closest("[data-club-join]");
            if (!button) {
                return;
            }

            event.preventDefault();
            showAppOnlyAlert("Vui long dang nhap tren app de gui yeu cau tham gia cau lac bo.");
        });

        function render() {
            refs.list.className = "native-page-list native-page-list--cards";
            refs.list.innerHTML = state.items.map(renderClubCard).join("");
            if (filterText) {
                filterText.textContent = state.query ? ("Tu khoa: " + state.query) : "Tat ca cau lac bo";
            }

            toggleCommonState(refs, {
                loading: state.loading,
                itemsLength: state.items.length,
                error: state.error,
                hasMore: state.items.length < state.total
            });
        }

        async function load(reset) {
            if (state.loading) {
                return;
            }

            if (!reset && state.items.length >= state.total) {
                return;
            }

            state.loading = true;
            if (reset) {
                state.error = "";
            }
            render();

            try {
                var nextPage = reset ? 1 : state.page;
                var payload = await fetchJson("/api/clubs?page=" + nextPage + "&pageSize=" + state.pageSize + "&keyword=" + encodeURIComponent(state.query));
                var nextItems = Array.isArray(payload && payload.items) ? payload.items : [];

                state.total = Number(payload && payload.total) || 0;
                if (reset) {
                    state.items = nextItems;
                    state.page = 2;
                } else {
                    state.items = state.items.concat(nextItems);
                    state.page += 1;
                }
            } catch (error) {
                state.error = "Khong tai duoc danh sach cau lac bo.";
                if (reset) {
                    state.items = [];
                    state.total = 0;
                    state.page = 1;
                }
            } finally {
                state.loading = false;
                render();
            }
        }

        if (refs.retry) {
            refs.retry.onclick = function () { load(true); };
        }

        if (form && input) {
            form.addEventListener("submit", function (event) {
                event.preventDefault();
                state.query = trimToEmpty(input.value);
                load(true);
            });
        }

        setupInfiniteObserver(refs.sentinel, function () {
            if (!state.loading && state.items.length < state.total) {
                load(false);
            }
        });

        load(true);
    }

    function initCoachLikePage(root, options) {
        var refs = getCommonRefs(root);
        var state = {
            query: "",
            page: 1,
            pageSize: 10,
            total: 0,
            items: [],
            loading: false,
            error: ""
        };
        var debounceTimer = null;

        setHeaderTitle(root, options.title);
        setHeaderAction(root, options.allowAdd ? {
            html: '<ion-icon name="add"></ion-icon>',
            onClick: function () {
                showAppOnlyAlert(options.addMessage);
            }
        } : null);
        setHeaderExtra(root, [
            '<div class="native-inline-search native-inline-search--compact">',
            '<label class="native-inline-search__box">',
            '<input type="search" placeholder="Tim kiem..." autocomplete="off" data-native-search-input>',
            '<ion-icon name="search"></ion-icon>',
            "</label>",
            "</div>",
            '<div class="native-table-head">',
            '<div class="native-table-head__stt">STT</div>',
            '<div class="native-table-head__member">' + escapeHtml(options.memberLabel) + '</div>',
            '<div class="native-table-head__scores"><span>Diem don</span><span>Diem doi</span></div>',
            "</div>"
        ].join(""));
        renderEmptyState(refs, options.emptyText);

        var input = qs("[data-native-search-input]", root);

        function render() {
            refs.list.className = "native-page-list native-page-list--table";
            refs.list.innerHTML = state.items.map(function (item, index) {
                return renderTableRow(item, index, options);
            }).join("");

            toggleCommonState(refs, {
                loading: state.loading,
                itemsLength: state.items.length,
                error: state.error,
                hasMore: state.items.length < state.total
            });
        }

        async function load(reset) {
            if (state.loading) {
                return;
            }

            if (!reset && state.items.length >= state.total) {
                return;
            }

            state.loading = true;
            if (reset) {
                state.error = "";
            }
            render();

            try {
                var nextPage = reset ? 1 : state.page;
                var payload = await fetchJson(options.endpoint + "?page=" + nextPage + "&pageSize=" + state.pageSize + "&query=" + encodeURIComponent(state.query));
                var nextItems = Array.isArray(payload && payload.items) ? payload.items : [];

                state.total = Number(payload && payload.total) || 0;
                if (reset) {
                    state.items = nextItems;
                    state.page = 2;
                } else {
                    state.items = state.items.concat(nextItems);
                    state.page += 1;
                }
            } catch (error) {
                state.error = options.errorText;
                if (reset) {
                    state.items = [];
                    state.total = 0;
                    state.page = 1;
                }
            } finally {
                state.loading = false;
                render();
            }
        }

        if (refs.retry) {
            refs.retry.onclick = function () { load(true); };
        }

        if (input) {
            input.addEventListener("input", function () {
                state.query = trimToEmpty(input.value);

                if (debounceTimer) {
                    clearTimeout(debounceTimer);
                }

                debounceTimer = setTimeout(function () {
                    load(true);
                }, 350);
            });
        }

        setupInfiniteObserver(refs.sentinel, function () {
            if (!state.loading && state.items.length < state.total) {
                load(false);
            }
        });

        load(true);
    }

    function initCourtsPage(root) {
        var refs = getCommonRefs(root);
        var state = {
            query: "",
            page: 0,
            pageSize: 10,
            total: 0,
            items: [],
            loading: false,
            error: ""
        };
        var debounceTimer = null;

        setHeaderAction(root, null);
        setHeaderExtra(root, [
            '<div class="native-inline-search native-inline-search--compact">',
            '<label class="native-inline-search__box">',
            '<input type="search" placeholder="Tim kiem..." autocomplete="off" data-native-search-input>',
            '<ion-icon name="search"></ion-icon>',
            "</label>",
            "</div>"
        ].join(""));
        renderEmptyState(refs, "Khong co san nao");

        var input = qs("[data-native-search-input]", root);

        function render() {
            refs.list.className = "native-page-list native-page-list--cards";
            refs.list.innerHTML = state.items.map(renderCourtCard).join("");

            toggleCommonState(refs, {
                loading: state.loading,
                itemsLength: state.items.length,
                error: state.error,
                hasMore: state.items.length < state.total
            });
        }

        async function load(reset) {
            if (state.loading) {
                return;
            }

            if (!reset && state.items.length >= state.total) {
                return;
            }

            state.loading = true;
            if (reset) {
                state.error = "";
            }
            render();

            try {
                var nextPage = reset ? 0 : state.page;
                var payload = await fetchJson("/api/public/courts?page=" + nextPage + "&pageSize=" + state.pageSize + "&query=" + encodeURIComponent(state.query));
                var nextItems = Array.isArray(payload && payload.items) ? payload.items : [];

                state.total = Number(payload && payload.total) || 0;
                if (reset) {
                    state.items = nextItems;
                    state.page = 1;
                } else {
                    state.items = state.items.concat(nextItems);
                    state.page += 1;
                }
            } catch (error) {
                state.error = "Khong tai duoc danh sach san.";
                if (reset) {
                    state.items = [];
                    state.total = 0;
                    state.page = 0;
                }
            } finally {
                state.loading = false;
                render();
            }
        }

        if (refs.retry) {
            refs.retry.onclick = function () { load(true); };
        }

        if (input) {
            input.addEventListener("input", function () {
                state.query = trimToEmpty(input.value);

                if (debounceTimer) {
                    clearTimeout(debounceTimer);
                }

                debounceTimer = setTimeout(function () {
                    load(true);
                }, 300);
            });
        }

        setupInfiniteObserver(refs.sentinel, function () {
            if (!state.loading && state.items.length < state.total) {
                load(false);
            }
        });

        load(true);
    }

    function initTournamentsPage(root) {
        var refs = getCommonRefs(root);
        var state = {
            query: "",
            tab: "ongoing",
            page: 1,
            pageSize: 50,
            total: 0,
            items: [],
            loading: false,
            error: ""
        };

        setHeaderAction(root, null);
        setHeaderExtra(root, [
            '<div class="native-inline-search native-inline-search--compact">',
            '<label class="native-inline-search__box">',
            '<input type="search" placeholder="Tim kiem..." autocomplete="off" data-native-search-input>',
            '<ion-icon name="search"></ion-icon>',
            "</label>",
            "</div>",
            '<div class="native-inline-filter"><span data-native-filter-text>Tat ca giai dau</span></div>',
            '<div class="native-tabs">',
            '<button class="native-tabs__item is-active" type="button" data-native-tab="ongoing">Dang</button>',
            '<button class="native-tabs__item" type="button" data-native-tab="finished">Ket thuc</button>',
            "</div>"
        ].join(""));
        renderEmptyState(refs, "Khong co giai dau nao");

        var input = qs("[data-native-search-input]", root);
        var tabs = root.querySelectorAll("[data-native-tab]");

        function filteredItems() {
            var query = trimToEmpty(state.query).toLowerCase();
            if (!query) {
                return state.items;
            }

            return state.items.filter(function (item) {
                var hay = (
                    trimToEmpty(item.title) + " " +
                    trimToEmpty(item.areaText) + " " +
                    trimToEmpty(item.locationText) + " " +
                    trimToEmpty(item.gameType)
                ).toLowerCase();

                return hay.indexOf(query) >= 0;
            });
        }

        function render() {
            var items = filteredItems();
            refs.list.className = "native-page-list native-page-list--cards native-page-list--tournaments";
            refs.list.innerHTML = items.map(renderTournamentCard).join("");

            Array.prototype.forEach.call(tabs, function (button) {
                button.classList.toggle("is-active", button.getAttribute("data-native-tab") === state.tab);
            });

            toggleCommonState(refs, {
                loading: state.loading,
                itemsLength: items.length,
                error: state.error,
                hasMore: state.items.length < state.total
            });
        }

        async function load(reset) {
            if (state.loading) {
                return;
            }

            if (!reset && state.items.length >= state.total) {
                return;
            }

            state.loading = true;
            if (reset) {
                state.error = "";
            }
            render();

            try {
                var nextPage = reset ? 1 : state.page;
                var status = state.tab === "finished" ? "CLOSED" : "OPEN";
                var payload = await fetchJson("/api/public/tournaments?page=" + nextPage + "&pageSize=" + state.pageSize + "&status=" + status);
                var nextItems = Array.isArray(payload && payload.items) ? payload.items : [];

                state.total = Number(payload && payload.total) || 0;
                if (reset) {
                    state.items = nextItems;
                    state.page = 2;
                } else {
                    state.items = state.items.concat(nextItems);
                    state.page += 1;
                }
            } catch (error) {
                state.error = "Khong tai duoc danh sach giai dau.";
                if (reset) {
                    state.items = [];
                    state.total = 0;
                    state.page = 1;
                }
            } finally {
                state.loading = false;
                render();
            }
        }

        if (refs.retry) {
            refs.retry.onclick = function () { load(true); };
        }

        if (input) {
            input.addEventListener("input", function () {
                state.query = trimToEmpty(input.value);
                render();
            });
        }

        Array.prototype.forEach.call(tabs, function (button) {
            button.addEventListener("click", function () {
                var nextTab = button.getAttribute("data-native-tab");
                if (nextTab === state.tab) {
                    return;
                }

                state.tab = nextTab;
                load(true);
            });
        });

        setupInfiniteObserver(refs.sentinel, function () {
            if (!state.loading && state.items.length < state.total) {
                load(false);
            }
        });

        load(true);
    }

    function initExchangesPage(root) {
        var refs = getCommonRefs(root);
        var state = {
            query: "",
            page: 1,
            pageSize: 10,
            total: 0,
            items: [],
            loading: false,
            error: ""
        };

        setHeaderAction(root, null);
        setHeaderExtra(root, [
            '<form class="native-inline-search" data-native-search-form>',
            '<label class="native-inline-search__box">',
            '<input type="search" placeholder="Tim CLB dang khieu chien..." autocomplete="off" data-native-search-input>',
            '<button type="submit" aria-label="Tim kiem"><ion-icon name="search"></ion-icon></button>',
            "</label>",
            "</form>",
            '<div class="native-inline-filter native-inline-filter--success"><ion-icon name="flash-outline"></ion-icon><span data-native-filter-text>Dang hien thi CLB bat khieu chien</span></div>'
        ].join(""));
        renderEmptyState(refs, "Khong co CLB nao dang khieu chien");

        var form = qs("[data-native-search-form]", root);
        var input = qs("[data-native-search-input]", root);
        var filterText = qs("[data-native-filter-text]", root);

        function render() {
            refs.list.className = "native-page-list native-page-list--cards";
            refs.list.innerHTML = state.items.map(renderChallengeClubCard).join("");

            if (filterText) {
                filterText.textContent = state.query ? ("Tu khoa: " + state.query) : "Dang hien thi CLB bat khieu chien";
            }

            toggleCommonState(refs, {
                loading: state.loading,
                itemsLength: state.items.length,
                error: state.error,
                hasMore: state.items.length < state.total
            });
        }

        async function load(reset) {
            if (state.loading) {
                return;
            }

            if (!reset && state.items.length >= state.total) {
                return;
            }

            state.loading = true;
            if (reset) {
                state.error = "";
            }
            render();

            try {
                var nextPage = reset ? 1 : state.page;
                var payload = await fetchJson("/api/clubs/challenging?page=" + nextPage + "&pageSize=" + state.pageSize + "&keyword=" + encodeURIComponent(state.query));
                var nextItems = Array.isArray(payload && payload.items) ? payload.items : [];

                state.total = Number(payload && payload.total) || 0;
                if (reset) {
                    state.items = nextItems;
                    state.page = 2;
                } else {
                    state.items = state.items.concat(nextItems);
                    state.page += 1;
                }
            } catch (error) {
                state.error = "Khong tai duoc danh sach CLB dang khieu chien.";
                if (reset) {
                    state.items = [];
                    state.total = 0;
                    state.page = 1;
                }
            } finally {
                state.loading = false;
                render();
            }
        }

        if (refs.retry) {
            refs.retry.onclick = function () { load(true); };
        }

        if (form && input) {
            form.addEventListener("submit", function (event) {
                event.preventDefault();
                state.query = trimToEmpty(input.value);
                load(true);
            });
        }

        setupInfiniteObserver(refs.sentinel, function () {
            if (!state.loading && state.items.length < state.total) {
                load(false);
            }
        });

        load(true);
    }

    function initMatchesPage(root) {
        var refs = getCommonRefs(root);
        var state = {
            formQuery: "",
            formFrom: "",
            formTo: "",
            appliedQuery: "",
            appliedFrom: "",
            appliedTo: "",
            page: 1,
            pageSize: 10,
            hasNextPage: true,
            items: [],
            loading: false,
            error: ""
        };

        setHeaderTitle(root, "Danh sach giai dau");
        setHeaderAction(root, null);
        setHeaderExtra(root, [
            '<div class="native-match-filter">',
            '<h2>Tim kiem & loc</h2>',
            '<label class="native-inline-search__box native-inline-search__box--match">',
            '<input type="search" placeholder="Tim ten giai, dia diem, nguoi to chuc..." autocomplete="off" data-match-query-input>',
            '<ion-icon name="search"></ion-icon>',
            '</label>',
            '<div class="native-match-filter__dates">',
            '<label class="native-match-filter__date"><ion-icon name="calendar-clear-outline"></ion-icon><span data-match-from-label>Tu ngay</span><input type="date" data-match-from-input></label>',
            '<label class="native-match-filter__date"><ion-icon name="calendar-clear-outline"></ion-icon><span data-match-to-label>Den ngay</span><input type="date" data-match-to-input></label>',
            '</div>',
            '<div class="native-match-filter__actions">',
            '<button class="native-match-filter__clear" type="button" data-match-clear><ion-icon name="refresh-outline"></ion-icon><span>Dat lai</span></button>',
            '<button class="native-match-filter__apply" type="button" data-match-apply><ion-icon name="funnel-outline"></ion-icon><span>Loc du lieu</span></button>',
            '</div>',
            '</div>'
        ].join(""));
        renderEmptyState(refs, "Khong co giai dau");

        var queryInput = qs("[data-match-query-input]", root);
        var fromInput = qs("[data-match-from-input]", root);
        var toInput = qs("[data-match-to-input]", root);
        var fromLabel = qs("[data-match-from-label]", root);
        var toLabel = qs("[data-match-to-label]", root);
        var clearButton = qs("[data-match-clear]", root);
        var applyButton = qs("[data-match-apply]", root);

        function syncDateLabels() {
            if (fromLabel) {
                fromLabel.textContent = state.formFrom ? formatDateOnly(state.formFrom) : "Tu ngay";
            }

            if (toLabel) {
                toLabel.textContent = state.formTo ? formatDateOnly(state.formTo) : "Den ngay";
            }
        }

        function filteredItems() {
            return state.items.filter(function (item) {
                if (state.appliedFrom || state.appliedTo) {
                    var start = parseDate(item.startTime);
                    if (!start) {
                        return false;
                    }

                    if (state.appliedFrom) {
                        var fromDate = parseDate(state.appliedFrom);
                        if (fromDate) {
                            fromDate.setHours(0, 0, 0, 0);
                            if (start < fromDate) {
                                return false;
                            }
                        }
                    }

                    if (state.appliedTo) {
                        var toDate = parseDate(state.appliedTo);
                        if (toDate) {
                            toDate.setHours(23, 59, 59, 999);
                            if (start > toDate) {
                                return false;
                            }
                        }
                    }
                }

                return true;
            });
        }

        function render() {
            var items = filteredItems();
            refs.list.className = "native-page-list native-page-list--cards";
            refs.list.innerHTML = items.map(renderMatchTournamentCard).join("");

            toggleCommonState(refs, {
                loading: state.loading,
                itemsLength: items.length,
                error: state.error,
                hasMore: state.hasNextPage
            });
        }

        async function load(reset) {
            if (state.loading) {
                return;
            }

            if (!reset && !state.hasNextPage) {
                return;
            }

            state.loading = true;
            if (reset) {
                state.error = "";
            }
            render();

            try {
                var nextPage = reset ? 1 : state.page + 1;
                var payload = await fetchJson("/api/public/tournaments?page=" + nextPage + "&pageSize=" + state.pageSize + "&status=ALL&query=" + encodeURIComponent(state.appliedQuery));
                var nextItems = Array.isArray(payload && payload.items) ? payload.items : [];

                state.items = reset ? nextItems : state.items.concat(nextItems);
                state.page = Number(payload && payload.page) || nextPage;
                state.hasNextPage = !!(payload && payload.hasNextPage);
            } catch (error) {
                state.error = "Khong tai duoc danh sach giai dau.";
                if (reset) {
                    state.items = [];
                    state.page = 1;
                    state.hasNextPage = false;
                }
            } finally {
                state.loading = false;
                render();
            }
        }

        if (refs.retry) {
            refs.retry.onclick = function () { load(true); };
        }

        if (queryInput) {
            queryInput.addEventListener("input", function () {
                state.formQuery = trimToEmpty(queryInput.value);
            });
        }

        if (fromInput) {
            fromInput.addEventListener("change", function () {
                state.formFrom = trimToEmpty(fromInput.value);
                syncDateLabels();
            });
        }

        if (toInput) {
            toInput.addEventListener("change", function () {
                state.formTo = trimToEmpty(toInput.value);
                syncDateLabels();
            });
        }

        if (clearButton) {
            clearButton.onclick = function () {
                state.formQuery = "";
                state.formFrom = "";
                state.formTo = "";
                state.appliedQuery = "";
                state.appliedFrom = "";
                state.appliedTo = "";

                if (queryInput) {
                    queryInput.value = "";
                }
                if (fromInput) {
                    fromInput.value = "";
                }
                if (toInput) {
                    toInput.value = "";
                }

                syncDateLabels();
                load(true);
            };
        }

        if (applyButton) {
            applyButton.onclick = function () {
                if (state.formFrom && state.formTo && parseDate(state.formFrom) > parseDate(state.formTo)) {
                    window.alert("Ngay bat dau phai nho hon hoac bang ngay ket thuc.");
                    return;
                }

                state.appliedQuery = state.formQuery;
                state.appliedFrom = state.formFrom;
                state.appliedTo = state.formTo;
                load(true);
            };
        }

        setupInfiniteObserver(refs.sentinel, function () {
            if (!state.loading && state.hasNextPage) {
                load(false);
            }
        });

        syncDateLabels();
        load(true);
    }

    function buildNotificationOpponentText(item) {
        var player1 = trimToEmpty(item && item.opponentTeam && item.opponentTeam.player1 && item.opponentTeam.player1.name);
        var player2 = trimToEmpty(item && item.opponentTeam && item.opponentTeam.player2 && item.opponentTeam.player2.name);

        if (player1 && player2) {
            return player1 + " - " + player2;
        }

        if (player1) {
            return player1;
        }

        return "Chua xac dinh doi thu";
    }

    function renderNotificationCard(item) {
        var startAtText = trimToEmpty(item && item.match && item.match.startAtText) || "Chua cap nhat";
        var addressText = trimToEmpty(item && item.match && item.match.addressText) || "Chua cap nhat";
        var courtText = trimToEmpty(item && item.match && item.match.courtText) || "Chua cap nhat";

        return [
            '<article class="native-notification-card">',
            '<h2 class="native-notification-card__title">' + escapeHtml(trimToEmpty(item && item.title) || "Hanaka Sport - Thong bao") + "</h2>",
            '<p class="native-notification-card__line">' + escapeHtml(trimToEmpty(item && item.message) || "Thong bao se duoc cap nhat tai day.") + "</p>",
            '<p class="native-notification-card__line"><strong>Doi thu:</strong> ' + escapeHtml(buildNotificationOpponentText(item)) + "</p>",
            '<p class="native-notification-card__line"><strong>Thoi gian:</strong> ' + escapeHtml(startAtText) + "</p>",
            '<p class="native-notification-card__line"><strong>Dia diem:</strong> ' + escapeHtml(addressText) + "</p>",
            '<p class="native-notification-card__line"><strong>San:</strong> ' + escapeHtml(courtText) + "</p>",
            '<p class="native-notification-card__time">' + escapeHtml(startAtText) + "</p>",
            "</article>"
        ].join("");
    }

    function renderNotificationLoginPrompt() {
        var loginHref = "/PickleballWeb/Login?returnUrl=" + encodeURIComponent("/PickleballWeb/Notifications");

        return [
            '<article class="native-auth-prompt">',
            '<span class="native-auth-prompt__icon"><ion-icon name="notifications-outline"></ion-icon></span>',
            "<strong>Dang nhap de xem thong bao</strong>",
            "<p>Trang nay se hien thi cac tran dau sap toi cua tai khoan giong trong ban app.</p>",
            '<a class="native-auth-prompt__button" href="' + escapeHtml(loginHref) + '">Dang nhap</a>',
            "</article>"
        ].join("");
    }

    function renderSettingsRow(options) {
        var href = buildSafeHref(options && options.href, "#");
        var external = isExternalHref(href);
        var danger = !!(options && options.danger);
        var attrs = external ? ' target="_blank" rel="noreferrer"' : "";

        return [
            '<a class="native-settings-row' + (danger ? " native-settings-row--danger" : "") + '" href="' + escapeHtml(href) + '"' + attrs + ">",
            '<span class="native-settings-row__left">',
            '<ion-icon class="native-settings-row__icon" name="' + escapeHtml(options && options.icon || "chevron-forward-outline") + '"></ion-icon>',
            '<span class="native-settings-row__label">' + escapeHtml(options && options.label || "Tuy chon") + "</span>",
            "</span>",
            '<ion-icon class="native-settings-row__chevron" name="chevron-forward"></ion-icon>',
            "</a>"
        ].join("");
    }

    function renderSettingsPage() {
        return [
            '<section class="native-settings-section">',
            '<h2 class="native-settings-section__title">Tai khoan</h2>',
            renderSettingsRow({
                label: "Quan ly tai khoan",
                icon: "person-circle-outline",
                href: "/PickleballWeb/Account"
            }),
            renderSettingsRow({
                label: "Doi mat khau",
                icon: "key-outline",
                href: "/PickleballWeb/ChangePassword"
            }),
            renderSettingsRow({
                label: "Xoa tai khoan",
                icon: "trash-outline",
                href: "/PickleballWeb/Account",
                danger: true
            }),
            '<p class="native-settings-note">Flow xoa tai khoan tren web duoc dat ben trong man Tai khoan de giong cach van hanh hien tai cua he thong.</p>',
            "</section>",
            '<div class="native-settings-divider"></div>',
            '<section class="native-settings-section">',
            '<h2 class="native-settings-section__title">An toan cong dong</h2>',
            renderSettingsRow({
                label: "Dieu khoan, moderation va block list",
                icon: "shield-checkmark-outline",
                href: "/PickleballWeb/CommunitySafety"
            }),
            renderSettingsRow({
                label: "Chinh sach quyen rieng tu",
                icon: "document-text-outline",
                href: "https://hanakasport.click/policy/index"
            }),
            '<p class="native-settings-note">Chat CLB da bat bo loc noi dung, co che bao cao vi pham, chan nguoi dung va cam ket xu ly moderation trong vong 24 gio.</p>',
            "</section>",
            '<div class="native-settings-divider"></div>',
            '<section class="native-settings-section">',
            '<h2 class="native-settings-section__title">Thong tin ung dung</h2>',
            '<p class="native-settings-version">Phien ban: 1.0.0</p>',
            "</section>"
        ].join("");
    }

    function initNotificationsPage(root) {
        var refs = getCommonRefs(root);
        var state = {
            loading: false,
            error: "",
            authRequired: false,
            items: []
        };

        setHeaderAction(root, null);
        setHeaderExtra(root, "");
        renderEmptyState(refs, "Hien chua co thong bao thi dau sap toi.");

        function render() {
            refs.list.className = "native-page-list native-page-list--notifications";

            if (state.authRequired) {
                refs.list.innerHTML = renderNotificationLoginPrompt();
            } else {
                refs.list.innerHTML = state.items.map(renderNotificationCard).join("");
            }

            toggleCommonState(refs, {
                loading: state.loading,
                itemsLength: state.authRequired ? 1 : state.items.length,
                error: state.error,
                hasMore: false
            });
        }

        async function load() {
            if (state.loading) {
                return;
            }

            state.loading = true;
            state.error = "";
            state.authRequired = false;
            render();

            try {
                var session = await fetchJson("/api/web-auth/me");
                if (!(session && session.isAuthenticated)) {
                    state.items = [];
                    state.authRequired = true;
                    return;
                }

                var payload = await fetchJson("/api/notifications/upcoming-matches");
                state.items = Array.isArray(payload && payload.items) ? payload.items : [];
            } catch (error) {
                state.items = [];
                state.error = "Khong tai duoc thong bao thi dau.";
            } finally {
                state.loading = false;
                render();
            }
        }

        if (refs.retry) {
            refs.retry.onclick = function () { load(); };
        }

        load();
    }

    function initSettingsPage(root) {
        var refs = getCommonRefs(root);

        setHeaderAction(root, null);
        setHeaderExtra(root, "");
        refs.list.className = "native-page-list native-page-list--settings";
        refs.list.innerHTML = renderSettingsPage();

        toggleCommonState(refs, {
            loading: false,
            itemsLength: 1,
            error: "",
            hasMore: false
        });
    }

    function initNativePage(root) {
        var kind = trimToEmpty(root.getAttribute("data-native-page-kind"));

        if (!kind) {
            return;
        }

        if (kind === "guide") {
            initGuidePage(root);
            return;
        }

        if (kind === "clubs") {
            initClubsPage(root);
            return;
        }

        if (kind === "coaches") {
            initCoachLikePage(root, {
                title: "Huan Luyen Vien",
                memberLabel: "Huan luyen vien",
                endpoint: "/api/coaches",
                detailHref: function (item) { return "/PickleballWeb/Coach/" + item.coachId; },
                singleValue: function (item) { return item.levelSingle; },
                doubleValue: function (item) { return item.levelDouble; },
                emptyName: "Huan luyen vien",
                emptyText: "Khong co huan luyen vien nao",
                errorText: "Khong tai duoc danh sach huan luyen vien.",
                allowAdd: true,
                addMessage: "Vui long dang nhap tren app de tao hoac cap nhat ho so huan luyen vien.",
                kind: "coach"
            });
            return;
        }

        if (kind === "referees") {
            initCoachLikePage(root, {
                title: "Trong Tai",
                memberLabel: "Trong tai",
                endpoint: "/api/referees",
                detailHref: function (item) { return "/PickleballWeb/Referee/" + item.refereeId; },
                singleValue: function (item) { return item.levelSingle; },
                doubleValue: function (item) { return item.levelDouble; },
                emptyName: "Trong tai",
                emptyText: "Khong co trong tai nao",
                errorText: "Khong tai duoc danh sach trong tai.",
                allowAdd: true,
                addMessage: "Vui long dang nhap tren app de tao hoac cap nhat ho so trong tai.",
                kind: "referee"
            });
            return;
        }

        if (kind === "courts") {
            initCourtsPage(root);
            return;
        }

        if (kind === "tournaments") {
            initTournamentsPage(root);
            return;
        }

        if (kind === "exchanges") {
            initExchangesPage(root);
            return;
        }

        if (kind === "matches") {
            initMatchesPage(root);
            return;
        }

        if (kind === "notifications") {
            initNotificationsPage(root);
            return;
        }

        if (kind === "settings") {
            initSettingsPage(root);
        }
    }

    document.addEventListener("DOMContentLoaded", function () {
        var root = qs("[data-native-page-kind]");
        if (root) {
            initNativePage(root);
        }
    });
})();
