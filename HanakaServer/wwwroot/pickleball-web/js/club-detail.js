(function () {
    const TAB_OVERVIEW = "overview";
    const TAB_MEMBERS = "members";
    const TAB_PENDING = "pending";

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

    function formatDate(value, fallback) {
        const date = parseDate(value);
        if (!date) {
            return fallback || "Chưa cập nhật";
        }

        return new Intl.DateTimeFormat("vi-VN", {
            day: "2-digit",
            month: "2-digit",
            year: "numeric"
        }).format(date);
    }

    function toNumber(value, fallback) {
        const number = Number(value);
        return Number.isFinite(number) ? number : (fallback ?? 0);
    }

    function formatScore(score) {
        const number = Number(score || 0);
        if (!Number.isFinite(number)) {
            return "0";
        }

        return number % 1 === 0
            ? number.toFixed(0)
            : number.toFixed(2).replace(/\.?0+$/, "");
    }

    function formatLevel(value) {
        const number = Number(value);
        return Number.isFinite(number) ? number.toFixed(1) : "0.0";
    }

    function formatMemberRole(role) {
        const normalized = trimToEmpty(role).toUpperCase();
        if (normalized === "OWNER") {
            return "Trưởng nhóm";
        }

        if (normalized === "VICE_OWNER") {
            return "Phó nhóm";
        }

        if (normalized === "MEMBER") {
            return "Thành viên";
        }

        return trimToEmpty(role) || "Thành viên";
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

    async function fetchJson(url, options) {
        const init = {
            method: options?.method || "GET",
            headers: {
                Accept: "application/json",
                ...(options?.headers || {})
            },
            credentials: "same-origin",
            cache: "no-store"
        };

        if (options?.body !== undefined) {
            init.body = typeof options.body === "string"
                ? options.body
                : JSON.stringify(options.body);

            if (!init.headers["Content-Type"]) {
                init.headers["Content-Type"] = "application/json";
            }
        }

        const response = await fetch(url, init);
        const raw = await response.text();
        let payload = null;

        if (raw) {
            try {
                payload = JSON.parse(raw);
            } catch (_error) {
                payload = raw;
            }
        }

        if (!response.ok) {
            const message = typeof payload === "object" && payload
                ? trimToEmpty(payload.message)
                : trimToEmpty(payload);

            const error = new Error(message || `Request failed: ${response.status}`);
            error.status = response.status;
            error.payload = payload;
            throw error;
        }

        return payload;
    }

    function setImageState(image, fallback, src, alt) {
        if (!image || !fallback) {
            return;
        }

        const normalized = normalizeMediaUrl(src);
        image.onload = function () {
            image.hidden = false;
            fallback.hidden = true;
        };
        image.onerror = function () {
            image.hidden = true;
            fallback.hidden = false;
        };

        if (!normalized) {
            image.removeAttribute("src");
            image.hidden = true;
            fallback.hidden = false;
            return;
        }

        image.alt = alt;
        image.hidden = true;
        fallback.hidden = false;
        image.src = normalized;
    }

    function renderStars(container, value) {
        if (!container) {
            return;
        }

        const full = Math.max(0, Math.min(5, Math.round(Number(value || 0))));
        container.innerHTML = [0, 1, 2, 3, 4].map(function (index) {
            const icon = index < full ? "star" : "star-outline";
            return `<ion-icon name="${icon}"></ion-icon>`;
        }).join("");
    }

    function createMemberCard(item, index, mode, canManage, actionLoadingKey) {
        const avatarUrl = normalizeMediaUrl(item.avatarUrl);
        const removeDisabled = actionLoadingKey === `remove-${item.userId}`;
        const approveDisabled = actionLoadingKey === `approve-${item.userId}`;
        const rejectDisabled = actionLoadingKey === `reject-${item.userId}`;
        const canRemove = trimToEmpty(item.memberRole).toUpperCase() !== "OWNER";
        const actions = [];

        if (canManage && mode === TAB_PENDING) {
            actions.push(
                `<button class="is-approve" type="button" data-club-action="approve" data-user-id="${item.userId}" ${approveDisabled ? "disabled" : ""}>${approveDisabled ? "Đang duyệt..." : "Duyệt"}</button>`,
                `<button class="is-reject" type="button" data-club-action="reject" data-user-id="${item.userId}" ${rejectDisabled ? "disabled" : ""}>${rejectDisabled ? "Đang xử lý..." : "Từ chối"}</button>`
            );
        } else if (canManage && mode === TAB_MEMBERS && canRemove) {
            actions.push(
                `<button class="is-reject" type="button" data-club-action="remove" data-user-id="${item.userId}" ${removeDisabled ? "disabled" : ""}>${removeDisabled ? "Đang xóa..." : "Xóa"}</button>`
            );
        }

        return [
            '<article class="club-detail-member-card">',
            `<button class="club-detail-member-card__top" type="button" data-club-member-link="${item.userId}">`,
            '<div class="club-detail-member-card__left">',
            `<span class="club-detail-member-index">${index + 1}</span>`,
            avatarUrl
                ? `<img class="club-detail-member-avatar" src="${escapeHtml(avatarUrl)}" alt="${escapeHtml(item.fullName || "Thành viên")}" loading="lazy">`
                : '<span class="club-detail-member-avatar--fallback"><ion-icon name="person-outline"></ion-icon></span>',
            '<span class="club-detail-member-nameblock">',
            `<strong>${escapeHtml(trimToEmpty(item.fullName) || "Chưa có tên")}</strong>`,
            `<span>${escapeHtml(formatMemberRole(item.memberRole))}</span>`,
            "</span>",
            '<ion-icon class="club-detail-member-arrow" name="chevron-forward"></ion-icon>',
            "</div>",
            "</button>",
            '<div class="club-detail-member-stats">',
            '<div class="club-detail-member-stat">',
            "<small>Điểm đơn</small>",
            `<strong>${escapeHtml(formatScore(item.ratingSingle))}</strong>`,
            "</div>",
            '<div class="club-detail-member-stat">',
            "<small>Điểm đôi</small>",
            `<strong>${escapeHtml(formatScore(item.ratingDouble))}</strong>`,
            "</div>",
            "</div>",
            actions.length > 0
                ? `<div class="club-detail-member-actions">${actions.join("")}</div>`
                : "",
            "</article>"
        ].join("");
    }

    function filterItems(items, query) {
        const normalized = trimToEmpty(query).toLowerCase();
        if (!normalized) {
            return items;
        }

        return items.filter(function (item) {
            const haystack = `${item.fullName || ""} ${item.memberRole || ""}`.toLowerCase();
            return haystack.includes(normalized);
        });
    }

    function setMatchBars(elements, win, draw, loss) {
        const values = [Math.max(win, 0.2), Math.max(draw, 0.2), Math.max(loss, 0.2)];
        const total = values.reduce(function (sum, item) { return sum + item; }, 0) || 1;
        const widths = values.map(function (value) {
            return `${(value / total) * 100}%`;
        });

        elements.winBar.style.width = widths[0];
        elements.drawBar.style.width = widths[1];
        elements.lossBar.style.width = widths[2];
    }

    function updateChallengeUi(state, elements) {
        const allowChallenge = !!state.club?.allowChallenge;
        const badge = elements.challengeBadge;
        const button = elements.challengeButton;

        badge.textContent = allowChallenge ? "Đang bật" : "Đang tắt";
        badge.classList.toggle("is-enabled", allowChallenge);
        button.classList.toggle("is-enabled", allowChallenge);
        button.disabled = state.challengeLoading;
        qsa("[data-club-challenge-option]", elements.modal).forEach(function (buttonElement) {
            const isActive = String(allowChallenge) === buttonElement.getAttribute("data-club-challenge-option");
            buttonElement.classList.toggle("is-active", isActive);
            buttonElement.disabled = state.challengeLoading;
        });
        elements.settingsTrigger.disabled = state.challengeLoading;
    }

    function renderOverview(state, elements) {
        const club = state.club;
        const overview = club?.overview || {};
        const address = trimToEmpty(overview.addressText) || trimToEmpty(club?.areaText) || "Chưa có địa chỉ";
        const description = trimToEmpty(overview.introduction) || `CLB ${trimToEmpty(club?.clubName)} đang hoạt động tại ${address}.`;
        const level = Number(overview.level ?? club?.ratingAvg ?? 1.5);
        const reviewsCount = toNumber(club?.reviewsCount);
        const win = toNumber(club?.matchesWin);
        const draw = toNumber(club?.matchesDraw);
        const loss = toNumber(club?.matchesLoss);

        elements.socialScore.textContent = "-";
        elements.matchesPlayed.textContent = String(toNumber(club?.matchesPlayed));
        elements.winText.textContent = `Thắng ${win}`;
        elements.drawText.textContent = `Hoà ${draw}`;
        elements.lossText.textContent = `Thua ${loss}`;
        elements.level.textContent = `Điểm trình: ${formatLevel(level)}`;
        elements.reviewLabel.textContent = `Đánh giá: ${formatLevel(level)}`;
        elements.reviewCount.textContent = `(${reviewsCount} Đánh giá)`;
        elements.description.textContent = description;
        elements.foundedAt.textContent = formatDate(overview.foundedAt, "01/10/2021");
        elements.address.textContent = address;
        elements.membersCount.textContent = String(toNumber(club?.membersCount));
        elements.pendingCount.textContent = String(toNumber(club?.pendingMembersCount));

        renderStars(elements.stars, Math.min(level, 5));
        setMatchBars(elements, win, draw, loss);

        const canManage = !!club?.canManage;
        elements.challengeCard.hidden = !canManage;
        elements.settingsTrigger.hidden = !canManage;
        elements.headerSpacer.hidden = canManage;
        updateChallengeUi(state, elements);
    }

    function renderMemberList(listElement, emptyElement, loadingElement, items, query, mode, canManage, actionLoadingKey) {
        const filteredItems = filterItems(items, query);
        loadingElement.hidden = true;
        emptyElement.hidden = filteredItems.length !== 0;
        listElement.innerHTML = filteredItems.map(function (item, index) {
            return createMemberCard(item, index, mode, canManage, actionLoadingKey);
        }).join("");
    }

    function setActiveTab(state, elements, tab) {
        state.activeTab = tab;
        qsa("[data-club-tab-button]", elements.root).forEach(function (button) {
            button.classList.toggle("is-active", button.getAttribute("data-club-tab-button") === tab);
        });

        qsa("[data-club-panel]", elements.root).forEach(function (panel) {
            panel.hidden = panel.getAttribute("data-club-panel") !== tab;
        });
    }

    function setLoadingState(elements, isLoading) {
        elements.loading.hidden = !isLoading;
        elements.content.hidden = isLoading;
        elements.error.hidden = true;
    }

    function setErrorState(elements, message) {
        elements.loading.hidden = true;
        elements.content.hidden = true;
        elements.error.hidden = false;
        elements.errorText.textContent = trimToEmpty(message) || "Không tải được chi tiết câu lạc bộ.";
    }

    async function loadOverview(state, elements) {
        setLoadingState(elements, true);

        try {
            const payload = await fetchJson(`/api/clubs/${state.clubId}/overview`);
            state.club = payload;
            elements.headerTitle.textContent = trimToEmpty(payload.clubName) || "Chi tiết CLB";
            document.title = `${trimToEmpty(payload.clubName) || "Chi tiết CLB"} | Hanaka Sport`;

            setImageState(elements.coverImage, elements.coverFallback, payload.coverUrl, payload.clubName || "Ảnh bìa câu lạc bộ");
            setImageState(elements.avatarImage, elements.avatarFallback, payload.owner?.avatarUrl, payload.clubName || "Ảnh đại diện câu lạc bộ");

            renderOverview(state, elements);
            elements.loading.hidden = true;
            elements.error.hidden = true;
            elements.content.hidden = false;
        } catch (error) {
            setErrorState(elements, error.message);
        }
    }

    async function ensureMembersLoaded(state, elements) {
        if (state.membersLoaded || state.membersLoading) {
            return;
        }

        state.membersLoading = true;
        elements.membersLoading.hidden = false;

        try {
            const payload = await fetchJson(`/api/clubs/${state.clubId}/members?page=1&pageSize=100`);
            state.members = Array.isArray(payload?.items) ? payload.items : [];
            state.membersLoaded = true;
            renderMemberList(
                elements.membersList,
                elements.membersEmpty,
                elements.membersLoading,
                state.members,
                state.memberQuery,
                TAB_MEMBERS,
                !!state.club?.canManage,
                state.actionLoadingKey
            );
        } catch (error) {
            window.alert(error.message || "Không tải được danh sách thành viên.");
            elements.membersLoading.hidden = true;
        } finally {
            state.membersLoading = false;
        }
    }

    async function ensurePendingLoaded(state, elements) {
        if (!state.club?.canManage) {
            elements.pendingLocked.hidden = false;
            elements.pendingLoading.hidden = true;
            elements.pendingList.innerHTML = "";
            elements.pendingEmpty.hidden = true;
            elements.pendingSearchShell.hidden = true;
            elements.pendingHead.hidden = true;
            return;
        }

        elements.pendingLocked.hidden = true;
        elements.pendingSearchShell.hidden = false;
        elements.pendingHead.hidden = false;
        if (state.pendingLoaded || state.pendingLoading) {
            return;
        }

        state.pendingLoading = true;
        elements.pendingLoading.hidden = false;

        try {
            const payload = await fetchJson(`/api/clubs/${state.clubId}/pending-members?page=1&pageSize=100`);
            state.pendingMembers = Array.isArray(payload?.items) ? payload.items : [];
            state.pendingLoaded = true;
            renderMemberList(
                elements.pendingList,
                elements.pendingEmpty,
                elements.pendingLoading,
                state.pendingMembers,
                state.pendingQuery,
                TAB_PENDING,
                !!state.club?.canManage,
                state.actionLoadingKey
            );
        } catch (error) {
            if (error.status === 403) {
                state.pendingMembers = [];
                state.pendingLoaded = true;
                elements.pendingLocked.hidden = false;
                elements.pendingLoading.hidden = true;
                elements.pendingList.innerHTML = "";
                elements.pendingEmpty.hidden = true;
                elements.pendingSearchShell.hidden = true;
                elements.pendingHead.hidden = true;
                return;
            }

            window.alert(error.message || "Không tải được danh sách chờ duyệt.");
            elements.pendingLoading.hidden = true;
        } finally {
            state.pendingLoading = false;
        }
    }

    function rerenderMembers(state, elements) {
        renderMemberList(
            elements.membersList,
            elements.membersEmpty,
            elements.membersLoading,
            state.members,
            state.memberQuery,
            TAB_MEMBERS,
            !!state.club?.canManage,
            state.actionLoadingKey
        );
    }

    function rerenderPending(state, elements) {
        if (!state.club?.canManage) {
            elements.pendingLocked.hidden = false;
            elements.pendingList.innerHTML = "";
            elements.pendingEmpty.hidden = true;
            elements.pendingLoading.hidden = true;
            elements.pendingSearchShell.hidden = true;
            elements.pendingHead.hidden = true;
            return;
        }

        elements.pendingLocked.hidden = true;
        elements.pendingSearchShell.hidden = false;
        elements.pendingHead.hidden = false;
        renderMemberList(
            elements.pendingList,
            elements.pendingEmpty,
            elements.pendingLoading,
            state.pendingMembers,
            state.pendingQuery,
            TAB_PENDING,
            !!state.club?.canManage,
            state.actionLoadingKey
        );
    }

    async function handleMemberAction(state, elements, action, userId) {
        const currentItem = action === "remove"
            ? state.members.find(function (item) { return Number(item.userId) === userId; })
            : state.pendingMembers.find(function (item) { return Number(item.userId) === userId; });

        if (!currentItem) {
            return;
        }

        const actionKey = `${action}-${userId}`;
        state.actionLoadingKey = actionKey;
        rerenderMembers(state, elements);
        rerenderPending(state, elements);

        try {
            if (action === "approve") {
                const response = await fetchJson(`/api/clubs/${state.clubId}/pending-members/${userId}/approve`, { method: "POST" });
                state.pendingMembers = state.pendingMembers.filter(function (item) { return Number(item.userId) !== userId; });
                state.members = state.members.concat([{ ...currentItem, memberRole: currentItem.memberRole || "MEMBER" }]);
                state.club.membersCount = toNumber(state.club.membersCount) + 1;
                state.club.pendingMembersCount = Math.max(toNumber(state.club.pendingMembersCount) - 1, 0);
                renderOverview(state, elements);
                window.alert(trimToEmpty(response?.message) || "Duyệt thành viên thành công.");
            } else if (action === "reject") {
                const response = await fetchJson(`/api/clubs/${state.clubId}/pending-members/${userId}`, { method: "DELETE" });
                state.pendingMembers = state.pendingMembers.filter(function (item) { return Number(item.userId) !== userId; });
                state.club.pendingMembersCount = Math.max(toNumber(state.club.pendingMembersCount) - 1, 0);
                renderOverview(state, elements);
                window.alert(trimToEmpty(response?.message) || "Đã từ chối yêu cầu.");
            } else if (action === "remove") {
                const response = await fetchJson(`/api/clubs/${state.clubId}/members/${userId}`, { method: "DELETE" });
                state.members = state.members.filter(function (item) { return Number(item.userId) !== userId; });
                state.club.membersCount = Math.max(toNumber(state.club.membersCount) - 1, 0);
                renderOverview(state, elements);
                window.alert(trimToEmpty(response?.message) || "Đã xóa thành viên.");
            }
        } catch (error) {
            window.alert(error.message || "Không thể cập nhật thành viên.");
        } finally {
            state.actionLoadingKey = "";
            rerenderMembers(state, elements);
            rerenderPending(state, elements);
        }
    }

    async function updateChallengeMode(state, elements, nextValue) {
        if (state.challengeLoading) {
            return;
        }

        state.challengeLoading = true;
        updateChallengeUi(state, elements);

        try {
            const response = await fetchJson(`/api/clubs/${state.clubId}/challenge-mode`, {
                method: "PUT",
                body: { allowChallenge: nextValue }
            });

            state.club.allowChallenge = typeof response?.allowChallenge === "boolean"
                ? response.allowChallenge
                : nextValue;
            updateChallengeUi(state, elements);
            elements.modal.hidden = true;
            window.alert(trimToEmpty(response?.message) || "Đã cập nhật chế độ khiêu chiến.");
        } catch (error) {
            window.alert(error.message || "Không thể cập nhật chế độ khiêu chiến.");
        } finally {
            state.challengeLoading = false;
            updateChallengeUi(state, elements);
        }
    }

    function bindEvents(state, elements) {
        qsa("[data-club-tab-button]", elements.root).forEach(function (button) {
            button.addEventListener("click", async function () {
                const tab = button.getAttribute("data-club-tab-button");
                setActiveTab(state, elements, tab);

                if (tab === TAB_MEMBERS) {
                    await ensureMembersLoaded(state, elements);
                }

                if (tab === TAB_PENDING) {
                    await ensurePendingLoaded(state, elements);
                }
            });
        });

        elements.retry.addEventListener("click", function () {
            loadOverview(state, elements);
        });

        elements.membersSearch.addEventListener("input", function (event) {
            state.memberQuery = event.target.value || "";
            rerenderMembers(state, elements);
        });

        elements.pendingSearch.addEventListener("input", function (event) {
            state.pendingQuery = event.target.value || "";
            rerenderPending(state, elements);
        });

        [elements.settingsTrigger, elements.challengeOpen].forEach(function (button) {
            button.addEventListener("click", function () {
                if (!state.club?.canManage || state.challengeLoading) {
                    return;
                }

                elements.modal.hidden = false;
                updateChallengeUi(state, elements);
            });
        });

        qsa("[data-club-modal-close]", elements.root).forEach(function (button) {
            button.addEventListener("click", function () {
                elements.modal.hidden = true;
            });
        });

        qsa("[data-club-challenge-option]", elements.modal).forEach(function (button) {
            button.addEventListener("click", function () {
                updateChallengeMode(state, elements, button.getAttribute("data-club-challenge-option") === "true");
            });
        });

        [elements.membersList, elements.pendingList].forEach(function (listElement) {
            listElement.addEventListener("click", function (event) {
                const actionButton = event.target.closest("[data-club-action]");
                if (actionButton) {
                    handleMemberAction(
                        state,
                        elements,
                        actionButton.getAttribute("data-club-action"),
                        Number(actionButton.getAttribute("data-user-id"))
                    );
                    return;
                }

                const memberButton = event.target.closest("[data-club-member-link]");
                if (memberButton) {
                    const userId = Number(memberButton.getAttribute("data-club-member-link"));
                    if (Number.isFinite(userId) && userId > 0) {
                        window.location.href = `/PickleballWeb/Member/${userId}`;
                    }
                }
            });
        });
    }

    function collectElements(root) {
        return {
            root,
            headerTitle: qs("[data-club-header-title]", root),
            settingsTrigger: qs("[data-club-settings-trigger]", root),
            headerSpacer: qs("[data-club-header-spacer]", root),
            loading: qs("[data-club-loading]", root),
            content: qs("[data-club-content]", root),
            error: qs("[data-club-error]", root),
            errorText: qs("[data-club-error-text]", root),
            retry: qs("[data-club-retry]", root),
            coverImage: qs("[data-club-cover-image]", root),
            coverFallback: qs("[data-club-cover-fallback]", root),
            avatarImage: qs("[data-club-avatar-image]", root),
            avatarFallback: qs("[data-club-avatar-fallback]", root),
            socialScore: qs("[data-club-social-score]", root),
            matchesPlayed: qs("[data-club-matches-played]", root),
            winText: qs("[data-club-win-text]", root),
            drawText: qs("[data-club-draw-text]", root),
            lossText: qs("[data-club-loss-text]", root),
            winBar: qs("[data-club-win-bar]", root),
            drawBar: qs("[data-club-draw-bar]", root),
            lossBar: qs("[data-club-loss-bar]", root),
            level: qs("[data-club-level]", root),
            reviewLabel: qs("[data-club-review-label]", root),
            reviewCount: qs("[data-club-review-count]", root),
            stars: qs("[data-club-stars]", root),
            description: qs("[data-club-description]", root),
            foundedAt: qs("[data-club-founded-at]", root),
            address: qs("[data-club-address]", root),
            membersCount: qs("[data-club-members-count]", root),
            pendingCount: qs("[data-club-pending-count]", root),
            challengeCard: qs("[data-club-challenge-card]", root),
            challengeBadge: qs("[data-club-challenge-badge]", root),
            challengeButton: qs("[data-club-challenge-open]", root),
            challengeOpen: qs("[data-club-challenge-open]", root),
            membersSearch: qs("[data-club-members-search]", root),
            membersLoading: qs("[data-club-members-loading]", root),
            membersList: qs("[data-club-members-list]", root),
            membersEmpty: qs("[data-club-members-empty]", root),
            pendingSearch: qs("[data-club-pending-search]", root),
            pendingSearchShell: qs("[data-club-pending-search-shell]", root),
            pendingHead: qs("[data-club-pending-head]", root),
            pendingLocked: qs("[data-club-pending-locked]", root),
            pendingLoading: qs("[data-club-pending-loading]", root),
            pendingList: qs("[data-club-pending-list]", root),
            pendingEmpty: qs("[data-club-pending-empty]", root),
            modal: qs("[data-club-modal]", root)
        };
    }

    function initClubDetailPage(root) {
        const clubId = Number(root.getAttribute("data-club-id"));
        if (!Number.isFinite(clubId) || clubId <= 0) {
            return;
        }

        const elements = collectElements(root);
        const state = {
            clubId,
            club: null,
            activeTab: TAB_OVERVIEW,
            members: [],
            membersLoaded: false,
            membersLoading: false,
            pendingMembers: [],
            pendingLoaded: false,
            pendingLoading: false,
            memberQuery: "",
            pendingQuery: "",
            actionLoadingKey: "",
            challengeLoading: false
        };

        bindEvents(state, elements);
        setActiveTab(state, elements, TAB_OVERVIEW);
        loadOverview(state, elements);
    }

    document.addEventListener("DOMContentLoaded", function () {
        qsa("[data-club-detail-page]").forEach(initClubDetailPage);
    });
})();
