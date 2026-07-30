(function () {
    function qs(selector, root) {
        return (root || document).querySelector(selector);
    }

    function qsa(selector, root) {
        return Array.prototype.slice.call((root || document).querySelectorAll(selector));
    }

    function trimToEmpty(value) {
        return String(value ?? "").trim();
    }

    var notificationTextDecoder = typeof TextDecoder === "function"
        ? new TextDecoder("utf-8", { fatal: true })
        : null;

    function looksLikeMojibake(value) {
        return /(Ã.|Â.|Ä.|á»|áº|Æ°|â€)/.test(value);
    }

    function decodeLatin1Utf8(value) {
        if (!notificationTextDecoder) {
            return value;
        }

        var bytes = new Uint8Array(value.length);
        for (var index = 0; index < value.length; index += 1) {
            var code = value.charCodeAt(index);
            if (code > 255) {
                return value;
            }

            bytes[index] = code;
        }

        try {
            return notificationTextDecoder.decode(bytes);
        } catch (_error) {
            return value;
        }
    }

    function normalizeDisplayText(value) {
        var text = trimToEmpty(value);
        if (!text) {
            return "";
        }

        var normalized = text;
        for (var attempt = 0; attempt < 2; attempt += 1) {
            if (!looksLikeMojibake(normalized)) {
                break;
            }

            var repaired = decodeLatin1Utf8(normalized);
            if (!repaired || repaired === normalized) {
                break;
            }

            normalized = repaired;
        }

        return normalized;
    }

    function tournamentGameTypeLabel(gameType, genderCategory, explicitLabel) {
        var label = normalizeDisplayText(explicitLabel);
        if (label) {
            return label;
        }

        var type = trimToEmpty(gameType).toUpperCase();
        var category = trimToEmpty(genderCategory).toUpperCase();

        if (type === "SINGLE" && category === "MEN") {
            return "\u0110\u01a1n nam";
        }

        if (type === "SINGLE" && category === "WOMEN") {
            return "\u0110\u01a1n n\u1eef";
        }

        if (type === "DOUBLE" && category === "MEN") {
            return "\u0110\u00f4i nam";
        }

        if (type === "DOUBLE" && category === "WOMEN") {
            return "\u0110\u00f4i n\u1eef";
        }

        if ((type === "DOUBLE" && category === "MIXED") || type === "MIXED") {
            return "\u0110\u00f4i nam n\u1eef";
        }

        if (type === "DOUBLE") {
            return "\u0110\u00f4i";
        }

        if (type === "SINGLE") {
            return "\u0110\u01a1n";
        }

        return normalizeDisplayText(gameType) || "-";
    }

    function formatFlexibleNumber(value) {
        var number = Number(value);
        if (!Number.isFinite(number)) {
            return "0";
        }

        if (Math.abs(number - Math.trunc(number)) < 0.000001) {
            return String(Math.trunc(number));
        }

        return number.toFixed(1).replace(/\.0$/, "");
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
            credentials: "same-origin",
            cache: "no-store"
        });

        if (!response.ok) {
            throw new Error("Request failed: " + response.status);
        }

        return response.json();
    }

    async function requestJson(url, options) {
        var init = Object.assign({
            credentials: "same-origin",
            cache: "no-store"
        }, options || {});

        init.headers = Object.assign({
            Accept: "application/json"
        }, options && options.body ? {
            "Content-Type": "application/json"
        } : {}, options && options.headers ? options.headers : {});

        var response = await fetch(url, init);
        var contentType = response.headers.get("content-type") || "";
        var payload = contentType.indexOf("application/json") >= 0
            ? await response.json().catch(function () { return null; })
            : await response.text().catch(function () { return ""; });

        if (!response.ok) {
            var message = typeof payload === "string"
                ? trimToEmpty(payload)
                : trimToEmpty(payload && (payload.message || payload.title));
            var error = new Error(message || ("Request failed: " + response.status));
            error.status = response.status;
            error.payload = payload;
            throw error;
        }

        return payload;
    }

    var NOTIFICATION_CENTER_EVENT = "hanaka:notifications-changed";
    var notificationCenter = {
        initialized: false,
        authenticated: false,
        knownPairRequestIds: Object.create(null),
        pendingPairItems: [],
        queuedPopupItem: null,
        popupRoot: null,
        activePopupRequestId: 0,
        realtimeListener: null,
        onNotificationChange: null,
        onVisibilityChange: null,
        syncToken: 0
    };

    function dispatchNotificationCenterChange(detail) {
        try {
            window.dispatchEvent(new CustomEvent(NOTIFICATION_CENTER_EVENT, {
                detail: detail || {}
            }));
        } catch (_error) {
        }
    }

    function getNotificationBellLinks() {
        var items = Array.prototype.slice.call(document.querySelectorAll(
            "[data-web-notification-bell], .app-bar__actions .round-icon[href=\"/PickleballWeb/Notifications\"]"
        ));
        var seen = [];

        return items.filter(function (item) {
            if (!item || seen.indexOf(item) >= 0) {
                return false;
            }

            seen.push(item);
            return true;
        });
    }

    function setNotificationBellCount(count) {
        var total = Math.max(0, Number(count) || 0);
        var text = total > 99 ? "99+" : String(total);

        getNotificationBellLinks().forEach(function (link) {
            var badge = qs("[data-web-notification-badge]", link);
            var baseLabel = normalizeDisplayText(link.getAttribute("data-bell-label")) || normalizeDisplayText(link.getAttribute("aria-label")) || "Thông báo";

            if (!badge) {
                badge = document.createElement("span");
                badge.className = "web-notification-badge";
                badge.setAttribute("data-web-notification-badge", "");
                badge.setAttribute("aria-hidden", "true");
                link.appendChild(badge);
            }

            if (!link.hasAttribute("data-bell-label")) {
                link.setAttribute("data-bell-label", baseLabel);
            }

            badge.hidden = total <= 0;
            badge.textContent = text;
            link.classList.toggle("has-badge", total > 0);
            link.setAttribute("aria-label", total > 0 ? (baseLabel + " (" + total + ")") : baseLabel);
        });
    }

    function buildPairRequestIdMap(items) {
        var map = Object.create(null);

        (Array.isArray(items) ? items : []).forEach(function (item) {
            var requestId = Number(item && item.pairRequestId);
            if (Number.isFinite(requestId) && requestId > 0) {
                map[String(requestId)] = true;
            }
        });

        return map;
    }

    function findPairRequestById(items, requestId) {
        var targetId = Number(requestId);
        if (!Number.isFinite(targetId) || targetId <= 0) {
            return null;
        }

        var list = Array.isArray(items) ? items : [];
        for (var i = 0; i < list.length; i += 1) {
            var itemId = Number(list[i] && list[i].pairRequestId);
            if (itemId === targetId) {
                return list[i];
            }
        }

        return null;
    }

    function getNotificationType(item) {
        return trimToEmpty(item && (item.type || item.notificationType || item.NotificationType)).toUpperCase();
    }

    function isPairRequestNotification(item) {
        return getNotificationType(item) === "PAIR_REQUEST";
    }

    function isPairLifecycleNotificationType(notificationType) {
        return notificationType === "PAIR_ACCEPTED" ||
            notificationType === "PAIR_REJECTED" ||
            notificationType === "PAIR_CANCELED" ||
            notificationType === "PAIR_EXPIRED";
    }

    function canPresentRealtimePairPopup() {
        return document.visibilityState !== "hidden";
    }

    function closePairRequestPopup() {
        if (notificationCenter.popupRoot) {
            notificationCenter.popupRoot.hidden = true;
            notificationCenter.popupRoot.removeAttribute("data-active-notification-id");
            notificationCenter.popupRoot.removeAttribute("data-active-notification-type");
        }

        notificationCenter.activePopupRequestId = 0;
        document.body.classList.remove("has-web-pair-popup");
    }

    function renderPairRequestPopupContent(item) {
        var requestId = Number(item && item.pairRequestId);
        var tournamentId = Number(item && item.tournamentId);
        var requestedBy = item && item.requestedBy ? item.requestedBy : {};
        var requesterName = normalizeDisplayText(requestedBy.fullName) || "Thành viên Hanaka";
        var avatarUrl = normalizeMediaUrl(requestedBy.avatarUrl);
        var popupTitle = normalizeDisplayText(item && item.title) || "Lời mời ghép đôi";
        var popupMessage = normalizeDisplayText(item && item.message) || (requesterName + " mời bạn ghép cặp.");
        var tournamentTitle = normalizeDisplayText(item && item.tournamentTitle) || normalizeDisplayText(item && item.title) || "Giải đấu";
        var expiresAt = formatDateTime(item && item.expiresAt) || "Sắp hết hạn";
        var detailHref = tournamentId > 0
            ? "/PickleballWeb/Tournament/" + tournamentId + "/Register"
            : "/PickleballWeb/Notifications";

        return [
            '<div class="web-pair-popup__eyebrow"><ion-icon name="notifications-outline"></ion-icon><span>Lời mời ghép đôi mới</span></div>',
            '<div class="web-pair-popup__head">',
            avatarUrl
                ? '<span class="native-notification-card__avatar"><img src="' + escapeHtml(avatarUrl) + '" alt="' + escapeHtml(requesterName) + '" loading="lazy"></span>'
                : '<span class="native-notification-card__avatar"><ion-icon name="person-outline"></ion-icon></span>',
            '<div>',
            '<h2 class="web-pair-popup__title" id="web-pair-popup-title">' + escapeHtml(popupTitle) + "</h2>",
            '<p class="web-pair-popup__message">' + escapeHtml(popupMessage) + "</p>",
            "</div>",
            "</div>",
            '<div class="web-pair-popup__meta">',
            '<div class="web-pair-popup__meta-row"><span class="web-pair-popup__meta-label">Người mời</span><span class="web-pair-popup__meta-value">' + escapeHtml(requesterName) + "</span></div>",
            '<div class="web-pair-popup__meta-row"><span class="web-pair-popup__meta-label">Giải đấu</span><span class="web-pair-popup__meta-value">' + escapeHtml(tournamentTitle) + "</span></div>",
            '<div class="web-pair-popup__meta-row"><span class="web-pair-popup__meta-label">Hết hạn</span><span class="web-pair-popup__meta-value">' + escapeHtml(expiresAt) + "</span></div>",
            "</div>",
            '<div class="web-pair-popup__actions">',
            '<button type="button" class="is-primary" data-pair-popup-action="accept" data-pair-request-id="' + escapeHtml(requestId || "") + '">Chấp nhận</button>',
            '<button type="button" data-pair-popup-action="reject" data-pair-request-id="' + escapeHtml(requestId || "") + '">Từ chối</button>',
            '<a href="' + escapeHtml(detailHref) + '">Xem chi tiết</a>',
            "</div>"
        ].join("");
    }

    function renderInfoNotificationPopupContent(item) {
        var notificationType = getNotificationType(item);
        var isPairResponse = isPairLifecycleNotificationType(notificationType);
        var tournamentId = Number(readNotificationValue(item, ["tournamentId", "TournamentId"]));
        var matchId = Number(readNotificationValue(item, ["matchId", "MatchId"]));
        var actor = item && item.acceptedBy
            ? item.acceptedBy
            : item && item.requestedTo
                ? item.requestedTo
                : item && item.requestedBy
                    ? item.requestedBy
                    : null;
        var actorName = normalizeDisplayText(actor && actor.fullName) || "Thành viên Hanaka";
        if (!isPairResponse) {
            actorName = "H\u1ec7 th\u1ed1ng gi\u1ea3i \u0111\u1ea5u";
        }
        var avatarUrl = normalizeMediaUrl(actor && actor.avatarUrl);
        var popupTitle = normalizeDisplayText(item && item.title) || "Thông báo ghép đôi";
        var popupMessage = normalizeDisplayText(item && item.message) || "Bạn có thông báo mới về ghép đôi.";
        var tournamentTitle = normalizeDisplayText(item && item.tournamentTitle) || "Giải đấu";
        var responseNote = normalizeDisplayText(item && item.responseNote);
        var detailHref = tournamentId > 0
            ? "/PickleballWeb/Tournament/" + tournamentId + "/Register"
            : "/PickleballWeb/Notifications";
        var detailText = tournamentId > 0 ? "Xem đăng ký" : "Mở thông báo";
        var eyebrowText = notificationType === "PAIR_ACCEPTED"
            ? "Ghép cặp thành công"
            : notificationType === "PAIR_REJECTED"
                ? "Phản hồi lời mời"
                : "Thông báo mới";
        var timeText = formatDateTime(item && item.createdAt) || "Vừa xong";

        detailHref = notificationType === "MATCH_WIN" && matchId > 0
            ? "/PickleballWeb/Match/" + matchId
            : tournamentId > 0
                ? "/PickleballWeb/Tournament/" + tournamentId + (isPairResponse ? "/Register" : "/Standings")
                : "/PickleballWeb/Notifications";
        detailText = notificationType === "MATCH_WIN"
            ? "Xem tr\u1eadn"
            : tournamentId > 0
                ? (isPairResponse ? "Xem \u0111\u0103ng k\u00fd" : "Xem gi\u1ea3i \u0111\u1ea5u")
                : "M\u1edf th\u00f4ng b\u00e1o";
        eyebrowText = getTournamentNotificationEyebrow(notificationType);
        var metaRows = isPairResponse
            ? [
                buildNotificationMetaRow("Ng\u01b0\u1eddi ph\u1ea3n h\u1ed3i", actorName),
                buildNotificationMetaRow("Gi\u1ea3i \u0111\u1ea5u", tournamentTitle),
                buildNotificationMetaRow("Th\u1eddi gian", timeText),
                responseNote ? buildNotificationMetaRow("Ghi ch\u00fa", responseNote) : ""
            ].join("")
            : buildTournamentNotificationDetailRows(item, "popup") + buildNotificationMetaRow("Th\u1eddi gian", timeText);

        return [
            '<div class="web-pair-popup__eyebrow"><ion-icon name="notifications-outline"></ion-icon><span>' + escapeHtml(eyebrowText) + "</span></div>",
            '<div class="web-pair-popup__head">',
            avatarUrl
                ? '<span class="native-notification-card__avatar"><img src="' + escapeHtml(avatarUrl) + '" alt="' + escapeHtml(actorName) + '" loading="lazy"></span>'
                : '<span class="native-notification-card__avatar"><ion-icon name="person-outline"></ion-icon></span>',
            '<div>',
            '<h2 class="web-pair-popup__title" id="web-pair-popup-title">' + escapeHtml(popupTitle) + "</h2>",
            '<p class="web-pair-popup__message">' + escapeHtml(popupMessage) + "</p>",
            "</div>",
            "</div>",
            '<div class="web-pair-popup__meta">',
            metaRows,
            '<div class="web-pair-popup__meta-row"><span class="web-pair-popup__meta-label">Người phản hồi</span><span class="web-pair-popup__meta-value">' + escapeHtml(actorName) + "</span></div>",
            '<div class="web-pair-popup__meta-row"><span class="web-pair-popup__meta-label">Giải đấu</span><span class="web-pair-popup__meta-value">' + escapeHtml(tournamentTitle) + "</span></div>",
            '<div class="web-pair-popup__meta-row"><span class="web-pair-popup__meta-label">Thời gian</span><span class="web-pair-popup__meta-value">' + escapeHtml(timeText) + "</span></div>",
            responseNote
                ? '<div class="web-pair-popup__meta-row"><span class="web-pair-popup__meta-label">Ghi chú</span><span class="web-pair-popup__meta-value">' + escapeHtml(responseNote) + "</span></div>"
                : "",
            "</div>",
            '<div class="web-pair-popup__actions">',
            '<a class="is-primary" href="' + escapeHtml(detailHref) + '" data-pair-popup-notification-link="' + escapeHtml(Number(item && (item.notificationId || item.id)) || "") + '">' + escapeHtml(detailText) + "</a>",
            '<button type="button" data-pair-popup-close>Đóng</button>',
            "</div>"
        ].join("");
    }

    function renderInfoNotificationPopupContent(item) {
        var notificationType = getNotificationType(item);
        var isPairResponse = isPairLifecycleNotificationType(notificationType);
        var tournamentId = Number(readNotificationValue(item, ["tournamentId", "TournamentId"]));
        var matchId = Number(readNotificationValue(item, ["matchId", "MatchId"]));
        var actor = item && item.acceptedBy
            ? item.acceptedBy
            : item && item.requestedTo
                ? item.requestedTo
                : item && item.requestedBy
                    ? item.requestedBy
                    : null;
        var actorName = normalizeDisplayText(actor && actor.fullName) || "Thành viên Hanaka";
        if (!isPairResponse) {
            actorName = "Hệ thống giải đấu";
        }

        var avatarUrl = normalizeMediaUrl(actor && actor.avatarUrl);
        var popupTitle = normalizeDisplayText(item && item.title) || (isPairResponse
            ? "Thông báo ghép đôi"
            : "Thông báo giải đấu");
        var popupMessage = normalizeDisplayText(item && item.message) || (isPairResponse
            ? "Bạn có thông báo mới về ghép đôi."
            : "Bạn có thông báo mới từ hệ thống giải đấu.");
        var tournamentTitle = normalizeDisplayText(item && item.tournamentTitle) || "Giải đấu";
        var responseNote = normalizeDisplayText(item && item.responseNote);
        var detailHref = notificationType === "MATCH_WIN" && matchId > 0
            ? "/PickleballWeb/Match/" + matchId
            : tournamentId > 0
                ? "/PickleballWeb/Tournament/" + tournamentId + (isPairResponse ? "/Register" : "/Standings")
                : "/PickleballWeb/Notifications";
        var detailText = notificationType === "MATCH_WIN"
            ? "Xem trận"
            : tournamentId > 0
                ? (isPairResponse ? "Xem đăng ký" : "Xem giải đấu")
                : "Mở thông báo";
        var eyebrowText = isPairResponse
            ? (notificationType === "PAIR_ACCEPTED" ? "Ghép cặp thành công" : "Phản hồi lời mời")
            : getTournamentNotificationEyebrow(notificationType);
        var timeText = formatDateTime(item && item.createdAt) || "Vừa xong";
        var metaRows = isPairResponse
            ? [
                buildNotificationMetaRow("Người phản hồi", actorName),
                buildNotificationMetaRow("Giải đấu", tournamentTitle),
                buildNotificationMetaRow("Thời gian", timeText),
                responseNote ? buildNotificationMetaRow("Ghi chú", responseNote) : ""
            ].join("")
            : buildTournamentNotificationDetailRows(item, "popup") + buildNotificationMetaRow("Thời gian", timeText);
        var fallbackIcon = isPairResponse ? "person-outline" : "trophy-outline";

        return [
            '<div class="web-pair-popup__eyebrow"><ion-icon name="notifications-outline"></ion-icon><span>' + escapeHtml(eyebrowText) + "</span></div>",
            '<div class="web-pair-popup__head">',
            avatarUrl
                ? '<span class="native-notification-card__avatar"><img src="' + escapeHtml(avatarUrl) + '" alt="' + escapeHtml(actorName) + '" loading="lazy"></span>'
                : '<span class="native-notification-card__avatar"><ion-icon name="' + escapeHtml(fallbackIcon) + '"></ion-icon></span>',
            '<div>',
            '<h2 class="web-pair-popup__title" id="web-pair-popup-title">' + escapeHtml(popupTitle) + "</h2>",
            '<p class="web-pair-popup__message">' + escapeHtml(popupMessage) + "</p>",
            "</div>",
            "</div>",
            '<div class="web-pair-popup__meta">',
            metaRows,
            "</div>",
            '<div class="web-pair-popup__actions">',
            '<a class="is-primary" href="' + escapeHtml(detailHref) + '" data-pair-popup-notification-link="' + escapeHtml(Number(item && (item.notificationId || item.id)) || "") + '">' + escapeHtml(detailText) + "</a>",
            '<button type="button" data-pair-popup-close>Đóng</button>',
            "</div>"
        ].join("");
    }

    function renderNotificationPopupContent(item) {
        return isPairRequestNotification(item)
            ? renderPairRequestPopupContent(item)
            : renderInfoNotificationPopupContent(item);
    }

    function ensurePairRequestPopup() {
        if (notificationCenter.popupRoot && document.body.contains(notificationCenter.popupRoot)) {
            return notificationCenter.popupRoot;
        }

        var root = document.createElement("div");
        root.className = "web-pair-popup";
        root.hidden = true;
        root.innerHTML = [
            '<div class="web-pair-popup__backdrop" data-pair-popup-close></div>',
            '<section class="web-pair-popup__dialog" role="dialog" aria-modal="true" aria-labelledby="web-pair-popup-title">',
            '<button class="web-pair-popup__close" type="button" aria-label="Đóng" data-pair-popup-close>',
            '<ion-icon name="close-outline"></ion-icon>',
            "</button>",
            '<div data-pair-popup-content></div>',
            "</section>"
        ].join("");

        root.addEventListener("click", async function (event) {
            var detailLink = event.target.closest("[data-pair-popup-notification-link]");
            if (detailLink) {
                var linkNotificationId = Number(detailLink.getAttribute("data-pair-popup-notification-link"));
                if (Number.isFinite(linkNotificationId) && linkNotificationId > 0) {
                    event.preventDefault();
                    var href = detailLink.getAttribute("href") || "/PickleballWeb/Notifications";
                    try {
                        await markUserNotificationRead(linkNotificationId);
                    } catch (_error) {
                    }
                    window.location.href = href;
                }
                return;
            }

            var closeTarget = event.target.closest("[data-pair-popup-close]");
            if (closeTarget) {
                var activeNotificationId = Number(root.getAttribute("data-active-notification-id"));
                var activeNotificationType = trimToEmpty(root.getAttribute("data-active-notification-type")).toUpperCase();
                if (activeNotificationId > 0 && activeNotificationType && activeNotificationType !== "PAIR_REQUEST") {
                    try {
                        await markUserNotificationRead(activeNotificationId);
                    } catch (_error) {
                    }
                }
                closePairRequestPopup();
                return;
            }

            var actionButton = event.target.closest("[data-pair-popup-action]");
            if (!actionButton) {
                return;
            }

            var requestId = Number(actionButton.getAttribute("data-pair-request-id"));
            var action = trimToEmpty(actionButton.getAttribute("data-pair-popup-action")).toLowerCase();
            var controls = Array.prototype.slice.call(root.querySelectorAll("[data-pair-popup-action]"));

            if (!Number.isFinite(requestId) || requestId <= 0 || (action !== "accept" && action !== "reject")) {
                return;
            }

            try {
                await performPairRequestAction(requestId, action, {
                    control: actionButton,
                    controls: controls
                });
                closePairRequestPopup();
                await syncNotificationCenter({ allowPopup: false });
            } catch (error) {
                window.alert(error && error.message ? error.message : "Không thể xử lý lời mời ghép đôi.");
            }
        });

        document.body.appendChild(root);
        notificationCenter.popupRoot = root;
        return root;
    }

    function showPairRequestPopup(item) {
        var popup = ensurePairRequestPopup();
        var content = qs("[data-pair-popup-content]", popup);

        if (!content || !item) {
            return;
        }

        content.innerHTML = renderNotificationPopupContent(item);
        popup.setAttribute("data-active-notification-id", String(Number(item && (item.notificationId || item.id)) || 0));
        popup.setAttribute("data-active-notification-type", getNotificationType(item));
        popup.hidden = false;
        notificationCenter.queuedPopupItem = null;
        notificationCenter.activePopupRequestId = isPairRequestNotification(item)
            ? (Number(item && item.pairRequestId) || 0)
            : 0;
        document.body.classList.add("has-web-pair-popup");
    }

    function normalizeUserBrief(payload) {
        if (!payload || typeof payload !== "object") {
            return null;
        }

        var fullName = normalizeDisplayText(payload.fullName || payload.FullName);
        var avatarUrl = trimToEmpty(payload.avatarUrl || payload.AvatarUrl);
        var verified = payload.verified;

        return {
            userId: Number(payload.userId || payload.UserId) || 0,
            fullName: fullName,
            avatarUrl: avatarUrl,
            verified: typeof verified === "boolean" ? verified : !!verified
        };
    }

    function getNotificationDetails(item) {
        return item && (item.details || item.Details) && typeof (item.details || item.Details) === "object"
            ? (item.details || item.Details)
            : {};
    }

    function readNotificationValue(item, names) {
        var details = getNotificationDetails(item);
        var sourceNames = Array.isArray(names) ? names : [names];

        for (var index = 0; index < sourceNames.length; index += 1) {
            var name = sourceNames[index];
            if (item && item[name] !== undefined && item[name] !== null && trimToEmpty(item[name]) !== "" && !(/id$/i.test(name) && Number(item[name]) === 0)) {
                return item[name];
            }

            if (details && details[name] !== undefined && details[name] !== null && trimToEmpty(details[name]) !== "" && !(/id$/i.test(name) && Number(details[name]) === 0)) {
                return details[name];
            }
        }

        return "";
    }

    function formatNotificationDecimal(value) {
        var number = Number(value);
        if (!Number.isFinite(number)) {
            return "";
        }

        return number.toFixed(2).replace(/\.?0+$/, "");
    }

    function formatSignedNotificationDecimal(value) {
        var text = formatNotificationDecimal(value);
        if (!text) {
            return "";
        }

        return Number(value) > 0 ? ("+" + text) : text;
    }

    function buildNotificationMetaRow(label, value) {
        var text = normalizeDisplayText(value);
        if (!text) {
            return "";
        }

        return '<div class="web-pair-popup__meta-row"><span class="web-pair-popup__meta-label">' + escapeHtml(label) + '</span><span class="web-pair-popup__meta-value">' + escapeHtml(text) + "</span></div>";
    }

    function buildNotificationCardLine(label, value) {
        var text = normalizeDisplayText(value);
        if (!text) {
            return "";
        }

        return '<p class="native-notification-card__line"><strong>' + escapeHtml(label) + ':</strong> ' + escapeHtml(text) + "</p>";
    }

    function getTournamentNotificationEyebrow(notificationType) {
        if (notificationType === "MATCH_WIN") {
            return "Ch\u00fac m\u1eebng th\u1eafng tr\u1eadn";
        }

        if (notificationType === "TOURNAMENT_PRIZE") {
            return "Th\u00e0nh t\u00edch gi\u1ea3i \u0111\u1ea5u";
        }

        if (notificationType === "RATING_UPDATED") {
            return "C\u1ed9ng \u0111i\u1ec3m tr\u00ecnh";
        }

        if (notificationType === "PAIR_ACCEPTED") {
            return "Gh\u00e9p c\u1eb7p th\u00e0nh c\u00f4ng";
        }

        if (notificationType === "PAIR_REJECTED") {
            return "Ph\u1ea3n h\u1ed3i l\u1eddi m\u1eddi";
        }

        if (notificationType === "PAIR_CANCELED") {
            return "L\u1eddi m\u1eddi \u0111\u00e3 h\u1ee7y";
        }

        if (notificationType === "PAIR_EXPIRED") {
            return "L\u1eddi m\u1eddi h\u1ebft h\u1ea1n";
        }

        return "Th\u00f4ng b\u00e1o m\u1edbi";
    }

    function buildTournamentNotificationDetailRows(item, mode) {
        var notificationType = getNotificationType(item);
        var rows = [];
        var tournamentId = Number(readNotificationValue(item, ["tournamentId", "TournamentId"]));
        var tournamentTitle = normalizeDisplayText(readNotificationValue(item, ["tournamentTitle", "TournamentTitle"])) || "Gi\u1ea3i \u0111\u1ea5u";
        var matchId = Number(readNotificationValue(item, ["matchId", "MatchId"]));
        var registrationId = Number(readNotificationValue(item, ["registrationId", "winnerRegistrationId", "RegistrationId", "WinnerRegistrationId"]));
        var regCode = readNotificationValue(item, ["regCode", "winnerRegCode", "RegCode", "WinnerRegCode"]);
        var teamText = readNotificationValue(item, ["teamText", "winnerTeamText", "TeamText", "WinnerTeamText"]);
        var scoreText = readNotificationValue(item, ["scoreText", "ScoreText"]);
        var roundLabel = readNotificationValue(item, ["roundLabel", "RoundLabel", "roundKey", "RoundKey"]);
        var groupName = readNotificationValue(item, ["groupName", "GroupName"]);
        var courtText = readNotificationValue(item, ["courtText", "CourtText"]);
        var addressText = readNotificationValue(item, ["addressText", "AddressText"]);
        var startAtText = readNotificationValue(item, ["startAtText", "StartAtText"]);
        var prizeLabel = readNotificationValue(item, ["prizeLabel", "PrizeLabel"]);
        var prizeOrder = readNotificationValue(item, ["prizeOrder", "PrizeOrder"]);
        var ratingLabel = readNotificationValue(item, ["ratingLabel", "RatingLabel"]);
        var ratingDelta = readNotificationValue(item, ["ratingDelta", "RatingDelta"]);
        var ratingBefore = readNotificationValue(item, ["ratingBefore", "RatingBefore"]);
        var ratingAfter = readNotificationValue(item, ["ratingAfter", "RatingAfter"]);

        rows.push(mode === "popup"
            ? buildNotificationMetaRow("Gi\u1ea3i \u0111\u1ea5u", tournamentId > 0 ? (tournamentTitle + " (#" + tournamentId + ")") : tournamentTitle)
            : buildNotificationCardLine("Gi\u1ea3i \u0111\u1ea5u", tournamentId > 0 ? (tournamentTitle + " (#" + tournamentId + ")") : tournamentTitle));

        if (notificationType === "MATCH_WIN") {
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("Tr\u1eadn", matchId > 0 ? ("#" + matchId) : "")
                : buildNotificationCardLine("Tr\u1eadn", matchId > 0 ? ("#" + matchId) : ""));
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("\u0110\u1ed9i th\u1eafng", registrationId > 0 ? ("#" + registrationId + (regCode ? " - " + regCode : "") + (teamText ? " - " + teamText : "")) : teamText)
                : buildNotificationCardLine("\u0110\u1ed9i th\u1eafng", registrationId > 0 ? ("#" + registrationId + (regCode ? " - " + regCode : "") + (teamText ? " - " + teamText : "")) : teamText));
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("T\u1ef7 s\u1ed1", scoreText)
                : buildNotificationCardLine("T\u1ef7 s\u1ed1", scoreText));
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("V\u00f2ng/b\u1ea3ng", [roundLabel, groupName].filter(Boolean).join(" - "))
                : buildNotificationCardLine("V\u00f2ng/b\u1ea3ng", [roundLabel, groupName].filter(Boolean).join(" - ")));
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("Th\u1eddi gian", startAtText)
                : buildNotificationCardLine("Th\u1eddi gian", startAtText));
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("S\u00e2n", courtText)
                : buildNotificationCardLine("S\u00e2n", courtText));
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("\u0110\u1ecba \u0111i\u1ec3m", addressText)
                : buildNotificationCardLine("\u0110\u1ecba \u0111i\u1ec3m", addressText));
        } else if (notificationType === "TOURNAMENT_PRIZE") {
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("Th\u00e0nh t\u00edch", prizeLabel + (prizeOrder ? " #" + prizeOrder : ""))
                : buildNotificationCardLine("Th\u00e0nh t\u00edch", prizeLabel + (prizeOrder ? " #" + prizeOrder : "")));
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("\u0110\u1ed9i \u0111\u0103ng k\u00fd", registrationId > 0 ? ("#" + registrationId + (regCode ? " - " + regCode : "") + (teamText ? " - " + teamText : "")) : teamText)
                : buildNotificationCardLine("\u0110\u1ed9i \u0111\u0103ng k\u00fd", registrationId > 0 ? ("#" + registrationId + (regCode ? " - " + regCode : "") + (teamText ? " - " + teamText : "")) : teamText));
        } else if (notificationType === "RATING_UPDATED") {
            var ratingText = [
                formatSignedNotificationDecimal(ratingDelta),
                ratingLabel,
                ratingBefore !== "" && ratingAfter !== "" ? (formatNotificationDecimal(ratingBefore) + " -> " + formatNotificationDecimal(ratingAfter)) : ""
            ].filter(Boolean).join(" | ");

            rows.push(mode === "popup"
                ? buildNotificationMetaRow("\u0110i\u1ec3m tr\u00ecnh", ratingText)
                : buildNotificationCardLine("\u0110i\u1ec3m tr\u00ecnh", ratingText));
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("L\u00fd do", prizeLabel)
                : buildNotificationCardLine("L\u00fd do", prizeLabel));
        }

        return rows.filter(Boolean).join("");
    }

    function buildTournamentNotificationDetailRows(item, mode) {
        var notificationType = getNotificationType(item);
        var rows = [];
        var tournamentId = Number(readNotificationValue(item, ["tournamentId", "TournamentId"]));
        var tournamentTitle = normalizeDisplayText(readNotificationValue(item, ["tournamentTitle", "TournamentTitle"])) || "Giải đấu";
        var matchId = Number(readNotificationValue(item, ["matchId", "MatchId"]));
        var registrationId = Number(readNotificationValue(item, ["registrationId", "winnerRegistrationId", "RegistrationId", "WinnerRegistrationId"]));
        var regCode = readNotificationValue(item, ["regCode", "winnerRegCode", "RegCode", "WinnerRegCode"]);
        var teamText = readNotificationValue(item, ["teamText", "winnerTeamText", "TeamText", "WinnerTeamText"]);
        var scoreText = readNotificationValue(item, ["scoreText", "ScoreText"]);
        var roundLabel = readNotificationValue(item, ["roundLabel", "RoundLabel", "roundKey", "RoundKey"]);
        var groupName = readNotificationValue(item, ["groupName", "GroupName"]);
        var courtText = readNotificationValue(item, ["courtText", "CourtText"]);
        var addressText = readNotificationValue(item, ["addressText", "AddressText"]);
        var startAtText = readNotificationValue(item, ["startAtText", "StartAtText"]);
        var prizeLabel = readNotificationValue(item, ["prizeLabel", "PrizeLabel"]);
        var prizeOrder = readNotificationValue(item, ["prizeOrder", "PrizeOrder"]);
        var tournamentPrizeId = Number(readNotificationValue(item, ["tournamentPrizeId", "TournamentPrizeId"]));
        var ratingHistoryId = Number(readNotificationValue(item, ["ratingHistoryId", "RatingHistoryId"]));
        var ratingLabel = readNotificationValue(item, ["ratingLabel", "RatingLabel"]);
        var ratingDelta = readNotificationValue(item, ["ratingDelta", "RatingDelta"]);
        var ratingBefore = readNotificationValue(item, ["ratingBefore", "RatingBefore"]);
        var ratingAfter = readNotificationValue(item, ["ratingAfter", "RatingAfter"]);
        var registrationText = registrationId > 0
            ? ("#" + registrationId + (regCode ? " - " + regCode : "") + (teamText ? " - " + teamText : ""))
            : teamText;

        rows.push(mode === "popup"
            ? buildNotificationMetaRow("Giải đấu", tournamentId > 0 ? (tournamentTitle + " (#" + tournamentId + ")") : tournamentTitle)
            : buildNotificationCardLine("Giải đấu", tournamentId > 0 ? (tournamentTitle + " (#" + tournamentId + ")") : tournamentTitle));

        if (notificationType === "MATCH_WIN") {
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("Trận", matchId > 0 ? ("#" + matchId) : "")
                : buildNotificationCardLine("Trận", matchId > 0 ? ("#" + matchId) : ""));
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("Đội thắng", registrationText)
                : buildNotificationCardLine("Đội thắng", registrationText));
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("Tỷ số", scoreText)
                : buildNotificationCardLine("Tỷ số", scoreText));
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("Vòng/bảng", [roundLabel, groupName].filter(Boolean).join(" - "))
                : buildNotificationCardLine("Vòng/bảng", [roundLabel, groupName].filter(Boolean).join(" - ")));
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("Thời gian", startAtText)
                : buildNotificationCardLine("Thời gian", startAtText));
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("Sân", courtText)
                : buildNotificationCardLine("Sân", courtText));
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("Địa điểm", addressText)
                : buildNotificationCardLine("Địa điểm", addressText));
        } else if (notificationType === "TOURNAMENT_PRIZE") {
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("Mã giải thưởng", tournamentPrizeId > 0 ? ("#" + tournamentPrizeId) : "")
                : buildNotificationCardLine("Mã giải thưởng", tournamentPrizeId > 0 ? ("#" + tournamentPrizeId) : ""));
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("Thành tích", prizeLabel + (prizeOrder ? " #" + prizeOrder : ""))
                : buildNotificationCardLine("Thành tích", prizeLabel + (prizeOrder ? " #" + prizeOrder : "")));
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("Đội đăng ký", registrationText)
                : buildNotificationCardLine("Đội đăng ký", registrationText));
        } else if (notificationType === "RATING_UPDATED") {
            var ratingText = [
                formatSignedNotificationDecimal(ratingDelta),
                ratingLabel,
                ratingBefore !== "" && ratingAfter !== "" ? (formatNotificationDecimal(ratingBefore) + " -> " + formatNotificationDecimal(ratingAfter)) : ""
            ].filter(Boolean).join(" | ");

            rows.push(mode === "popup"
                ? buildNotificationMetaRow("Mã cộng điểm", ratingHistoryId > 0 ? ("#" + ratingHistoryId) : "")
                : buildNotificationCardLine("Mã cộng điểm", ratingHistoryId > 0 ? ("#" + ratingHistoryId) : ""));
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("Đội đăng ký", registrationText)
                : buildNotificationCardLine("Đội đăng ký", registrationText));
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("Điểm trình", ratingText)
                : buildNotificationCardLine("Điểm trình", ratingText));
            rows.push(mode === "popup"
                ? buildNotificationMetaRow("Lý do", prizeLabel)
                : buildNotificationCardLine("Lý do", prizeLabel));
        }

        return rows.filter(Boolean).join("");
    }

    function normalizeRealtimePairRequest(payload) {
        var source = payload && payload.details ? payload.details : payload;
        var requestId = Number((source && (source.pairRequestId || source.PairRequestId)) || (payload && (payload.pairRequestId || payload.PairRequestId)));
        var tournamentId = Number((source && (source.tournamentId || source.TournamentId)) || (payload && (payload.tournamentId || payload.TournamentId)));

        if (!Number.isFinite(requestId) || requestId <= 0) {
            return null;
        }

        var tournamentTitle = normalizeDisplayText(source && (source.tournamentTitle || source.TournamentTitle || source.title || source.Title));
        var requestedBy = normalizeUserBrief(source && (source.requestedBy || source.RequestedBy));
        var requestedTo = normalizeUserBrief(source && (source.requestedTo || source.RequestedTo));

        return {
            type: "PAIR_REQUEST",
            pairRequestId: requestId,
            tournamentId: Number.isFinite(tournamentId) && tournamentId > 0 ? tournamentId : 0,
            tournamentTitle: tournamentTitle,
            expiresAt: source && (source.expiresAt || source.ExpiresAt),
            title: normalizeDisplayText(payload && (payload.title || payload.Title)) || "Lời mời ghép đôi",
            message: normalizeDisplayText(payload && (payload.body || payload.Body)) || ((requestedBy && requestedBy.fullName) ? (requestedBy.fullName + " mời bạn ghép cặp.") : "Bạn có lời mời ghép đôi mới."),
            requestedBy: requestedBy,
            requestedTo: requestedTo
        };
    }

    function normalizeRealtimeUserNotification(payload) {
        var source = payload && payload.details ? payload.details : payload;
        var notificationId = Number(payload && (payload.notificationId || payload.NotificationId));
        var pairRequestId = Number((source && (source.pairRequestId || source.PairRequestId)) || (payload && (payload.pairRequestId || payload.PairRequestId)));
        var tournamentId = Number((source && (source.tournamentId || source.TournamentId)) || (payload && (payload.tournamentId || payload.TournamentId)));
        var registrationId = Number((source && (source.registrationId || source.RegistrationId)) || (payload && (payload.registrationId || payload.RegistrationId)));
        var matchId = Number((source && (source.matchId || source.MatchId)) || (payload && (payload.matchId || payload.MatchId)));
        var tournamentPrizeId = Number((source && (source.tournamentPrizeId || source.TournamentPrizeId)) || (payload && (payload.tournamentPrizeId || payload.TournamentPrizeId)));
        var ratingHistoryId = Number((source && (source.ratingHistoryId || source.RatingHistoryId)) || (payload && (payload.ratingHistoryId || payload.RatingHistoryId)));
        var notificationType = getNotificationType(payload);

        if (!notificationType) {
            return null;
        }

        return {
            id: Number.isFinite(notificationId) && notificationId > 0 ? notificationId : 0,
            notificationId: Number.isFinite(notificationId) && notificationId > 0 ? notificationId : 0,
            type: notificationType,
            notificationType: notificationType,
            title: normalizeDisplayText(payload && (payload.title || payload.Title)) || "Thông báo mới",
            message: normalizeDisplayText(payload && (payload.body || payload.Body)) || "Bạn có thông báo ghép đôi mới.",
            createdAt: payload && (payload.createdAt || payload.CreatedAt),
            pairRequestId: Number.isFinite(pairRequestId) && pairRequestId > 0 ? pairRequestId : 0,
            matchId: Number.isFinite(matchId) && matchId > 0 ? matchId : 0,
            tournamentId: Number.isFinite(tournamentId) && tournamentId > 0 ? tournamentId : 0,
            tournamentTitle: normalizeDisplayText(source && (source.tournamentTitle || source.TournamentTitle || source.title || source.Title)),
            registrationId: Number.isFinite(registrationId) && registrationId > 0 ? registrationId : 0,
            tournamentPrizeId: Number.isFinite(tournamentPrizeId) && tournamentPrizeId > 0 ? tournamentPrizeId : 0,
            ratingHistoryId: Number.isFinite(ratingHistoryId) && ratingHistoryId > 0 ? ratingHistoryId : 0,
            responseNote: normalizeDisplayText(source && (source.responseNote || source.ResponseNote)),
            acceptedBy: normalizeUserBrief(source && (source.acceptedBy || source.AcceptedBy)),
            requestedBy: normalizeUserBrief(source && (source.requestedBy || source.RequestedBy)),
            requestedTo: normalizeUserBrief(source && (source.requestedTo || source.RequestedTo)),
            details: source && typeof source === "object" ? source : {},
            isRead: false
        };
    }

    function normalizeRealtimeUserNotification(payload) {
        var source = payload && payload.details ? payload.details : payload;
        var notificationId = Number(payload && (payload.notificationId || payload.NotificationId));
        var pairRequestId = Number((source && (source.pairRequestId || source.PairRequestId)) || (payload && (payload.pairRequestId || payload.PairRequestId)));
        var tournamentId = Number((source && (source.tournamentId || source.TournamentId)) || (payload && (payload.tournamentId || payload.TournamentId)));
        var registrationId = Number((source && (source.registrationId || source.RegistrationId)) || (payload && (payload.registrationId || payload.RegistrationId)));
        var matchId = Number((source && (source.matchId || source.MatchId)) || (payload && (payload.matchId || payload.MatchId)));
        var tournamentPrizeId = Number((source && (source.tournamentPrizeId || source.TournamentPrizeId)) || (payload && (payload.tournamentPrizeId || payload.TournamentPrizeId)));
        var ratingHistoryId = Number((source && (source.ratingHistoryId || source.RatingHistoryId)) || (payload && (payload.ratingHistoryId || payload.RatingHistoryId)));
        var notificationType = getNotificationType(payload);
        var isPairResponse = isPairLifecycleNotificationType(notificationType);

        if (!notificationType) {
            return null;
        }

        return {
            id: Number.isFinite(notificationId) && notificationId > 0 ? notificationId : 0,
            notificationId: Number.isFinite(notificationId) && notificationId > 0 ? notificationId : 0,
            type: notificationType,
            notificationType: notificationType,
            title: normalizeDisplayText(payload && (payload.title || payload.Title)) || (isPairResponse ? "Thông báo ghép đôi" : "Thông báo giải đấu"),
            message: normalizeDisplayText(payload && (payload.body || payload.Body)) || (isPairResponse
                ? "Bạn có thông báo mới về ghép đôi."
                : "Bạn có thông báo mới từ hệ thống giải đấu."),
            createdAt: payload && (payload.createdAt || payload.CreatedAt),
            pairRequestId: Number.isFinite(pairRequestId) && pairRequestId > 0 ? pairRequestId : 0,
            matchId: Number.isFinite(matchId) && matchId > 0 ? matchId : 0,
            tournamentId: Number.isFinite(tournamentId) && tournamentId > 0 ? tournamentId : 0,
            tournamentTitle: normalizeDisplayText(source && (source.tournamentTitle || source.TournamentTitle || source.title || source.Title)),
            registrationId: Number.isFinite(registrationId) && registrationId > 0 ? registrationId : 0,
            tournamentPrizeId: Number.isFinite(tournamentPrizeId) && tournamentPrizeId > 0 ? tournamentPrizeId : 0,
            ratingHistoryId: Number.isFinite(ratingHistoryId) && ratingHistoryId > 0 ? ratingHistoryId : 0,
            responseNote: normalizeDisplayText(source && (source.responseNote || source.ResponseNote)),
            acceptedBy: normalizeUserBrief(source && (source.acceptedBy || source.AcceptedBy)),
            requestedBy: normalizeUserBrief(source && (source.requestedBy || source.RequestedBy)),
            requestedTo: normalizeUserBrief(source && (source.requestedTo || source.RequestedTo)),
            details: source && typeof source === "object" ? source : {},
            isRead: false
        };
    }

    function presentRealtimePairPopup(item) {
        if (!item) {
            return false;
        }

        if (!canPresentRealtimePairPopup()) {
            notificationCenter.queuedPopupItem = item;
            return false;
        }

        showPairRequestPopup(item);
        return true;
    }

    async function performPairRequestAction(requestId, action, options) {
        var targetId = Number(requestId);
        var normalizedAction = trimToEmpty(action).toLowerCase();
        var control = options && options.control ? options.control : null;
        var controls = Array.isArray(options && options.controls) ? options.controls.filter(Boolean) : [];

        if (control && controls.indexOf(control) < 0) {
            controls.unshift(control);
        }

        if (!Number.isFinite(targetId) || targetId <= 0 || (normalizedAction !== "accept" && normalizedAction !== "reject")) {
            throw new Error("Yêu cầu ghép đôi không hợp lệ.");
        }

        var snapshots = controls.map(function (item) {
            return {
                node: item,
                disabled: !!item.disabled,
                text: item.textContent
            };
        });

        controls.forEach(function (item) {
            item.disabled = true;
        });

        if (control) {
        control.textContent = normalizedAction === "reject" ? "Đang từ chối..." : "Đang chấp nhận...";
        }

        try {
            var payload = await requestJson("/api/tournament-registrations/pair-requests/" + targetId + "/" + normalizedAction, {
                method: "POST",
                body: normalizedAction === "reject" ? JSON.stringify({ responseNote: "" }) : null
            });

            dispatchNotificationCenterChange({
                requestId: targetId,
                action: normalizedAction,
                payload: payload
            });

            return payload;
        } finally {
            snapshots.forEach(function (snapshot) {
                if (!snapshot.node) {
                    return;
                }

                snapshot.node.disabled = snapshot.disabled;
                snapshot.node.textContent = snapshot.text;
            });
        }
    }

    async function markUserNotificationRead(notificationId) {
        var targetId = Number(notificationId);
        if (!Number.isFinite(targetId) || targetId <= 0) {
            return false;
        }

        await requestJson("/api/notifications/inbox/" + targetId + "/read", {
            method: "POST"
        });

        dispatchNotificationCenterChange({
            notificationId: targetId,
            action: "read"
        });

        return true;
    }

    async function syncNotificationCenter(options) {
        if (!notificationCenter.authenticated) {
            setNotificationBellCount(0);
            closePairRequestPopup();
            return 0;
        }

        var syncToken = ++notificationCenter.syncToken;
        var requestedPopupId = Number(options && options.popupRequestId);
        var allowPopup = !!(options && options.allowPopup);

        try {
            var results = await Promise.allSettled([
                fetchJson("/api/notifications/pair-requests?includeResponses=true"),
                fetchJson("/api/notifications/upcoming-matches"),
                fetchJson("/api/notifications/inbox")
            ]);

            if (syncToken !== notificationCenter.syncToken) {
                return 0;
            }

            var pairItems = results[0].status === "fulfilled" && Array.isArray(results[0].value && results[0].value.items)
                ? results[0].value.items
                : [];
            var pendingPairTotal = results[0].status === "fulfilled"
                ? Math.max(0, Number(results[0].value && results[0].value.pendingTotal) || 0)
                : 0;
            var matchItems = results[1].status === "fulfilled" && Array.isArray(results[1].value && results[1].value.items)
                ? results[1].value.items
                : [];
            var inboxPayload = results[2].status === "fulfilled" && results[2].value
                ? results[2].value
                : null;
            var unreadNonPairTotal = Math.max(0, Number(inboxPayload && inboxPayload.unreadNonPairTotal) || 0);
            var previousIds = notificationCenter.knownPairRequestIds;
            var activePopupId = Number(notificationCenter.activePopupRequestId);
            var nextPopupItem = null;

            notificationCenter.pendingPairItems = pairItems.slice();
            notificationCenter.knownPairRequestIds = buildPairRequestIdMap(pairItems);
            setNotificationBellCount(pendingPairTotal + matchItems.length + unreadNonPairTotal);

            if (notificationCenter.queuedPopupItem && isPairRequestNotification(notificationCenter.queuedPopupItem)) {
                var queuedId = Number(notificationCenter.queuedPopupItem.pairRequestId);
                if (!findPairRequestById(pairItems, queuedId)) {
                    notificationCenter.queuedPopupItem = null;
                }
            }

            if (activePopupId > 0) {
                nextPopupItem = findPairRequestById(pairItems, activePopupId);
                if (!nextPopupItem) {
                    closePairRequestPopup();
                }
            }

            if (allowPopup) {
                nextPopupItem = findPairRequestById(pairItems, requestedPopupId);

                if (!nextPopupItem) {
                    nextPopupItem = pairItems.find(function (item) {
                        var itemId = Number(item && item.pairRequestId);
                        return Number.isFinite(itemId) && itemId > 0 && !previousIds[String(itemId)];
                    }) || null;
                }

                if (nextPopupItem) {
                    presentRealtimePairPopup(nextPopupItem);
                }
            }

            return pairItems.length + matchItems.length;
        } catch (_error) {
            if (syncToken !== notificationCenter.syncToken) {
                return 0;
            }

            notificationCenter.pendingPairItems = [];
            notificationCenter.knownPairRequestIds = Object.create(null);
            setNotificationBellCount(0);
            closePairRequestPopup();
            return 0;
        }
    }

    function initNotificationCenter() {
        if (notificationCenter.initialized) {
            return;
        }

        notificationCenter.initialized = true;
        setNotificationBellCount(0);

        notificationCenter.onNotificationChange = function () {
            syncNotificationCenter({ allowPopup: false });
        };

        notificationCenter.onVisibilityChange = function () {
            if (document.visibilityState === "visible" && notificationCenter.authenticated) {
                if (notificationCenter.queuedPopupItem) {
                    presentRealtimePairPopup(notificationCenter.queuedPopupItem);
                }

                syncNotificationCenter({ allowPopup: true });
            }
        };

        window.addEventListener(NOTIFICATION_CENTER_EVENT, notificationCenter.onNotificationChange);
        document.addEventListener("visibilitychange", notificationCenter.onVisibilityChange);

        fetchJson("/api/web-auth/me")
            .then(function (session) {
                notificationCenter.authenticated = !!(session && session.isAuthenticated);

                if (!notificationCenter.authenticated) {
                    setNotificationBellCount(0);
                    return;
                }

                connectRealtime();

                if (!notificationCenter.realtimeListener) {
                    notificationCenter.realtimeListener = addRealtimeListener(function (event) {
                        if (trimToEmpty(event && event.type) !== "tournament.notification") {
                            return;
                        }

                        var payload = event && event.payload ? event.payload : {};
                        var notificationType = trimToEmpty(payload.notificationType || payload.NotificationType).toUpperCase();
                        var popupRequestId = Number(payload.pairRequestId || payload.PairRequestId);
                        var popupItem = null;

                        if (notificationType === "PAIR_REQUEST") {
                            popupItem = normalizeRealtimePairRequest(payload);
                        } else if (notificationType) {
                            popupItem = normalizeRealtimeUserNotification(payload);
                        }

                        if (popupItem) {
                            presentRealtimePairPopup(popupItem);
                        }

                        syncNotificationCenter({
                            allowPopup: notificationType === "PAIR_REQUEST",
                            popupRequestId: popupRequestId
                        });
                    });
                }

                syncNotificationCenter({ allowPopup: true });
            })
            .catch(function () {
                notificationCenter.authenticated = false;
                setNotificationBellCount(0);
            });

        window.addEventListener("pagehide", function () {
            closePairRequestPopup();

            if (notificationCenter.realtimeListener) {
                notificationCenter.realtimeListener();
                notificationCenter.realtimeListener = null;
            }

            if (notificationCenter.onNotificationChange) {
                window.removeEventListener(NOTIFICATION_CENTER_EVENT, notificationCenter.onNotificationChange);
                notificationCenter.onNotificationChange = null;
            }

            if (notificationCenter.onVisibilityChange) {
                document.removeEventListener("visibilitychange", notificationCenter.onVisibilityChange);
                notificationCenter.onVisibilityChange = null;
            }
        }, { once: true });
    }

    var realtime = {
        ws: null,
        reconnectTimer: null,
        pingTimer: null,
        manualClose: false,
        listeners: [],
        openHandlers: [],
        subscriptions: {},
        directSubscriptions: {},
        reconnectDelay: 2500
    };

    function buildRealtimeUrl() {
        var protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
        return protocol + "//" + window.location.host + "/ws";
    }

    function emitRealtime(event) {
        realtime.listeners.slice().forEach(function (listener) {
            try {
                listener(event);
            } catch (_error) {
            }
        });
    }

    function addRealtimeListener(listener) {
        if (typeof listener !== "function") {
            return function () { };
        }

        realtime.listeners.push(listener);
        return function () {
            realtime.listeners = realtime.listeners.filter(function (item) {
                return item !== listener;
            });
        };
    }

    function startRealtimePing() {
        window.clearInterval(realtime.pingTimer);
        realtime.pingTimer = window.setInterval(function () {
            sendRealtime({ type: "ping" });
        }, 25000);
    }

    function flushRealtimeSubscriptions() {
        Object.keys(realtime.subscriptions).forEach(function (clubId) {
            if (realtime.subscriptions[clubId]) {
                sendRealtime({
                    type: "club.subscribe",
                    clubId: Number(clubId)
                });
            }
        });

        Object.keys(realtime.directSubscriptions).forEach(function (roomId) {
            if (realtime.directSubscriptions[roomId]) {
                sendRealtime({
                    type: "direct.subscribe",
                    roomId: Number(roomId)
                });
            }
        });
    }

    function connectRealtime() {
        if (!("WebSocket" in window)) {
            return false;
        }

        if (realtime.ws && (
            realtime.ws.readyState === WebSocket.OPEN ||
            realtime.ws.readyState === WebSocket.CONNECTING
        )) {
            return true;
        }

        realtime.manualClose = false;

        try {
            realtime.ws = new WebSocket(buildRealtimeUrl());
        } catch (_error) {
            return false;
        }

        realtime.ws.addEventListener("open", function () {
            emitRealtime({ type: "__socket_open__" });
            flushRealtimeSubscriptions();
            startRealtimePing();
        });

        realtime.ws.addEventListener("message", function (event) {
            try {
                emitRealtime(JSON.parse(event.data));
            } catch (_error) {
            }
        });

        realtime.ws.addEventListener("close", function () {
            emitRealtime({ type: "__socket_close__" });
            window.clearInterval(realtime.pingTimer);
            realtime.ws = null;

            if (!realtime.manualClose) {
                window.clearTimeout(realtime.reconnectTimer);
                realtime.reconnectTimer = window.setTimeout(function () {
                    connectRealtime();
                }, realtime.reconnectDelay);
            }
        });

        realtime.ws.addEventListener("error", function () {
            emitRealtime({ type: "__socket_error__" });
        });

        return true;
    }

    function sendRealtime(payload) {
        if (!realtime.ws || realtime.ws.readyState !== WebSocket.OPEN) {
            connectRealtime();
            return false;
        }

        try {
            realtime.ws.send(JSON.stringify(payload));
            return true;
        } catch (_error) {
            return false;
        }
    }

    function subscribeClubRealtime(clubId) {
        var id = Number(clubId);
        if (!Number.isFinite(id) || id <= 0) {
            return false;
        }

        realtime.subscriptions[String(id)] = true;
        connectRealtime();
        return sendRealtime({ type: "club.subscribe", clubId: id });
    }

    function unsubscribeClubRealtime(clubId) {
        var id = Number(clubId);
        if (!Number.isFinite(id) || id <= 0) {
            return false;
        }

        delete realtime.subscriptions[String(id)];
        return sendRealtime({ type: "club.unsubscribe", clubId: id });
    }

    function sendClubTypingRealtime(clubId, isTyping) {
        var id = Number(clubId);
        if (!Number.isFinite(id) || id <= 0) {
            return false;
        }

        return sendRealtime({
            type: "club.typing",
            clubId: id,
            isTyping: !!isTyping
        });
    }

    function subscribeDirectRealtime(roomId) {
        var id = Number(roomId);
        if (!Number.isFinite(id) || id <= 0) {
            return false;
        }

        realtime.directSubscriptions[String(id)] = true;
        connectRealtime();
        return sendRealtime({ type: "direct.subscribe", roomId: id });
    }

    function unsubscribeDirectRealtime(roomId) {
        var id = Number(roomId);
        if (!Number.isFinite(id) || id <= 0) {
            return false;
        }

        delete realtime.directSubscriptions[String(id)];
        return sendRealtime({ type: "direct.unsubscribe", roomId: id });
    }

    function sendDirectTypingRealtime(roomId, isTyping) {
        var id = Number(roomId);
        if (!Number.isFinite(id) || id <= 0) {
            return false;
        }

        return sendRealtime({
            type: "direct.typing",
            roomId: id,
            isTyping: !!isTyping
        });
    }

    var publicPageRealtime = {
        ws: null,
        reconnectTimer: null,
        pingTimer: null,
        manualClose: false,
        listeners: [],
        matchSubscriptions: {},
        videosSubscribed: false,
        reconnectDelay: 2500
    };

    function buildPublicRealtimeUrl() {
        var protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
        return protocol + "//" + window.location.host + "/ws-public";
    }

    function emitPublicRealtime(event) {
        publicPageRealtime.listeners.slice().forEach(function (listener) {
            try {
                listener(event);
            } catch (_error) {
            }
        });
    }

    function addPublicRealtimeListener(listener) {
        if (typeof listener !== "function") {
            return function () { };
        }

        publicPageRealtime.listeners.push(listener);
        return function () {
            publicPageRealtime.listeners = publicPageRealtime.listeners.filter(function (item) {
                return item !== listener;
            });
        };
    }

    function sendPublicRealtime(payload) {
        if (!publicPageRealtime.ws || publicPageRealtime.ws.readyState !== WebSocket.OPEN) {
            connectPublicRealtime();
            return false;
        }

        try {
            publicPageRealtime.ws.send(JSON.stringify(payload));
            return true;
        } catch (_error) {
            return false;
        }
    }

    function flushPublicRealtimeSubscriptions() {
        if (publicPageRealtime.videosSubscribed) {
            sendPublicRealtime({ type: "videos.subscribe" });
        }

        Object.keys(publicPageRealtime.matchSubscriptions).forEach(function (key) {
            if (!publicPageRealtime.matchSubscriptions[key]) {
                return;
            }

            sendPublicRealtime({
                type: "match.subscribe",
                matchId: Number(key)
            });
        });
    }

    function connectPublicRealtime() {
        if (!("WebSocket" in window)) {
            return false;
        }

        if (publicPageRealtime.ws && (
            publicPageRealtime.ws.readyState === WebSocket.OPEN ||
            publicPageRealtime.ws.readyState === WebSocket.CONNECTING
        )) {
            return true;
        }

        publicPageRealtime.manualClose = false;

        try {
            publicPageRealtime.ws = new WebSocket(buildPublicRealtimeUrl());
        } catch (_error) {
            return false;
        }

        publicPageRealtime.ws.addEventListener("open", function () {
            emitPublicRealtime({ type: "__public_socket_open__" });
            flushPublicRealtimeSubscriptions();
            window.clearInterval(publicPageRealtime.pingTimer);
            publicPageRealtime.pingTimer = window.setInterval(function () {
                sendPublicRealtime({ type: "ping" });
            }, 25000);
        });

        publicPageRealtime.ws.addEventListener("message", function (event) {
            try {
                emitPublicRealtime(JSON.parse(event.data));
            } catch (_error) {
            }
        });

        publicPageRealtime.ws.addEventListener("close", function () {
            emitPublicRealtime({ type: "__public_socket_close__" });
            window.clearInterval(publicPageRealtime.pingTimer);
            publicPageRealtime.ws = null;

            if (!publicPageRealtime.manualClose) {
                window.clearTimeout(publicPageRealtime.reconnectTimer);
                publicPageRealtime.reconnectTimer = window.setTimeout(function () {
                    connectPublicRealtime();
                }, publicPageRealtime.reconnectDelay);
            }
        });

        publicPageRealtime.ws.addEventListener("error", function () {
            emitPublicRealtime({ type: "__public_socket_error__" });
        });

        return true;
    }

    function subscribeVideosFeedRealtime() {
        publicPageRealtime.videosSubscribed = true;
        connectPublicRealtime();
        return sendPublicRealtime({ type: "videos.subscribe" });
    }

    function subscribeMatchPublicRealtime(matchId) {
        var id = Number(matchId);
        if (!Number.isFinite(id) || id <= 0) {
            return false;
        }

        publicPageRealtime.matchSubscriptions[String(id)] = true;
        connectPublicRealtime();
        return sendPublicRealtime({ type: "match.subscribe", matchId: id });
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
            action.disabled = false;
            action.removeAttribute("aria-label");
            action.className = "native-page-header__action";
            spacer.hidden = false;
            return;
        }

        action.hidden = false;
        action.className = "native-page-header__action" + (options.className ? (" " + options.className) : "");
        action.innerHTML = options.html || "";
        action.onclick = options.onClick || null;
        action.disabled = !!options.disabled;

        if (options.ariaLabel) {
            action.setAttribute("aria-label", options.ariaLabel);
        } else {
            action.removeAttribute("aria-label");
        }

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

    function redirectToWebLogin(returnUrl) {
        var target = trimToEmpty(returnUrl) || (window.location.pathname + window.location.search);
        window.location.href = "/PickleballWeb/Login?returnUrl=" + encodeURIComponent(target);
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
        var isJoining = !!item.isJoining;

        if (status === "MANAGER") {
            return '<a class="native-club-card__button native-club-card__button--green" href="/PickleballWeb/Club/' + escapeHtml(item.clubId) + '">Quản lý</a>';
        }

        if (status === "MEMBER") {
            return '<span class="native-club-card__button native-club-card__button--green is-disabled">Thành viên</span>';
        }

        if (status === "PENDING") {
            return '<button class="native-club-card__button native-club-card__button--amber' + (isJoining ? " is-loading" : "") + '" type="button" data-club-cancel="' + escapeHtml(item.clubId) + '"' + (isJoining ? " disabled" : "") + ">" + (isJoining ? "Đang hủy..." : "Chờ duyệt") + "</button>";
        }

        return '<button class="native-club-card__button native-club-card__button--red' + (isJoining ? " is-loading" : "") + '" type="button" data-club-join="' + escapeHtml(item.clubId) + '"' + (isJoining ? " disabled" : "") + ">" + (isJoining ? "Đang gửi..." : "Xin vào") + "</button>";
    }

    function renderClubCard(item) {
        var coverUrl = trimToEmpty(item.coverUrl);
        var membersCount = Number(item.membersCount || 0);
        var areaText = trimToEmpty(item.areaText) || "Chưa có khu vực";
        var ratingAvg = Number(item.ratingAvg || 0);

        return [
            '<article class="native-club-card">',
            coverUrl
                ? '<img class="native-club-card__cover" src="' + escapeHtml(coverUrl) + '" alt="' + escapeHtml(item.clubName || "CLB") + '" loading="lazy">'
                : '<div class="native-club-card__cover native-club-card__cover--fallback"><ion-icon name="image-outline"></ion-icon></div>',
            '<div class="native-club-card__body">',
            '<h2 class="native-club-card__title">' + escapeHtml(trimToEmpty(item.clubName) || "CLB Hanaka") + (membersCount > 0 ? " (" + membersCount + " tv)" : "") + "</h2>",
            '<div class="native-club-card__rating"><span>' + ratingAvg.toFixed(1) + "</span><span class=\"native-club-card__stars\">" + ratingStars(ratingAvg) + '</span><span>(' + escapeHtml(item.reviewsCount || 0) + ' Đánh giá)</span></div>',
            '<p class="native-club-card__meta">Khu vực: ' + escapeHtml(areaText) + "</p>",
            '<div class="native-club-card__stats">',
            '<span>Trận: ' + escapeHtml(item.matchesPlayed || 0) + "</span>",
            '<span>Thắng: ' + escapeHtml(item.matchesWin || 0) + "</span>",
            '<span>Hoa: ' + escapeHtml(item.matchesDraw || 0) + "</span>",
            '<span>Thua: ' + escapeHtml(item.matchesLoss || 0) + "</span>",
            "</div>",
            '<div class="native-club-card__actions">',
            renderClubButton(item),
            '<a class="native-club-card__button native-club-card__button--cyan" href="/PickleballWeb/Club/' + escapeHtml(item.clubId) + '">Xem chi tiết</a>',
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
            mine ? '<span class="' + badgeClass + '">Tôi</span>' : "",
            "</div>",
            '<span class="native-table-row__city">' + escapeHtml(trimToEmpty(item.city) || "Chưa cập nhật") + "</span>",
            '<span class="native-table-row__status ' + (item.verified ? "is-good" : "is-bad") + '">' + (item.verified ? "Đã xác thực" : "Chưa xác thực") + "</span>",
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
            '<h2>' + escapeHtml(trimToEmpty(item.courtName) || "Sân Hanaka") + "</h2>",
            '<p>Khu vực: <strong>' + escapeHtml(trimToEmpty(item.areaText) || "Chưa cập nhật") + "</strong></p>",
            '<p>Quản lý: <strong>' + escapeHtml(trimToEmpty(item.managerName) || "Chưa cập nhật") + "</strong></p>",
            '<p>Điện thoại: <strong>' + escapeHtml(trimToEmpty(item.phone) || "Chưa cập nhật") + "</strong></p>",
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
            return { text: "Đang mở đăng ký", className: "is-open" };
        }

        if (normalized === "CLOSED") {
            return { text: "Đã đóng đăng ký", className: "is-closed" };
        }

        if (normalized === "FINISHED") {
            return { text: "Đã kết thúc", className: "is-finished" };
        }

        return { text: normalized || "Không xác định", className: "is-draft" };
    }

    function renderTournamentCard(item) {
        var bannerUrl = trimToEmpty(item.bannerUrl);
        var gameTypeLabel = tournamentGameTypeLabel(item.gameType, item.genderCategory, item.tournamentTypeLabel);
        var singleLimit = formatFlexibleNumber(item.singleLimit);
        var doubleLimit = formatFlexibleNumber(item.doubleLimit);

        return [
            '<article class="native-tournament-card">',
            bannerUrl
                ? '<img class="native-tournament-card__banner" src="' + escapeHtml(bannerUrl) + '" alt="' + escapeHtml(item.title || "Giải đấu") + '" loading="lazy">'
                : '<div class="native-tournament-card__banner native-tournament-card__banner--fallback"><ion-icon name="image-outline"></ion-icon></div>',
            '<a class="native-tournament-card__body" href="/PickleballWeb/Tournament/' + escapeHtml(item.tournamentId) + '">',
            '<h2>' + escapeHtml(trimToEmpty(item.title) || "Giải đấu Hanaka") + "</h2>",
            '<p>Ngày: <strong>' + escapeHtml(formatDateTime(item.startTime) || "-") + "</strong></p>",
            '<p>Hạn đăng ký: <strong>' + escapeHtml(formatDateTime(item.registerDeadline) || "-") + "</strong></p>",
            '<div class="native-tournament-card__split"><p>Thể thức: <strong>' + escapeHtml(trimToEmpty(item.formatText) || "-") + "</strong></p><p>Giải: <strong>" + escapeHtml(gameTypeLabel) + "</strong></p></div>",
            '<div class="native-tournament-card__split"><p>Giới hạn trình đơn tối đa: <strong>' + escapeHtml(singleLimit) + "</strong></p><p>Cặp tối đa: <strong>" + escapeHtml(doubleLimit) + "</strong></p></div>",
            '<p>Khu vực: <strong>' + escapeHtml(trimToEmpty(item.areaText) || "-") + "</strong></p>",
            '<div class="native-tournament-card__split"><p>Số đội dự kiến: <strong>' + escapeHtml(item.expectedTeams ?? 0) + "</strong></p><p>Số trận thi đấu: <strong>" + escapeHtml(item.matchesCount ?? 0) + "</strong></p></div>",
            '<p>Tình trạng: <strong>' + escapeHtml(trimToEmpty(item.stateText) || trimToEmpty(item.statusText) || trimToEmpty(item.status) || "-") + "</strong></p>",
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
            '<div class="native-exchange-card__badge"><ion-icon name="flash-outline"></ion-icon><span>Đang khiêu chiến</span></div>',
            '<h2>' + escapeHtml(trimToEmpty(item.clubName) || "Câu lạc bộ") + "</h2>",
            '<p><ion-icon name="location-outline"></ion-icon><span>' + escapeHtml(trimToEmpty(item.areaText) || "Chưa có khu vực") + "</span></p>",
            '<p><ion-icon name="people-outline"></ion-icon><span>' + escapeHtml(item.membersCount || 0) + ' thành viên</span></p>',
            '<p><ion-icon name="time-outline"></ion-icon><span>Cập nhật: ' + escapeHtml(formatUpdatedTime(item.challengeUpdatedAt || item.updatedAt || item.createdAt)) + "</span></p>",
            '<div class="native-exchange-card__stats">',
            '<div><strong>' + escapeHtml(item.matchesPlayed || 0) + '</strong><span>Trận</span></div>',
            '<div><strong>' + escapeHtml(item.matchesWin || 0) + '</strong><span>Thắng</span></div>',
            '<div><strong>' + escapeHtml(item.matchesDraw || 0) + '</strong><span>Hoa</span></div>',
            '<div><strong>' + escapeHtml(item.matchesLoss || 0) + '</strong><span>Thua</span></div>',
            "</div>",
            '<a class="native-exchange-card__detail" href="/PickleballWeb/Club/' + escapeHtml(item.clubId) + '">Xem chi tiết</a>',
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
                ? '<img class="native-match-card__banner" src="' + escapeHtml(bannerUrl) + '" alt="' + escapeHtml(item.title || "Giải đấu") + '" loading="lazy">'
                : '<div class="native-match-card__banner native-match-card__banner--fallback"><ion-icon name="image-outline"></ion-icon><span>Không có banner</span></div>',
            '<a class="native-match-card__body" href="/PickleballWeb/Tournament/' + escapeHtml(item.tournamentId) + '">',
            '<div class="native-match-card__top">',
            '<div class="native-match-card__headcopy">',
            '<h2>' + escapeHtml(trimToEmpty(item.title) || "Giải đấu Hanaka") + "</h2>",
            '<span>' + escapeHtml(trimToEmpty(item.gameType) || "-") + ' • ' + escapeHtml(trimToEmpty(item.formatText) || "Chưa có thể thức") + "</span>",
            "</div>",
            '<span class="native-match-card__status ' + escapeHtml(statusInfo.className) + '">' + escapeHtml(trimToEmpty(item.statusText) || statusInfo.text) + "</span>",
            "</div>",
            '<p><ion-icon name="calendar-outline"></ion-icon><span>' + escapeHtml(formatDateTime(item.startTime) || "Chưa có lịch") + "</span></p>",
            item.registerDeadline
                ? '<p><ion-icon name="time-outline"></ion-icon><span>Hạn đăng ký: ' + escapeHtml(formatDateTime(item.registerDeadline)) + "</span></p>"
                : "",
            location
                ? '<p><ion-icon name="location-outline"></ion-icon><span>' + escapeHtml(location) + "</span></p>"
                : "",
            '<div class="native-match-card__grid">',
            '<div><small>Số đội dự kiến</small><strong>' + escapeHtml(item.expectedTeams ?? 0) + "</strong></div>",
            '<div><small>Đã đăng ký</small><strong>' + escapeHtml(item.registeredCount ?? 0) + "</strong></div>",
            '<div><small>Đã ghép cặp</small><strong>' + escapeHtml(item.pairedCount ?? 0) + "</strong></div>",
            '<div><small>Số trận</small><strong>' + escapeHtml(item.matchesCount ?? 0) + "</strong></div>",
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
                            type === "phone" ? "Điện thoại" :
                                type === "email" ? "Email" : "Liên kết"
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

        setHeaderTitle(root, "Hướng dẫn sử dụng, giới thiệu ứng dụng");
        setHeaderAction(root, null);
        setHeaderExtra(root, "");
        renderEmptyState(refs, "Chưa có dữ liệu liên hệ");

        (async function () {
            try {
                var payload = await fetchJson("/api/links");
                var items = Array.isArray(payload && payload.items) ? payload.items : [];

                refs.list.innerHTML = [
                    '<section class="native-guide-section">',
                    '<h2>Thông tin Hanaka Sport</h2>',
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
                    error: "Không tải được thông tin hướng dẫn.",
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
            joiningClubId: null,
            session: null,
            error: ""
        };

        setHeaderTitle(root, "Pickleball");
        setHeaderAction(root, {
            html: '<ion-icon name="add"></ion-icon>',
            onClick: function () {
                if (!(state.session && state.session.isAuthenticated)) {
                    window.alert("Bạn chưa đăng nhập. Vui lòng đăng nhập để tạo câu lạc bộ.");
                    redirectToWebLogin("/PickleballWeb/Clubs");
                    return;
                }

                showAppOnlyAlert("Chức năng tạo câu lạc bộ hiện được thực hiện trong ứng dụng.");
            }
        });
        setHeaderExtra(root, [
            '<form class="native-inline-search" data-native-search-form>',
            '<label class="native-inline-search__box">',
            '<input type="search" placeholder="Tìm kiếm CLB..." autocomplete="off" data-native-search-input>',
            '<button type="submit" aria-label="Tìm kiếm"><ion-icon name="search"></ion-icon></button>',
            "</label>",
            "</form>",
            '<div class="native-inline-filter"><ion-icon name="location-outline"></ion-icon><span data-native-filter-text>Tất cả câu lạc bộ</span></div>'
        ].join(""));
        renderEmptyState(refs, "Không có câu lạc bộ nào");

        var form = qs("[data-native-search-form]", root);
        var input = qs("[data-native-search-input]", root);
        var filterText = qs("[data-native-filter-text]", root);

        refs.list.addEventListener("click", function (event) {
            var button = event.target.closest("[data-club-join]");
            var cancelButton = event.target.closest("[data-club-cancel]");
            if (button) {
                event.preventDefault();
                handleJoinClub(button);
                return;
            }

            if (cancelButton) {
                event.preventDefault();
                handleCancelJoinClub(cancelButton);
            }
        });

        function render() {
            refs.list.className = "native-page-list native-page-list--cards";
            refs.list.innerHTML = state.items.map(function (item) {
                var clubId = Number(item && item.clubId);
                return renderClubCard(Object.assign({}, item, {
                    isJoining: Number.isFinite(clubId) && clubId === state.joiningClubId
                }));
            }).join("");
            if (filterText) {
                filterText.textContent = state.query ? ("Từ khóa: " + state.query) : "Tất cả câu lạc bộ";
            }

            toggleCommonState(refs, {
                loading: state.loading,
                itemsLength: state.items.length,
                error: state.error,
                hasMore: state.items.length < state.total
            });
        }

        async function refreshSession() {
            try {
                state.session = await requestJson("/api/web-auth/me", { method: "GET" });
            } catch (_error) {
                state.session = { isAuthenticated: false };
            }
        }

        function updateClubRelation(clubId, relation) {
            var nextStatus = trimToEmpty(relation && relation.myClubStatus) || "PENDING";

            state.items = state.items.map(function (club) {
                if (Number(club && club.clubId) !== clubId) {
                    return club;
                }

                return Object.assign({}, club, {
                    myClubStatus: nextStatus,
                    myMemberRole: nextStatus === "NONE" ? null : (trimToEmpty(relation && relation.myMemberRole) || "MEMBER"),
                    canManage: !!(relation && relation.canManage)
                });
            });
        }

        async function handleJoinClub(button) {
            var clubId = Number(button.getAttribute("data-club-join"));
            if (!Number.isFinite(clubId) || clubId <= 0 || state.joiningClubId) {
                return;
            }

            if (!(state.session && state.session.isAuthenticated)) {
                await refreshSession();
            }

            if (!(state.session && state.session.isAuthenticated)) {
                window.alert("Bạn chưa đăng nhập. Vui lòng đăng nhập để gửi yêu cầu tham gia câu lạc bộ.");
                redirectToWebLogin("/PickleballWeb/Clubs");
                return;
            }

            state.joiningClubId = clubId;
            render();

            try {
                var payload = await requestJson("/api/clubs/" + clubId + "/join", {
                    method: "POST"
                });

                updateClubRelation(clubId, {
                    myClubStatus: trimToEmpty(payload && payload.myClubStatus) || "PENDING",
                    myMemberRole: "MEMBER",
                    canManage: false
                });
                window.alert(trimToEmpty(payload && payload.message) || "Đã gửi yêu cầu tham gia. Vui lòng chờ duyệt.");
            } catch (error) {
                if (error && error.status === 401) {
                    state.session = { isAuthenticated: false };
                    window.alert("Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại để gửi yêu cầu tham gia câu lạc bộ.");
                    redirectToWebLogin("/PickleballWeb/Clubs");
                    return;
                }

                if (error && error.payload && error.payload.myClubStatus) {
                    updateClubRelation(clubId, error.payload);
                }

                window.alert((error && error.message) || "Không thể gửi yêu cầu tham gia.");
            } finally {
                state.joiningClubId = null;
                render();
            }
        }

        async function handleCancelJoinClub(button) {
            var clubId = Number(button.getAttribute("data-club-cancel"));
            if (!Number.isFinite(clubId) || clubId <= 0 || state.joiningClubId) {
                return;
            }

            if (!(state.session && state.session.isAuthenticated)) {
                await refreshSession();
            }

            if (!(state.session && state.session.isAuthenticated)) {
                window.alert("Bạn chưa đăng nhập. Vui lòng đăng nhập để hủy yêu cầu tham gia câu lạc bộ.");
                redirectToWebLogin("/PickleballWeb/Clubs");
                return;
            }

            state.joiningClubId = clubId;
            render();

            try {
                var payload = await requestJson("/api/clubs/" + clubId + "/join", {
                    method: "DELETE"
                });

                updateClubRelation(clubId, {
                    myClubStatus: trimToEmpty(payload && payload.myClubStatus) || "NONE",
                    myMemberRole: null,
                    canManage: false
                });
                window.alert(trimToEmpty(payload && payload.message) || "Đã hủy yêu cầu tham gia CLB.");
            } catch (error) {
                if (error && error.status === 401) {
                    state.session = { isAuthenticated: false };
                    window.alert("Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại để hủy yêu cầu tham gia câu lạc bộ.");
                    redirectToWebLogin("/PickleballWeb/Clubs");
                    return;
                }

                if (error && error.payload && error.payload.myClubStatus) {
                    updateClubRelation(clubId, error.payload);
                }

                window.alert((error && error.message) || "Không thể hủy yêu cầu tham gia.");
            } finally {
                state.joiningClubId = null;
                render();
            }
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
                state.error = "Không tải được danh sách câu lạc bộ.";
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

        refreshSession().finally(function () {
            load(true);
        });
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
            '<input type="search" placeholder="Tìm kiếm..." autocomplete="off" data-native-search-input>',
            '<ion-icon name="search"></ion-icon>',
            "</label>",
            "</div>",
            '<div class="native-table-head">',
            '<div class="native-table-head__stt">STT</div>',
            '<div class="native-table-head__member">' + escapeHtml(options.memberLabel) + '</div>',
            '<div class="native-table-head__scores"><span>Điểm đơn</span><span>Điểm đôi</span></div>',
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
            '<input type="search" placeholder="Tìm kiếm..." autocomplete="off" data-native-search-input>',
            '<ion-icon name="search"></ion-icon>',
            "</label>",
            "</div>"
        ].join(""));
        renderEmptyState(refs, "Không có sân nào");

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
                state.error = "Không tải được danh sách sân.";
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
            '<input type="search" placeholder="Tìm kiếm..." autocomplete="off" data-native-search-input>',
            '<ion-icon name="search"></ion-icon>',
            "</label>",
            "</div>",
            '<div class="native-inline-filter"><span data-native-filter-text>Tất cả giải đấu</span></div>',
            '<div class="native-tabs">',
            '<button class="native-tabs__item is-active" type="button" data-native-tab="ongoing">Đang</button>',
            '<button class="native-tabs__item" type="button" data-native-tab="finished">Ket thuc</button>',
            "</div>"
        ].join(""));
        renderEmptyState(refs, "Không có giải đấu nào");

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
                state.error = "Không tải được danh sách giải đấu.";
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
            '<input type="search" placeholder="Tìm CLB Đang khiêu chiến..." autocomplete="off" data-native-search-input>',
            '<button type="submit" aria-label="Tìm kiếm"><ion-icon name="search"></ion-icon></button>',
            "</label>",
            "</form>",
            '<div class="native-inline-filter native-inline-filter--success"><ion-icon name="flash-outline"></ion-icon><span data-native-filter-text>Đang hiển thị CLB bật khiêu chiến</span></div>'
        ].join(""));
        renderEmptyState(refs, "Không có CLB nào đang khiêu chiến");

        var form = qs("[data-native-search-form]", root);
        var input = qs("[data-native-search-input]", root);
        var filterText = qs("[data-native-filter-text]", root);

        function render() {
            refs.list.className = "native-page-list native-page-list--cards";
            refs.list.innerHTML = state.items.map(renderChallengeClubCard).join("");

            if (filterText) {
                filterText.textContent = state.query ? ("Từ khóa: " + state.query) : "Đang hiển thị CLB bật khiêu chiến";
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
                state.error = "Không tải được danh sách CLB đang khiêu chiến.";
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

        setHeaderTitle(root, "Danh sách giải đấu");
        setHeaderAction(root, null);
        setHeaderExtra(root, [
            '<div class="native-match-filter">',
            '<h2>Tìm kiếm & lọc</h2>',
            '<label class="native-inline-search__box native-inline-search__box--match">',
            '<input type="search" placeholder="Tìm tên giải, địa điểm, người tổ chức..." autocomplete="off" data-match-query-input>',
            '<ion-icon name="search"></ion-icon>',
            '</label>',
            '<div class="native-match-filter__dates">',
            '<label class="native-match-filter__date"><ion-icon name="calendar-clear-outline"></ion-icon><span data-match-from-label>Từ ngày</span><input type="date" data-match-from-input></label>',
            '<label class="native-match-filter__date"><ion-icon name="calendar-clear-outline"></ion-icon><span data-match-to-label>Đến ngày</span><input type="date" data-match-to-input></label>',
            '</div>',
            '<div class="native-match-filter__actions">',
            '<button class="native-match-filter__clear" type="button" data-match-clear><ion-icon name="refresh-outline"></ion-icon><span>Dat lai</span></button>',
            '<button class="native-match-filter__apply" type="button" data-match-apply><ion-icon name="funnel-outline"></ion-icon><span>Lọc dữ liệu</span></button>',
            '</div>',
            '</div>'
        ].join(""));
        renderEmptyState(refs, "Không có giải đấu");

        var queryInput = qs("[data-match-query-input]", root);
        var fromInput = qs("[data-match-from-input]", root);
        var toInput = qs("[data-match-to-input]", root);
        var fromLabel = qs("[data-match-from-label]", root);
        var toLabel = qs("[data-match-to-label]", root);
        var clearButton = qs("[data-match-clear]", root);
        var applyButton = qs("[data-match-apply]", root);

        function syncDateLabels() {
            if (fromLabel) {
                fromLabel.textContent = state.formFrom ? formatDateOnly(state.formFrom) : "Từ ngày";
            }

            if (toLabel) {
                toLabel.textContent = state.formTo ? formatDateOnly(state.formTo) : "Đến ngày";
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
                state.error = "Không tải được danh sách giải đấu.";
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
                    window.alert("Ngày bắt đầu phải nhỏ hơn hoặc bằng ngày kết thúc.");
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

        return "Chưa xác định đối thủ";
    }

    function renderNotificationCard(item) {
        var notificationType = getNotificationType(item);

        if (notificationType === "PAIR_REQUEST") {
            return renderPairRequestNotificationCard(item);
        }

        if (notificationType === "TOURNAMENT_MATCH") {
            return renderMatchNotificationCard(item);
        }

        return renderUserNotificationCard(item);
    }

    function renderMatchNotificationCard(item) {
        var startAtText = trimToEmpty(item && item.match && item.match.startAtText) || "Chưa cập nhật";
        var addressText = trimToEmpty(item && item.match && item.match.addressText) || "Chưa cập nhật";
        var courtText = trimToEmpty(item && item.match && item.match.courtText) || "Chưa cập nhật";

        return [
            '<article class="native-notification-card native-notification-card--match">',
            '<h2 class="native-notification-card__title">' + escapeHtml(normalizeDisplayText(item && item.title) || "Hanaka Sport - Thông báo") + "</h2>",
            '<p class="native-notification-card__line">' + escapeHtml(normalizeDisplayText(item && item.message) || "Thông báo sẽ được cập nhật tại đây.") + "</p>",
            '<p class="native-notification-card__line"><strong>Đối thủ:</strong> ' + escapeHtml(buildNotificationOpponentText(item)) + "</p>",
            '<p class="native-notification-card__line"><strong>Thời gian:</strong> ' + escapeHtml(startAtText) + "</p>",
            '<p class="native-notification-card__line"><strong>Địa điểm:</strong> ' + escapeHtml(addressText) + "</p>",
            '<p class="native-notification-card__line"><strong>San:</strong> ' + escapeHtml(courtText) + "</p>",
            '<p class="native-notification-card__time">' + escapeHtml(startAtText) + "</p>",
            "</article>"
        ].join("");
    }

    function getUserNotificationActor(item) {
        if (item && item.acceptedBy) {
            return item.acceptedBy;
        }

        if (item && item.requestedTo) {
            return item.requestedTo;
        }

        if (item && item.requestedBy) {
            return item.requestedBy;
        }

        return null;
    }

    function buildNotificationDetailHref(item) {
        var tournamentId = Number(readNotificationValue(item, ["tournamentId", "TournamentId"]));
        var notificationType = getNotificationType(item);
        var matchId = Number(readNotificationValue(item, ["matchId", "MatchId"]));

        if (notificationType === "MATCH_WIN" && matchId > 0) {
            return "/PickleballWeb/Match/" + matchId;
        }

        return tournamentId > 0
            ? "/PickleballWeb/Tournament/" + tournamentId + (isPairLifecycleNotificationType(notificationType) ? "/Register" : "/Standings")
            : "/PickleballWeb/Notifications";
    }

    function renderUserNotificationCard(item) {
        var actor = getUserNotificationActor(item);
        var actorName = normalizeDisplayText(actor && actor.fullName) || "Thành viên Hanaka";
        var avatarUrl = normalizeMediaUrl(actor && actor.avatarUrl);
        var tournamentTitle = normalizeDisplayText(item && item.tournamentTitle) || "Giải đấu";
        var createdAtText = formatDateTime(item && item.createdAt) || "Vừa xong";
        var responseNote = normalizeDisplayText(item && item.responseNote);
        var notificationType = getNotificationType(item);
        var isPairResponse = isPairLifecycleNotificationType(notificationType);
        if (!isPairResponse) {
            actorName = "H\u1ec7 th\u1ed1ng gi\u1ea3i \u0111\u1ea5u";
        }
        var isUnread = item && (item.isRead === false || item.IsRead === false);
        var notificationId = Number(item && (item.notificationId || item.id));
        var notificationTournamentId = Number(readNotificationValue(item, ["tournamentId", "TournamentId"]));
        var eyebrowText = notificationType === "PAIR_ACCEPTED"
            ? "Chấp nhận lời mời"
            : notificationType === "PAIR_REJECTED"
                ? "Từ chối lời mời"
                : "Thông báo";
        var detailHref = buildNotificationDetailHref(item);
        if (!isPairResponse) {
            eyebrowText = getTournamentNotificationEyebrow(notificationType);
        }
        var detailText = Number(item && item.tournamentId) > 0 ? "Xem đăng ký" : "Mở thông báo";
        if (!isPairResponse && notificationType === "MATCH_WIN") {
            detailText = "Xem tr\u1eadn";
        } else if (!isPairResponse && notificationTournamentId > 0) {
            detailText = "Xem gi\u1ea3i \u0111\u1ea5u";
        }

        var detailRows = isPairResponse ? "" : buildTournamentNotificationDetailRows(item, "card");
        var stateBadge = isUnread
            ? '<span class="native-notification-card__badge native-notification-card__badge--unread">Chưa đọc</span>'
            : '<span class="native-notification-card__badge native-notification-card__badge--read">Đã đọc</span>';

        return [
            '<article class="native-notification-card native-notification-card--system' + (isUnread ? " native-notification-card--unread" : " native-notification-card--read") + '">',
            '<div class="native-notification-card__pair-head">',
            avatarUrl
                ? '<span class="native-notification-card__avatar"><img src="' + escapeHtml(avatarUrl) + '" alt="' + escapeHtml(actorName) + '" loading="lazy"></span>'
                : '<span class="native-notification-card__avatar"><ion-icon name="person-outline"></ion-icon></span>',
            '<div>',
            '<div class="native-notification-card__topline">',
            '<span class="native-notification-card__badge">' + escapeHtml(eyebrowText) + "</span>",
            stateBadge,
            "</div>",
            '<h2 class="native-notification-card__title">' + escapeHtml(normalizeDisplayText(item && item.title) || "Thông báo") + "</h2>",
            '<p class="native-notification-card__line">' + escapeHtml(normalizeDisplayText(item && item.message) || "Thông báo sẽ được cập nhật tại đây.") + "</p>",
            "</div>",
            "</div>",
            '<p class="native-notification-card__line"><strong>Người phản hồi:</strong> ' + escapeHtml(actorName) + "</p>",
            '<p class="native-notification-card__line"><strong>Giải đấu:</strong> ' + escapeHtml(tournamentTitle) + "</p>",
            responseNote
                ? '<p class="native-notification-card__line"><strong>Ghi chú:</strong> ' + escapeHtml(responseNote) + "</p>"
                : "",
            detailRows,
            '<div class="native-notification-card__actions">',
            '<a class="is-primary" href="' + escapeHtml(detailHref) + '" data-notification-link="' + escapeHtml(notificationId || "") + '" data-notification-unread="' + escapeHtml(isUnread ? "true" : "false") + '">' + escapeHtml(detailText) + "</a>",
            isUnread && notificationId > 0
                ? '<button type="button" data-notification-read="' + escapeHtml(notificationId) + '">Đánh dấu đã đọc</button>'
                : "",
            "</div>",
            '<p class="native-notification-card__time">' + escapeHtml(createdAtText) + "</p>",
            "</article>"
        ].join("");
    }

    function renderUserNotificationCard(item) {
        var actor = getUserNotificationActor(item);
        var actorName = normalizeDisplayText(actor && actor.fullName) || "Thành viên Hanaka";
        var avatarUrl = normalizeMediaUrl(actor && actor.avatarUrl);
        var tournamentTitle = normalizeDisplayText(item && item.tournamentTitle) || "Giải đấu";
        var createdAtText = formatDateTime(item && item.createdAt) || "Vừa xong";
        var responseNote = normalizeDisplayText(item && item.responseNote);
        var notificationType = getNotificationType(item);
        var isPairResponse = isPairLifecycleNotificationType(notificationType);
        if (!isPairResponse) {
            actorName = "Hệ thống giải đấu";
        }

        var isUnread = item && (item.isRead === false || item.IsRead === false);
        var notificationId = Number(item && (item.notificationId || item.id));
        var notificationTournamentId = Number(readNotificationValue(item, ["tournamentId", "TournamentId"]));
        var eyebrowText = isPairResponse
            ? (notificationType === "PAIR_ACCEPTED" ? "Chấp nhận lời mời" : "Từ chối lời mời")
            : getTournamentNotificationEyebrow(notificationType);
        var detailHref = buildNotificationDetailHref(item);
        var detailText = notificationType === "MATCH_WIN"
            ? "Xem trận"
            : notificationTournamentId > 0
                ? (isPairResponse ? "Xem đăng ký" : "Xem giải đấu")
                : "Mở thông báo";
        var detailRows = isPairResponse
            ? [
                '<p class="native-notification-card__line"><strong>Người phản hồi:</strong> ' + escapeHtml(actorName) + "</p>",
                '<p class="native-notification-card__line"><strong>Giải đấu:</strong> ' + escapeHtml(tournamentTitle) + "</p>",
                responseNote
                    ? '<p class="native-notification-card__line"><strong>Ghi chú:</strong> ' + escapeHtml(responseNote) + "</p>"
                    : ""
            ].join("")
            : buildTournamentNotificationDetailRows(item, "card");
        var stateBadge = isUnread
            ? '<span class="native-notification-card__badge native-notification-card__badge--unread">Chưa đọc</span>'
            : '<span class="native-notification-card__badge native-notification-card__badge--read">Đã đọc</span>';
        var fallbackIcon = isPairResponse ? "person-outline" : "trophy-outline";

        return [
            '<article class="native-notification-card native-notification-card--system' + (isUnread ? " native-notification-card--unread" : " native-notification-card--read") + '">',
            '<div class="native-notification-card__pair-head">',
            avatarUrl
                ? '<span class="native-notification-card__avatar"><img src="' + escapeHtml(avatarUrl) + '" alt="' + escapeHtml(actorName) + '" loading="lazy"></span>'
                : '<span class="native-notification-card__avatar"><ion-icon name="' + escapeHtml(fallbackIcon) + '"></ion-icon></span>',
            '<div>',
            '<div class="native-notification-card__topline">',
            '<span class="native-notification-card__badge">' + escapeHtml(eyebrowText) + "</span>",
            stateBadge,
            "</div>",
            '<h2 class="native-notification-card__title">' + escapeHtml(normalizeDisplayText(item && item.title) || "Thông báo") + "</h2>",
            '<p class="native-notification-card__line">' + escapeHtml(normalizeDisplayText(item && item.message) || "Thông báo sẽ được cập nhật tại đây.") + "</p>",
            "</div>",
            "</div>",
            detailRows,
            '<div class="native-notification-card__actions">',
            '<a class="is-primary" href="' + escapeHtml(detailHref) + '" data-notification-link="' + escapeHtml(notificationId || "") + '" data-notification-unread="' + escapeHtml(isUnread ? "true" : "false") + '">' + escapeHtml(detailText) + "</a>",
            isUnread && notificationId > 0
                ? '<button type="button" data-notification-read="' + escapeHtml(notificationId) + '">Đánh dấu đã đọc</button>'
                : "",
            "</div>",
            '<p class="native-notification-card__time">' + escapeHtml(createdAtText) + "</p>",
            "</article>"
        ].join("");
    }

    function renderPairRequestNotificationCard(item) {
        var requestId = Number(item && item.pairRequestId);
        var tournamentId = Number(item && item.tournamentId);
        var requestedBy = item && item.requestedBy ? item.requestedBy : {};
        var requesterName = normalizeDisplayText(requestedBy.fullName) || "Thành viên Hanaka";
        var tournamentTitle = normalizeDisplayText(item && item.tournamentTitle) || "Giải đấu";
        var expiresAt = formatDateTime(item && item.expiresAt);
        var avatarUrl = normalizeMediaUrl(requestedBy.avatarUrl);

        return [
            '<article class="native-notification-card native-notification-card--pair" data-pair-request-card="' + escapeHtml(requestId || "") + '">',
            '<div class="native-notification-card__pair-head">',
            avatarUrl
                ? '<span class="native-notification-card__avatar"><img src="' + escapeHtml(avatarUrl) + '" alt="' + escapeHtml(requesterName) + '" loading="lazy"></span>'
                : '<span class="native-notification-card__avatar"><ion-icon name="person-outline"></ion-icon></span>',
            '<div>',
            '<h2 class="native-notification-card__title">' + escapeHtml(normalizeDisplayText(item && item.title) || "Lời mời ghép đôi") + "</h2>",
            '<p class="native-notification-card__line">' + escapeHtml(normalizeDisplayText(item && item.message) || (requesterName + " mời bạn ghép cặp.")) + "</p>",
            "</div>",
            "</div>",
            '<p class="native-notification-card__line"><strong>Giải đấu:</strong> ' + escapeHtml(tournamentTitle) + "</p>",
            '<p class="native-notification-card__line"><strong>Hết hạn:</strong> ' + escapeHtml(expiresAt) + "</p>",
            '<div class="native-notification-card__actions">',
            '<button type="button" data-pair-request-accept="' + escapeHtml(requestId || "") + '">Chấp nhận</button>',
            '<button type="button" data-pair-request-reject="' + escapeHtml(requestId || "") + '">Từ chối</button>',
            tournamentId > 0
                ? '<a href="/PickleballWeb/Tournament/' + escapeHtml(tournamentId) + '/Register">Xem phiếu</a>'
                : "",
            "</div>",
            "</article>"
        ].join("");
    }

    function renderNotificationLoginPrompt() {
        var loginHref = "/PickleballWeb/Login?returnUrl=" + encodeURIComponent("/PickleballWeb/Notifications");

        return [
            '<article class="native-auth-prompt">',
            '<span class="native-auth-prompt__icon"><ion-icon name="notifications-outline"></ion-icon></span>',
            "<strong>Đăng nhập để xem thông báo</strong>",
            "<p>Trang này sẽ hiển thị các trận đấu sắp tới của tài khoản giống trong ứng dụng.</p>",
            '<a class="native-auth-prompt__button" href="' + escapeHtml(loginHref) + '">Đăng nhập</a>',
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
            '<span class="native-settings-row__label">' + escapeHtml(options && options.label || "Tùy chọn") + "</span>",
            "</span>",
            '<ion-icon class="native-settings-row__chevron" name="chevron-forward"></ion-icon>',
            "</a>"
        ].join("");
    }

    function renderSettingsPage() {
        return [
            '<section class="native-settings-section">',
            '<h2 class="native-settings-section__title">Tài khoản</h2>',
            renderSettingsRow({
                label: "Quản lý tài khoản",
                icon: "person-circle-outline",
                href: "/PickleballWeb/Account"
            }),
            renderSettingsRow({
                label: "Đổi mật khẩu",
                icon: "key-outline",
                href: "/PickleballWeb/ChangePassword"
            }),
            renderSettingsRow({
                label: "Xóa tài khoản",
                icon: "trash-outline",
                href: "/PickleballWeb/Account",
                danger: true
            }),
            '<p class="native-settings-note">Luồng xóa tài khoản trên web được đặt bên trong màn Tài khoản để giống cách vận hành hiện tại của hệ thống.</p>',
            "</section>",
            '<div class="native-settings-divider"></div>',
            '<section class="native-settings-section">',
            '<h2 class="native-settings-section__title">An toàn cộng đồng</h2>',
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
            '<p class="native-settings-note">Trò chuyện CLB có bộ lọc nội dung, cơ chế báo cáo vi phạm, chặn người dùng và cam kết xử lý kiểm duyệt trong vòng 24 giờ.</p>',
            "</section>",
            '<div class="native-settings-divider"></div>',
            '<section class="native-settings-section">',
            '<h2 class="native-settings-section__title">Thông tin ứng dụng</h2>',
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
            viewMode: "all",
            pairItems: [],
            inboxItems: [],
            matchItems: [],
            inboxPage: 1,
            inboxPageSize: 20,
            inboxTotal: 0,
            inboxHasMore: false,
            unreadNonPairTotal: 0,
            markingAll: false,
            readingMap: Object.create(null),
            refreshQueued: false,
            refreshQueuedReset: false
        };
        var removeRealtimeListener = null;
        var handleNotificationCenterChange = null;
        var inboxInfiniteObserver = null;

        renderEmptyState(refs, "Hiện chưa có thông báo mới.");

        function getRenderedItemsLength() {
            if (state.viewMode === "matches") {
                return state.matchItems.length;
            }

            return state.pairItems.length + state.inboxItems.length + state.matchItems.length;
        }

        function buildNotificationSection(title, description, cardsHtml, footerHtml) {
            if (!cardsHtml) {
                return "";
            }

            return [
                '<section class="native-notification-section">',
                '<div class="native-notification-section__head">',
                '<div>',
                '<h2 class="native-notification-section__title">' + escapeHtml(title) + "</h2>",
                description
                    ? '<p class="native-notification-section__meta">' + escapeHtml(description) + "</p>"
                    : "",
                "</div>",
                "</div>",
                '<div class="native-notification-section__body">',
                cardsHtml,
                "</div>",
                footerHtml
                    ? '<div class="native-notification-section__footer">' + footerHtml + "</div>"
                    : "",
                "</section>"
            ].join("");
        }

        function buildHeaderSummary() {
            if (state.authRequired) {
                return "";
            }

            var meta = [];
            meta.push('<button class="native-notification-toolbar__chip native-notification-toolbar__chip--filter' + (state.viewMode === "all" ? " is-active" : "") + '" type="button" data-notification-view="all">Tất cả</button>');
            meta.push('<button class="native-notification-toolbar__chip native-notification-toolbar__chip--filter native-notification-toolbar__chip--match' + (state.viewMode === "matches" ? " is-active" : "") + '" type="button" data-notification-view="matches"><ion-icon name="calendar-outline"></ion-icon><span>Lịch đấu: ' + escapeHtml(state.matchItems.length) + "</span></button>");
            meta.push('<span class="native-notification-toolbar__chip' + (state.unreadNonPairTotal > 0 ? " is-unread" : "") + '">Chưa đọc: ' + escapeHtml(state.unreadNonPairTotal) + "</span>");

            if (state.inboxTotal > 0) {
                meta.push('<span class="native-notification-toolbar__meta">Thông báo hệ thống: ' + escapeHtml(state.inboxItems.length) + "/" + escapeHtml(state.inboxTotal) + "</span>");
            }

            if (state.pairItems.length > 0) {
                meta.push('<span class="native-notification-toolbar__meta">Cần phản hồi: ' + escapeHtml(state.pairItems.length) + "</span>");
            }

            if (state.matchItems.length > 0) {
                meta.push('<span class="native-notification-toolbar__meta">Sắp thi đấu: ' + escapeHtml(state.matchItems.length) + "</span>");
            }

            return meta.length > 0
                ? '<div class="native-notification-toolbar">' + meta.join("") + "</div>"
                : "";
        }

        function getNotificationItemId(item) {
            var notificationId = Number(item && (item.notificationId || item.id));
            return Number.isFinite(notificationId) && notificationId > 0
                ? notificationId
                : 0;
        }

        function mergeInboxItems(existingItems, nextItems, reset) {
            var merged = [];
            var seen = Object.create(null);
            var source = reset
                ? nextItems
                : (Array.isArray(existingItems) ? existingItems : []).concat(Array.isArray(nextItems) ? nextItems : []);

            source.forEach(function (item, index) {
                var notificationId = getNotificationItemId(item);
                var key = notificationId > 0 ? ("id:" + notificationId) : ("idx:" + index);
                if (seen[key]) {
                    return;
                }

                seen[key] = true;
                merged.push(item);
            });

            return merged;
        }

        function buildNotificationSectionsHtml() {
            var sections = [];
            var pairHtml = state.pairItems.map(renderNotificationCard).join("");
            var inboxHtml = state.inboxItems.map(renderNotificationCard).join("");
            var matchHtml = state.matchItems.map(renderNotificationCard).join("");

            if (matchHtml) {
                sections.push(buildNotificationSection(
                    "Lịch thi đấu sắp tới",
                    state.matchItems.length + " trận đã được lên lịch",
                    matchHtml,
                    ""
                ));
            }

            if (state.viewMode === "matches") {
                return sections.join("");
            }

            if (pairHtml) {
                sections.push(buildNotificationSection(
                    "Lời mời chờ phản hồi",
                    state.pairItems.length + " lời mời cần bạn xử lý",
                    pairHtml,
                    ""
                ));
            }

            if (inboxHtml) {
                sections.push(buildNotificationSection(
                    "Thông báo của bạn",
                    "Đã tải " + state.inboxItems.length + "/" + state.inboxTotal + " thông báo",
                    inboxHtml,
                    state.inboxHasMore
                        ? '<button class="native-notification-loadmore" type="button" data-notification-load-more' + (state.loading ? " disabled" : "") + ">" + (state.loading ? "Đang tải..." : "Xem thêm thông báo") + "</button>"
                        : ""
                ));
            }

            if (false && matchHtml) {
                sections.push(buildNotificationSection(
                    "Lịch thi đấu sắp tới",
                    state.matchItems.length + " trận đã được lên lịch",
                    matchHtml,
                    ""
                ));
            }

            return sections.join("");
        }

        function render() {
            refs.list.className = "native-page-list native-page-list--notifications";

            setHeaderAction(root, !state.authRequired && state.unreadNonPairTotal > 0
                ? {
                    className: "native-page-header__action--notification-readall",
                    html: [
                        '<ion-icon name="checkmark-done-outline" aria-hidden="true"></ion-icon>',
                        "<span>" + (state.markingAll ? "Đang xử lý" : "Đọc hết") + "</span>"
                    ].join(""),
                    ariaLabel: state.markingAll
                        ? "Đang đánh dấu các thông báo là đã đọc"
                        : "Đánh dấu tất cả thông báo là đã đọc",
                    disabled: state.markingAll,
                    onClick: function () {
                        markAllNotificationsRead();
                    }
                }
                : null);
            setHeaderExtra(root, buildHeaderSummary());
            renderEmptyState(refs, state.viewMode === "matches"
                ? "Chưa có lịch thi đấu sắp tới."
                : "Hiện chưa có thông báo mới.");

            if (state.authRequired) {
                refs.list.innerHTML = renderNotificationLoginPrompt();
            } else {
                refs.list.innerHTML = buildNotificationSectionsHtml();
            }

            toggleCommonState(refs, {
                loading: state.loading,
                itemsLength: state.authRequired ? 1 : getRenderedItemsLength(),
                error: state.error,
                hasMore: !state.authRequired && state.viewMode === "all" && state.inboxHasMore
            });
        }

        function applyInboxPayload(payload, reset) {
            var nextItems = payload && Array.isArray(payload.items)
                ? payload.items.filter(function (item) {
                    return getNotificationType(item) !== "PAIR_REQUEST";
                })
                : [];

            state.unreadNonPairTotal = Math.max(0, Number(payload && payload.unreadNonPairTotal) || 0);
            state.inboxTotal = Math.max(0, Number(payload && payload.total) || 0);
            state.inboxHasMore = !!(payload && payload.hasMore);
            state.inboxPage = Math.max(1, Number(payload && payload.page) || 1) + 1;

            if (reset) {
                state.inboxItems = mergeInboxItems([], nextItems, true);
                return;
            }

            state.inboxItems = mergeInboxItems(state.inboxItems, nextItems, false);
        }

        function markInboxNotificationReadLocally(notificationId) {
            var targetId = Number(notificationId);
            var didMark = false;

            state.inboxItems = state.inboxItems.map(function (item) {
                var itemId = Number(item && (item.notificationId || item.id));
                if (itemId !== targetId) {
                    return item;
                }

                if (!(item && (item.isRead === false || item.IsRead === false))) {
                    return item;
                }

                didMark = true;
                return Object.assign({}, item, {
                    isRead: true,
                    IsRead: true,
                    readAt: item.readAt || new Date().toISOString(),
                    ReadAt: item.ReadAt || item.readAt || new Date().toISOString()
                });
            });

            if (didMark && state.unreadNonPairTotal > 0) {
                state.unreadNonPairTotal -= 1;
            }

            return didMark;
        }

        async function markNotificationRead(notificationId) {
            var targetId = Number(notificationId);
            var readingKey = String(targetId);

            if (!Number.isFinite(targetId) || targetId <= 0 || state.readingMap[readingKey]) {
                return false;
            }

            state.readingMap[readingKey] = true;
            render();

            try {
                await requestJson("/api/notifications/inbox/" + targetId + "/read", {
                    method: "POST"
                });

                markInboxNotificationReadLocally(targetId);
                await syncNotificationCenter({ allowPopup: false });
                return true;
            } finally {
                delete state.readingMap[readingKey];
                render();
            }
        }

        async function markAllNotificationsRead() {
            if (state.markingAll || state.unreadNonPairTotal <= 0) {
                return;
            }

            state.markingAll = true;
            render();

            try {
                await requestJson("/api/notifications/inbox/read-all", {
                    method: "POST"
                });

                state.inboxItems = state.inboxItems.map(function (item) {
                    return Object.assign({}, item, {
                        isRead: true,
                        IsRead: true,
                        readAt: item && item.readAt ? item.readAt : new Date().toISOString(),
                        ReadAt: item && item.ReadAt ? item.ReadAt : (item && item.readAt ? item.readAt : new Date().toISOString())
                    });
                });
                state.unreadNonPairTotal = 0;

                await syncNotificationCenter({ allowPopup: false });
            } catch (error) {
                window.alert(error && error.message ? error.message : "Không thể cập nhật trạng thái thông báo.");
            } finally {
                state.markingAll = false;
                render();
            }
        }

        async function load(reset) {
            if (state.loading) {
                state.refreshQueued = true;
                state.refreshQueuedReset = state.refreshQueuedReset || !!reset;
                return;
            }

            if (!reset && !state.inboxHasMore) {
                return;
            }

            state.loading = true;
            if (reset) {
                state.error = "";
                state.authRequired = false;
            }
            render();

            try {
                var session = await fetchJson("/api/web-auth/me");
                if (!(session && session.isAuthenticated)) {
                    state.authRequired = true;
                    state.pairItems = [];
                    state.inboxItems = [];
                    state.matchItems = [];
                    state.inboxPage = 1;
                    state.inboxTotal = 0;
                    state.inboxHasMore = false;
                    state.unreadNonPairTotal = 0;
                    return;
                }

                var requestedPage = reset ? 1 : state.inboxPage;
                var requests = [
                    fetchJson("/api/notifications/inbox?page=" + requestedPage + "&pageSize=" + state.inboxPageSize)
                ];

                if (reset) {
                    requests.push(fetchJson("/api/notifications/pair-requests"));
                    requests.push(fetchJson("/api/notifications/upcoming-matches"));
                }

                var results = await Promise.allSettled(requests);
                if (results[0].status !== "fulfilled") {
                    throw results[0].reason || new Error("inbox");
                }

                if (reset) {
                    state.pairItems = results[1].status === "fulfilled" && Array.isArray(results[1].value && results[1].value.items)
                        ? results[1].value.items
                        : [];
                    state.matchItems = results[2].status === "fulfilled" && Array.isArray(results[2].value && results[2].value.items)
                        ? results[2].value.items
                        : [];
                }

                applyInboxPayload(results[0].value, reset);
            } catch (_error) {
                if (reset) {
                    state.pairItems = [];
                    state.inboxItems = [];
                    state.matchItems = [];
                    state.inboxPage = 1;
                    state.inboxTotal = 0;
                    state.inboxHasMore = false;
                    state.unreadNonPairTotal = 0;
                }

                state.error = "Không tải được thông báo.";
            } finally {
                state.loading = false;
                var shouldReplay = state.refreshQueued;
                var replayReset = state.refreshQueuedReset;
                state.refreshQueued = false;
                state.refreshQueuedReset = false;
                render();

                if (shouldReplay) {
                    load(replayReset);
                }
            }
        }

        if (refs.retry) {
            refs.retry.onclick = function () { load(true); };
        }

        root.addEventListener("click", function (event) {
            var viewButton = event.target.closest("[data-notification-view]");
            if (!viewButton) {
                return;
            }

            var nextView = trimToEmpty(viewButton.getAttribute("data-notification-view")) === "matches"
                ? "matches"
                : "all";

            if (state.viewMode === nextView) {
                return;
            }

            state.viewMode = nextView;
            render();
        });

        if (refs.list) {
            refs.list.addEventListener("click", async function (event) {
                var loadMoreButton = event.target.closest("[data-notification-load-more]");
                var readButton = event.target.closest("[data-notification-read]");
                var detailLink = event.target.closest("[data-notification-link]");
                var acceptButton = event.target.closest("[data-pair-request-accept]");
                var rejectButton = event.target.closest("[data-pair-request-reject]");
                var button = acceptButton || rejectButton;

                if (loadMoreButton) {
                    await load(false);
                    return;
                }

                if (readButton) {
                    var notificationId = Number(readButton.getAttribute("data-notification-read"));
                    if (!Number.isFinite(notificationId) || notificationId <= 0) {
                        return;
                    }

                    try {
                        await markNotificationRead(notificationId);
                    } catch (error) {
                        window.alert(error && error.message ? error.message : "Không thể cập nhật trạng thái thông báo.");
                    }
                    return;
                }

                if (detailLink) {
                    var isUnreadLink = trimToEmpty(detailLink.getAttribute("data-notification-unread")).toLowerCase() === "true";
                    var targetNotificationId = Number(detailLink.getAttribute("data-notification-link"));
                    var href = trimToEmpty(detailLink.getAttribute("href")) || "#";

                    if (!isUnreadLink || !Number.isFinite(targetNotificationId) || targetNotificationId <= 0 || href === "#") {
                        return;
                    }

                    event.preventDefault();

                    try {
                        await markNotificationRead(targetNotificationId);
                    } catch (_error) {
                    }

                    window.location.href = href;
                    return;
                }

                if (!button) {
                    return;
                }

                var requestId = Number(button.getAttribute(acceptButton ? "data-pair-request-accept" : "data-pair-request-reject"));
                if (!Number.isFinite(requestId) || requestId <= 0) {
                    return;
                }

                var action = acceptButton ? "accept" : "reject";
                var card = button.closest("[data-pair-request-card]");
                var controls = card
                    ? Array.prototype.slice.call(card.querySelectorAll("[data-pair-request-accept], [data-pair-request-reject]"))
                    : [button];

                try {
                    await performPairRequestAction(requestId, action, {
                        control: button,
                        controls: controls
                    });

                    await load(true);
                } catch (error) {
                    window.alert(error && error.message ? error.message : "Không thể xử lý lời mời.");
                }
            });
        }

        inboxInfiniteObserver = setupInfiniteObserver(refs.sentinel, function () {
            if (!state.loading && !state.authRequired && state.viewMode === "all" && state.inboxHasMore) {
                load(false);
            }
        });

        handleNotificationCenterChange = function () {
            load(true);
        };

        window.addEventListener(NOTIFICATION_CENTER_EVENT, handleNotificationCenterChange);

        connectRealtime();
        removeRealtimeListener = addRealtimeListener(function (event) {
            if (trimToEmpty(event && event.type) === "tournament.notification") {
                load(true);
            }
        });

        window.addEventListener("pagehide", function () {
            if (removeRealtimeListener) {
                removeRealtimeListener();
                removeRealtimeListener = null;
            }

            if (handleNotificationCenterChange) {
                window.removeEventListener(NOTIFICATION_CENTER_EVENT, handleNotificationCenterChange);
                handleNotificationCenterChange = null;
            }

            if (inboxInfiniteObserver) {
                inboxInfiniteObserver.disconnect();
                inboxInfiniteObserver = null;
            }
        }, { once: true });

        load(true);
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

    function normalizeSearchText(value) {
        return String(value || "")
            .toLowerCase()
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "");
    }

    function formatRoomTime(value) {
        var date = parseDate(value);
        if (!date) {
            return "";
        }

        var now = new Date();
        var sameDay = date.getFullYear() === now.getFullYear() &&
            date.getMonth() === now.getMonth() &&
            date.getDate() === now.getDate();

        if (sameDay) {
            return pad2(date.getHours()) + ":" + pad2(date.getMinutes());
        }

        return pad2(date.getDate()) + "/" + pad2(date.getMonth() + 1);
    }

    function formatMessageTime(value) {
        var date = parseDate(value);
        if (!date) {
            return "";
        }

        return pad2(date.getHours()) + ":" + pad2(date.getMinutes());
    }

    function renderTextWithBreaks(value) {
        return escapeHtml(String(value || "")).replace(/\r?\n/g, "<br>");
    }

    function getSessionUserId(session) {
        var raw = session && session.user
            ? session.user.userId ?? session.user.id ?? session.user.UserId
            : "";

        return trimToEmpty(raw);
    }

    function renderAuthPrompt(options) {
        var returnUrl = trimToEmpty(options && options.returnUrl) || window.location.pathname;
        var icon = trimToEmpty(options && options.icon) || "log-in-outline";
        var loginHref = "/PickleballWeb/Login?returnUrl=" + encodeURIComponent(returnUrl);

        return [
            '<article class="native-auth-prompt native-auth-prompt--panel">',
            '<span class="native-auth-prompt__icon"><ion-icon name="' + escapeHtml(icon) + '"></ion-icon></span>',
            "<strong>" + escapeHtml(trimToEmpty(options && options.title) || "Đăng nhập để tiếp tục") + "</strong>",
            "<p>" + escapeHtml(trimToEmpty(options && options.body) || "Vui lòng đăng nhập để xem nội dung này.") + "</p>",
            '<a class="native-auth-prompt__button" href="' + escapeHtml(loginHref) + '">Đăng nhập</a>',
            "</article>"
        ].join("");
    }

    function buildMatchVideoTitle(item) {
        var team1 = trimToEmpty(item && (item.team1Name || item.team1DisplayName || item.team1));
        var team2 = trimToEmpty(item && (item.team2Name || item.team2DisplayName || item.team2));
        var roundLabel = trimToEmpty(item && item.roundLabel);
        var groupName = trimToEmpty(item && item.groupName);
        var parts = [];

        if (team1 || team2) {
            parts.push((team1 || "Đội 1") + " gặp " + (team2 || "Đội 2"));
        }

        if (roundLabel) {
            parts.push(roundLabel);
        }

        if (groupName) {
            parts.push(formatVideoGroupLabel(groupName));
        }

        return parts.join(" • ");
    }

    function formatVideoGroupLabel(groupName) {
        var label = trimToEmpty(groupName).replace(/^bang\s+/i, "");

        if (!label) {
            return "";
        }

        return /^bảng\b/i.test(label) ? label : "Bảng " + label;
    }

    function getYoutubeId(url) {
        var href = trimToEmpty(url);

        if (!href) {
            return "";
        }

        try {
            var parsed = new URL(href, window.location.origin);

            if (parsed.hostname.indexOf("youtu.be") >= 0) {
                return parsed.pathname.replace(/^\/+/, "");
            }

            if (parsed.searchParams.get("v")) {
                return parsed.searchParams.get("v") || "";
            }

            if (parsed.pathname.indexOf("/shorts/") === 0 || parsed.pathname.indexOf("/embed/") === 0) {
                return parsed.pathname.split("/")[2] || "";
            }
        } catch (_error) {
            return "";
        }

        return "";
    }

    function buildVideoPlayable(url) {
        var href = trimToEmpty(url);

        if (!href) {
            return { type: "none", src: "" };
        }

        var youtubeId = getYoutubeId(href);
        if (youtubeId) {
            return {
                type: "youtube",
                src: "https://www.youtube.com/embed/" + youtubeId + "?playsinline=1&rel=0"
            };
        }

        if (/\.(mp4|webm|ogg|mov|m4v)(?:$|[?#])/i.test(href)) {
            return { type: "file", src: href };
        }

        return { type: "frame", src: href };
    }

    function renderVideoCard(item) {
        var matchId = item && item.matchId;
        var href = matchId ? "/PickleballWeb/Video/" + matchId : "#";
        var bannerUrl = normalizeMediaUrl(item && item.tournamentBannerUrl);
        var tournamentTitle = trimToEmpty(item && item.tournamentTitle) || "Hanaka Sport";
        var title = buildMatchVideoTitle(item) || tournamentTitle;
        var team1Player1 = trimToEmpty(item && item.team1Player1Name) || trimToEmpty(item && item.team1Name) || "Đội 1";
        var team1Player2 = trimToEmpty(item && item.team1Player2Name);
        var team2Player1 = trimToEmpty(item && item.team2Player1Name) || trimToEmpty(item && item.team2Name) || "Đội 2";
        var team2Player2 = trimToEmpty(item && item.team2Player2Name);
        var team1Class = "native-video-card__team " + (team1Player2 ? "has-two-players" : "has-one-player");
        var team2Class = "native-video-card__team " + (team2Player2 ? "has-two-players" : "has-one-player");

        function renderPlayer(name, avatar) {
            var avatarUrl = normalizeMediaUrl(avatar);

            return [
                '<div class="native-video-card__player">',
                avatarUrl
                    ? '<span class="native-video-card__avatar"><img src="' + escapeHtml(avatarUrl) + '" alt="' + escapeHtml(name || "Vận động viên") + '" loading="lazy"></span>'
                    : '<span class="native-video-card__avatar native-video-card__avatar--fallback"><ion-icon name="person-outline"></ion-icon></span>',
                '<span class="native-video-card__player-name" title="' + escapeHtml(name || "Vận động viên") + '">' + escapeHtml(name || "Vận động viên") + "</span>",
                "</div>"
            ].join("");
        }

        return [
            '<a class="native-video-card" href="' + escapeHtml(href) + '">',
            bannerUrl
                ? '<img class="native-video-card__banner" src="' + escapeHtml(bannerUrl) + '" alt="' + escapeHtml(tournamentTitle) + '" loading="lazy">'
                : '<div class="native-video-card__banner native-video-card__banner--fallback"><ion-icon name="image-outline"></ion-icon></div>',
            '<div class="native-video-card__body">',
            '<div class="native-video-card__meta">',
            '<span>' + escapeHtml(formatDateTime(item && item.startAt) || "Chưa có lịch") + "</span>",
            trimToEmpty(item && item.roundLabel) ? '<span>• ' + escapeHtml(item.roundLabel) + "</span>" : "",
            "</div>",
            '<h2 class="native-video-card__title">' + escapeHtml(title) + "</h2>",
            '<p class="native-video-card__tournament">' + escapeHtml(tournamentTitle) + "</p>",
            (trimToEmpty(item && item.groupName) || trimToEmpty(item && item.courtText))
                ? '<p class="native-video-card__submeta">' + escapeHtml([trimToEmpty(item && item.groupName) ? formatVideoGroupLabel(item.groupName) : "", trimToEmpty(item && item.courtText)].filter(Boolean).join(" • ")) + "</p>"
                : "",
            '<div class="native-video-card__teams">',
            '<div class="' + team1Class + '">',
            renderPlayer(team1Player1, item && item.team1Player1Avatar),
            team1Player2 ? renderPlayer(team1Player2, item && item.team1Player2Avatar) : "",
            '<strong class="native-video-card__score">' + escapeHtml(item && item.scoreTeam1 != null ? item.scoreTeam1 : 0) + "</strong>",
            "</div>",
            '<div class="' + team2Class + '">',
            renderPlayer(team2Player1, item && item.team2Player1Avatar),
            team2Player2 ? renderPlayer(team2Player2, item && item.team2Player2Avatar) : "",
            '<strong class="native-video-card__score">' + escapeHtml(item && item.scoreTeam2 != null ? item.scoreTeam2 : 0) + "</strong>",
            "</div>",
            "</div>",
            '<div class="native-video-card__foot ' + (trimToEmpty(item && item.videoUrl) ? "is-live" : "is-muted") + '">',
            '<ion-icon name="play-circle-outline"></ion-icon>',
            '<span>' + (trimToEmpty(item && item.videoUrl) ? "Xem video" : "Chưa có video") + "</span>",
            "</div>",
            "</div>",
            "</a>"
        ].join("");
    }

    function initVideosPage(root) {
        var refs = getCommonRefs(root);
        var state = {
            tab: "all",
            query: "",
            page: 1,
            pageSize: 10,
            hasMore: true,
            items: [],
            loading: false,
            error: ""
        };
        var removePublicRealtimeListener = null;

        setHeaderTitle(root, "Videos");
        setHeaderAction(root, null);
        setHeaderExtra(root, [
            '<div class="native-video-toolbar">',
            '<label class="native-inline-search__box native-inline-search__box--video">',
            '<input type="search" placeholder="Tìm video, VĐV, bảng đấu..." autocomplete="off" data-video-query-input>',
            '<ion-icon name="search"></ion-icon>',
            "</label>",
            '<div class="native-tabs native-tabs--video">',
            '<button class="native-tabs__item is-active" type="button" data-video-tab="all">Tất cả</button>',
            '<button class="native-tabs__item" type="button" data-video-tab="suggested">De xuat</button>',
            '<button class="native-tabs__item" type="button" data-video-tab="live">Hom nay</button>',
            "</div>",
            "</div>"
        ].join(""));
        renderEmptyState(refs, "Không có video trận đấu phù hợp.");

        function filteredItems() {
            var query = normalizeSearchText(state.query);

            if (!query) {
                return state.items;
            }

            return state.items.filter(function (item) {
                var haystack = normalizeSearchText([
                    item && item.tournamentTitle,
                    buildMatchVideoTitle(item),
                    item && item.roundLabel,
                    item && item.groupName,
                    item && item.team1Name,
                    item && item.team1Player1Name,
                    item && item.team1Player2Name,
                    item && item.team2Name,
                    item && item.team2Player1Name,
                    item && item.team2Player2Name
                ].filter(Boolean).join(" "));

                return haystack.indexOf(query) >= 0;
            });
        }

        function render() {
            var items = filteredItems();
            refs.list.className = "native-page-list native-page-list--videos";
            refs.list.innerHTML = items.map(renderVideoCard).join("");

            toggleCommonState(refs, {
                loading: state.loading,
                itemsLength: items.length,
                error: state.error,
                hasMore: state.hasMore
            });
        }

        function applyRealtimeScoreUpdate(payload) {
            var matchId = Number(payload && (payload.matchId || payload.MatchId));
            if (!Number.isFinite(matchId) || matchId <= 0) {
                return false;
            }

            var changed = false;
            state.items = state.items.map(function (item) {
                if (!item || Number(item.matchId) !== matchId) {
                    return item;
                }

                changed = true;
                return Object.assign({}, item, {
                    scoreTeam1: Number(payload && (payload.scoreTeam1 ?? payload.ScoreTeam1) || 0),
                    scoreTeam2: Number(payload && (payload.scoreTeam2 ?? payload.ScoreTeam2) || 0),
                    isCompleted: !!(payload && (payload.isCompleted ?? payload.IsCompleted)),
                    winnerRegistrationId: payload ? (payload.winnerRegistrationId ?? payload.WinnerRegistrationId ?? null) : null,
                    winnerSide: trimToEmpty(payload && (payload.winnerSide || payload.WinnerSide || payload.winnerTeam || payload.WinnerTeam)) || null,
                    updatedAt: payload ? (payload.updatedAt || payload.UpdatedAt || item.updatedAt) : item.updatedAt
                });
            });

            if (changed) {
                render();
            }

            return changed;
        }

        async function load(reset) {
            if (state.loading) {
                return;
            }

            if (!reset && !state.hasMore) {
                return;
            }

            state.loading = true;
            if (reset) {
                state.error = "";
            }
            render();

            try {
                var nextPage = reset ? 1 : state.page + 1;
                var payload = await fetchJson("/api/videos/videos?tab=" + encodeURIComponent(state.tab) + "&page=" + nextPage + "&pageSize=" + state.pageSize);
                var nextItems = Array.isArray(payload && payload.items) ? payload.items : [];

                state.items = reset ? nextItems : state.items.concat(nextItems);
                state.page = Number(payload && payload.page) || nextPage;
                state.hasMore = !!(payload && payload.hasMore);
            } catch (_error) {
                state.error = "Không tải được danh sách video.";
                if (reset) {
                    state.items = [];
                    state.page = 1;
                    state.hasMore = false;
                }
            } finally {
                state.loading = false;
                render();
            }
        }

        if (refs.retry) {
            refs.retry.onclick = function () { load(true); };
        }

        var queryInput = qs("[data-video-query-input]", root);
        if (queryInput) {
            queryInput.addEventListener("input", function () {
                state.query = trimToEmpty(queryInput.value);
                render();
            });
        }

        Array.from(root.querySelectorAll("[data-video-tab]")).forEach(function (button) {
            button.addEventListener("click", function () {
                var nextTab = trimToEmpty(button.getAttribute("data-video-tab")) || "all";
                if (nextTab === state.tab) {
                    return;
                }

                state.tab = nextTab;
                Array.from(root.querySelectorAll("[data-video-tab]")).forEach(function (node) {
                    node.classList.toggle("is-active", node === button);
                });
                load(true);
            });
        });

        setupInfiniteObserver(refs.sentinel, function () {
            if (!state.loading && state.hasMore) {
                load(false);
            }
        });

        subscribeVideosFeedRealtime();
        removePublicRealtimeListener = addPublicRealtimeListener(function (event) {
            if (trimToEmpty(event && event.type) !== "tournament.match.score.updated") {
                return;
            }

            var payload = event && event.payload ? event.payload : {};
            applyRealtimeScoreUpdate(payload);
        });

        window.addEventListener("pagehide", function () {
            if (removePublicRealtimeListener) {
                removePublicRealtimeListener();
                removePublicRealtimeListener = null;
            }
        }, { once: true });

        load(true);
    }

    function renderVideoPlayerFallback(title, bannerUrl, videoUrl) {
        return [
            '<div class="native-video-player__fallback"' + (bannerUrl ? ' style="background-image:url(\'' + escapeHtml(bannerUrl) + '\')"' : "") + '>',
            '<div class="native-video-player__fallback-overlay"></div>',
            '<div class="native-video-player__fallback-copy">',
            '<ion-icon name="play-circle-outline"></ion-icon>',
            '<strong>' + escapeHtml(title || "Không mở được video trong web") + "</strong>",
            "<p>Video này cần mở bằng trình duyệt ngoài hoặc dịch vụ video gốc.</p>",
            videoUrl
                ? '<a class="native-video-player__external" href="' + escapeHtml(buildSafeHref(videoUrl, "#")) + '" target="_blank" rel="noreferrer">Mo video ben ngoai</a>'
                : '<button class="native-video-player__external is-disabled" type="button" disabled>Không có video</button>',
            "</div>",
            "</div>"
        ].join("");
    }

    function renderVideoTeamSummary(team, score, isWinner) {
        var player1 = team && team.player1 ? team.player1 : {};
        var player2 = team && team.player2 ? team.player2 : null;

        function renderPlayer(player) {
            var avatarUrl = normalizeMediaUrl(player && player.avatar);
            var name = trimToEmpty(player && player.name) || "Thành viên";

            return [
                '<div class="native-video-meta__player">',
                avatarUrl
                    ? '<span class="native-video-meta__avatar"><img src="' + escapeHtml(avatarUrl) + '" alt="' + escapeHtml(name) + '" loading="lazy"></span>'
                    : '<span class="native-video-meta__avatar native-video-meta__avatar--fallback"><ion-icon name="person-outline"></ion-icon></span>',
                '<span class="native-video-meta__name">' + escapeHtml(name) + "</span>",
                "</div>"
            ].join("");
        }

        return [
            '<article class="native-video-meta__team' + (isWinner ? " is-winner" : "") + '">',
            '<div class="native-video-meta__team-head">',
            '<div>',
            '<h3>' + escapeHtml(trimToEmpty(team && team.displayName) || "Đội thi đấu") + "</h3>",
            '<span>' + escapeHtml(trimToEmpty(team && team.regCode) || "Đang cập nhật") + "</span>",
            "</div>",
            '<strong>' + escapeHtml(score != null ? score : 0) + "</strong>",
            "</div>",
            '<div class="native-video-meta__roster">',
            renderPlayer(player1),
            player2 ? renderPlayer(player2) : "",
            "</div>",
            "</article>"
        ].join("");
    }

    function initVideoPlayerPage(root) {
        var refs = getCommonRefs(root);
        var matchId = Number(root.getAttribute("data-native-page-id"));
        var refreshTimer = null;
        var removePublicRealtimeListener = null;

        renderEmptyState(refs, "Không tìm thấy video trận đấu.");

        async function load() {
            if (!Number.isFinite(matchId) || matchId <= 0) {
                refs.list.className = "native-page-list native-page-list--video-player";
                refs.list.innerHTML = renderVideoPlayerFallback("Không tìm thấy trận đấu", "", "");
                toggleCommonState(refs, {
                    loading: false,
                    itemsLength: 1,
                    error: "",
                    hasMore: false
                });
                return;
            }

            toggleCommonState(refs, {
                loading: true,
                itemsLength: 0,
                error: "",
                hasMore: false
            });

            try {
                var payload = await fetchJson("/api/tournaments/matches/" + matchId);
                var tournament = payload && payload.tournament ? payload.tournament : {};
                var match = payload && payload.match ? payload.match : {};
                var round = payload && payload.round ? payload.round : {};
                var group = payload && payload.group ? payload.group : {};
                var bannerUrl = normalizeMediaUrl(tournament && tournament.bannerUrl);
                var videoUrl = trimToEmpty(match && match.videoUrl);
                var title = buildMatchVideoTitle({
                    team1Name: match && match.team1 && match.team1.displayName,
                    team2Name: match && match.team2 && match.team2.displayName,
                    roundLabel: round && round.roundLabel,
                    groupName: group && group.groupName
                }) || trimToEmpty(tournament && tournament.title) || "Xem video";
                var playable = buildVideoPlayable(videoUrl);

                setHeaderTitle(root, trimToEmpty(tournament && tournament.title) || "Xem video");
                setHeaderAction(root, videoUrl ? {
                    className: "native-page-header__action--video-status",
                    html: '<ion-icon name="open-outline"></ion-icon><span>Mo video</span>',
                    ariaLabel: "Mở video trận đấu",
                    onClick: function () {
                        window.open(buildSafeHref(videoUrl, "#"), "_blank", "noopener");
                    }
                } : {
                    className: "native-page-header__action--video-status is-disabled",
                    html: '<ion-icon name="videocam-off-outline"></ion-icon><span>Không có video</span>',
                    ariaLabel: "Trận đấu hiện tại chưa có video",
                    disabled: true
                });
                setHeaderExtra(root, "");

                refs.list.className = "native-page-list native-page-list--video-player";
                refs.list.innerHTML = [
                    '<section class="native-video-player">',
                    '<div class="native-video-player__surface">',
                    playable.type === "youtube" || playable.type === "frame"
                        ? '<iframe class="native-video-player__frame" src="' + escapeHtml(playable.src) + '" allow="autoplay; encrypted-media; picture-in-picture" allowfullscreen loading="lazy" referrerpolicy="strict-origin-when-cross-origin"></iframe>'
                        : playable.type === "file"
                            ? '<video class="native-video-player__media" controls playsinline poster="' + escapeHtml(bannerUrl) + '" src="' + escapeHtml(playable.src) + '"></video>'
                            : renderVideoPlayerFallback(title, bannerUrl, videoUrl),
                    "</div>",
                    '<div class="native-video-meta">',
                    '<p class="native-video-meta__eyebrow">' + escapeHtml(trimToEmpty(tournament && tournament.title) || "Hanaka Sport") + "</p>",
                    '<h2 class="native-video-meta__title">' + escapeHtml(title) + "</h2>",
                    '<div class="native-video-meta__chips">',
                    trimToEmpty(round && round.roundLabel) ? '<span>' + escapeHtml(round.roundLabel) + "</span>" : "",
                    trimToEmpty(group && group.groupName) ? '<span>' + escapeHtml(formatVideoGroupLabel(group.groupName)) + "</span>" : "",
                    match && match.isCompleted ? '<span class="is-completed">Đã kết thúc</span>' : '<span class="is-open">Đang diễn ra</span>',
                    "</div>",
                    '<div class="native-video-meta__info">',
                    formatDateTime(match && match.startAt) ? '<div><small>Thời gian</small><strong>' + escapeHtml(formatDateTime(match.startAt)) + "</strong></div>" : "",
                    trimToEmpty(match && match.courtText) ? '<div><small>San</small><strong>' + escapeHtml(match.courtText) + "</strong></div>" : "",
                    trimToEmpty(match && match.addressText) ? '<div><small>Địa điểm</small><strong>' + escapeHtml(match.addressText) + "</strong></div>" : "",
                    videoUrl ? '<div><small>Video</small><strong><a href="' + escapeHtml(buildSafeHref(videoUrl, "#")) + '" target="_blank" rel="noreferrer">Mo lien ket goc</a></strong></div>' : "",
                    "</div>",
                    '<div class="native-video-meta__teams">',
                    renderVideoTeamSummary(match && match.team1, match && match.scoreTeam1, ["1", "TEAM1"].indexOf(trimToEmpty(match && match.winnerTeam).toUpperCase()) >= 0),
                    renderVideoTeamSummary(match && match.team2, match && match.scoreTeam2, ["2", "TEAM2"].indexOf(trimToEmpty(match && match.winnerTeam).toUpperCase()) >= 0),
                    "</div>",
                    "</div>",
                    "</section>"
                ].join("");

                toggleCommonState(refs, {
                    loading: false,
                    itemsLength: 1,
                    error: "",
                    hasMore: false
                });
            } catch (_error) {
                refs.list.className = "native-page-list native-page-list--video-player";
                refs.list.innerHTML = renderVideoPlayerFallback("Không tải được chi tiết video", "", "");
                toggleCommonState(refs, {
                    loading: false,
                    itemsLength: 1,
                    error: "",
                    hasMore: false
                });
            }
        }

        if (Number.isFinite(matchId) && matchId > 0) {
            subscribeMatchPublicRealtime(matchId);
            removePublicRealtimeListener = addPublicRealtimeListener(function (event) {
                if (trimToEmpty(event && event.type) !== "tournament.match.score.updated") {
                    return;
                }

                var payload = event && event.payload ? event.payload : {};
                if (Number(payload.matchId || payload.MatchId) !== matchId) {
                    return;
                }

                window.clearTimeout(refreshTimer);
                refreshTimer = window.setTimeout(function () {
                    load();
                }, 180);
            });
        }

        window.addEventListener("pagehide", function () {
            window.clearTimeout(refreshTimer);
            if (removePublicRealtimeListener) {
                removePublicRealtimeListener();
                removePublicRealtimeListener = null;
            }
        }, { once: true });

        load();
    }

    function renderChatRoomCard(item) {
        var coverUrl = normalizeMediaUrl(item && item.clubCoverUrl);
        var clubName = trimToEmpty(item && item.clubName) || "CLB Hanaka";
        var previewText = trimToEmpty(item && item.lastMessagePreview) || "Chưa có tin nhắn";

        return [
            '<a class="native-chat-room-card" href="/PickleballWeb/Chat/' + escapeHtml(item && item.clubId) + '">',
            coverUrl
                ? '<span class="native-chat-room-card__cover"><img src="' + escapeHtml(coverUrl) + '" alt="' + escapeHtml(clubName) + '" loading="lazy"></span>'
                : '<span class="native-chat-room-card__cover native-chat-room-card__cover--fallback"><ion-icon name="people-outline"></ion-icon></span>',
            '<span class="native-chat-room-card__body">',
            '<span class="native-chat-room-card__top">',
            '<strong>' + escapeHtml(clubName) + "</strong>",
            '<span>' + escapeHtml(formatRoomTime(item && item.lastMessageAt)) + "</span>",
            "</span>",
            trimToEmpty(item && item.areaText)
                ? '<span class="native-chat-room-card__area">' + escapeHtml(item.areaText) + "</span>"
                : "",
            '<span class="native-chat-room-card__preview">' + escapeHtml(trimToEmpty(item && item.lastSenderName) ? item.lastSenderName + ": " + previewText : previewText) + "</span>",
            "</span>",
            "</a>"
        ].join("");
    }

    function getAvatarInitials(name) {
        var parts = trimToEmpty(name)
            .split(/\s+/)
            .filter(Boolean)
            .slice(-2);

        if (!parts.length) {
            return "HS";
        }

        return parts.map(function (part) {
            return part.charAt(0);
        }).join("").toUpperCase();
    }

    function renderDirectAvatar(user, className, fallbackIcon) {
        var avatarUrl = normalizeMediaUrl(user && user.avatarUrl);
        var fullName = trimToEmpty(user && user.fullName) || "Thành viên";
        var baseClass = className || "native-chat-room-card__cover";

        if (avatarUrl) {
            return '<span class="' + escapeHtml(baseClass) + '"><img src="' + escapeHtml(avatarUrl) + '" alt="' + escapeHtml(fullName) + '" loading="lazy"></span>';
        }

        return '<span class="' + escapeHtml(baseClass + " " + baseClass + "--fallback") + '">' +
            (fallbackIcon ? '<ion-icon name="' + escapeHtml(fallbackIcon) + '"></ion-icon>' : '<span>' + escapeHtml(getAvatarInitials(fullName)) + "</span>") +
            "</span>";
    }

    function renderDirectRoomCard(item) {
        var roomId = item && (item.roomId || item.directChatRoomId);
        var otherUser = item && item.otherUser ? item.otherUser : {};
        var fullName = trimToEmpty(otherUser.fullName || item && item.title) || "Thành viên";
        var previewText = trimToEmpty(item && item.lastMessagePreview) || "Chưa có tin nhắn";
        var blocked = !!(item && (item.isBlockedByMe || item.hasBlockedMe));
        var unread = Number(item && item.unreadCount) || 0;

        return [
            '<a class="native-chat-room-card native-chat-room-card--direct' + (blocked ? " is-blocked" : "") + '" href="/PickleballWeb/DirectChat/' + escapeHtml(roomId) + '">',
            renderDirectAvatar(Object.assign({}, otherUser, { fullName: fullName }), "native-chat-room-card__cover", ""),
            '<span class="native-chat-room-card__body">',
            '<span class="native-chat-room-card__top">',
            '<strong>' + escapeHtml(fullName) + "</strong>",
            '<span>' + escapeHtml(formatRoomTime(item && item.lastMessageAt)) + "</span>",
            "</span>",
            '<span class="native-chat-room-card__area">' + escapeHtml(blocked
                ? (item.isBlockedByMe ? "Bạn đang chặn người này" : "Hiện chưa thể nhắn tin")
                : (trimToEmpty(otherUser.phone) || trimToEmpty(otherUser.city) || ("ID: " + trimToEmpty(otherUser.userId)))) + "</span>",
            '<span class="native-chat-room-card__preview-row">',
            '<span class="native-chat-room-card__preview">' + escapeHtml(trimToEmpty(item && item.lastSenderName) && previewText !== "Chưa có tin nhắn" ? item.lastSenderName + ": " + previewText : previewText) + "</span>",
            unread > 0 ? '<span class="native-chat-room-card__badge">' + escapeHtml(unread > 99 ? "99+" : unread) + "</span>" : "",
            "</span>",
            "</span>",
            "</a>"
        ].join("");
    }

    function renderDirectSearchResult(item, openingUserId) {
        var userId = item && item.userId;
        var fullName = trimToEmpty(item && item.fullName) || "Thành viên";
        var blocked = !!(item && (item.isBlockedByMe || item.hasBlockedMe));
        var opening = trimToEmpty(openingUserId) && trimToEmpty(openingUserId) === trimToEmpty(userId);

        return [
            '<button class="native-chat-user-result' + (blocked ? " is-blocked" : "") + '" type="button" data-direct-chat-user-id="' + escapeHtml(userId) + '" ' + (opening ? "disabled" : "") + '>',
            renderDirectAvatar(item, "native-chat-user-result__avatar", ""),
            '<span class="native-chat-user-result__body">',
            '<span class="native-chat-user-result__name">' + escapeHtml(fullName) + (item && item.verified ? ' <ion-icon name="checkmark-circle"></ion-icon>' : "") + "</span>",
            '<span class="native-chat-user-result__meta">' + escapeHtml(trimToEmpty(item && item.phone) || trimToEmpty(item && item.city) || ("ID: " + trimToEmpty(userId))) + "</span>",
            '<span class="native-chat-user-result__hint">' + escapeHtml(blocked
                ? (item.isBlockedByMe ? "Bạn đang chặn người này" : "Người này hiện không nhận tin")
                : (item && item.existingRoomId ? "Đã có cuộc trò chuyện" : "Bấm để bắt đầu chat")) + "</span>",
            "</span>",
            opening ? '<span class="members-app-spinner native-chat-user-result__spinner" aria-hidden="true"></span>' : '<ion-icon name="chevron-forward"></ion-icon>',
            "</button>"
        ].join("");
    }

    function initChatListPage(root) {
        var refs = getCommonRefs(root);
        var state = {
            session: null,
            query: "",
            loading: false,
            error: "",
            authRequired: false,
            items: []
        };
        var refreshTimer = null;
        var removeRealtimeListener = null;

        setHeaderTitle(root, "Tin nhắn CLB");
        setHeaderAction(root, null);
        setHeaderExtra(root, [
            '<div class="native-chat-toolbar">',
            '<label class="native-inline-search__box native-inline-search__box--video">',
            '<input type="search" placeholder="Tìm tên CLB, khu vực..." autocomplete="off" data-chat-room-query-input>',
            '<ion-icon name="search"></ion-icon>',
            "</label>",
            '<p class="native-chat-toolbar__note">Chỉ hiển thị các phòng chat CLB mà tài khoản đã tham gia.</p>',
            "</div>"
        ].join(""));
        renderEmptyState(refs, "Bạn chưa có phòng chat CLB nào.");

        function filteredItems() {
            var query = normalizeSearchText(state.query);

            if (!query) {
                return state.items;
            }

            return state.items.filter(function (item) {
                return normalizeSearchText([
                    item && item.clubName,
                    item && item.areaText,
                    item && item.lastMessagePreview,
                    item && item.lastSenderName
                ].filter(Boolean).join(" ")).indexOf(query) >= 0;
            });
        }

        function render() {
            var items = filteredItems();
            refs.list.className = "native-page-list native-page-list--chat-rooms";

            if (state.authRequired) {
                refs.list.innerHTML = renderAuthPrompt({
                    icon: "chatbubbles-outline",
                    title: "Đăng nhập để vào chat CLB",
                    body: "Chỉ thành viên CLB đã đăng nhập mới xem được danh sách phòng chat.",
                    returnUrl: "/PickleballWeb/Chats"
                });
            } else {
                refs.list.innerHTML = items.map(renderChatRoomCard).join("");
            }

            toggleCommonState(refs, {
                loading: state.loading,
                itemsLength: state.authRequired ? 1 : items.length,
                error: state.error,
                hasMore: false
            });
        }

        function syncRoomSubscriptions() {
            state.items.forEach(function (item) {
                if (item && item.clubId) {
                    subscribeClubRealtime(item.clubId);
                }
            });
        }

        async function refreshRooms(options) {
            var silent = !!(options && options.silent);

            if (state.loading && !silent) {
                return;
            }

            if (!silent) {
                state.loading = true;
                state.error = "";
                state.authRequired = false;
                render();
            }

            try {
                var payload = await requestJson("/api/clubs/chat-rooms?page=1&pageSize=50", {
                    method: "GET",
                    headers: { Accept: "application/json" }
                });

                state.items = Array.isArray(payload && payload.items) ? payload.items : [];
                syncRoomSubscriptions();
            } catch (_error) {
                if (!silent) {
                    state.items = [];
                    state.error = "Không tải được danh sách phòng chat.";
                }
            } finally {
                if (!silent) {
                    state.loading = false;
                }
                render();
            }
        }

        function scheduleRefreshRooms() {
            window.clearTimeout(refreshTimer);
            refreshTimer = window.setTimeout(function () {
                refreshRooms({ silent: true });
            }, 250);
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
                var session = await requestJson("/api/web-auth/me", {
                    method: "GET",
                    headers: { Accept: "application/json" }
                });

                if (!(session && session.isAuthenticated)) {
                    state.session = null;
                    state.items = [];
                    state.authRequired = true;
                    return;
                }

                state.session = session;
                connectRealtime();
                await refreshRooms({ silent: true });
            } catch (_error) {
                state.items = [];
                state.error = "Không tải được danh sách phòng chat.";
            } finally {
                state.loading = false;
                render();
            }
        }

        if (refs.retry) {
            refs.retry.onclick = function () { load(); };
        }

        var queryInput = qs("[data-chat-room-query-input]", root);
        if (queryInput) {
            queryInput.addEventListener("input", function () {
                state.query = trimToEmpty(queryInput.value);
                render();
            });
        }

        removeRealtimeListener = addRealtimeListener(function (event) {
            var type = trimToEmpty(event && event.type);

            if (type === "__socket_open__") {
                syncRoomSubscriptions();
                return;
            }

            if (
                type === "club.notification" ||
                type === "club.message.created" ||
                type === "club.message.deleted"
            ) {
                scheduleRefreshRooms();
            }
        });

        window.addEventListener("pagehide", function () {
            window.clearTimeout(refreshTimer);
            if (removeRealtimeListener) {
                removeRealtimeListener();
                removeRealtimeListener = null;
            }
        }, { once: true });

        load();
    }

    function initUnifiedChatListPage(root) {
        var refs = getCommonRefs(root);
        var state = {
            session: null,
            mode: "direct",
            directQuery: "",
            clubQuery: "",
            loading: false,
            searchLoading: false,
            hasSearched: false,
            error: "",
            searchError: "",
            authRequired: false,
            directRooms: [],
            clubRooms: [],
            searchResults: [],
            openingUserId: ""
        };
        var refreshTimer = null;
        var removeRealtimeListener = null;

        setHeaderTitle(root, "Trò chuyện");
        setHeaderAction(root, {
            html: '<ion-icon name="person-add-outline"></ion-icon>',
            ariaLabel: "Tìm người chat",
            onClick: function () {
                state.mode = "direct";
                syncToolbarMode();
                render();

                var input = qs("[data-direct-chat-search-input]", root);
                if (input) {
                    input.focus();
                }
            }
        });
        setHeaderExtra(root, [
            '<div class="native-chat-toolbar native-chat-toolbar--unified">',
            '<form class="native-chat-search-form" data-direct-chat-search-form>',
            '<label class="native-inline-search__box native-inline-search__box--video">',
            '<input type="search" placeholder="Nhập số điện thoại hoặc ID" autocomplete="off" data-direct-chat-search-input>',
            '<ion-icon name="search"></ion-icon>',
            "</label>",
            '<button class="native-chat-search-form__button" type="submit" aria-label="Tìm"><ion-icon name="arrow-forward"></ion-icon></button>',
            "</form>",
            '<div class="native-chat-club-tools" data-chat-club-tools hidden>',
            '<label class="native-inline-search__box native-inline-search__box--video">',
            '<input type="search" placeholder="Tìm tên CLB, khu vực..." autocomplete="off" data-chat-room-query-input>',
            '<ion-icon name="search"></ion-icon>',
            "</label>",
            '<p class="native-chat-toolbar__note">Chỉ hiển thị các phòng chat CLB mà tài khoản đã tham gia.</p>',
            "</div>",
            "</div>"
        ].join(""));
        renderEmptyState(refs, "Bạn chưa có cuộc trò chuyện nào.");

        function syncToolbarMode() {
            qsa("[data-chat-mode]", root).forEach(function (button) {
                var active = trimToEmpty(button.getAttribute("data-chat-mode")) === state.mode;
                button.classList.toggle("is-active", active);
                button.setAttribute("aria-selected", active ? "true" : "false");
            });

            var directForm = qs("[data-direct-chat-search-form]", root);
            if (directForm) {
                directForm.hidden = state.mode !== "direct";
            }

            var clubTools = qs("[data-chat-club-tools]", root);
            if (clubTools) {
                clubTools.hidden = state.mode !== "club";
            }
        }

        function filteredClubItems() {
            var query = normalizeSearchText(state.clubQuery);

            if (!query) {
                return state.clubRooms;
            }

            return state.clubRooms.filter(function (item) {
                return normalizeSearchText([
                    item && item.clubName,
                    item && item.areaText,
                    item && item.lastMessagePreview,
                    item && item.lastSenderName
                ].filter(Boolean).join(" ")).indexOf(query) >= 0;
            });
        }

        function renderDirectSearchSection() {
            if (!state.hasSearched && !state.searchLoading) {
                return "";
            }

            var body = "";
            if (state.searchLoading) {
                body = '<div class="native-chat-inline-state"><span class="members-app-spinner" aria-hidden="true"></span><span>Đang tìm thành viên...</span></div>';
            } else if (state.searchError) {
                body = '<div class="native-chat-inline-state is-error">' + escapeHtml(state.searchError) + "</div>";
            } else if (state.searchResults.length) {
                body = '<div class="native-chat-user-results">' + state.searchResults.map(function (item) {
                    return renderDirectSearchResult(item, state.openingUserId);
                }).join("") + "</div>";
            } else {
                body = '<div class="native-chat-inline-state">Không tìm thấy thành viên phù hợp.</div>';
            }

            return [
                '<section class="native-chat-section">',
                '<h2>Kết quả tìm kiếm</h2>',
                body,
                "</section>"
            ].join("");
        }

        function renderDirectContent() {
            return [
                renderDirectSearchSection(),
                '<section class="native-chat-section">',
                '<h2>Tin nhắn cá nhân</h2>',
                state.directRooms.length
                    ? state.directRooms.map(renderDirectRoomCard).join("")
                    : '<div class="native-chat-inline-state">Chưa có cuộc trò chuyện cá nhân nào.</div>',
                "</section>"
            ].join("");
        }

        function renderClubContent() {
            var items = filteredClubItems();
            return [
                '<section class="native-chat-section">',
                '<h2>Phòng chat CLB</h2>',
                items.length
                    ? items.map(renderChatRoomCard).join("")
                    : '<div class="native-chat-inline-state">Bạn chưa có phòng chat CLB nào.</div>',
                "</section>"
            ].join("");
        }

        function render() {
            refs.list.className = "native-page-list native-page-list--chat-rooms native-page-list--chat-unified";

            if (state.authRequired) {
                refs.list.innerHTML = renderAuthPrompt({
                    icon: "chatbubbles-outline",
                    title: "Đăng nhập để trò chuyện",
                    body: "Bạn cần đăng nhập để tìm thành viên, chat cá nhân và xem phòng chat CLB.",
                    returnUrl: "/PickleballWeb/Chats"
                });
            } else {
                refs.list.innerHTML = state.mode === "direct"
                    ? renderDirectContent()
                    : renderClubContent();
            }

            toggleCommonState(refs, {
                loading: state.loading,
                itemsLength: state.authRequired ? 1 : 1,
                error: state.error,
                hasMore: false
            });
        }

        function syncDirectSubscriptions() {
            state.directRooms.forEach(function (item) {
                var roomId = item && (item.roomId || item.directChatRoomId);
                if (roomId) {
                    subscribeDirectRealtime(roomId);
                }
            });
        }

        function syncClubSubscriptions() {
            state.clubRooms.forEach(function (item) {
                if (item && item.clubId) {
                    subscribeClubRealtime(item.clubId);
                }
            });
        }

        async function refreshDirectRooms() {
            var payload = await requestJson("/api/direct-chats/rooms?page=1&pageSize=50", {
                method: "GET",
                headers: { Accept: "application/json" }
            });

            state.directRooms = Array.isArray(payload && payload.items) ? payload.items : [];
            syncDirectSubscriptions();
        }

        async function refreshClubRooms() {
            var payload = await requestJson("/api/clubs/chat-rooms?page=1&pageSize=50", {
                method: "GET",
                headers: { Accept: "application/json" }
            });

            state.clubRooms = Array.isArray(payload && payload.items) ? payload.items : [];
            syncClubSubscriptions();
        }

        async function refreshRooms(options) {
            var silent = !!(options && options.silent);

            if (state.loading && !silent) {
                return;
            }

            if (!silent) {
                state.loading = true;
                state.error = "";
                render();
            }

            try {
                await Promise.all([
                    refreshDirectRooms(),
                    refreshClubRooms()
                ]);
            } catch (_error) {
                if (!silent) {
                    state.directRooms = [];
                    state.clubRooms = [];
                    state.error = "Không tải được danh sách trò chuyện.";
                }
            } finally {
                if (!silent) {
                    state.loading = false;
                }
                render();
            }
        }

        function scheduleRefreshRooms() {
            window.clearTimeout(refreshTimer);
            refreshTimer = window.setTimeout(function () {
                refreshRooms({ silent: true });
            }, 250);
        }

        async function searchUsers() {
            var input = qs("[data-direct-chat-search-input]", root);
            var keyword = trimToEmpty(input && input.value);
            var isNumeric = /^\d+$/.test(keyword);

            state.directQuery = keyword;
            state.hasSearched = true;
            state.searchError = "";
            state.searchResults = [];

            if (!keyword || (keyword.length < 2 && !isNumeric)) {
                state.searchError = "Nhập ít nhất 2 ký tự hoặc nhập đúng ID thành viên.";
                render();
                return;
            }

            state.searchLoading = true;
            render();

            try {
                var payload = await requestJson("/api/direct-chats/users/search?keyword=" + encodeURIComponent(keyword) + "&page=1&pageSize=20", {
                    method: "GET",
                    headers: { Accept: "application/json" }
                });

                state.searchResults = Array.isArray(payload && payload.items) ? payload.items : [];
            } catch (error) {
                state.searchError = error.message || "Không tìm được thành viên.";
            } finally {
                state.searchLoading = false;
                render();
            }
        }

        async function openDirectChat(userId) {
            var targetId = Number(userId);
            if (!Number.isFinite(targetId) || targetId <= 0 || state.openingUserId) {
                return;
            }

            var item = state.searchResults.find(function (candidate) {
                return Number(candidate && candidate.userId) === targetId;
            });

            if (item && (item.isBlockedByMe || item.hasBlockedMe)) {
                window.alert(item.isBlockedByMe
                    ? "Bạn đang chặn người này. Hãy bỏ chặn để tiếp tục."
                    : "Người này hiện không thể nhận tin nhắn từ bạn.");
                return;
            }

            if (item && item.existingRoomId) {
                window.location.href = "/PickleballWeb/DirectChat/" + item.existingRoomId;
                return;
            }

            state.openingUserId = String(targetId);
            render();

            try {
                var payload = await requestJson("/api/direct-chats/rooms", {
                    method: "POST",
                    body: JSON.stringify({ targetUserId: targetId })
                });
                var room = payload && payload.item ? payload.item : null;
                var roomId = room && (room.roomId || room.directChatRoomId);

                if (!roomId) {
                    throw new Error("Không mở được phòng chat.");
                }

                window.location.href = "/PickleballWeb/DirectChat/" + roomId;
            } catch (error) {
                state.openingUserId = "";
                window.alert(error.message || "Không mở được phòng chat.");
                render();
            }
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
                var session = await requestJson("/api/web-auth/me", {
                    method: "GET",
                    headers: { Accept: "application/json" }
                });

                if (!(session && session.isAuthenticated)) {
                    state.session = null;
                    state.directRooms = [];
                    state.clubRooms = [];
                    state.authRequired = true;
                    return;
                }

                state.session = session;
                connectRealtime();
                await refreshRooms({ silent: true });
            } catch (_error) {
                state.directRooms = [];
                state.clubRooms = [];
                state.error = "Không tải được danh sách trò chuyện.";
            } finally {
                state.loading = false;
                render();
            }
        }

        if (refs.retry) {
            refs.retry.onclick = function () { load(); };
        }

        qsa("[data-chat-mode]", root).forEach(function (button) {
            button.addEventListener("click", function () {
                state.mode = trimToEmpty(button.getAttribute("data-chat-mode")) || "direct";
                syncToolbarMode();
                render();
            });
        });

        var directForm = qs("[data-direct-chat-search-form]", root);
        if (directForm) {
            directForm.addEventListener("submit", function (event) {
                event.preventDefault();
                searchUsers();
            });
        }

        var directInput = qs("[data-direct-chat-search-input]", root);
        if (directInput) {
            directInput.addEventListener("input", function () {
                state.directQuery = trimToEmpty(directInput.value);
                state.searchError = "";
            });
        }

        var clubQueryInput = qs("[data-chat-room-query-input]", root);
        if (clubQueryInput) {
            clubQueryInput.addEventListener("input", function () {
                state.clubQuery = trimToEmpty(clubQueryInput.value);
                render();
            });
        }

        refs.list.addEventListener("click", function (event) {
            var userButton = event.target && event.target.closest
                ? event.target.closest("[data-direct-chat-user-id]")
                : null;

            if (userButton) {
                event.preventDefault();
                openDirectChat(userButton.getAttribute("data-direct-chat-user-id"));
            }
        });

        removeRealtimeListener = addRealtimeListener(function (event) {
            var type = trimToEmpty(event && event.type);

            if (type === "__socket_open__") {
                syncDirectSubscriptions();
                syncClubSubscriptions();
                return;
            }

            if (
                type === "direct.notification" ||
                type === "direct.message.created" ||
                type === "direct.message.recalled" ||
                type === "direct.message.updated" ||
                type === "direct.message.deleted" ||
                type === "direct.block.changed" ||
                type === "club.notification" ||
                type === "club.message.created" ||
                type === "club.message.deleted"
            ) {
                scheduleRefreshRooms();
            }
        });

        window.addEventListener("pagehide", function () {
            window.clearTimeout(refreshTimer);
            state.directRooms.forEach(function (item) {
                var roomId = item && (item.roomId || item.directChatRoomId);
                if (roomId) {
                    unsubscribeDirectRealtime(roomId);
                }
            });
            state.clubRooms.forEach(function (item) {
                if (item && item.clubId) {
                    unsubscribeClubRealtime(item.clubId);
                }
            });
            if (removeRealtimeListener) {
                removeRealtimeListener();
                removeRealtimeListener = null;
            }
        }, { once: true });

        syncToolbarMode();
        load();
    }

    function renderDirectChatRoomHeader(room) {
        if (!room) {
            return "";
        }

        var otherUser = room.otherUser || {};
        var fullName = trimToEmpty(otherUser.fullName || room.title) || "Thành viên";
        var meta = trimToEmpty(otherUser.phone) || trimToEmpty(otherUser.city) || ("ID: " + trimToEmpty(otherUser.userId));
        var status = room.isBlockedByMe
            ? "Bạn đang chặn"
            : room.hasBlockedMe
                ? "Không thể nhắn"
                : "Chat cá nhân";

        return [
            '<div class="native-chat-room-head native-chat-room-head--direct">',
            renderDirectAvatar(Object.assign({}, otherUser, { fullName: fullName }), "native-chat-room-head__cover", ""),
            '<div class="native-chat-room-head__copy">',
            '<strong>' + escapeHtml(fullName) + "</strong>",
            '<span>' + escapeHtml(meta) + "</span>",
            '<small class="native-chat-room-head__status">' + escapeHtml(status) + "</small>",
            "</div>",
            "</div>"
        ].join("");
    }

    function renderDirectChatMessage(item, myUserId) {
        var senderId = trimToEmpty(item && (item.senderUserId || item.sender && item.sender.userId));
        var isMine = senderId && trimToEmpty(myUserId) && senderId === trimToEmpty(myUserId);
        var senderName = trimToEmpty(item && item.sender && item.sender.fullName) || "Thành viên";
        var avatarUrl = normalizeMediaUrl(item && item.sender && item.sender.avatarUrl);
        var content = trimToEmpty(item && item.content);
        var mediaUrl = normalizeMediaUrl(item && item.mediaUrl);
        var messageId = item && (item.messageId || item.directChatMessageId);
        var recalled = !!(item && item.isRecalled);
        var edited = !!(item && item.editedAt) && !recalled;

        return [
            '<div class="native-chat-message native-chat-message--direct' + (isMine ? " is-mine" : "") + '">',
            isMine
                ? ""
                : avatarUrl
                    ? '<span class="native-chat-message__avatar"><img src="' + escapeHtml(avatarUrl) + '" alt="' + escapeHtml(senderName) + '" loading="lazy"></span>'
                    : '<span class="native-chat-message__avatar native-chat-message__avatar--fallback"><ion-icon name="person-outline"></ion-icon></span>',
            '<div class="native-chat-message__stack">',
            isMine ? "" : '<span class="native-chat-message__sender">' + escapeHtml(senderName) + "</span>",
            '<div class="native-chat-message__bubble' + (isMine ? " is-mine" : "") + (recalled ? " is-recalled" : "") + '">',
            !recalled && mediaUrl ? '<img class="native-chat-message__media" src="' + escapeHtml(mediaUrl) + '" alt="Tin nhắn hình ảnh" loading="lazy">' : "",
            recalled
                ? '<p class="native-chat-message__text">Tin nhắn đã được thu hồi.</p>'
                : content
                    ? '<p class="native-chat-message__text">' + renderTextWithBreaks(content) + "</p>"
                    : '<p class="native-chat-message__text">[Tin nhắn]</p>',
            "</div>",
            '<span class="native-chat-message__meta">',
            '<span class="native-chat-message__time">' + escapeHtml(formatMessageTime(item && item.sentAt)) + "</span>",
            edited ? '<span class="native-chat-message__edited">Đã sửa</span>' : "",
            '<span class="native-chat-message__actions">',
            isMine && !recalled && trimToEmpty(item && item.messageType).toLowerCase() === "text"
                ? '<button type="button" data-direct-edit-message-id="' + escapeHtml(messageId) + '"><ion-icon name="create-outline"></ion-icon><span>Sửa</span></button>'
                : "",
            isMine && !recalled
                ? '<button type="button" data-direct-recall-message-id="' + escapeHtml(messageId) + '"><ion-icon name="refresh-circle-outline"></ion-icon><span>Thu hồi</span></button>'
                : "",
            isMine && !recalled
                ? '<button type="button" data-direct-delete-message-id="' + escapeHtml(messageId) + '"><ion-icon name="trash-outline"></ion-icon><span>Xóa</span></button>'
                : "",
            !isMine && !recalled
                ? '<button type="button" data-direct-report-message-id="' + escapeHtml(messageId) + '"><ion-icon name="flag-outline"></ion-icon><span>Báo cáo</span></button>'
                : "",
            !isMine && !recalled
                ? '<button type="button" data-direct-block-user-id="' + escapeHtml(senderId) + '" data-direct-block-message-id="' + escapeHtml(messageId) + '"><ion-icon name="ban-outline"></ion-icon><span>Chặn</span></button>'
                : "",
            "</span>",
            "</span>",
            "</div>",
            "</div>"
        ].join("");
    }

    function renderDirectBlockBanner(state) {
        if (!(state && (state.isBlockedByMe || state.hasBlockedMe))) {
            return "";
        }

        return [
            '<article class="native-chat-block-banner">',
            '<ion-icon name="ban-outline"></ion-icon>',
            '<div>',
            '<strong>' + escapeHtml(state.isBlockedByMe ? "Bạn đã chặn người này" : "Người này hiện không thể nhắn tin") + "</strong>",
            '<p>' + escapeHtml(state.isBlockedByMe
                ? "Bỏ chặn để tiếp tục gửi và nhận tin nhắn trong phòng chat này."
                : "Bạn không thể gửi tin nhắn cho đến khi trạng thái chặn được thay đổi.") + "</p>",
            "</div>",
            "</article>"
        ].join("");
    }

    function initDirectChatRoomPage(root) {
        var refs = getCommonRefs(root);
        var roomId = Number(root.getAttribute("data-native-page-id"));
        var state = {
            session: null,
            room: null,
            items: [],
            typingUsers: [],
            loading: false,
            error: "",
            authRequired: false,
            composerText: "",
            sending: false,
            isBlockedByMe: false,
            hasBlockedMe: false,
            page: 1,
            pageSize: 30,
            total: 0,
            loadingMore: false
        };
        var removeRealtimeListener = null;
        var removeViewportListeners = null;
        var typingTimer = null;
        var typingExpiryTimers = {};

        renderEmptyState(refs, "Chưa có tin nhắn trong phòng chat này.");

        function myUserId() {
            return getSessionUserId(state.session);
        }

        function otherUser() {
            return state.room && state.room.otherUser ? state.room.otherUser : {};
        }

        function otherUserId() {
            return trimToEmpty(otherUser().userId);
        }

        function getMessageId(item) {
            return trimToEmpty(item && (item.messageId || item.directChatMessageId));
        }

        function upsertMessage(item) {
            if (!item) {
                return false;
            }

            var messageId = getMessageId(item);
            var replaced = false;

            if (messageId) {
                state.items = state.items.map(function (existing) {
                    if (getMessageId(existing) === messageId) {
                        replaced = true;
                        return item;
                    }

                    return existing;
                });

                if (replaced) {
                    return false;
                }
            }

            state.items = state.items.concat([item]).sort(function (a, b) {
                var aDate = parseDate(a && a.sentAt);
                var bDate = parseDate(b && b.sentAt);
                return (aDate ? aDate.getTime() : 0) - (bDate ? bDate.getTime() : 0);
            });

            return true;
        }

        function mergeMessages(items) {
            var changed = false;

            (Array.isArray(items) ? items : []).forEach(function (item) {
                if (upsertMessage(item)) {
                    changed = true;
                }
            });

            return changed;
        }

        function hasMoreMessages() {
            return state.items.length < state.total;
        }

        function markMessageRecalled(messageId, item) {
            var id = trimToEmpty(messageId);
            if (!id) {
                return;
            }

            if (item) {
                upsertMessage(item);
                return;
            }

            state.items = state.items.map(function (existing) {
                if (getMessageId(existing) !== id) {
                    return existing;
                }

                return Object.assign({}, existing, {
                    content: null,
                    mediaUrl: null,
                    isRecalled: true
                });
            });
        }

        function findMessage(messageId) {
            var id = trimToEmpty(messageId);
            if (!id) {
                return null;
            }

            return state.items.find(function (item) {
                return getMessageId(item) === id;
            }) || null;
        }

        function removeMessage(messageId) {
            var id = trimToEmpty(messageId);
            if (!id) {
                return;
            }

            state.items = state.items.filter(function (item) {
                return getMessageId(item) !== id;
            });
        }

        function clearTypingUser(userId) {
            var id = trimToEmpty(userId);
            if (!id) {
                return;
            }

            window.clearTimeout(typingExpiryTimers[id]);
            delete typingExpiryTimers[id];
            state.typingUsers = state.typingUsers.filter(function (item) {
                return trimToEmpty(item && item.userId) !== id;
            });
        }

        function setTypingUser(event) {
            var userId = trimToEmpty(event && event.userId);

            if (!userId || userId === myUserId()) {
                return;
            }

            if (!(event && event.isTyping)) {
                clearTypingUser(userId);
                render();
                return;
            }

            var fullName = trimToEmpty(event && event.fullName) || trimToEmpty(otherUser().fullName) || "Thành viên";
            var exists = false;
            state.typingUsers = state.typingUsers.map(function (item) {
                if (trimToEmpty(item && item.userId) === userId) {
                    exists = true;
                    return {
                        userId: userId,
                        fullName: fullName
                    };
                }

                return item;
            });

            if (!exists) {
                state.typingUsers = state.typingUsers.concat([{
                    userId: userId,
                    fullName: fullName
                }]);
            }

            window.clearTimeout(typingExpiryTimers[userId]);
            typingExpiryTimers[userId] = window.setTimeout(function () {
                clearTypingUser(userId);
                render();
            }, 2800);

            render();
        }

        function renderTypingIndicator() {
            if (!state.typingUsers.length) {
                return "";
            }

            var names = state.typingUsers
                .map(function (item) { return trimToEmpty(item && item.fullName); })
                .filter(Boolean)
                .slice(0, 2)
                .join(", ");

            return [
                '<div class="native-chat-typing">',
                '<span class="native-chat-typing__dots"><i></i><i></i><i></i></span>',
                '<span>' + escapeHtml((names || "Thành viên") + " đang nhập...") + "</span>",
                "</div>"
            ].join("");
        }

        function canSend() {
            return !state.isBlockedByMe && !state.hasBlockedMe;
        }

        function syncVisualViewportHeight() {
            var viewport = window.visualViewport;
            var height = viewport && viewport.height ? viewport.height : window.innerHeight;

            if (Number.isFinite(height) && height > 0) {
                root.style.setProperty("--direct-chat-height", Math.round(height) + "px");
            }
        }

        function bindVisualViewportHeight() {
            var viewport = window.visualViewport;
            var target = viewport || window;

            syncVisualViewportHeight();
            target.addEventListener("resize", syncVisualViewportHeight);
            if (viewport) {
                viewport.addEventListener("scroll", syncVisualViewportHeight);
            } else {
                window.addEventListener("orientationchange", syncVisualViewportHeight);
            }

            return function () {
                target.removeEventListener("resize", syncVisualViewportHeight);
                if (viewport) {
                    viewport.removeEventListener("scroll", syncVisualViewportHeight);
                } else {
                    window.removeEventListener("orientationchange", syncVisualViewportHeight);
                }
                root.style.removeProperty("--direct-chat-height");
            };
        }

        function setComposerFocused(isFocused) {
            root.classList.toggle("is-composer-focused", !!isFocused);
            syncVisualViewportHeight();
            if (isFocused) {
                window.setTimeout(function () {
                    scrollChatToBottom(root);
                }, 80);
            }
        }

        function syncHeader() {
            var name = trimToEmpty(otherUser().fullName || state.room && state.room.title);
            setHeaderTitle(root, name || "Chat cá nhân");
            if (state.room) {
                setHeaderExtra(root, renderDirectChatRoomHeader(Object.assign({}, state.room, {
                    isBlockedByMe: state.isBlockedByMe,
                    hasBlockedMe: state.hasBlockedMe
                })));
            } else {
                setHeaderExtra(root, "");
            }

            if (!state.session || !otherUserId()) {
                setHeaderAction(root, null);
                return;
            }

            setHeaderAction(root, {
                html: state.isBlockedByMe
                    ? '<ion-icon name="lock-open-outline"></ion-icon>'
                    : '<ion-icon name="ban-outline"></ion-icon>',
                ariaLabel: state.isBlockedByMe ? "Bỏ chặn người dùng" : "Chặn người dùng",
                onClick: function () {
                    if (state.isBlockedByMe) {
                        unblockOtherUser();
                    } else {
                        blockOtherUser(null);
                    }
                }
            });
        }

        function render() {
            refs.list.className = "native-page-list native-page-list--chat-room";

            if (state.authRequired) {
                refs.list.innerHTML = renderAuthPrompt({
                    icon: "chatbubble-ellipses-outline",
                    title: "Đăng nhập để vào chat cá nhân",
                    body: "Bạn cần đăng nhập để xem và gửi tin nhắn cá nhân.",
                    returnUrl: "/PickleballWeb/DirectChat/" + roomId
                });
            } else {
                refs.list.innerHTML = [
                    '<section class="native-chat-room native-chat-room--direct">',
                    renderDirectBlockBanner(state),
                    state.items.length > 0
                        ? '<div class="native-chat-room__thread" data-direct-chat-thread>' +
                        (state.loadingMore ? '<div class="native-chat-room__loading-more">Đang tải tin nhắn cũ hơn...</div>' : "") +
                        state.items.map(function (item) {
                            return renderDirectChatMessage(item, myUserId());
                        }).join("") + renderTypingIndicator() + '<div data-chat-room-bottom></div></div>'
                        : '<div class="native-chat-room__empty">Chưa có tin nhắn nào trong phòng chat này.</div>',
                    '<form class="native-chat-composer' + (canSend() ? "" : " is-disabled") + '" data-direct-chat-compose-form>',
                    '<textarea class="native-chat-composer__input" rows="1" placeholder="' + escapeHtml(canSend() ? "Nhập tin nhắn..." : "Hiện chưa thể gửi tin nhắn") + '" data-direct-chat-compose-input ' + (canSend() ? "" : "disabled") + '>' + escapeHtml(state.composerText) + '</textarea>',
                    '<button class="native-chat-composer__send" type="submit" data-direct-chat-compose-send ' + ((state.sending || !trimToEmpty(state.composerText) || !canSend()) ? "disabled" : "") + '>',
                    '<ion-icon name="send"></ion-icon>',
                    "</button>",
                    "</form>",
                    "</section>"
                ].join("");

                bindComposer();
                bindMessageThread();
            }

            toggleCommonState(refs, {
                loading: state.loading,
                itemsLength: state.authRequired ? 1 : state.items.length + 1,
                error: state.error,
                hasMore: false
            });
        }

        function bindMessageThread() {
            var thread = qs("[data-direct-chat-thread]", root);
            if (!thread) {
                return;
            }

            thread.addEventListener("scroll", function () {
                if (thread.scrollTop <= 48) {
                    loadOlderMessages();
                }
            }, { passive: true });
        }

        function bindComposer() {
            var form = qs("[data-direct-chat-compose-form]", root);
            var input = qs("[data-direct-chat-compose-input]", root);
            var sendButton = qs("[data-direct-chat-compose-send]", root);

            if (input) {
                input.value = state.composerText;
                input.addEventListener("focus", function () {
                    setComposerFocused(true);
                });
                input.addEventListener("blur", function () {
                    window.setTimeout(function () {
                        setComposerFocused(false);
                    }, 80);
                });
                input.addEventListener("input", function () {
                    state.composerText = input.value;
                    if (sendButton) {
                        sendButton.disabled = state.sending || !trimToEmpty(state.composerText) || !canSend();
                    }

                    sendDirectTypingRealtime(roomId, trimToEmpty(state.composerText).length > 0);
                    window.clearTimeout(typingTimer);
                    typingTimer = window.setTimeout(function () {
                        sendDirectTypingRealtime(roomId, false);
                    }, 1200);
                });
            }

            if (form) {
                form.addEventListener("submit", async function (event) {
                    event.preventDefault();

                    var content = trimToEmpty(state.composerText);
                    if (!content || state.sending || !canSend()) {
                        return;
                    }

                    state.sending = true;
                    render();

                    try {
                        var payload = await requestJson("/api/direct-chats/rooms/" + roomId + "/messages", {
                            method: "POST",
                            body: JSON.stringify({
                                messageType: "text",
                                content: content
                            })
                        });
                        var saved = payload && payload.item ? payload.item : null;
                        if (saved) {
                            upsertMessage(saved);
                        }
                        state.composerText = "";
                        sendDirectTypingRealtime(roomId, false);
                    } catch (error) {
                        if (error && error.payload) {
                            state.isBlockedByMe = !!error.payload.isBlockedByMe;
                            state.hasBlockedMe = !!error.payload.hasBlockedMe;
                            syncHeader();
                        }
                        window.alert(error.message || "Không gửi được tin nhắn.");
                    } finally {
                        state.sending = false;
                        render();
                        scrollChatToBottom(root);
                    }
                });
            }
        }

        async function recallMessage(messageId) {
            var id = Number(messageId);
            if (!Number.isFinite(id) || id <= 0) {
                return;
            }

            if (!window.confirm("Thu hồi tin nhắn này?")) {
                return;
            }

            try {
                var payload = await requestJson("/api/direct-chats/messages/" + id + "/recall", {
                    method: "POST"
                });
                markMessageRecalled(id, payload && payload.item);
                render();
            } catch (error) {
                window.alert(error.message || "Không thu hồi được tin nhắn.");
            }
        }

        async function editMessage(messageId) {
            var id = Number(messageId);
            if (!Number.isFinite(id) || id <= 0) {
                return;
            }

            var current = findMessage(id);
            var oldContent = trimToEmpty(current && current.content);
            var nextContent = window.prompt("Sửa tin nhắn", oldContent);
            if (nextContent == null) {
                return;
            }

            nextContent = trimToEmpty(nextContent);
            if (!nextContent || nextContent === oldContent) {
                return;
            }

            try {
                var payload = await requestJson("/api/direct-chats/messages/" + id, {
                    method: "PATCH",
                    body: JSON.stringify({
                        content: nextContent
                    })
                });
                if (payload && payload.item) {
                    upsertMessage(payload.item);
                }
                render();
            } catch (error) {
                window.alert(error.message || "Không sửa được tin nhắn.");
            }
        }

        async function deleteMessage(messageId) {
            var id = Number(messageId);
            if (!Number.isFinite(id) || id <= 0) {
                return;
            }

            if (!window.confirm("Xóa tin nhắn này?")) {
                return;
            }

            try {
                await requestJson("/api/direct-chats/messages/" + id, {
                    method: "DELETE"
                });
                removeMessage(id);
                render();
            } catch (error) {
                window.alert(error.message || "Không xóa được tin nhắn.");
            }
        }

        function normalizeDirectReportReason(value) {
            var normalized = trimToEmpty(value)
                .toLowerCase()
                .replace(/-/g, "_")
                .replace(/\s+/g, "_");

            if (
                normalized === "hate_or_harassment" ||
                normalized === "violent_threat" ||
                normalized === "sexual_content" ||
                normalized === "spam_or_scam" ||
                normalized === "other"
            ) {
                return normalized;
            }

            return "other";
        }

        async function reportMessage(messageId) {
            var id = Number(messageId);
            if (!Number.isFinite(id) || id <= 0) {
                return;
            }

            var current = findMessage(id);
            if (!current) {
                return;
            }

            var reason = window.prompt(
                "Lý do báo cáo: hate_or_harassment, violent_threat, sexual_content, spam_or_scam, other",
                "other"
            );
            if (reason == null) {
                return;
            }

            var sender = current.sender || {};
            var targetId = Number(current.senderUserId || sender.userId || otherUserId());
            if (!Number.isFinite(targetId) || targetId <= 0) {
                window.alert("Không xác định được người gửi tin nhắn.");
                return;
            }

            try {
                var payload = await requestJson("/api/moderation/reports", {
                    method: "POST",
                    body: JSON.stringify({
                        kind: "message",
                        reason: normalizeDirectReportReason(reason),
                        directChatRoomId: roomId,
                        directChatMessageId: id,
                        messageContent: trimToEmpty(current.content),
                        targetUserId: targetId,
                        targetUserName: trimToEmpty(sender.fullName) || trimToEmpty(otherUser().fullName) || "Thành viên",
                        source: "direct_chat_report_web"
                    })
                });

                window.alert(payload && payload.developerNotified
                    ? "Báo cáo đã được gửi tới moderation."
                    : "Báo cáo đã được ghi nhận.");
            } catch (error) {
                window.alert(error.message || "Không gửi được báo cáo.");
            }
        }

        async function blockOtherUser(sourceMessageId) {
            var targetId = Number(otherUserId());
            if (!Number.isFinite(targetId) || targetId <= 0 || state.isBlockedByMe) {
                return;
            }

            if (!window.confirm("Chặn người dùng này?")) {
                return;
            }

            try {
                await requestJson("/api/direct-chats/users/" + targetId + "/block", {
                    method: "POST",
                    body: JSON.stringify({
                        roomId: roomId,
                        messageId: Number(sourceMessageId) || null,
                        reason: "other",
                        notes: "Chặn từ chat cá nhân web."
                    })
                });
                state.isBlockedByMe = true;
                state.composerText = "";
                syncHeader();
                render();
            } catch (error) {
                window.alert(error.message || "Không chặn được người dùng.");
            }
        }

        async function unblockOtherUser() {
            var targetId = Number(otherUserId());
            if (!Number.isFinite(targetId) || targetId <= 0 || !state.isBlockedByMe) {
                return;
            }

            try {
                await requestJson("/api/direct-chats/users/" + targetId + "/block", {
                    method: "DELETE"
                });
                state.isBlockedByMe = false;
                syncHeader();
                render();
            } catch (error) {
                window.alert(error.message || "Không bỏ chặn được người dùng.");
            }
        }

        function applyBlockEvent(event) {
            var payload = event && event.payload ? event.payload : event;
            var currentUserId = myUserId();
            var targetId = otherUserId();
            var blockerId = trimToEmpty(payload && payload.blockerUserId);
            var blockedId = trimToEmpty(payload && payload.blockedUserId);
            var eventRoomId = Number(payload && (payload.roomId || payload.directChatRoomId));

            if (eventRoomId > 0 && eventRoomId !== roomId) {
                return false;
            }

            if (!currentUserId || !targetId) {
                return false;
            }

            if (blockerId === currentUserId && blockedId === targetId) {
                state.isBlockedByMe = !!(payload && payload.isBlocked);
                return true;
            }

            if (blockerId === targetId && blockedId === currentUserId) {
                state.hasBlockedMe = !!(payload && payload.isBlocked);
                return true;
            }

            return false;
        }

        async function loadOlderMessages() {
            if (state.loading || state.loadingMore || !hasMoreMessages()) {
                return;
            }

            var thread = qs("[data-direct-chat-thread]", root);
            var previousHeight = thread ? thread.scrollHeight : 0;
            var previousTop = thread ? thread.scrollTop : 0;
            var nextPage = state.page + 1;

            state.loadingMore = true;
            render();

            try {
                var payload = await requestJson("/api/direct-chats/rooms/" + roomId + "/messages?page=" + nextPage + "&pageSize=" + state.pageSize, {
                    method: "GET",
                    headers: { Accept: "application/json" }
                });

                state.page = payload && payload.page ? Number(payload.page) || nextPage : nextPage;
                state.total = payload && payload.total ? Number(payload.total) || state.total : state.total;
                mergeMessages(payload && payload.items);
            } catch (_error) {
                state.error = "Không tải được tin nhắn cũ hơn.";
            } finally {
                state.loadingMore = false;
                render();

                var nextThread = qs("[data-direct-chat-thread]", root);
                if (nextThread && previousHeight > 0) {
                    nextThread.scrollTop = Math.max(0, nextThread.scrollHeight - previousHeight + previousTop);
                }
            }
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
                var session = await requestJson("/api/web-auth/me", {
                    method: "GET",
                    headers: { Accept: "application/json" }
                });

                if (!(session && session.isAuthenticated)) {
                    state.session = null;
                    state.room = null;
                    state.items = [];
                    state.page = 1;
                    state.total = 0;
                    state.authRequired = true;
                    syncHeader();
                    return;
                }

                state.session = session;

                var payloads = await Promise.all([
                    requestJson("/api/direct-chats/rooms/" + roomId, {
                        method: "GET",
                        headers: { Accept: "application/json" }
                    }),
                    requestJson("/api/direct-chats/rooms/" + roomId + "/messages?page=1&pageSize=" + state.pageSize, {
                        method: "GET",
                        headers: { Accept: "application/json" }
                    })
                ]);
                var roomPayload = payloads[0];
                var messagePayload = payloads[1];

                state.room = roomPayload && roomPayload.item ? roomPayload.item : roomPayload;
                state.items = Array.isArray(messagePayload && messagePayload.items) ? messagePayload.items : [];
                state.page = messagePayload && messagePayload.page ? Number(messagePayload.page) || 1 : 1;
                state.total = messagePayload && messagePayload.total ? Number(messagePayload.total) || state.items.length : state.items.length;
                state.isBlockedByMe = !!((messagePayload && messagePayload.isBlockedByMe) || (state.room && state.room.isBlockedByMe));
                state.hasBlockedMe = !!((messagePayload && messagePayload.hasBlockedMe) || (state.room && state.room.hasBlockedMe));
                syncHeader();
                connectRealtime();
                subscribeDirectRealtime(roomId);
            } catch (_error) {
                state.items = [];
                state.room = null;
                state.page = 1;
                state.total = 0;
                state.error = "Không tải được phòng chat cá nhân.";
                syncHeader();
            } finally {
                state.loading = false;
                render();
            }
        }

        if (refs.retry) {
            refs.retry.onclick = function () { load(); };
        }

        refs.list.addEventListener("click", function (event) {
            var editButton = event.target && event.target.closest
                ? event.target.closest("[data-direct-edit-message-id]")
                : null;
            if (editButton) {
                event.preventDefault();
                editMessage(editButton.getAttribute("data-direct-edit-message-id"));
                return;
            }

            var recallButton = event.target && event.target.closest
                ? event.target.closest("[data-direct-recall-message-id]")
                : null;
            if (recallButton) {
                event.preventDefault();
                recallMessage(recallButton.getAttribute("data-direct-recall-message-id"));
                return;
            }

            var deleteButton = event.target && event.target.closest
                ? event.target.closest("[data-direct-delete-message-id]")
                : null;
            if (deleteButton) {
                event.preventDefault();
                deleteMessage(deleteButton.getAttribute("data-direct-delete-message-id"));
                return;
            }

            var reportButton = event.target && event.target.closest
                ? event.target.closest("[data-direct-report-message-id]")
                : null;
            if (reportButton) {
                event.preventDefault();
                reportMessage(reportButton.getAttribute("data-direct-report-message-id"));
                return;
            }

            var blockButton = event.target && event.target.closest
                ? event.target.closest("[data-direct-block-user-id]")
                : null;
            if (blockButton) {
                event.preventDefault();
                blockOtherUser(blockButton.getAttribute("data-direct-block-message-id"));
            }
        });

        removeRealtimeListener = addRealtimeListener(function (event) {
            var type = trimToEmpty(event && event.type);
            var eventRoomId = Number(event && (event.roomId || event.directChatRoomId));

            if (type === "__socket_open__") {
                if (state.session && !state.authRequired) {
                    subscribeDirectRealtime(roomId);
                }
                return;
            }

            if (type === "direct.block.changed") {
                if (applyBlockEvent(event)) {
                    syncHeader();
                    render();
                }
                return;
            }

            if (eventRoomId !== roomId) {
                return;
            }

            if (type === "direct.message.created") {
                var item = event && event.item ? event.item : null;
                var added = upsertMessage(item);
                clearTypingUser(item && item.senderUserId);
                render();
                if (added) {
                    scrollChatToBottom(root);
                }
                return;
            }

            if (type === "direct.message.recalled") {
                markMessageRecalled(event && (event.messageId || event.directChatMessageId), event && event.item);
                render();
                return;
            }

            if (type === "direct.message.updated") {
                if (event && event.item) {
                    upsertMessage(event.item);
                    render();
                }
                return;
            }

            if (type === "direct.message.deleted") {
                removeMessage(event && (event.messageId || event.directChatMessageId));
                render();
                return;
            }

            if (type === "direct.typing") {
                setTypingUser(event);
            }
        });

        window.addEventListener("pagehide", function () {
            window.clearTimeout(typingTimer);
            Object.keys(typingExpiryTimers).forEach(function (key) {
                window.clearTimeout(typingExpiryTimers[key]);
            });
            root.classList.remove("is-composer-focused");
            if (removeViewportListeners) {
                removeViewportListeners();
                removeViewportListeners = null;
            }
            sendDirectTypingRealtime(roomId, false);
            unsubscribeDirectRealtime(roomId);
            if (removeRealtimeListener) {
                removeRealtimeListener();
                removeRealtimeListener = null;
            }
        }, { once: true });

        setHeaderTitle(root, "Chat cá nhân");
        setHeaderAction(root, null);
        setHeaderExtra(root, "");
        removeViewportListeners = bindVisualViewportHeight();
        load();
    }

    function renderChatRoomHeader(club) {
        if (!club) {
            return "";
        }

        var coverUrl = normalizeMediaUrl(club && (club.coverUrl || club.clubCoverUrl));
        var clubName = trimToEmpty(club && (club.clubName || club.name)) || "Trò chuyện CLB";
        var areaText = trimToEmpty(club && club.areaText);

        return [
            '<div class="native-chat-room-head">',
            coverUrl
                ? '<span class="native-chat-room-head__cover"><img src="' + escapeHtml(coverUrl) + '" alt="' + escapeHtml(clubName) + '" loading="lazy"></span>'
                : '<span class="native-chat-room-head__cover native-chat-room-head__cover--fallback"><ion-icon name="people-outline"></ion-icon></span>',
            '<div class="native-chat-room-head__copy">',
            '<strong>' + escapeHtml(clubName) + "</strong>",
            areaText ? '<span>' + escapeHtml(areaText) + "</span>" : "",
            "</div>",
            '<a class="native-chat-room-head__link" href="/PickleballWeb/Club/' + escapeHtml(club && club.clubId) + '"><ion-icon name="open-outline"></ion-icon></a>',
            "</div>"
        ].join("");
    }

    function renderChatMessage(item, myUserId) {
        var senderId = trimToEmpty(item && (item.senderUserId || item.sender && item.sender.userId));
        var isMine = senderId && myUserId && senderId === myUserId;
        var senderName = trimToEmpty(item && item.sender && item.sender.fullName) || "Thành viên";
        var avatarUrl = normalizeMediaUrl(item && item.sender && item.sender.avatarUrl);
        var content = trimToEmpty(item && item.content);
        var mediaUrl = normalizeMediaUrl(item && item.mediaUrl);

        return [
            '<div class="native-chat-message' + (isMine ? " is-mine" : "") + '">',
            isMine
                ? ""
                : avatarUrl
                    ? '<span class="native-chat-message__avatar"><img src="' + escapeHtml(avatarUrl) + '" alt="' + escapeHtml(senderName) + '" loading="lazy"></span>'
                    : '<span class="native-chat-message__avatar native-chat-message__avatar--fallback"><ion-icon name="person-outline"></ion-icon></span>',
            '<div class="native-chat-message__stack">',
            isMine ? "" : '<span class="native-chat-message__sender">' + escapeHtml(senderName) + "</span>",
            '<div class="native-chat-message__bubble' + (isMine ? " is-mine" : "") + '">',
            mediaUrl ? '<img class="native-chat-message__media" src="' + escapeHtml(mediaUrl) + '" alt="Tin nhắn hình ảnh" loading="lazy">' : "",
            content ? '<p class="native-chat-message__text">' + renderTextWithBreaks(content) + "</p>" : "",
            !content && !mediaUrl ? '<p class="native-chat-message__text">[Tin nhắn]</p>' : "",
            "</div>",
            '<span class="native-chat-message__time">' + escapeHtml(formatMessageTime(item && item.sentAt)) + "</span>",
            "</div>",
            "</div>"
        ].join("");
    }

    function renderChatRoomAccessPrompt(clubId) {
        return [
            '<article class="native-auth-prompt native-auth-prompt--panel">',
            '<span class="native-auth-prompt__icon"><ion-icon name="shield-outline"></ion-icon></span>',
            "<strong>Bạn chưa có quyền vào phòng chat này</strong>",
            "<p>Hệ thống chỉ cho phép thành viên CLB xem và gửi tin nhắn trong phòng chat.</p>",
            '<a class="native-auth-prompt__button" href="/PickleballWeb/Club/' + escapeHtml(clubId) + '">Mở trang CLB</a>',
            "</article>"
        ].join("");
    }

    function scrollChatToBottom(root) {
        var thread = qs(".native-chat-room__thread", root);
        var anchor = qs("[data-chat-room-bottom]", root);

        if (!anchor) {
            return;
        }

        function applyScroll() {
            if (thread) {
                thread.scrollTop = Math.max(0, thread.scrollHeight - thread.clientHeight);
                return;
            }

            anchor.scrollIntoView({ block: "end", inline: "nearest" });
        }

        function scheduleScroll(delay) {
            window.setTimeout(function () {
                window.requestAnimationFrame(applyScroll);
            }, delay);
        }

        applyScroll();
        window.requestAnimationFrame(function () {
            applyScroll();
            window.requestAnimationFrame(applyScroll);
        });

        scheduleScroll(80);
        scheduleScroll(220);

        if (thread) {
            Array.prototype.forEach.call(thread.querySelectorAll("img"), function (image) {
                if (image.complete) {
                    return;
                }

                image.addEventListener("load", applyScroll, { once: true });
                image.addEventListener("error", applyScroll, { once: true });
            });
        }
    }

    function initChatRoomPage(root) {
        var refs = getCommonRefs(root);
        var clubId = Number(root.getAttribute("data-native-page-id"));
        var state = {
            session: null,
            club: null,
            items: [],
            typingUsers: [],
            loading: false,
            error: "",
            authRequired: false,
            accessDenied: false,
            composerText: "",
            sending: false
        };
        var removeRealtimeListener = null;
        var typingTimer = null;
        var typingExpiryTimers = {};

        renderEmptyState(refs, "Chưa có tin nhắn trong phòng chat này.");

        function getMessageId(item) {
            return trimToEmpty(item && item.messageId);
        }

        function upsertMessage(item) {
            if (!item) {
                return false;
            }

            var messageId = getMessageId(item);
            var replaced = false;

            if (messageId) {
                state.items = state.items.map(function (existing) {
                    if (getMessageId(existing) === messageId) {
                        replaced = true;
                        return item;
                    }

                    return existing;
                });

                if (replaced) {
                    return false;
                }
            }

            state.items = state.items.concat([item]).sort(function (a, b) {
                var aDate = parseDate(a && a.sentAt);
                var bDate = parseDate(b && b.sentAt);
                return (aDate ? aDate.getTime() : 0) - (bDate ? bDate.getTime() : 0);
            });

            return true;
        }

        function removeMessage(messageId) {
            var id = trimToEmpty(messageId);
            if (!id) {
                return;
            }

            state.items = state.items.filter(function (item) {
                return getMessageId(item) !== id;
            });
        }

        function clearTypingUser(userId) {
            var id = trimToEmpty(userId);
            if (!id) {
                return;
            }

            window.clearTimeout(typingExpiryTimers[id]);
            delete typingExpiryTimers[id];
            state.typingUsers = state.typingUsers.filter(function (item) {
                return trimToEmpty(item && item.userId) !== id;
            });
        }

        function setTypingUser(event) {
            var userId = trimToEmpty(event && event.userId);
            var myUserId = getSessionUserId(state.session);

            if (!userId || userId === myUserId) {
                return;
            }

            if (!(event && event.isTyping)) {
                clearTypingUser(userId);
                render();
                return;
            }

            var fullName = trimToEmpty(event && event.fullName) || "Thành viên";
            var exists = false;
            state.typingUsers = state.typingUsers.map(function (item) {
                if (trimToEmpty(item && item.userId) === userId) {
                    exists = true;
                    return {
                        userId: userId,
                        fullName: fullName
                    };
                }

                return item;
            });

            if (!exists) {
                state.typingUsers = state.typingUsers.concat([{
                    userId: userId,
                    fullName: fullName
                }]);
            }

            window.clearTimeout(typingExpiryTimers[userId]);
            typingExpiryTimers[userId] = window.setTimeout(function () {
                clearTypingUser(userId);
                render();
            }, 2800);

            render();
        }

        function renderTypingIndicator() {
            if (!state.typingUsers.length) {
                return "";
            }

            var names = state.typingUsers
                .map(function (item) { return trimToEmpty(item && item.fullName); })
                .filter(Boolean)
                .slice(0, 2)
                .join(", ");

            return [
                '<div class="native-chat-typing">',
                '<span class="native-chat-typing__dots"><i></i><i></i><i></i></span>',
                '<span>' + escapeHtml((names || "Thành viên") + " đang nhập...") + "</span>",
                "</div>"
            ].join("");
        }

        function render() {
            refs.list.className = "native-page-list native-page-list--chat-room";

            if (state.authRequired) {
                refs.list.innerHTML = renderAuthPrompt({
                    icon: "chatbubbles-outline",
                    title: "Đăng nhập để vào phòng chat",
                    body: "Bạn cần đăng nhập bằng tài khoản đã tham gia CLB để xem và gửi tin nhắn.",
                    returnUrl: "/PickleballWeb/Chat/" + clubId
                });
            } else if (state.accessDenied) {
                refs.list.innerHTML = renderChatRoomAccessPrompt(clubId);
            } else {
                refs.list.innerHTML = [
                    '<section class="native-chat-room">',
                    state.items.length > 0
                        ? '<div class="native-chat-room__thread">' + state.items.map(function (item) {
                            return renderChatMessage(item, getSessionUserId(state.session));
                        }).join("") + renderTypingIndicator() + '<div data-chat-room-bottom></div></div>'
                        : '<div class="native-chat-room__empty">Chưa có tin nhắn nào trong phòng chat này.</div>',
                    '<form class="native-chat-composer" data-chat-compose-form>',
                    '<textarea class="native-chat-composer__input" rows="1" placeholder="Nhập tin nhắn..." data-chat-compose-input>' + escapeHtml(state.composerText) + '</textarea>',
                    '<button class="native-chat-composer__send" type="submit" data-chat-compose-send ' + ((state.sending || !trimToEmpty(state.composerText)) ? "disabled" : "") + '>',
                    '<ion-icon name="send"></ion-icon>',
                    "</button>",
                    "</form>",
                    "</section>"
                ].join("");

                var form = qs("[data-chat-compose-form]", root);
                var input = qs("[data-chat-compose-input]", root);
                var sendButton = qs("[data-chat-compose-send]", root);

                if (input) {
                    input.value = state.composerText;
                    input.addEventListener("input", function () {
                        state.composerText = input.value;
                        if (sendButton) {
                            sendButton.disabled = state.sending || !trimToEmpty(state.composerText);
                        }

                        sendClubTypingRealtime(clubId, trimToEmpty(state.composerText).length > 0);
                        window.clearTimeout(typingTimer);
                        typingTimer = window.setTimeout(function () {
                            sendClubTypingRealtime(clubId, false);
                        }, 1200);
                    });
                }

                if (form) {
                    form.addEventListener("submit", async function (event) {
                        event.preventDefault();

                        var content = trimToEmpty(state.composerText);
                        if (!content || state.sending) {
                            return;
                        }

                        state.sending = true;
                        render();

                        try {
                            var payload = await requestJson("/api/clubs/" + clubId + "/messages", {
                                method: "POST",
                                body: JSON.stringify({
                                    messageType: "TEXT",
                                    content: content
                                })
                            });
                            var saved = payload && payload.item ? payload.item : null;
                            if (saved) {
                                upsertMessage(saved);
                            }
                            state.composerText = "";
                            sendClubTypingRealtime(clubId, false);
                        } catch (error) {
                            window.alert(error.message || "Không gửi được tin nhắn.");
                        } finally {
                            state.sending = false;
                            render();
                            scrollChatToBottom(root);
                        }
                    });
                }
            }

            toggleCommonState(refs, {
                loading: state.loading,
                itemsLength: state.authRequired || state.accessDenied ? 1 : state.items.length + 1,
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
            state.accessDenied = false;
            render();

            try {
                var session = await requestJson("/api/web-auth/me", {
                    method: "GET",
                    headers: { Accept: "application/json" }
                });

                if (!(session && session.isAuthenticated)) {
                    state.session = null;
                    state.club = null;
                    state.items = [];
                    state.authRequired = true;
                    return;
                }

                state.session = session;

                var clubPromise = requestJson("/api/clubs/" + clubId, {
                    method: "GET",
                    headers: { Accept: "application/json" }
                }).catch(function () { return null; });

                var messagePayload = await requestJson("/api/clubs/" + clubId + "/messages?page=1&pageSize=100", {
                    method: "GET",
                    headers: { Accept: "application/json" }
                });

                state.club = await clubPromise;
                state.items = Array.isArray(messagePayload && messagePayload.items) ? messagePayload.items : [];
                setHeaderTitle(root, trimToEmpty(state.club && state.club.clubName) || "Trò chuyện CLB");
                setHeaderExtra(root, renderChatRoomHeader(state.club));
                connectRealtime();
                subscribeClubRealtime(clubId);
            } catch (error) {
                state.items = [];
                if (error && error.status === 403) {
                    state.accessDenied = true;
                    setHeaderExtra(root, "");
                } else {
                    state.error = "Không tải được phòng chat.";
                }
            } finally {
                state.loading = false;
                render();
                if (!state.authRequired && !state.accessDenied && !state.error) {
                    scrollChatToBottom(root);
                }
            }
        }

        if (refs.retry) {
            refs.retry.onclick = function () { load(); };
        }

        removeRealtimeListener = addRealtimeListener(function (event) {
            var type = trimToEmpty(event && event.type);
            var eventClubId = Number(event && event.clubId);

            if (type === "__socket_open__") {
                if (state.session && !state.authRequired && !state.accessDenied) {
                    subscribeClubRealtime(clubId);
                }
                return;
            }

            if (eventClubId !== clubId) {
                return;
            }

            if (type === "club.message.created") {
                var item = event && event.item ? event.item : null;
                var added = upsertMessage(item);
                clearTypingUser(item && item.senderUserId);
                render();
                if (added) {
                    scrollChatToBottom(root);
                }
                return;
            }

            if (type === "club.message.deleted") {
                removeMessage(event && event.messageId);
                render();
                return;
            }

            if (type === "club.typing") {
                setTypingUser(event);
            }
        });

        window.addEventListener("pagehide", function () {
            window.clearTimeout(typingTimer);
            Object.keys(typingExpiryTimers).forEach(function (key) {
                window.clearTimeout(typingExpiryTimers[key]);
            });
            sendClubTypingRealtime(clubId, false);
            unsubscribeClubRealtime(clubId);
            if (removeRealtimeListener) {
                removeRealtimeListener();
                removeRealtimeListener = null;
            }
        }, { once: true });

        setHeaderTitle(root, "Trò chuyện CLB");
        setHeaderAction(root, null);
        setHeaderExtra(root, "");
        load();
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
                title: "Huấn Luyện Viên",
                memberLabel: "Huấn luyện viên",
                endpoint: "/api/coaches",
                detailHref: function (item) { return "/PickleballWeb/Coach/" + item.coachId; },
                singleValue: function (item) { return item.levelSingle; },
                doubleValue: function (item) { return item.levelDouble; },
                emptyName: "Huấn luyện viên",
                emptyText: "Không có huấn luyện viên nào",
                errorText: "Không tải được danh sách huấn luyện viên.",
                allowAdd: true,
                addMessage: "Vui lòng đăng nhập trong ứng dụng để tạo hoặc cập nhật hồ sơ huấn luyện viên.",
                kind: "coach"
            });
            return;
        }

        if (kind === "referees") {
            initCoachLikePage(root, {
                title: "Trọng Tài",
                memberLabel: "Trọng tài",
                endpoint: "/api/referees",
                detailHref: function (item) { return "/PickleballWeb/Referee/" + item.refereeId; },
                singleValue: function (item) { return item.levelSingle; },
                doubleValue: function (item) { return item.levelDouble; },
                emptyName: "Trọng tài",
                emptyText: "Không có trọng tài nào",
                errorText: "Không tải được danh sách trọng tài.",
                allowAdd: true,
                addMessage: "Vui lòng đăng nhập trong ứng dụng để tạo hoặc cập nhật hồ sơ trọng tài.",
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

        if (kind === "videos") {
            initVideosPage(root);
            return;
        }

        if (kind === "video-player") {
            initVideoPlayerPage(root);
            return;
        }

        if (kind === "chat-list") {
            initUnifiedChatListPage(root);
            return;
        }

        if (kind === "chat-room") {
            initChatRoomPage(root);
            return;
        }

        if (kind === "direct-chat-room") {
            initDirectChatRoomPage(root);
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
        initNotificationCenter();

        var root = qs("[data-native-page-kind]");
        if (root) {
            initNativePage(root);
        }
    });
})();
