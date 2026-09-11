(() => {
    'use strict';
    const root = document.getElementById('relay-setup');
    if (!root) return;
    const byId = id => document.getElementById(id);
    const base = `/api/admin/tournaments/${encodeURIComponent(root.dataset.tournamentId)}/relay`;
    let data = null;
    let currentTeam = null;
    let busy = false;
    let dirty = false;

    function message(text, error = false) {
        const box = byId('relay-message');
        box.className = `alert ${error ? 'alert-danger' : 'alert-success'}`;
        box.textContent = text;
    }
    async function api(path = '', method = 'GET', body) {
        const response = await fetch(base + path, {
            method, credentials: 'same-origin', headers: { 'Content-Type': 'application/json' },
            body: body === undefined ? undefined : JSON.stringify(body)
        });
        const payload = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(payload.message || `Không xử lý được yêu cầu (${response.status}).`);
        return payload;
    }
    function setControls() {
        byId('relay-settings-form').querySelectorAll('input,select,button').forEach(x => x.disabled = busy);
        byId('relay-team-form').querySelectorAll('input,select,button').forEach(x => x.disabled = busy || !data?.settings);
        byId('relay-registration').disabled = busy || !data?.settings;
        // Changing team size after draft creation is rejected by the backend as well.
        byId('relay-team-size').disabled = busy || !!data?.teams?.length;
        byId('relay-activate').disabled = busy || !data?.settings || !!data?.settings?.isEnabled;
    }
    function renderTeam() {
        const id = Number(byId('relay-registration').value);
        currentTeam = data?.teams.find(x => x.registrationId === id) || null;
        byId('relay-team-name').value = currentTeam?.teamName || '';
        byId('relay-captain').value = currentTeam?.captainUserId || '';
        byId('relay-ready-state').textContent = currentTeam?.isReady
            ? 'Đội hình đã đủ người và có thể sử dụng ngay. Bạn vẫn có thể chỉnh sửa khi cần.'
            : 'Nhập đủ thành viên hợp lệ để đội sẵn sàng thi đấu.';
        const tbody = byId('relay-members');
        tbody.replaceChildren();
        const size = data?.settings?.teamSize || 6;
        for (let position = 1; position <= size; position++) {
            const member = currentTeam?.members.find(x => x.position === position);
            const row = document.createElement('tr');
            row.dataset.position = String(position);
            for (const label of [String(Math.ceil(position / 2)), String(position)]) {
                const td = document.createElement('td'); td.textContent = label; row.append(td);
            }
            const name = document.createElement('input');
            name.className = 'form-control'; name.dataset.field = 'name'; name.required = true; name.maxLength = 150;
            name.value = member?.displayName || ''; name.setAttribute('aria-label', `Họ tên người ${position}`);
            const user = document.createElement('input');
            user.className = 'form-control'; user.dataset.field = 'user'; user.type = 'number'; user.min = '1'; user.step = '1';
            user.value = member?.userId || ''; user.setAttribute('aria-label', `Mã tài khoản người ${position}`);
            for (const input of [name, user]) { const td = document.createElement('td'); td.append(input); row.append(td); }
            tbody.append(row);
        }
        dirty = false;
        setControls();
    }
    async function load(selectedId) {
        data = await api();
        byId('relay-title').textContent = `Đội tiếp sức · ${data.tournament.title}`;
        byId('relay-team-size').value = String(data.settings?.teamSize || 6);
        byId('relay-target-score').value = String(data.settings?.targetScore || 40);
        byId('relay-active-state').textContent = data.settings?.isEnabled ? 'Đã kích hoạt' : 'Chưa kích hoạt';
        const select = byId('relay-registration');
        select.replaceChildren(new Option('Chọn đăng ký', ''));
        for (const registration of data.registrations) {
            const team = data.teams.find(x => x.registrationId === registration.registrationId);
            const label = `${registration.regCode} · ${team?.teamName || [registration.player1Name, registration.player2Name].filter(Boolean).join(' / ')}`;
            select.add(new Option(label, String(registration.registrationId)));
        }
        if (selectedId) select.value = String(selectedId);
        renderTeam();
    }
    async function run(action) {
        if (busy) return;
        busy = true; setControls();
        try { await action(); } catch (error) { message(error.message, true); }
        finally { busy = false; setControls(); }
    }
    byId('relay-registration').addEventListener('change', renderTeam);
    byId('relay-team-form').addEventListener('input', () => { dirty = true; setControls(); });
    byId('relay-settings-form').addEventListener('submit', event => {
        event.preventDefault();
        run(async () => {
            await api('/settings', 'PUT', { teamSize: Number(byId('relay-team-size').value),
                targetScore: Number(byId('relay-target-score').value), expectedVersion: data?.settings?.version || 0 });
            await load(); message('Đã lưu cấu hình chuẩn bị giải.');
        });
    });
    byId('relay-activate').addEventListener('click', () => {
        if (!window.confirm('Kích hoạt thi đấu tiếp sức với cấu hình và các đội hình đầy đủ hiện tại?')) return;
        run(async () => {
            await api('/activate', 'POST', { expectedVersion: data?.settings?.version || 0 });
            await load(); message('Đã kích hoạt thi đấu tiếp sức.');
        });
    });
    byId('relay-team-form').addEventListener('submit', event => {
        event.preventDefault();
        run(async () => {
            const id = Number(byId('relay-registration').value);
            if (!id) throw new Error('Chọn đăng ký cần bổ sung đội hình.');
            const members = [...byId('relay-members').querySelectorAll('tr')].map(row => ({
                position: Number(row.dataset.position), displayName: row.querySelector('[data-field=name]').value.trim(),
                userId: Number(row.querySelector('[data-field=user]').value) || null,
                avatarUrl: currentTeam?.members.find(x => x.position === Number(row.dataset.position))?.avatarUrl || null
            }));
            await api(`/teams/${id}`, 'PUT', { teamName: byId('relay-team-name').value.trim(),
                captainUserId: Number(byId('relay-captain').value) || null, members, expectedVersion: currentTeam?.version || 0 });
            await load(id); message('Đã lưu đội hình. Đội đủ người có thể được sử dụng ngay.');
        });
    });
    run(() => load());
})();
