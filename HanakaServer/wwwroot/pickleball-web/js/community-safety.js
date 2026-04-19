(function () {
    var COMMUNITY_TERMS_KEY = "communityTermsState_v1";
    var COMMUNITY_TERMS_VERSION = "2026-04-09";

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

    async function parseResponsePayload(response) {
        var contentType = response.headers.get("content-type") || "";
        if (contentType.indexOf("application/json") >= 0) {
            return response.json().catch(function () { return null; });
        }

        return response.text().catch(function () { return ""; });
    }

    function responseMessage(payload, fallback) {
        if (!payload) {
            return fallback;
        }

        if (typeof payload === "string" && payload) {
            return payload;
        }

        if (typeof payload.message === "string" && payload.message) {
            return payload.message;
        }

        if (typeof payload.title === "string" && payload.title) {
            return payload.title;
        }

        return fallback;
    }

    async function requestJson(url, options) {
        var response = await fetch(url, Object.assign({
            credentials: "same-origin"
        }, options || {}));
        var payload = await parseResponsePayload(response);

        if (!response.ok) {
            throw new Error(responseMessage(payload, "Yêu cầu không thành công."));
        }

        return payload;
    }

    function readTermsState() {
        try {
            var raw = localStorage.getItem(COMMUNITY_TERMS_KEY);
            if (!raw) {
                return {
                    accepted: false,
                    acceptedAt: null
                };
            }

            var payload = JSON.parse(raw);
            var accepted = !!payload &&
                payload.version === COMMUNITY_TERMS_VERSION &&
                !!(payload.accepted || payload.acceptedAt);

            return {
                accepted: accepted,
                acceptedAt: accepted ? trimToEmpty(payload.acceptedAt) : null
            };
        } catch (error) {
            return {
                accepted: false,
                acceptedAt: null
            };
        }
    }

    function acceptTerms() {
        var payload = {
            version: COMMUNITY_TERMS_VERSION,
            accepted: true,
            acceptedAt: new Date().toISOString(),
            source: "community_safety_web"
        };

        localStorage.setItem(COMMUNITY_TERMS_KEY, JSON.stringify(payload));
        return payload;
    }

    function renderBlockedUser(item) {
        return [
            '<article class="community-blocked-card">',
            "<h4>", escapeHtml(item && item.fullName ? item.fullName : "Người dùng"), "</h4>",
            "<p>Chặn lúc: ", escapeHtml(item && item.blockedAt ? new Date(item.blockedAt).toLocaleString("vi-VN") : "Chưa rõ"), "</p>",
            "<p>Lý do: ", escapeHtml(item && item.reason ? item.reason : "other"), "</p>",
            '<button class="community-unblock-btn" type="button" data-community-unblock="', escapeHtml(item && item.userId ? item.userId : ""), '">Bỏ chặn</button>',
            "</article>"
        ].join("");
    }

    function renderReport(item) {
        var title = item && item.kind === "user" ? "Báo cáo người dùng" : "Báo cáo tin nhắn";
        var pending = item && item.pendingSync
            ? "Đã ghi nhận trên thiết bị"
            : item && item.developerNotified
                ? "Đã gửi moderation"
                : "";

        return [
            '<article class="community-report-card">',
            "<h4>", escapeHtml(title + ": " + (item && item.targetUserName ? item.targetUserName : "Người dùng")), "</h4>",
            "<p>Lý do: ", escapeHtml(item && item.reasonLabel ? item.reasonLabel : (item && item.reason ? item.reason : "other")), "</p>",
            "<p>Tạo lúc: ", escapeHtml(item && item.createdAt ? new Date(item.createdAt).toLocaleString("vi-VN") : "Chưa rõ"), "</p>",
            pending ? '<div class="community-report-tag">' + escapeHtml(pending) + "</div>" : "",
            "</article>"
        ].join("");
    }

    function initCommunitySafetyPage(root) {
        var state = {
            authenticated: false,
            blockedUsers: [],
            reports: []
        };

        var refs = {
            statusBadge: qs("[data-community-status-badge]", root),
            statusText: qs("[data-community-status-text]", root),
            acceptedAt: qs("[data-community-accepted-at]", root),
            acceptButton: qs("[data-community-accept]", root),
            blockedList: qs("[data-community-blocked-list]", root),
            blockedEmpty: qs("[data-community-blocked-empty]", root),
            reportList: qs("[data-community-report-list]", root),
            reportEmpty: qs("[data-community-report-empty]", root)
        };

        function renderTerms() {
            var terms = readTermsState();

            refs.statusBadge.classList.toggle("is-ok", !!terms.accepted);
            refs.statusText.textContent = terms.accepted ? "Đã đồng ý" : "Chưa đồng ý";
            refs.acceptButton.hidden = !!terms.accepted;

            if (terms.acceptedAt) {
                refs.acceptedAt.hidden = false;
                refs.acceptedAt.textContent = "Đồng ý lúc: " + new Date(terms.acceptedAt).toLocaleString("vi-VN");
            } else {
                refs.acceptedAt.hidden = true;
                refs.acceptedAt.textContent = "";
            }
        }

        function renderBlockedUsers() {
            if (!state.authenticated) {
                refs.blockedList.hidden = true;
                refs.blockedList.innerHTML = "";
                refs.blockedEmpty.hidden = false;
                refs.blockedEmpty.innerHTML = "<p>Đăng nhập để xem danh sách chặn của bạn.</p>";
                return;
            }

            if (!state.blockedUsers.length) {
                refs.blockedList.hidden = true;
                refs.blockedList.innerHTML = "";
                refs.blockedEmpty.hidden = false;
                refs.blockedEmpty.innerHTML = "<p>Bạn chưa chặn tài khoản nào.</p>";
                return;
            }

            refs.blockedList.hidden = false;
            refs.blockedList.innerHTML = state.blockedUsers.map(renderBlockedUser).join("");
            refs.blockedEmpty.hidden = true;
        }

        function renderReports() {
            if (!state.authenticated) {
                refs.reportList.hidden = true;
                refs.reportList.innerHTML = "";
                refs.reportEmpty.hidden = false;
                refs.reportEmpty.innerHTML = "<p>Đăng nhập để xem lịch sử báo cáo của bạn.</p>";
                return;
            }

            if (!state.reports.length) {
                refs.reportList.hidden = true;
                refs.reportList.innerHTML = "";
                refs.reportEmpty.hidden = false;
                refs.reportEmpty.innerHTML = "<p>Chưa có báo cáo nào được tạo trên tài khoản này.</p>";
                return;
            }

            refs.reportList.hidden = false;
            refs.reportList.innerHTML = state.reports.map(renderReport).join("");
            refs.reportEmpty.hidden = true;
        }

        async function loadData() {
            try {
                var session = await requestJson("/api/web-auth/me", {
                    method: "GET",
                    headers: {
                        Accept: "application/json"
                    }
                });

                state.authenticated = !!(session && session.isAuthenticated);
            } catch (error) {
                state.authenticated = false;
            }

            if (!state.authenticated) {
                state.blockedUsers = [];
                state.reports = [];
                renderBlockedUsers();
                renderReports();
                return;
            }

            try {
                var results = await Promise.allSettled([
                    requestJson("/api/moderation/blocks", {
                        method: "GET",
                        headers: {
                            Accept: "application/json"
                        }
                    }),
                    requestJson("/api/moderation/reports?limit=10", {
                        method: "GET",
                        headers: {
                            Accept: "application/json"
                        }
                    })
                ]);

                state.blockedUsers = results[0].status === "fulfilled" && Array.isArray(results[0].value.items)
                    ? results[0].value.items
                    : [];
                state.reports = results[1].status === "fulfilled" && Array.isArray(results[1].value.items)
                    ? results[1].value.items
                    : [];
            } catch (error) {
                state.blockedUsers = [];
                state.reports = [];
            }

            renderBlockedUsers();
            renderReports();
        }

        refs.acceptButton.addEventListener("click", function () {
            acceptTerms();
            renderTerms();
            window.alert("Bạn đã đồng ý Điều khoản sử dụng và có thể truy cập khu vực chat cộng đồng.");
        });

        root.addEventListener("click", async function (event) {
            var unblockButton = event.target.closest("[data-community-unblock]");
            if (!unblockButton) {
                return;
            }

            event.preventDefault();

            var userId = trimToEmpty(unblockButton.getAttribute("data-community-unblock"));
            if (!userId) {
                return;
            }

            try {
                await requestJson("/api/moderation/blocks/" + encodeURIComponent(userId), {
                    method: "DELETE",
                    headers: {
                        Accept: "application/json"
                    }
                });

                state.blockedUsers = state.blockedUsers.filter(function (item) {
                    return String(item && item.userId) !== userId;
                });
                renderBlockedUsers();
            } catch (error) {
                window.alert(error.message || "Không thể bỏ chặn người dùng lúc này.");
            }
        });

        renderTerms();
        renderBlockedUsers();
        renderReports();
        loadData();
    }

    document.addEventListener("DOMContentLoaded", function () {
        var root = qs("[data-community-safety-page]");
        if (!root) {
            return;
        }

        initCommunitySafetyPage(root);
    });
})();
