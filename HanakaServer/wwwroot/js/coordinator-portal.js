(() => {
    "use strict";
    const $ = id => document.getElementById(id);
    const labels = { NOT_STARTED: "Chưa bắt đầu", PREPARING: "Chuẩn bị đánh", IN_PROGRESS: "Đang đánh", COMPLETED: "Kết thúc" };
    const csrf = document.querySelector('[name="__RequestVerificationToken"]').value;
    const realtime = window.HanakaPublicRealtime;
    const matchDate = new Intl.DateTimeFormat("vi-VN", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" });
    const cards = new Map();
    let tournamentId = "", matches = new Map(), updates = new Map(), allowed = false;
    let requestId = 0, controller, refreshTimer, toastTimer, editing, saving = false, loggingOut = false;

    function element(tag, className, value) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (value !== undefined) node.textContent = value;
        return node;
    }
    function status(match) {
        return match.isCompleted ? "COMPLETED" : labels[match.matchStatus] ? match.matchStatus : "NOT_STARTED";
    }
    function editable(match) {
        return allowed && match && ["NOT_STARTED", "PREPARING"].includes(status(match)) && Number(match.stateVersion) > 0;
    }
    function teamName(match, side) {
        return match[`team${side}`]?.displayName || match[`team${side}Text`] || "Chờ xác định đội";
    }
    function merge(base, patch) {
        if (!patch || Number(patch.stateVersion) < Number(base.stateVersion)) return base;
        const result = { ...base, ...patch };
        // Socket snapshots carry IDs, not display names. Do not show the old team's name after a bracket change.
        for (const side of [1, 2]) {
            const field = `team${side}RegistrationId`;
            if (field in patch && patch[field] !== base[field] && !(`team${side}` in patch)) {
                result[`team${side}`] = null;
                result[`team${side}Text`] = "Đang cập nhật đội…";
            }
        }
        return result;
    }
    function pageError(message) {
        $("page-error").textContent = message || "";
        $("page-error").hidden = !message;
    }
    function revoke(message, expired) {
        allowed = false;
        if (expired) {
            const link = element("a", "", " Đăng nhập lại");
            link.href = "/CoordinatorPortal/Login";
            pageError(message);
            $("page-error").append(link);
        } else pageError(message);
        render();
    }
    async function api(path, options = {}) {
        let validate = body => body && typeof body === "object";
        if (path.endsWith("/session")) validate = body => body && Number.isSafeInteger(body.userId) && body.userId > 0
            && Array.isArray(body.tournaments) && body.tournaments.every(t => Number.isSafeInteger(t.tournamentId) && t.tournamentId > 0 && typeof t.title === "string");
        else if (path.endsWith("/rounds-with-matches")) validate = body => body && Array.isArray(body.rounds)
            && body.rounds.every(r => Array.isArray(r.groups) && r.groups.every(g => Array.isArray(g.matches)
                && g.matches.every(m => Number.isSafeInteger(m.matchId) && m.matchId > 0 && Number.isSafeInteger(m.stateVersion) && m.stateVersion > 0)));
        return window.HanakaCoordinatorHttp.request(path, { validate, ...options,
            headers: { "Content-Type": "application/json", RequestVerificationToken: csrf, ...options.headers } });
    }
    function selectOptions(select, options, placeholder) {
        const previous = select.value;
        select.replaceChildren();
        if (placeholder) select.append(new Option(placeholder, ""));
        for (const [value, title] of options) select.append(new Option(title, value));
        if ([...select.options].some(o => o.value === previous)) select.value = previous;
    }
    function setTournament(id) {
        id = String(id || "");
        if (id === tournamentId) return;
        if (tournamentId) realtime?.unsubscribeTournament(Number(tournamentId));
        tournamentId = id;
        matches.clear(); updates.clear(); cards.clear();
        $("coordination-dialog").close(); editing = null;
        $("court-filter").value = "";
        $("round-filter").value = "";
        if (id) realtime?.subscribeTournament(Number(id));
    }
    async function refresh() {
        const sequence = ++requestId;
        controller?.abort();
        controller = new AbortController();
        const signal = controller.signal;
        $("refresh").disabled = true;
        $("matches").setAttribute("aria-busy", "true");
        try {
            const session = await api("/api/coordinator-portal/session", { signal });
            if (sequence !== requestId) return false;
            $("operator-name").textContent = session.fullName || "Người điều phối";
            const tournaments = session.tournaments || [];
            const selected = tournaments.find(t => String(t.tournamentId) === tournamentId) || tournaments[0];
            setTournament(selected?.tournamentId);
            selectOptions($("tournament"), tournaments.map(t => [String(t.tournamentId), t.title]), tournaments.length ? null : "Chưa được phân công giải");
            $("tournament").value = tournamentId;
            $("tournament").disabled = !tournaments.length;
            allowed = Boolean(tournamentId);
            if (!tournamentId) {
                pageError(""); render();
                $("sync-state").textContent = "Liên hệ ban tổ chức để được phân công giải.";
                return true;
            }
            if (!matches.size) render();
            const data = await api(`/api/tournaments/${tournamentId}/rounds-with-matches`, { signal });
            if (sequence !== requestId) return false;
            const next = new Map();
            for (const round of data.rounds || []) for (const group of round.groups || []) for (const match of group.matches || []) {
                const id = String(match.matchId);
                let row = { ...match, roundId: String(round.tournamentRoundMapId ?? round.roundKey),
                    roundLabel: round.roundLabel || round.roundKey || "Vòng đấu", groupName: group.groupName || "" };
                const current = matches.get(id);
                if (current && Number(current.stateVersion) > Number(row.stateVersion)) row = merge(row, current);
                const patch = updates.get(id);
                if (patch && Number(patch.stateVersion) > Number(row.stateVersion)) row = merge(row, patch);
                if (patch && Number(patch.stateVersion) <= Number(match.stateVersion)) updates.delete(id);
                next.set(id, row);
            }
            matches = next;
            rebuildFilters(); render(); pageError("");
            $("sync-state").textContent = `Đã cập nhật lúc ${new Date().toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit" })}`;
            return true;
        } catch (error) {
            if (sequence !== requestId || error.name === "AbortError") return false;
            if ([401, 403].includes(error.status)) revoke(error.message, error.status === 401);
            else { pageError(error.message); render(); }
            $("sync-state").textContent = "Chưa đồng bộ được. Nhấn Làm mới để thử lại.";
            return false;
        } finally {
            if (sequence === requestId) { $("refresh").disabled = false; $("matches").setAttribute("aria-busy", "false"); }
        }
    }
    function scheduleRefresh() {
        clearTimeout(refreshTimer);
        refreshTimer = setTimeout(refresh, 250);
    }
    function rebuildFilters() {
        const courts = [...new Set([...matches.values()].map(m => m.courtText).filter(Boolean))].sort((a, b) => a.localeCompare(b, "vi", { numeric: true }));
        selectOptions($("court-filter"), [["__unassigned", "Chưa xếp sân"], ...courts.map(c => [c, c])], "Tất cả sân");
        const rounds = new Map([...matches.values()].map(m => [m.roundId, m.roundLabel]));
        selectOptions($("round-filter"), [...rounds], "Tất cả vòng");
        $("court-suggestions").replaceChildren(...courts.map(c => new Option(c, c)));
    }
    function render() {
        const focusedMatch = document.activeElement?.closest(".match-card")?.dataset.matchId;
        const all = [...matches.values()];
        const counts = Object.fromEntries(Object.keys(labels).map(key => [key, 0]));
        for (const match of all) counts[status(match)]++;
        for (const key of Object.keys(labels)) $("count-" + key).textContent = counts[key];
        document.querySelectorAll("[data-status]").forEach(button => button.setAttribute("aria-pressed", String(button.dataset.status === $("status-filter").value)));
        const query = $("search").value.trim().toLocaleLowerCase("vi");
        const court = $("court-filter").value, round = $("round-filter").value, state = $("status-filter").value;
        const visible = all.filter(m => (!state || status(m) === state) && (!round || m.roundId === round)
            && (!court || (court === "__unassigned" ? !m.courtText : m.courtText === court))
            && (!query || `${m.matchId} ${teamName(m, 1)} ${teamName(m, 2)}`.toLocaleLowerCase("vi").includes(query)));
        $("match-count").textContent = `${visible.length}/${all.length} trận`;
        // Keep unchanged cards mounted. A score burst on a large schedule must not
        // recreate thousands of cards, event handlers and date formatters per event.
        for (const id of cards.keys()) if (!matches.has(id)) cards.delete(id);
        const nodes = visible.map(match => {
            const id = String(match.matchId), cached = cards.get(id);
            if (cached?.match === match && cached.allowed === allowed) return cached.node;
            const node = card(match);
            cached?.node.replaceWith(node);
            cards.set(id, { match, allowed, node });
            return node;
        });
        const visibleNodes = new Set(nodes), container = $("matches");
        for (const node of [...container.children]) if (!visibleNodes.has(node)) node.remove();
        let next = container.firstElementChild;
        for (const node of nodes) {
            if (node === next) next = next.nextElementSibling;
            else container.insertBefore(node, next);
        }
        if (focusedMatch) {
            const card = [...$("matches").children].find(node => node.dataset.matchId === focusedMatch);
            card?.querySelector("button")?.focus({ preventScroll: true });
        }
        $("empty-state").hidden = visible.length > 0;
        $("empty-state").textContent = !tournamentId ? "Bạn chưa được phân công điều phối giải nào."
            : !all.length ? "Giải chưa có trận đấu hoặc lịch đấu chưa tải xong." : "Không có trận phù hợp với bộ lọc.";
        syncEditor();
    }
    function card(match) {
        const state = status(match);
        const node = element("article", "match-card " + state); node.dataset.matchId = match.matchId;
        const top = element("div", "match-top");
        top.append(element("span", "match-id", `#${match.matchId}`), element("span", "badge", labels[state]));
        const date = match.startAt ? new Date(match.startAt) : null;
        const when = date && !Number.isNaN(date.getTime()) ? matchDate.format(date) : "Chưa xếp giờ";
        node.append(top, element("p", "match-meta", `${match.roundLabel} · ${match.groupName} · ${when}`));
        const teams = element("div", "teams");
        for (const side of [1, 2]) {
            const winner = state === "COMPLETED" && match.winnerRegistrationId != null && match.winnerRegistrationId === match[`team${side}RegistrationId`];
            const team = element("div", "team-line" + (winner ? " winner" : ""));
            team.append(element("span", "", teamName(match, side)), element("strong", "", match[`scoreTeam${side}`] ?? "—")); teams.append(team);
        }
        node.append(teams, element("p", "court-line", match.courtText || "Chưa xếp sân"));
        const footer = element("div", "match-footer");
        const ready = match.team1RegistrationId && match.team2RegistrationId && match.completionReason !== "BYE";
        footer.append(element("p", "muted", ["IN_PROGRESS", "COMPLETED"].includes(state) ? "Trọng tài cập nhật" : ready ? "Sẵn sàng điều phối" : "Chờ đủ hai đội"));
        if (editable(match)) {
            const button = element("button", "", "Điều phối"); button.type = "button";
            button.setAttribute("aria-label", `Điều phối trận ${match.matchId}`);
            button.addEventListener("click", () => openEditor(String(match.matchId))); footer.append(button);
        }
        node.append(footer); return node;
    }
    function openEditor(id) {
        const match = matches.get(id);
        if (!editable(match)) return;
        editing = { id, version: Number(match.stateVersion), conflicted: false };
        $("edit-title").textContent = `Điều phối trận #${match.matchId}`;
        $("edit-teams").textContent = `${teamName(match, 1)} — ${teamName(match, 2)}`;
        $("edit-court").value = match.courtText || "";
        $("edit-preparing").checked = status(match) === "PREPARING";
        $("edit-error").textContent = "";
        syncEditor();
        if (!$("coordination-dialog").open) $("coordination-dialog").showModal();
    }
    function syncEditor() {
        if (!editing) return;
        const match = matches.get(editing.id);
        const locked = !editable(match), stale = !locked && (editing.conflicted || Number(match.stateVersion) !== editing.version);
        const missingTeams = !match?.team1RegistrationId || !match?.team2RegistrationId || match?.completionReason === "BYE";
        $("edit-court").disabled = saving || locked;
        $("edit-preparing").disabled = saving || locked || missingTeams;
        $("save-coordination").disabled = saving || locked || stale;
        $("save-coordination").textContent = saving ? "Đang lưu…" : "Lưu điều phối";
        $("close-editor").disabled = saving;
        $("reload-editor").hidden = !stale;
        $("reload-editor").disabled = saving;
        const message = locked ? "Trận đã bắt đầu, kết thúc hoặc quyền điều phối đã thay đổi. Không thể sửa sân và trạng thái."
            : stale ? "Trận vừa được cập nhật. Tải dữ liệu mới và kiểm tra lại trước khi lưu."
            : missingTeams ? "Trận chưa đủ hai đội. Bạn có thể xếp sân, chưa thể gọi chuẩn bị." : "";
        $("edit-warning").textContent = message; $("edit-warning").hidden = !message;
    }
    function applyUpdate(patch) {
        if (!patch || String(patch.tournamentId) !== tournamentId || !Number.isFinite(Number(patch.stateVersion))) return;
        const id = String(patch.matchId), previous = updates.get(id);
        if (!previous || Number(patch.stateVersion) >= Number(previous.stateVersion)) updates.set(id, patch);
        const current = matches.get(id);
        if (current && Number(patch.stateVersion) >= Number(current.stateVersion)) {
            const changedTeams = [1, 2].some(side => `team${side}RegistrationId` in patch && patch[`team${side}RegistrationId`] !== current[`team${side}RegistrationId`]);
            const updated = merge(current, patch);
            matches.set(id, updated);
            if (updated.courtText !== current.courtText) rebuildFilters();
            render();
            if (changedTeams) scheduleRefresh();
        } else if (!current) scheduleRefresh();
    }

    $("coordination-form").addEventListener("submit", async event => {
        event.preventDefault();
        if (saving || !editing || $("save-coordination").disabled) return;
        const court = $("edit-court").value.trim(), preparing = $("edit-preparing").checked;
        if (preparing && !court) { $("edit-error").textContent = "Nhập sân trước khi gọi chuẩn bị đánh."; $("edit-court").focus(); return; }
        const draft = { ...editing };
        const targetTournamentId = tournamentId;
        saving = true; $("edit-error").textContent = ""; syncEditor();
        try {
            const updated = await api(`/api/coordinator-portal/matches/${draft.id}`, { method: "PUT",
                validate: body => body && String(body.matchId) === draft.id && String(body.tournamentId) === targetTournamentId
                    && Number.isSafeInteger(body.stateVersion) && body.stateVersion >= draft.version && labels[body.matchStatus],
                body: JSON.stringify({ expectedVersion: draft.version, courtText: court || null, preparing }) });
            applyUpdate(updated);
            $("coordination-dialog").close(); editing = null;
            $("toast").textContent = `Đã cập nhật điều phối trận #${draft.id}.`;
            $("toast").hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => $("toast").hidden = true, 4000);
        } catch (error) {
            $("edit-error").textContent = error.message;
            if ((error.status === 409 || error.uncertain) && editing) {
                editing.conflicted = true;
                if (error.uncertain) $("edit-error").textContent += " Chưa xác nhận được kết quả lưu. Tải dữ liệu mới trước khi thao tác tiếp.";
                await refresh();
            }
            else if ([401, 403].includes(error.status)) { revoke(error.message, error.status === 401); scheduleRefresh(); }
        } finally { saving = false; syncEditor(); }
    });
    $("reload-editor").addEventListener("click", async () => {
        const id = editing?.id;
        $("reload-editor").disabled = true;
        const ok = await refresh();
        if (ok && editing?.id === id) openEditor(id);
        syncEditor();
    });
    $("close-editor").addEventListener("click", () => { if (!saving) $("coordination-dialog").close(); });
    $("coordination-dialog").addEventListener("cancel", event => { if (saving) event.preventDefault(); });
    $("coordination-dialog").addEventListener("close", () => {
        // The browser may deliver a queued close event after the next match was opened.
        if (!$("coordination-dialog").open) editing = null;
    });
    $("tournament").addEventListener("change", () => { setTournament($("tournament").value); render(); refresh(); });
    $("refresh").addEventListener("click", refresh);
    for (const id of ["court-filter", "round-filter", "status-filter"]) $(id).addEventListener("change", render);
    $("search").addEventListener("input", render);
    document.querySelectorAll("[data-status]").forEach(button => button.addEventListener("click", () => {
        $("status-filter").value = $("status-filter").value === button.dataset.status ? "" : button.dataset.status; render();
    }));
    $("clear-filters").addEventListener("click", () => { for (const id of ["search", "court-filter", "round-filter", "status-filter"]) $(id).value = ""; render(); });
    $("logout").addEventListener("click", async () => {
        if (loggingOut || saving) return;
        loggingOut = true; $("logout").disabled = true;
        try { await api("/api/coordinator-portal/logout", { method: "POST" }); window.location.assign("/CoordinatorPortal/Login"); }
        catch (error) { revoke(error.message, error.status === 401); loggingOut = false; $("logout").disabled = false; }
    });
    realtime?.on(event => {
        if (event.type === "tournament.match.coordination.updated" || event.type === "tournament.match.score.updated") applyUpdate(event.payload);
        else if (event.type === "tournament.bracket.updated" || event.type === "__public_socket_open__") scheduleRefresh();
        else if (event.type === "__public_socket_close__" || event.type === "__public_socket_error__")
            $("sync-state").textContent = "Đang nối lại cập nhật trực tiếp. Lịch vẫn tự tải lại mỗi 30 giây.";
    });
    document.addEventListener("visibilitychange", () => { if (!document.hidden) scheduleRefresh(); });
    window.addEventListener("focus", scheduleRefresh);
    setInterval(() => { if (!document.hidden && !$("refresh").disabled) refresh(); }, 30000);
    refresh();
})();
