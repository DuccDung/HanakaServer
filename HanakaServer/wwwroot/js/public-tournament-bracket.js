(function () {
    const MIN_SCALE = 0.45;
    const MAX_SCALE = 1.75;
    const DEFAULT_SCALE = (window.innerWidth || 1024) < 768 ? 0.72 : 0.86;

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

    function clamp(value, min, max) {
        return Math.min(max, Math.max(min, value));
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
        return parsed
            ? parsed.prefix + String(parsed.start + 1).padStart(parsed.width, "0")
            : "R" + (roundIndex + 1);
    }

    function buildSyntheticRoundLabel(previousRoundLabel, roundIndex) {
        const parsed = parseTrailingNumber(previousRoundLabel);
        return parsed
            ? parsed.prefix + String(parsed.start + 1).padStart(parsed.width, "0")
            : "Vòng " + (roundIndex + 1);
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

        if (/^Bảng\s+\d+$/i.test(compact) || /^Bang\s+\d+$/i.test(compact)) {
            return "B" + compact.replace(/^Bảng\s+/i, "").replace(/^Bang\s+/i, "");
        }

        if (/^\d+$/.test(compact)) {
            return "B" + compact;
        }

        if (/^[A-Za-z]+$/.test(compact)) {
            return compact.toUpperCase();
        }

        return compact
            .toUpperCase()
            .replace(/[^A-Z0-9]+/g, "-")
            .replace(/^-+|-+$/g, "") || ("B" + (groupIndex + 1));
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

    function normalizeSourceType(value) {
        const normalized = trimToEmpty(value).toUpperCase();
        if (normalized === "WINNER_MATCH" || normalized === "LOSER_MATCH" || normalized === "GROUP_RANK" || normalized === "BYE") {
            return normalized;
        }

        return "REGISTRATION";
    }

    function buildSlotSource(match, slotNumber) {
        const prefix = slotNumber === 1 ? "team1" : "team2";
        const sourceType = normalizeSourceType(match?.[prefix + "SourceType"]);
        const sourceMatchId = toNumber(match?.[prefix + "SourceMatchId"]);
        const sourceGroupId = toNumber(match?.[prefix + "SourceGroupId"]);
        const sourceRank = toNumber(match?.[prefix + "SourceRank"]);
        const sourceText = trimToEmpty(match?.[prefix + "SourceText"]);
        let badge = "";
        let label = "";
        let tone = "registration";

        if (sourceType === "WINNER_MATCH") {
            badge = sourceMatchId > 0 ? "W#" + sourceMatchId : "WIN";
            label = sourceMatchId > 0 ? "Thắng trận #" + sourceMatchId : "Thắng trận";
            tone = "winner";
        } else if (sourceType === "LOSER_MATCH") {
            badge = sourceMatchId > 0 ? "L#" + sourceMatchId : "LOS";
            label = sourceMatchId > 0 ? "Thua trận #" + sourceMatchId : "Thua trận";
            tone = "loser";
        } else if (sourceType === "GROUP_RANK") {
            badge = sourceRank > 0 ? "R" + sourceRank : "R?";
            label = sourceRank > 0 ? "Hạng " + sourceRank + " bảng" : "Hạng bảng";
            tone = "group-rank";
        } else if (sourceType === "BYE") {
            badge = "BYE";
            label = "Miễn đấu";
            tone = "bye";
        }

        return {
            type: sourceType,
            matchId: sourceMatchId,
            groupId: sourceGroupId,
            rank: sourceRank,
            text: sourceText,
            badge: badge,
            label: label,
            tone: tone,
            isLinked: sourceType !== "REGISTRATION"
        };
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

    function buildMatchCard(match, matchIndex, groupKey, groupName) {
        const winnerId = match?.winnerRegistrationId;
        const isWinnerA = !!winnerId && winnerId === match?.team1RegistrationId;
        const isWinnerB = !!winnerId && winnerId === match?.team2RegistrationId;
        const teamASource = buildSlotSource(match, 1);
        const teamBSource = buildSlotSource(match, 2);
        const teamAResolved = !!match?.team1Resolved || toNumber(match?.team1RegistrationId) > 0;
        const teamBResolved = !!match?.team2Resolved || toNumber(match?.team2RegistrationId) > 0;

        return {
            matchId: toNumber(match?.matchId),
            isCompleted: !!match?.isCompleted,
            hasVideo: !!trimToEmpty(match?.videoUrl),
            title: "#" + (trimToEmpty(match?.matchId) || (groupKey + "-" + (matchIndex + 1))),
            groupKey: groupKey,
            groupName: groupName,
            metaText: buildMatchMeta(match),
            teamA: buildTeamName(match?.team1, groupKey + "-#1"),
            teamB: buildTeamName(match?.team2, groupKey + "-#2"),
            teamAIdentity: buildTeamIdentity(match?.team1, match?.team1RegistrationId),
            teamBIdentity: buildTeamIdentity(match?.team2, match?.team2RegistrationId),
            teamASource: teamASource,
            teamBSource: teamBSource,
            teamAResolved: teamAResolved,
            teamBResolved: teamBResolved,
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
        const completedCount = matches.filter(function (match) { return !!match?.isCompleted; }).length;
        const matchCards = matches.map(function (match, matchIndex) {
            return buildMatchCard(match, matchIndex, groupKey, groupName);
        });

        return {
            isReal: true,
            groupId: toNumber(group?.groupId) || toNumber(group?.tournamentRoundGroupId),
            groupName: groupName,
            groupKey: groupKey,
            matchCount: matchCards.length,
            completedCount: completedCount,
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

    function buildMatchSourceMap(round) {
        const sourceMap = new Map();

        (round?.groups || []).forEach(function (group, groupIndex) {
            (group?.matches || []).forEach(function (match) {
                if (toNumber(match?.matchId) > 0) {
                    sourceMap.set(String(match.matchId), groupIndex);
                }
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
        if (!previousRound._matchSourceMap) {
            previousRound._matchSourceMap = buildMatchSourceMap(previousRound);
        }

        const inferred = new Set();
        const sourceMap = previousRound._sourceMap;
        const matchSourceMap = previousRound._matchSourceMap;

        (currentGroup?.matches || []).forEach(function (match) {
            [match?.teamASource, match?.teamBSource].forEach(function (slotSource) {
                if (!slotSource || !slotSource.matchId) {
                    return;
                }

                const key = String(toNumber(slotSource.matchId));
                if (matchSourceMap.has(key)) {
                    inferred.add(matchSourceMap.get(key));
                }
            });

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
            groupId: 0,
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

    function collectNextRoundMatchTargets(currentRound) {
        const targetMap = new Map();
        let targetOrder = 0;

        (currentRound?.groups || []).forEach(function (group, groupIndex) {
            (group?.matches || []).forEach(function (match, matchIndex) {
                const targetKey = [groupIndex, matchIndex, toNumber(match?.matchId)].join(":");

                [match?.teamASource, match?.teamBSource].forEach(function (slotSource, slotIndex) {
                    if (!slotSource || !slotSource.matchId) {
                        return;
                    }

                    const sourceType = normalizeSourceType(slotSource.type);
                    if (sourceType !== "WINNER_MATCH" && sourceType !== "LOSER_MATCH") {
                        return;
                    }

                    const sourceMatchId = toNumber(slotSource.matchId);
                    if (sourceMatchId <= 0) {
                        return;
                    }

                    const sourceKey = String(sourceMatchId);
                    const candidate = {
                        targetKey: targetKey,
                        targetOrder: targetOrder,
                        slotOrder: slotIndex
                    };
                    const existing = targetMap.get(sourceKey);

                    if (!existing || candidate.targetOrder < existing.targetOrder || (candidate.targetOrder === existing.targetOrder && candidate.slotOrder < existing.slotOrder)) {
                        targetMap.set(sourceKey, candidate);
                    }
                });

                targetOrder += 1;
            });
        });

        return targetMap;
    }

    function orderPreviousRoundMatchesForTargets(previousRound, currentRound) {
        const targetMap = collectNextRoundMatchTargets(currentRound);
        if (!targetMap.size) {
            return;
        }

        (previousRound?.groups || []).forEach(function (group) {
            if (!Array.isArray(group?.matches) || group.matches.length <= 1) {
                return;
            }

            group.matches = group.matches
                .map(function (match, originalIndex) {
                    const target = targetMap.get(String(toNumber(match?.matchId)));
                    return {
                        match: match,
                        originalIndex: originalIndex,
                        targetOrder: target ? target.targetOrder : Number.MAX_SAFE_INTEGER,
                        slotOrder: target ? target.slotOrder : Number.MAX_SAFE_INTEGER
                    };
                })
                .sort(function (left, right) {
                    if (left.targetOrder !== right.targetOrder) {
                        return left.targetOrder - right.targetOrder;
                    }
                    if (left.slotOrder !== right.slotOrder) {
                        return left.slotOrder - right.slotOrder;
                    }
                    return left.originalIndex - right.originalIndex;
                })
                .map(function (entry) {
                    return entry.match;
                });
        });
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

                orderPreviousRoundMatchesForTargets(previousRound, currentRound);
            }

            finalizeRoundMetrics(currentRound);
        }

        const lastRound = rounds[rounds.length - 1];
        if (lastRound && lastRound.groups.length > 1 && lastRound.groups.some(function (group) { return group?.isReal; })) {
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
            return 560;
        }
        if (viewport >= 1200) {
            return 500;
        }
        if (viewport >= 768) {
            return 430;
        }
        return 380;
    }

    function calculateGroupHeight(group) {
        const matchCount = Math.max(0, toNumber(group?.matchCount));
        const matchSectionHeight = matchCount > 0
            ? matchCount * 142 + Math.max(0, matchCount - 1) * 14
            : 96;

        return matchSectionHeight;
    }

    function getSourceGroupSpanHeight(previousRound, sourceIndexes, groupGap) {
        const groups = Array.isArray(previousRound?.groups) ? previousRound.groups : [];
        const indexes = Array.from(new Set((sourceIndexes || [])
            .map(function (index) { return toNumber(index); })
            .filter(function (index) { return index >= 0 && index < groups.length; })))
            .sort(function (left, right) { return left - right; });

        if (indexes.length === 0) {
            return 0;
        }

        const firstIndex = indexes[0];
        const lastIndex = indexes[indexes.length - 1];
        let height = 0;

        for (let index = firstIndex; index <= lastIndex; index += 1) {
            height += toNumber(groups[index]?.height);
        }

        height += Math.max(0, lastIndex - firstIndex) * groupGap;
        return height;
    }

    function stretchDependentGroupHeights(rounds, groupGap) {
        for (let roundIndex = 1; roundIndex < rounds.length; roundIndex += 1) {
            const previousRound = rounds[roundIndex - 1];
            const currentRound = rounds[roundIndex];

            currentRound.groups.forEach(function (group) {
                const sourceSpanHeight = getSourceGroupSpanHeight(previousRound, group.sourceIndexes, groupGap);
                if (sourceSpanHeight > 0) {
                    group.height = Math.max(group.height, sourceSpanHeight);
                }
            });
        }
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
            let y = metrics.topOffset;

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
                    lines.push({
                        d: "M " + sourceX + " " + sourceY + " H " + midX + " V " + targetY + " H " + targetX,
                        className: "is-fallback"
                    });
                });
            });
        }

        return lines;
    }

    function buildLayout(payload) {
        const rounds = buildStageRounds(payload);
        const columnWidth = getColumnWidth();
        const columnGap = window.innerWidth >= 1200 ? 44 : 28;
        const groupGap = 14;
        const leftOffset = 44;
        const topOffset = 130;
        const headerTop = 32;
        const bottomPadding = window.innerWidth >= 1200 ? 220 : 160;

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
        const lines = [];
        const boardWidth = leftOffset * 2 + rounds.length * columnWidth + Math.max(0, rounds.length - 1) * columnGap + 80;

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
            initialScrollLeft: 0
        };
    }

    function applyHorizontalPanSpace(layout, viewport) {
        if (!layout || !Array.isArray(layout.rounds) || layout.rounds.length === 0) {
            return;
        }

        const viewportWidth = Math.max(0, toNumber(viewport?.clientWidth));
        const desiredSidePadding = viewportWidth >= 1200 ? 56 : 32;
        const baseWidth = Math.max(0, toNumber(layout.width));
        const minimumWidth = viewportWidth > 0
            ? Math.max(baseWidth, viewportWidth)
            : baseWidth;
        const extraWidth = Math.max(0, minimumWidth - baseWidth);
        const leftPadding = desiredSidePadding;
        const rightPadding = desiredSidePadding + extraWidth;

        layout.rounds.forEach(function (round) {
            round.x += leftPadding;
            round.groups.forEach(function (group) {
                group.x += leftPadding;
            });
        });

        layout.leftOffset += leftPadding;
        layout.width = baseWidth + leftPadding + rightPadding;
        layout.initialScrollLeft = 0;
        layout.lines = [];
    }

    function renderMatchTeam(match, slotNumber) {
        const isTeamA = slotNumber === 1;
        const source = isTeamA ? match.teamASource : match.teamBSource;
        const identity = isTeamA ? match.teamAIdentity : match.teamBIdentity;
        const teamName = isTeamA ? match.teamA : match.teamB;
        const score = isTeamA ? match.scoreA : match.scoreB;
        const isWinner = isTeamA ? match.isWinnerA : match.isWinnerB;
        const isResolved = isTeamA ? match.teamAResolved : match.teamBResolved;
        const sourceClasses = [
            "pb-match__source",
            source?.isLinked ? "is-" + source.tone : "",
            isResolved ? "is-resolved" : "is-pending"
        ].filter(Boolean).join(" ");
        const sourceTitle = trimToEmpty(source?.text) || trimToEmpty(source?.label);
        const sourceHtml = source?.isLinked
            ? '<small class="' + escapeHtml(sourceClasses) + '" title="' + escapeHtml(sourceTitle) + '"><b>' + escapeHtml(source.badge) + "</b>" + (source?.label ? '<span>' + escapeHtml(source.label) + "</span>" : "") + "</small>"
            : "";
        const identityHtml = identity
            ? '<small class="pb-match__identity">' + escapeHtml(identity) + "</small>"
            : "";
        const tagsHtml = sourceHtml || identityHtml
            ? '<div class="pb-match__team-tags">' + sourceHtml + identityHtml + "</div>"
            : "";

        return [
            '<div class="pb-match__team" data-slot="' + slotNumber + '" data-source-type="' + escapeHtml(source?.type || "REGISTRATION") + '" data-source-match-id="' + escapeHtml(source?.matchId || "") + '" data-source-group-id="' + escapeHtml(source?.groupId || "") + '" data-source-rank="' + escapeHtml(source?.rank || "") + '">',
            '<div class="pb-match__team-main">',
            tagsHtml,
            '<span class="pb-match__name ' + (isWinner ? "is-winner" : "") + '">' + escapeHtml(teamName) + "</span>",
            "</div>",
            '<b class="pb-match__score ' + (isWinner ? "is-winner" : "") + '">' + escapeHtml(score) + "</b>",
            "</div>"
        ].join("");
    }

    function renderMatch(match) {
        const classes = [
            "pb-match",
            match.isCompleted ? "is-completed" : "",
            match.hasVideo ? "has-video" : ""
        ].filter(Boolean).join(" ");

        return [
            '<article class="' + escapeHtml(classes) + '" data-match-id="' + escapeHtml(match.matchId || "") + '">',
            '<div class="pb-match__top">',
            '<div class="pb-match__title">',
            '<span class="pb-match__badge">' + escapeHtml(match.groupKey || "") + "</span>",
            '<span class="pb-match__group">' + escapeHtml(match.groupName || "") + "</span>",
            '<strong class="pb-match__id">' + escapeHtml(match.title) + "</strong>",
            "</div>",
            '<span class="pb-match__meta">' + escapeHtml(match.metaText || "") + "</span>",
            "</div>",
            renderMatchTeam(match, 1),
            renderMatchTeam(match, 2),
            "</article>"
        ].join("");
    }

    function renderGroup(group, roundIndex, groupIndex) {
        const classes = ["pb-group", group.isReal ? "is-real" : "is-virtual"].join(" ");
        return [
            '<article class="' + escapeHtml(classes) + '" data-round-index="' + escapeHtml(roundIndex) + '" data-group-index="' + escapeHtml(groupIndex) + '" data-group-id="' + escapeHtml(group.groupId || "") + '" style="left:' + escapeHtml(group.x) + "px;top:" + escapeHtml(group.y) + "px;width:" + escapeHtml(group.width) + "px;height:" + escapeHtml(group.height) + 'px">',
            '<div class="pb-group__body">',
            group.matches.length > 0
                ? group.matches.map(renderMatch).join("")
                : '<div class="pb-group__empty">' + escapeHtml(group.isReal ? "Bảng này chưa có trận đấu." : "Nhánh này chưa có bảng hoặc trận đấu.") + "</div>",
            "</div>",
            "</article>"
        ].join("");
    }

    function renderRoundTitle(round, headerTop, roundIndex) {
        const classes = ["pb-round-title", round.isSynthetic ? "is-virtual" : "is-real"].join(" ");
        return [
            '<div class="' + escapeHtml(classes) + '" data-round-title-index="' + escapeHtml(roundIndex) + '" style="left:' + escapeHtml(round.x) + "px;top:" + escapeHtml(headerTop) + "px;width:" + escapeHtml(round.width) + 'px">',
            '<div class="pb-round-title__main">',
            '<span class="pb-round-title__key">' + escapeHtml(round.roundKey) + "</span>",
            '<strong class="pb-round-title__label">' + escapeHtml(round.roundLabel) + "</strong>",
            "</div>",
            '<div class="pb-round-title__meta">',
            "<span>" + escapeHtml(String(round.groupCount)) + " bảng</span>",
            "<span>" + escapeHtml(String(round.matchCount)) + " trận</span>",
            '<i class="pb-round-title__dot" aria-hidden="true"></i>',
            "</div>",
            "</div>"
        ].join("");
    }

    function renderLinePath(line) {
        const path = typeof line === "string" ? line : line?.d;
        if (!path) {
            return "";
        }

        const classes = ["pb-line", typeof line === "string" ? "" : line?.className]
            .filter(Boolean)
            .join(" ");

        return '<path class="' + escapeHtml(classes) + '" d="' + escapeHtml(path) + '"></path>';
    }

    function buildBoardHtml(layout) {
        return [
            layout.rounds.map(function (round, roundIndex) {
                return renderRoundTitle(round, layout.headerTop, roundIndex);
            }).join(""),
            layout.rounds.map(function (round, roundIndex) {
                return round.groups.map(function (group, groupIndex) {
                    return renderGroup(group, roundIndex, groupIndex);
                }).join("");
            }).join("")
        ].join("");
    }

    function findGroupElement(board, roundIndex, groupIndex) {
        return qs('.pb-group[data-round-index="' + roundIndex + '"][data-group-index="' + groupIndex + '"]', board);
    }

    function getBoardViewportScale(board, boardRect) {
        const rect = boardRect || board?.getBoundingClientRect?.();
        const styleWidth = toNumber((board?.style?.width || "").replace("px", ""));
        const styleHeight = toNumber((board?.style?.height || "").replace("px", ""));
        const width = Math.max(1, styleWidth || toNumber(board?.offsetWidth));
        const height = Math.max(1, styleHeight || toNumber(board?.offsetHeight));

        return {
            x: rect && rect.width > 0 ? rect.width / width : 1,
            y: rect && rect.height > 0 ? rect.height / height : 1
        };
    }

    function getElementMidpoint(element, boardRect, edge, scale) {
        const rect = element.getBoundingClientRect();
        const x = edge === "right" ? rect.right : edge === "left" ? rect.left : rect.left + rect.width / 2;
        const scaleX = scale?.x || 1;
        const scaleY = scale?.y || 1;

        return {
            x: (x - boardRect.left) / scaleX,
            y: (rect.top + rect.height / 2 - boardRect.top) / scaleY
        };
    }

    function buildConnectorPath(source, target) {
        const sourceX = Math.round(source.x);
        const sourceY = Math.round(source.y);
        const targetX = Math.round(target.x);
        const targetY = Math.round(target.y);
        const distance = Math.max(48, Math.abs(targetX - sourceX));
        const midX = sourceX <= targetX
            ? sourceX + Math.round(distance / 2)
            : sourceX + 52;

        return "M " + sourceX + " " + sourceY + " H " + midX + " V " + targetY + " H " + targetX;
    }

    function buildSeparatedConnectorPath(source, target, index, count) {
        const sourceX = Math.round(source.x);
        const sourceY = Math.round(source.y);
        const targetX = Math.round(target.x);
        const targetY = Math.round(target.y);
        const distance = Math.max(48, Math.abs(targetX - sourceX));
        const laneOffset = (index - (count - 1) / 2) * 24;
        let midX = sourceX <= targetX
            ? sourceX + Math.round(distance * 0.5) + laneOffset
            : sourceX + 52 + laneOffset;

        if (sourceX <= targetX) {
            const minMidX = sourceX + 26;
            const maxMidX = targetX - 26;
            midX = maxMidX > minMidX
                ? clamp(midX, minMidX, maxMidX)
                : sourceX + Math.round(distance / 2);
        }

        return "M " + sourceX + " " + sourceY + " H " + midX + " V " + targetY + " H " + targetX;
    }

    function getLineClassForSource(sourceType) {
        sourceType = normalizeSourceType(sourceType);
        if (sourceType === "WINNER_MATCH") {
            return "is-winner-source";
        }
        if (sourceType === "LOSER_MATCH") {
            return "is-loser-source";
        }
        if (sourceType === "GROUP_RANK") {
            return "is-group-rank-source";
        }
        return "is-fallback";
    }

    function collectDependencyTargets(board) {
        const matchMap = new Map();
        const groupMap = new Map();
        const targetMap = new Map();

        qsa("[data-match-id]", board).forEach(function (element) {
            const id = toNumber(element.dataset.matchId);
            if (id > 0) {
                matchMap.set(String(id), element);
            }
        });

        qsa("[data-group-id]", board).forEach(function (element) {
            const id = toNumber(element.dataset.groupId);
            if (id > 0) {
                groupMap.set(String(id), element);
            }
        });

        qsa(".pb-match__team[data-source-type]", board).forEach(function (targetSlot) {
            const sourceType = normalizeSourceType(targetSlot.dataset.sourceType);
            let sourceElement = null;

            if (sourceType === "WINNER_MATCH" || sourceType === "LOSER_MATCH") {
                sourceElement = matchMap.get(String(toNumber(targetSlot.dataset.sourceMatchId)));
            } else if (sourceType === "GROUP_RANK") {
                sourceElement = groupMap.get(String(toNumber(targetSlot.dataset.sourceGroupId)));
            }

            if (!sourceElement) {
                return;
            }

            const sourceGroup = sourceElement.closest(".pb-group[data-round-index]");
            const targetGroup = targetSlot.closest(".pb-group[data-round-index]");
            if (!sourceGroup || !targetGroup) {
                return;
            }

            const sourceRoundIndex = toNumber(sourceGroup?.dataset.roundIndex);
            const targetRoundIndex = toNumber(targetGroup?.dataset.roundIndex);

            if (targetRoundIndex - sourceRoundIndex !== 1) {
                return;
            }

            const targetMatch = targetSlot.closest(".pb-match");
            if (!targetMatch) {
                return;
            }

            const targetKey = targetMatch.dataset.matchId || ("target-" + targetMap.size);
            if (!targetMap.has(targetKey)) {
                targetMap.set(targetKey, {
                    targetMatch: targetMatch,
                    dependencies: []
                });
            }

            targetMap.get(targetKey).dependencies.push({
                sourceElement: sourceElement,
                targetSlot: targetSlot,
                sourceType: sourceType,
                className: getLineClassForSource(sourceType)
            });
        });

        return Array.from(targetMap.values());
    }

    function alignDependencyTargetCards(board) {
        const boardRect = board.getBoundingClientRect();
        const boardScale = getBoardViewportScale(board, boardRect);
        const targetEntries = collectDependencyTargets(board);

        targetEntries.forEach(function (entry) {
            const dependencies = entry.dependencies;
            if (dependencies.length <= 1) {
                return;
            }

            const sourceYValues = dependencies.map(function (dependency) {
                return getElementMidpoint(dependency.sourceElement, boardRect, "right", boardScale).y;
            });
            const minY = Math.min.apply(null, sourceYValues);
            const maxY = Math.max.apply(null, sourceYValues);
            const desiredCenter = Math.round((minY + maxY) / 2);
            const targetCenter = getElementMidpoint(entry.targetMatch, boardRect, "left", boardScale).y;
            const currentMargin = toNumber(window.getComputedStyle(entry.targetMatch).marginTop);
            const nextMargin = Math.max(0, Math.round(currentMargin + desiredCenter - targetCenter));

            if (Math.abs(nextMargin - currentMargin) > 1) {
                entry.targetMatch.style.marginTop = nextMargin + "px";
            }
        });
    }

    function buildDependencyLinePathsFromDom(board) {
        const boardRect = board.getBoundingClientRect();
        const boardScale = getBoardViewportScale(board, boardRect);
        const targetEntries = collectDependencyTargets(board);
        const lines = [];

        targetEntries.forEach(function (entry) {
            const dependencies = entry.dependencies;

            if (dependencies.length <= 1) {
                dependencies.forEach(function (dependency) {
                    lines.push({
                        d: buildConnectorPath(
                            getElementMidpoint(dependency.sourceElement, boardRect, "right", boardScale),
                            getElementMidpoint(dependency.targetSlot, boardRect, "left", boardScale)
                        ),
                        className: dependency.className
                    });
                });
                return;
            }

            const sourcePoints = dependencies
                .map(function (dependency, index) {
                    const sourcePoint = getElementMidpoint(dependency.sourceElement, boardRect, "right", boardScale);
                    const targetPoint = getElementMidpoint(dependency.targetSlot, boardRect, "left", boardScale);
                    return {
                        x: Math.round(sourcePoint.x),
                        y: Math.round(sourcePoint.y),
                        targetY: targetPoint.y,
                        className: dependency.className,
                        order: index
                    };
                })
                .sort(function (left, right) {
                    if (left.y !== right.y) {
                        return left.y - right.y;
                    }
                    if (left.targetY !== right.targetY) {
                        return left.targetY - right.targetY;
                    }
                    return left.order - right.order;
                });

            const targetPoint = getElementMidpoint(entry.targetMatch, boardRect, "left", boardScale);
            const targetX = Math.round(targetPoint.x);
            const targetY = Math.round(targetPoint.y);
            const maxSourceX = Math.max.apply(null, sourcePoints.map(function (point) { return point.x; }));
            const sourceYValues = sourcePoints.map(function (point) { return point.y; });
            const minY = Math.min.apply(null, sourceYValues);
            const maxY = Math.max.apply(null, sourceYValues);
            const sourceSpanY = maxY - minY;
            const shouldSeparateLongSpan = sourceSpanY > 380;

            if (shouldSeparateLongSpan) {
                sourcePoints.forEach(function (point, index) {
                    const dependency = dependencies[point.order];
                    lines.push({
                        d: buildSeparatedConnectorPath(
                            getElementMidpoint(dependency.sourceElement, boardRect, "right", boardScale),
                            getElementMidpoint(dependency.targetSlot, boardRect, "left", boardScale),
                            index,
                            sourcePoints.length
                        ),
                        className: point.className + " is-split-long"
                    });
                });
                return;
            }

            const junctionY = Math.round((minY + maxY) / 2);
            const available = targetX - maxSourceX;
            const trunkX = available > 72
                ? clamp(maxSourceX + Math.round(available * 0.58), maxSourceX + 28, targetX - 34)
                : Math.max(maxSourceX + 36, targetX - 38);
            const className = sourcePoints.every(function (point) { return point.className === sourcePoints[0].className; })
                ? sourcePoints[0].className
                : "is-mixed-source";

            sourcePoints.forEach(function (point) {
                lines.push({
                    d: "M " + point.x + " " + point.y + " H " + trunkX,
                    className: className + " is-tree-branch"
                });
            });

            lines.push({
                d: "M " + trunkX + " " + minY + " V " + maxY,
                className: className + " is-tree-trunk"
            });
            lines.push({
                d: "M " + trunkX + " " + junctionY + " V " + targetY + " H " + targetX,
                className: className + " is-tree-output"
            });
        });

        return lines;
    }

    function updateBoardLines(board, layout) {
        if (layout) {
            layout.lines = [];
        }
    }

    function syncMeasuredLayout(board, layout, onSizeChanged) {
        if (!board || !layout || !Array.isArray(layout.rounds)) {
            return;
        }

        qsa(".pb-match", board).forEach(function (matchElement) {
            matchElement.style.marginTop = "";
        });

        layout.rounds.forEach(function (round, roundIndex) {
            round.groups.forEach(function (group, groupIndex) {
                const element = findGroupElement(board, roundIndex, groupIndex);
                if (!element) {
                    return;
                }

                element.style.height = "auto";
                group.height = Math.max(
                    toNumber(group.height),
                    calculateGroupHeight(group),
                    Math.ceil(Math.max(element.scrollHeight, element.offsetHeight))
                );
            });
        });

        layout.height = updateRoundPositions(layout.rounds, layout);
        board.style.height = layout.height + "px";

        layout.rounds.forEach(function (round, roundIndex) {
            const titleElement = qs('.pb-round-title[data-round-title-index="' + roundIndex + '"]', board);
            if (titleElement) {
                titleElement.style.left = round.x + "px";
                titleElement.style.top = layout.headerTop + "px";
                titleElement.style.width = round.width + "px";
            }

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
        if (typeof onSizeChanged === "function") {
            onSizeChanged();
        }
    }

    function initPublicBracket(root) {
        if (!root || root._publicTournamentBracket) {
            return root?._publicTournamentBracket || null;
        }

        const tournamentId = toNumber(root.dataset.tournamentId);
        const viewport = qs("[data-bracket-viewport]", root);
        const surface = qs("[data-bracket-surface]", root);
        const board = qs("[data-bracket-board]", root);
        const loading = qs("[data-bracket-loading]", root);
        const errorBox = qs("[data-bracket-error]", root);
        let latestPayload = null;
        let latestLayout = null;
        let scale = DEFAULT_SCALE;
        let dragging = false;
        let startX = 0;
        let startY = 0;
        let startLeft = 0;
        let startTop = 0;
        let pinching = false;
        let pinchStartDistance = 0;
        let pinchStartScale = scale;

        function setError(message) {
            if (!errorBox) {
                return;
            }

            if (trimToEmpty(message)) {
                errorBox.textContent = message;
                errorBox.classList.add("is-visible");
            } else {
                errorBox.textContent = "";
                errorBox.classList.remove("is-visible");
            }
        }

        function setLoading(isLoading) {
            if (loading) {
                loading.style.display = isLoading ? "" : "none";
            }
        }

        function updateSurfaceSize() {
            if (!latestLayout || !surface || !board) {
                return;
            }

            const width = Math.ceil(latestLayout.width * scale);
            const height = Math.ceil(latestLayout.height * scale);
            surface.style.width = width + "px";
            surface.style.height = height + "px";
            surface.style.minWidth = width + "px";
            surface.style.minHeight = height + "px";
            board.style.transform = "scale(" + scale + ")";
        }

        function setScale(nextScale, anchorClientX, anchorClientY) {
            if (!viewport || !latestLayout) {
                scale = clamp(nextScale, MIN_SCALE, MAX_SCALE);
                updateSurfaceSize();
                return;
            }

            const previousScale = scale;
            const next = clamp(nextScale, MIN_SCALE, MAX_SCALE);
            if (Math.abs(next - previousScale) < 0.001) {
                return;
            }

            const rect = viewport.getBoundingClientRect();
            const anchorX = Number.isFinite(anchorClientX) ? anchorClientX : rect.left + rect.width / 2;
            const anchorY = Number.isFinite(anchorClientY) ? anchorClientY : rect.top + rect.height / 2;
            const localX = anchorX - rect.left;
            const localY = anchorY - rect.top;
            const contentX = (viewport.scrollLeft + localX) / previousScale;
            const contentY = (viewport.scrollTop + localY) / previousScale;

            scale = next;
            updateSurfaceSize();

            viewport.scrollLeft = Math.max(0, contentX * scale - localX);
            viewport.scrollTop = Math.max(0, contentY * scale - localY);
        }

        function startPan(clientX, clientY) {
            if (!viewport || pinching) {
                return;
            }

            dragging = true;
            startX = clientX;
            startY = clientY;
            startLeft = viewport.scrollLeft;
            startTop = viewport.scrollTop;
            viewport.classList.add("is-dragging");
        }

        function movePan(clientX, clientY) {
            if (!dragging || !viewport || pinching) {
                return;
            }

            viewport.scrollLeft = startLeft - (clientX - startX);
            viewport.scrollTop = startTop - (clientY - startY);
        }

        function endPan() {
            dragging = false;
            viewport?.classList.remove("is-dragging");
        }

        function getTouchDistance(touches) {
            const dx = touches[0].clientX - touches[1].clientX;
            const dy = touches[0].clientY - touches[1].clientY;
            return Math.sqrt(dx * dx + dy * dy);
        }

        function getTouchMidpoint(touches) {
            return {
                x: (touches[0].clientX + touches[1].clientX) / 2,
                y: (touches[0].clientY + touches[1].clientY) / 2
            };
        }

        function initGestures() {
            if (!viewport || viewport.dataset.publicBracketGestures === "true") {
                return;
            }

            viewport.dataset.publicBracketGestures = "true";

            viewport.addEventListener("dragstart", function (event) {
                event.preventDefault();
            });

            viewport.addEventListener("pointerdown", function (event) {
                if (event.pointerType !== "mouse") {
                    return;
                }

                if (event.button !== undefined && event.button !== 0) {
                    return;
                }

                startPan(event.clientX, event.clientY);
                try {
                    viewport.setPointerCapture?.(event.pointerId);
                } catch (_error) {
                    // Embedded browsers can reject pointer capture on scroll containers.
                }
            });

            viewport.addEventListener("pointermove", function (event) {
                if (!dragging) {
                    return;
                }

                event.preventDefault();
                movePan(event.clientX, event.clientY);
            });

            function stopPointerDrag(event) {
                if (!dragging) {
                    return;
                }

                endPan();
                try {
                    viewport.releasePointerCapture?.(event.pointerId);
                } catch (_error) {
                    // Matching pointer capture fallback above.
                }
            }

            viewport.addEventListener("pointerup", stopPointerDrag);
            viewport.addEventListener("pointercancel", stopPointerDrag);
            viewport.addEventListener("pointerleave", stopPointerDrag);

            viewport.addEventListener("touchstart", function (event) {
                if (!event.touches) {
                    return;
                }

                if (event.touches.length >= 2) {
                    endPan();
                    pinching = true;
                    pinchStartDistance = Math.max(1, getTouchDistance(event.touches));
                    pinchStartScale = scale;
                    event.preventDefault();
                    return;
                }

                if (event.touches.length === 1) {
                    const touch = event.touches[0];
                    startPan(touch.clientX, touch.clientY);
                }
            }, { passive: false });

            viewport.addEventListener("touchmove", function (event) {
                if (!event.touches) {
                    return;
                }

                if (event.touches.length >= 2) {
                    const nextDistance = Math.max(1, getTouchDistance(event.touches));
                    const midpoint = getTouchMidpoint(event.touches);
                    setScale(pinchStartScale * (nextDistance / pinchStartDistance), midpoint.x, midpoint.y);
                    event.preventDefault();
                    return;
                }

                if (dragging && event.touches.length === 1) {
                    const touch = event.touches[0];
                    event.preventDefault();
                    movePan(touch.clientX, touch.clientY);
                }
            }, { passive: false });

            viewport.addEventListener("touchend", function (event) {
                if (event.touches && event.touches.length >= 2) {
                    return;
                }

                pinching = false;
                endPan();
            }, { passive: true });

            viewport.addEventListener("touchcancel", function () {
                pinching = false;
                endPan();
            }, { passive: true });

            viewport.addEventListener("wheel", function (event) {
                if (event.ctrlKey || event.metaKey) {
                    const direction = event.deltaY > 0 ? -1 : 1;
                    setScale(scale + direction * 0.08, event.clientX, event.clientY);
                    event.preventDefault();
                    return;
                }

                if (event.shiftKey || Math.abs(event.deltaX) > Math.abs(event.deltaY)) {
                    viewport.scrollLeft += event.deltaX || event.deltaY;
                    event.preventDefault();
                }
            }, { passive: false });

            window.addEventListener("blur", function () {
                pinching = false;
                endPan();
            });
        }

        function render(payload) {
            if (!board || !surface) {
                return;
            }

            const layout = buildLayout(payload);
            applyHorizontalPanSpace(layout, viewport);
            latestLayout = layout;
            board.classList.remove("is-measuring");

            if (!layout.rounds.length) {
                board.style.width = "100%";
                board.style.height = "640px";
                surface.style.width = "100%";
                surface.style.height = "640px";
                board.innerHTML = '<div class="public-bracket__loading">Giải đấu này chưa có dữ liệu để dựng sơ đồ.</div>';
                updateZoomLabel();
                return;
            }

            board.style.width = layout.width + "px";
            board.style.height = layout.height + "px";
            board.style.minWidth = layout.width + "px";
            board.classList.add("is-measuring");
            board.innerHTML = buildBoardHtml(layout);
            updateSurfaceSize();

            window.requestAnimationFrame(function () {
                syncMeasuredLayout(board, layout, updateSurfaceSize);
                board.classList.remove("is-measuring");

                if (layout.initialScrollLeft > 0 && viewport) {
                    viewport.scrollLeft = Math.max(0, layout.initialScrollLeft * scale);
                }
            });
        }

        async function load() {
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
                    board.innerHTML = '<div class="public-bracket__loading">Không tải được sơ đồ giải đấu.</div>';
                }
            } finally {
                setLoading(false);
            }
        }

        const rerender = debounce(function () {
            if (latestPayload) {
                render(latestPayload);
            }
        }, 140);

        initGestures();
        window.addEventListener("resize", rerender);

        const api = {
            load: load,
            reload: load,
            rerender: rerender,
            setScale: setScale
        };

        root._publicTournamentBracket = api;
        load();
        return api;
    }

    qsa("[data-public-tournament-bracket]").forEach(initPublicBracket);
})();
