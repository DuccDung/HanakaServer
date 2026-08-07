(function () {
    "use strict";

    const root = document.getElementById("bracketTemplateEditor");
    if (!root) return;

    const templateId = Number(root.dataset.templateId);
    let versionId = Number(root.dataset.versionId);
    const draftStorageKey = `hanaka:bracket-template-draft:${versionId}`;
    const canvasZoomStorageKey = "hanaka:bracket-template-canvas-zoom";
    const matchLayoutStorageKey = `hanaka:bracket-template-match-layout:${versionId}`;
    const roundLayoutStorageKey = `hanaka:bracket-template-round-layout:${versionId}`;
    const roundsHost = root.querySelector("[data-rounds]");
    const state = {
        graph: null,
        dirty: false,
        busy: false,
        readOnly: false,
        canvasZoom: 1,
        canvasFocus: false,
        connectorFocusMatchKey: null,
        connectorFocusRoundKey: null,
        matchLayouts: {},
        roundLayouts: {},
        validation: null,
        matchEditor: null,
        bulkMatchEditor: null,
        initialTeamNumberingEditor: null,
        byePassEditor: null,
        quickPairEditor: null,
        sourceMatchPicker: null,
        advanceAudits: {}
    };
    const matchEditorModal = document.getElementById("matchEditorModal");
    const bulkMatchModal = document.getElementById("bulkMatchModal");
    const initialTeamNumberingModal = document.getElementById("initialTeamNumberingModal");
    const byePassModal = document.getElementById("byePassModal");
    const quickPairModal = document.getElementById("quickPairModal");
    const advanceAuditModal = document.getElementById("advanceAuditModal");
    const bulkAdvanceAuditModal = document.getElementById("bulkAdvanceAuditModal");
    const sourceMatchPickerDialog = document.getElementById("sourceMatchPickerDialog");
    const canvasScroll = root.querySelector(".bte-canvas-scroll");
    const canvasViewport = root.querySelector("[data-canvas-viewport]");
    const canvasStage = root.querySelector("[data-canvas-stage]");

    try {
        const savedZoom = Number(localStorage.getItem(canvasZoomStorageKey));
        if (savedZoom >= .5 && savedZoom <= 1.25) state.canvasZoom = savedZoom;
    } catch (_) { }
    try {
        const savedLayouts = JSON.parse(localStorage.getItem(matchLayoutStorageKey) || "{}");
        if (savedLayouts && typeof savedLayouts === "object" && !Array.isArray(savedLayouts)) state.matchLayouts = savedLayouts;
    } catch (_) { state.matchLayouts = {}; }
    try {
        const savedLayouts = JSON.parse(localStorage.getItem(roundLayoutStorageKey) || "{}");
        if (savedLayouts && typeof savedLayouts === "object" && !Array.isArray(savedLayouts)) state.roundLayouts = savedLayouts;
    } catch (_) { state.roundLayouts = {}; }

    const sourceLabels = {
        SEED: "Đội ban đầu", WINNER_MATCH: "Thắng trận", LOSER_MATCH: "Thua trận",
        GROUP_RANK: "Hạng bảng", BYE: "Miễn đấu (BYE)"
    };
    const versionStatusLabels = {
        DRAFT: "Bản nháp",
        PUBLISHED: "Đã xuất bản",
        ARCHIVED: "Đã lưu trữ"
    };
    const severityLabels = {
        ERROR: "LỖI",
        WARNING: "CẢNH BÁO",
        INFO: "THÔNG TIN"
    };

    const defaultGroupColor = "#5874D4";
    const groupColorPalette = ["#5874D4", "#16876C", "#C17413", "#9B51B5", "#C94F66", "#2683A8"];

    function esc(value) {
        return String(value ?? "").replace(/[&<>'"]/g, (char) => ({
            "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;"
        })[char]);
    }

    function attr(value) { return esc(value); }
    function disabled() { return state.readOnly ? " disabled" : ""; }

    function normalizeGroupColor(value, fallback = defaultGroupColor) {
        const color = String(value || "").trim().toUpperCase();
        return /^#[0-9A-F]{6}$/.test(color) ? color : fallback;
    }

    function groupThemeStyle(value) {
        const color = normalizeGroupColor(value);
        const red = Number.parseInt(color.slice(1, 3), 16);
        const green = Number.parseInt(color.slice(3, 5), 16);
        const blue = Number.parseInt(color.slice(5, 7), 16);
        const solidTint = (strength) => {
            const mix = (channel) => Math.round(255 + ((channel - 255) * strength));
            return `rgb(${mix(red)},${mix(green)},${mix(blue)})`;
        };
        return [
            `--bte-group-accent:${color}`,
            `--bte-group-head:${solidTint(.18)}`,
            `--bte-group-card:${solidTint(.10)}`,
            `--bte-group-border:${solidTint(.42)}`
        ].join(";");
    }

    function applyGroupTheme(groupElement, value) {
        if (!groupElement) return;
        const color = normalizeGroupColor(value);
        groupElement.setAttribute("style", groupThemeStyle(color));
        const picker = groupElement.querySelector("[data-group-field='groupColor']");
        if (picker) {
            picker.value = color;
            picker.title = `Màu nhánh ${color}`;
        }
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

    function showMessage(type, message) {
        const error = root.querySelector("[data-editor-error]");
        const success = root.querySelector("[data-editor-success]");
        error.classList.add("d-none");
        success.classList.add("d-none");
        if (!message) return;
        const target = type === "error" ? error : success;
        target.textContent = message;
        target.classList.remove("d-none");
        if (type === "success") setTimeout(() => target.classList.add("d-none"), 3500);
    }

    function setBusy(value, initial = false) {
        state.busy = value;
        root.querySelector("[data-editor-loading]").classList.toggle("d-none", !initial || !value);
        root.querySelectorAll("button").forEach((button) => {
            button.disabled = value
                || button.hasAttribute("data-disabled-always")
                || (state.readOnly && isEditAction(button.dataset.action));
        });
    }

    function isEditAction(action) {
        return ["add-round", "add-group", "add-match", "add-matches-bulk", "number-initial-teams", "add-bye-pass", "quick-pair", "edit-match", "delete-round", "delete-group", "delete-match"].includes(action);
    }

    function markDirty(value = true) {
        if (state.readOnly) return;
        state.dirty = value;
        const badge = root.querySelector("[data-save-state]");
        badge.classList.toggle("is-dirty", value);
        badge.classList.remove("is-saving");
        badge.innerHTML = value ? "<i></i>Chưa lưu" : "<i></i>Đã đồng bộ";
        if (value) scheduleLocalRecoverySave();
    }

    function reflectValidationState(validation) {
        if (state.readOnly || state.dirty || !validation) return;
        const badge = root.querySelector("[data-save-state]");
        if (validation.errorCount > 0) {
            badge.className = "bte-save-state is-error";
            badge.innerHTML = "<i></i>Bản nháp có lỗi";
        } else {
            badge.className = "bte-save-state";
            badge.innerHTML = "<i></i>Đã lưu";
        }
    }

    let recoveryTimer;
    function scheduleLocalRecoverySave() {
        clearTimeout(recoveryTimer);
        recoveryTimer = setTimeout(() => {
            try {
                collectGraph();
                localStorage.setItem(draftStorageKey, JSON.stringify({
                    rowVersion: state.graph.rowVersion,
                    savedAt: new Date().toISOString(),
                    graph: state.graph
                }));
            } catch (_) { /* Trình duyệt có thể chặn localStorage; không làm gián đoạn editor. */ }
        }, 250);
    }

    function clearLocalRecovery() {
        clearTimeout(recoveryTimer);
        try { localStorage.removeItem(draftStorageKey); } catch (_) { }
    }

    function option(value, label, selected) {
        return `<option value="${attr(value)}"${value === selected ? " selected" : ""}>${esc(label)}</option>`;
    }

    function normalizeSourceKey(value) {
        return String(value || "").trim().toUpperCase();
    }

    function matchLayout(matchKey) {
        const layout = state.matchLayouts[normalizeSourceKey(matchKey)] || {};
        return {
            x: Number.isFinite(Number(layout.x)) ? Number(layout.x) : 0,
            y: Number.isFinite(Number(layout.y)) ? Number(layout.y) : 0
        };
    }

    function persistMatchLayouts() {
        try { localStorage.setItem(matchLayoutStorageKey, JSON.stringify(state.matchLayouts)); } catch (_) { }
    }

    function setMatchLayout(matchKey, position, persist = false) {
        const key = normalizeSourceKey(matchKey);
        if (!key) return;
        if (Math.abs(position.x) < .5 && Math.abs(position.y) < .5) delete state.matchLayouts[key];
        else state.matchLayouts[key] = { x: Math.round(position.x), y: Math.round(position.y) };
        if (persist) persistMatchLayouts();
    }

    function moveMatchLayoutKey(oldKey, newKey) {
        const previous = normalizeSourceKey(oldKey);
        const next = normalizeSourceKey(newKey);
        if (!previous || !next || previous === next || !state.matchLayouts[previous]) return;
        state.matchLayouts[next] = state.matchLayouts[previous];
        delete state.matchLayouts[previous];
        persistMatchLayouts();
    }

    function resetMatchPosition(matchKey) {
        delete state.matchLayouts[normalizeSourceKey(matchKey)];
        persistMatchLayouts();
        const card = [...root.querySelectorAll("[data-match-key]")]
            .find((item) => item.dataset.matchKey === matchKey);
        card?.style.setProperty("--bte-match-x", "0px");
        card?.style.setProperty("--bte-match-y", "0px");
        syncFollowingActions();
        scheduleConnectors();
    }

    function resetAllMatchPositions() {
        state.matchLayouts = {};
        state.roundLayouts = {};
        persistMatchLayouts();
        persistRoundLayouts();
        root.querySelectorAll("[data-match-key]").forEach((card) => {
            card.style.setProperty("--bte-match-x", "0px");
            card.style.setProperty("--bte-match-y", "0px");
        });
        root.querySelectorAll("[data-round-key]").forEach((round) => {
            round.style.setProperty("--bte-round-x", "0px");
        });
        syncFollowingActions();
        syncCanvasStage();
        scheduleConnectors();
        showMessage("success", "Đã đưa toàn bộ vòng và card trận về vị trí mặc định.");
    }

    function removeMatchLayouts(matchKeys) {
        let changed = false;
        matchKeys.forEach((matchKey) => {
            const key = normalizeSourceKey(matchKey);
            if (state.matchLayouts[key]) {
                delete state.matchLayouts[key];
                changed = true;
            }
        });
        if (changed) persistMatchLayouts();
    }

    function roundLayout(roundKey) {
        const layout = state.roundLayouts[normalizeSourceKey(roundKey)] || {};
        return { x: Number.isFinite(Number(layout.x)) ? Number(layout.x) : 0 };
    }

    function persistRoundLayouts() {
        try { localStorage.setItem(roundLayoutStorageKey, JSON.stringify(state.roundLayouts)); } catch (_) { }
    }

    function setRoundLayout(roundKey, position, persist = false) {
        const key = normalizeSourceKey(roundKey);
        if (!key) return;
        if (Math.abs(position.x) < .5) delete state.roundLayouts[key];
        else state.roundLayouts[key] = { x: Math.round(position.x) };
        if (persist) persistRoundLayouts();
    }

    function moveRoundLayoutKey(oldKey, newKey) {
        const previous = normalizeSourceKey(oldKey);
        const next = normalizeSourceKey(newKey);
        if (!previous || !next || previous === next || !state.roundLayouts[previous]) return;
        state.roundLayouts[next] = state.roundLayouts[previous];
        delete state.roundLayouts[previous];
        persistRoundLayouts();
    }

    function resetRoundPosition(roundKey) {
        delete state.roundLayouts[normalizeSourceKey(roundKey)];
        persistRoundLayouts();
        const round = [...root.querySelectorAll("[data-round-key]")]
            .find((item) => item.dataset.roundKey === roundKey);
        round?.style.setProperty("--bte-round-x", "0px");
        syncCanvasStage();
        scheduleConnectors();
    }

    function removeRoundLayout(roundKey) {
        const key = normalizeSourceKey(roundKey);
        if (!state.roundLayouts[key]) return;
        delete state.roundLayouts[key];
        persistRoundLayouts();
    }

    function positionFollowingAction(element, matchKey) {
        if (!element) return;
        const layout = matchKey ? matchLayout(matchKey) : { x: 0, y: 0 };
        element.style.setProperty("--bte-follow-x", `${layout.x}px`);
        element.style.setProperty("--bte-follow-y", `${layout.y}px`);
    }

    function syncFollowingActions() {
        root.querySelectorAll("[data-group-key]").forEach((group) => {
            const matchesHost = group.querySelector(":scope > .bte-group__matches");
            const matches = [...(matchesHost?.querySelectorAll(":scope > [data-match-key]") || [])];
            positionFollowingAction(matchesHost?.querySelector(":scope > [data-follow-group-actions]"), matches.at(-1)?.dataset.matchKey);
        });
        root.querySelectorAll("[data-round-key]").forEach((round) => {
            const matches = [...round.querySelectorAll("[data-match-key]")];
            positionFollowingAction(round.querySelector("[data-follow-round-actions]"), matches.at(-1)?.dataset.matchKey);
        });
    }

    function duplicateMatchSource(match) {
        const slots = [...(match?.slots || [])].sort((a, b) => a.slotNumber - b.slotNumber);
        if (slots.length !== 2) return null;
        const [first, second] = slots;
        if (first.sourceType !== second.sourceType) return null;

        if (first.sourceType === "SEED"
            && Number(first.seedNumber) > 0
            && Number(first.seedNumber) === Number(second.seedNumber)) {
            return { message: `Đội 1 và Đội 2 đang cùng dùng vị trí đội ban đầu ${first.seedNumber}. Hãy chọn hai vị trí khác nhau.` };
        }

        if (["WINNER_MATCH", "LOSER_MATCH"].includes(first.sourceType)) {
            const sourceKey = normalizeSourceKey(first.sourceMatchKey);
            if (sourceKey && sourceKey === normalizeSourceKey(second.sourceMatchKey)) {
                const resultLabel = first.sourceType === "WINNER_MATCH" ? "đội thắng" : "đội thua";
                return { message: `Đội 1 và Đội 2 đều lấy ${resultLabel} của trận ${sourceKey}. Hãy chọn hai nguồn khác nhau.` };
            }
        }

        if (first.sourceType === "GROUP_RANK") {
            const groupKey = normalizeSourceKey(first.sourceGroupKey);
            if (groupKey
                && groupKey === normalizeSourceKey(second.sourceGroupKey)
                && Number(first.sourceRank) > 0
                && Number(first.sourceRank) === Number(second.sourceRank)) {
                return { message: `Đội 1 và Đội 2 đều lấy hạng ${first.sourceRank} của bảng ${groupKey}. Hãy chọn hai nguồn khác nhau.` };
            }
        }

        if (first.sourceType === "BYE") {
            return { message: "Một trận không thể để cả Đội 1 và Đội 2 cùng miễn đấu (BYE)." };
        }

        return null;
    }

    function byePassThroughInfo(match) {
        const slots = [...(match?.slots || [])].sort((first, second) => first.slotNumber - second.slotNumber);
        if (slots.length !== 2) return null;
        const byeSlots = slots.filter((slot) => normalizeSourceKey(slot.sourceType) === "BYE");
        if (byeSlots.length !== 1) return null;
        const teamSlot = slots.find((slot) => slot !== byeSlots[0]);
        if (normalizeSourceKey(teamSlot?.sourceType) !== "SEED" || Number(teamSlot.seedNumber) <= 0) return null;
        return { slots, byeSlot: byeSlots[0], teamSlot, seedNumber: Number(teamSlot.seedNumber) };
    }

    function byePassLabel(match) {
        const info = byePassThroughInfo(match);
        return info ? `BYE · Đội ban đầu ${info.seedNumber}` : null;
    }

    function firstDuplicateGraphSource() {
        for (const location of graphLocations().matches) {
            const duplicate = duplicateMatchSource(location.match);
            if (duplicate) return { ...location, duplicate };
        }
        return null;
    }

    function graphLocations() {
        const groups = [];
        const matches = [];
        state.graph.rounds.forEach((round, roundIndex) => {
            round.groups.forEach((group, groupIndex) => {
                const groupLocation = { round, roundIndex, group, groupIndex };
                groups.push(groupLocation);
                group.matches.forEach((match, matchIndex) => {
                    matches.push({ ...groupLocation, match, matchIndex });
                });
            });
        });
        return { groups, matches };
    }

    function comesBefore(source, target) {
        if (source.round.sortOrder !== target.round.sortOrder) return source.round.sortOrder < target.round.sortOrder;
        if (source.group.sortOrder !== target.group.sortOrder) return source.group.sortOrder < target.group.sortOrder;
        return source.match.sortOrder < target.match.sortOrder;
    }

    function groupMatchSources(sources) {
        const groups = [];
        sources.forEach((source) => {
            const groupId = `${source.round.roundKey}::${source.group.groupKey}`;
            let group = groups.find((item) => item.id === groupId);
            if (!group) {
                group = {
                    id: groupId,
                    label: `${source.round.roundLabel} · ${source.group.groupName || source.group.groupKey}`,
                    sources: []
                };
                groups.push(group);
            }
            group.sources.push(source);
        });

        return groups;
    }

    function groupedMatchOptionsHtml(sources, selectedKey) {
        return groupMatchSources(sources).map((group) => `<optgroup label="${attr(group.label)}">
            ${group.sources.map((source) => {
                const key = source.match.matchKey;
                const label = `${source.match.matchLabel || key} (${key})`;
                const search = `${group.label} ${label}`;
                return `<option value="${attr(key)}" data-source-search="${attr(search)}"${key === selectedKey ? " selected" : ""}>${esc(label)}</option>`;
            }).join("")}
        </optgroup>`).join("");
    }

    function normalizeSearch(value) {
        return String(value || "").normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase().trim();
    }

    function sourceFieldHtml(slot, matchKey, targetOverride = null) {
        const locations = graphLocations();
        const target = targetOverride || locations.matches.find((item) => item.match.matchKey === matchKey);
        if (slot.sourceType === "SEED") {
            return `<input type="number" min="1" max="1024" value="${slot.seedNumber ?? ""}" data-slot-field="seedNumber" aria-label="Vị trí đội ban đầu" placeholder="Ví dụ: 1"${disabled()} />`;
        }
        if (slot.sourceType === "WINNER_MATCH" || slot.sourceType === "LOSER_MATCH") {
            const sources = locations.matches
                .filter((source) => source.match.matchKey !== matchKey
                    && (!target || comesBefore(source, target))
                    && (slot.sourceType !== "LOSER_MATCH" || !byePassThroughInfo(source.match)));
            const choices = groupedMatchOptionsHtml(sources, slot.sourceMatchKey);
            if (targetOverride) {
                const selected = sources.find((source) => source.match.matchKey === slot.sourceMatchKey);
                const selectedLabel = selected ? `${selected.match.matchLabel || selected.match.matchKey} (${selected.match.matchKey})` : "Chọn trận nguồn";
                const selectedContext = selected
                    ? `${selected.round.roundLabel} · ${selected.group.groupName || selected.group.groupKey}`
                    : `${sources.length} trận có thể chọn`;
                return `<div class="bte-source-match-trigger-wrap">
                    <input type="hidden" value="${attr(slot.sourceMatchKey || "")}" data-slot-field="sourceMatchKey" />
                    <button class="bte-source-match-trigger" type="button" data-source-match-open${disabled()}>
                        <i class="fas fa-sitemap"></i>
                        <span><strong>${esc(selectedLabel)}</strong><small>${esc(selectedContext)}</small></span>
                        <i class="fas fa-external-link-alt"></i>
                    </button>
                </div>`;
            }
            return `<select data-slot-field="sourceMatchKey" aria-label="Trận nguồn"${disabled()}><option value="">Chọn trận...</option>${choices}</select>`;
        }
        if (slot.sourceType === "GROUP_RANK") {
            const choices = locations.groups
                .filter((source) => !target || source.round.sortOrder < target.round.sortOrder)
                .map((source) => option(
                    source.group.groupKey,
                    `${source.round.roundLabel} · ${source.group.groupName || source.group.groupKey} (${source.group.groupKey})`,
                    slot.sourceGroupKey))
                .join("");
            return `<div class="bte-slot__source-fields"><select data-slot-field="sourceGroupKey" aria-label="Bảng nguồn"${disabled()}><option value="">Chọn bảng...</option>${choices}</select><input type="number" min="1" value="${slot.sourceRank ?? ""}" data-slot-field="sourceRank" aria-label="Hạng"${disabled()} /></div>`;
        }
        return `<span class="text-muted small">Tự động miễn đấu</span>`;
    }

    function renderSlot(slot, matchKey, slotIndex, targetOverride = null) {
        const sourceOptions = Object.entries(sourceLabels).map(([value, label]) => option(value, label, slot.sourceType)).join("");
        const matchSourceClass = ["WINNER_MATCH", "LOSER_MATCH"].includes(slot.sourceType) ? " bte-slot--match-source" : "";
        const connectionStateClass = slot.sourceType === "WINNER_MATCH" && slot.sourceMatchKey
            ? " is-connected is-winner"
            : slot.sourceType === "LOSER_MATCH" && slot.sourceMatchKey
                ? " is-connected is-loser"
                : slot.sourceType === "GROUP_RANK" && slot.sourceGroupKey
                    ? " is-connected is-group"
                    : "";
        return `<div class="bte-slot${matchSourceClass}" data-slot-index="${slotIndex}">
            ${targetOverride ? "" : `<span class="bte-connection-input${connectionStateClass}" data-connection-target title="Thả dây vào nguồn đội ${slot.slotNumber}"><i></i></span>`}
            <div class="bte-slot__identity">
                <span class="bte-slot__number">${slot.slotNumber}</span>
                <strong>Đội ${slot.slotNumber}</strong>
            </div>
            <label class="bte-control-field">
                <span class="bte-field-caption">Loại nguồn</span>
                <select data-slot-field="sourceType" aria-label="Loại nguồn vị trí ${slot.slotNumber}"${disabled()}>${sourceOptions}</select>
            </label>
            <div class="bte-control-field bte-slot__value">
                <span class="bte-field-caption">Chi tiết nguồn</span>
                <div>${sourceFieldHtml(slot, matchKey, targetOverride)}</div>
            </div>
        </div>`;
    }

    function renderByePassMatch(match, roundIndex, groupIndex, matchIndex, info) {
        const round = state.graph.rounds[roundIndex];
        const group = round.groups[groupIndex];
        const layout = matchLayout(match.matchKey);
        const positionStyle = `--bte-match-x:${layout.x}px;--bte-match-y:${layout.y}px`;
        const locationLabel = `Vòng ${String(roundIndex + 1).padStart(2, "0")} · Nhánh ${String(groupIndex + 1).padStart(2, "0")} · BYE ${String(matchIndex + 1).padStart(2, "0")}`;
        const locationTitle = `${round.roundLabel || round.roundKey} · ${group.groupName || group.groupKey} · ${match.matchLabel || match.matchKey}`;
        const targetCount = sourceDependents(match.matchKey, null).length;
        const hiddenSlot = (slot, slotIndex) => `<div data-slot-index="${slotIndex}">
            <input type="hidden" value="${attr(slot.sourceType)}" data-slot-field="sourceType" />
            ${slot.sourceType === "SEED" ? `<input type="hidden" value="${attr(slot.seedNumber)}" data-slot-field="seedNumber" />` : ""}
        </div>`;
        return `<article class="bte-match bte-bye-pass" data-bye-pass-card data-match-index="${matchIndex}" data-match-key="${attr(match.matchKey)}" data-match-order="${String(matchIndex + 1).padStart(2, "0")}" style="${positionStyle}">
            <button class="bte-match__drag-handle" type="button" data-match-drag-handle title="${attr(locationTitle)} — Giữ và kéo để di chuyển card BYE">
                <i class="fas fa-forward"></i><span>${esc(locationLabel)}</span>
            </button>
            <div class="bte-bye-pass__data d-none">
                <input type="hidden" value="${attr(match.matchKey)}" data-match-field="matchKey" />
                <input type="hidden" value="${attr(match.matchLabel || "")}" data-match-field="matchLabel" />
                <input type="hidden" value="${match.sortOrder ?? matchIndex}" data-match-field="sortOrder" />
                <input type="checkbox" data-match-field="isTerminal"${match.isTerminal ? " checked" : ""} />
                <input type="hidden" value="${attr(match.terminalType || "")}" data-match-field="terminalType" />
                ${info.slots.map(hiddenSlot).join("")}
            </div>
            <div class="bte-bye-pass__head">
                <span class="bte-bye-pass__badge"><i class="fas fa-forward"></i>Suất BYE</span>
                <div class="bte-match__actions">
                    <button class="bte-icon-button bte-position-drag" type="button" data-match-drag-handle data-match-layout-key="${attr(match.matchKey)}" title="Giữ để kéo card; nhấp đúp để đưa về vị trí mặc định" aria-label="Kéo card BYE"><i class="fas fa-arrows-alt"></i></button>
                    <button class="bte-icon-button text-primary" type="button" data-action="edit-match" data-round="${roundIndex}" data-group="${groupIndex}" data-match="${matchIndex}" title="Sửa nguồn của suất BYE" aria-label="Sửa suất BYE"${disabled()}><i class="fas fa-pen"></i></button>
                    <button class="bte-icon-button text-danger" type="button" data-action="delete-match" data-round="${roundIndex}" data-group="${groupIndex}" data-match="${matchIndex}" title="Xóa suất BYE" aria-label="Xóa suất BYE"${disabled()}><i class="fas fa-trash-alt"></i></button>
                </div>
            </div>
            <div class="bte-bye-pass__team">
                <span class="bte-bye-pass__number">${info.seedNumber}</span>
                <span><strong>Đội ban đầu ${info.seedNumber}</strong><small>${esc(match.matchKey)} · Không ghép cặp tại vòng này</small></span>
            </div>
            <div class="bte-bye-pass__route ${targetCount ? "is-connected" : ""}">
                <span><i class="fas ${targetCount ? "fa-check-circle" : "fa-plug"}"></i>${targetCount ? `Đã nối ${targetCount} đích` : "Chưa nối vòng đích"}</span>
                <button class="bte-connection-source is-bye-pass" type="button" data-connection-source="WINNER_MATCH" data-source-match-key="${attr(match.matchKey)}" title="Kéo đội ban đầu ${info.seedNumber} tới một vòng đứng sau"${disabled()}><i></i>BYE · Đi tiếp</button>
            </div>
        </article>`;
    }

    function renderMatch(match, roundIndex, groupIndex, matchIndex) {
        const byePass = byePassThroughInfo(match);
        if (byePass) return renderByePassMatch(match, roundIndex, groupIndex, matchIndex, byePass);
        const slots = [...(match.slots || [])].sort((a, b) => a.slotNumber - b.slotNumber);
        while (slots.length < 2) slots.push({ slotNumber: slots.length + 1, sourceType: "SEED", seedNumber: null });
        const round = state.graph.rounds[roundIndex];
        const group = round.groups[groupIndex];
        const layout = matchLayout(match.matchKey);
        const positionStyle = `--bte-match-x:${layout.x}px;--bte-match-y:${layout.y}px`;
        const locationLabel = `Vòng ${String(roundIndex + 1).padStart(2, "0")} · Nhánh ${String(groupIndex + 1).padStart(2, "0")} · Trận ${String(matchIndex + 1).padStart(2, "0")}`;
        const locationTitle = `${round.roundLabel || round.roundKey} · ${group.groupName || group.groupKey} · ${match.matchLabel || match.matchKey}`;
        return `<article class="bte-match" data-match-index="${matchIndex}" data-match-key="${attr(match.matchKey)}" data-match-order="${String(matchIndex + 1).padStart(2, "0")}" style="${positionStyle}">
            <button class="bte-match__drag-handle" type="button" data-match-drag-handle title="${attr(locationTitle)} — Giữ và kéo để di chuyển card trận">
                <i class="fas fa-grip-vertical"></i><span>${esc(locationLabel)}</span>
            </button>
            <div class="bte-match__head">
                <label class="bte-control-field bte-match__key-field">
                    <span class="bte-field-caption">Mã trận</span>
                    <input class="bte-key" value="${attr(match.matchKey)}" data-match-field="matchKey" aria-label="Mã trận"${disabled()} />
                </label>
                <label class="bte-control-field">
                    <span class="bte-field-caption">Tên hiển thị</span>
                    <input value="${attr(match.matchLabel || "")}" data-match-field="matchLabel" placeholder="Ví dụ: Bán kết 1" aria-label="Nhãn trận"${disabled()} />
                </label>
                <div class="bte-match__actions">
                    <button class="bte-icon-button bte-position-drag" type="button" data-match-drag-handle data-match-layout-key="${attr(match.matchKey)}" title="Giữ để kéo card; nhấp đúp để đưa về vị trí mặc định" aria-label="Kéo để di chuyển card trận"><i class="fas fa-arrows-alt"></i></button>
                    <button class="bte-icon-button text-primary" type="button" data-action="edit-match" data-round="${roundIndex}" data-group="${groupIndex}" data-match="${matchIndex}" title="Thiết kế chi tiết trận" aria-label="Thiết kế chi tiết trận"${disabled()}><i class="fas fa-pen"></i></button>
                    <button class="bte-icon-button text-danger" type="button" data-action="delete-match" data-round="${roundIndex}" data-group="${groupIndex}" data-match="${matchIndex}" title="Xóa trận" aria-label="Xóa trận"${disabled()}><i class="fas fa-trash-alt"></i></button>
                </div>
            </div>
            <div class="bte-match__slots">
                ${slots.slice(0, 2).map((slot, index) => renderSlot(slot, match.matchKey, index)).join("")}
            </div>
            <div class="bte-match__footer">
                <label class="bte-terminal-toggle"><input type="checkbox" data-match-field="isTerminal"${match.isTerminal ? " checked" : ""}${disabled()} /><span>Trận cuối nhánh</span></label>
                <label class="bte-control-field bte-terminal-type">
                    <span class="bte-field-caption">Kết quả cuối</span>
                    <select data-match-field="terminalType" aria-label="Loại trận kết thúc"${disabled()}>
                        ${option("", "Không áp dụng", match.terminalType || "")}${option("CHAMPION", "Vô địch", match.terminalType)}${option("THIRD_PLACE", "Hạng ba", match.terminalType)}${option("PLACEMENT", "Xếp hạng", match.terminalType)}
                    </select>
                </label>
                <input type="hidden" value="${match.sortOrder ?? matchIndex}" data-match-field="sortOrder" />
            </div>
            <div class="bte-match__connection-ports">
                <span><i class="fas fa-link"></i>Kéo nối</span>
                <button class="bte-connection-source is-winner" type="button" data-connection-source="WINNER_MATCH" data-source-match-key="${attr(match.matchKey)}" title="Kéo đội thắng ${attr(match.matchKey)} sang một nguồn đội"${disabled()}><i></i>Thắng</button>
                <button class="bte-connection-source is-loser" type="button" data-connection-source="LOSER_MATCH" data-source-match-key="${attr(match.matchKey)}" title="Kéo đội thua ${attr(match.matchKey)} sang một nguồn đội"${disabled()}><i></i>Thua</button>
            </div>
        </article>`;
    }

    function renderGroup(group, roundIndex, groupIndex) {
        const groupColor = normalizeGroupColor(group.groupColor);
        const round = state.graph.rounds[roundIndex];
        const byePassCount = (group.matches || []).filter((match) => byePassThroughInfo(match)).length;
        const regularMatchCount = (group.matches || []).length - byePassCount;
        const byeUnavailable = normalizeSourceKey(round?.roundType) === "GROUP_STAGE";
        return `<section class="bte-group" data-group-index="${groupIndex}" data-group-key="${attr(group.groupKey)}" style="${attr(groupThemeStyle(groupColor))}">
            <div class="bte-group__head">
                <div class="bte-group__titlebar">
                    <span><i class="fas fa-layer-group"></i>Bảng / nhánh</span>
                    <button class="bte-icon-button text-danger" type="button" data-action="delete-group" data-round="${roundIndex}" data-group="${groupIndex}" title="Xóa bảng hoặc nhánh" aria-label="Xóa bảng hoặc nhánh"${disabled()}><i class="fas fa-trash-alt"></i></button>
                </div>
                <div class="bte-group__identity">
                    <label class="bte-control-field bte-group__key-field">
                        <span class="bte-field-caption">Mã nhánh</span>
                        <input class="bte-key" value="${attr(group.groupKey)}" data-group-field="groupKey" aria-label="Mã bảng"${disabled()} />
                    </label>
                    <label class="bte-control-field">
                        <span class="bte-field-caption">Tên hiển thị</span>
                        <input value="${attr(group.groupName)}" data-group-field="groupName" aria-label="Tên bảng"${disabled()} />
                    </label>
                </div>
                <div class="bte-group__meta">
                    <label class="bte-control-field">
                        <span class="bte-field-caption">Loại bảng / nhánh</span>
                        <select data-group-field="groupType" aria-label="Loại bảng"${disabled()}>
                            ${option("GENERIC", "Nhánh thường", group.groupType)}${option("ROUND_ROBIN", "Vòng tròn", group.groupType)}${option("KNOCKOUT_BRANCH", "Nhánh loại trực tiếp", group.groupType)}${option("FINAL", "Chung kết", group.groupType)}${option("PLACEMENT", "Xếp hạng", group.groupType)}
                        </select>
                    </label>
                    <label class="bte-control-field bte-group-color-field">
                        <span class="bte-field-caption">Màu nhánh</span>
                        <input class="bte-group-color-picker" type="color" value="${groupColor}" data-group-field="groupColor" aria-label="Màu nhánh" title="Màu nhánh ${groupColor}"${disabled()} />
                    </label>
                    <label class="bte-control-field bte-order-field">
                        <span class="bte-field-caption">Thứ tự</span>
                        <input type="number" value="${group.sortOrder ?? groupIndex}" data-group-field="sortOrder" aria-label="Thứ tự bảng"${disabled()} />
                    </label>
                </div>
            </div>
            <div class="bte-group__matches">
                <div class="bte-group__match-list-head">
                    <span><i class="fas fa-th-large"></i>Các trận đấu</span>
                    <small>${regularMatchCount} trận${byePassCount ? ` · ${byePassCount} BYE` : ""}</small>
                </div>
                ${(group.matches || []).map((match, matchIndex) => renderMatch(match, roundIndex, groupIndex, matchIndex)).join("")}
                <div class="bte-group__match-actions" data-follow-group-actions>
                    <button class="bte-add" type="button" data-action="add-match" data-round="${roundIndex}" data-group="${groupIndex}"${disabled()}><i class="fas fa-plus mr-1"></i>Thêm một trận</button>
                    <button class="bte-add bte-add--bulk" type="button" data-action="add-matches-bulk" data-round="${roundIndex}" data-group="${groupIndex}"${disabled()}><i class="fas fa-copy mr-1"></i>Tạo hàng loạt</button>
                    <button class="bte-add bte-add--pair" type="button" data-action="quick-pair" data-round="${roundIndex}" data-group="${groupIndex}"${disabled()}><i class="fas fa-random mr-1"></i>Ghép cặp nhanh từ trận trước</button>
                    <button class="bte-add bte-add--bye" type="button" data-action="add-bye-pass" data-round="${roundIndex}" data-group="${groupIndex}"${byeUnavailable ? " data-disabled-always disabled title=\"BYE không áp dụng cho vòng bảng\"" : disabled()}><i class="fas fa-forward mr-1"></i>Thêm BYE</button>
                </div>
            </div>
        </section>`;
    }

    function renderRound(round, roundIndex) {
        const layout = roundLayout(round.roundKey);
        const positionStyle = `--bte-round-x:${layout.x}px`;
        const initialTeamSlotCount = (round.groups || []).flatMap((group) => group.matches || [])
            .flatMap((match) => match.slots || [])
            .filter((slot) => normalizeSourceKey(slot.sourceType) === "SEED").length;
        const numberingUnavailable = normalizeSourceKey(round.roundType) === "GROUP_STAGE" || initialTeamSlotCount === 0;
        return `<section class="bte-round" data-round-index="${roundIndex}" data-round-key="${attr(round.roundKey)}" style="${positionStyle}">
            <div class="bte-round__head">
                <div class="bte-round__titlebar" data-round-drag-handle title="Giữ và kéo sang trái hoặc phải để di chuyển toàn bộ vòng">
                    <span><i class="fas fa-arrows-alt-h"></i>Vòng ${String(roundIndex + 1).padStart(2, "0")}</span>
                    <div class="bte-round__title-actions">
                        <button class="bte-round-numbering" type="button" data-action="number-initial-teams" data-round="${roundIndex}"${numberingUnavailable ? " data-disabled-always disabled" : disabled()} title="${numberingUnavailable ? "Vòng này không có vị trí Đội ban đầu phù hợp để đánh số" : "Tự đánh số các vị trí Đội ban đầu trong vòng này"}">
                            <i class="fas fa-list-ol"></i><span>Đánh số đội</span>
                        </button>
                        <button class="bte-round-audit-visibility d-none" type="button" data-action="toggle-advance-colors" data-round="${roundIndex}" title="Ẩn màu cảnh báo của vòng này">
                            <i class="fas fa-eye-slash"></i><span>Ẩn màu</span>
                        </button>
                        <div class="dropdown">
                            <button class="bte-round-audit dropdown-toggle" type="button" data-toggle="dropdown" data-action="round-audit-menu" aria-haspopup="true" aria-expanded="false" title="Kiểm tra đội thắng hoặc đội thua chưa được đi tiếp">
                                <i class="fas fa-route"></i><span>Kiểm tra</span>
                            </button>
                            <div class="dropdown-menu dropdown-menu-right bte-round-audit-menu">
                                <h6 class="dropdown-header">Kiểm tra đội chưa đi tiếp</h6>
                                <button class="dropdown-item" type="button" data-action="audit-advancement" data-round="${roundIndex}" data-source-type="WINNER_MATCH">
                                    <i class="fas fa-trophy fa-fw mr-2"></i>Đội thắng
                                </button>
                                <button class="dropdown-item" type="button" data-action="audit-advancement" data-round="${roundIndex}" data-source-type="LOSER_MATCH">
                                    <i class="fas fa-level-down-alt fa-fw mr-2"></i>Đội thua
                                </button>
                            </div>
                        </div>
                        <button class="bte-icon-button" type="button" data-action="delete-round" data-round="${roundIndex}" title="Xóa vòng" aria-label="Xóa vòng"${disabled()}><i class="fas fa-trash-alt"></i></button>
                    </div>
                </div>
                <div class="bte-round__head-row">
                    <label class="bte-control-field bte-round__key-field">
                        <span class="bte-field-caption">Mã vòng</span>
                        <input class="bte-key" value="${attr(round.roundKey)}" data-round-field="roundKey" aria-label="Mã vòng"${disabled()} />
                    </label>
                    <label class="bte-control-field">
                        <span class="bte-field-caption">Tên vòng đấu</span>
                        <input class="bte-label" value="${attr(round.roundLabel)}" data-round-field="roundLabel" aria-label="Tên vòng"${disabled()} />
                    </label>
                </div>
                <div class="bte-round__meta">
                    <label class="bte-control-field">
                        <span class="bte-field-caption">Loại vòng</span>
                        <select data-round-field="roundType" aria-label="Loại vòng"${disabled()}>
                            ${option("GROUP_STAGE", "Vòng bảng", round.roundType)}${option("KNOCKOUT", "Loại trực tiếp", round.roundType)}${option("FINAL", "Chung kết", round.roundType)}${option("PLACEMENT", "Xếp hạng", round.roundType)}${option("LOSER_BRACKET", "Nhánh thua", round.roundType)}
                        </select>
                    </label>
                    <label class="bte-control-field bte-order-field">
                        <span class="bte-field-caption">Thứ tự</span>
                        <input type="number" value="${round.sortOrder ?? roundIndex}" data-round-field="sortOrder" aria-label="Thứ tự vòng"${disabled()} />
                    </label>
                </div>
            </div>
            <div class="bte-round__body">
                ${(round.groups || []).map((group, groupIndex) => renderGroup(group, roundIndex, groupIndex)).join("")}
                <button class="bte-add bte-round__add-group" type="button" data-action="add-group" data-round="${roundIndex}" data-follow-round-actions${disabled()}><i class="fas fa-plus mr-1"></i>Thêm bảng / nhánh</button>
            </div>
        </section>`;
    }

    let connectorFrame;
    let connectionDrag = null;
    let matchPositionDrag = null;
    let roundPositionDrag = null;
    let roundAutoScrollFrame = null;
    let suppressRoundFocusUntil = 0;
    function scheduleConnectors() {
        cancelAnimationFrame(connectorFrame);
        connectorFrame = requestAnimationFrame(drawConnectors);
    }

    function clampCanvasZoom(value) {
        return Math.max(.5, Math.min(1.25, Math.round(value * 20) / 20));
    }

    function canvasNaturalSize(includeRightWorkspace = true) {
        const rounds = [...roundsHost.children];
        const styles = getComputedStyle(roundsHost);
        const gap = Number.parseFloat(styles.columnGap || styles.gap) || 0;
        const contentWidth = rounds.reduce((sum, round) => sum + round.offsetWidth, 0)
            + Math.max(0, rounds.length - 1) * gap;
        const contentHeight = rounds.reduce((height, round) => Math.max(height, round.offsetHeight), 0);
        const rightWorkspace = includeRightWorkspace && rounds.length && canvasScroll
            ? Math.max(1200, canvasScroll.clientWidth / state.canvasZoom)
            : 0;
        const size = {
            width: Math.max(360, contentWidth + 32 + rightWorkspace),
            height: Math.max(630, contentHeight + 32)
        };
        if (canvasStage && rounds.length) {
            const stageRect = canvasStage.getBoundingClientRect();
            root.querySelectorAll("[data-round-key], [data-match-key], [data-follow-group-actions], [data-follow-round-actions]").forEach((element) => {
                const rect = element.getBoundingClientRect();
                size.width = Math.max(
                    size.width,
                    (rect.right - stageRect.left) / state.canvasZoom + 24 + rightWorkspace);
                size.height = Math.max(size.height, (rect.bottom - stageRect.top) / state.canvasZoom + 24);
            });
        }
        return size;
    }

    function syncCanvasStage() {
        if (!canvasScroll || !canvasViewport || !canvasStage) return { width: 0, height: 0 };
        const contentSize = canvasNaturalSize();
        const size = {
            width: Math.max(contentSize.width, canvasScroll.clientWidth / state.canvasZoom),
            height: Math.max(contentSize.height, canvasScroll.clientHeight / state.canvasZoom)
        };
        canvasStage.style.width = `${size.width}px`;
        canvasStage.style.height = `${size.height}px`;
        canvasStage.style.transform = `scale(${state.canvasZoom})`;
        canvasViewport.style.width = `${Math.max(canvasScroll.clientWidth, size.width * state.canvasZoom)}px`;
        canvasViewport.style.height = `${Math.max(canvasScroll.clientHeight, size.height * state.canvasZoom)}px`;
        const svg = root.querySelector("[data-connectors]");
        svg?.setAttribute("width", size.width);
        svg?.setAttribute("height", size.height);
        svg?.setAttribute("viewBox", `0 0 ${size.width} ${size.height}`);
        return size;
    }

    function updateCanvasViewControls() {
        const label = root.querySelector("[data-graph-zoom-label]");
        if (label) label.textContent = `${Math.round(state.canvasZoom * 100)}%`;
        const focus = root.querySelector("[data-action='toggle-canvas-focus']");
        if (focus) {
            focus.classList.toggle("is-active", state.canvasFocus);
            focus.innerHTML = state.canvasFocus
                ? '<i class="fas fa-compress mr-1"></i><span>Thu lại</span>'
                : '<i class="fas fa-expand mr-1"></i><span>Mở rộng</span>';
        }
    }

    function setCanvasZoom(value, keepCenter = true) {
        if (!canvasScroll) return;
        const previousZoom = state.canvasZoom;
        const centerX = (canvasScroll.scrollLeft + canvasScroll.clientWidth / 2) / previousZoom;
        const centerY = (canvasScroll.scrollTop + canvasScroll.clientHeight / 2) / previousZoom;
        state.canvasZoom = clampCanvasZoom(value);
        try { localStorage.setItem(canvasZoomStorageKey, String(state.canvasZoom)); } catch (_) { }
        updateCanvasViewControls();
        syncCanvasStage();
        if (keepCenter) {
            canvasScroll.scrollLeft = Math.max(0, centerX * state.canvasZoom - canvasScroll.clientWidth / 2);
            canvasScroll.scrollTop = Math.max(0, centerY * state.canvasZoom - canvasScroll.clientHeight / 2);
        }
        scheduleConnectors();
    }

    function fitCanvasWidth() {
        if (!canvasScroll) return;
        const size = canvasNaturalSize(false);
        setCanvasZoom(Math.min(1, (canvasScroll.clientWidth - 24) / size.width), false);
        canvasScroll.scrollTo({ left: 0, top: 0, behavior: "smooth" });
    }

    function toggleCanvasFocus() {
        state.canvasFocus = !state.canvasFocus;
        root.classList.toggle("is-canvas-focus", state.canvasFocus);
        updateCanvasViewControls();
        requestAnimationFrame(() => {
            syncCanvasStage();
            scheduleConnectors();
        });
    }

    function beginRoundPositionDrag(handle, event) {
        if (event.button !== 0 || !canvasStage || !canvasScroll || event.target.closest("[data-action]")) return;
        const round = handle.closest("[data-round-key]");
        const roundKey = round?.dataset.roundKey;
        if (!round || !roundKey) return;
        event.preventDefault();
        event.stopPropagation();
        const initial = roundLayout(roundKey);
        const roundRect = round.getBoundingClientRect();
        const stageRect = canvasStage.getBoundingClientRect();
        const baseLeft = (roundRect.left - stageRect.left) / state.canvasZoom - initial.x;
        const start = {
            clientX: event.clientX,
            scrollLeft: canvasScroll.scrollLeft
        };
        let moved = false;
        roundPositionDrag = { round, handle, roundKey, initial, baseLeft, start };
        round.classList.add("is-round-positioning");
        handle.setPointerCapture(event.pointerId);

        let pointerX = event.clientX;
        const updatePosition = () => {
            const scrollX = (canvasScroll.scrollLeft - start.scrollLeft) / state.canvasZoom;
            setRoundLayout(roundKey, {
                x: Math.max(16 - baseLeft, initial.x + (pointerX - start.clientX) / state.canvasZoom + scrollX)
            });
            round.style.setProperty("--bte-round-x", `${roundLayout(roundKey).x}px`);
            syncCanvasStage();
            scheduleConnectors();
        };
        const autoScroll = () => {
            if (!roundPositionDrag || roundPositionDrag.round !== round) return;
            const scrollRect = canvasScroll.getBoundingClientRect();
            const edge = 64;
            const maxSpeed = 24;
            let delta = 0;
            if (pointerX < scrollRect.left + edge) {
                delta = -Math.ceil(maxSpeed * Math.min(1, (scrollRect.left + edge - pointerX) / edge));
            } else if (pointerX > scrollRect.right - edge) {
                delta = Math.ceil(maxSpeed * Math.min(1, (pointerX - scrollRect.right + edge) / edge));
            }
            if (delta) {
                const previousScroll = canvasScroll.scrollLeft;
                canvasScroll.scrollLeft += delta;
                if (canvasScroll.scrollLeft !== previousScroll) updatePosition();
            }
            roundAutoScrollFrame = requestAnimationFrame(autoScroll);
        };
        const move = (moveEvent) => {
            if (Math.abs(moveEvent.clientX - start.clientX) > 4) moved = true;
            pointerX = moveEvent.clientX;
            updatePosition();
        };
        const stop = (stopEvent) => {
            handle.removeEventListener("pointermove", move);
            handle.removeEventListener("pointerup", stop);
            handle.removeEventListener("pointercancel", stop);
            cancelAnimationFrame(roundAutoScrollFrame);
            roundAutoScrollFrame = null;
            round.classList.remove("is-round-positioning");
            roundPositionDrag = null;
            if (moved) {
                suppressRoundFocusUntil = performance.now() + 300;
            } else if (stopEvent.type === "pointerup") {
                suppressRoundFocusUntil = performance.now() + 300;
                setConnectorRoundFocus(roundKey);
            }
            persistRoundLayouts();
            syncCanvasStage();
            scheduleConnectors();
        };
        handle.addEventListener("pointermove", move);
        handle.addEventListener("pointerup", stop);
        handle.addEventListener("pointercancel", stop);
        roundAutoScrollFrame = requestAnimationFrame(autoScroll);
    }

    function beginMatchPositionDrag(handle, event) {
        if (event.button !== 0 || !canvasStage || !canvasScroll) return;
        const card = handle.closest("[data-match-key]");
        const matchKey = card?.dataset.matchKey;
        if (!card || !matchKey) return;
        event.preventDefault();
        event.stopPropagation();
        const initial = matchLayout(matchKey);
        const cardRect = card.getBoundingClientRect();
        const stageRect = canvasStage.getBoundingClientRect();
        const baseLeft = (cardRect.left - stageRect.left) / state.canvasZoom - initial.x;
        const baseTop = (cardRect.top - stageRect.top) / state.canvasZoom - initial.y;
        const start = {
            clientX: event.clientX,
            clientY: event.clientY,
            scrollLeft: canvasScroll.scrollLeft,
            scrollTop: canvasScroll.scrollTop
        };
        matchPositionDrag = { card, handle, matchKey, initial, baseLeft, baseTop, start };
        card.classList.add("is-positioning");
        handle.setPointerCapture(event.pointerId);

        const move = (moveEvent) => {
            const scrollRect = canvasScroll.getBoundingClientRect();
            const edge = 46;
            const speed = 16;
            if (moveEvent.clientX < scrollRect.left + edge) canvasScroll.scrollLeft -= speed;
            else if (moveEvent.clientX > scrollRect.right - edge) canvasScroll.scrollLeft += speed;
            if (moveEvent.clientY < scrollRect.top + edge) canvasScroll.scrollTop -= speed;
            else if (moveEvent.clientY > scrollRect.bottom - edge) canvasScroll.scrollTop += speed;

            const scrollX = (canvasScroll.scrollLeft - start.scrollLeft) / state.canvasZoom;
            const scrollY = (canvasScroll.scrollTop - start.scrollTop) / state.canvasZoom;
            const position = {
                x: Math.max(16 - baseLeft, initial.x + (moveEvent.clientX - start.clientX) / state.canvasZoom + scrollX),
                y: Math.max(16 - baseTop, initial.y + (moveEvent.clientY - start.clientY) / state.canvasZoom + scrollY)
            };
            setMatchLayout(matchKey, position);
            const next = matchLayout(matchKey);
            card.style.setProperty("--bte-match-x", `${next.x}px`);
            card.style.setProperty("--bte-match-y", `${next.y}px`);
            syncFollowingActions();
            scheduleConnectors();
        };
        const stop = () => {
            handle.removeEventListener("pointermove", move);
            handle.removeEventListener("pointerup", stop);
            handle.removeEventListener("pointercancel", stop);
            card.classList.remove("is-positioning");
            matchPositionDrag = null;
            persistMatchLayouts();
            scheduleConnectors();
        };
        handle.addEventListener("pointermove", move);
        handle.addEventListener("pointerup", stop);
        handle.addEventListener("pointercancel", stop);
    }

    function connectorCanvasPoint(element, side) {
        const stageRect = canvasStage.getBoundingClientRect();
        const rect = element.getBoundingClientRect();
        return {
            x: ((side === "right" ? rect.right : rect.left) - stageRect.left) / state.canvasZoom,
            y: (rect.top - stageRect.top + rect.height / 2) / state.canvasZoom
        };
    }

    function connectorSourceExitPoint(anchor, sourceCard) {
        const stageRect = canvasStage.getBoundingClientRect();
        const anchorRect = anchor.getBoundingClientRect();
        const cardRect = sourceCard.getBoundingClientRect();
        const stubLength = Math.max(2, (cardRect.bottom - anchorRect.bottom) / state.canvasZoom + 1);
        anchor.style.setProperty("--bte-source-stub-length", `${stubLength}px`);
        return {
            x: (anchorRect.left - stageRect.left + anchorRect.width / 2) / state.canvasZoom,
            y: (cardRect.bottom - stageRect.top) / state.canvasZoom + 1
        };
    }

    function clientToConnectorPoint(clientX, clientY) {
        const rect = canvasStage.getBoundingClientRect();
        return {
            x: (clientX - rect.left) / state.canvasZoom,
            y: (clientY - rect.top) / state.canvasZoom
        };
    }

    function connectorCurve(start, end) {
        const distance = end.x - start.x;
        const bend = Math.max(42, Math.abs(distance) * .42);
        const routeX = distance >= 36 ? null : Math.max(start.x, end.x) + 72;
        const control1 = { x: routeX ?? start.x + bend, y: start.y };
        const control2 = { x: routeX ?? end.x - bend, y: end.y };
        return {
            d: `M ${start.x} ${start.y} C ${control1.x} ${control1.y}, ${control2.x} ${control2.y}, ${end.x} ${end.y}`,
            label: {
                x: (start.x + 3 * control1.x + 3 * control2.x + end.x) / 8,
                y: (start.y + 3 * control1.y + 3 * control2.y + end.y) / 8
            }
        };
    }

    function hasConnectorFocus() {
        return !!state.connectorFocusMatchKey || !!state.connectorFocusRoundKey;
    }

    function connectorFocusMatchKeys() {
        const keys = new Set();
        if (state.connectorFocusMatchKey) keys.add(state.connectorFocusMatchKey);
        if (!state.connectorFocusRoundKey || !state.graph) return keys;
        const round = state.graph.rounds.find((item) => item.roundKey === state.connectorFocusRoundKey);
        round?.groups.forEach((group) => group.matches.forEach((match) => keys.add(match.matchKey)));
        return keys;
    }

    function updateConnectorFocusCards() {
        const focusedMatchKeys = connectorFocusMatchKeys();
        root.querySelectorAll("[data-match-key]").forEach((card) => {
            const focused = focusedMatchKeys.has(card.dataset.matchKey);
            card.classList.toggle("is-connection-focus", focused);
            card.classList.remove("is-connector-obscured");
        });
        root.querySelectorAll("[data-round-key]").forEach((round) => {
            round.classList.toggle("is-connection-focus",
                !!state.connectorFocusRoundKey && round.dataset.roundKey === state.connectorFocusRoundKey);
        });
        root.classList.toggle("has-connector-focus", hasConnectorFocus());
    }

    function setConnectorFocus(matchKey) {
        state.connectorFocusMatchKey = matchKey || null;
        state.connectorFocusRoundKey = null;
        updateConnectorFocusCards();
        scheduleConnectors();
    }

    function setConnectorRoundFocus(roundKey) {
        const nextRoundKey = roundKey && state.connectorFocusRoundKey !== roundKey ? roundKey : null;
        state.connectorFocusMatchKey = null;
        state.connectorFocusRoundKey = nextRoundKey;
        updateConnectorFocusCards();
        scheduleConnectors();
    }

    function dimCardsCoveredByFocusedConnectors(svg) {
        root.querySelectorAll("[data-match-key]").forEach((card) => card.classList.remove("is-connector-obscured"));
        if (!hasConnectorFocus() || !canvasStage) return;

        const stageRect = canvasStage.getBoundingClientRect();
        const connectors = [...svg.querySelectorAll(".bte-connector[data-source-match-key][data-target-match-key]")];
        const focusedEndpointKeys = new Set(connectors.flatMap((connector) => [
            connector.dataset.sourceMatchKey,
            connector.dataset.targetMatchKey
        ]).filter(Boolean));
        connectorFocusMatchKeys().forEach((matchKey) => focusedEndpointKeys.add(matchKey));
        const candidates = [...root.querySelectorAll("[data-match-key]")]
            .map((card) => {
                const rect = card.getBoundingClientRect();
                return {
                    card,
                    left: (rect.left - stageRect.left) / state.canvasZoom + 4,
                    right: (rect.right - stageRect.left) / state.canvasZoom - 4,
                    top: (rect.top - stageRect.top) / state.canvasZoom + 4,
                    bottom: (rect.bottom - stageRect.top) / state.canvasZoom - 4
                };
            });
        if (!candidates.length) return;

        connectors.forEach((connector) => {
            const path = connector.querySelector(".bte-connector-line");
            if (!path || typeof path.getTotalLength !== "function") return;
            const length = path.getTotalLength();
            const step = Math.max(4, Math.min(10, length / 80));
            for (let distance = 0; distance <= length; distance += step) {
                const point = path.getPointAtLength(distance);
                candidates.forEach((candidate) => {
                    if (!focusedEndpointKeys.has(candidate.card.dataset.matchKey)
                        && !candidate.card.classList.contains("is-connector-obscured")
                        && point.x >= candidate.left && point.x <= candidate.right
                        && point.y >= candidate.top && point.y <= candidate.bottom) {
                        candidate.card.classList.add("is-connector-obscured");
                    }
                });
            }
        });
    }

    function drawConnectors() {
        if (!state.graph) return;
        const svg = root.querySelector("[data-connectors]");
        syncCanvasStage();

        const findByKey = (selector, key, dataName) => [...root.querySelectorAll(selector)]
            .find((element) => element.dataset[dataName] === key);
        if (state.connectorFocusMatchKey
            && !findByKey("[data-match-key]", state.connectorFocusMatchKey, "matchKey")) {
            state.connectorFocusMatchKey = null;
        }
        if (state.connectorFocusRoundKey
            && !findByKey("[data-round-key]", state.connectorFocusRoundKey, "roundKey")) {
            state.connectorFocusRoundKey = null;
        }
        const focusActive = hasConnectorFocus();
        const focusedMatchKeys = connectorFocusMatchKeys();
        const graphMatchByKey = new Map(graphLocations().matches
            .map((location) => [normalizeSourceKey(location.match.matchKey), location.match]));
        updateConnectorFocusCards();
        root.querySelectorAll("[data-connection-source].has-connector-stub")
            .forEach((anchor) => anchor.classList.remove("has-connector-stub"));
        const paths = [`<defs>
            <marker id="bte-arrow-winner" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto" markerUnits="userSpaceOnUse"><path class="bte-marker-shape is-winner" d="M0,0 L8,4 L0,8 Z" /></marker>
            <marker id="bte-arrow-loser" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto" markerUnits="userSpaceOnUse"><path class="bte-marker-shape is-loser" d="M0,0 L8,4 L0,8 Z" /></marker>
            <marker id="bte-arrow-group" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto" markerUnits="userSpaceOnUse"><path class="bte-marker-shape is-group" d="M0,0 L8,4 L0,8 Z" /></marker>
            <marker id="bte-arrow-bye" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto" markerUnits="userSpaceOnUse"><path class="bte-marker-shape is-bye" d="M0,0 L8,4 L0,8 Z" /></marker>
        </defs>`];
        state.graph.rounds.forEach((round) => round.groups.forEach((group) => group.matches.forEach((match) => {
            const targetMatch = findByKey("[data-match-key]", match.matchKey, "matchKey");
            if (!targetMatch) return;
            (match.slots || []).forEach((slot, slotIndex) => {
                const targetSlot = targetMatch.querySelector(`[data-slot-index='${slotIndex}']`);
                if (!targetSlot) return;
                let source = null;
                let pathClass = "";
                let sourceKey = "";
                let sourceLabel = "";
                if (slot.sourceType === "WINNER_MATCH" || slot.sourceType === "LOSER_MATCH") {
                    source = findByKey("[data-match-key]", slot.sourceMatchKey, "matchKey");
                    pathClass = slot.sourceType === "LOSER_MATCH" ? "is-loser" : "is-winner";
                    sourceKey = slot.sourceMatchKey || "?";
                    sourceLabel = slot.sourceType === "LOSER_MATCH" ? `Thua ${sourceKey}` : `Thắng ${sourceKey}`;
                    const sourceByePass = slot.sourceType === "WINNER_MATCH"
                        ? byePassThroughInfo(graphMatchByKey.get(normalizeSourceKey(sourceKey)))
                        : null;
                    if (sourceByePass) {
                        pathClass = "is-bye";
                        sourceLabel = `BYE · Đội ${sourceByePass.seedNumber}`;
                    }
                } else if (slot.sourceType === "GROUP_RANK") {
                    source = findByKey("[data-group-key]", slot.sourceGroupKey, "groupKey");
                    pathClass = "is-group";
                    sourceKey = slot.sourceGroupKey || "?";
                    sourceLabel = `Hạng ${slot.sourceRank || "?"} · ${sourceKey}`;
                }
                if (!source) return;
                const sourceMatchKey = slot.sourceType === "GROUP_RANK" ? "" : sourceKey;
                const sourceRoundKey = source.closest("[data-round-key]")?.dataset.roundKey || "";
                if (focusActive
                    && !focusedMatchKeys.has(match.matchKey)
                    && !focusedMatchKeys.has(sourceMatchKey)
                    && sourceRoundKey !== state.connectorFocusRoundKey) return;
                const sourceAnchor = slot.sourceType === "GROUP_RANK"
                    ? source.querySelector(".bte-group__head") || source
                    : source.querySelector(`[data-connection-source='${slot.sourceType}']`) || source;
                const targetAnchor = targetSlot.querySelector("[data-connection-target]") || targetSlot;
                const start = slot.sourceType === "GROUP_RANK"
                    ? connectorCanvasPoint(sourceAnchor, "right")
                    : connectorSourceExitPoint(sourceAnchor, source);
                if (slot.sourceType !== "GROUP_RANK") sourceAnchor.classList.add("has-connector-stub");
                const end = connectorCanvasPoint(targetAnchor, "left");
                const curve = connectorCurve(start, end);
                const marker = pathClass === "is-loser"
                    ? "bte-arrow-loser"
                    : pathClass === "is-group"
                        ? "bte-arrow-group"
                        : pathClass === "is-bye"
                            ? "bte-arrow-bye"
                            : "bte-arrow-winner";
                const labelWidth = Math.max(54, sourceLabel.length * 5.8 + 14);
                const title = `${sourceLabel} → ${match.matchLabel || match.matchKey} · Đội ${slot.slotNumber}`;
                paths.push(`<g class="bte-connector ${pathClass}" data-source-match-key="${attr(sourceMatchKey)}" data-target-match-key="${attr(match.matchKey)}">
                    <title>${esc(title)}</title>
                    <path class="bte-connector-halo" d="${curve.d}" />
                    <path class="bte-connector-line ${pathClass}" d="${curve.d}" marker-end="url(#${marker})" />
                    <circle class="bte-connector-dot is-source ${pathClass}" cx="${start.x}" cy="${start.y}" r="4" />
                    <circle class="bte-connector-dot is-target ${pathClass}" cx="${end.x}" cy="${end.y}" r="4" />
                    <g class="bte-connector-label ${pathClass}" transform="translate(${curve.label.x},${curve.label.y})">
                        <rect x="${-labelWidth / 2}" y="-10" width="${labelWidth}" height="20" rx="7" />
                        <text text-anchor="middle" dominant-baseline="central">${esc(sourceLabel)}</text>
                    </g>
                </g>`);
            });
        })));
        svg.innerHTML = paths.join("");
        dimCardsCoveredByFocusedConnectors(svg);
    }

    function connectionTargetAt(clientX, clientY) {
        const element = document.elementFromPoint(clientX, clientY);
        const slot = element?.closest("[data-slot-index]");
        return slot?.querySelector("[data-connection-target]") || null;
    }

    function connectionTargetLocation(port) {
        if (!port || !connectionDrag) return null;
        const matchKey = port.closest("[data-match-key]")?.dataset.matchKey;
        const target = graphLocations().matches.find((item) => item.match.matchKey === matchKey);
        if (!target || !comesBefore(connectionDrag.source, target)) return null;
        return { target, slotIndex: Number(port.closest("[data-slot-index]").dataset.slotIndex) };
    }

    function clearConnectionDrag() {
        root.classList.remove("is-connecting");
        root.querySelectorAll(".is-connection-valid,.is-connection-hover,.is-connection-source-active")
            .forEach((element) => element.classList.remove("is-connection-valid", "is-connection-hover", "is-connection-source-active"));
        root.querySelectorAll(".is-dragging-connector")
            .forEach((element) => element.classList.remove("is-dragging-connector"));
        root.querySelector("[data-drag-connector]")?.remove();
        connectionDrag = null;
    }

    function clearOtherByePassTargets(sourceKey, targetMatch, targetSlotIndex) {
        state.graph.rounds.forEach((round) => round.groups.forEach((group) => group.matches.forEach((match) => {
            (match.slots || []).forEach((slot, slotIndex) => {
                if (match === targetMatch && slotIndex === targetSlotIndex) return;
                if (normalizeSourceKey(slot.sourceType) !== "WINNER_MATCH"
                    || normalizeSourceKey(slot.sourceMatchKey) !== normalizeSourceKey(sourceKey)) return;
                slot.sourceType = "SEED";
                slot.seedNumber = null;
                slot.sourceMatchKey = null;
                slot.sourceGroupKey = null;
                slot.sourceRank = null;
            });
        })));
    }

    function beginConnectionDrag(handle, event) {
        if (state.readOnly || event.button !== 0) return;
        collectGraph();
        const sourceKey = handle.dataset.sourceMatchKey;
        const source = graphLocations().matches.find((item) => item.match.matchKey === sourceKey);
        if (!source) return;
        event.preventDefault();
        state.connectorFocusMatchKey = sourceKey;
        state.connectorFocusRoundKey = null;
        updateConnectorFocusCards();
        drawConnectors();
        const svg = root.querySelector("[data-connectors]");
        const sourceType = handle.dataset.connectionSource;
        const sourceByePass = sourceType === "WINNER_MATCH" ? byePassThroughInfo(source.match) : null;
        const pathClass = sourceByePass ? "is-bye" : sourceType === "LOSER_MATCH" ? "is-loser" : "is-winner";
        const marker = sourceByePass ? "bte-arrow-bye" : sourceType === "LOSER_MATCH" ? "bte-arrow-loser" : "bte-arrow-winner";
        const sourceCard = handle.closest("[data-match-key]");
        const start = connectorSourceExitPoint(handle, sourceCard);
        handle.classList.add("is-dragging-connector");
        svg.insertAdjacentHTML("beforeend", `<g data-drag-connector class="bte-connector-drag ${pathClass}">
            <path class="bte-connector-halo" d="M ${start.x} ${start.y} L ${start.x} ${start.y}" />
            <path class="bte-connector-line ${pathClass}" d="M ${start.x} ${start.y} L ${start.x} ${start.y}" marker-end="url(#${marker})" />
        </g>`);
        connectionDrag = { source, sourceKey, sourceType, start, handle };
        root.classList.add("is-connecting");
        handle.classList.add("is-connection-source-active");
        root.querySelectorAll("[data-connection-target]").forEach((port) => {
            const matchKey = port.closest("[data-match-key]")?.dataset.matchKey;
            const target = graphLocations().matches.find((item) => item.match.matchKey === matchKey);
            if (target && comesBefore(source, target)) port.classList.add("is-connection-valid");
        });
        handle.setPointerCapture(event.pointerId);

        const move = (moveEvent) => {
            const end = clientToConnectorPoint(moveEvent.clientX, moveEvent.clientY);
            const curve = connectorCurve(start, end);
            const drag = root.querySelector("[data-drag-connector]");
            drag?.querySelectorAll("path").forEach((path) => path.setAttribute("d", curve.d));
            root.querySelectorAll(".is-connection-hover").forEach((port) => port.classList.remove("is-connection-hover"));
            const port = connectionTargetAt(moveEvent.clientX, moveEvent.clientY);
            if (connectionTargetLocation(port)) port.classList.add("is-connection-hover");
        };
        const stop = (stopEvent) => {
            const port = connectionTargetAt(stopEvent.clientX, stopEvent.clientY);
            const location = connectionTargetLocation(port);
            if (location) {
                const slot = location.target.match.slots[location.slotIndex];
                const previousSlot = { ...slot };
                slot.sourceType = sourceType;
                slot.seedNumber = null;
                slot.sourceMatchKey = sourceKey;
                slot.sourceGroupKey = null;
                slot.sourceRank = null;
                const duplicate = duplicateMatchSource(location.target.match);
                if (duplicate) {
                    Object.assign(slot, previousSlot);
                    port.closest(".bte-slot")?.classList.add("is-duplicate-source");
                    setTimeout(() => port.closest(".bte-slot")?.classList.remove("is-duplicate-source"), 1800);
                    showMessage("error", duplicate.message);
                    location.duplicate = true;
                } else if (sourceByePass) {
                    clearOtherByePassTargets(sourceKey, location.target.match, location.slotIndex);
                }
            }
            handle.removeEventListener("pointermove", move);
            handle.removeEventListener("pointerup", stop);
            handle.removeEventListener("pointercancel", cancel);
            clearConnectionDrag();
            if (location && !location.duplicate) {
                render();
                markDirty();
                const sourceResult = sourceByePass
                    ? `suất BYE của Đội ban đầu ${sourceByePass.seedNumber}`
                    : sourceType === "LOSER_MATCH" ? "đội thua" : "đội thắng";
                showMessage("success", `Đã nối ${sourceResult} ${sourceByePass ? "" : sourceKey} vào nguồn đội ${location.slotIndex + 1}.`);
            } else {
                scheduleConnectors();
            }
        };
        const cancel = () => {
            handle.removeEventListener("pointermove", move);
            handle.removeEventListener("pointerup", stop);
            handle.removeEventListener("pointercancel", cancel);
            clearConnectionDrag();
            scheduleConnectors();
        };
        handle.addEventListener("pointermove", move);
        handle.addEventListener("pointerup", stop);
        handle.addEventListener("pointercancel", cancel);
    }

    function render() {
        if (!state.graph) return;
        roundsHost.innerHTML = state.graph.rounds.map(renderRound).join("");
        applyAdvanceAuditState();
        syncFollowingActions();
        root.querySelector("[data-empty-canvas]").classList.toggle("d-none", state.graph.rounds.length > 0);
        const groups = state.graph.rounds.reduce((sum, round) => sum + round.groups.length, 0);
        const matches = state.graph.rounds.reduce((sum, round) => sum + round.groups.reduce((groupSum, group) => groupSum + group.matches.length, 0), 0);
        root.querySelector("[data-graph-summary]").textContent = `${state.graph.rounds.length} vòng · ${groups} bảng/nhánh · ${matches} trận`;
        scheduleConnectors();
    }

    function readNumber(element, fallback = 0) {
        if (!element || element.value === "") return fallback;
        const value = Number(element?.value);
        return Number.isFinite(value) ? value : fallback;
    }

    function collectGraph() {
        if (!state.graph) return null;
        state.graph.rounds = [...roundsHost.querySelectorAll(":scope > [data-round-index]")].map((roundEl) => ({
            roundKey: roundEl.querySelector("[data-round-field='roundKey']").value.trim().toUpperCase(),
            roundLabel: roundEl.querySelector("[data-round-field='roundLabel']").value.trim(),
            roundType: roundEl.querySelector("[data-round-field='roundType']").value,
            sortOrder: readNumber(roundEl.querySelector("[data-round-field='sortOrder']")),
            groups: [...roundEl.querySelectorAll(":scope > .bte-round__body > [data-group-index]")].map((groupEl) => ({
                groupKey: groupEl.querySelector("[data-group-field='groupKey']").value.trim().toUpperCase(),
                groupName: groupEl.querySelector("[data-group-field='groupName']").value.trim(),
                groupType: groupEl.querySelector("[data-group-field='groupType']").value,
                groupColor: normalizeGroupColor(groupEl.querySelector("[data-group-field='groupColor']")?.value),
                sortOrder: readNumber(groupEl.querySelector("[data-group-field='sortOrder']")),
                matches: [...groupEl.querySelectorAll(":scope > .bte-group__matches > [data-match-index]")].map((matchEl) => ({
                    matchKey: matchEl.querySelector("[data-match-field='matchKey']").value.trim().toUpperCase(),
                    matchLabel: matchEl.querySelector("[data-match-field='matchLabel']").value.trim() || null,
                    sortOrder: readNumber(matchEl.querySelector("[data-match-field='sortOrder']")),
                    isTerminal: matchEl.querySelector("[data-match-field='isTerminal']").checked,
                    terminalType: matchEl.querySelector("[data-match-field='terminalType']").value || null,
                    slots: [...matchEl.querySelectorAll("[data-slot-index]")].map((slotEl, slotIndex) => {
                        const sourceType = slotEl.querySelector("[data-slot-field='sourceType']").value;
                        return {
                            slotNumber: slotIndex + 1,
                            sourceType,
                            seedNumber: sourceType === "SEED" ? readNumber(slotEl.querySelector("[data-slot-field='seedNumber']"), null) : null,
                            sourceMatchKey: ["WINNER_MATCH", "LOSER_MATCH"].includes(sourceType) ? slotEl.querySelector("[data-slot-field='sourceMatchKey']")?.value || null : null,
                            sourceGroupKey: sourceType === "GROUP_RANK" ? slotEl.querySelector("[data-slot-field='sourceGroupKey']")?.value || null : null,
                            sourceRank: sourceType === "GROUP_RANK" ? readNumber(slotEl.querySelector("[data-slot-field='sourceRank']"), null) : null
                        };
                    })
                }))
            }))
        }));
        return state.graph;
    }

    function payloadFromGraph() {
        collectGraph();
        return {
            minimumTeams: state.graph.minimumTeams,
            seedCapacity: state.graph.seedCapacity,
            allowBye: state.graph.allowBye,
            defaultSeedingMethod: state.graph.defaultSeedingMethod,
            rowVersion: state.graph.rowVersion,
            rounds: state.graph.rounds
        };
    }

    function bindEditorState() {
        root.querySelector("[data-version-label]").textContent = `Phiên bản ${state.graph.versionNumber} · ${versionStatusLabels[state.graph.status] || state.graph.status}`;
    }

    function renderValidation(validation) {
        state.validation = validation;
        root.querySelector("[data-error-count]").textContent = validation?.errorCount ?? 0;
        root.querySelector("[data-warning-count]").textContent = validation?.warningCount ?? 0;
        const issues = (validation?.issues || []).filter((item) => item.severity !== "INFO");
        const grouped = [];
        issues.forEach((issue) => {
            const key = `${issue.severity}|${issue.code}|${issue.message}`;
            let group = grouped.find((item) => item.key === key);
            if (!group) {
                group = { key, issue, count: 0, locations: [] };
                grouped.push(group);
            }
            group.count += 1;
            const location = [issue.matchKey || issue.groupKey || issue.roundKey, issue.slotNumber ? `Đội ${issue.slotNumber}` : null]
                .filter(Boolean).join(" · ");
            if (location) group.locations.push(location);
        });
        root.querySelector("[data-validation-empty]").classList.toggle("d-none", grouped.length > 0);
        root.querySelector("[data-validation-list]").innerHTML = grouped.map((group, index) => {
            const locations = [...new Set(group.locations)];
            const visibleLocations = locations.slice(0, 3).join(", ");
            const remaining = Math.max(0, locations.length - 3);
            return `<button class="bte-validation-item ${group.issue.severity === "WARNING" ? "is-warning" : ""}" type="button" data-action="focus-issue" data-issue="${index}">
                <strong>${esc(severityLabels[group.issue.severity] || group.issue.severity)}${group.count > 1 ? ` · ${group.count} vị trí` : ""}</strong>
                <span>${esc(group.issue.message)}</span>
                ${visibleLocations ? `<small>${esc(visibleLocations)}${remaining ? ` và ${remaining} vị trí khác` : ""}</small>` : ""}
            </button>`;
        }).join("");
        root.querySelector("[data-validation-list]")._issues = grouped.map((group) => group.issue);
    }

    async function loadGraph() {
        setBusy(true, true);
        try {
            state.graph = await api(`/api/admin/bracket-templates/versions/${versionId}`);
            state.advanceAudits = {};
            state.readOnly = state.graph.status !== "DRAFT";
            if (!state.readOnly) {
                try {
                    const recovery = JSON.parse(localStorage.getItem(draftStorageKey) || "null");
                    if (recovery?.rowVersion === state.graph.rowVersion && recovery?.graph) {
                        state.graph = recovery.graph;
                        state.dirty = true;
                        showMessage("success", `Đã khôi phục thay đổi chưa lưu trên trình duyệt lúc ${new Date(recovery.savedAt).toLocaleString("vi-VN")}.`);
                    }
                } catch (_) { clearLocalRecovery(); }
            }
            bindEditorState();
            render();
            markDirty(state.dirty);
            if (state.readOnly) {
                const save = root.querySelector("[data-action='save']");
                save.disabled = false;
                save.innerHTML = '<i class="fas fa-code-branch mr-2"></i>Tạo bản nháp mới';
                root.querySelector("[data-action='publish']").classList.add("d-none");
            }
            await validate(false);
        } catch (error) { showMessage("error", error.message); }
        finally { setBusy(false, false); }
    }

    async function validate(showSuccess = true) {
        const result = await api(`/api/admin/bracket-templates/versions/${versionId}/validate-draft`, {
            method: "POST", body: JSON.stringify(payloadFromGraph())
        });
        renderValidation(result.data);
        reflectValidationState(result.data);
        if (showSuccess) {
            const validation = result.data;
            showMessage(validation.isValid ? "success" : "error",
                validation.isValid
                    ? `Cấu trúc hợp lệ${validation.warningCount ? `, còn ${validation.warningCount} cảnh báo.` : "."}`
                    : `Cấu trúc còn ${validation.errorCount} lỗi cần xử lý.`);
        }
        return result.data;
    }

    async function save() {
        collectGraph();
        const duplicateLocation = firstDuplicateGraphSource();
        if (duplicateLocation) {
            const card = [...root.querySelectorAll("[data-match-key]")]
                .find((item) => item.dataset.matchKey === duplicateLocation.match.matchKey);
            card?.querySelectorAll("[data-slot-index]").forEach((slot) => slot.classList.add("is-duplicate-source"));
            card?.scrollIntoView({ behavior: "smooth", block: "center", inline: "center" });
            showMessage("error", `${duplicateLocation.match.matchKey}: ${duplicateLocation.duplicate.message}`);
            return false;
        }
        const badge = root.querySelector("[data-save-state]");
        badge.className = "bte-save-state is-saving";
        badge.innerHTML = "<i></i>Đang lưu...";
        try {
            const result = await api(`/api/admin/bracket-templates/versions/${versionId}`, {
                method: "PUT", body: JSON.stringify(payloadFromGraph())
            });
            state.graph = result.data;
            clearLocalRecovery();
            bindEditorState();
            render();
            markDirty(false);
            const validation = await validate(false);
            reflectValidationState(validation);
            showMessage("success", validation.isValid
                ? `Đã lưu bản nháp${validation.warningCount ? `; còn ${validation.warningCount} cảnh báo.` : "."}`
                : `Đã lưu bản nháp đang làm dở; còn ${validation.errorCount} lỗi cần xử lý trước khi xuất bản.`);
            return true;
        } catch (error) {
            if (error.code === "CONCURRENCY_CONFLICT") {
                badge.className = "bte-save-state is-dirty";
                badge.innerHTML = "<i></i>Có xung đột";
            }
            throw error;
        }
    }

    async function createDraftVersion() {
        const result = await api(`/api/admin/bracket-templates/${templateId}/versions`, { method: "POST", body: "{}" });
        versionId = result.data.bracketTemplateVersionId;
        location.href = `/BracketTemplates/Editor?templateId=${templateId}&versionId=${versionId}`;
    }

    async function publish() {
        const validation = await validate(false);
        if (!validation.isValid) {
            showMessage("error", `Không thể xuất bản khi còn ${validation.errorCount} lỗi.`);
            return;
        }
        if (state.dirty && !(await save())) return;
        const prompt = validation.warningCount
            ? `Cấu trúc còn ${validation.warningCount} cảnh báo. Xác nhận xuất bản và khóa phiên bản này?`
            : "Xác nhận xuất bản? Phiên bản đã xuất bản sẽ không thể chỉnh sửa.";
        if (!window.confirm(prompt)) return;
        await api(`/api/admin/bracket-templates/versions/${versionId}/publish`, { method: "POST", body: "{}" });
        showMessage("success", "Đã xuất bản mẫu sơ đồ thi đấu thành công.");
        await loadGraph();
    }

    function nextKey(prefix, values) {
        let number = values.length + 1;
        let key = `${prefix}${number}`;
        while (values.includes(key)) key = `${prefix}${++number}`;
        return key;
    }

    function addRound() {
        collectGraph();
        const key = nextKey("R", state.graph.rounds.map((x) => x.roundKey));
        state.graph.rounds.push({ roundKey: key, roundLabel: `Vòng ${state.graph.rounds.length + 1}`, roundType: "KNOCKOUT", sortOrder: state.graph.rounds.length, groups: [] });
        render(); markDirty();
    }

    function addGroup(roundIndex) {
        collectGraph();
        const allKeys = state.graph.rounds.flatMap((x) => x.groups.map((g) => g.groupKey));
        const key = nextKey("G", allKeys);
        const groups = state.graph.rounds[roundIndex].groups;
        groups.push({
            groupKey: key,
            groupName: `Nhánh ${groups.length + 1}`,
            groupType: "GENERIC",
            groupColor: groupColorPalette[allKeys.length % groupColorPalette.length],
            sortOrder: groups.length,
            matches: []
        });
        render(); markDirty();
    }

    function buildNewMatch(roundIndex, groupIndex) {
        const allKeys = state.graph.rounds.flatMap((x) => x.groups.flatMap((g) => g.matches.map((m) => m.matchKey)));
        const key = nextKey("M", allKeys);
        const matches = state.graph.rounds[roundIndex].groups[groupIndex].matches;
        const usedSeeds = new Set(state.graph.rounds
            .flatMap((round) => round.groups)
            .flatMap((group) => group.matches)
            .flatMap((match) => match.slots || [])
            .filter((slot) => slot.sourceType === "SEED" && Number(slot.seedNumber) > 0)
            .map((slot) => Number(slot.seedNumber)));
        const availableSeeds = [];
        for (let seed = 1; seed <= 1024 && availableSeeds.length < 2; seed += 1) {
            if (!usedSeeds.has(seed)) availableSeeds.push(seed);
        }
        return {
            matchKey: key, matchLabel: `Trận ${matches.length + 1}`, sortOrder: matches.length,
            isTerminal: false, terminalType: null,
            slots: [
                { slotNumber: 1, sourceType: "SEED", seedNumber: availableSeeds[0] ?? null },
                { slotNumber: 2, sourceType: "SEED", seedNumber: availableSeeds[1] ?? null }
            ]
        };
    }

    function cloneMatch(match) {
        return JSON.parse(JSON.stringify(match));
    }

    function matchEditorTarget(editor) {
        const round = state.graph.rounds[editor.roundIndex];
        const group = round.groups[editor.groupIndex];
        return { round, group, match: editor.draft };
    }

    function renderMatchEditor() {
        const editor = state.matchEditor;
        if (!editor || !matchEditorModal) return;
        const round = state.graph.rounds[editor.roundIndex];
        const group = round.groups[editor.groupIndex];
        const match = editor.draft;
        const slots = [...(match.slots || [])].sort((a, b) => a.slotNumber - b.slotNumber);
        while (slots.length < 2) slots.push({ slotNumber: slots.length + 1, sourceType: "SEED", seedNumber: null });
        match.slots = slots.slice(0, 2);

        matchEditorModal.querySelector("[data-match-editor-title]").textContent =
            editor.matchIndex === null ? "Tạo trận thủ công" : `Sửa ${match.matchLabel || match.matchKey}`;
        matchEditorModal.querySelector("[data-match-editor-context]").textContent =
            `${round.roundLabel} · ${group.groupName}`;
        matchEditorModal.querySelector("[data-match-editor-body]").innerHTML = `
            <div class="bte-match-editor-grid">
                <div class="form-group">
                    <label>Mã ổn định</label>
                    <input class="form-control" value="${attr(match.matchKey)}" data-match-editor-field="matchKey" maxlength="50" autocomplete="off" />
                    <small>Dùng để các trận sau tham chiếu đội thắng hoặc đội thua.</small>
                </div>
                <div class="form-group">
                    <label>Nhãn hiển thị</label>
                    <input class="form-control" value="${attr(match.matchLabel || "")}" data-match-editor-field="matchLabel" maxlength="200" autocomplete="off" />
                    <small>Ví dụ: Bán kết 1, Chung kết, Tranh hạng ba.</small>
                </div>
                <div class="form-group">
                    <label>Thứ tự trong bảng/nhánh</label>
                    <input class="form-control" type="number" value="${match.sortOrder ?? 0}" data-match-editor-field="sortOrder" />
                </div>
                <div class="form-group">
                    <label>Loại trận kết thúc</label>
                    <select class="form-control" data-match-editor-field="terminalType">
                        ${option("", "Không phải trận kết thúc", match.terminalType || "")}
                        ${option("CHAMPION", "Vô địch", match.terminalType)}
                        ${option("THIRD_PLACE", "Hạng ba", match.terminalType)}
                        ${option("PLACEMENT", "Xếp hạng", match.terminalType)}
                    </select>
                </div>
            </div>
            <label class="bte-match-editor-terminal">
                <input type="checkbox" data-match-editor-field="isTerminal"${match.isTerminal ? " checked" : ""} />
                <span><strong>Đây là trận cuối của một nhánh</strong><small>Mẫu sơ đồ phải có đúng một trận kết thúc loại Vô địch để xuất bản.</small></span>
            </label>
            <div class="bte-match-editor-source-head">
                <div><span class="bte-step">01</span><div><strong>Nguồn đội 1</strong><small>Đội ban đầu, đội thắng, đội thua, hạng bảng hoặc miễn đấu</small></div></div>
                <div><span class="bte-step">02</span><div><strong>Nguồn đội 2</strong><small>Chỉ chọn nguồn từ vị trí trước trận hiện tại</small></div></div>
            </div>
            <div class="bte-match-editor-slots">
                ${match.slots.map((slot, index) => renderSlot(slot, match.matchKey, index, matchEditorTarget(editor))).join("")}
            </div>
            <div class="bte-match-editor-source-error d-none" data-match-source-error role="alert">
                <i class="fas fa-exclamation-triangle"></i><span></span>
            </div>`;
    }

    function showMatchEditorSourceError(duplicate) {
        if (!matchEditorModal) return;
        const warning = matchEditorModal.querySelector("[data-match-source-error]");
        const slots = matchEditorModal.querySelectorAll("[data-slot-index]");
        slots.forEach((slot) => slot.classList.toggle("is-duplicate-source", Boolean(duplicate)));
        if (!warning) return;
        warning.classList.toggle("d-none", !duplicate);
        warning.querySelector("span").textContent = duplicate?.message || "";
    }

    function validateMatchEditorSources(match = null) {
        const duplicate = duplicateMatchSource(match || readMatchEditor());
        showMatchEditorSourceError(duplicate);
        return duplicate;
    }

    function readMatchEditor() {
        const editor = state.matchEditor;
        if (!editor || !matchEditorModal) return null;
        const body = matchEditorModal.querySelector("[data-match-editor-body]");
        const matchKey = body.querySelector("[data-match-editor-field='matchKey']").value.trim().toUpperCase();
        return {
            matchKey,
            matchLabel: body.querySelector("[data-match-editor-field='matchLabel']").value.trim() || null,
            sortOrder: readNumber(body.querySelector("[data-match-editor-field='sortOrder']")),
            isTerminal: body.querySelector("[data-match-editor-field='isTerminal']").checked,
            terminalType: body.querySelector("[data-match-editor-field='terminalType']").value || null,
            slots: [...body.querySelectorAll("[data-slot-index]")].map((slotEl, slotIndex) => {
                const sourceType = slotEl.querySelector("[data-slot-field='sourceType']").value;
                return {
                    slotNumber: slotIndex + 1,
                    sourceType,
                    seedNumber: sourceType === "SEED" ? readNumber(slotEl.querySelector("[data-slot-field='seedNumber']"), null) : null,
                    sourceMatchKey: ["WINNER_MATCH", "LOSER_MATCH"].includes(sourceType)
                        ? slotEl.querySelector("[data-slot-field='sourceMatchKey']")?.value || null
                        : null,
                    sourceGroupKey: sourceType === "GROUP_RANK"
                        ? slotEl.querySelector("[data-slot-field='sourceGroupKey']")?.value || null
                        : null,
                    sourceRank: sourceType === "GROUP_RANK"
                        ? readNumber(slotEl.querySelector("[data-slot-field='sourceRank']"), null)
                        : null
                };
            })
        };
    }

    function openMatchEditor(roundIndex, groupIndex, matchIndex = null) {
        if (state.readOnly || !matchEditorModal) return;
        closeSourceMatchPicker();
        collectGraph();
        const matches = state.graph.rounds[roundIndex].groups[groupIndex].matches;
        const current = matchIndex === null ? buildNewMatch(roundIndex, groupIndex) : matches[matchIndex];
        state.matchEditor = {
            roundIndex,
            groupIndex,
            matchIndex,
            originalKey: matchIndex === null ? null : current.matchKey,
            draft: cloneMatch(current)
        };
        renderMatchEditor();
        validateMatchEditorSources(state.matchEditor.draft);
        const dialog = matchEditorModal.querySelector(".modal-dialog");
        dialog?.classList.remove("is-dragged");
        if (dialog) { dialog.style.left = ""; dialog.style.top = ""; }
        window.jQuery(matchEditorModal).modal("show");
    }

    function saveMatchEditor() {
        const editor = state.matchEditor;
        if (!editor) return;
        const match = readMatchEditor();
        const keyInput = matchEditorModal.querySelector("[data-match-editor-field='matchKey']");
        keyInput.classList.remove("is-invalid");
        if (!match.matchKey) {
            keyInput.classList.add("is-invalid");
            keyInput.focus();
            return;
        }

        const duplicate = graphLocations().matches.some((item) =>
            item.match.matchKey === match.matchKey
            && !(item.roundIndex === editor.roundIndex
                && item.groupIndex === editor.groupIndex
                && item.matchIndex === editor.matchIndex));
        if (duplicate) {
            keyInput.classList.add("is-invalid");
            showMessage("error", `Mã trận ${match.matchKey} đã tồn tại trong mẫu sơ đồ thi đấu.`);
            keyInput.focus();
            return;
        }

        const duplicateSource = validateMatchEditorSources(match);
        if (duplicateSource) {
            showMessage("error", duplicateSource.message);
            matchEditorModal.querySelector("[data-match-source-error]")?.scrollIntoView({ behavior: "smooth", block: "center" });
            return;
        }

        if (editor.originalKey && editor.originalKey !== match.matchKey) {
            const dependents = sourceDependents(editor.originalKey, null);
            if (!dependencyPrompt(
                "Tiếp tục đổi mã và cập nhật các liên kết này?",
                `Trận ${editor.originalKey}`,
                dependents)) return;
            state.graph.rounds.flatMap((round) => round.groups).flatMap((group) => group.matches)
                .flatMap((item) => item.slots || [])
                .filter((slot) => ["WINNER_MATCH", "LOSER_MATCH"].includes(slot.sourceType)
                    && slot.sourceMatchKey === editor.originalKey)
                .forEach((slot) => { slot.sourceMatchKey = match.matchKey; });
            moveMatchLayoutKey(editor.originalKey, match.matchKey);
        }

        const matches = state.graph.rounds[editor.roundIndex].groups[editor.groupIndex].matches;
        if (editor.matchIndex === null) matches.push(match);
        else matches[editor.matchIndex] = match;
        closeSourceMatchPicker();
        window.jQuery(matchEditorModal).modal("hide");
        state.matchEditor = null;
        render();
        markDirty();
    }

    function addMatch(roundIndex, groupIndex) {
        openMatchEditor(roundIndex, groupIndex);
    }

    function openBulkMatchEditor(roundIndex, groupIndex) {
        if (state.readOnly || !bulkMatchModal) return;
        collectGraph();
        const round = state.graph.rounds[roundIndex];
        const group = round?.groups[groupIndex];
        if (!round || !group) return;

        state.bulkMatchEditor = { roundIndex, groupIndex };
        bulkMatchModal.querySelector("[data-bulk-match-context]").textContent =
            `${round.roundLabel} · ${group.groupName}`;
        const countInput = bulkMatchModal.querySelector("[data-bulk-match-count]");
        countInput.value = "4";
        countInput.classList.remove("is-invalid");
        bulkMatchModal.querySelector("[data-bulk-assign-seeds]").checked = false;
        window.jQuery(bulkMatchModal).modal("show");
        window.setTimeout(() => { countInput.focus(); countInput.select(); }, 180);
    }

    function createBulkMatches() {
        const editor = state.bulkMatchEditor;
        if (!editor || !bulkMatchModal) return;
        const countInput = bulkMatchModal.querySelector("[data-bulk-match-count]");
        const count = Number(countInput.value);
        countInput.classList.remove("is-invalid");
        if (!Number.isInteger(count) || count < 2 || count > 64) {
            countInput.classList.add("is-invalid");
            countInput.focus();
            return;
        }

        const group = state.graph.rounds[editor.roundIndex].groups[editor.groupIndex];
        const assignSeeds = bulkMatchModal.querySelector("[data-bulk-assign-seeds]").checked;
        for (let index = 0; index < count; index += 1) {
            const match = buildNewMatch(editor.roundIndex, editor.groupIndex);
            if (!assignSeeds) match.slots.forEach((slot) => { slot.seedNumber = null; });
            group.matches.push(match);
        }

        window.jQuery(bulkMatchModal).modal("hide");
        state.bulkMatchEditor = null;
        render();
        markDirty();
        showMessage("success", `Đã tạo ${count} khung trận trong ${group.groupName}. Hãy thiết lập nguồn cho từng trận.`);
    }

    function initialTeamLocations() {
        return state.graph.rounds.flatMap((round, roundIndex) =>
            (round.groups || []).flatMap((group, groupIndex) =>
                (group.matches || []).flatMap((match, matchIndex) => {
                    const matchSlots = match.slots || [];
                    const isByePass = matchSlots.some((slot) => normalizeSourceKey(slot.sourceType) === "BYE")
                        && matchSlots.some((slot) => normalizeSourceKey(slot.sourceType) === "SEED");
                    return matchSlots
                        .filter((slot) => normalizeSourceKey(slot.sourceType) === "SEED")
                        .map((slot) => ({
                            round, roundIndex, group, groupIndex, match, matchIndex, slot, isByePass
                        }));
                })));
    }

    function buildInitialTeamNumberingPlan(roundIndex) {
        const round = state.graph.rounds[roundIndex];
        const capacity = Number(state.graph.seedCapacity);
        const plan = {
            round, roundIndex, capacity,
            selected: [], preserved: [], assignments: [],
            blankCount: 0, duplicateCount: 0, outOfRangeCount: 0,
            externalDuplicateCount: 0, unassignedSlotCount: 0, unusedPositionCount: 0,
            error: null
        };
        if (!round) {
            plan.error = "Không tìm thấy vòng đấu cần đánh số.";
            return plan;
        }
        if (!Number.isInteger(capacity) || capacity < 2 || capacity > 1024) {
            plan.error = "Sức chứa đội phải là số nguyên từ 2 đến 1024 trước khi đánh số.";
            return plan;
        }
        if (normalizeSourceKey(round.roundType) === "GROUP_STAGE") {
            plan.error = "Không tự đánh số theo từng slot cho vòng bảng vì một đội có thể xuất hiện ở nhiều trận.";
            return plan;
        }

        const allLocations = initialTeamLocations();
        plan.selected = allLocations.filter((location) => location.roundIndex === roundIndex);
        if (!plan.selected.length) {
            plan.error = "Vòng này không có slot Đội ban đầu để đánh số.";
            return plan;
        }

        const used = new Map();
        allLocations.filter((location) => location.roundIndex !== roundIndex).forEach((location) => {
            const number = Number(location.slot.seedNumber);
            if (!Number.isInteger(number) || number < 1 || number > capacity) return;
            if (used.has(number)) plan.externalDuplicateCount += 1;
            else used.set(number, location);
        });

        const prioritized = [
            ...plan.selected.filter((location) => location.isByePass),
            ...plan.selected.filter((location) => !location.isByePass)
        ];
        const pending = [];
        prioritized.forEach((location) => {
            const raw = location.slot.seedNumber;
            const number = Number(raw);
            const blank = raw === null || raw === undefined || String(raw).trim() === "";
            if (Number.isInteger(number) && number >= 1 && number <= capacity && !used.has(number)) {
                used.set(number, location);
                plan.preserved.push({ ...location, number });
                return;
            }
            if (blank) plan.blankCount += 1;
            else if (!Number.isInteger(number) || number < 1 || number > capacity) plan.outOfRangeCount += 1;
            else plan.duplicateCount += 1;
            pending.push(location);
        });

        const available = [];
        for (let number = 1; number <= capacity; number += 1) {
            if (!used.has(number)) available.push(number);
        }
        pending.forEach((location, index) => {
            if (index < available.length) plan.assignments.push({ ...location, number: available[index] });
        });
        plan.unassignedSlotCount = Math.max(0, pending.length - plan.assignments.length);
        plan.unusedPositionCount = Math.max(0, available.length - plan.assignments.length);
        return plan;
    }

    function initialTeamAssignmentLabel(item) {
        if (item.isByePass) return `${item.match.matchLabel || item.match.matchKey} · suất BYE`;
        return `${item.match.matchLabel || item.match.matchKey} · Đội ${item.slot.slotNumber}`;
    }

    function renderInitialTeamNumberingPlan() {
        const editor = state.initialTeamNumberingEditor;
        if (!editor || !initialTeamNumberingModal) return;
        const plan = buildInitialTeamNumberingPlan(editor.roundIndex);
        editor.plan = plan;
        initialTeamNumberingModal.querySelector("[data-initial-team-numbering-context]").textContent =
            plan.round ? `${plan.round.roundLabel} · ${plan.round.roundKey}` : "Vòng không còn tồn tại";
        initialTeamNumberingModal.querySelector("[data-initial-team-numbering-summary]").innerHTML = `
            <div><strong>${Number.isInteger(plan.capacity) ? plan.capacity : "—"}</strong><span>Sức chứa tối đa</span></div>
            <div><strong>${plan.selected.length}</strong><span>Slot Đội ban đầu</span></div>
            <div><strong>${plan.preserved.length}</strong><span>Vị trí được giữ</span></div>
            <div><strong>${plan.assignments.length}</strong><span>Vị trí sẽ được gán</span></div>`;

        const error = initialTeamNumberingModal.querySelector("[data-initial-team-numbering-error]");
        error.className = "alert d-none";
        if (plan.error || plan.unassignedSlotCount > 0) {
            error.textContent = plan.error || `Thiếu ${plan.unassignedSlotCount} số vị trí để gán cho các slot trong vòng. Hãy tăng sức chứa hoặc giảm số slot Đội ban đầu.`;
            error.classList.add("alert-danger");
            error.classList.remove("d-none");
        } else if (plan.externalDuplicateCount > 0 || plan.unusedPositionCount > 0) {
            const messages = [];
            if (plan.externalDuplicateCount > 0) messages.push(`Ngoài vòng này còn ${plan.externalDuplicateCount} vị trí bị trùng cần kiểm tra riêng.`);
            if (plan.unusedPositionCount > 0) messages.push(`Sau khi gán vẫn còn ${plan.unusedPositionCount} vị trí từ 1–${plan.capacity} chưa được dùng trong graph.`);
            error.textContent = messages.join(" ");
            error.classList.add("alert-warning");
            error.classList.remove("d-none");
        }

        const preview = initialTeamNumberingModal.querySelector("[data-initial-team-numbering-preview]");
        preview.innerHTML = plan.assignments.length
            ? `<div class="bte-numbering-preview__head"><span>Các vị trí sắp cập nhật</span><small>${plan.assignments.length} thay đổi</small></div>
               ${plan.assignments.map((item) => `<div class="bte-numbering-preview__row"><strong>${esc(initialTeamAssignmentLabel(item))}</strong><span>Vị trí ${item.number}</span></div>`).join("")}`
            : '<div class="bte-numbering-preview__empty">Không có vị trí nào cần cập nhật.</div>';

        const details = [];
        if (plan.blankCount) details.push(`${plan.blankCount} ô trống`);
        if (plan.duplicateCount) details.push(`${plan.duplicateCount} số trùng`);
        if (plan.outOfRangeCount) details.push(`${plan.outOfRangeCount} số ngoài giới hạn`);
        initialTeamNumberingModal.querySelector("[data-initial-team-numbering-status]").textContent = details.length
            ? `Sẽ xử lý ${details.join(" · ")}`
            : "Các vị trí hiện tại đã hợp lệ";
        initialTeamNumberingModal.querySelector("[data-initial-team-numbering-apply]").disabled =
            Boolean(plan.error) || plan.unassignedSlotCount > 0 || plan.assignments.length === 0;
    }

    function openInitialTeamNumbering(roundIndex) {
        if (state.readOnly || !initialTeamNumberingModal || !window.jQuery) return;
        collectGraph();
        state.initialTeamNumberingEditor = { roundIndex, plan: null };
        renderInitialTeamNumberingPlan();
        window.jQuery(initialTeamNumberingModal).modal("show");
    }

    function applyInitialTeamNumbering() {
        const editor = state.initialTeamNumberingEditor;
        if (!editor || !initialTeamNumberingModal) return;
        collectGraph();
        const plan = buildInitialTeamNumberingPlan(editor.roundIndex);
        const error = initialTeamNumberingModal.querySelector("[data-initial-team-numbering-error]");
        if (plan.error || plan.unassignedSlotCount > 0 || !plan.assignments.length) {
            editor.plan = plan;
            renderInitialTeamNumberingPlan();
            return;
        }
        plan.assignments.forEach((assignment) => { assignment.slot.seedNumber = assignment.number; });
        const roundLabel = plan.round.roundLabel;
        const changedCount = plan.assignments.length;
        window.jQuery(initialTeamNumberingModal).modal("hide");
        state.initialTeamNumberingEditor = null;
        render();
        markDirty();
        showMessage("success", `Đã tự đánh số ${changedCount} vị trí Đội ban đầu trong ${roundLabel}. Hãy lưu bản nháp để ghi nhận thay đổi.`);
        error.classList.add("d-none");
    }

    function usedSeedPositions() {
        const used = new Map();
        state.graph.rounds.forEach((round) => round.groups.forEach((group) => group.matches.forEach((match) => {
            (match.slots || []).forEach((slot) => {
                const seedNumber = Number(slot.seedNumber);
                if (normalizeSourceKey(slot.sourceType) === "SEED" && seedNumber > 0 && !used.has(seedNumber)) {
                    used.set(seedNumber, {
                        matchKey: match.matchKey,
                        matchLabel: match.matchLabel || match.matchKey,
                        isByePass: Boolean(byePassThroughInfo(match))
                    });
                }
            });
        })));
        return used;
    }

    function updateByePassSelectionState() {
        const editor = state.byePassEditor;
        if (!editor || !byePassModal) return;
        byePassModal.querySelectorAll("[data-bye-pass-seed]").forEach((input) => {
            const selected = editor.selectedSeeds.has(Number(input.value));
            input.checked = selected;
            input.closest(".bte-bye-pass-option")?.classList.toggle("is-selected", selected);
        });
        const selectedCount = editor.selectedSeeds.size;
        const availableCount = byePassModal.querySelectorAll("[data-bye-pass-seed]:not(:disabled)").length;
        byePassModal.querySelector("[data-bye-pass-status]").textContent = selectedCount
            ? `Đã chọn ${selectedCount} vị trí · ${availableCount} vị trí có thể dùng`
            : `${availableCount} vị trí chưa được sử dụng`;
        byePassModal.querySelector("[data-bye-pass-create]").disabled = selectedCount === 0;
    }

    function filterByePassPositions() {
        if (!byePassModal) return;
        const query = normalizeSearch(byePassModal.querySelector("[data-bye-pass-search]")?.value);
        byePassModal.querySelectorAll("[data-bye-pass-search-text]").forEach((row) => {
            row.classList.toggle("d-none", Boolean(query) && !row.dataset.byePassSearchText.includes(query));
        });
    }

    function renderByePassEditor() {
        const editor = state.byePassEditor;
        if (!editor || !byePassModal) return;
        const used = usedSeedPositions();
        const capacity = Math.max(0, Number(state.graph.seedCapacity) || 0);
        const list = byePassModal.querySelector("[data-bye-pass-list]");
        list.innerHTML = capacity
            ? Array.from({ length: capacity }, (_, index) => index + 1).map((seedNumber) => {
                const usage = used.get(seedNumber);
                const usageText = usage
                    ? `${usage.isByePass ? "Đã là suất BYE" : "Đã dùng"} · ${usage.matchLabel} (${usage.matchKey})`
                    : "Chưa được ghép cặp hoặc gán vào card khác";
                const search = normalizeSearch(`đội ban đầu ${seedNumber} ${usageText}`);
                return `<label class="bte-bye-pass-option ${usage ? "is-used" : ""}${editor.selectedSeeds.has(seedNumber) ? " is-selected" : ""}" data-bye-pass-search-text="${attr(search)}">
                    <input type="checkbox" value="${seedNumber}" data-bye-pass-seed${usage ? " disabled" : ""}${editor.selectedSeeds.has(seedNumber) ? " checked" : ""} />
                    <span class="bte-bye-pass-option__number">${seedNumber}</span>
                    <span><strong>Đội ban đầu ${seedNumber}</strong><small>${esc(usageText)}</small></span>
                    <i class="fas ${usage ? "fa-lock" : "fa-check"}"></i>
                </label>`;
            }).join("")
            : '<div class="text-center text-muted p-4">Template chưa có sức chứa đội hợp lệ.</div>';
        filterByePassPositions();
        updateByePassSelectionState();
    }

    function openByePassEditor(roundIndex, groupIndex) {
        if (state.readOnly || !byePassModal || !window.jQuery) return;
        collectGraph();
        const round = state.graph.rounds[roundIndex];
        const group = round?.groups[groupIndex];
        if (!round || !group) return;
        if (normalizeSourceKey(round.roundType) === "GROUP_STAGE") {
            showMessage("error", "BYE không áp dụng cho vòng bảng.");
            return;
        }
        state.byePassEditor = { roundIndex, groupIndex, selectedSeeds: new Set() };
        byePassModal.querySelector("[data-bye-pass-context]").textContent =
            `${round.roundLabel} · ${group.groupName} · Chọn một hoặc nhiều vị trí đội ban đầu`;
        byePassModal.querySelector("[data-bye-pass-search]").value = "";
        byePassModal.querySelector("[data-bye-pass-error]").classList.add("d-none");
        renderByePassEditor();
        window.jQuery(byePassModal).modal("show");
    }

    function createByePasses() {
        const editor = state.byePassEditor;
        if (!editor || !byePassModal) return;
        const error = byePassModal.querySelector("[data-bye-pass-error]");
        const selectedSeeds = [...editor.selectedSeeds].sort((first, second) => first - second);
        if (!selectedSeeds.length) {
            error.textContent = "Vui lòng chọn ít nhất một vị trí đội được BYE.";
            error.classList.remove("d-none");
            return;
        }

        collectGraph();
        const round = state.graph.rounds[editor.roundIndex];
        const group = round?.groups[editor.groupIndex];
        if (!round || !group || normalizeSourceKey(round.roundType) === "GROUP_STAGE") {
            error.textContent = "Vòng hoặc nhánh được chọn không còn hợp lệ để tạo BYE.";
            error.classList.remove("d-none");
            return;
        }
        const used = usedSeedPositions();
        const conflict = selectedSeeds.find((seedNumber) => used.has(seedNumber));
        if (conflict) {
            error.textContent = `Đội ban đầu ${conflict} vừa được sử dụng ở một card khác. Vui lòng tải lại danh sách và chọn vị trí khác.`;
            error.classList.remove("d-none");
            renderByePassEditor();
            return;
        }

        selectedSeeds.forEach((seedNumber) => {
            const match = buildNewMatch(editor.roundIndex, editor.groupIndex);
            match.matchLabel = `BYE · Đội ban đầu ${seedNumber}`;
            match.slots = [
                { slotNumber: 1, sourceType: "SEED", seedNumber },
                { slotNumber: 2, sourceType: "BYE", seedNumber: null, sourceMatchKey: null, sourceGroupKey: null, sourceRank: null }
            ];
            group.matches.push(match);
        });
        state.graph.allowBye = true;
        window.jQuery(byePassModal).modal("hide");
        state.byePassEditor = null;
        render();
        markDirty();
        showMessage("success", `Đã tạo ${selectedSeeds.length} card BYE trong ${group.groupName}. Hãy kéo dây BYE · Đi tiếp tới vòng đích.`);
    }

    function hasConfiguredSource(slot) {
        if (!slot) return false;
        if (slot.sourceType === "SEED") return Number(slot.seedNumber) > 0;
        if (["WINNER_MATCH", "LOSER_MATCH"].includes(slot.sourceType)) return Boolean(normalizeSourceKey(slot.sourceMatchKey));
        if (slot.sourceType === "GROUP_RANK") {
            return Boolean(normalizeSourceKey(slot.sourceGroupKey)) && Number(slot.sourceRank) > 0;
        }
        return slot.sourceType === "BYE";
    }

    function isEmptyQuickPairTarget(match) {
        const slots = match?.slots || [];
        return slots.length <= 2 && !slots.some(hasConfiguredSource);
    }

    function compareMatchLocations(first, second) {
        return first.round.sortOrder - second.round.sortOrder
            || first.group.sortOrder - second.group.sortOrder
            || first.match.sortOrder - second.match.sortOrder;
    }

    function quickPairOutputId(sourceType, matchKey) {
        return `${sourceType}:${normalizeSourceKey(matchKey)}`;
    }

    function usedMatchOutputIds(editor = state.quickPairEditor) {
        const ids = new Set();
        state.graph.rounds.forEach((round, roundIndex) => round.groups.forEach((group, groupIndex) => {
            if (editor?.replaceExisting && roundIndex === editor.roundIndex && groupIndex === editor.groupIndex) return;
            group.matches.forEach((match) => (match.slots || []).forEach((slot) => {
                if (["WINNER_MATCH", "LOSER_MATCH"].includes(slot.sourceType)
                    && normalizeSourceKey(slot.sourceMatchKey)) {
                    ids.add(quickPairOutputId(slot.sourceType, slot.sourceMatchKey));
                }
            }));
        }));
        return ids;
    }

    function quickPairTargets(editor = state.quickPairEditor) {
        if (!editor) return [];
        const group = state.graph.rounds[editor.roundIndex]?.groups[editor.groupIndex];
        return [...(group?.matches || [])]
            .filter((match) => editor.replaceExisting || isEmptyQuickPairTarget(match))
            .sort((first, second) => first.sortOrder - second.sortOrder);
    }

    function refreshQuickPairSources(editor = state.quickPairEditor) {
        if (!editor) return;
        const round = state.graph.rounds[editor.roundIndex];
        const group = round?.groups[editor.groupIndex];
        if (!round || !group) return;
        const targets = quickPairTargets(editor);
        const targetKeys = new Set(targets.map((match) => normalizeSourceKey(match.matchKey)));
        const referenceMatch = targets[0] || {
            matchKey: "__QUICK_PAIR_TARGET__",
            sortOrder: Math.max(-1, ...group.matches.map((match) => Number(match.sortOrder) || 0)) + 1
        };
        const targetLocation = { round, group, match: referenceMatch };
        const usedIds = usedMatchOutputIds(editor);
        editor.sources = graphLocations().matches
            .filter((source) => !targetKeys.has(normalizeSourceKey(source.match.matchKey))
                && comesBefore(source, targetLocation))
            .sort(compareMatchLocations)
            .map((source) => ({
                roundKey: source.round.roundKey,
                roundLabel: source.round.roundLabel,
                groupKey: source.group.groupKey,
                groupName: source.group.groupName || source.group.groupKey,
                matchKey: source.match.matchKey,
                matchLabel: source.match.matchLabel || source.match.matchKey,
                isByePass: Boolean(byePassThroughInfo(source.match)),
                winnerUsed: usedIds.has(quickPairOutputId("WINNER_MATCH", source.match.matchKey)),
                loserUsed: usedIds.has(quickPairOutputId("LOSER_MATCH", source.match.matchKey))
            }));
        editor.sourceOutputs = editor.sources.flatMap((source) => {
            const winner = { id: quickPairOutputId("WINNER_MATCH", source.matchKey), sourceType: "WINNER_MATCH", sourceMatchKey: source.matchKey, matchKey: source.matchKey, matchLabel: source.matchLabel, context: `${source.roundLabel} · ${source.groupName}`, used: source.winnerUsed, outputLabel: source.isByePass ? "BYE" : "Thắng" };
            if (source.isByePass) return [winner];
            return [winner, { id: quickPairOutputId("LOSER_MATCH", source.matchKey), sourceType: "LOSER_MATCH", sourceMatchKey: source.matchKey, matchKey: source.matchKey, matchLabel: source.matchLabel, context: `${source.roundLabel} · ${source.groupName}`, used: source.loserUsed, outputLabel: "Thua" }];
        });
        editor.selectedIds = editor.selectedIds.filter((id) => editor.sourceOutputs.some((source) => source.id === id && !source.used));
    }

    function quickPairSourceGroups(sources) {
        const groups = [];
        sources.forEach((source) => {
            const id = `${source.roundKey}:${source.groupKey}`;
            let group = groups.find((item) => item.id === id);
            if (!group) {
                group = { id, label: `${source.roundLabel} · ${source.groupName}`, sources: [] };
                groups.push(group);
            }
            group.sources.push(source);
        });
        return groups;
    }

    function renderQuickPairEditor() {
        const editor = state.quickPairEditor;
        if (!editor || !quickPairModal) return;
        const selectedIds = new Set(editor.selectedIds);
        const sourceList = quickPairModal.querySelector("[data-quick-pair-source-list]");
        sourceList.innerHTML = editor.sources.length
            ? quickPairSourceGroups(editor.sources).map((group) => `<section class="bte-quick-pair-source-group" data-quick-pair-source-group>
                <div class="bte-quick-pair-source-group__head"><i class="fas fa-layer-group"></i>${esc(group.label)}</div>
                ${group.sources.map((source) => {
                    const search = `${group.label} ${source.matchKey} ${source.matchLabel}`;
                    const winnerId = quickPairOutputId("WINNER_MATCH", source.matchKey);
                    const loserId = quickPairOutputId("LOSER_MATCH", source.matchKey);
                    const outputButton = (id, type, label, used) => `<button type="button" class="bte-quick-pair-source-button ${type === "WINNER_MATCH" ? "is-winner" : "is-loser"}${selectedIds.has(id) ? " is-selected" : ""}${used ? " is-used" : ""}" data-quick-pair-source-id="${attr(id)}"${used ? " disabled title=\"Nguồn này đã được dùng ở một trận khác\"" : ""}><i class="fas ${selectedIds.has(id) ? "fa-check" : used ? "fa-lock" : "fa-plus"}"></i>${label}</button>`;
                    return `<div class="bte-quick-pair-source-row" data-quick-pair-search-text="${attr(normalizeSearch(search))}">
                        <span class="bte-quick-pair-source-key">${esc(source.matchKey)}</span>
                        <span class="bte-quick-pair-source-name"><strong>${esc(source.matchLabel)}</strong><small>${esc(group.label)}</small></span>
                        <div>${outputButton(winnerId, "WINNER_MATCH", source.isByePass ? "BYE · Đi tiếp" : "Thắng", source.winnerUsed)}${source.isByePass ? "" : outputButton(loserId, "LOSER_MATCH", "Thua", source.loserUsed)}</div>
                    </div>`;
                }).join("")}
            </section>`).join("")
            : `<div class="bte-quick-pair-empty"><i class="fas fa-info-circle"></i><strong>Không có trận nguồn hợp lệ</strong><span>Hãy tạo trận ở vị trí trước nhánh này trước.</span></div>`;

        const selection = quickPairModal.querySelector("[data-quick-pair-selection-list]");
        const selectedSources = editor.selectedIds
            .map((id) => editor.sourceOutputs.find((source) => source.id === id))
            .filter(Boolean);
        selection.innerHTML = selectedSources.length
            ? selectedSources.map((source, index) => `<div class="bte-quick-pair-selected-row">
                <span class="bte-quick-pair-order">${index + 1}</span>
                <span class="bte-quick-pair-selected-type ${source.sourceType === "WINNER_MATCH" ? "is-winner" : "is-loser"}">${esc(source.outputLabel || (source.sourceType === "WINNER_MATCH" ? "Thắng" : "Thua"))}</span>
                <span><strong>${esc(source.matchLabel)}</strong><small>${esc(source.matchKey)} · ${esc(source.context)}</small></span>
                <div>
                    <button type="button" data-quick-pair-move="-1" data-quick-pair-index="${index}" title="Đưa lên"${index === 0 ? " disabled" : ""}><i class="fas fa-chevron-up"></i></button>
                    <button type="button" data-quick-pair-move="1" data-quick-pair-index="${index}" title="Đưa xuống"${index === selectedSources.length - 1 ? " disabled" : ""}><i class="fas fa-chevron-down"></i></button>
                    <button type="button" data-quick-pair-remove="${attr(source.id)}" title="Bỏ nguồn"><i class="fas fa-times"></i></button>
                </div>
            </div>`).join("")
            : `<div class="bte-quick-pair-empty is-compact"><i class="fas fa-hand-pointer"></i><span>Chọn Đội thắng hoặc Đội thua ở danh sách bên trái.</span></div>`;

        const pairCount = Math.floor(selectedSources.length / 2);
        const targets = quickPairTargets(editor);
        const preview = quickPairModal.querySelector("[data-quick-pair-preview]");
        preview.innerHTML = pairCount
            ? `<div class="bte-quick-pair-preview__title"><i class="fas fa-project-diagram"></i>Xem trước ${pairCount} cặp</div>${Array.from({ length: pairCount }, (_, index) => {
                const first = selectedSources[index * 2];
                const second = selectedSources[index * 2 + 1];
                const target = targets[index];
                const targetLabel = target ? `${target.matchLabel || target.matchKey} (${target.matchKey})` : `Tạo trận mới ${index - targets.length + 1}`;
                return `<div class="bte-quick-pair-preview-row">
                    <span>${index + 1}</span><div><strong>${esc(targetLabel)}</strong><small>${esc(first.outputLabel || (first.sourceType === "WINNER_MATCH" ? "Thắng" : "Thua"))} ${esc(first.matchKey)} <b>vs</b> ${esc(second.outputLabel || (second.sourceType === "WINNER_MATCH" ? "Thắng" : "Thua"))} ${esc(second.matchKey)}</small></div>
                </div>`;
            }).join("")}`
            : "";

        quickPairModal.querySelector("[data-quick-pair-selected-count]").textContent = selectedSources.length;
        const apply = quickPairModal.querySelector("[data-quick-pair-apply]");
        const status = quickPairModal.querySelector("[data-quick-pair-footer-status]");
        const valid = selectedSources.length >= 2 && selectedSources.length % 2 === 0;
        apply.disabled = !valid;
        if (!selectedSources.length) status.textContent = "Chọn ít nhất 2 nguồn đội.";
        else if (selectedSources.length % 2 !== 0) status.textContent = `Đang lẻ 1 nguồn; chọn thêm 1 nguồn để đủ cặp.`;
        else {
            const createCount = Math.max(0, pairCount - targets.length);
            const targetLabel = editor.replaceExisting ? "trận hiện có" : "trận trống";
            status.textContent = `${pairCount} cặp · dùng ${Math.min(pairCount, targets.length)} ${targetLabel}${createCount ? ` · tạo thêm ${createCount} trận` : ""}.`;
        }
        filterQuickPairSources(quickPairModal.querySelector("[data-quick-pair-search]"));
    }

    function filterQuickPairSources(input) {
        if (!quickPairModal) return;
        const query = normalizeSearch(input?.value);
        quickPairModal.querySelectorAll("[data-quick-pair-source-group]").forEach((group) => {
            let visible = 0;
            group.querySelectorAll("[data-quick-pair-search-text]").forEach((row) => {
                const matches = !query || row.dataset.quickPairSearchText.includes(query);
                row.classList.toggle("d-none", !matches);
                if (matches) visible += 1;
            });
            group.classList.toggle("d-none", visible === 0);
        });
    }

    function openQuickPairEditor(roundIndex, groupIndex) {
        if (state.readOnly || !quickPairModal) return;
        collectGraph();
        const round = state.graph.rounds[roundIndex];
        const group = round?.groups[groupIndex];
        if (!round || !group) return;
        state.quickPairEditor = { roundIndex, groupIndex, replaceExisting: false, sources: [], sourceOutputs: [], selectedIds: [] };
        refreshQuickPairSources(state.quickPairEditor);
        quickPairModal.querySelector("[data-quick-pair-context]").textContent = `${round.roundLabel} · ${group.groupName}`;
        quickPairModal.querySelector("[data-quick-pair-search]").value = "";
        quickPairModal.querySelector("[data-quick-pair-replace]").checked = false;
        renderQuickPairEditor();
        window.jQuery(quickPairModal).modal("show");
    }

    function toggleQuickPairSource(id) {
        const editor = state.quickPairEditor;
        const source = editor?.sourceOutputs.find((item) => item.id === id);
        if (!editor || !source || source.used) return;
        const index = editor.selectedIds.indexOf(id);
        if (index >= 0) editor.selectedIds.splice(index, 1);
        else editor.selectedIds.push(id);
        renderQuickPairEditor();
    }

    function selectAllQuickPairSources(sourceType) {
        const editor = state.quickPairEditor;
        if (!editor) return;
        editor.sourceOutputs
            .filter((source) => source.sourceType === sourceType && !source.used && !editor.selectedIds.includes(source.id))
            .forEach((source) => editor.selectedIds.push(source.id));
        renderQuickPairEditor();
    }

    function applyQuickPairs() {
        const editor = state.quickPairEditor;
        if (!editor || !quickPairModal) return;
        const selected = editor.selectedIds
            .map((id) => editor.sourceOutputs.find((source) => source.id === id))
            .filter(Boolean);
        if (selected.length < 2 || selected.length % 2 !== 0 || new Set(editor.selectedIds).size !== editor.selectedIds.length) return;

        const group = state.graph.rounds[editor.roundIndex].groups[editor.groupIndex];
        const targets = quickPairTargets(editor);
        const pairCount = selected.length / 2;
        let created = 0;
        for (let index = 0; index < pairCount; index += 1) {
            let target = targets[index];
            if (!target) {
                target = buildNewMatch(editor.roundIndex, editor.groupIndex);
                group.matches.push(target);
                created += 1;
            }
            target.slots = [selected[index * 2], selected[index * 2 + 1]].map((source, slotIndex) => ({
                slotNumber: slotIndex + 1,
                sourceType: source.sourceType,
                seedNumber: null,
                sourceMatchKey: source.sourceMatchKey,
                sourceGroupKey: null,
                sourceRank: null
            }));
        }

        window.jQuery(quickPairModal).modal("hide");
        state.quickPairEditor = null;
        render();
        markDirty();
        showMessage("success", `Đã ghép ${pairCount} cặp trong ${group.groupName}${created ? ` và tạo thêm ${created} trận` : ""}.`);
    }

    function positionSourceMatchPicker() {
        if (!sourceMatchPickerDialog || sourceMatchPickerDialog.classList.contains("d-none")) return;
        const editorDialog = matchEditorModal?.querySelector(".modal-dialog");
        const editorRect = editorDialog?.getBoundingClientRect();
        const pickerRect = sourceMatchPickerDialog.getBoundingClientRect();
        const margin = 14;
        let left = window.innerWidth - pickerRect.width - margin;
        let top = editorRect?.top ?? margin;

        if (editorRect && window.innerWidth - editorRect.right >= pickerRect.width + margin * 2)
            left = editorRect.right + margin;
        else if (editorRect && editorRect.left >= pickerRect.width + margin * 2)
            left = editorRect.left - pickerRect.width - margin;

        left = Math.max(margin, Math.min(left, window.innerWidth - pickerRect.width - margin));
        top = Math.max(margin, Math.min(top, window.innerHeight - pickerRect.height - margin));
        sourceMatchPickerDialog.style.left = `${left}px`;
        sourceMatchPickerDialog.style.top = `${top}px`;
    }

    function closeSourceMatchPicker() {
        if (!sourceMatchPickerDialog) return;
        sourceMatchPickerDialog.classList.add("d-none");
        state.sourceMatchPicker = null;
    }

    function renderSourceMatchPickerList() {
        const picker = state.sourceMatchPicker;
        if (!picker || !sourceMatchPickerDialog) return;
        const list = sourceMatchPickerDialog.querySelector("[data-source-match-list]");
        list.innerHTML = `
            <button class="bte-source-option bte-source-option--clear${picker.selectedKey ? "" : " is-selected"}" type="button" data-source-match-value="">
                <i class="fas fa-ban"></i><span><strong>Chưa chọn trận</strong><small>Để trống nguồn trận</small></span>
            </button>
            ${groupMatchSources(picker.sources).map((group) => `<section class="bte-source-option-group" data-source-option-group>
                <div class="bte-source-option-group__head"><i class="fas fa-layer-group"></i><span>${esc(group.label)}</span></div>
                ${group.sources.map((source) => {
                    const key = source.match.matchKey;
                    const label = source.match.matchLabel || key;
                    const search = `${group.label} ${label} ${key}`;
                    const editor = state.matchEditor;
                    const currentSlot = editor?.draft?.slots?.[picker.slotIndex];
                    const usedByOtherSlot = editor?.draft?.slots?.some((slot, index) => index !== picker.slotIndex
                        && slot.sourceType === currentSlot?.sourceType
                        && normalizeSourceKey(slot.sourceMatchKey) === normalizeSourceKey(key));
                    return `<button class="bte-source-option${key === picker.selectedKey ? " is-selected" : ""}${usedByOtherSlot ? " is-unavailable" : ""}" type="button" data-source-match-value="${attr(key)}" data-source-search="${attr(search)}"${usedByOtherSlot ? " disabled title=\"Nguồn này đã được dùng cho đội còn lại\"" : ""}>
                        <span class="bte-source-option__key">${esc(key)}</span>
                        <span><strong>${esc(label)}</strong><small>${esc(usedByOtherSlot ? "Đã dùng cho đội còn lại" : group.label)}</small></span>
                        <i class="fas ${usedByOtherSlot ? "fa-lock" : "fa-check"}"></i>
                    </button>`;
                }).join("")}
            </section>`).join("")}`;
        sourceMatchPickerDialog.querySelector("[data-source-match-count]").textContent = `${picker.sources.length} trận có thể chọn`;
    }

    function openSourceMatchPicker(button) {
        if (!state.matchEditor || !sourceMatchPickerDialog) return;
        state.matchEditor.draft = readMatchEditor();
        const editor = state.matchEditor;
        const target = matchEditorTarget(editor);
        const slotIndex = Number(button.closest("[data-slot-index]")?.dataset.slotIndex);
        const slot = editor.draft.slots[slotIndex];
        if (!slot) return;
        const sources = graphLocations().matches
            .filter((source) => source.match.matchKey !== editor.draft.matchKey && comesBefore(source, target));

        state.sourceMatchPicker = {
            slotIndex,
            sources,
            selectedKey: slot.sourceMatchKey || ""
        };
        sourceMatchPickerDialog.querySelector("[data-source-match-picker-context]").textContent =
            `Nguồn đội ${slotIndex + 1} · ${sourceLabels[slot.sourceType] || slot.sourceType}`;
        const search = sourceMatchPickerDialog.querySelector("[data-floating-source-search]");
        search.value = "";
        renderSourceMatchPickerList();
        sourceMatchPickerDialog.classList.remove("d-none");
        sourceMatchPickerDialog.classList.remove("is-dragged");
        positionSourceMatchPicker();
        search.focus();
    }

    function filterFloatingSourcePicker(input) {
        const query = normalizeSearch(input.value);
        let visible = 0;
        const picker = state.sourceMatchPicker;
        sourceMatchPickerDialog?.querySelectorAll("[data-source-option-group]").forEach((group) => {
            let groupVisible = 0;
            group.querySelectorAll("[data-source-match-value]").forEach((item) => {
                const matches = !query || normalizeSearch(item.dataset.sourceSearch).includes(query);
                item.classList.toggle("d-none", !matches);
                if (matches) { visible += 1; groupVisible += 1; }
            });
            group.classList.toggle("d-none", groupVisible === 0);
        });
        const count = sourceMatchPickerDialog?.querySelector("[data-source-match-count]");
        if (count && picker) count.textContent = query
            ? `${visible}/${picker.sources.length} trận phù hợp`
            : `${picker.sources.length} trận có thể chọn`;
    }

    function chooseSourceMatch(value) {
        const picker = state.sourceMatchPicker;
        if (!picker || !state.matchEditor) return;
        const draft = readMatchEditor();
        draft.slots[picker.slotIndex].sourceMatchKey = value || null;
        const duplicate = duplicateMatchSource(draft);
        if (duplicate) {
            showMatchEditorSourceError(duplicate);
            showMessage("error", duplicate.message);
            return;
        }
        state.matchEditor.draft = draft;
        closeSourceMatchPicker();
        renderMatchEditor();
    }

    function enableDrag(element, handle) {
        if (!element || !handle) return;
        handle.addEventListener("pointerdown", (event) => {
            if (event.button !== 0 || event.target.closest("button, input, select, textarea, a")) return;
            const rect = element.getBoundingClientRect();
            const offsetX = event.clientX - rect.left;
            const offsetY = event.clientY - rect.top;
            element.classList.add("is-dragged");
            element.style.left = `${rect.left}px`;
            element.style.top = `${rect.top}px`;
            handle.setPointerCapture(event.pointerId);

            const move = (moveEvent) => {
                const maxLeft = Math.max(8, window.innerWidth - rect.width - 8);
                const maxTop = Math.max(8, window.innerHeight - Math.min(rect.height, window.innerHeight - 16) - 8);
                const left = Math.max(8, Math.min(moveEvent.clientX - offsetX, maxLeft));
                const top = Math.max(8, Math.min(moveEvent.clientY - offsetY, maxTop));
                element.style.left = `${left}px`;
                element.style.top = `${top}px`;
            };
            const stop = () => {
                handle.removeEventListener("pointermove", move);
                handle.removeEventListener("pointerup", stop);
                handle.removeEventListener("pointercancel", stop);
            };
            handle.addEventListener("pointermove", move);
            handle.addEventListener("pointerup", stop);
            handle.addEventListener("pointercancel", stop);
        });
    }

    function sourceDependents(matchKey, groupKey) {
        return state.graph.rounds
            .flatMap((round) => round.groups)
            .flatMap((group) => group.matches)
            .filter((match) => (match.slots || []).some((slot) =>
                (matchKey && ["WINNER_MATCH", "LOSER_MATCH"].includes(slot.sourceType) && slot.sourceMatchKey === matchKey)
                || (groupKey && slot.sourceType === "GROUP_RANK" && slot.sourceGroupKey === groupKey)))
            .map((match) => match.matchLabel || match.matchKey);
    }

    function dependencyPrompt(action, label, dependents) {
        if (!dependents.length) return true;
        return window.confirm(
            `${label} đang được ${dependents.length} trận sử dụng làm nguồn: ${dependents.join(", ")}. ${action}`);
    }

    function clearAdvanceAuditHighlights() {
        root.querySelectorAll(".is-advance-missing-winner,.is-advance-missing-loser")
            .forEach((card) => card.classList.remove("is-advance-missing-winner", "is-advance-missing-loser"));
    }

    function advanceAuditRoundKey(round, roundIndex) {
        return normalizeSourceKey(round?.roundKey) || `__ROUND_${roundIndex}`;
    }

    function advanceAuditForRound(roundIndex, create = false) {
        const round = state.graph?.rounds?.[roundIndex];
        if (!round) return null;
        const key = advanceAuditRoundKey(round, roundIndex);
        if (!state.advanceAudits[key] && create) {
            state.advanceAudits[key] = { visible: true, WINNER_MATCH: [], LOSER_MATCH: [] };
        }
        return state.advanceAudits[key] || null;
    }

    function applyAdvanceAuditState() {
        clearAdvanceAuditHighlights();
        if (!state.graph) return;

        state.graph.rounds.forEach((round, roundIndex) => {
            const audit = advanceAuditForRound(roundIndex);
            const validMatchKeys = new Set(round.groups
                .flatMap((group) => group.matches)
                .map((match) => normalizeSourceKey(match.matchKey)));
            if (audit) {
                audit.WINNER_MATCH = [...new Set(audit.WINNER_MATCH || [])].filter((key) => validMatchKeys.has(key));
                audit.LOSER_MATCH = [...new Set(audit.LOSER_MATCH || [])].filter((key) => validMatchKeys.has(key));
            }

            const winnerKeys = new Set(audit?.WINNER_MATCH || []);
            const loserKeys = new Set(audit?.LOSER_MATCH || []);
            const hasResults = winnerKeys.size > 0 || loserKeys.size > 0;
            const visibilityButton = root.querySelector(`[data-action='toggle-advance-colors'][data-round='${roundIndex}']`);
            if (visibilityButton) {
                visibilityButton.classList.toggle("d-none", !hasResults);
                visibilityButton.title = audit?.visible === false
                    ? "Hiện lại màu cảnh báo của vòng này"
                    : "Ẩn màu cảnh báo của vòng này";
                visibilityButton.innerHTML = audit?.visible === false
                    ? '<i class="fas fa-eye"></i><span>Hiện màu</span>'
                    : '<i class="fas fa-eye-slash"></i><span>Ẩn màu</span>';
            }

            if (!hasResults || audit?.visible === false) return;
            round.groups.forEach((group, groupIndex) => group.matches.forEach((match, matchIndex) => {
                const matchKey = normalizeSourceKey(match.matchKey);
                const card = matchCardAt(roundIndex, groupIndex, matchIndex);
                if (winnerKeys.has(matchKey)) card?.classList.add("is-advance-missing-winner");
                if (loserKeys.has(matchKey)) card?.classList.add("is-advance-missing-loser");
            }));
        });
    }

    function clearAllAdvanceAudits() {
        state.advanceAudits = {};
        applyAdvanceAuditState();
    }

    function toggleAdvanceAuditColors(roundIndex) {
        const audit = advanceAuditForRound(roundIndex);
        if (!audit) return;
        audit.visible = audit.visible === false;
        applyAdvanceAuditState();
    }

    function moveAdvanceAuditRoundKey(oldKey, nextKey) {
        const previous = normalizeSourceKey(oldKey);
        const next = normalizeSourceKey(nextKey);
        if (!previous || !next || previous === next || !state.advanceAudits[previous]) return;
        state.advanceAudits[next] = state.advanceAudits[previous];
        delete state.advanceAudits[previous];
    }

    function matchCardAt(roundIndex, groupIndex, matchIndex) {
        return root.querySelector(`[data-round-index='${roundIndex}'] [data-group-index='${groupIndex}'] [data-match-index='${matchIndex}']`);
    }

    function missingAdvancementForRound(roundIndex, sourceType, locations = graphLocations()) {
        const candidates = locations.matches.filter((location) =>
            location.roundIndex === roundIndex
            && !location.match.isTerminal
            && (sourceType !== "LOSER_MATCH" || !byePassThroughInfo(location.match)));
        return candidates.filter((source) => !locations.matches.some((target) =>
            comesBefore(source, target)
            && (target.match.slots || []).some((slot) =>
                normalizeSourceKey(slot.sourceType) === sourceType
                && normalizeSourceKey(slot.sourceMatchKey) === normalizeSourceKey(source.match.matchKey))));
    }

    function roundTypeLabel(value) {
        return ({
            GROUP_STAGE: "Vòng bảng", KNOCKOUT: "Loại trực tiếp", FINAL: "Chung kết",
            PLACEMENT: "Xếp hạng", LOSER_BRACKET: "Nhánh thua"
        })[normalizeSourceKey(value)] || value || "Chưa xác định";
    }

    function updateBulkAdvanceAuditStatus() {
        if (!bulkAdvanceAuditModal) return;
        const roundInputs = [...bulkAdvanceAuditModal.querySelectorAll("[data-bulk-audit-round]:not(:disabled)")];
        const selectedRoundCount = roundInputs.filter((input) => input.checked).length;
        const selectedSourceCount = bulkAdvanceAuditModal.querySelectorAll("[data-bulk-audit-source]:checked").length;
        const status = bulkAdvanceAuditModal.querySelector("[data-bulk-audit-status]");
        const runButton = bulkAdvanceAuditModal.querySelector("[data-bulk-audit-run]");
        status.textContent = `${selectedRoundCount}/${roundInputs.length} vòng · ${selectedSourceCount} loại kết quả`;
        runButton.disabled = selectedRoundCount === 0 || selectedSourceCount === 0;
    }

    function openBulkAdvanceAudit() {
        if (!bulkAdvanceAuditModal || !window.jQuery) return;
        collectGraph();
        const error = bulkAdvanceAuditModal.querySelector("[data-bulk-audit-error]");
        error.textContent = "";
        error.classList.add("d-none");
        bulkAdvanceAuditModal.querySelector("[data-bulk-audit-rounds]").innerHTML = state.graph.rounds.length
            ? state.graph.rounds.map((round, roundIndex) => {
                const isGroupStage = normalizeSourceKey(round.roundType) === "GROUP_STAGE";
                return `<label class="bte-bulk-audit-round ${isGroupStage ? "is-disabled" : ""}">
                    <input type="checkbox" data-bulk-audit-round value="${roundIndex}" ${isGroupStage ? "disabled" : "checked"} />
                    <code>${esc(round.roundKey || `R${roundIndex + 1}`)}</code>
                    <span><strong>${esc(round.roundLabel || `Vòng ${roundIndex + 1}`)}</strong><small>${esc(isGroupStage ? "Không áp dụng · đi tiếp bằng hạng bảng" : roundTypeLabel(round.roundType))}</small></span>
                </label>`;
            }).join("")
            : '<div class="text-center text-muted p-4">Chưa có vòng nào để kiểm tra.</div>';
        updateBulkAdvanceAuditStatus();
        window.jQuery(bulkAdvanceAuditModal).modal("show");
    }

    function runBulkAdvanceAudit() {
        if (!bulkAdvanceAuditModal || !advanceAuditModal || !window.jQuery) return;
        collectGraph();
        const selectedRounds = [...bulkAdvanceAuditModal.querySelectorAll("[data-bulk-audit-round]:checked")]
            .map((input) => Number(input.value));
        const sourceTypes = [...bulkAdvanceAuditModal.querySelectorAll("[data-bulk-audit-source]:checked")]
            .map((input) => input.value);
        const error = bulkAdvanceAuditModal.querySelector("[data-bulk-audit-error]");
        if (!selectedRounds.length || !sourceTypes.length) {
            error.textContent = !selectedRounds.length
                ? "Vui lòng chọn ít nhất một vòng cần kiểm tra."
                : "Vui lòng chọn đội thắng, đội thua hoặc cả hai.";
            error.classList.remove("d-none");
            return;
        }

        error.classList.add("d-none");
        const locations = graphLocations();
        const missing = [];
        state.advanceAudits = {};
        selectedRounds.forEach((roundIndex) => {
            const round = state.graph.rounds[roundIndex];
            if (!round || normalizeSourceKey(round.roundType) === "GROUP_STAGE") return;
            const audit = advanceAuditForRound(roundIndex, true);
            sourceTypes.forEach((sourceType) => {
                const items = missingAdvancementForRound(roundIndex, sourceType, locations);
                audit[sourceType] = [...new Set(items.map((location) => normalizeSourceKey(location.match.matchKey)))];
                missing.push(...items.map((location) => ({ ...location, sourceType })));
            });
            audit.visible = true;
        });
        applyAdvanceAuditState();

        const summary = advanceAuditModal.querySelector("[data-advance-audit-summary]");
        const list = advanceAuditModal.querySelector("[data-advance-audit-list]");
        advanceAuditModal.querySelector("[data-advance-audit-title]").textContent = "Kết quả kiểm tra nhiều vòng";
        advanceAuditModal.querySelector("[data-advance-audit-context]").textContent =
            `Đã kiểm tra ${selectedRounds.length} vòng với ${sourceTypes.length} loại kết quả. Các trận terminal và vòng bảng được bỏ qua.`;

        if (missing.length === 0) {
            summary.className = "bte-advance-audit-summary is-success";
            summary.innerHTML = '<i class="fas fa-check-circle"></i><div><strong>Không phát hiện thiếu liên kết</strong><span>Tất cả kết quả đã chọn trong các vòng này đều có nơi đi tiếp.</span></div>';
            list.innerHTML = "";
        } else {
            summary.className = "bte-advance-audit-summary is-info";
            summary.innerHTML = `<i class="fas fa-exclamation-triangle"></i><div><strong>Có ${missing.length} kết quả chưa được đi tiếp</strong><span>Các card tương ứng đã được đổi màu; chọn một dòng để đi tới đúng trận.</span></div>`;
            list.innerHTML = missing.map((location) => {
                const isWinner = location.sourceType === "WINNER_MATCH";
                const outcomeTitle = isWinner ? "Đội thắng" : "Đội thua";
                const round = state.graph.rounds[location.roundIndex];
                return `<button class="bte-advance-audit-item ${isWinner ? "is-winner" : "is-loser"}" type="button"
                        data-advance-audit-focus data-round="${location.roundIndex}" data-group="${location.groupIndex}" data-match="${location.matchIndex}">
                        <i class="fas ${isWinner ? "fa-trophy" : "fa-level-down-alt"}"></i>
                        <span><strong>${esc(location.match.matchLabel || location.match.matchKey)} <code>${esc(location.match.matchKey)}</code></strong><small>${esc(round.roundLabel || round.roundKey)} · ${esc(location.group.groupName || location.group.groupKey)}</small></span>
                        <em>${outcomeTitle} chưa có trận đích</em><i class="fas fa-chevron-right"></i>
                    </button>`;
            }).join("");
        }

        window.jQuery(bulkAdvanceAuditModal).one("hidden.bs.modal", () => {
            window.jQuery(advanceAuditModal).modal("show");
        }).modal("hide");
    }

    function showAdvanceAudit(roundIndex, sourceType) {
        if (!advanceAuditModal || !["WINNER_MATCH", "LOSER_MATCH"].includes(sourceType)) return;
        collectGraph();

        const round = state.graph.rounds[roundIndex];
        if (!round) return;

        const summary = advanceAuditModal.querySelector("[data-advance-audit-summary]");
        const list = advanceAuditModal.querySelector("[data-advance-audit-list]");
        const outcomeLabel = sourceType === "WINNER_MATCH" ? "đội thắng" : "đội thua";
        const outcomeTitle = sourceType === "WINNER_MATCH" ? "Đội thắng" : "Đội thua";
        const outcomeIcon = sourceType === "WINNER_MATCH" ? "fa-trophy" : "fa-level-down-alt";
        const roundLabel = round.roundLabel || round.roundKey || `Vòng ${roundIndex + 1}`;

        advanceAuditModal.querySelector("[data-advance-audit-title]").textContent = `Kiểm tra ${outcomeLabel} · ${roundLabel}`;
        advanceAuditModal.querySelector("[data-advance-audit-context]").textContent =
            "Các trận cuối nhánh được bỏ qua. Một kết quả chỉ được tính là đã đi tiếp khi có trận đứng sau sử dụng đúng nguồn này.";

        if (normalizeSourceKey(round.roundType) === "GROUP_STAGE") {
            delete state.advanceAudits[advanceAuditRoundKey(round, roundIndex)];
            applyAdvanceAuditState();
            summary.className = "bte-advance-audit-summary is-info";
            summary.innerHTML = `<i class="fas fa-info-circle"></i><div><strong>Không áp dụng cho vòng bảng</strong><span>${esc(roundLabel)} đi tiếp bằng hạng bảng, không dựa trực tiếp vào ${esc(outcomeLabel)} của từng trận.</span></div>`;
            list.innerHTML = "";
            window.jQuery(advanceAuditModal).modal("show");
            return;
        }

        const missing = missingAdvancementForRound(roundIndex, sourceType);

        const audit = advanceAuditForRound(roundIndex, true);
        audit[sourceType] = [...new Set(missing.map((location) => normalizeSourceKey(location.match.matchKey)))];
        audit.visible = true;
        applyAdvanceAuditState();

        if (missing.length === 0) {
            summary.className = "bte-advance-audit-summary is-success";
            summary.innerHTML = `<i class="fas fa-check-circle"></i><div><strong>Không phát hiện thiếu liên kết</strong><span>Tất cả ${esc(outcomeLabel)} trong ${esc(roundLabel)} đã có nơi đi tiếp.</span></div>`;
            list.innerHTML = "";
        } else {
            summary.className = `bte-advance-audit-summary ${sourceType === "WINNER_MATCH" ? "is-winner" : "is-loser"}`;
            summary.innerHTML = `<i class="fas fa-exclamation-triangle"></i><div><strong>Có ${missing.length} ${esc(outcomeLabel)} chưa được đi tiếp</strong><span>Các card tương ứng đã được đổi màu trên sơ đồ.</span></div>`;
            list.innerHTML = missing.map((location) => `<button class="bte-advance-audit-item ${sourceType === "WINNER_MATCH" ? "is-winner" : "is-loser"}" type="button"
                    data-advance-audit-focus data-round="${location.roundIndex}" data-group="${location.groupIndex}" data-match="${location.matchIndex}">
                    <i class="fas ${outcomeIcon}"></i>
                    <span><strong>${esc(location.match.matchLabel || location.match.matchKey)} <code>${esc(location.match.matchKey)}</code></strong><small>${esc(location.group.groupName || location.group.groupKey)}</small></span>
                    <em>${outcomeTitle} chưa có trận đích</em><i class="fas fa-chevron-right"></i>
                </button>`).join("");
        }

        window.jQuery(advanceAuditModal).modal("show");
    }

    function renderPreview() {
        collectGraph();
        const matchCount = state.graph.rounds.reduce((sum, round) => sum + round.groups.reduce((groupSum, group) => groupSum + group.matches.length, 0), 0);
        const groupCount = state.graph.rounds.reduce((sum, round) => sum + round.groups.length, 0);
        const previewStats = [
            [state.graph.seedCapacity, "Vị trí đội ban đầu"],
            [state.graph.rounds.length, "Vòng đấu"],
            [groupCount, "Bảng/nhánh"],
            [matchCount, "Trận đấu"]
        ];
        document.querySelector("[data-preview-summary]").innerHTML = previewStats
            .map(([value, label]) => `<div><strong>${esc(value)}</strong><span>${esc(label)}</span></div>`)
            .join("");
        const matchLabels = new Map(state.graph.rounds
            .flatMap((round) => round.groups)
            .flatMap((group) => group.matches)
            .map((match) => [match.matchKey, match.matchLabel || match.matchKey]));
        const groupLabels = new Map(state.graph.rounds
            .flatMap((round) => round.groups)
            .map((group) => [group.groupKey, group.groupName || group.groupKey]));
        const sourceLabel = (labels, key) => {
            const label = labels.get(key);
            return label && label !== key ? `${label} (${key})` : (key || "?");
        };
        const slotText = (slot) => {
            if (slot.sourceType === "SEED") return `Đội ban đầu ${slot.seedNumber || "?"}`;
            if (slot.sourceType === "BYE") return "Miễn đấu (BYE)";
            if (slot.sourceType === "GROUP_RANK") return `Hạng ${slot.sourceRank || "?"} · ${sourceLabel(groupLabels, slot.sourceGroupKey)}`;
            return `${sourceLabels[slot.sourceType] || slot.sourceType} · ${sourceLabel(matchLabels, slot.sourceMatchKey)}`;
        };
        document.querySelector("[data-preview-board]").innerHTML = state.graph.rounds.map((round) => `
            <section class="bte-preview-round"><h3>${esc(round.roundLabel)} <code>${esc(round.roundKey)}</code></h3>
                ${round.groups.map((group) => `<div class="bte-preview-group"><strong>${esc(group.groupName)}</strong>
                    ${group.matches.map((match) => `<div class="bte-preview-match"><strong>${esc(match.matchLabel || match.matchKey)}</strong>${match.isTerminal ? `<small>${esc(match.terminalType === "CHAMPION" ? "Vô địch" : match.terminalType === "THIRD_PLACE" ? "Hạng ba" : match.terminalType || "Kết thúc")}</small>` : ""}${match.slots.map((slot) => `<div class="bte-preview-slot">${esc(slotText(slot))}</div>`).join("")}</div>`).join("")}
                </div>`).join("")}
            </section>`).join("");
        window.jQuery("#templatePreviewModal").modal("show");
    }

    function focusIssue(index) {
        const issues = root.querySelector("[data-validation-list]")._issues || [];
        const issue = issues[index];
        if (!issue) return;
        root.querySelectorAll(".is-focused").forEach((node) => node.classList.remove("is-focused"));
        let target = null;
        if (issue.matchKey) {
            const matchTarget = [...root.querySelectorAll("[data-match-key]")]
                .find((x) => x.dataset.matchKey === issue.matchKey);
            target = issue.slotNumber && matchTarget
                ? matchTarget.querySelector(`[data-slot-index='${Number(issue.slotNumber) - 1}']`) || matchTarget
                : matchTarget;
        }
        if (!target && issue.groupKey) target = [...root.querySelectorAll("[data-group-key]")].find((x) => x.dataset.groupKey === issue.groupKey);
        if (!target && issue.roundKey) target = [...root.querySelectorAll("[data-round-key]")].find((x) => x.dataset.roundKey === issue.roundKey);
        if (target) { target.classList.add("is-focused"); target.scrollIntoView({ behavior: "smooth", block: "center", inline: "center" }); }
    }

    root.addEventListener("input", (event) => {
        if (event.target.matches("[data-group-field='groupColor']")) {
            applyGroupTheme(event.target.closest("[data-group-index]"), event.target.value);
        }
        const slotElement = event.target.closest("[data-slot-index]");
        if (slotElement && event.target.matches("[data-slot-field]")) {
            const roundIndex = Number(event.target.closest("[data-round-index]")?.dataset.roundIndex);
            const groupIndex = Number(event.target.closest("[data-group-index]")?.dataset.groupIndex);
            const matchIndex = Number(event.target.closest("[data-match-index]")?.dataset.matchIndex);
            const slotIndex = Number(slotElement.dataset.slotIndex);
            slotElement._sourceBeforeEdit ??= cloneMatch(
                state.graph.rounds[roundIndex].groups[groupIndex].matches[matchIndex].slots[slotIndex]);
        }
        if (event.target.closest("[data-rounds]") || event.target.closest(".bte-sidebar")) {
            markDirty();
            scheduleConnectors();
        }
    });
    root.addEventListener("change", (event) => {
        if (event.target.matches("[data-round-field='roundKey']")) {
            const round = event.target.closest("[data-round-key]");
            const oldKey = round?.dataset.roundKey || "";
            const nextKey = event.target.value.trim().toUpperCase();
            if (oldKey && nextKey && oldKey !== nextKey) {
                collectGraph();
                if (state.connectorFocusRoundKey === oldKey) state.connectorFocusRoundKey = nextKey;
                moveRoundLayoutKey(oldKey, nextKey);
                moveAdvanceAuditRoundKey(oldKey, nextKey);
                render();
                markDirty();
                return;
            }
        }
        if (event.target.matches("[data-match-field='matchKey']")) {
            const card = event.target.closest("[data-match-key]");
            const oldKey = card?.dataset.matchKey || "";
            const nextKey = event.target.value.trim().toUpperCase();
            if (oldKey && nextKey && oldKey !== nextKey) {
                const dependents = sourceDependents(oldKey, null);
                if (!dependencyPrompt("Tiếp tục đổi mã và cập nhật các liên kết này?", `Trận ${oldKey}`, dependents)) {
                    event.target.value = oldKey;
                    return;
                }
                collectGraph();
                state.graph.rounds.flatMap((round) => round.groups).flatMap((group) => group.matches)
                    .flatMap((match) => match.slots || [])
                    .filter((slot) => ["WINNER_MATCH", "LOSER_MATCH"].includes(slot.sourceType) && slot.sourceMatchKey === oldKey)
                    .forEach((slot) => { slot.sourceMatchKey = nextKey; });
                if (state.connectorFocusMatchKey === oldKey) state.connectorFocusMatchKey = nextKey;
                moveMatchLayoutKey(oldKey, nextKey);
                render();
                markDirty();
                return;
            }
        }
        if (event.target.matches("[data-group-field='groupKey']")) {
            const card = event.target.closest("[data-group-key]");
            const oldKey = card?.dataset.groupKey || "";
            const nextKey = event.target.value.trim().toUpperCase();
            if (oldKey && nextKey && oldKey !== nextKey) {
                const dependents = sourceDependents(null, oldKey);
                if (!dependencyPrompt("Tiếp tục đổi mã và cập nhật các liên kết này?", `Bảng/nhánh ${oldKey}`, dependents)) {
                    event.target.value = oldKey;
                    return;
                }
                collectGraph();
                state.graph.rounds.flatMap((round) => round.groups).flatMap((group) => group.matches)
                    .flatMap((match) => match.slots || [])
                    .filter((slot) => slot.sourceType === "GROUP_RANK" && slot.sourceGroupKey === oldKey)
                    .forEach((slot) => { slot.sourceGroupKey = nextKey; });
                render();
                markDirty();
                return;
            }
        }
        if (event.target.matches("[data-slot-field]")) {
            const roundIndex = Number(event.target.closest("[data-round-index]")?.dataset.roundIndex);
            const groupIndex = Number(event.target.closest("[data-group-index]")?.dataset.groupIndex);
            const matchIndex = Number(event.target.closest("[data-match-index]")?.dataset.matchIndex);
            const slotElement = event.target.closest("[data-slot-index]");
            const slotIndex = Number(slotElement?.dataset.slotIndex);
            const previousSlot = slotElement?._sourceBeforeEdit
                || cloneMatch(state.graph.rounds[roundIndex].groups[groupIndex].matches[matchIndex].slots[slotIndex]);
            if (slotElement) delete slotElement._sourceBeforeEdit;
            const sourceTypeChanged = event.target.matches("[data-slot-field='sourceType']");
            collectGraph();
            const match = state.graph.rounds[roundIndex].groups[groupIndex].matches[matchIndex];
            const duplicate = duplicateMatchSource(match);
            if (duplicate) {
                match.slots[slotIndex] = previousSlot;
                render();
                const restoredCard = [...root.querySelectorAll("[data-match-key]")]
                    .find((item) => item.dataset.matchKey === match.matchKey);
                restoredCard?.querySelectorAll("[data-slot-index]").forEach((slot) => slot.classList.add("is-duplicate-source"));
                showMessage("error", duplicate.message);
                scheduleLocalRecoverySave();
                return;
            }
            if (sourceTypeChanged) render();
            else scheduleConnectors();
            markDirty();
        }
    });

    matchEditorModal?.addEventListener("click", (event) => {
        const button = event.target.closest("[data-source-match-open]");
        if (button) openSourceMatchPicker(button);
    });
    matchEditorModal?.addEventListener("change", (event) => {
        if (!state.matchEditor) return;
        if (event.target.matches("[data-slot-field='sourceType']")) {
            closeSourceMatchPicker();
            state.matchEditor.draft = readMatchEditor();
            renderMatchEditor();
            validateMatchEditorSources(state.matchEditor.draft);
            return;
        }
        if (event.target.closest("[data-slot-index]")) validateMatchEditorSources();
        if (event.target.matches("[data-match-editor-field='terminalType']") && event.target.value) {
            matchEditorModal.querySelector("[data-match-editor-field='isTerminal']").checked = true;
        }
    });
    matchEditorModal?.addEventListener("input", (event) => {
        if (state.matchEditor && event.target.closest("[data-slot-index]")) validateMatchEditorSources();
    });
    matchEditorModal?.querySelector("[data-match-editor-save]")?.addEventListener("click", saveMatchEditor);
    if (matchEditorModal && window.jQuery) {
        window.jQuery(matchEditorModal).on("hidden.bs.modal", () => {
            closeSourceMatchPicker();
            state.matchEditor = null;
        });
    }
    sourceMatchPickerDialog?.addEventListener("input", (event) => {
        if (event.target.matches("[data-floating-source-search]")) filterFloatingSourcePicker(event.target);
    });
    sourceMatchPickerDialog?.addEventListener("click", (event) => {
        if (event.target.closest("[data-source-picker-close]")) { closeSourceMatchPicker(); return; }
        const option = event.target.closest("[data-source-match-value]");
        if (option) chooseSourceMatch(option.dataset.sourceMatchValue || "");
    });
    enableDrag(matchEditorModal?.querySelector(".modal-dialog"), matchEditorModal?.querySelector("[data-drag-handle]"));
    enableDrag(sourceMatchPickerDialog, sourceMatchPickerDialog?.querySelector("[data-drag-handle]"));
    bulkMatchModal?.querySelector("[data-bulk-match-create]")?.addEventListener("click", createBulkMatches);
    bulkMatchModal?.querySelector("[data-bulk-match-count]")?.addEventListener("keydown", (event) => {
        if (event.key === "Enter") {
            event.preventDefault();
            createBulkMatches();
        }
    });
    if (bulkMatchModal && window.jQuery) {
        window.jQuery(bulkMatchModal).on("hidden.bs.modal", () => { state.bulkMatchEditor = null; });
    }
    initialTeamNumberingModal?.querySelector("[data-initial-team-numbering-apply]")?.addEventListener("click", applyInitialTeamNumbering);
    if (initialTeamNumberingModal && window.jQuery) {
        window.jQuery(initialTeamNumberingModal).on("hidden.bs.modal", () => { state.initialTeamNumberingEditor = null; });
    }
    byePassModal?.addEventListener("input", (event) => {
        if (event.target.matches("[data-bye-pass-search]")) filterByePassPositions();
    });
    byePassModal?.addEventListener("change", (event) => {
        const input = event.target.closest("[data-bye-pass-seed]");
        if (!input || input.disabled || !state.byePassEditor) return;
        const seedNumber = Number(input.value);
        if (input.checked) state.byePassEditor.selectedSeeds.add(seedNumber);
        else state.byePassEditor.selectedSeeds.delete(seedNumber);
        updateByePassSelectionState();
    });
    byePassModal?.querySelector("[data-bye-pass-create]")?.addEventListener("click", createByePasses);
    if (byePassModal && window.jQuery) {
        window.jQuery(byePassModal).on("hidden.bs.modal", () => { state.byePassEditor = null; });
    }
    quickPairModal?.addEventListener("input", (event) => {
        if (event.target.matches("[data-quick-pair-search]")) filterQuickPairSources(event.target);
    });
    quickPairModal?.addEventListener("change", (event) => {
        if (event.target.matches("[data-quick-pair-replace]") && state.quickPairEditor) {
            state.quickPairEditor.replaceExisting = event.target.checked;
            state.quickPairEditor.selectedIds = [];
            refreshQuickPairSources();
            renderQuickPairEditor();
        }
    });
    quickPairModal?.addEventListener("click", (event) => {
        const source = event.target.closest("[data-quick-pair-source-id]");
        if (source) { toggleQuickPairSource(source.dataset.quickPairSourceId); return; }
        const selectAll = event.target.closest("[data-quick-pair-select-all]");
        if (selectAll) { selectAllQuickPairSources(selectAll.dataset.quickPairSelectAll); return; }
        const remove = event.target.closest("[data-quick-pair-remove]");
        if (remove) { toggleQuickPairSource(remove.dataset.quickPairRemove); return; }
        const move = event.target.closest("[data-quick-pair-move]");
        if (move && state.quickPairEditor) {
            const from = Number(move.dataset.quickPairIndex);
            const to = from + Number(move.dataset.quickPairMove);
            if (from >= 0 && to >= 0 && to < state.quickPairEditor.selectedIds.length) {
                const [item] = state.quickPairEditor.selectedIds.splice(from, 1);
                state.quickPairEditor.selectedIds.splice(to, 0, item);
                renderQuickPairEditor();
            }
        }
    });
    quickPairModal?.querySelector("[data-quick-pair-apply]")?.addEventListener("click", applyQuickPairs);
    if (quickPairModal && window.jQuery) {
        window.jQuery(quickPairModal).on("hidden.bs.modal", () => { state.quickPairEditor = null; });
    }
    bulkAdvanceAuditModal?.addEventListener("change", (event) => {
        if (event.target.matches("[data-bulk-audit-round], [data-bulk-audit-source]")) {
            bulkAdvanceAuditModal.querySelector("[data-bulk-audit-error]").classList.add("d-none");
            updateBulkAdvanceAuditStatus();
        }
    });
    bulkAdvanceAuditModal?.addEventListener("click", (event) => {
        if (event.target.closest("[data-bulk-audit-select-all]")) {
            bulkAdvanceAuditModal.querySelectorAll("[data-bulk-audit-round]:not(:disabled)")
                .forEach((input) => { input.checked = true; });
            updateBulkAdvanceAuditStatus();
            return;
        }
        if (event.target.closest("[data-bulk-audit-clear-all]")) {
            bulkAdvanceAuditModal.querySelectorAll("[data-bulk-audit-round]")
                .forEach((input) => { input.checked = false; });
            updateBulkAdvanceAuditStatus();
            return;
        }
        if (event.target.closest("[data-bulk-audit-run]")) runBulkAdvanceAudit();
    });
    advanceAuditModal?.addEventListener("click", (event) => {
        if (event.target.closest("[data-advance-audit-clear]")) {
            clearAllAdvanceAudits();
            window.jQuery(advanceAuditModal).modal("hide");
            return;
        }
        const focus = event.target.closest("[data-advance-audit-focus]");
        if (!focus) return;
        const card = matchCardAt(Number(focus.dataset.round), Number(focus.dataset.group), Number(focus.dataset.match));
        window.jQuery(advanceAuditModal).modal("hide");
        window.setTimeout(() => {
            root.querySelectorAll(".is-focused").forEach((node) => node.classList.remove("is-focused"));
            card?.classList.add("is-focused");
            card?.scrollIntoView({ behavior: "smooth", block: "center", inline: "center" });
        }, 180);
    });

    root.addEventListener("pointerdown", (event) => {
        const roundHandle = event.target.closest("[data-round-drag-handle]");
        if (roundHandle) { beginRoundPositionDrag(roundHandle, event); return; }
        const matchHandle = event.target.closest("[data-match-drag-handle]");
        if (matchHandle) { beginMatchPositionDrag(matchHandle, event); return; }
        const connectionHandle = event.target.closest("[data-connection-source]");
        if (connectionHandle) beginConnectionDrag(connectionHandle, event);
    });

    root.addEventListener("click", (event) => {
        if (state.busy || connectionDrag || matchPositionDrag || roundPositionDrag) return;
        const card = event.target.closest("[data-match-key]");
        if (card && canvasStage?.contains(card)) {
            setConnectorFocus(card.dataset.matchKey);
            return;
        }
        const roundHead = event.target.closest(".bte-round__head");
        if (roundHead && canvasStage?.contains(roundHead)) {
            if (event.target.closest("button,input,select,textarea,a,label,[contenteditable='true'],[role='button']")) return;
            if (performance.now() < suppressRoundFocusUntil) return;
            const round = roundHead.closest("[data-round-key]");
            if (round) setConnectorRoundFocus(round.dataset.roundKey);
            return;
        }
        if (event.target.closest(".bte-canvas-scroll")) setConnectorFocus(null);
    });

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape" && hasConnectorFocus()
            && !document.querySelector(".modal.show")) {
            setConnectorFocus(null);
        }
    });

    root.addEventListener("dblclick", (event) => {
        const matchHandle = event.target.closest("[data-match-drag-handle]");
        const card = matchHandle?.closest("[data-match-key]");
        if (card) {
            event.preventDefault();
            resetMatchPosition(card.dataset.matchKey);
            showMessage("success", `Đã đưa card ${card.dataset.matchKey} về vị trí mặc định.`);
            return;
        }
        const roundHandle = event.target.closest("[data-round-drag-handle]");
        const round = roundHandle?.closest("[data-round-key]");
        if (!round || event.target.closest("[data-action]")) return;
        event.preventDefault();
        resetRoundPosition(round.dataset.roundKey);
        showMessage("success", `Đã đưa vòng ${round.dataset.roundKey} về vị trí mặc định.`);
    });

    root.addEventListener("click", async (event) => {
        const button = event.target.closest("[data-action]");
        if (!button || state.busy) return;
        const action = button.dataset.action;
        try {
            if (action === "save") { if (state.readOnly) await createDraftVersion(); else await save(); }
            if (action === "validate") await validate();
            if (action === "publish") await publish();
            if (action === "preview") renderPreview();
            if (action === "graph-zoom-out") setCanvasZoom(state.canvasZoom - .1);
            if (action === "graph-zoom-reset") setCanvasZoom(1);
            if (action === "graph-zoom-in") setCanvasZoom(state.canvasZoom + .1);
            if (action === "graph-zoom-fit") fitCanvasWidth();
            if (action === "toggle-canvas-focus") toggleCanvasFocus();
            if (action === "reset-all-match-positions") resetAllMatchPositions();
            if (action === "bulk-audit") openBulkAdvanceAudit();
            if (action === "audit-advancement") showAdvanceAudit(Number(button.dataset.round), button.dataset.sourceType);
            if (action === "toggle-advance-colors") toggleAdvanceAuditColors(Number(button.dataset.round));
            if (action === "add-round") addRound();
            if (action === "add-group") addGroup(Number(button.dataset.round));
            if (action === "add-match") addMatch(Number(button.dataset.round), Number(button.dataset.group));
            if (action === "add-matches-bulk") openBulkMatchEditor(Number(button.dataset.round), Number(button.dataset.group));
            if (action === "number-initial-teams") openInitialTeamNumbering(Number(button.dataset.round));
            if (action === "add-bye-pass") openByePassEditor(Number(button.dataset.round), Number(button.dataset.group));
            if (action === "quick-pair") openQuickPairEditor(Number(button.dataset.round), Number(button.dataset.group));
            if (action === "edit-match") {
                openMatchEditor(Number(button.dataset.round), Number(button.dataset.group), Number(button.dataset.match));
            }
            if (action === "delete-round") {
                collectGraph();
                const round = state.graph.rounds[Number(button.dataset.round)];
                const dependents = [...new Set([
                    ...round.groups.flatMap((group) => sourceDependents(null, group.groupKey)),
                    ...round.groups.flatMap((group) => group.matches.flatMap((match) => sourceDependents(match.matchKey, null)))
                ])];
                if (dependencyPrompt("Nếu xóa, các vị trí đó sẽ báo lỗi cho tới khi được sửa.", `Vòng ${round.roundKey}`, dependents)
                    && window.confirm("Xóa vòng và toàn bộ bảng/trận bên trong?")) {
                    removeMatchLayouts(round.groups.flatMap((group) => group.matches.map((match) => match.matchKey)));
                    removeRoundLayout(round.roundKey);
                    delete state.advanceAudits[advanceAuditRoundKey(round, Number(button.dataset.round))];
                    state.graph.rounds.splice(Number(button.dataset.round), 1);
                    render();
                    markDirty();
                }
            }
            if (action === "delete-group") {
                collectGraph();
                const groups = state.graph.rounds[Number(button.dataset.round)].groups;
                const group = groups[Number(button.dataset.group)];
                const dependents = [...new Set([
                    ...sourceDependents(null, group.groupKey),
                    ...group.matches.flatMap((match) => sourceDependents(match.matchKey, null))
                ])];
                if (dependencyPrompt("Nếu xóa, các vị trí đó sẽ báo lỗi cho tới khi được sửa.", `Bảng/nhánh ${group.groupKey}`, dependents)
                    && window.confirm("Xóa bảng và toàn bộ trận bên trong?")) {
                    removeMatchLayouts(group.matches.map((match) => match.matchKey));
                    groups.splice(Number(button.dataset.group), 1);
                    render();
                    markDirty();
                }
            }
            if (action === "delete-match") {
                collectGraph();
                const matches = state.graph.rounds[Number(button.dataset.round)].groups[Number(button.dataset.group)].matches;
                const match = matches[Number(button.dataset.match)];
                const dependents = sourceDependents(match.matchKey, null);
                const byePass = byePassThroughInfo(match);
                const itemLabel = byePass ? `Suất BYE · Đội ban đầu ${byePass.seedNumber}` : `Trận ${match.matchKey}`;
                if (dependencyPrompt("Nếu xóa, các vị trí đó sẽ báo lỗi cho tới khi được sửa.", itemLabel, dependents)
                    && window.confirm(byePass ? "Xóa suất BYE này?" : "Xóa trận này?")) {
                    removeMatchLayouts([match.matchKey]);
                    matches.splice(Number(button.dataset.match), 1);
                    render();
                    markDirty();
                }
            }
            if (action === "focus-issue") focusIssue(Number(button.dataset.issue));
        } catch (error) { showMessage("error", error.message); }
    });

    window.addEventListener("beforeunload", (event) => {
        if (!state.dirty) return;
        event.preventDefault();
        event.returnValue = "";
    });
    window.addEventListener("resize", scheduleConnectors);

    updateCanvasViewControls();
    syncCanvasStage();
    loadGraph();
})();
