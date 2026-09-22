(function (global) {
    "use strict";
    const escape = value => String(value ?? "").replace(/[&<>"']/g, char => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[char]));
    const empty = () => [1, 2, 3].map(partNumber => ({partNumber, scoreTeam1: 0, scoreTeam2: 0}));
    const partsOf = match => match.relayScores?.parts || empty();
    function render(match, editable = true, prefix = "relay") {
        const allocation = !!match.relayScores?.requiresAllocation;
        return `<div class="relay-scoreboard" data-relay-board data-relay-prefix="${escape(prefix)}" data-relay-version="${Number(match.relayScores?.version || 0)}">
            <div class="relay-totals" aria-label="Tổng điểm toàn trận">
                <div class="relay-total-label">Tổng điểm · 3 đội con</div>
                ${[1, 2].map(side => `<div class="relay-total-team relay-side-${side}"><span>${escape(match[`team${side}Text`] || `Đội ${side}`)}</span><strong data-relay-total="${side}" data-live-score="team${side}">${Number(match[`scoreTeam${side}`] || 0)}</strong></div>`).join("")}
            </div>
            ${allocation ? `<div class="relay-allocation" role="status">Trận đã có tỷ số <b>${Number(match.scoreTeam1)}–${Number(match.scoreTeam2)}</b>. Nhập điểm của 3 đội con khớp tổng này rồi bấm <b>Lưu phân bổ</b> để tiếp tục. <span data-relay-allocation-total></span></div>` : ""}
            <div class="relay-parts">
            ${partsOf(match).map(part => `<section class="relay-part" data-relay-part="${part.partNumber}" aria-labelledby="${prefix}Part${part.partNumber}">
                <div class="relay-part-heading"><h3 id="${prefix}Part${part.partNumber}">Đội con ${part.partNumber}</h3><span>${editable ? "Cộng/trừ tự lưu" : "Điểm đã lưu"}</span></div>
                <div class="relay-part-teams">${[1, 2].map(side => {
                    const id = `${prefix}Score${part.partNumber}_${side}`;
                    const name = match[`team${side}Text`] || `Đội ${side}`;
                    return `<div class="relay-team-row relay-side-${side}"><label class="relay-team-name" for="${id}"><small>Đội ${side}</small>${escape(name)}</label>
                        ${editable ? `<div class="relay-stepper">
                            <button type="button" class="relay-step relay-step-minus" data-score-target="${id}" data-score-step="-1" aria-label="Trừ 1 điểm ${escape(name)}, đội con ${part.partNumber}">−</button>
                            <input id="${id}" data-relay-input data-part="${part.partNumber}" data-side="${side}" inputmode="numeric" pattern="[0-9]*" autocomplete="off" enterkeyhint="done" value="${part[`scoreTeam${side}`]}" aria-label="Điểm ${escape(name)}, đội con ${part.partNumber}">
                            <button type="button" class="relay-step relay-step-plus" data-score-target="${id}" data-score-step="1" aria-label="Cộng 1 điểm ${escape(name)}, đội con ${part.partNumber}">+</button>
                        </div>` : `<strong class="relay-read-score" data-relay-read="${part.partNumber}_${side}">${part[`scoreTeam${side}`]}</strong>`}
                    </div>`;
                }).join("")}</div>
                ${editable ? `<div class="relay-part-status" data-relay-status="${part.partNumber}" role="status" aria-live="polite">Sẵn sàng</div>` : ""}
            </section>`).join("")}
            </div>
        </div>`;
    }
    function read(root, match) {
        const parts = [1, 2, 3].map(partNumber => {
            const score = side => Number(root.querySelector(`[data-relay-input][data-part="${partNumber}"][data-side="${side}"]`)?.value || 0);
            return {partNumber, scoreTeam1: score(1), scoreTeam2: score(2)};
        });
        const changed = parts.filter((p, i) => p.scoreTeam1 !== partsOf(match)[i].scoreTeam1 || p.scoreTeam2 !== partsOf(match)[i].scoreTeam2);
        return {expectedVersion: Number(root.querySelector("[data-relay-board]")?.dataset.relayVersion ?? match.relayScores?.version ?? 0), allocateExisting: !!match.relayScores?.requiresAllocation,
            changedPart: changed.length === 1 ? changed[0].partNumber : null, parts};
    }
    function totals(parts) {
        return {scoreTeam1: parts.reduce((sum, p) => sum + p.scoreTeam1, 0), scoreTeam2: parts.reduce((sum, p) => sum + p.scoreTeam2, 0)};
    }
    function preview(root, match) {
        const total = totals(read(root, match).parts);
        if (match.relayScores?.requiresAllocation) {
            const allocation = root.querySelector("[data-relay-allocation-total]");
            if (allocation) allocation.textContent = `Đang phân bổ: ${total.scoreTeam1}–${total.scoreTeam2}.`;
        } else {
            [1, 2].forEach(side => { const el = root.querySelector(`[data-relay-total="${side}"]`); if (el) el.textContent = total[`scoreTeam${side}`]; });
        }
        return total;
    }
    function sync(root, match) {
        if (!root) return;
        const board = root.querySelector("[data-relay-board]");
        if (board && match.relayScores) board.dataset.relayVersion = match.relayScores.version;
        partsOf(match).forEach(part => [1, 2].forEach(side => {
            const input = root.querySelector(`[data-relay-input][data-part="${part.partNumber}"][data-side="${side}"]`);
            const label = root.querySelector(`[data-relay-read="${part.partNumber}_${side}"]`);
            if (input) input.value = part[`scoreTeam${side}`];
            if (label) label.textContent = part[`scoreTeam${side}`];
        }));
        [1, 2].forEach(side => { const el = root.querySelector(`[data-relay-total="${side}"]`); if (el) el.textContent = match[`scoreTeam${side}`]; });
    }
    function status(root, part, message, type = "") {
        if (!root) return;
        root.querySelectorAll(part ? `[data-relay-status="${part}"]` : "[data-relay-status]").forEach(el => {
            el.textContent = message; el.dataset.state = type;
        });
    }
    function valid(value) {
        return value && Number.isSafeInteger(value.version) && value.version >= 0
            && typeof value.requiresAllocation === "boolean" && Array.isArray(value.parts) && value.parts.length === 3
            && value.parts.every((part, i) => part.partNumber === i + 1 && Number.isInteger(part.scoreTeam1)
                && part.scoreTeam1 >= 0 && Number.isInteger(part.scoreTeam2) && part.scoreTeam2 >= 0);
    }
    function historyDetail(history) {
        if (!history.relayPartsJson) return "";
        try {
            const parts = JSON.parse(history.relayPartsJson);
            return `<div class="relay-history">${history.relayPartNumber ? `Chấm đội con ${Number(history.relayPartNumber)} · ` : ""}${parts.map(p => `Con ${Number(p.partNumber)}: ${Number(p.scoreTeam1)}–${Number(p.scoreTeam2)}`).join(" · ")}</div>`;
        } catch { return ""; }
    }
    global.HanakaRelayScoreboard = {render, read, totals, preview, sync, status, valid, historyDetail};
})(window);
