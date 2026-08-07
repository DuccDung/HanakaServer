(function () {
    "use strict";

    const root = document.getElementById("bracketTemplateLibrary");
    if (!root) return;

    const state = { page: 1, pageSize: 20, totalPages: 1, totalItems: 0, busy: false, items: [] };
    const $ = (selector, scope = root) => scope.querySelector(selector);
    const rows = $("[data-template-rows]");
    const loading = $("[data-loading]");
    const errorBox = $("[data-library-error]");

    function escapeHtml(value) {
        return String(value ?? "").replace(/[&<>'"]/g, (char) => ({
            "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;"
        })[char]);
    }

    function formatType(value) {
        return ({
            SINGLE_ELIMINATION: "Loại trực tiếp",
            GROUP_KNOCKOUT: "Vòng bảng + knockout",
            CUSTOM: "Tùy chỉnh"
        })[value] || value || "—";
    }

    function statusBadge(value) {
        const map = {
            DRAFT: ["Bản nháp", "draft"],
            PUBLISHED: ["Đã xuất bản", "published"],
            ARCHIVED: ["Đã lưu trữ", "archived"]
        };
        const item = map[value] || [value || "—", "archived"];
        return `<span class="bt-status bt-status--${item[1]}"><i></i>${item[0]}</span>`;
    }

    function formatDate(value) {
        if (!value) return "—";
        return new Intl.DateTimeFormat("vi-VN", { dateStyle: "short", timeStyle: "short" }).format(new Date(value));
    }

    async function api(url, options) {
        const response = await fetch(url, {
            credentials: "same-origin",
            headers: { "Content-Type": "application/json", ...(options?.headers || {}) },
            ...options
        });
        const payload = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(payload.message || "Không thể thực hiện yêu cầu.");
        return payload;
    }

    function setError(message) {
        errorBox.textContent = message || "";
        errorBox.classList.toggle("d-none", !message);
    }

    function setBusy(value) {
        state.busy = value;
        loading.classList.toggle("d-none", !value);
        root.querySelectorAll("button").forEach((button) => {
            if (!button.closest(".modal")) button.disabled = value;
        });
    }

    function render(items) {
        state.items = items;
        rows.innerHTML = items.map((item) => {
            const capacity = item.seedCapacity
                ? `${item.minimumTeams ?? 2}–${item.seedCapacity} đội`
                : "Chưa cấu hình";
            return `<tr>
                <td>
                    <div class="bt-template-name">${escapeHtml(item.templateName)}</div>
                    <code>${escapeHtml(item.templateCode)}</code>
                    ${item.description ? `<small>${escapeHtml(item.description)}</small>` : ""}
                </td>
                <td><span class="bt-format"><i class="fas fa-sitemap"></i>${escapeHtml(formatType(item.formatType))}</span></td>
                <td>${escapeHtml(capacity)}</td>
                <td>${item.currentVersionNumber ? `<strong>v${item.currentVersionNumber}</strong>` : '<span class="text-muted">Draft</span>'}</td>
                <td>${statusBadge(item.status)}</td>
                <td><span class="bt-use-count">${item.applicationCount}</span>${item.currentVersionNumber ? `<small>v${item.currentVersionNumber}: ${item.currentVersionApplicationCount} lần</small>` : ""}</td>
                <td><span>${escapeHtml(formatDate(item.updatedAt || item.createdAt))}</span><small>${escapeHtml(item.updatedByName || "Hệ thống")}</small></td>
                <td class="text-right text-nowrap">
                    <button class="btn btn-sm btn-primary" type="button" data-action="edit" data-id="${item.bracketTemplateId}">
                        <i class="fas fa-pen mr-1"></i>Mở
                    </button>
                    <div class="dropdown d-inline-block">
                        <button class="btn btn-sm btn-light dropdown-toggle" type="button" data-toggle="dropdown" aria-haspopup="true" aria-expanded="false" aria-label="Thêm thao tác"></button>
                        <div class="dropdown-menu dropdown-menu-right">
                            <button class="dropdown-item" type="button" data-action="settings" data-id="${item.bracketTemplateId}"><i class="fas fa-sliders-h fa-fw mr-2"></i>Sửa tên và sức chứa</button>
                            <div class="dropdown-divider"></div>
                            <button class="dropdown-item" type="button" data-action="new-version" data-id="${item.bracketTemplateId}"><i class="fas fa-code-branch fa-fw mr-2"></i>Tạo version mới</button>
                            <button class="dropdown-item" type="button" data-action="clone" data-id="${item.bracketTemplateId}"><i class="far fa-copy fa-fw mr-2"></i>Nhân bản</button>
                            <button class="dropdown-item" type="button" data-action="publish" data-id="${item.bracketTemplateId}"><i class="fas fa-check-circle fa-fw mr-2"></i>Xuất bản draft</button>
                            <div class="dropdown-divider"></div>
                            <button class="dropdown-item" type="button" data-action="archive" data-id="${item.bracketTemplateId}"><i class="fas fa-archive fa-fw mr-2"></i>Lưu trữ</button>
                            <button class="dropdown-item text-danger" type="button" data-action="delete-template" data-id="${item.bracketTemplateId}"><i class="fas fa-trash-alt fa-fw mr-2"></i>Xóa template</button>
                        </div>
                    </div>
                </td>
            </tr>`;
        }).join("");
        $("[data-empty]").classList.toggle("d-none", items.length > 0);
    }

    async function load() {
        if (state.busy) return;
        setBusy(true);
        setError("");
        try {
            const params = new URLSearchParams({
                search: $("#btSearch").value.trim(),
                formatType: $("#btFormat").value,
                status: $("#btStatus").value,
                page: state.page,
                pageSize: state.pageSize
            });
            const result = await api(`/api/admin/bracket-templates?${params}`);
            state.page = result.page;
            state.totalPages = result.totalPages;
            state.totalItems = result.totalItems;
            render(result.items || []);
            $("[data-list-summary]").textContent = `${state.totalItems} template trong thư viện`;
            $("[data-page-info]").textContent = `Trang ${state.page} / ${state.totalPages}`;
            $("[data-action='prev-page']").disabled = state.page <= 1;
            $("[data-action='next-page']").disabled = state.page >= state.totalPages;
        } catch (error) {
            setError(error.message);
            state.items = [];
            rows.innerHTML = "";
        } finally {
            setBusy(false);
        }
    }

    async function getDetail(templateId) {
        return api(`/api/admin/bracket-templates/${templateId}`);
    }

    function pickVersion(detail, preferDraft = true) {
        const versions = detail.versions || [];
        return (preferDraft ? versions.find((x) => x.status === "DRAFT") : null)
            || versions.find((x) => x.bracketTemplateVersionId === detail.currentPublishedVersionId)
            || versions[0];
    }

    async function openEditor(templateId) {
        const detail = await getDetail(templateId);
        const version = pickVersion(detail);
        if (!version) throw new Error("Template chưa có version để chỉnh sửa.");
        location.href = `/BracketTemplates/Editor?templateId=${templateId}&versionId=${version.bracketTemplateVersionId}`;
    }

    async function createVersion(templateId) {
        const detail = await getDetail(templateId);
        const existingDraft = (detail.versions || []).find((x) => x.status === "DRAFT");
        if (existingDraft) {
            location.href = `/BracketTemplates/Editor?templateId=${templateId}&versionId=${existingDraft.bracketTemplateVersionId}`;
            return;
        }
        const result = await api(`/api/admin/bracket-templates/${templateId}/versions`, { method: "POST", body: "{}" });
        location.href = `/BracketTemplates/Editor?templateId=${templateId}&versionId=${result.data.bracketTemplateVersionId}`;
    }

    async function publishDraft(templateId) {
        const detail = await getDetail(templateId);
        const draft = (detail.versions || []).find((x) => x.status === "DRAFT");
        if (!draft) throw new Error("Template không có version draft để xuất bản.");
        const validationResult = await api(`/api/admin/bracket-templates/versions/${draft.bracketTemplateVersionId}/validate`, { method: "POST", body: "{}" });
        const validation = validationResult.data;
        if (!validation.isValid) throw new Error(`Chưa thể xuất bản: còn ${validation.errorCount} lỗi cấu trúc.`);
        const warningText = validation.warningCount
            ? `Cấu trúc còn ${validation.warningCount} cảnh báo. Bạn vẫn muốn xuất bản?`
            : "Xuất bản version này? Sau khi xuất bản, cấu trúc sẽ không thể chỉnh sửa.";
        if (!window.confirm(warningText)) return;
        await api(`/api/admin/bracket-templates/versions/${draft.bracketTemplateVersionId}/publish`, { method: "POST", body: "{}" });
        await load();
    }

    async function cloneTemplate(templateId) {
        const detail = await getDetail(templateId);
        const source = pickVersion(detail);
        if (!source) throw new Error("Không tìm thấy version nguồn.");
        const suggestedCode = `${detail.templateCode}_COPY`;
        const code = window.prompt("Mã template mới:", suggestedCode)?.trim();
        if (!code) return;
        const name = window.prompt("Tên template mới:", `${detail.templateName} - Bản sao`)?.trim();
        if (!name) return;
        const result = await api("/api/admin/bracket-templates/clone", {
            method: "POST",
            body: JSON.stringify({ sourceVersionId: source.bracketTemplateVersionId, templateCode: code, templateName: name })
        });
        const draft = pickVersion(result.data);
        location.href = `/BracketTemplates/Editor?templateId=${result.data.bracketTemplateId}&versionId=${draft.bracketTemplateVersionId}`;
    }

    async function archiveTemplate(templateId) {
        if (!window.confirm("Lưu trữ template này? Các giải đã áp dụng vẫn hoạt động bình thường.")) return;
        await api(`/api/admin/bracket-templates/${templateId}/archive`, { method: "POST", body: "{}" });
        await load();
    }

    function findItem(templateId) {
        return state.items.find((item) => Number(item.bracketTemplateId) === Number(templateId));
    }

    function openSettings(templateId) {
        const item = findItem(templateId);
        if (!item) throw new Error("Không tìm thấy template trong danh sách hiện tại.");

        document.getElementById("editTemplateId").value = item.bracketTemplateId;
        document.getElementById("editTemplateRowVersion").value = item.rowVersion || "";
        document.getElementById("editTemplateCode").value = item.templateCode || "";
        document.getElementById("editTemplateName").value = item.templateName || "";
        document.getElementById("editMinimumTeams").value = item.minimumTeams ?? 2;
        document.getElementById("editSeedCapacity").value = item.seedCapacity ?? 2;
        const editError = document.querySelector("[data-edit-error]");
        editError.textContent = "";
        editError.classList.add("d-none");
        window.jQuery("#editBracketTemplateModal").modal("show");
    }

    function openDeleteTemplate(templateId) {
        const item = findItem(templateId);
        if (!item) throw new Error("Không tìm thấy template trong danh sách hiện tại.");

        document.getElementById("deleteTemplateId").value = item.bracketTemplateId;
        document.getElementById("deleteTemplateRowVersion").value = item.rowVersion || "";
        document.getElementById("deleteTemplateName").textContent = item.templateName || "Template";
        document.getElementById("deleteTemplateCode").textContent = item.templateCode || "";
        const confirmation = document.getElementById("deleteTemplateConfirmation");
        const deleteSubmit = document.querySelector("[data-delete-submit]");
        const inUse = document.querySelector("[data-delete-in-use]");
        const deleteError = document.querySelector("[data-delete-error]");
        const applicationCount = Number(item.applicationCount || 0);

        confirmation.value = "";
        confirmation.disabled = applicationCount > 0;
        deleteSubmit.disabled = true;
        deleteError.textContent = "";
        deleteError.classList.add("d-none");
        inUse.textContent = applicationCount > 0
            ? `Template đã được áp dụng ${applicationCount} lần nên không thể xóa. Hãy dùng chức năng Lưu trữ.`
            : "";
        inUse.classList.toggle("d-none", applicationCount === 0);
        window.jQuery("#deleteBracketTemplateModal").modal("show");
        if (applicationCount === 0) {
            window.setTimeout(() => confirmation.focus(), 150);
        }
    }

    let searchTimer;
    $("#btSearch").addEventListener("input", () => {
        clearTimeout(searchTimer);
        searchTimer = setTimeout(() => { state.page = 1; load(); }, 350);
    });
    [$("#btFormat"), $("#btStatus")].forEach((control) => control.addEventListener("change", () => { state.page = 1; load(); }));
    $("#btPageSize").addEventListener("change", (event) => { state.pageSize = Number(event.target.value); state.page = 1; load(); });

    root.addEventListener("click", async (event) => {
        const button = event.target.closest("[data-action]");
        if (!button || state.busy) return;
        const action = button.dataset.action;
        const id = Number(button.dataset.id);
        try {
            if (action === "open-create") window.jQuery("#createBracketTemplateModal").modal("show");
            if (action === "refresh") await load();
            if (action === "prev-page" && state.page > 1) { state.page--; await load(); }
            if (action === "next-page" && state.page < state.totalPages) { state.page++; await load(); }
            if (action === "edit") await openEditor(id);
            if (action === "settings") openSettings(id);
            if (action === "new-version") await createVersion(id);
            if (action === "publish") await publishDraft(id);
            if (action === "clone") await cloneTemplate(id);
            if (action === "archive") await archiveTemplate(id);
            if (action === "delete-template") openDeleteTemplate(id);
        } catch (error) { setError(error.message); }
    });

    const form = document.getElementById("createBracketTemplateForm");
    const createModal = document.getElementById("createBracketTemplateModal");
    const codeInput = document.getElementById("createTemplateCode");
    const nameInput = document.getElementById("createTemplateName");
    const createError = form.querySelector("[data-create-error]");
    const createSubmit = form.querySelector("[data-create-submit]");

    if (createModal && window.jQuery) {
        window.jQuery(createModal).on("show.bs.modal", async () => {
            form.reset();
            createError.classList.add("d-none");
            createError.textContent = "";
            codeInput.value = "";
            codeInput.placeholder = "Đang tạo mã...";
            createSubmit.disabled = true;

            try {
                const result = await api("/api/admin/bracket-templates/next-code");
                if (!result.code) throw new Error("Không thể tạo mã template.");
                codeInput.value = result.code;
                codeInput.placeholder = "";
                createSubmit.disabled = false;
            } catch (error) {
                createError.textContent = error.message;
                createError.classList.remove("d-none");
            }
        });

        window.jQuery(createModal).on("shown.bs.modal", () => nameInput.focus());
    }

    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        createError.classList.add("d-none");
        createSubmit.disabled = true;
        try {
            const payload = {
                templateCode: codeInput.value.trim(),
                templateName: nameInput.value.trim(),
                description: document.getElementById("createDescription").value.trim() || null
            };
            const result = await api("/api/admin/bracket-templates", { method: "POST", body: JSON.stringify(payload) });
            const version = pickVersion(result.data);
            location.href = `/BracketTemplates/Editor?templateId=${result.data.bracketTemplateId}&versionId=${version.bracketTemplateVersionId}`;
        } catch (error) {
            createError.textContent = error.message;
            createError.classList.remove("d-none");
        } finally { createSubmit.disabled = false; }
    });

    const editForm = document.getElementById("editBracketTemplateForm");
    const editSubmit = editForm.querySelector("[data-edit-submit]");
    const editError = editForm.querySelector("[data-edit-error]");
    editForm.addEventListener("submit", async (event) => {
        event.preventDefault();
        editError.textContent = "";
        editError.classList.add("d-none");

        const templateName = document.getElementById("editTemplateName").value.trim();
        const minimumTeams = Number(document.getElementById("editMinimumTeams").value);
        const seedCapacity = Number(document.getElementById("editSeedCapacity").value);
        if (!templateName) {
            editError.textContent = "Vui lòng nhập tên template.";
            editError.classList.remove("d-none");
            return;
        }
        if (!Number.isInteger(minimumTeams) || minimumTeams < 2 || minimumTeams > 1024
            || !Number.isInteger(seedCapacity) || seedCapacity < 2 || seedCapacity > 1024) {
            editError.textContent = "Số đội tối thiểu và tối đa phải là số nguyên từ 2 đến 1024.";
            editError.classList.remove("d-none");
            return;
        }
        if (minimumTeams > seedCapacity) {
            editError.textContent = "Số đội tối thiểu không được lớn hơn số đội tối đa.";
            editError.classList.remove("d-none");
            return;
        }

        const templateId = Number(document.getElementById("editTemplateId").value);
        editSubmit.disabled = true;
        try {
            await api(`/api/admin/bracket-templates/${templateId}/settings`, {
                method: "PUT",
                body: JSON.stringify({
                    templateName,
                    minimumTeams,
                    seedCapacity,
                    rowVersion: document.getElementById("editTemplateRowVersion").value
                })
            });
            window.jQuery("#editBracketTemplateModal").modal("hide");
            await load();
        } catch (error) {
            editError.textContent = error.message;
            editError.classList.remove("d-none");
        } finally {
            editSubmit.disabled = false;
        }
    });

    const deleteForm = document.getElementById("deleteBracketTemplateForm");
    const deleteConfirmation = document.getElementById("deleteTemplateConfirmation");
    const deleteSubmit = deleteForm.querySelector("[data-delete-submit]");
    const deleteError = deleteForm.querySelector("[data-delete-error]");
    deleteConfirmation.addEventListener("input", () => {
        deleteSubmit.disabled = deleteConfirmation.disabled || deleteConfirmation.value !== "XOA";
    });
    deleteForm.addEventListener("submit", async (event) => {
        event.preventDefault();
        if (deleteConfirmation.disabled || deleteConfirmation.value !== "XOA") return;

        deleteError.textContent = "";
        deleteError.classList.add("d-none");
        deleteSubmit.disabled = true;
        const templateId = Number(document.getElementById("deleteTemplateId").value);
        try {
            await api(`/api/admin/bracket-templates/${templateId}`, {
                method: "DELETE",
                body: JSON.stringify({
                    rowVersion: document.getElementById("deleteTemplateRowVersion").value,
                    confirmation: deleteConfirmation.value
                })
            });
            window.jQuery("#deleteBracketTemplateModal").modal("hide");
            await load();
        } catch (error) {
            deleteError.textContent = error.message;
            deleteError.classList.remove("d-none");
            deleteSubmit.disabled = false;
        }
    });

    load();
})();
