(function () {
    "use strict";

    const root = document.getElementById("tournamentBracketSetup");
    if (!root) return;
    const tournamentId = Number(root.dataset.tournamentId);
    const state = {
        locked: root.dataset.registrationLocked === "true", active: null, templates: [], eligible: [],
        originalOrder: [], seedOrder: [], selectedVersionId: null, preview: null, randomSeed: null,
        placementMethod: "REGISTRATION_ORDER", pendingPlacementMethod: null,
        currentStep: 1, maxStep: 1, busy: false, seedQuery: "", applyReferee: null
    };

    function esc(value) {
        return String(value ?? "").replace(/[&<>'"]/g, (char) => ({
            "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;"
        })[char]);
    }

    async function api(url, options) {
        const response = await fetch(url, {
            credentials: "same-origin",
            headers: { "Content-Type": "application/json", ...(options?.headers || {}) },
            ...options
        });
        const payload = await response.json().catch(() => ({}));
        if (!response.ok) {
            const error = new Error(payload.message || "Không thể thực hiện yêu cầu.");
            error.code = payload.code || `HTTP_${response.status}`;
            throw error;
        }
        return payload;
    }

    function formatDate(value) {
        if (!value) return "—";
        return new Intl.DateTimeFormat("vi-VN", { dateStyle: "short", timeStyle: "short" }).format(new Date(value));
    }

    function methodLabel(value) {
        return ({ REGISTRATION_ORDER: "Xếp lần lượt", RANDOM: "Xếp random", MANUAL: "Admin sắp xếp", RANKING: "Theo trình" })[value] || value;
    }

    function uiText(value) {
        return String(value ?? "")
            .replace(/reseed/gi, "xếp lại")
            .replace(/seed\s+(\d+)/gi, "Đội đấu $1")
            .replace(/\bseed\b/gi, "đội đấu");
    }

    function formatLabel(value) {
        return ({ SINGLE_ELIMINATION: "Loại trực tiếp", GROUP_KNOCKOUT: "Vòng bảng + knockout", CUSTOM: "Tùy chỉnh" })[value] || value;
    }

    function showMessage(type, message) {
        const error = root.querySelector("[data-setup-error]");
        const success = root.querySelector("[data-setup-success]");
        error.classList.add("d-none"); success.classList.add("d-none");
        if (!message) return;
        const target = type === "error" ? error : success;
        target.textContent = uiText(message); target.classList.remove("d-none");
        target.scrollIntoView({ behavior: "smooth", block: "nearest" });
        if (type === "success") setTimeout(() => target.classList.add("d-none"), 4500);
    }

    function operationErrorMessage(action, error) {
        const step = action === "confirm-placement" || action === "open-placement-method"
            ? "Bước 3 · xếp đội đấu"
            : action === "apply"
                ? "Bước 4 · kiểm tra và ghi bracket"
                : action === "toggle-lock"
                    ? "Bước 1 · khóa đăng ký"
                    : "Thao tác bracket";
        return uiText(`${step}${error?.code ? ` [${error.code}]` : ""}: ${error?.message || "Không thể thực hiện yêu cầu."}`);
    }

    function setBusy(value, initial = false) {
        state.busy = value;
        root.querySelector("[data-setup-loading]").classList.toggle("d-none", !initial || !value);
        root.querySelectorAll("button").forEach((button) => {
            if (!button.closest(".modal")) button.disabled = value || (button.dataset.action === "apply" && !document.getElementById("confirmBracketApply")?.checked);
        });
    }

    function updateLockUi() {
        const pill = root.querySelector("[data-lock-status]");
        pill.innerHTML = state.locked ? '<i class="fas fa-lock"></i>Đã khóa đăng ký' : '<i class="fas fa-lock-open"></i>Chưa khóa đăng ký';
        const card = root.querySelector(".tbs-lock-card");
        card.classList.toggle("is-locked", state.locked);
        root.querySelector("[data-lock-title]").textContent = state.locked ? "Danh sách đăng ký đã khóa" : "Khóa danh sách đăng ký";
        root.querySelector("[data-lock-description]").textContent = state.locked
            ? "Snapshot đội đã sẵn sàng để tạo preview và áp dụng bracket."
            : "User vẫn có thể tạo đăng ký hoặc ghép cặp; cần khóa trước khi apply.";
        const button = root.querySelector("[data-action='toggle-lock']");
        button.className = state.locked ? "btn btn-outline-secondary" : "btn btn-primary";
        button.innerHTML = state.locked ? '<i class="fas fa-lock-open mr-2"></i>Mở đăng ký' : '<i class="fas fa-lock mr-2"></i>Khóa đăng ký';
    }

    function renderEligible() {
        root.querySelector("[data-eligible-count]").textContent = state.eligible.length;
        root.querySelector("[data-eligible-list]").innerHTML = state.eligible.length
            ? state.eligible.map((team, index) => `<div class="tbs-team-row">
                <span class="tbs-team-row__number">${index + 1}</span>
                <div><strong>${esc(team.teamName)}</strong><small>${esc(team.regCode || `#${team.registrationId}`)} · ${esc([team.player1Name, team.player2Name].filter(Boolean).join(" & "))}</small><small>Trình ${team.player1Level || 0}${team.player2Name ? ` + ${team.player2Level || 0}` : ""} · ${team.points || 0} điểm xếp hạng</small></div>
                <span class="tbs-paid">${team.paid ? "Đã thanh toán" : "Miễn phí"}</span><span class="tbs-reg-time">${esc(formatDate(team.registeredAt))}</span>
            </div>`).join("")
            : '<div class="text-center text-muted p-4">Chưa có đăng ký đủ điều kiện.</div>';
    }

    function renderTemplates() {
        root.querySelector("[data-template-grid]").innerHTML = state.templates.length
            ? state.templates.map((item) => {
                const bye = Math.max(0, (item.seedCapacity || 0) - (item.eligibleTeamCount || 0));
                return `<label class="tbs-template-card ${item.isApplicable ? "" : "is-disabled"}">
                    <input type="radio" name="bracketTemplate" value="${item.currentPublishedVersionId}" ${item.isApplicable ? "" : "disabled"} ${Number(item.currentPublishedVersionId) === state.selectedVersionId ? "checked" : ""} />
                    <div class="tbs-template-card__head"><code>${esc(item.templateCode)}</code><h3>${esc(item.templateName)}</h3></div>
                    <div class="tbs-template-card__body"><p>${esc(item.description || "Không có mô tả.")}</p>
                        <div class="tbs-template-stats"><span><strong>${item.minimumTeams}–${item.seedCapacity}</strong>đội</span><span><strong>${bye}</strong>BYE dự kiến</span><span><strong>${item.matchCount}</strong>trận</span></div>
                        <div class="tbs-template-card__foot"><span>${esc(formatLabel(item.formatType))}</span><span>v${item.currentVersionNumber} · ${item.roundCount} vòng · ${item.groupCount} bảng</span></div>
                        ${item.isApplicable ? "" : `<span class="tbs-incompatible"><i class="fas fa-exclamation-triangle mr-1"></i>${esc(item.inapplicableReason)}</span>`}
                    </div>
                </label>`;
            }).join("")
            : '<div class="text-center text-muted p-5">Chưa có template published.</div>';
    }

    function orderedTeams() {
        const byId = new Map(state.eligible.map((item) => [item.registrationId, item]));
        return state.seedOrder.map((id) => id > 0
            ? byId.get(id)
            : { registrationId: id, isBye: true, teamName: "BYE · Miễn đấu" }).filter(Boolean);
    }

    function resetSeedOrder() {
        state.seedOrder = [];
    }

    function selectTemplateDefaultSeeding() {
        const template = state.templates.find((item) => Number(item.currentPublishedVersionId) === state.selectedVersionId);
        state.placementMethod = template?.defaultSeedingMethod === "RANDOM" ? "RANDOM" : "REGISTRATION_ORDER";
        state.pendingPlacementMethod = null;
        state.randomSeed = null;
    }

    function renderSeeds() {
        root.querySelector("[data-placement-method-label]").textContent = state.preview
            ? methodLabel(state.preview.seedingMethod)
            : "Chưa xếp đội";
        root.querySelector("[data-placement-summary]").textContent = state.preview
            ? `${state.preview.eligibleRegistrationCount} đội · ${state.preview.byeCount} BYE`
            : "0 đội đấu";
        if (!state.preview) {
            root.querySelector("[data-seed-list]").innerHTML = '<div class="text-center text-muted p-4">Hãy chọn cách xếp và xác nhận để phân bổ đội đấu.</div>';
            return;
        }
        const query = state.seedQuery.trim().toLocaleLowerCase("vi");
        const rows = orderedTeams()
            .map((team, index) => ({ team, placementNumber: index + 1 }))
            .filter(({ team }) => !query || [team.teamName, team.regCode, team.player1Name, team.player2Name, team.isBye ? "BYE miễn đấu" : ""]
                .filter(Boolean).join(" ").toLocaleLowerCase("vi").includes(query));
        root.querySelector("[data-seed-list]").innerHTML = rows.length ? rows.map(({ team, placementNumber }) => `<div class="tbs-seed-row ${team.isBye ? "is-bye" : ""}">
            <span class="tbs-seed-row__number" title="Đội đấu ${placementNumber}">${placementNumber}</span>
            <div><strong>${esc(team.teamName)}</strong><small>${team.isBye ? "Suất trống tự động đi tiếp" : `${esc(team.regCode || "")} · ${esc([team.player1Name, team.player2Name].filter(Boolean).join(" & "))} · Trình ${team.player1Level || 0}${team.player2Name ? ` + ${team.player2Level || 0}` : ""}`}</small></div>
            <span class="tbs-paid">${team.isBye ? "BYE" : team.paid ? "Đã thanh toán" : "Hợp lệ"}</span>
        </div>`).join("") : '<div class="text-center text-muted p-4">Không tìm thấy đội phù hợp.</div>';
    }

    function renderActive() {
        const activeCard = root.querySelector("[data-active-application]");
        const wizard = root.querySelector("[data-setup-wizard]");
        const pill = root.querySelector("[data-application-status]");
        if (!state.active) {
            activeCard.classList.add("d-none"); wizard.classList.remove("d-none");
            pill.classList.remove("is-applied"); pill.innerHTML = "<i></i>Chưa áp dụng";
            return;
        }
        activeCard.classList.remove("d-none"); wizard.classList.add("d-none");
        pill.classList.add("is-applied"); pill.innerHTML = "<i></i>Đã áp dụng";
        root.querySelector("[data-active-title]").textContent = `${state.active.templateName} · v${state.active.versionNumber}`;
        root.querySelector("[data-active-meta]").textContent = `${state.active.templateCode} · ${methodLabel(state.active.seedingMethod)} · ${state.active.appliedByName || "Admin"} · áp dụng ${formatDate(state.active.appliedAt)}`;
        const snapshotRegistrationIds = new Set((state.active.seeds || []).filter((seed) => seed.registrationId).map((seed) => seed.registrationId));
        const newRegistrations = state.eligible.filter((team) => !snapshotRegistrationIds.has(team.registrationId));
        const newRegistrationNotice = root.querySelector("[data-active-new-registrations]");
        newRegistrationNotice.classList.toggle("d-none", newRegistrations.length === 0);
        newRegistrationNotice.textContent = newRegistrations.length
            ? `${newRegistrations.length} đăng ký mới chưa nằm trong bracket: ${newRegistrations.map((team) => team.regCode || `#${team.registrationId}`).join(", ")}. Cần reset và xếp lại trước khi thi đấu nếu muốn bổ sung.`
            : "";
        root.querySelector("[data-active-stats]").innerHTML = `<span>${state.active.eligibleRegistrationCount} đội</span><span>${state.active.byeCount} BYE</span><span>${state.active.generatedRoundCount} vòng</span><span>${state.active.generatedGroupCount} bảng</span><span>${state.active.generatedMatchCount} trận</span>`;
    }

    function renderHistory(items) {
        root.querySelector("[data-history-rows]").innerHTML = items.length ? items.map((item) => `<tr>
            <td><strong>#${item.tournamentBracketApplicationId}</strong><small>${item.isActive ? "Đang hoạt động" : "Đã đóng"}</small></td>
            <td><strong>${esc(item.templateName)}</strong><small>${esc(item.templateCode)} · v${item.versionNumber}</small></td>
            <td><strong>${esc(methodLabel(item.seedingMethod))}</strong><small>${item.eligibleRegistrationCount} đội · ${item.byeCount} BYE</small></td>
            <td><span class="tbs-history-status is-${String(item.status).toLowerCase()}">${esc(item.status)}</span>${item.revertReason ? `<small>${esc(item.revertReason)}</small>` : ""}</td>
            <td>${esc(formatDate(item.appliedAt || item.createdAt))}</td>
        </tr>`).join("") : '<tr><td colspan="5" class="text-center text-muted p-4">Chưa có lịch sử áp dụng.</td></tr>';
    }

    async function load() {
        setBusy(true, true); showMessage();
        try {
            const [templates, eligible, application, history] = await Promise.all([
                api(`/api/admin/tournaments/${tournamentId}/bracket/templates`),
                api(`/api/admin/tournaments/${tournamentId}/bracket/eligible-registrations`),
                api(`/api/admin/tournaments/${tournamentId}/bracket/application`),
                api(`/api/admin/tournaments/${tournamentId}/bracket/application-history`)
            ]);
            state.templates = templates.items || [];
            state.eligible = eligible.data || [];
            state.originalOrder = state.eligible.map((item) => item.registrationId);
            state.active = application.item;
            const firstApplicable = state.templates.find((item) => item.isApplicable);
            state.selectedVersionId = firstApplicable ? Number(firstApplicable.currentPublishedVersionId) : null;
            resetSeedOrder();
            selectTemplateDefaultSeeding();
            updateLockUi(); renderEligible(); renderTemplates(); renderSeeds(); renderActive(); renderHistory(history.items || []);
        } catch (error) { showMessage("error", error.message); }
        finally { setBusy(false, false); }
    }

    function goStep(step) {
        if (step === 2 && state.eligible.length === 0) throw new Error("Chưa có đội đủ điều kiện để tạo bracket.");
        if (step >= 3 && !state.selectedVersionId) throw new Error("Vui lòng chọn một template phù hợp.");
        if (step >= 3 && !state.preview) throw new Error("Vui lòng xếp đội đấu và xác nhận phương pháp trước khi tiếp tục.");
        state.currentStep = step; state.maxStep = Math.max(state.maxStep, step);
        root.querySelectorAll("[data-step]").forEach((panel) => panel.classList.toggle("d-none", Number(panel.dataset.step) !== step));
        root.querySelectorAll("[data-step-nav]").forEach((button) => {
            const number = Number(button.dataset.stepNav);
            button.classList.toggle("is-active", number === step); button.classList.toggle("is-done", number < step);
        });
        [...root.querySelectorAll(".tbs-steps > i")].forEach((line, index) => line.classList.toggle("is-done", index + 1 < step));
        root.querySelector(".tbs-wizard").scrollIntoView({ behavior: "smooth", block: "start" });
    }

    function selectedTemplate() {
        return state.templates.find((item) => Number(item.currentPublishedVersionId) === state.selectedVersionId) || null;
    }

    function openPlacementMethodModal() {
        const template = selectedTemplate();
        if (!template) throw new Error("Vui lòng chọn một template phù hợp.");
        if (!template.isApplicable) throw new Error(template.inapplicableReason || "Template không phù hợp với số đội hiện tại.");
        if (!template.roundCount || !template.matchCount) throw new Error("Template chưa có vòng và trận để phân bổ đội đấu.");
        document.querySelectorAll("#teamPlacementMethodForm input[name='placementMethod']")
            .forEach((input) => { input.checked = false; });
        window.jQuery("#teamPlacementMethodModal").modal("show");
    }

    function populatePlacementConfirmation() {
        const template = selectedTemplate();
        if (!template || !state.pendingPlacementMethod) throw new Error("Thông tin xếp đội đấu không còn hợp lệ.");
        const byeCount = Math.max(0, Number(template.seedCapacity || 0) - state.eligible.length);
        document.querySelector("[data-placement-confirm-template]").textContent = `${template.templateName} · v${template.currentVersionNumber}`;
        document.querySelector("[data-placement-confirm-team-count]").textContent = state.eligible.length;
        document.querySelector("[data-placement-confirm-capacity]").textContent = template.seedCapacity;
        document.querySelector("[data-placement-confirm-bye]").textContent = byeCount;
        document.querySelector("[data-placement-confirm-method]").textContent = methodLabel(state.pendingPlacementMethod);
        document.querySelector("[data-placement-confirm-note]").textContent = state.pendingPlacementMethod === "RANDOM"
            ? "Chỉ sau khi xác nhận, hệ thống mới xáo đội. Mã random sẽ được giữ nguyên cho bản xem trước và lúc áp dụng."
            : "Chỉ sau khi xác nhận, hệ thống mới gắn đội theo đúng thứ tự danh sách đăng ký hợp lệ.";
    }

    function switchModal(fromSelector, toSelector) {
        const from = window.jQuery(fromSelector);
        from.one("hidden.bs.modal", () => window.jQuery(toSelector).modal("show"));
        from.modal("hide");
    }

    function syncPlacementOrderFromPreview() {
        let byeIndex = 0;
        state.seedOrder = [...(state.preview?.seeds || [])]
            .sort((first, second) => first.seedNumber - second.seedNumber)
            .map((item) => item.registrationId || -(++byeIndex));
    }

    async function createPlacementPreview(method) {
        if (!state.selectedVersionId) throw new Error("Vui lòng chọn template.");
        state.randomSeed = null;
        const body = {
            bracketTemplateVersionId: state.selectedVersionId,
            seedingMethod: method,
            randomSeed: null,
            seedAssignments: []
        };
        const result = await api(`/api/admin/tournaments/${tournamentId}/bracket/preview`, { method: "POST", body: JSON.stringify(body) });
        state.preview = result.data;
        state.placementMethod = method;
        state.randomSeed = state.preview.randomSeed;
        syncPlacementOrderFromPreview();
        renderSeeds();
        goStep(3);
        showMessage("success", `Đã phân bổ ${state.preview.eligibleRegistrationCount} đội theo phương pháp ${methodLabel(method)}.`);
    }

    function renderPreview() {
        const preview = state.preview;
        root.querySelector("[data-preview-hash]").textContent = preview.previewHash;
        const stats = [
            [preview.eligibleRegistrationCount, "Đội"], [preview.byeCount, "BYE"], [preview.roundCount, "Vòng"],
            [preview.groupCount, "Bảng / nhánh"], [preview.matchCount, "Trận"], [preview.matchCount, "Dùng cấu hình chung"]
        ];
        root.querySelector("[data-preview-summary]").innerHTML = stats.map(([value, label]) => `<div class="tbs-preview-stat"><strong>${value}</strong><span>${label}</span></div>`).join("");
        const issues = (preview.validation?.issues || []).filter((item) => item.severity !== "INFO");
        const validation = root.querySelector("[data-preview-validation]");
        validation.classList.toggle("d-none", issues.length === 0);
        validation.innerHTML = issues.map((item) => `<div><strong>${esc(item.severity)}:</strong> ${esc(uiText(item.message))}</div>`).join("");
        const hasErrors = (preview.validation?.errorCount || 0) > 0;
        root.querySelector("[data-preview-board]").innerHTML = preview.rounds.map((round) => `<section class="tbs-preview-round">
            <h3>${esc(round.roundLabel)}</h3>${round.groups.map((group) => `<div class="tbs-preview-group"><strong>${esc(group.groupName)}</strong>
                ${group.matches.map((match) => `<div class="tbs-preview-match"><strong>${esc(match.matchLabel || match.matchKey)}</strong>${match.isTerminal ? `<small>${esc(match.terminalType === "CHAMPION" ? "Vô địch" : match.terminalType === "THIRD_PLACE" ? "Hạng ba" : match.terminalType || "Terminal")}</small>` : ""}${match.slots.map((slot) => `<div class="tbs-preview-slot ${slot.isBye ? "is-bye" : ""}">${esc(uiText(slot.displayText))}</div>`).join("")}</div>`).join("")}
            </div>`).join("")}</section>`).join("");
        document.getElementById("confirmBracketApply").checked = false;
        document.getElementById("confirmBracketApply").disabled = hasErrors;
        root.querySelector("[data-action='apply']").disabled = true;
    }

    async function toggleLock() {
        const next = !state.locked;
        const prompt = next
            ? `Khóa danh sách ${state.eligible.length} đội? User sẽ không thể đăng ký hoặc ghép cặp mới.`
            : "Mở lại đăng ký? Preview hiện tại (nếu có) sẽ bị hủy.";
        if (!window.confirm(prompt)) return;
        await api(`/api/admin/tournaments/${tournamentId}/bracket/registration-lock`, { method: "POST", body: JSON.stringify({ locked: next }) });
        state.locked = next; state.preview = null; resetSeedOrder(); renderSeeds(); updateLockUi(); showMessage("success", next ? "Đã khóa danh sách đăng ký." : "Đã mở lại đăng ký.");
    }

    function showApplyModalError(message) {
        const target = document.querySelector("[data-apply-modal-error]");
        target.textContent = uiText(message || "");
        target.classList.toggle("d-none", !message);
    }

    function renderApplyReferee(referee) {
        const target = document.querySelector("[data-apply-referee-result]");
        target.classList.toggle("is-empty", !referee);
        target.classList.toggle("is-valid", Boolean(referee));
        target.innerHTML = referee
            ? `<i class="fas fa-check-circle"></i><span><strong>${esc(referee.fullName)}</strong><small>User #${referee.userId} · ${esc(referee.refereeType || "Trọng tài")} · hồ sơ đã xác minh</small></span>`
            : '<i class="fas fa-user-shield"></i><span>Chưa kiểm tra tài khoản trọng tài.</span>';
    }

    async function lookupApplyReferee() {
        const input = document.getElementById("applyRefereeUserId");
        const refereeUserId = Number(input.value);
        if (!Number.isSafeInteger(refereeUserId) || refereeUserId <= 0)
            throw new Error("Vui lòng nhập ID tài khoản trọng tài hợp lệ.");
        const referee = await api(`/api/admin/referees/find-by-user-id/${refereeUserId}`);
        if (Number(referee.userId) !== refereeUserId)
            throw new Error("Tài khoản trọng tài trả về không khớp với ID đã nhập.");
        state.applyReferee = referee;
        renderApplyReferee(referee);
        return referee;
    }

    function openApplyDetailsModal() {
        if (!state.locked) throw new Error("Cần khóa danh sách đăng ký trước khi áp dụng.");
        if (!state.preview) throw new Error("Bản xem trước không còn hiệu lực. Vui lòng xếp lại đội đấu.");
        if (!document.getElementById("confirmBracketApply").checked) throw new Error("Vui lòng xác nhận đã kiểm tra đội đấu và đường đi tiếp.");
        document.querySelector("[data-apply-template]").textContent = `${state.preview.templateName} · v${state.preview.versionNumber}`;
        document.querySelector("[data-apply-match-count]").textContent = state.preview.matchCount;
        document.querySelector("[data-apply-shared-match-count]").textContent = state.preview.matchCount;
        showApplyModalError();
        renderApplyReferee(state.applyReferee);
        window.jQuery("#applyBracketDetailsModal").modal("show");
    }

    async function applyBracket(details) {
        if (!state.locked) throw new Error("Cần khóa danh sách đăng ký trước khi áp dụng.");
        if (!state.preview) throw new Error("Bản xem trước không còn hiệu lực. Vui lòng xếp lại đội đấu.");
        const method = state.preview.seedingMethod;
        const body = {
            bracketTemplateVersionId: state.preview.bracketTemplateVersionId,
            seedingMethod: method,
            randomSeed: state.preview.randomSeed,
            seedAssignments: [],
            previewHash: state.preview.previewHash,
            startAt: details.startAt,
            refereeUserId: details.refereeUserId,
            addressText: details.addressText
        };
        const result = await api(`/api/admin/tournaments/${tournamentId}/bracket/apply`, { method: "POST", body: JSON.stringify(body) });
        state.active = result.data;
        state.preview = null;
        renderActive();
        showMessage("success", `Đã tạo ${state.active.generatedRoundCount} vòng, ${state.active.generatedGroupCount} bảng và ${state.active.generatedMatchCount} trận.`);
        const history = await api(`/api/admin/tournaments/${tournamentId}/bracket/application-history`);
        renderHistory(history.items || []);
    }

    async function reconcileBracket() {
        const result = await api(`/api/admin/tournaments/${tournamentId}/bracket/reconcile`, {
            method: "POST",
            body: "{}"
        });
        const data = result.data;
        if (data.unresolvedSlotCount > 0) {
            showMessage("error", `${result.message} ${data.unresolvedSlots.join("; ")}`);
            return;
        }
        showMessage("success", result.message);
    }

    function showPlacementSnapshot() {
        if (!state.active) return;
        document.querySelector("[data-placement-snapshot-rows]").innerHTML = (state.active.seeds || []).map((item) => `<tr>
            <td><strong>Đội đấu ${item.seedNumber}</strong></td>
            <td><strong>${esc(item.isBye ? "BYE · Miễn đấu" : item.teamName)}</strong><small>${item.isBye ? "Suất trống" : esc([item.player1Name, item.player2Name].filter(Boolean).join(" & "))}</small></td>
            <td>${esc(item.regCode || "—")}</td>
            <td>${item.isManuallyAdjusted ? "Admin điều chỉnh" : esc(methodLabel(state.active.seedingMethod))}</td>
        </tr>`).join("");
        window.jQuery("#teamPlacementSnapshotModal").modal("show");
    }

    root.addEventListener("change", (event) => {
        if (event.target.name === "bracketTemplate") {
            state.selectedVersionId = Number(event.target.value);
            state.preview = null;
            resetSeedOrder();
            selectTemplateDefaultSeeding();
            renderSeeds();
        }
        if (event.target.id === "confirmBracketApply") {
            const hasErrors = (state.preview?.validation?.errorCount || 0) > 0;
            root.querySelector("[data-action='apply']").disabled = !event.target.checked || state.busy || hasErrors;
        }
    });

    document.getElementById("teamPlacementSearch").addEventListener("input", (event) => {
        state.seedQuery = event.target.value || "";
        renderSeeds();
    });

    root.addEventListener("click", async (event) => {
        const button = event.target.closest("[data-action], [data-step-nav]");
        if (!button || state.busy) return;
        try {
            if (button.dataset.stepNav && Number(button.dataset.stepNav) <= state.maxStep) goStep(Number(button.dataset.stepNav));
            if (button.dataset.action === "next" || button.dataset.action === "back") goStep(Number(button.dataset.next));
            if (button.dataset.action === "toggle-lock") await toggleLock();
            if (button.dataset.action === "open-placement-method") openPlacementMethodModal();
            if (button.dataset.action === "show-preview") { renderPreview(); goStep(4); }
            if (button.dataset.action === "apply") openApplyDetailsModal();
            if (button.dataset.action === "reconcile") { setBusy(true); await reconcileBracket(); }
            if (button.dataset.action === "view-placements") showPlacementSnapshot();
            if (button.dataset.action === "open-reset") window.jQuery("#resetBracketModal").modal("show");
        } catch (error) { showMessage("error", operationErrorMessage(button.dataset.action, error)); }
        finally { if (state.busy) setBusy(false); }
    });

    const placementMethodForm = document.getElementById("teamPlacementMethodForm");
    placementMethodForm.addEventListener("submit", (event) => event.preventDefault());
    placementMethodForm.querySelectorAll("input[name='placementMethod']").forEach((input) => {
        input.addEventListener("click", () => {
            if (!["REGISTRATION_ORDER", "RANDOM"].includes(input.value)) return;
            state.pendingPlacementMethod = input.value;
            try {
                populatePlacementConfirmation();
                switchModal("#teamPlacementMethodModal", "#teamPlacementConfirmModal");
            } catch (error) {
                window.jQuery("#teamPlacementMethodModal").modal("hide");
                showMessage("error", operationErrorMessage("open-placement-method", error));
            }
        });
    });

    document.getElementById("backToPlacementMethod").addEventListener("click", () => {
        switchModal("#teamPlacementConfirmModal", "#teamPlacementMethodModal");
    });

    document.getElementById("teamPlacementConfirmForm").addEventListener("submit", async (event) => {
        event.preventDefault();
        const submit = event.target.querySelector("button[type='submit']");
        submit.disabled = true;
        setBusy(true);
        try {
            const method = state.pendingPlacementMethod;
            if (!["REGISTRATION_ORDER", "RANDOM"].includes(method)) throw new Error("Vui lòng chọn lại cách xếp đội đấu.");
            await createPlacementPreview(method);
            window.jQuery("#teamPlacementConfirmModal").modal("hide");
        } catch (error) {
            window.jQuery("#teamPlacementConfirmModal").modal("hide");
            showMessage("error", operationErrorMessage("confirm-placement", error));
        } finally {
            state.pendingPlacementMethod = null;
            submit.disabled = false;
            setBusy(false);
        }
    });

    document.getElementById("applyRefereeUserId").addEventListener("input", () => {
        state.applyReferee = null;
        renderApplyReferee(null);
        showApplyModalError();
    });

    document.getElementById("checkApplyReferee").addEventListener("click", async (event) => {
        const button = event.currentTarget;
        button.disabled = true;
        showApplyModalError();
        try {
            await lookupApplyReferee();
        } catch (error) {
            state.applyReferee = null;
            renderApplyReferee(null);
            showApplyModalError(error.message);
        } finally {
            button.disabled = false;
        }
    });

    document.getElementById("applyBracketDetailsForm").addEventListener("submit", async (event) => {
        event.preventDefault();
        const form = event.currentTarget;
        if (!form.checkValidity()) {
            form.reportValidity();
            return;
        }

        const refereeUserId = Number(document.getElementById("applyRefereeUserId").value);
        const addressText = document.getElementById("applyMatchAddress").value.trim();
        const startAt = document.getElementById("applyMatchStartAt").value;
        const submit = form.querySelector("button[type='submit']");
        submit.disabled = true;
        setBusy(true);
        showApplyModalError();
        try {
            if (!addressText) throw new Error("Vui lòng nhập địa chỉ thi đấu.");
            if (!state.applyReferee || Number(state.applyReferee.userId) !== refereeUserId)
                await lookupApplyReferee();
            await applyBracket({ startAt, refereeUserId, addressText });
            window.jQuery("#applyBracketDetailsModal").modal("hide");
        } catch (error) {
            showApplyModalError(operationErrorMessage("apply", error));
        } finally {
            submit.disabled = false;
            setBusy(false);
        }
    });

    document.getElementById("resetBracketForm").addEventListener("submit", async (event) => {
        event.preventDefault();
        const confirmation = document.getElementById("resetConfirmation").value.trim();
        const reason = document.getElementById("resetReason").value.trim();
        if (confirmation !== "RESET") { showMessage("error", "Vui lòng nhập đúng RESET để xác nhận."); return; }
        const submit = event.target.querySelector("button[type='submit']"); submit.disabled = true;
        try {
            await api(`/api/admin/tournaments/${tournamentId}/bracket/reset`, { method: "POST", body: JSON.stringify({ reason }) });
            window.jQuery("#resetBracketModal").modal("hide");
            showMessage("success", "Đã reset bracket. Danh sách đội đấu cũ vẫn được lưu trong lịch sử.");
            await load();
        } catch (error) { showMessage("error", error.message); }
        finally { submit.disabled = false; }
    });

    load();
})();
