(function () {
    "use strict";

    const SOURCE_TYPES = {
        registration: "REGISTRATION",
        winner: "WINNER_MATCH",
        loser: "LOSER_MATCH",
        groupRank: "GROUP_RANK",
        bye: "BYE"
    };

    function qs(selector, root) {
        return (root || document).querySelector(selector);
    }

    function qsa(selector, root) {
        return Array.from((root || document).querySelectorAll(selector));
    }

    function toNumber(value) {
        const number = Number(value);
        return Number.isFinite(number) ? number : 0;
    }

    function text(value) {
        return String(value ?? "").trim();
    }

    function normalizeSearchText(value) {
        return text(value)
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .toLowerCase();
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    async function requestJson(url, options) {
        const requestOptions = Object.assign({
            credentials: "same-origin",
            cache: "no-store",
            headers: { Accept: "application/json" }
        }, options || {});

        if (requestOptions.body && typeof requestOptions.body !== "string") {
            requestOptions.headers = Object.assign({}, requestOptions.headers, {
                "Content-Type": "application/json"
            });
            requestOptions.body = JSON.stringify(requestOptions.body);
        }

        const response = await fetch(url, requestOptions);
        const raw = await response.text();
        let payload = null;

        if (raw) {
            try {
                payload = JSON.parse(raw);
            } catch (_error) {
                payload = null;
            }
        }

        if (!response.ok) {
            const error = new Error(text(payload?.message) || text(raw) || ("Yêu cầu thất bại: " + response.status));
            error.payload = payload;
            error.status = response.status;
            throw error;
        }

        return payload;
    }

    function formatGroupContext(context) {
        const round = text(context?.roundLabel) || text(context?.roundKey) || "Vòng đấu";
        const group = text(context?.groupName) || ("Bảng #" + toNumber(context?.groupId));
        return round + " / " + group;
    }

    function toDateTimeLocal(value) {
        if (!value) {
            return "";
        }

        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return "";
        }

        const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000);
        return local.toISOString().slice(0, 16);
    }

    function toIsoDateTime(value) {
        if (!text(value)) {
            return null;
        }

        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            throw new Error("Thời gian bắt đầu không hợp lệ.");
        }

        return date.toISOString();
    }

    function getGroupId(group) {
        return toNumber(group?.groupId) || toNumber(group?.tournamentRoundGroupId);
    }

    function getRoundMapId(round) {
        return toNumber(round?.roundMapId) || toNumber(round?.tournamentRoundMapId);
    }

    function getMatchId(match) {
        return toNumber(match?.matchId);
    }

    function findGroupContext(payload, groupId) {
        const rounds = Array.isArray(payload?.rounds) ? payload.rounds : [];

        for (const round of rounds) {
            const groups = Array.isArray(round?.groups) ? round.groups : [];
            for (const group of groups) {
                if (getGroupId(group) !== toNumber(groupId)) {
                    continue;
                }

                return {
                    groupId: getGroupId(group),
                    groupName: text(group?.groupName),
                    roundMapId: toNumber(round?.roundMapId) || toNumber(round?.tournamentRoundMapId),
                    roundKey: text(round?.roundKey),
                    roundLabel: text(round?.roundLabel),
                    group: group,
                    round: round
                };
            }
        }

        return null;
    }

    function findMatchContext(payload, matchId) {
        const rounds = Array.isArray(payload?.rounds) ? payload.rounds : [];

        for (const round of rounds) {
            const groups = Array.isArray(round?.groups) ? round.groups : [];
            for (const group of groups) {
                const matches = Array.isArray(group?.matches) ? group.matches : [];
                const match = matches.find(function (item) {
                    return getMatchId(item) === toNumber(matchId);
                });

                if (match) {
                    return Object.assign(findGroupContext(payload, getGroupId(group)) || {}, {
                        match: match,
                        matchId: getMatchId(match)
                    });
                }
            }
        }

        return null;
    }

    function initBracketEditor(page) {
        if (!page || page._adminTournamentBracketEditor) {
            return page?._adminTournamentBracketEditor || null;
        }

        const tournamentId = toNumber(page.dataset.tournamentId);
        const board = qs("[data-bracket-board]", page);
        const panelTop = qs(".admin-tournament-bracket-panel__top", page);
        if (!tournamentId || !board || !panelTop) {
            return null;
        }

        const state = {
            mode: "view",
            formMode: "create",
            groupId: 0,
            matchId: 0,
            context: null,
            match: null,
            sourceOptions: null,
            registrations: null,
            referee: null,
            ready: false,
            saving: false,
            structureBusy: false
        };
        const sourceOptionsCache = new Map();

        const editorTools = document.createElement("div");
        editorTools.className = "admin-bracket-editor-tools";

        const modeControl = document.createElement("div");
        modeControl.className = "admin-bracket-editor-mode";
        modeControl.setAttribute("aria-label", "Chế độ nhánh đấu");
        modeControl.innerHTML = [
            '<button type="button" class="is-active" data-bracket-editor-mode="view" title="Chỉ xem và kiểm tra đường nối"><i class="fas fa-eye"></i><span>Xem</span></button>',
            '<button type="button" data-bracket-editor-mode="design" title="Tạo và chỉnh sửa trận trực tiếp"><i class="fas fa-pen"></i><span>Thiết kế</span></button>'
        ].join("");
        editorTools.appendChild(modeControl);

        const structureButton = document.createElement("button");
        structureButton.type = "button";
        structureButton.className = "admin-bracket-editor-structure-button";
        structureButton.dataset.bracketStructureOpen = "true";
        structureButton.title = "Tạo và quản lý vòng, bảng đấu";
        structureButton.innerHTML = '<i class="fas fa-layer-group"></i><span>Vòng / Bảng</span>';
        editorTools.appendChild(structureButton);
        panelTop.appendChild(editorTools);

        const drawer = document.createElement("aside");
        drawer.className = "admin-bracket-editor-drawer";
        drawer.setAttribute("aria-hidden", "true");
        drawer.innerHTML = [
            '<div class="admin-bracket-editor-drawer__head">',
            '<div><small data-editor-kicker>CHỈNH SỬA NHÁNH ĐẤU</small><h3 data-editor-title>Tạo trận đấu</h3></div>',
            '<button type="button" data-editor-close title="Đóng bảng" aria-label="Đóng bảng"><i class="fas fa-times"></i></button>',
            '</div>',
            '<div class="admin-bracket-editor-context" data-editor-context></div>',
            '<div class="admin-bracket-editor-lock d-none" data-editor-lock><i class="fas fa-lock"></i><span>Trận đã kết thúc. Nhánh đấu chỉ cho phép xem thông tin.</span></div>',
            '<div class="admin-bracket-editor-error d-none" data-editor-error></div>',
            '<form data-editor-form novalidate>',
            '<div class="admin-bracket-editor-drawer__body">',
            buildSlotForm(1, "Vị trí A"),
            buildSlotForm(2, "Vị trí B"),
            '<section class="admin-bracket-editor-section">',
            '<div class="admin-bracket-editor-section__title"><i class="fas fa-calendar-alt"></i><span>Lịch và địa điểm</span></div>',
            '<div class="admin-bracket-editor-grid">',
            '<label><span>Bắt đầu</span><input type="datetime-local" data-editor-field="startAt" /></label>',
            '<label><span>Sân</span><input type="text" data-editor-field="courtText" maxlength="255" placeholder="Sân 1" /></label>',
            '</div>',
            '<label><span>Địa chỉ</span><input type="text" data-editor-field="addressText" maxlength="500" placeholder="Địa chỉ thi đấu" /></label>',
            '<label><span>Liên kết video</span><input type="url" data-editor-field="videoUrl" maxlength="500" placeholder="https://..." /></label>',
            '</section>',
            '<section class="admin-bracket-editor-section">',
            '<div class="admin-bracket-editor-section__title"><i class="fas fa-user-shield"></i><span>Trọng tài</span></div>',
            '<label><span>ID trọng tài</span><div class="admin-bracket-editor-referee-input"><input type="number" min="1" data-editor-field="refereeUserId" placeholder="Nhập ID người dùng" /><button type="button" data-editor-referee-check title="Kiểm tra trọng tài"><i class="fas fa-search"></i></button></div></label>',
            '<div class="admin-bracket-editor-referee is-empty" data-editor-referee>Chưa xác nhận trọng tài.</div>',
            '</section>',
            '</div>',
            '<div class="admin-bracket-editor-drawer__actions">',
            '<button type="button" class="is-danger d-none" data-editor-delete><i class="fas fa-trash-alt"></i><span>Xóa trận</span></button>',
            '<div class="admin-bracket-editor-drawer__actions-main">',
            '<button type="button" class="is-secondary" data-editor-cancel>Hủy</button>',
            '<button type="submit" class="is-primary" data-editor-save><i class="fas fa-save"></i><span>Lưu trận</span></button>',
            '</div>',
            '</div>',
            '</form>'
        ].join("");
        document.body.appendChild(drawer);

        const structureDrawer = document.createElement("aside");
        structureDrawer.className = "admin-bracket-editor-drawer admin-bracket-structure-drawer";
        structureDrawer.setAttribute("aria-hidden", "true");
        structureDrawer.innerHTML = [
            '<div class="admin-bracket-editor-drawer__head">',
            '<div><small>CẤU TRÚC NHÁNH ĐẤU</small><h3>Quản lý vòng và bảng</h3></div>',
            '<button type="button" data-structure-close title="Đóng bảng" aria-label="Đóng bảng"><i class="fas fa-times"></i></button>',
            '</div>',
            '<div class="admin-bracket-editor-context"><i class="fas fa-sitemap"></i><div><b>Cấu trúc giải đấu</b><span>Giải đấu #' + escapeHtml(tournamentId) + '</span></div></div>',
            '<div class="admin-bracket-editor-error d-none" data-structure-error></div>',
            '<div class="admin-bracket-structure-drawer__body">',
            '<section class="admin-bracket-editor-section">',
            '<div class="admin-bracket-editor-section__title"><i class="fas fa-layer-group"></i><span>Tạo vòng đấu</span></div>',
            '<form data-structure-round-form novalidate>',
            '<div class="admin-bracket-editor-grid">',
            '<label><span>Mã vòng</span><input type="text" maxlength="50" data-structure-round-key placeholder="R1" required /></label>',
            '<label><span>Thứ tự</span><input type="number" min="0" data-structure-round-sort required /></label>',
            '</div>',
            '<label><span>Tên vòng</span><input type="text" maxlength="255" data-structure-round-label placeholder="Vòng 1" required /></label>',
            '<button type="submit" class="admin-bracket-structure-submit" data-structure-action><i class="fas fa-plus"></i><span>Tạo vòng</span></button>',
            '</form>',
            '</section>',
            '<section class="admin-bracket-editor-section">',
            '<div class="admin-bracket-editor-section__title"><i class="fas fa-table"></i><span>Tạo bảng đấu</span></div>',
            '<form data-structure-group-form novalidate>',
            '<label><span>Vòng đấu</span><select data-structure-group-round required></select></label>',
            '<div class="admin-bracket-editor-grid">',
            '<label><span>Tên bảng</span><input type="text" maxlength="255" data-structure-group-name placeholder="A" required /></label>',
            '<label><span>Thứ tự</span><input type="number" min="0" data-structure-group-sort required /></label>',
            '</div>',
            '<button type="submit" class="admin-bracket-structure-submit" data-structure-action><i class="fas fa-plus"></i><span>Tạo bảng</span></button>',
            '</form>',
            '</section>',
            '<section class="admin-bracket-editor-section admin-bracket-structure-overview">',
            '<div class="admin-bracket-editor-section__title"><i class="fas fa-list"></i><span>Cấu trúc hiện tại</span><small data-structure-summary></small></div>',
            '<div class="admin-bracket-structure-list" data-structure-list></div>',
            '</section>',
            '</div>'
        ].join("");
        document.body.appendChild(structureDrawer);

        const toast = document.createElement("div");
        toast.className = "admin-bracket-editor-toast";
        toast.setAttribute("role", "status");
        document.body.appendChild(toast);

        function buildSlotForm(slotNumber, title) {
            return [
                '<section class="admin-bracket-editor-section" data-editor-slot="' + slotNumber + '">',
                '<div class="admin-bracket-editor-section__title"><span>' + title + '</span><small data-slot-preview>Chưa chọn nguồn</small></div>',
                '<label><span>Nguồn vận động viên</span><select data-slot-source-type>',
                '<option value="REGISTRATION">Đội đăng ký</option>',
                '<option value="WINNER_MATCH">Thắng trận</option>',
                '<option value="LOSER_MATCH">Thua trận</option>',
                '<option value="GROUP_RANK">Hạng bảng</option>',
                '<option value="BYE" hidden>Miễn đấu</option>',
                '</select></label>',
                '<div data-slot-panel="REGISTRATION">',
                '<label><span>Tìm nhanh ID đội / mã đăng ký</span>',
                '<div class="admin-bracket-editor-team-search">',
                '<input type="search" data-slot-registration-search placeholder="Ví dụ: 307 hoặc 30-0001" autocomplete="off" />',
                '<button type="button" data-slot-registration-find title="Tìm đội trong giải" aria-label="Tìm đội trong giải"><i class="fas fa-search"></i></button>',
                '</div>',
                '</label>',
                '<small class="admin-bracket-editor-team-search__status" data-slot-registration-status></small>',
                '<label><span>Đội thi đấu</span><select data-slot-registration></select></label>',
                '</div>',
                '<div class="d-none" data-slot-panel="MATCH"><label><span>Trận nguồn</span><select data-slot-match></select></label></div>',
                '<div class="d-none" data-slot-panel="GROUP_RANK">',
                '<div class="admin-bracket-editor-grid">',
                '<label><span>Bảng nguồn</span><select data-slot-group></select></label>',
                '<label><span>Hạng</span><input type="number" min="1" value="1" data-slot-rank /></label>',
                '</div>',
                '</div>',
                '</section>'
            ].join("");
        }

        function getViewer() {
            return page._adminTournamentBracketViewer || null;
        }

        function getPayload() {
            return getViewer()?.getPayload?.() || null;
        }

        function getStructureRounds() {
            return Array.isArray(getPayload()?.rounds) ? getPayload().rounds : [];
        }

        function getNextRoundDefaults() {
            const rounds = getStructureRounds();
            const usedKeys = new Set(rounds.map(function (round) {
                return text(round?.roundKey).toLowerCase();
            }));
            let number = 1;

            while (usedKeys.has(("R" + number).toLowerCase())) {
                number += 1;
            }

            return {
                roundKey: "R" + number,
                roundLabel: "Vòng " + number,
                sortOrder: rounds.length
                    ? Math.max.apply(null, rounds.map(function (round) { return toNumber(round?.sortOrder); })) + 1
                    : 1
            };
        }

        function getNextGroupDefaults(round) {
            const groups = Array.isArray(round?.groups) ? round.groups : [];
            const usedNames = new Set(groups.map(function (group) {
                return normalizeSearchText(group?.groupName);
            }));
            let groupName = "";

            for (let code = 65; code <= 90; code += 1) {
                const candidate = String.fromCharCode(code);
                if (!usedNames.has(candidate.toLowerCase())) {
                    groupName = candidate;
                    break;
                }
            }

            if (!groupName) {
                let number = groups.length + 1;
                while (usedNames.has(normalizeSearchText("Bảng " + number))) {
                    number += 1;
                }
                groupName = "Bảng " + number;
            }

            return {
                groupName: groupName,
                sortOrder: groups.length
                    ? Math.max.apply(null, groups.map(function (group) { return toNumber(group?.sortOrder); })) + 1
                    : 1
            };
        }

        function clearStructureError() {
            const errorBox = qs("[data-structure-error]", structureDrawer);
            errorBox.classList.add("d-none");
            errorBox.innerHTML = "";
        }

        function showStructureError(error) {
            const errorBox = qs("[data-structure-error]", structureDrawer);
            errorBox.innerHTML = '<div><i class="fas fa-exclamation-circle"></i><span>'
                + escapeHtml(error?.message || "Thao tác thất bại.")
                + "</span></div>";
            errorBox.classList.remove("d-none");
        }

        function setStructureBusy(busy) {
            state.structureBusy = !!busy;
            qsa("[data-structure-action], [data-structure-delete-round], [data-structure-delete-group]", structureDrawer)
                .forEach(function (button) {
                    button.disabled = state.structureBusy || button.dataset.structureLocked === "true";
                });
            qsa("input, select", structureDrawer).forEach(function (field) {
                field.disabled = state.structureBusy || field.dataset.structureLocked === "true";
            });
        }

        function setGroupFormDefaults(roundMapId, force) {
            const rounds = getStructureRounds();
            const round = rounds.find(function (item) {
                return getRoundMapId(item) === toNumber(roundMapId);
            }) || null;
            const nameInput = qs("[data-structure-group-name]", structureDrawer);
            const sortInput = qs("[data-structure-group-sort]", structureDrawer);

            if (!round) {
                nameInput.value = "";
                sortInput.value = "";
                return;
            }

            const defaults = getNextGroupDefaults(round);
            if (force || !text(nameInput.value)) {
                nameInput.value = defaults.groupName;
            }
            if (force || !text(sortInput.value)) {
                sortInput.value = String(defaults.sortOrder);
            }
        }

        function renderStructurePanel(preferredRoundMapId, resetDefaults) {
            const rounds = getStructureRounds();
            const list = qs("[data-structure-list]", structureDrawer);
            const summary = qs("[data-structure-summary]", structureDrawer);
            const roundSelect = qs("[data-structure-group-round]", structureDrawer);
            const currentRoundMapId = toNumber(preferredRoundMapId)
                || toNumber(roundSelect.value)
                || getRoundMapId(rounds[rounds.length - 1]);
            const groupCount = rounds.reduce(function (total, round) {
                return total + (Array.isArray(round?.groups) ? round.groups.length : 0);
            }, 0);

            summary.textContent = rounds.length + " vòng · " + groupCount + " bảng";
            setSelectOptions(
                roundSelect,
                rounds,
                getRoundMapId,
                function (round) {
                    return (text(round?.roundKey) || "Vòng") + " - " + (text(round?.roundLabel) || "Chưa đặt tên");
                },
                currentRoundMapId,
                rounds.length ? "Chọn vòng đấu" : "Chưa có vòng đấu"
            );

            const selectedRoundMapId = toNumber(roundSelect.value) || getRoundMapId(rounds[0]);
            if (selectedRoundMapId) {
                roundSelect.value = String(selectedRoundMapId);
            }

            const groupForm = qs("[data-structure-group-form]", structureDrawer);
            qsa("input, select, button", groupForm).forEach(function (field) {
                field.dataset.structureLocked = rounds.length ? "false" : "true";
            });

            if (!rounds.length) {
                list.innerHTML = '<div class="admin-bracket-structure-empty">Chưa có vòng đấu.</div>';
            } else {
                list.innerHTML = rounds.map(function (round) {
                    const roundMapId = getRoundMapId(round);
                    const groups = Array.isArray(round?.groups) ? round.groups : [];
                    const matchCount = groups.reduce(function (total, group) {
                        return total + (Array.isArray(group?.matches) ? group.matches.length : 0);
                    }, 0);
                    const groupRows = groups.length
                        ? groups.map(function (group) {
                            const groupId = getGroupId(group);
                            const matches = Array.isArray(group?.matches) ? group.matches : [];
                            const deleteLocked = matches.length > 0;
                            return [
                                '<div class="admin-bracket-structure-group">',
                                '<div><b>' + escapeHtml(text(group?.groupName) || ("Bảng #" + groupId)) + '</b><span>ID bảng #' + escapeHtml(groupId) + ' · ' + escapeHtml(matches.length) + ' trận</span></div>',
                                '<button type="button" data-structure-delete-group="' + escapeHtml(groupId) + '" data-round-map-id="' + escapeHtml(roundMapId) + '" data-structure-locked="' + (deleteLocked ? "true" : "false") + '" title="' + (deleteLocked ? "Xóa hết trận trong bảng trước" : "Xóa bảng") + '" aria-label="Xóa bảng ' + escapeHtml(text(group?.groupName)) + '"' + (deleteLocked ? " disabled" : "") + '><i class="fas fa-trash-alt"></i></button>',
                                '</div>'
                            ].join("");
                        }).join("")
                        : '<div class="admin-bracket-structure-empty is-compact">Chưa có bảng đấu.</div>';
                    const roundDeleteLocked = groups.length > 0;

                    return [
                        '<div class="admin-bracket-structure-round">',
                        '<div class="admin-bracket-structure-round__head">',
                        '<div><span>' + escapeHtml(text(round?.roundKey) || "Vòng") + '</span><b>' + escapeHtml(text(round?.roundLabel) || "Chưa đặt tên") + '</b><small>ID vòng #' + escapeHtml(roundMapId) + ' · ' + escapeHtml(groups.length) + ' bảng · ' + escapeHtml(matchCount) + ' trận</small></div>',
                        '<button type="button" data-structure-delete-round="' + escapeHtml(roundMapId) + '" data-structure-locked="' + (roundDeleteLocked ? "true" : "false") + '" title="' + (roundDeleteLocked ? "Xóa hết bảng trong vòng trước" : "Xóa vòng") + '" aria-label="Xóa vòng ' + escapeHtml(text(round?.roundLabel)) + '"' + (roundDeleteLocked ? " disabled" : "") + '><i class="fas fa-trash-alt"></i></button>',
                        '</div>',
                        '<div class="admin-bracket-structure-groups">' + groupRows + '</div>',
                        '</div>'
                    ].join("");
                }).join("");
            }

            const roundDefaults = getNextRoundDefaults();
            const roundKeyInput = qs("[data-structure-round-key]", structureDrawer);
            const roundLabelInput = qs("[data-structure-round-label]", structureDrawer);
            const roundSortInput = qs("[data-structure-round-sort]", structureDrawer);
            if (resetDefaults || !text(roundKeyInput.value)) roundKeyInput.value = roundDefaults.roundKey;
            if (resetDefaults || !text(roundLabelInput.value)) roundLabelInput.value = roundDefaults.roundLabel;
            if (resetDefaults || !text(roundSortInput.value)) roundSortInput.value = String(roundDefaults.sortOrder);

            setGroupFormDefaults(selectedRoundMapId, !!resetDefaults);
            setStructureBusy(false);
        }

        function openStructureDrawer(roundMapId) {
            if (state.mode !== "design") {
                return;
            }

            closeDrawer();
            clearStructureError();
            renderStructurePanel(roundMapId, true);
            structureDrawer.classList.add("is-open");
            structureDrawer.setAttribute("aria-hidden", "false");
            document.body.classList.add("has-admin-bracket-editor");

            if (toNumber(roundMapId)) {
                qs("[data-structure-group-name]", structureDrawer).focus();
            } else {
                qs("[data-structure-round-key]", structureDrawer).focus();
            }
        }

        function closeStructureDrawer() {
            structureDrawer.classList.remove("is-open");
            structureDrawer.setAttribute("aria-hidden", "true");
            if (!drawer.classList.contains("is-open")) {
                document.body.classList.remove("has-admin-bracket-editor");
            }
            clearStructureError();
        }

        function setMode(mode) {
            state.mode = mode === "design" ? "design" : "view";
            page.classList.toggle("is-bracket-design-mode", state.mode === "design");
            qsa("[data-bracket-editor-mode]", modeControl).forEach(function (button) {
                const active = button.dataset.bracketEditorMode === state.mode;
                button.classList.toggle("is-active", active);
                button.setAttribute("aria-pressed", active ? "true" : "false");
            });

            if (state.mode === "view") {
                closeDrawer();
                closeStructureDrawer();
            }

            decorateBoard();
        }

        function decorateBoard() {
            qsa(".admin-bracket-round-title.is-real[data-round-map-id]", board).forEach(function (roundElement) {
                const roundMapId = toNumber(roundElement.dataset.roundMapId);
                if (!roundMapId) {
                    return;
                }

                let addGroupButton = qs("[data-bracket-create-group]", roundElement);
                if (!addGroupButton) {
                    addGroupButton = document.createElement("button");
                    addGroupButton.type = "button";
                    addGroupButton.className = "admin-bracket-round-title__add";
                    addGroupButton.dataset.bracketCreateGroup = String(roundMapId);
                    addGroupButton.innerHTML = '<i class="fas fa-plus"></i>';
                    roundElement.appendChild(addGroupButton);
                }

                addGroupButton.title = "Tạo bảng trong vòng này";
                addGroupButton.setAttribute("aria-label", addGroupButton.title);
            });

            qsa(".admin-bracket-group[data-group-id]", board).forEach(function (groupElement) {
                const groupId = toNumber(groupElement.dataset.groupId);
                if (!groupId) {
                    return;
                }

                let addButton = qs("[data-bracket-create-match]", groupElement);
                if (!addButton) {
                    addButton = document.createElement("button");
                    addButton.type = "button";
                    addButton.className = "admin-bracket-group__add";
                    addButton.dataset.bracketCreateMatch = String(groupId);
                    addButton.innerHTML = '<i class="fas fa-plus"></i>';
                    groupElement.appendChild(addButton);
                }

                const context = findGroupContext(getPayload(), groupId);
                addButton.title = "Tạo trận trong " + (text(context?.groupName) || ("bảng #" + groupId));
                addButton.setAttribute("aria-label", addButton.title);
            });

            qsa(".admin-bracket-match", board).forEach(function (card) {
                const completed = card.classList.contains("is-completed");
                let indicator = qs("[data-bracket-edit-indicator]", card);
                if (!indicator) {
                    indicator = document.createElement("span");
                    indicator.className = "admin-bracket-match__edit-indicator";
                    indicator.dataset.bracketEditIndicator = "true";
                    card.appendChild(indicator);
                }
                indicator.innerHTML = completed
                    ? '<i class="fas fa-lock"></i>'
                    : '<i class="fas fa-pen"></i>';
                card.classList.toggle("is-editor-locked", state.mode === "design" && completed);
                card.title = state.mode === "design"
                    ? (completed ? "Trận đã kết thúc - chỉ xem" : "Mở bảng chỉnh sửa trận")
                    : "Bấm để làm rõ dây liên kết";
            });
        }

        function openDrawer() {
            closeStructureDrawer();
            drawer.classList.add("is-open");
            drawer.setAttribute("aria-hidden", "false");
            document.body.classList.add("has-admin-bracket-editor");
        }

        function closeDrawer() {
            drawer.classList.remove("is-open");
            drawer.setAttribute("aria-hidden", "true");
            if (!structureDrawer.classList.contains("is-open")) {
                document.body.classList.remove("has-admin-bracket-editor");
            }
            clearError();
        }

        function clearError() {
            const errorBox = qs("[data-editor-error]", drawer);
            errorBox.classList.add("d-none");
            errorBox.innerHTML = "";
        }

        function showError(error) {
            const errorBox = qs("[data-editor-error]", drawer);
            const dependencies = Array.isArray(error?.payload?.dependencies)
                ? error.payload.dependencies
                : [];
            let html = '<div><i class="fas fa-exclamation-circle"></i><span>' + escapeHtml(error?.message || "Thao tác thất bại.") + "</span></div>";

            if (dependencies.length) {
                html += '<ul>' + dependencies.map(function (dependency) {
                    const round = text(dependency?.roundLabel) || text(dependency?.roundKey) || "Vòng sau";
                    const group = text(dependency?.groupName) || ("Bảng #" + toNumber(dependency?.groupId));
                    return "<li>Trận #" + escapeHtml(dependency?.matchId) + " - " + escapeHtml(round) + " / " + escapeHtml(group) + " - Vị trí " + escapeHtml(dependency?.slots) + "</li>";
                }).join("") + "</ul>";
            }

            errorBox.innerHTML = html;
            errorBox.classList.remove("d-none");
        }

        function showToast(message) {
            toast.textContent = message;
            toast.classList.add("is-visible");
            window.clearTimeout(toast._hideTimer);
            toast._hideTimer = window.setTimeout(function () {
                toast.classList.remove("is-visible");
            }, 2600);
        }

        function setBusy(busy) {
            state.saving = !!busy;
            const saveButton = qs("[data-editor-save]", drawer);
            const deleteButton = qs("[data-editor-delete]", drawer);
            saveButton.disabled = state.saving || !state.ready;
            deleteButton.disabled = state.saving || !state.ready;
            saveButton.innerHTML = state.saving
                ? '<i class="fas fa-spinner fa-spin"></i><span>Đang lưu...</span>'
                : '<i class="fas fa-save"></i><span>Lưu trận</span>';
        }

        async function loadRegistrations() {
            if (state.registrations) {
                return state.registrations;
            }

            const payload = await requestJson("/api/admin/tournaments/" + tournamentId + "/registrations?tab=SUCCESS");
            state.registrations = Array.isArray(payload?.items) ? payload.items : [];
            return state.registrations;
        }

        async function loadSourceOptions(groupId, force) {
            if (!force && sourceOptionsCache.has(groupId)) {
                return sourceOptionsCache.get(groupId);
            }

            const payload = await requestJson("/api/admin/groups/" + groupId + "/matches/source-options");
            sourceOptionsCache.set(groupId, payload);
            return payload;
        }

        async function loadMatchDetail(groupId, matchId) {
            const payload = await requestJson("/api/admin/groups/" + groupId + "/matches");
            return (Array.isArray(payload?.items) ? payload.items : []).find(function (item) {
                return getMatchId(item) === matchId;
            }) || null;
        }

        function setSelectOptions(select, items, valueSelector, labelSelector, selectedValue, placeholder) {
            select.innerHTML = "";
            const placeholderOption = document.createElement("option");
            placeholderOption.value = "";
            placeholderOption.textContent = placeholder;
            select.appendChild(placeholderOption);

            items.forEach(function (item) {
                const option = document.createElement("option");
                option.value = String(valueSelector(item));
                option.textContent = labelSelector(item);
                select.appendChild(option);
            });

            select.value = selectedValue ? String(selectedValue) : "";
        }

        function registrationLabel(item) {
            const names = [text(item?.player1Name), text(item?.player2Name)].filter(Boolean).join(" & ");
            const code = text(item?.regCode) || ("ID " + toNumber(item?.registrationId));
            return "ID " + toNumber(item?.registrationId) + " | " + code + " - " + (names || "Chưa có tên đội");
        }

        function registrationSearchText(item) {
            return normalizeSearchText([
                item?.registrationId,
                item?.regCode,
                item?.player1Name,
                item?.player2Name
            ].filter(function (value) { return text(value); }).join(" "));
        }

        function populateRegistrationSelect(slotNumber, selectedRegistrationId, query, autoSelect) {
            const section = qs('[data-editor-slot="' + slotNumber + '"]', drawer);
            const select = qs("[data-slot-registration]", section);
            const status = qs("[data-slot-registration-status]", section);
            const normalizedQuery = normalizeSearchText(query);
            const registrations = state.registrations || [];
            let matches = registrations;
            let exactMatch = null;

            if (normalizedQuery) {
                exactMatch = registrations.find(function (item) {
                    return String(toNumber(item?.registrationId)) === normalizedQuery
                        || normalizeSearchText(item?.regCode) === normalizedQuery;
                }) || null;
                matches = exactMatch
                    ? [exactMatch]
                    : registrations.filter(function (item) {
                        return registrationSearchText(item).includes(normalizedQuery);
                    });
            }

            const selectedStillVisible = matches.some(function (item) {
                return toNumber(item?.registrationId) === toNumber(selectedRegistrationId);
            });
            setSelectOptions(
                select,
                matches,
                function (item) { return toNumber(item?.registrationId); },
                registrationLabel,
                selectedStillVisible ? selectedRegistrationId : null,
                matches.length ? "Chọn đội đăng ký" : "Không tìm thấy đội"
            );

            const autoSelected = autoSelect && (exactMatch || matches.length === 1)
                ? (exactMatch || matches[0])
                : null;
            if (autoSelected) {
                select.value = String(toNumber(autoSelected.registrationId));
            }

            status.classList.toggle("is-error", !!normalizedQuery && matches.length === 0);
            status.classList.toggle("is-selected", !!autoSelected);
            if (!normalizedQuery) {
                status.textContent = registrations.length + " đội hợp lệ trong giải.";
            } else if (matches.length === 0) {
                status.textContent = "Không tìm thấy đội phù hợp trong giải.";
            } else if (autoSelected) {
                status.textContent = "Đã chọn " + registrationLabel(autoSelected) + ".";
            } else {
                status.textContent = "Tìm thấy " + matches.length + " đội. Chọn trong danh sách bên dưới.";
            }

            updateSlotPreview(slotNumber);
        }

        function flattenSourceMatches(sourceOptions, currentMatchId) {
            const result = [];
            const rounds = Array.isArray(sourceOptions?.previousRounds) ? sourceOptions.previousRounds : [];

            rounds.forEach(function (round) {
                const groups = Array.isArray(round?.groups) ? round.groups : [];
                groups.forEach(function (group) {
                    const matches = Array.isArray(group?.matches) ? group.matches : [];
                    matches.forEach(function (match) {
                        if (getMatchId(match) === currentMatchId) {
                            return;
                        }

                        result.push({
                            matchId: getMatchId(match),
                            label: (text(round?.roundKey) || text(round?.roundLabel)) + " / " + text(group?.groupName) + " - " + text(match?.label),
                            isCompleted: !!match?.isCompleted
                        });
                    });
                });
            });

            return result;
        }

        function flattenSourceGroups(sourceOptions) {
            const result = [];
            const rounds = Array.isArray(sourceOptions?.previousRounds) ? sourceOptions.previousRounds : [];

            rounds.forEach(function (round) {
                const groups = Array.isArray(round?.groups) ? round.groups : [];
                groups.forEach(function (group) {
                    result.push({
                        groupId: toNumber(group?.groupId),
                        label: (text(round?.roundKey) || text(round?.roundLabel)) + " / " + text(group?.groupName)
                    });
                });
            });

            return result;
        }

        function getSlotValue(match, slotNumber, suffix) {
            const prefix = slotNumber === 1 ? "team1" : "team2";
            return match?.[prefix + suffix];
        }

        function fillSlot(slotNumber, match) {
            const section = qs('[data-editor-slot="' + slotNumber + '"]', drawer);
            const sourceType = text(getSlotValue(match, slotNumber, "SourceType")) || SOURCE_TYPES.registration;
            const sourceSelect = qs("[data-slot-source-type]", section);
            sourceSelect.value = sourceType;

            qs("[data-slot-registration-search]", section).value = "";
            populateRegistrationSelect(
                slotNumber,
                getSlotValue(match, slotNumber, "RegistrationId"),
                "",
                false
            );

            const sourceMatches = flattenSourceMatches(state.sourceOptions, state.matchId);
            setSelectOptions(
                qs("[data-slot-match]", section),
                sourceMatches,
                function (item) { return item.matchId; },
                function (item) { return item.label + (item.isCompleted ? " (đã xong)" : " (đang chờ)"); },
                getSlotValue(match, slotNumber, "SourceMatchId"),
                "Chọn trận nguồn"
            );

            const sourceGroups = flattenSourceGroups(state.sourceOptions);
            setSelectOptions(
                qs("[data-slot-group]", section),
                sourceGroups,
                function (item) { return item.groupId; },
                function (item) { return item.label; },
                getSlotValue(match, slotNumber, "SourceGroupId"),
                "Chọn bảng nguồn"
            );

            qs("[data-slot-rank]", section).value = String(toNumber(getSlotValue(match, slotNumber, "SourceRank")) || 1);
            updateSlotPanels(slotNumber);
        }

        function updateSlotPanels(slotNumber) {
            const section = qs('[data-editor-slot="' + slotNumber + '"]', drawer);
            const sourceType = qs("[data-slot-source-type]", section).value;
            qsa("[data-slot-panel]", section).forEach(function (panel) {
                const panelType = panel.dataset.slotPanel;
                const visible = panelType === sourceType
                    || (panelType === "MATCH" && (sourceType === SOURCE_TYPES.winner || sourceType === SOURCE_TYPES.loser));
                panel.classList.toggle("d-none", !visible);
            });
            updateSlotPreview(slotNumber);
        }

        function updateSlotPreview(slotNumber) {
            const section = qs('[data-editor-slot="' + slotNumber + '"]', drawer);
            const sourceType = qs("[data-slot-source-type]", section).value;
            const preview = qs("[data-slot-preview]", section);
            let previewText = "Chưa chọn nguồn";

            if (sourceType === SOURCE_TYPES.registration) {
                const select = qs("[data-slot-registration]", section);
                previewText = select.selectedOptions[0]?.value ? select.selectedOptions[0].textContent : "Chưa chọn đội";
            } else if (sourceType === SOURCE_TYPES.winner || sourceType === SOURCE_TYPES.loser) {
                const select = qs("[data-slot-match]", section);
                const matchId = toNumber(select.value);
                previewText = matchId > 0
                    ? (sourceType === SOURCE_TYPES.winner ? "Chờ thắng trận #" : "Chờ thua trận #") + matchId
                    : "Chưa chọn trận nguồn";
            } else if (sourceType === SOURCE_TYPES.groupRank) {
                const groupSelect = qs("[data-slot-group]", section);
                const rank = toNumber(qs("[data-slot-rank]", section).value);
                previewText = groupSelect.value
                    ? "Chờ hạng " + (rank || 1) + " - " + groupSelect.selectedOptions[0].textContent
                    : "Chưa chọn bảng nguồn";
            } else if (sourceType === SOURCE_TYPES.bye) {
                previewText = "Miễn đấu (đang giữ cấu hình cũ)";
            }

            preview.textContent = previewText;
            preview.title = previewText;
        }

        function setReferee(referee) {
            state.referee = referee || null;
            const box = qs("[data-editor-referee]", drawer);
            if (!referee) {
                box.className = "admin-bracket-editor-referee is-empty";
                box.textContent = "Chưa xác nhận trọng tài.";
                return;
            }

            const details = [text(referee.fullName), text(referee.phone), text(referee.email)].filter(Boolean);
            box.className = "admin-bracket-editor-referee is-valid";
            box.innerHTML = '<i class="fas fa-check-circle"></i><div><b>' + escapeHtml(details[0] || ("Người dùng #" + referee.userId)) + '</b><span>' + escapeHtml(details.slice(1).join(" · ") || ("ID người dùng " + referee.userId)) + "</span></div>";
        }

        async function verifyReferee() {
            const input = qs('[data-editor-field="refereeUserId"]', drawer);
            const userId = toNumber(input.value);
            if (!userId) {
                throw new Error("Bắt buộc nhập ID người dùng của trọng tài.");
            }

            if (toNumber(state.referee?.userId) === userId) {
                return state.referee;
            }

            const referee = await requestJson("/api/admin/referees/find-by-user-id/" + userId);
            setReferee(referee);
            return referee;
        }

        function setFormLocked(locked) {
            const lockBox = qs("[data-editor-lock]", drawer);
            lockBox.classList.toggle("d-none", !locked);
            qsa("input, select", qs("[data-editor-form]", drawer)).forEach(function (field) {
                field.disabled = locked;
            });
            qs("[data-editor-referee-check]", drawer).disabled = locked;
            qsa("[data-slot-registration-find]", drawer).forEach(function (button) {
                button.disabled = locked;
            });
            qs("[data-editor-save]", drawer).classList.toggle("d-none", locked);
            qs("[data-editor-delete]", drawer).classList.toggle("d-none", locked || state.formMode !== "edit");
        }

        async function prepareForm(context, match) {
            state.context = context;
            state.groupId = context.groupId;
            state.matchId = getMatchId(match);
            state.formMode = state.matchId > 0 ? "edit" : "create";
            state.match = match || null;
            state.referee = null;
            state.ready = false;
            clearError();
            openDrawer();
            qs(".admin-bracket-editor-drawer__body", drawer).scrollTop = 0;

            qs("[data-editor-title]", drawer).textContent = state.formMode === "create"
                ? "Tạo trận đấu"
                : "Trận #" + state.matchId;
            qs("[data-editor-kicker]", drawer).textContent = state.formMode === "create"
                ? "TRẬN MỚI · ID TỰ ĐỘNG"
                : "CHỈNH SỬA TRỰC TIẾP";
            qs("[data-editor-context]", drawer).innerHTML = '<i class="fas fa-sitemap"></i><div><b>' + escapeHtml(formatGroupContext(context)) + '</b><span>Giải đấu #' + escapeHtml(tournamentId) + ' · ID bảng #' + escapeHtml(context.groupId) + "</span></div>";

            try {
                setBusy(true);
                const references = await Promise.all([
                    loadRegistrations(),
                    loadSourceOptions(context.groupId, false)
                ]);
                state.registrations = references[0];
                state.sourceOptions = references[1];

                const detail = state.formMode === "edit"
                    ? (await loadMatchDetail(context.groupId, state.matchId))
                    : null;
                if (state.formMode === "edit" && !detail) {
                    throw new Error("Không tải được chi tiết trận #" + state.matchId + ".");
                }

                state.match = detail || {};
                fillSlot(1, state.match);
                fillSlot(2, state.match);
                qs('[data-editor-field="startAt"]', drawer).value = toDateTimeLocal(state.match?.startAt);
                qs('[data-editor-field="courtText"]', drawer).value = text(state.match?.courtText);
                qs('[data-editor-field="addressText"]', drawer).value = text(state.match?.addressText);
                qs('[data-editor-field="videoUrl"]', drawer).value = text(state.match?.videoUrl);
                qs('[data-editor-field="refereeUserId"]', drawer).value = state.match?.refereeUserId || "";

                if (state.match?.refereeUserId) {
                    setReferee({
                        userId: state.match.refereeUserId,
                        fullName: state.match.refereeName,
                        phone: state.match.refereePhone,
                        email: state.match.refereeEmail
                    });
                } else {
                    setReferee(null);
                }

                setFormLocked(!!state.match?.isCompleted);
                state.ready = true;
            } catch (error) {
                showError(error);
            } finally {
                setBusy(false);
            }
        }

        async function openCreate(groupId) {
            const context = findGroupContext(getPayload(), groupId);
            if (!context) {
                showToast("Không tìm thấy group thật để tạo trận.");
                return;
            }

            await prepareForm(context, null);
        }

        async function openEdit(matchId) {
            const context = findMatchContext(getPayload(), matchId);
            if (!context) {
                showToast("Không tìm thấy trận trên nhánh đấu hiện tại.");
                return;
            }

            await prepareForm(context, context.match);
        }

        function buildSlotPayload(slotNumber) {
            const section = qs('[data-editor-slot="' + slotNumber + '"]', drawer);
            const sourceType = qs("[data-slot-source-type]", section).value;

            if (sourceType === SOURCE_TYPES.registration) {
                const registrationId = toNumber(qs("[data-slot-registration]", section).value);
                if (!registrationId) {
                    throw new Error("Vị trí " + (slotNumber === 1 ? "A" : "B") + ": chưa chọn đội đăng ký.");
                }
                return { sourceType: sourceType, registrationId: registrationId };
            }

            if (sourceType === SOURCE_TYPES.winner || sourceType === SOURCE_TYPES.loser) {
                const sourceMatchId = toNumber(qs("[data-slot-match]", section).value);
                if (!sourceMatchId) {
                    throw new Error("Vị trí " + (slotNumber === 1 ? "A" : "B") + ": chưa chọn trận nguồn.");
                }
                return { sourceType: sourceType, sourceMatchId: sourceMatchId };
            }

            if (sourceType === SOURCE_TYPES.groupRank) {
                const sourceGroupId = toNumber(qs("[data-slot-group]", section).value);
                const sourceRank = toNumber(qs("[data-slot-rank]", section).value);
                if (!sourceGroupId || sourceRank < 1) {
                    throw new Error("Vị trí " + (slotNumber === 1 ? "A" : "B") + ": bảng nguồn hoặc thứ hạng không hợp lệ.");
                }
                return { sourceType: sourceType, sourceGroupId: sourceGroupId, sourceRank: sourceRank };
            }

            if (sourceType === SOURCE_TYPES.bye && state.formMode === "edit") {
                return { sourceType: SOURCE_TYPES.bye };
            }

            throw new Error("Nguồn của vị trí " + (slotNumber === 1 ? "A" : "B") + " không hợp lệ.");
        }

        function buildPayload() {
            const team1 = buildSlotPayload(1);
            const team2 = buildSlotPayload(2);

            if (team1.sourceType === SOURCE_TYPES.registration
                && team2.sourceType === SOURCE_TYPES.registration
                && team1.registrationId === team2.registrationId) {
                throw new Error("Vị trí A và vị trí B không được chọn cùng một đội.");
            }

            const videoUrl = text(qs('[data-editor-field="videoUrl"]', drawer).value);
            if (videoUrl && !/^https?:\/\//i.test(videoUrl)) {
                throw new Error("Liên kết video phải bắt đầu bằng http:// hoặc https://.");
            }

            const refereeUserId = toNumber(qs('[data-editor-field="refereeUserId"]', drawer).value);
            if (!refereeUserId) {
                throw new Error("Trận đấu bắt buộc phải có trọng tài hợp lệ.");
            }

            const payload = {
                team1: team1,
                team2: team2,
                team1RegistrationId: team1.sourceType === SOURCE_TYPES.registration ? team1.registrationId : null,
                team2RegistrationId: team2.sourceType === SOURCE_TYPES.registration ? team2.registrationId : null,
                startAt: toIsoDateTime(qs('[data-editor-field="startAt"]', drawer).value),
                addressText: text(qs('[data-editor-field="addressText"]', drawer).value) || null,
                courtText: text(qs('[data-editor-field="courtText"]', drawer).value) || null,
                videoUrl: videoUrl || null,
                refereeUserId: refereeUserId
            };

            if (state.formMode === "edit") {
                payload.startAtSet = true;
            }

            return payload;
        }

        async function reloadAfterChange(matchId) {
            sourceOptionsCache.clear();
            const viewer = getViewer();
            if (viewer?.reload) {
                await viewer.reload({
                    preserveViewport: true,
                    focusMatchId: matchId || null
                });
            }

            page.dispatchEvent(new CustomEvent("adminbracket:changed", {
                detail: {
                    groupId: state.groupId,
                    matchId: matchId || null,
                    action: state.formMode
                }
            }));
        }

        async function reloadAfterStructureChange(action, detail) {
            sourceOptionsCache.clear();
            const viewer = getViewer();
            if (viewer?.reload) {
                await viewer.reload({ preserveViewport: true });
            }

            page.dispatchEvent(new CustomEvent("adminbracket:changed", {
                detail: Object.assign({
                    action: action,
                    structureChanged: true
                }, detail || {})
            }));
        }

        async function createRound(event) {
            event.preventDefault();
            if (state.structureBusy) {
                return;
            }

            const roundKey = text(qs("[data-structure-round-key]", structureDrawer).value);
            const roundLabel = text(qs("[data-structure-round-label]", structureDrawer).value);
            const sortRaw = text(qs("[data-structure-round-sort]", structureDrawer).value);
            if (!roundKey || !roundLabel || !sortRaw) {
                showStructureError(new Error("Nhập đầy đủ mã vòng, tên vòng và thứ tự."));
                return;
            }

            clearStructureError();
            try {
                setStructureBusy(true);
                const result = await requestJson("/api/admin/tournaments/" + tournamentId + "/round-maps", {
                    method: "POST",
                    body: {
                        roundKey: roundKey,
                        roundLabel: roundLabel,
                        sortOrder: Math.max(0, toNumber(sortRaw))
                    }
                });
                const roundMapId = getRoundMapId(result);
                await reloadAfterStructureChange("round-created", { roundMapId: roundMapId });
                renderStructurePanel(roundMapId, true);
                qs("[data-structure-group-name]", structureDrawer).focus();
                showToast("Đã tạo vòng " + roundLabel + ".");
            } catch (error) {
                showStructureError(error);
            } finally {
                setStructureBusy(false);
            }
        }

        async function createGroup(event) {
            event.preventDefault();
            if (state.structureBusy) {
                return;
            }

            const roundMapId = toNumber(qs("[data-structure-group-round]", structureDrawer).value);
            const groupName = text(qs("[data-structure-group-name]", structureDrawer).value);
            const sortRaw = text(qs("[data-structure-group-sort]", structureDrawer).value);
            if (!roundMapId || !groupName || !sortRaw) {
                showStructureError(new Error("Chọn vòng và nhập đầy đủ tên, thứ tự bảng."));
                return;
            }

            clearStructureError();
            try {
                setStructureBusy(true);
                const result = await requestJson("/api/admin/round-maps/" + roundMapId + "/groups", {
                    method: "POST",
                    body: {
                        groupName: groupName,
                        sortOrder: Math.max(0, toNumber(sortRaw))
                    }
                });
                const groupId = getGroupId(result);
                await reloadAfterStructureChange("group-created", {
                    roundMapId: roundMapId,
                    groupId: groupId
                });
                renderStructurePanel(roundMapId, true);
                qs("[data-structure-group-name]", structureDrawer).focus();
                showToast("Đã tạo bảng " + groupName + ".");
            } catch (error) {
                showStructureError(error);
            } finally {
                setStructureBusy(false);
            }
        }

        async function deleteStructureRound(roundMapId) {
            if (state.structureBusy || !roundMapId) {
                return;
            }
            if (!window.confirm("Xóa vòng đấu này? Vòng chỉ xóa được khi không còn bảng.")) {
                return;
            }

            clearStructureError();
            try {
                setStructureBusy(true);
                await requestJson("/api/admin/tournaments/" + tournamentId + "/round-maps/" + roundMapId, {
                    method: "DELETE"
                });
                await reloadAfterStructureChange("round-deleted", { roundMapId: roundMapId });
                renderStructurePanel(0, true);
                showToast("Đã xóa vòng đấu.");
            } catch (error) {
                showStructureError(error);
            } finally {
                setStructureBusy(false);
            }
        }

        async function deleteStructureGroup(roundMapId, groupId) {
            if (state.structureBusy || !roundMapId || !groupId) {
                return;
            }
            if (!window.confirm("Xóa bảng đấu này? Bảng chỉ xóa được khi không còn trận và không được dùng làm nguồn.")) {
                return;
            }

            clearStructureError();
            try {
                setStructureBusy(true);
                await requestJson("/api/admin/round-maps/" + roundMapId + "/groups/" + groupId, {
                    method: "DELETE"
                });
                await reloadAfterStructureChange("group-deleted", {
                    roundMapId: roundMapId,
                    groupId: groupId
                });
                renderStructurePanel(roundMapId, true);
                showToast("Đã xóa bảng đấu.");
            } catch (error) {
                showStructureError(error);
            } finally {
                setStructureBusy(false);
            }
        }

        async function saveMatch(event) {
            event.preventDefault();
            if (state.saving || !state.ready || state.match?.isCompleted) {
                return;
            }

            clearError();
            try {
                setBusy(true);
                await verifyReferee();
                const payload = buildPayload();
                let result;

                if (state.formMode === "create") {
                    result = await requestJson("/api/admin/groups/" + state.groupId + "/matches", {
                        method: "POST",
                        body: payload
                    });
                } else {
                    result = await requestJson("/api/admin/groups/" + state.groupId + "/matches/" + state.matchId, {
                        method: "PUT",
                        body: payload
                    });
                }

                const savedMatchId = toNumber(result?.matchId) || state.matchId;
                const message = state.formMode === "create"
                    ? "Đã tạo trận #" + savedMatchId + "."
                    : "Đã cập nhật trận #" + savedMatchId + ".";
                closeDrawer();
                await reloadAfterChange(savedMatchId);
                showToast(message);
            } catch (error) {
                showError(error);
            } finally {
                setBusy(false);
            }
        }

        async function deleteMatch() {
            if (state.saving || !state.ready || state.formMode !== "edit" || state.match?.isCompleted) {
                return;
            }

            if (!window.confirm("Xóa trận #" + state.matchId + "? Chỉ có thể xóa khi trận này không còn cấp nguồn cho trận sau.")) {
                return;
            }

            clearError();
            try {
                setBusy(true);
                await requestJson("/api/admin/groups/" + state.groupId + "/matches/" + state.matchId, {
                    method: "DELETE"
                });
                const deletedMatchId = state.matchId;
                closeDrawer();
                await reloadAfterChange(null);
                showToast("Đã xóa trận #" + deletedMatchId + ".");
            } catch (error) {
                showError(error);
            } finally {
                setBusy(false);
            }
        }

        modeControl.addEventListener("click", function (event) {
            const button = event.target.closest("[data-bracket-editor-mode]");
            if (button) {
                setMode(button.dataset.bracketEditorMode);
            }
        });
        structureButton.addEventListener("click", function () {
            openStructureDrawer(0);
        });

        board.addEventListener("click", function (event) {
            if (state.mode !== "design") {
                return;
            }

            const createGroupButton = event.target.closest("[data-bracket-create-group]");
            if (createGroupButton) {
                event.preventDefault();
                event.stopPropagation();
                openStructureDrawer(toNumber(createGroupButton.dataset.bracketCreateGroup));
                return;
            }

            const createButton = event.target.closest("[data-bracket-create-match]");
            if (createButton) {
                event.preventDefault();
                event.stopPropagation();
                openCreate(toNumber(createButton.dataset.bracketCreateMatch));
                return;
            }

            const card = event.target.closest(".admin-bracket-match[data-match-id]");
            if (card) {
                event.preventDefault();
                event.stopPropagation();
                openEdit(toNumber(card.dataset.matchId));
            }
        }, true);

        board.addEventListener("keydown", function (event) {
            if (state.mode !== "design" || (event.key !== "Enter" && event.key !== " ")) {
                return;
            }

            const card = event.target.closest(".admin-bracket-match[data-match-id]");
            if (card) {
                event.preventDefault();
                event.stopPropagation();
                openEdit(toNumber(card.dataset.matchId));
            }
        }, true);

        page.addEventListener("adminbracket:rendered", decorateBoard);
        qs("[data-editor-close]", drawer).addEventListener("click", closeDrawer);
        qs("[data-editor-cancel]", drawer).addEventListener("click", closeDrawer);
        qs("[data-editor-delete]", drawer).addEventListener("click", deleteMatch);
        qs("[data-editor-form]", drawer).addEventListener("submit", saveMatch);
        qs("[data-structure-close]", structureDrawer).addEventListener("click", closeStructureDrawer);
        qs("[data-structure-round-form]", structureDrawer).addEventListener("submit", createRound);
        qs("[data-structure-group-form]", structureDrawer).addEventListener("submit", createGroup);
        qs("[data-structure-group-round]", structureDrawer).addEventListener("change", function () {
            setGroupFormDefaults(toNumber(this.value), true);
        });
        qs("[data-structure-list]", structureDrawer).addEventListener("click", function (event) {
            const deleteRoundButton = event.target.closest("[data-structure-delete-round]");
            if (deleteRoundButton && !deleteRoundButton.disabled) {
                deleteStructureRound(toNumber(deleteRoundButton.dataset.structureDeleteRound));
                return;
            }

            const deleteGroupButton = event.target.closest("[data-structure-delete-group]");
            if (deleteGroupButton && !deleteGroupButton.disabled) {
                deleteStructureGroup(
                    toNumber(deleteGroupButton.dataset.roundMapId),
                    toNumber(deleteGroupButton.dataset.structureDeleteGroup)
                );
            }
        });
        qs("[data-editor-referee-check]", drawer).addEventListener("click", async function () {
            clearError();
            try {
                await verifyReferee();
            } catch (error) {
                setReferee(null);
                showError(error);
            }
        });
        qs('[data-editor-field="refereeUserId"]', drawer).addEventListener("input", function () {
            if (toNumber(state.referee?.userId) !== toNumber(this.value)) {
                setReferee(null);
            }
        });
        qsa("[data-editor-slot]", drawer).forEach(function (section) {
            const slotNumber = toNumber(section.dataset.editorSlot);
            const registrationSearch = qs("[data-slot-registration-search]", section);
            const registrationSelect = qs("[data-slot-registration]", section);
            qs("[data-slot-source-type]", section).addEventListener("change", function () {
                updateSlotPanels(slotNumber);
            });
            registrationSearch.addEventListener("input", function () {
                populateRegistrationSelect(slotNumber, registrationSelect.value, this.value, false);
            });
            registrationSearch.addEventListener("keydown", function (event) {
                if (event.key !== "Enter") {
                    return;
                }
                event.preventDefault();
                populateRegistrationSelect(slotNumber, registrationSelect.value, this.value, true);
            });
            qs("[data-slot-registration-find]", section).addEventListener("click", function () {
                populateRegistrationSelect(slotNumber, registrationSelect.value, registrationSearch.value, true);
            });
            qsa("select, input", section).forEach(function (field) {
                field.addEventListener("change", function () {
                    updateSlotPreview(slotNumber);
                });
                field.addEventListener("input", function () {
                    updateSlotPreview(slotNumber);
                });
            });
        });
        document.addEventListener("keydown", function (event) {
            if (event.key === "Escape") {
                if (structureDrawer.classList.contains("is-open")) {
                    closeStructureDrawer();
                } else if (drawer.classList.contains("is-open")) {
                    closeDrawer();
                }
            }
        });

        const api = {
            setMode: setMode,
            getMode: function () { return state.mode; },
            openCreate: openCreate,
            openEdit: openEdit,
            openStructure: openStructureDrawer,
            close: function () {
                closeDrawer();
                closeStructureDrawer();
            }
        };
        page._adminTournamentBracketEditor = api;
        setMode("view");
        return api;
    }

    window.AdminTournamentBracketEditor = window.AdminTournamentBracketEditor || {};
    window.AdminTournamentBracketEditor.init = initBracketEditor;

    qsa('[data-admin-bracket-editable="true"]').forEach(initBracketEditor);
})();
