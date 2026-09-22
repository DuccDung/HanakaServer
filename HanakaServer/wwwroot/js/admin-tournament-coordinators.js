(() => {
    const root = document.getElementById('coordinator-admin');
    if (!root) return;
    const base = `/api/admin/tournaments/${encodeURIComponent(root.dataset.tournamentId)}/coordinators`;
    const error = document.getElementById('coordinator-error');
    const list = document.getElementById('coordinator-list');
    const results = document.getElementById('coordinator-results');
    let busy = false;
    async function api(url, options) {
        const response = await fetch(url, options);
        const data = await response.json();
        if (!response.ok) throw new Error(data.message || 'Không thực hiện được thao tác. Vui lòng thử lại.');
        return data;
    }
    function row(user, assigned) {
        const wrap = document.createElement('div');
        wrap.className = 'd-flex align-items-center justify-content-between py-2';
        wrap.style.gap = '12px';
        wrap.style.flexWrap = 'wrap';
        const label = document.createElement('span');
        label.textContent = `${user.fullName} · ID ${user.userId}${user.phone ? ` · ${user.phone}` : ''}${user.isActive === false ? ' · Đã khóa' : ''}`;
        const button = document.createElement('button');
        button.type = 'button';
        button.className = assigned ? 'btn btn-sm btn-outline-danger' : 'btn btn-sm btn-primary';
        button.textContent = assigned ? 'Thu hồi' : 'Phân công';
        button.addEventListener('click', async () => {
            if (busy) return;
            if (assigned && !window.confirm(`Thu hồi quyền điều phối của ${user.fullName}?`)) return;
            busy = true; button.disabled = true; error.textContent = '';
            try {
                await api(`${base}/${encodeURIComponent(user.userId)}`, { method: assigned ? 'DELETE' : 'PUT' });
                results.replaceChildren();
                await refresh();
            } catch (failure) { error.textContent = failure.message; }
            finally { busy = false; button.disabled = false; }
        });
        wrap.append(label, button);
        return wrap;
    }
    async function refresh() {
        const users = await api(base);
        list.replaceChildren(...users.map(user => row(user, true)));
        if (!users.length) list.textContent = 'Chưa có người điều phối.';
    }
    document.getElementById('coordinator-search').addEventListener('submit', async event => {
        event.preventDefault();
        if (busy) return;
        busy = true; error.textContent = ''; results.replaceChildren();
        const button = event.currentTarget.querySelector('button');
        button.disabled = true;
        try {
            const query = document.getElementById('coordinator-query').value.trim();
            const data = await api(`/api/admin/users/lookup?query=${encodeURIComponent(query)}`);
            results.replaceChildren(...(data.items || []).map(user => row(user, false)));
            if (!data.items?.length) results.textContent = 'Không tìm thấy tài khoản.';
        } catch (failure) { error.textContent = failure.message; }
        finally { busy = false; button.disabled = false; }
    });
    refresh().catch(failure => { error.textContent = failure.message; });
})();
