(() => {
    "use strict";
    const labels = Object.freeze({ NOT_STARTED: "Chưa bắt đầu", PREPARING: "Chuẩn bị đánh", IN_PROGRESS: "Đang đánh", COMPLETED: "Kết thúc" });
    const fields = ["matchId", "tournamentId", "groupId", "matchStatus", "stateVersion", "courtText", "addressText", "startAt",
        "scoreTeam1", "scoreTeam2", "isCompleted", "winnerRegistrationId", "winnerTeam", "winner", "updatedAt", "videoUrl",
        "team1RegistrationId", "team2RegistrationId", "team1", "team2", "completionReason"];
    const owns = (value, key) => Object.prototype.hasOwnProperty.call(value, key);
    function normalize(value) {
        const result = {};
        for (const key of fields) {
            const pascal = key[0].toUpperCase() + key.slice(1);
            if (owns(value || {}, key)) result[key] = value[key];
            else if (owns(value || {}, pascal)) result[key] = value[pascal];
        }
        return result;
    }
    const version = match => Number.isSafeInteger(Number(match?.stateVersion)) && Number(match.stateVersion) > 0 ? Number(match.stateVersion) : 0;
    function presentation(match) {
        const status = match?.isCompleted ? "COMPLETED" : owns(labels, match?.matchStatus) ? match.matchStatus
            : Number(match?.scoreTeam1) > 0 || Number(match?.scoreTeam2) > 0 ? "IN_PROGRESS" : "NOT_STARTED";
        return { status, label: labels[status] };
    }
    function canCoordinate(match) {
        return version(match) > 0 && ["NOT_STARTED", "PREPARING"].includes(presentation(match).status);
    }
    function merge(current, value) {
        const incoming = normalize(value);
        if (!current) return incoming;
        if (version(current) && !version(incoming)) return current;
        const older = version(incoming) < version(current) || (version(incoming) === version(current)
            && Date.parse(incoming.updatedAt) < Date.parse(current.updatedAt));
        if (older) {
            // An early socket snapshot may have no names. Fill them only for the same participants.
            const result = { ...incoming, ...current };
            for (const side of [1, 2]) {
                const team = `team${side}`, id = `${team}RegistrationId`;
                if (owns(current, id) && current[id] !== incoming[id]) result[team] = current[team] || null;
                else if (!current[team]) result[team] = incoming[team];
            }
            return result;
        }
        const result = { ...current, ...incoming };
        for (const side of [1, 2]) {
            const team = `team${side}`, id = `${team}RegistrationId`;
            if (owns(incoming, id) && incoming[id] !== current[id] && !owns(incoming, team)) result[team] = null;
        }
        if (owns(incoming, "winnerRegistrationId") && incoming.winnerRegistrationId !== current.winnerRegistrationId) {
            if (!owns(incoming, "winner")) result.winner = null;
            if (!owns(incoming, "winnerTeam")) result.winnerTeam = null;
        }
        return result;
    }
    const validSnapshot = match => Number(match?.matchId) > 0 && version(match) > 0
        && owns(labels, match?.matchStatus) && typeof match.isCompleted === "boolean";
    const validSchedule = data => Array.isArray(data?.rounds) && data.rounds.every(round => Array.isArray(round.groups)
        && round.groups.every(group => Array.isArray(group.matches) && group.matches.every(match => Number(match.matchId) > 0)));

    async function mount({ root, body, tournamentId, render, renderMatch, initialize }) {
        const request = window.HanakaCoordinatorHttp.request;
        const lifetime = new AbortController(), signal = lifetime.signal;
        const matches = new Map(), removed = new Set(), cards = new Map(), painted = new WeakMap();
        let data, topology, draft, saving = false, permission = null, checkingPermission = false;
        let readController, permissionController, readSequence = 0, permissionSequence = 0, refreshTimer, permissionError = "";
        const notice = document.createElement("div");
        notice.className = "schedule-notice";
        notice.hidden = true;
        notice.innerHTML = '<span role="status"></span> <button type="button">Tải lại</button>';
        body.before(notice);
        const showNotice = message => { notice.hidden = !message; notice.querySelector("span").textContent = message || ""; };
        const dialog = document.createElement("dialog");
        dialog.className = "schedule-coordination";
        dialog.setAttribute("aria-labelledby", "schedule-coordination-title");
        dialog.innerHTML = `<form novalidate>
            <h2 id="schedule-coordination-title">Điều phối trận đấu</h2>
            <p data-current-match></p>
            <label for="schedule-court">Sân thi đấu</label>
            <input id="schedule-court" name="court" maxlength="100" autocomplete="off" placeholder="Ví dụ: Sân 1" />
            <label class="schedule-coordination__check"><input name="preparing" type="checkbox" /> Chuẩn bị đánh</label>
            <p class="schedule-coordination__hint">Cần đủ hai đội và có sân để chuyển sang chuẩn bị. Trọng tài bắt đầu trận sẽ khóa điều phối.</p>
            <p data-form-message role="status" aria-live="polite"></p>
            <div class="schedule-coordination__actions">
                <button type="button" data-close>Đóng</button>
                <button type="button" data-reload>Nạp lại form</button>
                <button type="submit" data-save>Lưu điều phối</button>
            </div>
        </form>`;
        root.append(dialog);
        const form = dialog.querySelector("form"), court = form.elements.court, preparing = form.elements.preparing;
        const save = form.querySelector("[data-save]"), reload = form.querySelector("[data-reload]");
        const message = form.querySelector("[data-form-message]");
        const authorized = () => permission?.canCoordinate === true && !!permission.requestToken;
        const eachMatch = (payload, callback) => payload.rounds.forEach(round => round.groups.forEach(group => group.matches.forEach(callback)));
        function updateForm() {
            if (!draft) return;
            const match = matches.get(draft.id);
            let reason = "";
            if (!authorized() || draft.actor !== permission?.userId) reason = permissionError || "Bạn không còn quyền điều phối giải này. Đăng nhập và kiểm tra lại phân công.";
            else if (removed.has(draft.id) || !match) reason = "Trận đã được gỡ khỏi lịch thi đấu.";
            else if (!canCoordinate(match)) reason = "Trận đã bắt đầu hoặc kết thúc. Không thể đổi sân hay trạng thái chuẩn bị.";
            else if (draft.version !== version(match)) reason = "Trận vừa được cập nhật. Bản nhập của bạn được giữ lại; nạp lại form trước khi lưu.";
            else if (draft.uncertain) reason = "Chưa xác định được kết quả lưu. Nạp lại form để kiểm tra trước khi thao tác tiếp.";
            const locked = saving || checkingPermission || draft.reloading || !!reason;
            court.disabled = preparing.disabled = !!locked;
            save.disabled = !!locked;
            reload.disabled = saving || checkingPermission || !!draft.reloading;
            form.querySelector("[data-close]").disabled = saving;
            save.textContent = saving ? "Đang lưu…" : "Lưu điều phối";
            message.textContent = reason || draft.error || "";
            form.querySelector("[data-current-match]").textContent = `#${draft.id} · ${presentation(match).label} · Sân hiện tại: ${match?.courtText || "Chưa cập nhật"}`;
        }
        function paintCard(match) {
            const card = cards.get(Number(match.matchId));
            if (!card) return;
            card.querySelector("[data-coordinate-match]").hidden = !authorized() || !canCoordinate(match);
            const signature = JSON.stringify(match);
            if (painted.get(card) === signature) return;
            const template = document.createElement("template");
            template.innerHTML = renderMatch(match, Number(card.querySelector("[data-schedule-match-index]").textContent) - 1);
            const fresh = template.content.firstElementChild;
            card.className = fresh.className;
            card.dataset.matchStatus = fresh.dataset.matchStatus;
            // Keep the existing buttons, keyboard focus, tabs and scroll position during updates.
            for (const selector of ["[data-schedule-match-index]", "[data-schedule-time]", "[data-schedule-court]", "[data-schedule-address]", "[data-schedule-status]",
                '[data-schedule-team-id="1"]', '[data-schedule-team-id="2"]', '[data-schedule-team-side="1"]', '[data-schedule-team-side="2"]',
                '[data-schedule-score-side="1"]', '[data-schedule-score-side="2"]']) {
                const old = card.querySelector(selector), next = fresh.querySelector(selector);
                if (old.textContent !== next.textContent) old.textContent = next.textContent;
                old.className = next.className; old.hidden = next.hidden; old.title = next.title;
            }
            const video = card.querySelector(".tournament-match-card__actions").firstElementChild;
            const nextVideo = fresh.querySelector(".tournament-match-card__actions").firstElementChild;
            if (video.outerHTML !== nextVideo.outerHTML) video.replaceWith(nextVideo);
            painted.set(card, signature);
        }
        function paint() { matches.forEach(paintCard); updateForm(); }
        function renderSchedule(next) {
            const shape = JSON.stringify([next.tournament, next.rounds.map(round => ({ ...round,
                groups: round.groups.map(group => ({ ...group, matches: group.matches.map(match => match.matchId) })) }))]);
            if (shape !== topology) {
                const active = body.querySelector('[data-tournament-tab-target].is-active')?.dataset.tournamentTabTarget;
                const tabsScroll = body.querySelector('[data-tournament-tab-group="schedule"]')?.scrollLeft || 0;
                const groups = new Map(Array.from(body.querySelectorAll("[data-tournament-group]"), group => [group.dataset.tournamentGroup, group.classList.contains("is-open")]));
                const scroll = window.scrollY;
                body.innerHTML = render(next);
                cards.clear();
                body.querySelectorAll("[data-schedule-match-id]").forEach(card => cards.set(Number(card.dataset.scheduleMatchId), card));
                initialize(next);
                const tab = Array.from(body.querySelectorAll("[data-tournament-tab-target]")).find(button => button.dataset.tournamentTabTarget === active);
                tab?.click();
                body.querySelectorAll("[data-tournament-group]").forEach(group => {
                    if (groups.get(group.dataset.tournamentGroup) === false) group.querySelector("[data-tournament-group-toggle]").click();
                });
                const tabs = body.querySelector('[data-tournament-tab-group="schedule"]');
                if (tabs) tabs.scrollLeft = tabsScroll;
                if (data) window.scrollTo({ top: scroll, behavior: "instant" });
                topology = shape;
            }
            data = next;
            paint();
        }
        async function refresh() {
            const sequence = ++readSequence;
            readController?.abort();
            const controller = readController = new AbortController();
            try {
                const next = await request(`/api/tournaments/${tournamentId}/rounds-with-matches`, {
                    signal: controller.signal, validate: validSchedule, hanakaLoading: "silent"
                });
                if (signal.aborted || sequence !== readSequence) return false;
                const ids = new Set();
                eachMatch(next, (match, index, list) => {
                    const id = Number(match.matchId);
                    ids.add(id); removed.delete(id);
                    const merged = merge(matches.get(id), match);
                    matches.set(id, merged); list[index] = merged;
                });
                let missingEarlyMatch = false;
                for (const id of matches.keys()) if (!ids.has(id)) {
                    missingEarlyMatch = !data;
                    matches.delete(id); removed.add(id);
                }
                renderSchedule(next);
                // A match created during the initial read may not be in that response yet.
                if (missingEarlyMatch) queueRefresh();
                showNotice("");
                return true;
            } catch (error) {
                if (!controller.signal.aborted && !signal.aborted && sequence === readSequence) {
                    showNotice(data ? "Chưa cập nhật được lịch mới. Dữ liệu đang hiển thị được giữ lại." : "Không tải được lịch thi đấu. Vui lòng thử lại.");
                }
                return false;
            } finally {
                if (sequence === readSequence) body.setAttribute("aria-busy", "false");
            }
        }
        async function checkPermission() {
            const sequence = ++permissionSequence;
            permissionController?.abort();
            const controller = permissionController = new AbortController();
            checkingPermission = true; updateForm();
            try {
                const next = await request(`/api/coordination/tournaments/${tournamentId}/permissions`, {
                    signal: controller.signal, hanakaLoading: "silent",
                    validate: value => typeof value?.canCoordinate === "boolean" && Number(value.userId) > 0
                        && (!value.canCoordinate || typeof value.requestToken === "string" && !!value.requestToken)
                });
                if (signal.aborted || sequence !== permissionSequence) return false;
                if (draft && draft.actor !== next.userId) { dialog.close(); draft = null; }
                permission = next;
                permissionError = "";
                return authorized();
            } catch (error) {
                if (controller.signal.aborted || signal.aborted || sequence !== permissionSequence) return false;
                permission = null;
                permissionError = [401, 403].includes(error.status) ? "" : "Không kiểm tra được quyền. Nạp lại form khi có kết nối.";
                return false;
            } finally {
                if (sequence === permissionSequence) { checkingPermission = false; paint(); }
            }
        }
        function fillDraft(match) {
            draft = { id: Number(match.matchId), version: version(match), actor: permission.userId };
            court.value = match.courtText || "";
            preparing.checked = presentation(match).status === "PREPARING";
            updateForm();
        }
        body.addEventListener("click", async event => {
            const button = event.target.closest("[data-coordinate-match]");
            if (!button || saving || dialog.open) return;
            const id = Number(button.dataset.coordinateMatch);
            if (!await checkPermission() || signal.aborted || dialog.open) return;
            const match = matches.get(id);
            if (!canCoordinate(match) || removed.has(id)) return;
            fillDraft(match); dialog.showModal(); court.focus();
        }, { signal });
        dialog.addEventListener("cancel", event => { if (saving) event.preventDefault(); }, { signal });
        dialog.addEventListener("close", () => { if (!dialog.open) draft = null; }, { signal });
        form.querySelector("[data-close]").addEventListener("click", () => { if (!saving) dialog.close(); }, { signal });
        reload.addEventListener("click", async () => {
            if (!draft || saving || draft.reloading) return;
            const editing = draft;
            editing.reloading = true; updateForm();
            const [loaded, allowed] = await Promise.all([refresh(), checkPermission()]);
            if (draft !== editing || signal.aborted) return;
            editing.reloading = false;
            if (loaded && allowed && matches.has(editing.id) && !removed.has(editing.id)) fillDraft(matches.get(editing.id));
            else { editing.error = "Chưa nạp được dữ liệu mới. Bản nhập vẫn được giữ lại."; updateForm(); }
        }, { signal });
        form.addEventListener("submit", async event => {
            event.preventDefault();
            if (!draft || saving || save.disabled) return;
            const editing = draft, courtText = court.value.trim(), isPreparing = preparing.checked;
            const match = matches.get(editing.id);
            if (courtText.length > 100 || (isPreparing && (!courtText || !match?.team1RegistrationId || !match?.team2RegistrationId || match.completionReason === "BYE"))) {
                editing.error = courtText.length > 100 ? "Tên sân tối đa 100 ký tự." : "Cần đủ hai đội và nhập sân trước khi chuẩn bị.";
                updateForm(); return;
            }
            saving = true; editing.error = ""; updateForm();
            try {
                if (!await checkPermission() || draft !== editing || editing.actor !== permission?.userId) return;
                const latest = matches.get(editing.id);
                if (editing.version !== version(latest) || !canCoordinate(latest) || removed.has(editing.id)) return;
                const snapshot = await request(`/api/coordination/matches/${editing.id}`, {
                    method: "PUT", signal, hanakaLoading: "silent",
                    headers: { "Content-Type": "application/json", RequestVerificationToken: permission.requestToken },
                    body: JSON.stringify({ expectedVersion: editing.version, courtText, preparing: isPreparing }),
                    // The server leaves the version unchanged when the requested values already match.
                    validate: value => validSnapshot(value) && Number(value.matchId) === editing.id && version(value) >= editing.version
                        && (value.courtText || "") === courtText && !value.isCompleted
                        && value.matchStatus === (isPreparing ? "PREPARING" : "NOT_STARTED")
                });
                if (signal.aborted) return;
                if (!removed.has(editing.id)) matches.set(editing.id, merge(matches.get(editing.id), snapshot));
                if (draft === editing) dialog.close();
                paint();
            } catch (error) {
                if (signal.aborted || draft !== editing) return;
                editing.error = error.message;
                if (error.uncertain || error.status === 409) editing.uncertain = true;
                if ([401, 403].includes(error.status)) permission = null;
                if (error.status === 409) await refresh();
            } finally { saving = false; paint(); }
        }, { signal });
        function queueRefresh() {
            window.clearTimeout(refreshTimer);
            refreshTimer = window.setTimeout(refresh, 100);
        }
        const realtime = window.HanakaPublicRealtime;
        const unsubscribe = realtime?.on(event => {
            if (event.type === "__public_socket_open__" && event.reconnected) { queueRefresh(); checkPermission(); return; }
            const incoming = normalize(event.payload);
            if (Number(incoming.tournamentId) !== tournamentId) return;
            if (event.type === "tournament.bracket.updated") { readController?.abort(); queueRefresh(); return; }
            if (!["tournament.match.score.updated", "tournament.match.coordination.updated"].includes(event.type)) return;
            const id = Number(incoming.matchId);
            if (!id || removed.has(id)) return;
            if (!validSnapshot(incoming)) { queueRefresh(); return; }
            const previous = matches.get(id), next = merge(previous, incoming);
            matches.set(id, next);
            paintCard(next); updateForm();
            if (data && (!previous || [1, 2].some(side => next[`team${side}RegistrationId`] !== previous[`team${side}RegistrationId`]))) queueRefresh();
        });
        realtime?.subscribeTournament(tournamentId);
        function resume() {
            if (document.hidden) return;
            refresh(); checkPermission();
        }
        notice.querySelector("button").addEventListener("click", resume, { signal });
        window.addEventListener("focus", resume, { signal });
        window.addEventListener("online", resume, { signal });
        window.addEventListener("pageshow", event => { if (event.persisted) resume(); }, { signal });
        document.addEventListener("visibilitychange", resume, { signal });
        const poll = window.setInterval(resume, 30000);
        window.addEventListener("pagehide", event => {
            if (event.persisted) return;
            lifetime.abort(); readController?.abort(); permissionController?.abort();
            window.clearInterval(poll); window.clearTimeout(refreshTimer);
            unsubscribe?.(); realtime?.unsubscribeTournament(tournamentId); dialog.remove(); notice.remove();
        }, { signal });
        await Promise.all([refresh(), checkPermission()]);
    }
    window.HanakaTournamentSchedule = Object.freeze({ presentation, canCoordinate, normalize, merge, mount });
})();
