(function () {
    "use strict";

    const root = document.getElementById("tournamentBracketSetup");
    if (!root) return;
    const tournamentId = Number(root.dataset.tournamentId);
    const state = {
        locked: root.dataset.registrationLocked === "true", active: null, templates: [], eligible: [],
        candidates: [], hasRegistrationFee: false, excludeUnpaidTeams: false, dataReady: false,
        originalOrder: [], seedOrder: [], selectedVersionId: null, preview: null, randomSeed: null,
        placementMethod: "REGISTRATION_ORDER", pendingPlacementMethod: null,
        currentStep: 1, maxStep: 1, busy: false, seedQuery: "", applyReferee: null,
        virtualTeamChoiceMade: false, pendingVirtualTeamMethod: null
    };
    let activeLoadController = null;
    let loadSequence = 0;

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
            const details = [...new Set((payload.issues || []).filter(item => item.severity === "ERROR").map(item => item.message))];
            const error = new Error(details.length
                ? details.slice(0, 8).join("\n") + (details.length > 8 ? `\nCòn ${details.length - 8} lỗi. Mở trình thiết kế mẫu để kiểm tra đầy đủ.` : "")
                : payload.message || "Không thể thực hiện yêu cầu.");
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
        return ({ SINGLE_ELIMINATION: "Loại trực tiếp", GROUP_KNOCKOUT: "Vòng bảng + knockout", DOUBLE_ELIMINATION: "Nhánh thắng/thua", CUSTOM: "Tùy chỉnh" })[value] || value;
    }

    function participantModeLabel(value) {
        return value === "RELAY_TEAM" ? "Đội tiếp sức" : "Đơn/đôi";
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
            if (!button.closest(".modal")) button.disabled = value || !state.dataReady
                || (button.dataset.action === "apply" && (!state.locked || !previewIsReady() || !document.getElementById("confirmBracketApply")?.checked));
        });
        document.getElementById("excludeUnpaidTeams").disabled = value || !state.hasRegistrationFee;
    }

    function previewIsReady() {
        const preview = state.preview;
        return Boolean(preview) && preview.validation?.isValid !== false
            && !(preview.validation?.errorCount > 0)
            && !(preview.validation?.issues || []).some(issue => issue.severity === "ERROR")
            && !(preview.rounds || []).some(round => round.groups.some(group => group.matches.some(match =>
                match.slots.some(slot => slot.sourceType === "SEED" && (!slot.seedNumber || !slot.registrationId)))));
    }

    function clearPreview() {
        state.preview = null;
        const confirmation = document.getElementById("confirmBracketApply");
        confirmation.checked = false;
        confirmation.disabled = true;
        root.querySelector("[data-action='apply']").disabled = true;
        root.querySelector("[data-preview-board]").innerHTML = "";
        root.querySelector("[data-preview-summary]").innerHTML = "";
        root.querySelector("[data-preview-payment-summary]").textContent = "";
        root.querySelector("[data-preview-validation]").classList.add("d-none");
        root.querySelector("[data-preview-hash]").textContent = "—";
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

    function rosterText(team) {
        if (!team.relay?.members) return [team.player1Name, team.player2Name].filter(Boolean).join(" & ");
        const pairs = new Map();
        [...team.relay.members].sort((a, b) => a.position - b.position).forEach(member => {
            const pair = Math.ceil(member.position / 2);
            if (!pairs.has(pair)) pairs.set(pair, []);
            pairs.get(pair).push(member.displayName);
        });
        return [...pairs].map(([number, names]) => `Cặp ${number}: ${names.join(" & ")}`).join(" · ");
    }

    function ratingText(team) {
        return team.relay ? `${team.relay.teamSize} VĐV · Đội hình cố định`
            : `Trình ${team.player1Level || 0}${team.player2Name ? ` + ${team.player2Level || 0}` : ""}`;
    }

    function paymentLabel(team) {
        return !state.hasRegistrationFee ? "Miễn phí" : team.paid ? "Đã thanh toán" : "Chưa thanh toán";
    }

    function updateSelectedTeams() {
        state.eligible = state.candidates.filter(team => !state.excludeUnpaidTeams || !state.hasRegistrationFee || team.paid);
        state.originalOrder = state.eligible.map(team => team.registrationId);
    }

    function invalidatePreview() {
        clearPreview();
        state.randomSeed = null;
        state.virtualTeamChoiceMade = false;
        state.pendingVirtualTeamMethod = null;
        state.pendingPlacementMethod = null;
        state.maxStep = 1;
        resetSeedOrder();
        document.getElementById("confirmBracketApply").checked = false;
        root.querySelector("[data-action='apply']").disabled = true;
        root.querySelector("[data-preview-board]").innerHTML = "";
        root.querySelector("[data-preview-summary]").innerHTML = "";
        root.querySelector("[data-preview-payment-summary]").textContent = "";
        renderSeeds();
    }

    function renderEligible() {
        const paidCount = state.candidates.filter(team => team.paid).length;
        const excludedCount = state.candidates.length - state.eligible.length;
        const paymentCounts = state.hasRegistrationFee
            ? `<span><strong>${paidCount}</strong> đã thanh toán</span><span><strong>${state.candidates.length - paidCount}</strong> chưa thanh toán</span>`
            : '<span>Giải miễn phí</span>';
        document.getElementById("excludeUnpaidTeams").checked = state.excludeUnpaidTeams;
        root.querySelector("[data-payment-description]").textContent = !state.hasRegistrationFee
            ? "Giải miễn phí: tất cả đội đủ điều kiện đều được đưa vào sơ đồ."
            : state.excludeUnpaidTeams
                ? "Các đội chưa thanh toán được đánh dấu bên dưới và không đưa vào sơ đồ. Đăng ký của đội vẫn được giữ."
                : "Mặc định đưa cả đội đã thanh toán và chưa thanh toán vào sơ đồ.";
        root.querySelector("[data-payment-summary]").innerHTML = `<span>Tổng <strong>${state.candidates.length}</strong> đội</span>${paymentCounts}<span>Đưa vào sơ đồ: <strong>${state.eligible.length}</strong></span><span>Bị loại do chưa thanh toán: <strong>${excludedCount}</strong></span>`;
        root.querySelector("[data-eligible-count]").textContent = state.eligible.length;
        root.querySelector("[data-eligible-list]").innerHTML = state.candidates.length
            ? state.candidates.map((team, index) => {
                const excluded = state.excludeUnpaidTeams && state.hasRegistrationFee && !team.paid;
                return `<div class="tbs-team-row ${excluded ? "is-excluded" : ""}">
                <span class="tbs-team-row__number">${index + 1}</span>
                <div><strong>${esc(team.teamName)}</strong><small>${esc(team.regCode || `#${team.registrationId}`)} · ${esc(rosterText(team))}</small><small>${esc(ratingText(team))} · ${team.points || 0} điểm xếp hạng</small>${excluded ? '<small class="tbs-excluded-label">Không đưa vào sơ đồ · Chưa thanh toán</small>' : ""}</div>
                <span class="tbs-paid ${state.hasRegistrationFee && !team.paid ? "is-unpaid" : ""}">${paymentLabel(team)}</span><span class="tbs-reg-time">${esc(formatDate(team.registeredAt))}</span>
            </div>`; }).join("")
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
                        <div class="tbs-template-card__foot"><span>${esc(formatLabel(item.formatType))} · ${esc(participantModeLabel(item.participantMode))}</span><span>v${item.currentVersionNumber} · ${item.roundCount} vòng · ${item.groupCount} bảng</span></div>
                        ${item.isApplicable ? "" : `<span class="tbs-incompatible"><i class="fas fa-exclamation-triangle mr-1"></i>${esc(item.inapplicableReason)}</span>`}
                    </div>
                </label>`;
            }).join("")
            : '<div class="text-center text-muted p-5">Chưa có template published.</div>';
    }

    function orderedTeams() {
        const byId = new Map(state.eligible.map((item) => [item.registrationId, item]));
        return [...(state.preview?.seeds || [])]
            .sort((first, second) => first.seedNumber - second.seedNumber)
            .map((seed) => seed.registrationId
                ? byId.get(seed.registrationId) || seed
                : {
                    ...seed,
                    registrationId: -seed.seedNumber,
                    teamName: seed.isVirtualTeam ? seed.teamName : "BYE · Miễn đấu"
                })
            .filter(Boolean);
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
            ? `${state.preview.eligibleRegistrationCount} đội thật · ${state.preview.virtualTeamCount || 0} đội ảo · ${state.preview.byeCount} BYE`
            : "0 đội đấu";
        if (!state.preview) {
            root.querySelector("[data-seed-list]").innerHTML = '<div class="text-center text-muted p-4">Hãy chọn cách xếp và xác nhận để phân bổ đội đấu.</div>';
            return;
        }
        const query = state.seedQuery.trim().toLocaleLowerCase("vi");
        const rows = orderedTeams()
            .map((team, index) => ({ team, placementNumber: index + 1 }))
            .filter(({ team }) => !query || [team.teamName, team.regCode, rosterText(team), team.isBye ? "BYE miễn đấu" : "", team.isVirtualTeam ? "đội ảo" : ""]
                .filter(Boolean).join(" ").toLocaleLowerCase("vi").includes(query));
        root.querySelector("[data-seed-list]").innerHTML = rows.length ? rows.map(({ team, placementNumber }) => `<div class="tbs-seed-row ${team.isBye ? "is-bye" : ""} ${team.isVirtualTeam ? "is-virtual" : ""}">
            <span class="tbs-seed-row__number" title="Đội đấu ${placementNumber}">${placementNumber}</span>
            <div><strong>${esc(team.teamName)}</strong><small>${team.isBye ? "Suất trống tự động đi tiếp" : team.isVirtualTeam ? "Đội nội bộ · Không liên kết user" : `${esc(team.regCode || "")} · ${esc(rosterText(team))} · ${esc(ratingText(team))}`}</small></div>
            <span class="tbs-paid ${!team.isBye && !team.isVirtualTeam && state.hasRegistrationFee && !team.paid ? "is-unpaid" : ""}">${team.isBye ? "BYE" : team.isVirtualTeam ? "Đội ảo" : paymentLabel(team)}</span>
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
        const newRegistrations = state.candidates.filter((team) => !snapshotRegistrationIds.has(team.registrationId));
        const newRegistrationNotice = root.querySelector("[data-active-new-registrations]");
        newRegistrationNotice.classList.toggle("d-none", newRegistrations.length === 0);
        newRegistrationNotice.textContent = newRegistrations.length
            ? `${newRegistrations.length} đội chưa nằm trong sơ đồ: ${newRegistrations.map((team) => team.regCode || `#${team.registrationId}`).join(", ")}. Các đội này có thể đã bị loại khi tạo sơ đồ hoặc được đăng ký sau đó. Cần reset và xếp lại trước khi thi đấu nếu muốn bổ sung.`
            : "";
        root.querySelector("[data-active-stats]").innerHTML = `<span>${state.active.eligibleRegistrationCount} đội thật</span><span>${state.active.virtualTeamCount || 0} đội ảo</span><span>${state.active.byeCount} BYE</span><span>${state.active.generatedRoundCount} vòng</span><span>${state.active.generatedGroupCount} bảng</span><span>${state.active.generatedMatchCount} trận</span>`;
    }

    function renderHistory(items) {
        root.querySelector("[data-history-rows]").innerHTML = items.length ? items.map((item) => `<tr>
            <td><strong>#${item.tournamentBracketApplicationId}</strong><small>${item.isActive ? "Đang hoạt động" : "Đã đóng"}</small></td>
            <td><strong>${esc(item.templateName)}</strong><small>${esc(item.templateCode)} · v${item.versionNumber}</small></td>
            <td><strong>${esc(methodLabel(item.seedingMethod))}</strong><small>${item.eligibleRegistrationCount} đội thật · ${item.virtualTeamCount || 0} đội ảo · ${item.byeCount} BYE</small></td>
            <td><span class="tbs-history-status is-${String(item.status).toLowerCase()}">${esc(item.status)}</span>${item.revertReason ? `<small>${esc(item.revertReason)}</small>` : ""}</td>
            <td>${esc(formatDate(item.appliedAt || item.createdAt))}</td>
        </tr>`).join("") : '<tr><td colspan="5" class="text-center text-muted p-4">Chưa có lịch sử áp dụng.</td></tr>';
    }

    async function load() {
        const sequence = ++loadSequence;
        activeLoadController?.abort();
        const controller = new AbortController();
        activeLoadController = controller;
        state.dataReady = false;
        invalidatePreview();
        goStep(1);
        setBusy(true, true); showMessage();
        try {
            const [templates, eligible, application, history] = await Promise.all([
                api(`/api/admin/tournaments/${tournamentId}/bracket/templates?excludeUnpaidTeams=${state.excludeUnpaidTeams}`, { signal: controller.signal }),
                api(`/api/admin/tournaments/${tournamentId}/bracket/eligible-registrations`, { signal: controller.signal }),
                api(`/api/admin/tournaments/${tournamentId}/bracket/application`, { signal: controller.signal }),
                api(`/api/admin/tournaments/${tournamentId}/bracket/application-history`, { signal: controller.signal })
            ]);
            if (sequence !== loadSequence) return;
            state.templates = templates.items || [];
            state.candidates = eligible.data || [];
            state.hasRegistrationFee = Number(eligible.registrationFeeAmount || 0) > 0;
            if (!state.hasRegistrationFee) state.excludeUnpaidTeams = false;
            updateSelectedTeams();
            state.dataReady = true;
            state.active = application.item;
            const firstApplicable = state.templates.find((item) => item.isApplicable);
            state.selectedVersionId = firstApplicable ? Number(firstApplicable.currentPublishedVersionId) : null;
            resetSeedOrder();
            selectTemplateDefaultSeeding();
            updateLockUi(); renderEligible(); renderTemplates(); renderSeeds(); renderActive(); renderHistory(history.items || []);
        } catch (error) {
            if (error.name !== "AbortError" && sequence === loadSequence) {
                showMessage("error", error.message);
            }
        }
        finally {
            if (sequence === loadSequence) {
                activeLoadController = null;
                setBusy(false, false);
            }
        }
    }

    function goStep(step) {
        if (step > 1 && !state.dataReady) throw new Error("Vui lòng tải lại danh sách đội trước khi tiếp tục.");
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

    async function createPlacementPreview(method, fillMissingWithVirtualTeams = false) {
        if (!state.selectedVersionId) throw new Error("Vui lòng chọn template.");
        clearPreview();
        state.maxStep = Math.min(state.maxStep, 2);
        renderSeeds();
        state.randomSeed = null;
        const body = {
            bracketTemplateVersionId: state.selectedVersionId,
            seedingMethod: method,
            randomSeed: null,
            fillMissingWithVirtualTeams,
            excludeUnpaidTeams: state.excludeUnpaidTeams,
            seedAssignments: []
        };
        const result = await api(`/api/admin/tournaments/${tournamentId}/bracket/preview`, { method: "POST", body: JSON.stringify(body) });
        state.preview = result.data;
        state.placementMethod = method;
        state.randomSeed = state.preview.randomSeed;
        state.virtualTeamChoiceMade = fillMissingWithVirtualTeams;
        state.pendingVirtualTeamMethod = null;
        syncPlacementOrderFromPreview();
        renderSeeds();
        goStep(3);
        showMessage("success", `Đã phân bổ ${state.preview.eligibleRegistrationCount} đội theo phương pháp ${methodLabel(method)}.`);
    }

    function renderPreview() {
        const preview = state.preview;
        if (!preview) throw new Error("Vui lòng xếp lại đội đấu để tạo bản xem trước mới.");
        root.querySelector("[data-preview-payment-summary]").textContent = `${preview.eligibleRegistrationCount} đội được đưa vào sơ đồ · ${preview.excludedUnpaidRegistrationCount || 0} đội bị loại do chưa thanh toán. ${preview.excludeUnpaidTeams ? "Đã chọn loại đội chưa thanh toán." : "Bao gồm cả đội đã thanh toán và chưa thanh toán."}`;
        root.querySelector("[data-preview-hash]").textContent = preview.previewHash;
        const stats = [
            [preview.eligibleRegistrationCount, "Đội thật"], [preview.virtualTeamCount || 0, "Đội ảo"], [preview.byeCount, "BYE"], [preview.roundCount, "Vòng"],
            [preview.groupCount, "Bảng / nhánh"], [preview.matchCount, "Trận"], [preview.matchCount, "Dùng cấu hình chung"]
        ];
        root.querySelector("[data-preview-summary]").innerHTML = stats.map(([value, label]) => `<div class="tbs-preview-stat"><strong>${value}</strong><span>${label}</span></div>`).join("");
        const issues = (preview.validation?.issues || []).filter((item) => item.severity !== "INFO");
        const validation = root.querySelector("[data-preview-validation]");
        validation.classList.toggle("d-none", issues.length === 0);
        validation.innerHTML = issues.map((item) => `<div><strong>${esc(item.severity)}:</strong> ${esc(uiText(item.message))}</div>`).join("");
        const hasErrors = !previewIsReady();
        if (hasErrors && !issues.some(item => item.severity === "ERROR")) {
            validation.classList.remove("d-none");
            validation.insertAdjacentHTML("beforeend", "<div>Mẫu còn vị trí đội chưa được gán. Hãy sửa mẫu và xem trước lại.</div>");
        }
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
            ? `Khóa danh sách đăng ký của giải (${state.candidates.length} đội đủ điều kiện)? User sẽ không thể đăng ký hoặc ghép cặp mới.`
            : "Mở lại đăng ký? Preview hiện tại (nếu có) sẽ bị hủy.";
        if (!window.confirm(prompt)) return;
        await api(`/api/admin/tournaments/${tournamentId}/bracket/registration-lock`, { method: "POST", body: JSON.stringify({ locked: next }) });
        state.locked = next; state.preview = null; state.virtualTeamChoiceMade = false; resetSeedOrder(); renderSeeds(); updateLockUi(); showMessage("success", next ? "Đã khóa danh sách đăng ký." : "Đã mở lại đăng ký.");
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

    function openVirtualTeamConfirmModal() {
        const pendingTemplate = state.pendingVirtualTeamMethod ? selectedTemplate() : null;
        const pendingMissingCount = pendingTemplate
            ? Math.max(0, Number(pendingTemplate.seedCapacity || 0) - state.eligible.length)
            : 0;
        if (!state.pendingVirtualTeamMethod && (!state.preview || state.preview.byeCount <= 0)) {
            openApplyDetailsModal();
            return;
        }
        const missingCount = state.pendingVirtualTeamMethod
            ? pendingMissingCount
            : Number(state.preview.byeCount || 0);
        const realCount = state.pendingVirtualTeamMethod
            ? state.eligible.length
            : state.preview.eligibleRegistrationCount;
        const capacity = state.pendingVirtualTeamMethod
            ? Number(pendingTemplate?.seedCapacity || 0)
            : state.preview.seedCapacity;
        document.querySelector("[data-virtual-missing-count]").textContent = missingCount;
        document.querySelector("[data-virtual-real-count]").textContent = realCount;
        document.querySelector("[data-virtual-capacity]").textContent = capacity;
        document.querySelector("[data-virtual-create-count]").textContent = missingCount;
        const keepByeButton = document.getElementById("keepBracketBye");
        keepByeButton.disabled = Boolean(state.pendingVirtualTeamMethod);
        keepByeButton.title = state.pendingVirtualTeamMethod
            ? "Không thể giữ BYE vì sẽ tạo ra trận có cả hai vị trí là BYE."
            : "";
        window.jQuery("#virtualTeamConfirmModal").modal("show");
    }

    async function createVirtualTeamPreview() {
        if (state.pendingVirtualTeamMethod) {
            await createPlacementPreview(state.pendingVirtualTeamMethod, true);
            return;
        }
        if (!state.preview) throw new Error("Bản xem trước không còn hiệu lực. Vui lòng xếp lại đội đấu.");
        const body = {
            bracketTemplateVersionId: state.preview.bracketTemplateVersionId,
            seedingMethod: state.preview.seedingMethod,
            randomSeed: state.preview.randomSeed,
            fillMissingWithVirtualTeams: true,
            excludeUnpaidTeams: state.preview.excludeUnpaidTeams,
            seedAssignments: []
        };
        const result = await api(`/api/admin/tournaments/${tournamentId}/bracket/preview`, {
            method: "POST",
            body: JSON.stringify(body)
        });
        state.preview = result.data;
        state.randomSeed = state.preview.randomSeed;
        state.virtualTeamChoiceMade = true;
        syncPlacementOrderFromPreview();
        renderSeeds();
        renderPreview();
        goStep(4);
        showMessage("success", `Đã thêm ${state.preview.virtualTeamCount || 0} đội ảo vào bản xem trước. Vui lòng kiểm tra và xác nhận lại.`);
    }

    function openApplyDetailsModal() {
        if (!state.locked) throw new Error("Cần khóa danh sách đăng ký trước khi áp dụng.");
        if (!state.preview) throw new Error("Bản xem trước không còn hiệu lực. Vui lòng xếp lại đội đấu.");
        if (!previewIsReady()) throw new Error("Sơ đồ còn lỗi cấu hình. Hãy sửa các vị trí đội được báo lỗi và xem trước lại.");
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
        if (!previewIsReady()) throw new Error("Sơ đồ còn lỗi cấu hình; chưa thể áp dụng.");
        const method = state.preview.seedingMethod;
        const body = {
            bracketTemplateVersionId: state.preview.bracketTemplateVersionId,
            seedingMethod: method,
            randomSeed: state.preview.randomSeed,
            fillMissingWithVirtualTeams: (state.preview.virtualTeamCount || 0) > 0,
            excludeUnpaidTeams: state.preview.excludeUnpaidTeams,
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
            <td><strong>${esc(item.isBye ? "BYE · Miễn đấu" : item.teamName)}</strong><small>${item.isBye ? "Suất trống" : item.isVirtualTeam ? "Đội ảo nội bộ · Không liên kết user" : esc(rosterText(item))}</small></td>
            <td>${esc(item.regCode || "—")}</td>
            <td>${item.isVirtualTeam ? "Tự động lấp chỗ thiếu" : item.isManuallyAdjusted ? "Admin điều chỉnh" : esc(methodLabel(state.active.seedingMethod))}</td>
        </tr>`).join("");
        window.jQuery("#teamPlacementSnapshotModal").modal("show");
    }

    root.addEventListener("change", async (event) => {
        if (event.target.id === "excludeUnpaidTeams") {
            if (state.busy) { event.target.checked = state.excludeUnpaidTeams; return; }
            state.excludeUnpaidTeams = state.hasRegistrationFee && event.target.checked;
            updateSelectedTeams();
            renderEligible();
            await load();
            return;
        }
        if (event.target.name === "bracketTemplate") {
            state.selectedVersionId = Number(event.target.value);
            clearPreview();
            state.maxStep = Math.min(state.maxStep, 2);
            state.virtualTeamChoiceMade = false;
            resetSeedOrder();
            selectTemplateDefaultSeeding();
            renderSeeds();
        }
        if (event.target.id === "confirmBracketApply") {
            root.querySelector("[data-action='apply']").disabled = !event.target.checked || state.busy || !state.locked || !previewIsReady();
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
            if (button.dataset.action === "apply") {
                if ((state.preview?.byeCount || 0) > 0 && !state.virtualTeamChoiceMade
                    && state.preview?.participantMode !== "RELAY_TEAM")
                    openVirtualTeamConfirmModal();
                else
                    openApplyDetailsModal();
            }
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
            const template = selectedTemplate();
            const hasMissingTeams = Number(template?.seedCapacity || 0) > state.eligible.length;
            if (error?.code === "DOUBLE_BYE_MATCH" && hasMissingTeams
                && template?.participantMode !== "RELAY_TEAM") {
                state.preview = null;
                state.pendingVirtualTeamMethod = state.pendingPlacementMethod;
                renderSeeds();
                const confirmationModal = window.jQuery("#teamPlacementConfirmModal");
                confirmationModal.one("hidden.bs.modal", openVirtualTeamConfirmModal);
                confirmationModal.modal("hide");
            } else {
                window.jQuery("#teamPlacementConfirmModal").modal("hide");
                showMessage("error", operationErrorMessage("confirm-placement", error));
            }
        } finally {
            state.pendingPlacementMethod = null;
            submit.disabled = false;
            setBusy(false);
        }
    });

    document.getElementById("keepBracketBye").addEventListener("click", () => {
        state.virtualTeamChoiceMade = true;
        const modal = window.jQuery("#virtualTeamConfirmModal");
        modal.one("hidden.bs.modal", () => {
            try {
                openApplyDetailsModal();
            } catch (error) {
                showMessage("error", operationErrorMessage("apply", error));
            }
        });
        modal.modal("hide");
    });

    document.getElementById("createVirtualBracketTeams").addEventListener("click", async (event) => {
        const button = event.currentTarget;
        button.disabled = true;
        try {
            await createVirtualTeamPreview();
            window.jQuery("#virtualTeamConfirmModal").modal("hide");
        } catch (error) {
            window.jQuery("#virtualTeamConfirmModal").modal("hide");
            showMessage("error", operationErrorMessage("apply", error));
        } finally {
            button.disabled = false;
        }
    });

    window.jQuery("#virtualTeamConfirmModal").on("hidden.bs.modal", () => {
        if (!state.preview) state.pendingVirtualTeamMethod = null;
        const keepByeButton = document.getElementById("keepBracketBye");
        keepByeButton.disabled = false;
        keepByeButton.title = "";
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
            if (error.code === "PREVIEW_CHANGED") {
                invalidatePreview();
                goStep(1);
                window.jQuery("#applyBracketDetailsModal").modal("hide");
                showMessage("error", "Danh sách hoặc thông tin đội đã thay đổi. Vui lòng tải lại trang và xếp đội lại.");
            }
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

    window.addEventListener("pagehide", () => activeLoadController?.abort(), { once: true });
    load();
})();
