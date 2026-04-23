(function () {
    function qs(selector, root) {
        return (root || document).querySelector(selector);
    }

    function qsa(selector, root) {
        return Array.from((root || document).querySelectorAll(selector));
    }

    function trimToEmpty(value) {
        return String(value ?? "").trim();
    }

    function toNumber(value) {
        const number = Number(value);
        return Number.isFinite(number) ? number : 0;
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function debounce(fn, delay) {
        let timer = null;
        return function () {
            const args = arguments;
            window.clearTimeout(timer);
            timer = window.setTimeout(function () {
                fn.apply(null, args);
            }, delay);
        };
    }

    function parseDate(value) {
        if (!value) {
            return null;
        }

        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? null : date;
    }

    function formatClock(value) {
        const date = parseDate(value);
        if (!date) {
            return "";
        }

        return new Intl.DateTimeFormat("vi-VN", {
            hour: "2-digit",
            minute: "2-digit"
        }).format(date);
    }

    function formatDateTime(value) {
        const date = parseDate(value);
        if (!date) {
            return "Chưa cập nhật";
        }

        return new Intl.DateTimeFormat("vi-VN", {
            day: "2-digit",
            month: "2-digit",
            year: "numeric",
            hour: "2-digit",
            minute: "2-digit"
        }).format(date);
    }

    async function fetchJson(url) {
        const response = await fetch(url, {
            headers: { Accept: "application/json" },
            cache: "no-store",
            credentials: "same-origin"
        });

        if (!response.ok) {
            const payload = await response.text().catch(function () { return ""; });
            throw new Error(trimToEmpty(payload) || ("Request failed: " + response.status));
        }

        return response.json();
    }

    function parseTrailingNumber(value) {
        const normalized = trimToEmpty(value);
        const match = normalized.match(/^(.*?)(\d+)$/);

        if (!match) {
            return null;
        }

        return {
            prefix: match[1],
            start: toNumber(match[2]),
            width: match[2].length
        };
    }

    function buildRoundKey(round, roundIndex) {
        return trimToEmpty(round?.roundKey) || ("R" + (roundIndex + 1));
    }

    function buildRoundLabel(round, roundIndex) {
        return trimToEmpty(round?.roundLabel) || ("Vòng " + (roundIndex + 1));
    }

    function buildSyntheticRoundKey(previousRoundKey, roundIndex) {
        const parsed = parseTrailingNumber(previousRoundKey);

        if (parsed) {
            return parsed.prefix + String(parsed.start + 1).padStart(parsed.width, "0");
        }

        return "R" + (roundIndex + 1);
    }

    function buildSyntheticRoundLabel(previousRoundLabel, roundIndex) {
        const parsed = parseTrailingNumber(previousRoundLabel);

        if (parsed) {
            return parsed.prefix + String(parsed.start + 1).padStart(parsed.width, "0");
        }

        return "Vòng " + (roundIndex + 1);
    }

    function normalizeGroupKeyPart(groupName, groupIndex) {
        const raw = trimToEmpty(groupName);

        if (!raw) {
            return "B" + (groupIndex + 1);
        }

        const normalized = raw
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .trim();
        const compact = normalized.replace(/\s+/g, " ");

        if (/^Bang\s+\d+$/i.test(compact)) {
            return "B" + compact.replace(/^Bang\s+/i, "");
        }

        if (/^\d+$/.test(compact)) {
            return "B" + compact;
        }

        if (/^[A-Za-z]+$/.test(compact)) {
            return compact.toUpperCase();
        }

        const safe = compact
            .toUpperCase()
            .replace(/[^A-Z0-9]+/g, "-")
            .replace(/^-+|-+$/g, "");

        return safe || ("B" + (groupIndex + 1));
    }

    function buildGroupDisplayKey(roundKey, groupName, groupIndex) {
        return roundKey + "-" + normalizeGroupKeyPart(groupName, groupIndex);
    }

    function formatScore(value) {
        if (value === null || value === undefined || value === "") {
            return "0";
        }

        return String(toNumber(value));
    }

    function buildTeamName(team, fallback) {
        return trimToEmpty(team?.displayName) || fallback;
    }

    function buildTeamIdentity(team, fallbackRegistrationId) {
        const registrationId = toNumber(team?.registrationId) || toNumber(fallbackRegistrationId);
        const regCode = trimToEmpty(team?.regCode);
        const parts = [];

        if (registrationId > 0) {
            parts.push("ID " + registrationId);
        }

        if (regCode) {
            parts.push(regCode);
        }

        return parts.join(" | ");
    }

    function buildMatchMeta(match) {
        const details = [];
        const timeText = formatClock(match?.startAt);
        const courtText = trimToEmpty(match?.courtText);
        const addressText = trimToEmpty(match?.addressText);

        if (timeText) {
            details.push(timeText);
        }

        if (courtText) {
            details.push(courtText);
        } else if (addressText) {
            details.push(addressText);
        }

        return details.join(" | ");
    }

    function buildMatchCard(match, matchIndex, groupKey) {
        const winnerId = match?.winnerRegistrationId;
        const isWinnerA = !!winnerId && winnerId === match?.team1RegistrationId;
        const isWinnerB = !!winnerId && winnerId === match?.team2RegistrationId;

        return {
            isCompleted: !!match?.isCompleted,
            hasVideo: !!trimToEmpty(match?.videoUrl),
            title: "#" + (trimToEmpty(match?.matchId) || (groupKey + "-" + (matchIndex + 1))),
            metaText: buildMatchMeta(match),
            teamA: buildTeamName(match?.team1, groupKey + "-#1"),
            teamB: buildTeamName(match?.team2, groupKey + "-#2"),
            teamAIdentity: buildTeamIdentity(match?.team1, match?.team1RegistrationId),
            teamBIdentity: buildTeamIdentity(match?.team2, match?.team2RegistrationId),
            scoreA: formatScore(match?.scoreTeam1),
            scoreB: formatScore(match?.scoreTeam2),
            isWinnerA: isWinnerA,
            isWinnerB: isWinnerB,
            teamRegistrationIds: [
                toNumber(match?.team1RegistrationId),
                toNumber(match?.team2RegistrationId)
            ].filter(function (item) { return item > 0; })
        };
    }

    function buildActualGroup(round, group, roundIndex, groupIndex) {
        const roundKey = buildRoundKey(round, roundIndex);
        const groupName = trimToEmpty(group?.groupName) || ("Bảng " + (groupIndex + 1));
        const groupKey = buildGroupDisplayKey(roundKey, groupName, groupIndex);
        const matches = Array.isArray(group?.matches) ? group.matches : [];
        const matchCards = matches.map(function (match, matchIndex) {
            return buildMatchCard(match, matchIndex, groupKey);
        });

        return {
            isReal: true,
            groupName: groupName,
            groupKey: groupKey,
            matchCount: matchCards.length,
            completedCount: matchCards.filter(function (item) { return item.isCompleted; }).length,
            matches: matchCards,
            sourceIndexes: [],
            sourceKeys: []
        };
    }

    function buildActualRound(round, roundIndex) {
        const groups = Array.isArray(round?.groups) ? round.groups : [];

        return {
            isSynthetic: false,
            roundKey: buildRoundKey(round, roundIndex),
            roundLabel: buildRoundLabel(round, roundIndex),
            sortOrder: toNumber(round?.sortOrder),
            groups: groups.map(function (group, groupIndex) {
                return buildActualGroup(round, group, roundIndex, groupIndex);
            })
        };
    }

    function buildWinnerSourceMap(previousRound) {
        const sourceMap = new Map();

        (previousRound?.groups || []).forEach(function (group, groupIndex) {
            (group?.matches || []).forEach(function (match) {
                const registrationIds = Array.isArray(match?.teamRegistrationIds) ? match.teamRegistrationIds : [];
                registrationIds.forEach(function (registrationId) {
                    const key = String(toNumber(registrationId));
                    if (registrationId > 0 && !sourceMap.has(key)) {
                        sourceMap.set(key, groupIndex);
                    }
                });
            });
        });

        return sourceMap;
    }

    function buildDistributedSourceIndexes(previousCount, currentCount, currentIndex) {
        if (previousCount <= 0) {
            return [];
        }

        const safeCurrentCount = Math.max(1, currentCount);
        const start = Math.floor(currentIndex * previousCount / safeCurrentCount);
        const end = Math.floor((currentIndex + 1) * previousCount / safeCurrentCount) - 1;
        const indexes = [];

        if (end < start) {
            indexes.push(Math.min(previousCount - 1, start));
        } else {
            for (let index = start; index <= end; index += 1) {
                indexes.push(index);
            }
        }

        return Array.from(new Set(indexes.filter(function (value) {
            return value >= 0 && value < previousCount;
        })));
    }

    function resolveSourceIndexes(previousRound, currentGroup, currentIndex, currentCount) {
        const previousGroups = Array.isArray(previousRound?.groups) ? previousRound.groups : [];

        if (previousGroups.length === 0) {
            return [];
        }

        if (!previousRound._sourceMap) {
            previousRound._sourceMap = buildWinnerSourceMap(previousRound);
        }

        const inferred = new Set();
        const sourceMap = previousRound._sourceMap;

        (currentGroup?.matches || []).forEach(function (match) {
            (match?.teamRegistrationIds || []).forEach(function (registrationId) {
                const key = String(toNumber(registrationId));
                if (sourceMap.has(key)) {
                    inferred.add(sourceMap.get(key));
                }
            });
        });

        if (inferred.size > 0) {
            return Array.from(inferred).sort(function (left, right) { return left - right; });
        }

        return buildDistributedSourceIndexes(previousGroups.length, currentCount, currentIndex);
    }

    function buildSourceKeys(previousRound, sourceIndexes) {
        const groups = Array.isArray(previousRound?.groups) ? previousRound.groups : [];
        return sourceIndexes.map(function (index) {
            const source = groups[index];
            return source ? ("W-" + source.groupKey) : "";
        }).filter(Boolean);
    }

    function createVirtualGroup(roundStage, groupIndex, previousRound) {
        const groupName = "Bảng " + (groupIndex + 1);
        const groupKey = buildGroupDisplayKey(roundStage.roundKey, groupName, groupIndex);
        const sourceIndexes = previousRound
            ? buildDistributedSourceIndexes(previousRound.groups.length, Math.max(1, previousRound.groups.length), groupIndex)
            : [];

        return {
            isReal: false,
            groupName: groupName,
            groupKey: groupKey,
            matchCount: 0,
            completedCount: 0,
            matches: [],
            sourceIndexes: sourceIndexes,
            sourceKeys: previousRound ? buildSourceKeys(previousRound, sourceIndexes) : []
        };
    }

    function finalizeRoundMetrics(roundStage) {
        const groups = Array.isArray(roundStage?.groups) ? roundStage.groups : [];
        roundStage.groupCount = groups.length;
        roundStage.matchCount = groups.reduce(function (total, group) {
            return total + toNumber(group?.matchCount);
        }, 0);
        roundStage.completedCount = groups.reduce(function (total, group) {
            return total + toNumber(group?.completedCount);
        }, 0);
        return roundStage;
    }

    function buildStageRounds(payload) {
        const apiRounds = Array.isArray(payload?.rounds) ? payload.rounds : [];
        const rounds = apiRounds.map(function (round, index) {
            return buildActualRound(round, index);
        });

        if (rounds.length === 0) {
            return [];
        }

        for (let roundIndex = 0; roundIndex < rounds.length; roundIndex += 1) {
            const currentRound = rounds[roundIndex];
            const previousRound = roundIndex > 0 ? rounds[roundIndex - 1] : null;

            if (currentRound.groups.length === 0 && previousRound && previousRound.groups.length > 0) {
                currentRound.groups = previousRound.groups.map(function (_group, groupIndex) {
                    return createVirtualGroup(currentRound, groupIndex, previousRound);
                });
            }

            if (previousRound && previousRound.groups.length > 0 && currentRound.groups.length > 0) {
                currentRound.groups.forEach(function (group, groupIndex) {
                    const sourceIndexes = group.isReal
                        ? resolveSourceIndexes(previousRound, group, groupIndex, currentRound.groups.length)
                        : (group.sourceIndexes || []);
                    group.sourceIndexes = sourceIndexes;
                    group.sourceKeys = buildSourceKeys(previousRound, sourceIndexes);
                });
            }

            finalizeRoundMetrics(currentRound);
        }

        const lastRound = rounds[rounds.length - 1];

        if (
            lastRound &&
            lastRound.groups.length > 1 &&
            lastRound.groups.some(function (group) { return group?.isReal; })
        ) {
            const nextIndex = rounds.length;
            const nextRoundKey = buildSyntheticRoundKey(lastRound.roundKey, nextIndex);
            const nextRoundLabel = buildSyntheticRoundLabel(lastRound.roundLabel, nextIndex);
            const syntheticRound = {
                isSynthetic: true,
                roundKey: nextRoundKey,
                roundLabel: nextRoundLabel,
                sortOrder: toNumber(lastRound.sortOrder) + 1,
                groups: lastRound.groups.map(function (_group, groupIndex) {
                    return createVirtualGroup({ roundKey: nextRoundKey }, groupIndex, lastRound);
                })
            };
            finalizeRoundMetrics(syntheticRound);
            rounds.push(syntheticRound);
        }

        return rounds;
    }

    function getColumnWidth() {
        const viewport = window.innerWidth || document.documentElement.clientWidth || 1280;
        if (viewport >= 1600) {
            return 400;
        }
        if (viewport >= 1200) {
            return 360;
        }
        if (viewport >= 768) {
            return 320;
        }
        return 292;
    }

    function calculateGroupHeight(group) {
        const matchCount = Math.max(0, toNumber(group?.matchCount));
        const hasSources = Array.isArray(group?.sourceKeys) && group.sourceKeys.length > 0;
        const matchSectionHeight = matchCount > 0
            ? matchCount * 94 + Math.max(0, matchCount - 1) * 12
            : 80;

        return 112 + (hasSources ? 44 : 0) + matchSectionHeight;
    }

    function getBoardBottomPadding() {
        return window.innerWidth >= 1200 ? 180 : 140;
    }

    function getRoundContentHeight(round, groupGap) {
        const groups = Array.isArray(round?.groups) ? round.groups : [];

        if (!groups.length) {
            return 0;
        }

        return groups.reduce(function (total, group) {
            return total + toNumber(group?.height);
        }, 0) + Math.max(0, groups.length - 1) * groupGap;
    }

    function updateRoundPositions(rounds, metrics) {
        const maxContentHeight = rounds.reduce(function (maxHeight, round) {
            return Math.max(maxHeight, getRoundContentHeight(round, metrics.groupGap));
        }, 0);
        const contentHeight = Math.max(520, maxContentHeight);
        const boardHeight = Math.max(720, metrics.topOffset + contentHeight + metrics.bottomPadding);

        rounds.forEach(function (round, roundIndex) {
            const x = metrics.leftOffset + roundIndex * (metrics.columnWidth + metrics.columnGap);
            const roundContentHeight = getRoundContentHeight(round, metrics.groupGap);
            let y = metrics.topOffset + Math.max(0, (contentHeight - roundContentHeight) / 2);

            round.x = x;
            round.width = metrics.columnWidth;
            round.contentHeight = roundContentHeight;
            round.contentOffsetY = y;

            round.groups.forEach(function (group) {
                group.x = x;
                group.y = y;
                group.width = metrics.columnWidth;
                y += group.height + metrics.groupGap;
            });
        });

        return boardHeight;
    }

    function buildLinePaths(rounds, columnGap) {
        const lines = [];

        for (let roundIndex = 1; roundIndex < rounds.length; roundIndex += 1) {
            const previousRound = rounds[roundIndex - 1];
            const currentRound = rounds[roundIndex];

            currentRound.groups.forEach(function (targetGroup) {
                const targetX = targetGroup.x;
                const targetY = targetGroup.y + targetGroup.height / 2;
                const midX = targetX - columnGap / 2;

                (targetGroup.sourceIndexes || []).forEach(function (sourceIndex) {
                    const sourceGroup = previousRound.groups[sourceIndex];

                    if (!sourceGroup) {
                        return;
                    }

                    const sourceX = sourceGroup.x + sourceGroup.width;
                    const sourceY = sourceGroup.y + sourceGroup.height / 2;
                    lines.push("M " + sourceX + " " + sourceY + " H " + midX + " V " + targetY + " H " + targetX);
                });
            });
        }

        return lines;
    }

    function applyHorizontalPanSpace(layout, scroller) {
        if (!layout || !Array.isArray(layout.rounds) || layout.rounds.length === 0) {
            return;
        }

        const viewportWidth = Math.max(0, toNumber(scroller?.clientWidth));
        const desiredSidePadding = viewportWidth >= 1200 ? 220 : 160;
        const baseWidth = Math.max(0, toNumber(layout.width));
        const minimumWidth = viewportWidth > 0
            ? viewportWidth + Math.max(960, desiredSidePadding * 2)
            : baseWidth + 960;
        const extraWidth = Math.max(960, minimumWidth - baseWidth);
        const leftPadding = desiredSidePadding + Math.floor(extraWidth / 2);
        const rightPadding = desiredSidePadding + Math.ceil(extraWidth / 2);

        layout.rounds.forEach(function (round) {
            round.x += leftPadding;
            round.groups.forEach(function (group) {
                group.x += leftPadding;
            });
        });

        layout.width = baseWidth + leftPadding + rightPadding;
        layout.initialScrollLeft = Math.max(0, leftPadding - 48);
        layout.lines = buildLinePaths(layout.rounds, layout.columnGap);
    }

    function buildLayout(payload) {
        const rounds = buildStageRounds(payload);
        const columnWidth = getColumnWidth();
        const columnGap = window.innerWidth >= 1200 ? 64 : 40;
        const groupGap = 20;
        const leftOffset = 24;
        const topOffset = 104;
        const headerTop = 20;
        const bottomPadding = getBoardBottomPadding();

        rounds.forEach(function (round) {
            round.groups.forEach(function (group) {
                group.height = calculateGroupHeight(group);
            });
        });

        const boardHeight = updateRoundPositions(rounds, {
            columnWidth: columnWidth,
            columnGap: columnGap,
            groupGap: groupGap,
            leftOffset: leftOffset,
            topOffset: topOffset,
            bottomPadding: bottomPadding
        });
        const lines = buildLinePaths(rounds, columnGap);

        const boardWidth = leftOffset * 2 + rounds.length * columnWidth + Math.max(0, rounds.length - 1) * columnGap + 40;
        const summary = {
            rounds: rounds.length,
            groups: rounds.reduce(function (total, round) { return total + toNumber(round.groupCount); }, 0),
            matches: rounds.reduce(function (total, round) { return total + toNumber(round.matchCount); }, 0),
            completed: rounds.reduce(function (total, round) { return total + toNumber(round.completedCount); }, 0)
        };

        return {
            rounds: rounds,
            lines: lines,
            width: boardWidth,
            height: boardHeight,
            headerTop: headerTop,
            columnWidth: columnWidth,
            columnGap: columnGap,
            groupGap: groupGap,
            leftOffset: leftOffset,
            topOffset: topOffset,
            bottomPadding: bottomPadding,
            summary: summary
        };
    }

    function renderMatch(match) {
        const classes = [
            "admin-bracket-match",
            match.isCompleted ? "is-completed" : "",
            match.hasVideo ? "has-video" : ""
        ].filter(Boolean).join(" ");

        return [
            '<article class="' + escapeHtml(classes) + '">',
            '<div class="admin-bracket-match__top">',
            '<strong>' + escapeHtml(match.title) + "</strong>",
            "<span>" + escapeHtml(match.metaText || "Chưa có giờ / sân") + "</span>",
            "</div>",
            '<div class="admin-bracket-match__team">',
            '<div class="admin-bracket-match__team-main">',
            match.teamAIdentity
                ? '<small class="admin-bracket-match__team-meta">' + escapeHtml(match.teamAIdentity) + "</small>"
                : "",
            '<span class="' + (match.isWinnerA ? "is-winner" : "") + '">' + escapeHtml(match.teamA) + "</span>",
            "</div>",
            '<b class="' + (match.isWinnerA ? "is-winner" : "") + '">' + escapeHtml(match.scoreA) + "</b>",
            "</div>",
            '<div class="admin-bracket-match__team">',
            '<div class="admin-bracket-match__team-main">',
            match.teamBIdentity
                ? '<small class="admin-bracket-match__team-meta">' + escapeHtml(match.teamBIdentity) + "</small>"
                : "",
            '<span class="' + (match.isWinnerB ? "is-winner" : "") + '">' + escapeHtml(match.teamB) + "</span>",
            "</div>",
            '<b class="' + (match.isWinnerB ? "is-winner" : "") + '">' + escapeHtml(match.scoreB) + "</b>",
            "</div>",
            "</article>"
        ].join("");
    }

    function renderGroup(group, roundIndex, groupIndex) {
        const groupClasses = [
            "admin-bracket-group",
            group.isReal ? "is-real" : "is-virtual"
        ].join(" ");

        return [
            '<article class="' + escapeHtml(groupClasses) + '" data-round-index="' + escapeHtml(roundIndex) + '" data-group-index="' + escapeHtml(groupIndex) + '" style="left:' + escapeHtml(group.x) + "px;top:" + escapeHtml(group.y) + "px;width:" + escapeHtml(group.width) + "px;height:" + escapeHtml(group.height) + 'px">',
            '<div class="admin-bracket-group__head">',
            '<div class="admin-bracket-group__title">',
            '<span class="admin-bracket-group__key">' + escapeHtml(group.groupKey) + "</span>",
            "<strong>" + escapeHtml(group.groupName) + "</strong>",
            "</div>",
            '<span class="admin-bracket-group__state-dot ' + (group.isReal ? "is-real" : "is-virtual") + '" aria-hidden="true"></span>',
            "</div>",
            '<div class="admin-bracket-group__meta">',
            "<span>" + escapeHtml(String(group.matchCount)) + " trận</span>",
            "<span>" + escapeHtml(String(group.completedCount)) + "/" + escapeHtml(String(group.matchCount)) + " xong</span>",
            "</div>",
            group.sourceKeys.length > 0
                ? '<div class="admin-bracket-group__sources">' + group.sourceKeys.map(function (item) {
                    return "<span>" + escapeHtml(item) + "</span>";
                }).join("") + "</div>"
                : "",
            '<div class="admin-bracket-group__body">',
            group.matches.length > 0
                ? group.matches.map(renderMatch).join("")
                : '<div class="admin-bracket-group__empty">' + escapeHtml(group.isReal ? "Bảng này chưa có trận đấu." : "Admin chưa tạo bảng hoặc trận cho nhánh này.") + "</div>",
            "</div>",
            "</article>"
        ].join("");
    }

    function renderRoundTitle(round, headerTop) {
        const classes = [
            "admin-bracket-round-title",
            round.isSynthetic ? "is-virtual" : "is-real"
        ].join(" ");

        return [
            '<div class="' + escapeHtml(classes) + '" style="left:' + escapeHtml(round.x) + "px;top:" + escapeHtml(headerTop) + "px;width:" + escapeHtml(round.width) + 'px">',
            '<div class="admin-bracket-round-title__text">',
            "<span>" + escapeHtml(round.roundKey) + "</span>",
            "<strong>" + escapeHtml(round.roundLabel) + "</strong>",
            "</div>",
            '<div class="admin-bracket-round-title__meta">',
            "<span>" + escapeHtml(String(round.groupCount)) + " bảng</span>",
            "<span>" + escapeHtml(String(round.matchCount)) + " trận</span>",
            "</div>",
            "</div>"
        ].join("");
    }

    function buildBoardHtml(layout) {
        return [
            '<svg class="admin-bracket-lines" data-bracket-lines="true" viewBox="0 0 ' + escapeHtml(layout.width) + " " + escapeHtml(layout.height) + '" aria-hidden="true">',
            layout.lines.map(function (path) {
                return '<path d="' + escapeHtml(path) + '"></path>';
            }).join(""),
            "</svg>",
            layout.rounds.map(function (round) {
                return renderRoundTitle(round, layout.headerTop);
            }).join(""),
            layout.rounds.map(function (round, roundIndex) {
                return round.groups.map(function (group, groupIndex) {
                    return renderGroup(group, roundIndex, groupIndex);
                }).join("");
            }).join("")
        ].join("");
    }

    function findGroupElement(board, roundIndex, groupIndex) {
        return qs('.admin-bracket-group[data-round-index="' + roundIndex + '"][data-group-index="' + groupIndex + '"]', board);
    }

    function updateBoardLines(board, layout) {
        const svg = qs("[data-bracket-lines]", board);
        const lines = buildLinePaths(layout.rounds, layout.columnGap);

        layout.lines = lines;

        if (!svg) {
            return;
        }

        svg.setAttribute("viewBox", "0 0 " + layout.width + " " + layout.height);
        svg.innerHTML = lines.map(function (path) {
            return '<path d="' + escapeHtml(path) + '"></path>';
        }).join("");
    }

    function syncMeasuredLayout(board, layout) {
        if (!board || !layout || !Array.isArray(layout.rounds)) {
            return;
        }

        layout.rounds.forEach(function (round, roundIndex) {
            round.groups.forEach(function (group, groupIndex) {
                const element = findGroupElement(board, roundIndex, groupIndex);

                if (!element) {
                    return;
                }

                element.style.height = "auto";
                group.height = Math.max(calculateGroupHeight(group), Math.ceil(Math.max(element.scrollHeight, element.offsetHeight)));
            });
        });

        layout.height = updateRoundPositions(layout.rounds, layout);
        board.style.height = layout.height + "px";

        layout.rounds.forEach(function (round, roundIndex) {
            round.groups.forEach(function (group, groupIndex) {
                const element = findGroupElement(board, roundIndex, groupIndex);

                if (!element) {
                    return;
                }

                element.style.left = group.x + "px";
                element.style.top = group.y + "px";
                element.style.width = group.width + "px";
                element.style.height = group.height + "px";
            });
        });

        updateBoardLines(board, layout);
    }

    function initDragScroller(scroller) {
        if (!scroller || scroller.dataset.dragReady === "true") {
            return;
        }

        scroller.dataset.dragReady = "true";
        let dragging = false;
        let startX = 0;
        let startY = 0;
        let startLeft = 0;
        let startTop = 0;

        function startPan(clientX, clientY) {
            dragging = true;
            startX = clientX;
            startY = clientY;
            startLeft = scroller.scrollLeft;
            startTop = scroller.scrollTop;
            scroller.classList.add("is-dragging");
        }

        function movePan(clientX, clientY) {
            if (!dragging) {
                return;
            }

            scroller.scrollLeft = startLeft - (clientX - startX);
            scroller.scrollTop = startTop - (clientY - startY);
        }

        function endPan() {
            if (!dragging) {
                return;
            }

            dragging = false;
            scroller.classList.remove("is-dragging");
        }

        scroller.addEventListener("dragstart", function (event) {
            event.preventDefault();
        });

        scroller.addEventListener("pointerdown", function (event) {
            if (event.pointerType !== "mouse") {
                return;
            }

            if (event.button !== undefined && event.button !== 0) {
                return;
            }

            startPan(event.clientX, event.clientY);
            try {
                scroller.setPointerCapture?.(event.pointerId);
            } catch (_error) {
                // Some embedded browsers do not allow pointer capture on scroll containers.
            }
        });

        scroller.addEventListener("pointermove", function (event) {
            if (!dragging) {
                return;
            }

            event.preventDefault();
            movePan(event.clientX, event.clientY);
        });

        function stopDragging(event) {
            if (!dragging) {
                return;
            }

            endPan();
            try {
                scroller.releasePointerCapture?.(event.pointerId);
            } catch (_error) {
                // Matching the setPointerCapture fallback above.
            }
        }

        scroller.addEventListener("pointerup", stopDragging);
        scroller.addEventListener("pointercancel", stopDragging);
        scroller.addEventListener("pointerleave", stopDragging);

        scroller.addEventListener("touchstart", function (event) {
            if (!event.touches || event.touches.length !== 1) {
                return;
            }

            const touch = event.touches[0];
            startPan(touch.clientX, touch.clientY);
        }, { passive: true });

        scroller.addEventListener("touchmove", function (event) {
            if (!dragging || !event.touches || event.touches.length !== 1) {
                return;
            }

            const touch = event.touches[0];
            event.preventDefault();
            movePan(touch.clientX, touch.clientY);
        }, { passive: false });

        scroller.addEventListener("touchend", endPan, { passive: true });
        scroller.addEventListener("touchcancel", endPan, { passive: true });

        scroller.addEventListener("wheel", function (event) {
            const canScrollHorizontally = scroller.scrollWidth > scroller.clientWidth;

            if (!canScrollHorizontally) {
                return;
            }

            if (event.shiftKey || Math.abs(event.deltaX) > Math.abs(event.deltaY)) {
                scroller.scrollLeft += event.deltaX || event.deltaY;
                event.preventDefault();
            }
        }, { passive: false });
        window.addEventListener("blur", function () {
            endPan();
        });
    }

    const page = qs("#adminTournamentBracketPage");
    if (!page) {
        return;
    }

    const tournamentId = toNumber(page.dataset.tournamentId);
    const board = qs("[data-bracket-board]", page);
    const loading = qs("[data-bracket-loading]", page);
    const errorBox = qs("[data-bracket-error]", page);
    const scroller = qs("[data-bracket-scroll]", page);
    const summaryRefs = {
        rounds: qs("[data-stat-rounds]", page),
        groups: qs("[data-stat-groups]", page),
        matches: qs("[data-stat-matches]", page),
        completed: qs("[data-stat-completed]", page)
    };

    let latestPayload = null;

    function setError(message) {
        if (!errorBox) {
            return;
        }

        if (trimToEmpty(message)) {
            errorBox.textContent = message;
            errorBox.classList.remove("d-none");
        } else {
            errorBox.textContent = "";
            errorBox.classList.add("d-none");
        }
    }

    function setLoading(isLoading) {
        if (!loading) {
            return;
        }

        loading.classList.toggle("d-none", !isLoading);
    }

    function render(payload) {
        if (!board) {
            return;
        }

        const layout = buildLayout(payload);
        applyHorizontalPanSpace(layout, scroller);
        board.classList.remove("is-measuring");

        if (!layout.rounds.length) {
            board.style.width = "100%";
            board.style.height = "640px";
            board.innerHTML = '<div class="admin-tournament-bracket-loading">Giải đấu này chưa có dữ liệu để dựng sơ đồ.</div>';
            return;
        }

        board.style.width = layout.width + "px";
        board.style.height = layout.height + "px";
        board.style.minWidth = layout.width + "px";
        board.classList.add("is-measuring");
        board.innerHTML = buildBoardHtml(layout);
        window.requestAnimationFrame(function () {
            syncMeasuredLayout(board, layout);
            board.classList.remove("is-measuring");

            if (layout.initialScrollLeft > 0) {
                scroller.scrollLeft = layout.initialScrollLeft;
            }
        });

        if (summaryRefs.rounds) summaryRefs.rounds.textContent = String(layout.summary.rounds);
        if (summaryRefs.groups) summaryRefs.groups.textContent = String(layout.summary.groups);
        if (summaryRefs.matches) summaryRefs.matches.textContent = String(layout.summary.matches);
        if (summaryRefs.completed) summaryRefs.completed.textContent = String(layout.summary.completed);
    }

    async function loadBracket() {
        if (!tournamentId) {
            setError("Thiếu tournamentId để tải sơ đồ.");
            return;
        }

        setError("");
        setLoading(true);

        try {
            latestPayload = await fetchJson("/api/tournaments/" + tournamentId + "/rounds-with-matches");
            render(latestPayload);
        } catch (error) {
            setError(error?.message || "Tải sơ đồ thất bại.");
            if (board) {
                board.style.width = "100%";
                board.style.height = "640px";
                board.innerHTML = '<div class="admin-tournament-bracket-loading">Không tải được sơ đồ giải đấu.</div>';
            }
        } finally {
            setLoading(false);
        }
    }

    const rerender = debounce(function () {
        if (latestPayload) {
            render(latestPayload);
        }
    }, 120);

    const reloadButton = qs("[data-reload-bracket]", page);
    if (reloadButton) {
        reloadButton.addEventListener("click", loadBracket);
    }

    const publicButton = qs("[data-open-public-bracket]", page);
    if (publicButton) {
        publicButton.addEventListener("click", function () {
            window.open("/PickleballWeb/Tournament/" + tournamentId + "/Bracket", "_blank", "noopener");
        });
    }

        initDragScroller(scroller);
    window.addEventListener("resize", rerender);
    loadBracket();
})();
