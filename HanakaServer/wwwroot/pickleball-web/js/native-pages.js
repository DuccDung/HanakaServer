(function () {
    function qs(selector, root) {
        return (root || document).querySelector(selector);
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

    function canPresentRealtimePairPopup() {
        return document.visibilityState !== "hidden";
    }

    function closePairRequestPopup() {
        if (notificationCenter.popupRoot) {
            notificationCenter.popupRoot.hidden = true;
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
        var tournamentId = Number(item && item.tournamentId);
        var actor = item && item.acceptedBy
            ? item.acceptedBy
            : item && item.requestedTo
                ? item.requestedTo
                : item && item.requestedBy
                    ? item.requestedBy
                    : null;
        var actorName = normalizeDisplayText(actor && actor.fullName) || "Thành viên Hanaka";
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
            '<div class="web-pair-popup__meta-row"><span class="web-pair-popup__meta-label">Người phản hồi</span><span class="web-pair-popup__meta-value">' + escapeHtml(actorName) + "</span></div>",
            '<div class="web-pair-popup__meta-row"><span class="web-pair-popup__meta-label">Giải đấu</span><span class="web-pair-popup__meta-value">' + escapeHtml(tournamentTitle) + "</span></div>",
            '<div class="web-pair-popup__meta-row"><span class="web-pair-popup__meta-label">Thời gian</span><span class="web-pair-popup__meta-value">' + escapeHtml(timeText) + "</span></div>",
            responseNote
                ? '<div class="web-pair-popup__meta-row"><span class="web-pair-popup__meta-label">Ghi chú</span><span class="web-pair-popup__meta-value">' + escapeHtml(responseNote) + "</span></div>"
                : "",
            "</div>",
            '<div class="web-pair-popup__actions">',
            '<a class="is-primary" href="' + escapeHtml(detailHref) + '">' + escapeHtml(detailText) + "</a>",
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
            '<button class="web-pair-popup__close" type="button" aria-label="Dong" data-pair-popup-close>',
            '<ion-icon name="close-outline"></ion-icon>',
            "</button>",
            '<div data-pair-popup-content></div>',
            "</section>"
        ].join("");

        root.addEventListener("click", async function (event) {
            var closeTarget = event.target.closest("[data-pair-popup-close]");
            if (closeTarget) {
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
            tournamentId: Number.isFinite(tournamentId) && tournamentId > 0 ? tournamentId : 0,
            tournamentTitle: normalizeDisplayText(source && (source.tournamentTitle || source.TournamentTitle || source.title || source.Title)),
            registrationId: Number.isFinite(registrationId) && registrationId > 0 ? registrationId : 0,
            responseNote: normalizeDisplayText(source && (source.responseNote || source.ResponseNote)),
            acceptedBy: normalizeUserBrief(source && (source.acceptedBy || source.AcceptedBy)),
            requestedBy: normalizeUserBrief(source && (source.requestedBy || source.RequestedBy)),
            requestedTo: normalizeUserBrief(source && (source.requestedTo || source.RequestedTo)),
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
            throw new Error("Yeu cau ghep doi khong hop le.");
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
                fetchJson("/api/notifications/pair-requests"),
                fetchJson("/api/notifications/upcoming-matches"),
                fetchJson("/api/notifications/inbox")
            ]);

            if (syncToken !== notificationCenter.syncToken) {
                return 0;
            }

            var pairItems = results[0].status === "fulfilled" && Array.isArray(results[0].value && results[0].value.items)
                ? results[0].value.items
                : [];
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
            setNotificationBellCount(pairItems.length + matchItems.length + unreadNonPairTotal);

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

                syncNotificationCenter({ allowPopup: false });
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
                        } else if (notificationType === "PAIR_ACCEPTED" || notificationType === "PAIR_REJECTED") {
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

                syncNotificationCenter({ allowPopup: false });
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
        var gameTypeLabel = tournamentGameTypeLabel(item.gameType, item.genderCategory, item.tournamentTypeLabel);

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

                showAppOnlyAlert("Chức năng tạo câu lạc bộ hiện đang thực hiện trên app.");
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
                filterText.textContent = state.query ? ("Tu khoa: " + state.query) : "Tat ca cau lac bo";
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
        var startAtText = trimToEmpty(item && item.match && item.match.startAtText) || "Chua cap nhat";
        var addressText = trimToEmpty(item && item.match && item.match.addressText) || "Chua cap nhat";
        var courtText = trimToEmpty(item && item.match && item.match.courtText) || "Chua cap nhat";

        return [
            '<article class="native-notification-card">',
            '<h2 class="native-notification-card__title">' + escapeHtml(normalizeDisplayText(item && item.title) || "Hanaka Sport - Thông báo") + "</h2>",
            '<p class="native-notification-card__line">' + escapeHtml(normalizeDisplayText(item && item.message) || "Thông báo sẽ được cập nhật tại đây.") + "</p>",
            '<p class="native-notification-card__line"><strong>Doi thu:</strong> ' + escapeHtml(buildNotificationOpponentText(item)) + "</p>",
            '<p class="native-notification-card__line"><strong>Thoi gian:</strong> ' + escapeHtml(startAtText) + "</p>",
            '<p class="native-notification-card__line"><strong>Dia diem:</strong> ' + escapeHtml(addressText) + "</p>",
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
        var tournamentId = Number(item && item.tournamentId);
        return tournamentId > 0
            ? "/PickleballWeb/Tournament/" + tournamentId + "/Register"
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
        var isUnread = item && (item.isRead === false || item.IsRead === false);
        var notificationId = Number(item && (item.notificationId || item.id));
        var eyebrowText = notificationType === "PAIR_ACCEPTED"
            ? "Chấp nhận lời mời"
            : notificationType === "PAIR_REJECTED"
                ? "Từ chối lời mời"
                : "Thông báo";
        var detailHref = buildNotificationDetailHref(item);
        var detailText = Number(item && item.tournamentId) > 0 ? "Xem đăng ký" : "Mở thông báo";
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
            "<p>Trang này sẽ hiển thị các trận đấu sắp tới của tài khoản giống trong bản app.</p>",
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

        renderEmptyState(refs, "Hiện chưa có thông báo mới.");

        function getRenderedItemsLength() {
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

            if (matchHtml) {
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
                    html: "<span>" + (state.markingAll ? "Đang xử lý..." : "Đọc hết") + "</span>",
                    onClick: function () {
                        markAllNotificationsRead();
                    }
                }
                : null);
            setHeaderExtra(root, buildHeaderSummary());

            if (state.authRequired) {
                refs.list.innerHTML = renderNotificationLoginPrompt();
            } else {
                refs.list.innerHTML = buildNotificationSectionsHtml();
            }

            toggleCommonState(refs, {
                loading: state.loading,
                itemsLength: state.authRequired ? 1 : getRenderedItemsLength(),
                error: state.error,
                hasMore: false
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
            "<strong>" + escapeHtml(trimToEmpty(options && options.title) || "Dang nhap de tiep tuc") + "</strong>",
            "<p>" + escapeHtml(trimToEmpty(options && options.body) || "Vui long dang nhap de xem noi dung nay.") + "</p>",
            '<a class="native-auth-prompt__button" href="' + escapeHtml(loginHref) + '">Dang nhap</a>',
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
            parts.push((team1 || "Doi 1") + " vs " + (team2 || "Doi 2"));
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
        var team1Player1 = trimToEmpty(item && item.team1Player1Name) || trimToEmpty(item && item.team1Name) || "Doi 1";
        var team1Player2 = trimToEmpty(item && item.team1Player2Name);
        var team2Player1 = trimToEmpty(item && item.team2Player1Name) || trimToEmpty(item && item.team2Name) || "Doi 2";
        var team2Player2 = trimToEmpty(item && item.team2Player2Name);
        var team1Class = "native-video-card__team " + (team1Player2 ? "has-two-players" : "has-one-player");
        var team2Class = "native-video-card__team " + (team2Player2 ? "has-two-players" : "has-one-player");

        function renderPlayer(name, avatar) {
            var avatarUrl = normalizeMediaUrl(avatar);

            return [
                '<div class="native-video-card__player">',
                avatarUrl
                    ? '<span class="native-video-card__avatar"><img src="' + escapeHtml(avatarUrl) + '" alt="' + escapeHtml(name || "Player") + '" loading="lazy"></span>'
                    : '<span class="native-video-card__avatar native-video-card__avatar--fallback"><ion-icon name="person-outline"></ion-icon></span>',
                '<span class="native-video-card__player-name" title="' + escapeHtml(name || "Player") + '">' + escapeHtml(name || "Player") + "</span>",
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
            '<span>' + escapeHtml(formatDateTime(item && item.startAt) || "Chua co lich") + "</span>",
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
            '<span>' + (trimToEmpty(item && item.videoUrl) ? "Xem video" : "Chua co video") + "</span>",
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
            '<input type="search" placeholder="Tim video, VDV, bang dau..." autocomplete="off" data-video-query-input>',
            '<ion-icon name="search"></ion-icon>',
            "</label>",
            '<div class="native-tabs native-tabs--video">',
            '<button class="native-tabs__item is-active" type="button" data-video-tab="all">Tat ca</button>',
            '<button class="native-tabs__item" type="button" data-video-tab="suggested">De xuat</button>',
            '<button class="native-tabs__item" type="button" data-video-tab="live">Hom nay</button>',
            "</div>",
            "</div>"
        ].join(""));
        renderEmptyState(refs, "Khong co video tran dau phu hop.");

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
                state.error = "Khong tai duoc danh sach video.";
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
            '<strong>' + escapeHtml(title || "Khong mo duoc video trong web") + "</strong>",
            "<p>Video nay can mo bang trinh duyet ngoai hoac dich vu video goc.</p>",
            videoUrl ? '<a class="native-video-player__external" href="' + escapeHtml(buildSafeHref(videoUrl, "#")) + '" target="_blank" rel="noreferrer">Mo video ben ngoai</a>' : "",
            "</div>",
            "</div>"
        ].join("");
    }

    function renderVideoTeamSummary(team, score, isWinner) {
        var player1 = team && team.player1 ? team.player1 : {};
        var player2 = team && team.player2 ? team.player2 : null;

        function renderPlayer(player) {
            var avatarUrl = normalizeMediaUrl(player && player.avatar);
            var name = trimToEmpty(player && player.name) || "Thanh vien";

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
            '<h3>' + escapeHtml(trimToEmpty(team && team.displayName) || "Doi thi dau") + "</h3>",
            '<span>' + escapeHtml(trimToEmpty(team && team.regCode) || "Dang cap nhat") + "</span>",
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

        renderEmptyState(refs, "Khong tim thay video tran dau.");

        async function load() {
            if (!Number.isFinite(matchId) || matchId <= 0) {
                refs.list.className = "native-page-list native-page-list--video-player";
                refs.list.innerHTML = renderVideoPlayerFallback("Khong tim thay tran dau", "", "");
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
                    html: '<ion-icon name="open-outline"></ion-icon>',
                    onClick: function () {
                        window.open(buildSafeHref(videoUrl, "#"), "_blank", "noopener");
                    }
                } : null);
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
                    match && match.isCompleted ? '<span class="is-completed">Da ket thuc</span>' : '<span class="is-open">Dang dien ra</span>',
                    "</div>",
                    '<div class="native-video-meta__info">',
                    formatDateTime(match && match.startAt) ? '<div><small>Thoi gian</small><strong>' + escapeHtml(formatDateTime(match.startAt)) + "</strong></div>" : "",
                    trimToEmpty(match && match.courtText) ? '<div><small>San</small><strong>' + escapeHtml(match.courtText) + "</strong></div>" : "",
                    trimToEmpty(match && match.addressText) ? '<div><small>Dia diem</small><strong>' + escapeHtml(match.addressText) + "</strong></div>" : "",
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
                refs.list.innerHTML = renderVideoPlayerFallback("Khong tai duoc chi tiet video", "", "");
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
        var previewText = trimToEmpty(item && item.lastMessagePreview) || "Chua co tin nhan";

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

        setHeaderTitle(root, "Tin nhan CLB");
        setHeaderAction(root, null);
        setHeaderExtra(root, [
            '<div class="native-chat-toolbar">',
            '<label class="native-inline-search__box native-inline-search__box--video">',
            '<input type="search" placeholder="Tim ten CLB, khu vuc..." autocomplete="off" data-chat-room-query-input>',
            '<ion-icon name="search"></ion-icon>',
            "</label>",
            '<p class="native-chat-toolbar__note">Chi hien thi cac phong chat CLB ma tai khoan da tham gia.</p>',
            "</div>"
        ].join(""));
        renderEmptyState(refs, "Ban chua co phong chat CLB nao.");

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
                    title: "Dang nhap de vao chat CLB",
                    body: "Chi thanh vien CLB da dang nhap moi xem duoc danh sach phong chat.",
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
                    state.error = "Khong tai duoc danh sach phong chat.";
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
                state.error = "Khong tai duoc danh sach phong chat.";
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

    function renderChatRoomHeader(club) {
        if (!club) {
            return "";
        }

        var coverUrl = normalizeMediaUrl(club && (club.coverUrl || club.clubCoverUrl));
        var clubName = trimToEmpty(club && (club.clubName || club.name)) || "Chat CLB";
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
        var senderName = trimToEmpty(item && item.sender && item.sender.fullName) || "Thanh vien";
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
            mediaUrl ? '<img class="native-chat-message__media" src="' + escapeHtml(mediaUrl) + '" alt="Tin nhan hinh anh" loading="lazy">' : "",
            content ? '<p class="native-chat-message__text">' + renderTextWithBreaks(content) + "</p>" : "",
            !content && !mediaUrl ? '<p class="native-chat-message__text">[Tin nhan]</p>' : "",
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
            "<strong>Ban chua co quyen vao phong chat nay</strong>",
            "<p>He thong chi cho phep thanh vien CLB xem va gui tin nhan trong phong chat.</p>",
            '<a class="native-auth-prompt__button" href="/PickleballWeb/Club/' + escapeHtml(clubId) + '">Mo trang CLB</a>',
            "</article>"
        ].join("");
    }

    function scrollChatToBottom(root) {
        var anchor = qs("[data-chat-room-bottom]", root);
        if (anchor) {
            window.requestAnimationFrame(function () {
                anchor.scrollIntoView({ block: "end" });
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

        renderEmptyState(refs, "Chua co tin nhan trong phong chat nay.");

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

            var fullName = trimToEmpty(event && event.fullName) || "Thanh vien";
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
                '<span>' + escapeHtml((names || "Thanh vien") + " dang nhap...") + "</span>",
                "</div>"
            ].join("");
        }

        function render() {
            refs.list.className = "native-page-list native-page-list--chat-room";

            if (state.authRequired) {
                refs.list.innerHTML = renderAuthPrompt({
                    icon: "chatbubbles-outline",
                    title: "Dang nhap de vao phong chat",
                    body: "Ban can dang nhap bang tai khoan da tham gia CLB de xem va gui tin nhan.",
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
                        : '<div class="native-chat-room__empty">Chua co tin nhan nao trong phong chat nay.</div>',
                    '<form class="native-chat-composer" data-chat-compose-form>',
                    '<textarea class="native-chat-composer__input" rows="1" placeholder="Nhap tin nhan..." data-chat-compose-input>' + escapeHtml(state.composerText) + '</textarea>',
                    '<button class="native-chat-composer__send" type="submit" data-chat-compose-send ' + ((state.sending || !trimToEmpty(state.composerText)) ? "disabled" : "") + '>',
                    state.sending ? "Dang gui" : '<ion-icon name="send"></ion-icon>',
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
                            window.alert(error.message || "Khong gui duoc tin nhan.");
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
                setHeaderTitle(root, trimToEmpty(state.club && state.club.clubName) || "Chat CLB");
                setHeaderExtra(root, renderChatRoomHeader(state.club));
                connectRealtime();
                subscribeClubRealtime(clubId);
            } catch (error) {
                state.items = [];
                if (error && error.status === 403) {
                    state.accessDenied = true;
                    setHeaderExtra(root, "");
                } else {
                    state.error = "Khong tai duoc phong chat.";
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

        setHeaderTitle(root, "Chat CLB");
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

        if (kind === "videos") {
            initVideosPage(root);
            return;
        }

        if (kind === "video-player") {
            initVideoPlayerPage(root);
            return;
        }

        if (kind === "chat-list") {
            initChatListPage(root);
            return;
        }

        if (kind === "chat-room") {
            initChatRoomPage(root);
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
