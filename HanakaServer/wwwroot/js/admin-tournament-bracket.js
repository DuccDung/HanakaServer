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

    function clampNumber(value, min, max) {
        const number = Number(value);
        const fallback = Number.isFinite(min) ? min : 0;
        return Math.min(max, Math.max(min, Number.isFinite(number) ? number : fallback));
    }

    function formatPercent(value) {
        return Math.round(toNumber(value) * 100) + "%";
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

    function getMatchDisplayOrder(match) {
        const matchId = toNumber(match?.matchId);
        return matchId > 0 ? matchId : Number.MAX_SAFE_INTEGER;
    }

    function getDisplayMatches(matches) {
        return (Array.isArray(matches) ? matches : [])
            .map(function (match, originalIndex) {
                return {
                    match: match,
                    originalIndex: originalIndex,
                    displayOrder: getMatchDisplayOrder(match)
                };
            })
            .sort(function (left, right) {
                if (left.displayOrder !== right.displayOrder) {
                    return left.displayOrder - right.displayOrder;
                }

                return left.originalIndex - right.originalIndex;
            })
            .map(function (entry) {
                return entry.match;
            });
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
        let tone = "registration";

        if (sourceType === "WINNER_MATCH") {
            badge = sourceMatchId > 0 ? "W#" + sourceMatchId : "WIN";
            tone = "winner";
        } else if (sourceType === "LOSER_MATCH") {
            badge = sourceMatchId > 0 ? "L#" + sourceMatchId : "LOS";
            tone = "loser";
        } else if (sourceType === "GROUP_RANK") {
            badge = sourceRank > 0 ? "R" + sourceRank : "R?";
            tone = "group-rank";
        } else if (sourceType === "BYE") {
            badge = "Miễn đấu";
            tone = "bye";
        }

        return {
            type: sourceType,
            matchId: sourceMatchId,
            groupId: sourceGroupId,
            rank: sourceRank,
            text: sourceText,
            badge: badge,
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

    function buildMatchCard(match, matchIndex, groupKey, groupName, roundIndex, roundKey, roundLabel) {
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
            roundIndex: roundIndex,
            roundKey: roundKey,
            roundLabel: roundLabel,
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
        const roundLabel = buildRoundLabel(round, roundIndex);
        const groupName = trimToEmpty(group?.groupName) || ("Bảng " + (groupIndex + 1));
        const groupKey = buildGroupDisplayKey(roundKey, groupName, groupIndex);
        const matches = Array.isArray(group?.matches) ? group.matches : [];
        const completedCount = matches.filter(function (match) { return !!match?.isCompleted; }).length;
        const matchCards = matches.map(function (match, matchIndex) {
            return buildMatchCard(match, matchIndex, groupKey, groupName, roundIndex, roundKey, roundLabel);
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
            roundMapId: toNumber(round?.roundMapId) || toNumber(round?.tournamentRoundMapId),
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
                const targetKey = [
                    groupIndex,
                    matchIndex,
                    toNumber(match?.matchId)
                ].join(":");

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

                    if (
                        !existing
                        || candidate.targetOrder < existing.targetOrder
                        || (candidate.targetOrder === existing.targetOrder && candidate.slotOrder < existing.slotOrder)
                    ) {
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
        const matchSectionHeight = matchCount > 0
            ? matchCount * 108 + Math.max(0, matchCount - 1) * 10
            : 72;

        return matchSectionHeight;
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

    function buildLinePaths(rounds) {
        const lines = [];

        for (let roundIndex = 1; roundIndex < rounds.length; roundIndex += 1) {
            const previousRound = rounds[roundIndex - 1];
            const currentRound = rounds[roundIndex];

            currentRound.groups.forEach(function (targetGroup) {
                const targetX = targetGroup.x;
                const targetY = targetGroup.y + targetGroup.height / 2;

                (targetGroup.sourceIndexes || []).forEach(function (sourceIndex) {
                    const sourceGroup = previousRound.groups[sourceIndex];

                    if (!sourceGroup) {
                        return;
                    }

                    const sourceX = sourceGroup.x + sourceGroup.width;
                    const sourceY = sourceGroup.y + sourceGroup.height / 2;
                    lines.push({
                        d: buildConnectorPath({ x: sourceX, y: sourceY }, { x: targetX, y: targetY }),
                        type: "fallback",
                        className: "is-fallback"
                    });
                });
            });
        }

        return lines;
    }

    function getVerticalPanSpace(scroller) {
        const viewportHeight = Math.max(0, toNumber(scroller?.clientHeight));
        return viewportHeight > 0
            ? Math.max(720, Math.round(viewportHeight * 1.25))
            : 720;
    }

    function applyPanSpace(layout, scroller) {
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

        layout.leftOffset += leftPadding;
        layout.width = baseWidth + leftPadding + rightPadding;
        layout.verticalPanBottom = getVerticalPanSpace(scroller);
        layout.height += layout.verticalPanBottom;
        layout.initialScrollLeft = Math.max(0, leftPadding - 48);
        layout.lines = buildLinePaths(layout.rounds);
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
        stretchDependentGroupHeights(rounds, groupGap);

        const boardHeight = updateRoundPositions(rounds, {
            columnWidth: columnWidth,
            columnGap: columnGap,
            groupGap: groupGap,
            leftOffset: leftOffset,
            topOffset: topOffset,
            bottomPadding: bottomPadding
        });
        const lines = buildLinePaths(rounds);

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
            '<article class="' + escapeHtml(classes) + '" data-match-id="' + escapeHtml(match.matchId || "") + '" data-round-index="' + escapeHtml(match.roundIndex ?? "") + '" data-round-key="' + escapeHtml(match.roundKey || "") + '" data-round-label="' + escapeHtml(match.roundLabel || "") + '" tabindex="0" role="button" title="Bấm để làm rõ dây liên kết">',
            '<div class="admin-bracket-match__top">',
            '<div class="admin-bracket-match__title">',
            '<div class="admin-bracket-match__group"><b>' + escapeHtml(match.groupKey || "") + "</b><span>" + escapeHtml(match.groupName || "") + "</span></div>",
            '<strong>' + escapeHtml(match.title) + "</strong>",
            "</div>",
            "<span>" + escapeHtml(match.metaText || "Chưa có giờ / sân") + "</span>",
            "</div>",
            renderMatchTeam(match, 1),
            renderMatchTeam(match, 2),
            "</article>"
        ].join("");
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
            "admin-bracket-match__source",
            source?.isLinked ? "is-" + source.tone : "",
            isResolved ? "is-resolved" : "is-pending"
        ].filter(Boolean).join(" ");
        const sourceHtml = source?.isLinked
            ? '<small class="' + escapeHtml(sourceClasses) + '" title="' + escapeHtml(source.text || "") + '"><b>' + escapeHtml(source.badge) + "</b></small>"
            : "";
        const identityHtml = identity
            ? '<small class="admin-bracket-match__team-meta">' + escapeHtml(identity) + "</small>"
            : "";
        const tagsHtml = sourceHtml || identityHtml
            ? '<div class="admin-bracket-match__team-tags">' + sourceHtml + identityHtml + "</div>"
            : "";

        return [
            '<div class="admin-bracket-match__team" data-slot="' + slotNumber + '" data-source-type="' + escapeHtml(source?.type || "REGISTRATION") + '" data-source-match-id="' + escapeHtml(source?.matchId || "") + '" data-source-group-id="' + escapeHtml(source?.groupId || "") + '" data-source-rank="' + escapeHtml(source?.rank || "") + '">',
            '<div class="admin-bracket-match__team-main">',
            tagsHtml,
            '<span class="' + (isWinner ? "is-winner" : "") + '">' + escapeHtml(teamName) + "</span>",
            "</div>",
            '<b class="' + (isWinner ? "is-winner" : "") + '">' + escapeHtml(score) + "</b>",
            "</div>"
        ].join("");
    }

    function renderGroup(group, roundIndex, groupIndex) {
        const displayMatches = getDisplayMatches(group.matches);
        const groupClasses = [
            "admin-bracket-group",
            group.isReal ? "is-real" : "is-virtual"
        ].join(" ");

        return [
            '<article class="' + escapeHtml(groupClasses) + '" data-round-index="' + escapeHtml(roundIndex) + '" data-group-index="' + escapeHtml(groupIndex) + '" data-group-id="' + escapeHtml(group.groupId || "") + '" style="left:' + escapeHtml(group.x) + "px;top:" + escapeHtml(group.y) + "px;width:" + escapeHtml(group.width) + "px;height:" + escapeHtml(group.height) + 'px">',
            '<div class="admin-bracket-group__body">',
            displayMatches.length > 0
                ? displayMatches.map(renderMatch).join("")
                : '<div class="admin-bracket-group__empty">' + escapeHtml(group.isReal ? "Bảng này chưa có trận đấu." : "Quản trị viên chưa tạo bảng hoặc trận cho nhánh này.") + "</div>",
            "</div>",
            "</article>"
        ].join("");
    }

    function renderRoundTitle(round, headerTop, roundIndex) {
        const classes = [
            "admin-bracket-round-title",
            round.isSynthetic ? "is-virtual" : "is-real"
        ].join(" ");

        return [
            '<div class="' + escapeHtml(classes) + '" data-round-title-index="' + escapeHtml(roundIndex) + '" data-round-map-id="' + escapeHtml(round.roundMapId || "") + '" style="left:' + escapeHtml(round.x) + "px;top:" + escapeHtml(headerTop) + "px;width:" + escapeHtml(round.width) + 'px">',
            '<div class="admin-bracket-round-title__text">',
            "<span>" + escapeHtml(round.roundKey) + "</span>",
            "<strong>" + escapeHtml(round.roundLabel) + "</strong>",
            "</div>",
            '<div class="admin-bracket-round-title__meta">',
            "<span>" + escapeHtml(String(round.groupCount)) + " bảng</span>",
            "<span>" + escapeHtml(String(round.matchCount)) + " trận</span>",
            '<i class="' + (round.isSynthetic ? "is-virtual" : "is-real") + '" aria-hidden="true"></i>',
            "</div>",
            "</div>"
        ].join("");
    }

    function renderLineDataAttributes(line) {
        if (!line || typeof line === "string") {
            return "";
        }

        const attributes = [
            ["data-source-type", line.sourceType || line.type || ""],
            ["data-source-match-id", line.sourceMatchId || ""],
            ["data-source-group-id", line.sourceGroupId || ""],
            ["data-target-match-id", line.targetMatchId || ""],
            ["data-target-slot", line.targetSlot || ""]
        ];

        return attributes
            .filter(function (entry) { return entry[1] !== ""; })
            .map(function (entry) { return " " + entry[0] + '="' + escapeHtml(entry[1]) + '"'; })
            .join("");
    }

    function renderLinePath(line) {
        if (line?.shape === "circle") {
            return '<circle class="' + escapeHtml(["admin-bracket-line-node", line?.className].filter(Boolean).join(" ")) + '" cx="' + escapeHtml(line.cx) + '" cy="' + escapeHtml(line.cy) + '" r="' + escapeHtml(line.r || 4) + '"' + renderLineDataAttributes(line) + '></circle>';
        }

        const path = typeof line === "string" ? line : line?.d;
        if (!path) {
            return "";
        }

        const classes = ["admin-bracket-line", typeof line === "string" ? "" : line?.className]
            .filter(Boolean)
            .join(" ");

        return '<path class="' + escapeHtml(classes) + '" d="' + escapeHtml(path) + '"' + renderLineDataAttributes(line) + '></path>';
    }

    function buildBoardHtml(layout) {
        return [
            '<svg class="admin-bracket-lines" data-bracket-lines="true" viewBox="0 0 ' + escapeHtml(layout.width) + " " + escapeHtml(layout.height) + '" aria-hidden="true">',
            layout.lines.map(function (path) {
                return renderLinePath(path);
            }).join(""),
            "</svg>",
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
        return qs('.admin-bracket-group[data-round-index="' + roundIndex + '"][data-group-index="' + groupIndex + '"]', board);
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
        const forward = sourceX <= targetX;
        const direction = forward ? 1 : -1;
        const distance = Math.max(72, Math.abs(targetX - sourceX));
        const control = clampNumber(Math.round(distance * 0.48), 64, 220);
        const c1x = sourceX + direction * control;
        const c2x = targetX - direction * control;

        return "M " + sourceX + " " + sourceY
            + " C " + c1x + " " + sourceY
            + " " + c2x + " " + targetY
            + " " + targetX + " " + targetY;
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

        qsa(".admin-bracket-match__team[data-source-type]", board).forEach(function (targetSlot) {
            const sourceType = normalizeSourceType(targetSlot.dataset.sourceType);
            let sourceElement = null;
            let sourceMatchId = 0;
            let sourceGroupId = 0;

            if (sourceType === "WINNER_MATCH" || sourceType === "LOSER_MATCH") {
                sourceMatchId = toNumber(targetSlot.dataset.sourceMatchId);
                sourceElement = matchMap.get(String(sourceMatchId));
            } else if (sourceType === "GROUP_RANK") {
                sourceGroupId = toNumber(targetSlot.dataset.sourceGroupId);
                sourceElement = groupMap.get(String(sourceGroupId));
            }

            if (!sourceElement) {
                return;
            }

            const targetMatch = targetSlot.closest(".admin-bracket-match");
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
                targetMatch: targetMatch,
                targetMatchId: toNumber(targetMatch.dataset.matchId),
                targetSlotNumber: toNumber(targetSlot.dataset.slot),
                sourceType: sourceType,
                sourceMatchId: sourceMatchId,
                sourceGroupId: sourceGroupId,
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
            entry.dependencies.forEach(function (dependency) {
                const sourcePoint = getElementMidpoint(dependency.sourceElement, boardRect, "right", boardScale);
                const targetPoint = getElementMidpoint(dependency.targetSlot, boardRect, "left", boardScale);

                lines.push({
                    d: buildConnectorPath(sourcePoint, targetPoint),
                    type: dependency.sourceType,
                    sourceType: dependency.sourceType,
                    sourceMatchId: dependency.sourceMatchId,
                    sourceGroupId: dependency.sourceGroupId,
                    targetMatchId: dependency.targetMatchId,
                    targetSlot: dependency.targetSlotNumber,
                    className: dependency.className + " is-direct-link"
                });
            });
        });

        return lines;
    }

    function updateBoardLines(board, layout) {
        const svg = qs("[data-bracket-lines]", board);
        const dependencyLines = buildDependencyLinePathsFromDom(board);
        const lines = dependencyLines.length > 0
            ? dependencyLines
            : buildLinePaths(layout.rounds);

        layout.lines = lines;

        if (!svg) {
            return;
        }

        svg.setAttribute("viewBox", "0 0 " + layout.width + " " + layout.height);
        svg.innerHTML = lines.map(renderLinePath).join("");
    }

    function syncMeasuredLayout(board, layout) {
        if (!board || !layout || !Array.isArray(layout.rounds)) {
            return;
        }

        qsa(".admin-bracket-match", board).forEach(function (matchElement) {
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

        stretchDependentGroupHeights(layout.rounds, layout.groupGap);
        layout.height = updateRoundPositions(layout.rounds, layout);
        layout.height += toNumber(layout.verticalPanBottom);
        board.style.height = layout.height + "px";

        layout.rounds.forEach(function (round, roundIndex) {
            const titleElement = qs('.admin-bracket-round-title[data-round-title-index="' + roundIndex + '"]', board);
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

        alignDependencyTargetCards(board);
        updateBoardLines(board, layout);
    }

    function clearBracketFocus(board) {
        if (!board) {
            return;
        }

        board.classList.remove("is-focus-mode");
        delete board.dataset.focusMatchId;

        qsa(".is-focus-selected, .is-focus-related, .is-focus-stack, .is-focus-slot, .is-focus-link", board)
            .forEach(function (element) {
                element.classList.remove(
                    "is-focus-selected",
                    "is-focus-related",
                    "is-focus-stack",
                    "is-focus-slot",
                    "is-focus-link"
                );
            });
    }

    function markRelatedMatch(board, matchId, relatedMatchIds) {
        const normalizedId = toNumber(matchId);
        if (normalizedId <= 0) {
            return null;
        }

        relatedMatchIds.add(String(normalizedId));
        return qs('.admin-bracket-match[data-match-id="' + normalizedId + '"]', board);
    }

    function markTargetSlot(board, matchId, slotNumber) {
        const normalizedMatchId = toNumber(matchId);
        const normalizedSlotNumber = toNumber(slotNumber);

        if (normalizedMatchId <= 0 || normalizedSlotNumber <= 0) {
            return;
        }

        const slot = qs(
            '.admin-bracket-match[data-match-id="' + normalizedMatchId + '"] .admin-bracket-match__team[data-slot="' + normalizedSlotNumber + '"]',
            board
        );

        if (slot) {
            slot.classList.add("is-focus-slot");
        }
    }

    function clearBracketValidation(board) {
        if (!board) {
            return;
        }

        board.classList.remove("is-validation-mode");
        qsa(".is-validation-warning, .is-validation-winner-missing, .is-validation-loser-missing, .is-validation-team-warning", board)
            .forEach(function (element) {
                element.classList.remove(
                    "is-validation-warning",
                    "is-validation-winner-missing",
                    "is-validation-loser-missing",
                    "is-validation-team-warning"
                );
            });
        qsa(".admin-bracket-validation-badge", board).forEach(function (element) {
            element.remove();
        });
    }

    function getValidationTeamRows(matchElement, type) {
        const rows = qsa(".admin-bracket-match__team", matchElement);
        const winnerRows = rows.filter(function (row) {
            return !!qs(".is-winner", row);
        });

        if (type === "winner") {
            return winnerRows.length ? winnerRows : rows;
        }

        if (!winnerRows.length) {
            return rows;
        }

        return rows.filter(function (row) {
            return !winnerRows.includes(row);
        });
    }

    function addValidationBadge(matchElement, text) {
        const badge = document.createElement("div");
        badge.className = "admin-bracket-validation-badge";
        badge.textContent = text;
        matchElement.appendChild(badge);
    }

    function normalizeValidationMode(value) {
        const normalized = trimToEmpty(value).toLowerCase();
        if (normalized === "loser" || normalized === "all") {
            return normalized;
        }

        return "winner";
    }

    function getValidationModeLabel(mode) {
        if (mode === "loser") {
            return "nhánh thua/cứu thua";
        }

        if (mode === "all") {
            return "nhánh thắng và nhánh thua";
        }

        return "nhánh thắng";
    }

    function collectBracketValidationIssues(board, rescueRoundIndexes, mode) {
        mode = normalizeValidationMode(mode);
        const matches = qsa(".admin-bracket-match[data-match-id]", board)
            .map(function (element) {
                return {
                    element: element,
                    matchId: toNumber(element.dataset.matchId),
                    roundIndex: toNumber(element.dataset.roundIndex),
                    roundKey: trimToEmpty(element.dataset.roundKey),
                    roundLabel: trimToEmpty(element.dataset.roundLabel)
                };
            })
            .filter(function (entry) {
                return entry.matchId > 0 && entry.roundIndex >= 0;
            });
        const roundMatchCounts = new Map();
        const outgoingByMatchId = new Map();
        const rescueKeys = new Set(Array.from(rescueRoundIndexes || []).map(String));
        let maxRoundIndex = -1;

        matches.forEach(function (entry) {
            const roundKey = String(entry.roundIndex);
            maxRoundIndex = Math.max(maxRoundIndex, entry.roundIndex);
            roundMatchCounts.set(roundKey, (roundMatchCounts.get(roundKey) || 0) + 1);
            outgoingByMatchId.set(String(entry.matchId), { winner: 0, loser: 0 });
        });

        qsa(".admin-bracket-line[data-source-match-id]", board).forEach(function (line) {
            const sourceMatchId = toNumber(line.dataset.sourceMatchId);
            const sourceType = normalizeSourceType(line.dataset.sourceType);

            if (sourceMatchId <= 0 || (sourceType !== "WINNER_MATCH" && sourceType !== "LOSER_MATCH")) {
                return;
            }

            const key = String(sourceMatchId);
            const outgoing = outgoingByMatchId.get(key) || { winner: 0, loser: 0 };
            if (sourceType === "WINNER_MATCH") {
                outgoing.winner += 1;
            } else {
                outgoing.loser += 1;
            }
            outgoingByMatchId.set(key, outgoing);
        });

        return matches
            .map(function (entry) {
                const roundKey = String(entry.roundIndex);
                const roundMatchCount = roundMatchCounts.get(roundKey) || 0;
                const shouldContinue = entry.roundIndex < maxRoundIndex || roundMatchCount > 1;
                const isRescueRound = rescueKeys.has(roundKey);
                const outgoing = outgoingByMatchId.get(String(entry.matchId)) || { winner: 0, loser: 0 };
                const missingWinner = (mode === "winner" || mode === "all") && shouldContinue && outgoing.winner <= 0;
                const missingLoser = (mode === "loser" || mode === "all") && shouldContinue && isRescueRound && outgoing.loser <= 0;

                if (!missingWinner && !missingLoser) {
                    return null;
                }

                return {
                    matchId: entry.matchId,
                    roundIndex: entry.roundIndex,
                    roundKey: entry.roundKey,
                    roundLabel: entry.roundLabel,
                    element: entry.element,
                    missingWinner: missingWinner,
                    missingLoser: missingLoser
                };
            })
            .filter(Boolean);
    }

    function applyBracketValidation(board, rescueRoundIndexes, mode) {
        clearBracketValidation(board);

        if (!board) {
            return [];
        }

        const issues = collectBracketValidationIssues(board, rescueRoundIndexes, mode);
        if (!issues.length) {
            return issues;
        }

        board.classList.add("is-validation-mode");
        issues.forEach(function (issue) {
            const matchElement = issue.element;
            matchElement.classList.add("is-validation-warning");

            if (issue.missingWinner) {
                matchElement.classList.add("is-validation-winner-missing");
                addValidationBadge(matchElement, "Thiếu winner đi tiếp");
                getValidationTeamRows(matchElement, "winner").forEach(function (row) {
                    row.classList.add("is-validation-team-warning");
                });
            }

            if (issue.missingLoser) {
                matchElement.classList.add("is-validation-loser-missing");
                addValidationBadge(matchElement, "Thiếu loser cứu thua");
                getValidationTeamRows(matchElement, "loser").forEach(function (row) {
                    row.classList.add("is-validation-team-warning");
                });
            }
        });

        return issues;
    }

    function applyBracketFocus(board, selectedMatch) {
        if (!board || !selectedMatch) {
            return;
        }

        const selectedMatchId = toNumber(selectedMatch.dataset.matchId);
        if (selectedMatchId <= 0) {
            return;
        }

        if (board.dataset.focusMatchId === String(selectedMatchId)) {
            clearBracketFocus(board);
            return;
        }

        clearBracketFocus(board);

        const relatedMatchIds = new Set();
        const upstreamTargetIds = new Set();
        const downstreamSourceIds = new Set();
        const downstreamSourceGroupIds = new Set();
        const visibleGroupIds = new Set();
        const focusLines = new Set();
        const lineEntries = qsa(".admin-bracket-line", board).map(function (line) {
            return {
                element: line,
                sourceMatchId: toNumber(line.dataset.sourceMatchId),
                sourceGroupId: toNumber(line.dataset.sourceGroupId),
                targetMatchId: toNumber(line.dataset.targetMatchId),
                targetSlot: toNumber(line.dataset.targetSlot)
            };
        });

        function getMatchElementById(matchId) {
            const normalizedMatchId = toNumber(matchId);
            return normalizedMatchId > 0
                ? qs('.admin-bracket-match[data-match-id="' + normalizedMatchId + '"]', board)
                : null;
        }

        function getMatchGroupId(matchId) {
            return toNumber(getMatchElementById(matchId)?.closest(".admin-bracket-group")?.dataset.groupId);
        }

        function addDownstreamSourceGroup(groupId) {
            const normalizedGroupId = toNumber(groupId);
            if (normalizedGroupId <= 0) {
                return false;
            }

            const key = String(normalizedGroupId);
            if (downstreamSourceGroupIds.has(key)) {
                return false;
            }

            downstreamSourceGroupIds.add(key);
            return true;
        }

        function addRelatedMatch(matchId) {
            const normalizedMatchId = toNumber(matchId);
            if (normalizedMatchId <= 0) {
                return false;
            }

            const key = String(normalizedMatchId);
            if (relatedMatchIds.has(key)) {
                return false;
            }

            relatedMatchIds.add(key);
            return true;
        }

        function addUpstreamTarget(matchId) {
            const normalizedMatchId = toNumber(matchId);
            if (normalizedMatchId <= 0) {
                return false;
            }

            const key = String(normalizedMatchId);
            const changedMatch = addRelatedMatch(normalizedMatchId);
            if (upstreamTargetIds.has(key)) {
                return changedMatch;
            }

            upstreamTargetIds.add(key);
            return true;
        }

        function addDownstreamSource(matchId) {
            const normalizedMatchId = toNumber(matchId);
            if (normalizedMatchId <= 0) {
                return false;
            }

            const key = String(normalizedMatchId);
            const changedMatch = addRelatedMatch(normalizedMatchId);
            const changedGroup = addDownstreamSourceGroup(getMatchGroupId(normalizedMatchId));
            if (downstreamSourceIds.has(key)) {
                return changedMatch || changedGroup;
            }

            downstreamSourceIds.add(key);
            return true;
        }

        function addVisibleGroup(groupId, queueUpstreamMatches) {
            const normalizedGroupId = toNumber(groupId);
            if (normalizedGroupId <= 0) {
                return false;
            }

            const key = String(normalizedGroupId);
            const wasKnown = visibleGroupIds.has(key);
            visibleGroupIds.add(key);
            const changedGroup = addDownstreamSourceGroup(normalizedGroupId);
            let changedMatch = false;

            const groupElement = qs('.admin-bracket-group[data-group-id="' + normalizedGroupId + '"]', board);
            if (groupElement) {
                qsa(".admin-bracket-match[data-match-id]", groupElement).forEach(function (matchElement) {
                    changedMatch = (queueUpstreamMatches
                        ? addUpstreamTarget(matchElement.dataset.matchId)
                        : addRelatedMatch(matchElement.dataset.matchId)) || changedMatch;
                });
            }

            return !wasKnown || changedGroup || changedMatch;
        }

        addUpstreamTarget(selectedMatchId);
        addDownstreamSource(selectedMatchId);

        let changed = true;
        while (changed) {
            changed = false;

            lineEntries.forEach(function (entry) {
                const isUpstream =
                    entry.targetMatchId > 0
                    && upstreamTargetIds.has(String(entry.targetMatchId));
                const isDownstream =
                    (entry.sourceMatchId > 0 && downstreamSourceIds.has(String(entry.sourceMatchId)))
                    || (entry.sourceGroupId > 0 && downstreamSourceGroupIds.has(String(entry.sourceGroupId)));

                if (isUpstream) {
                    focusLines.add(entry);
                    changed = addUpstreamTarget(entry.sourceMatchId) || changed;

                    if (entry.sourceGroupId > 0) {
                        changed = addVisibleGroup(entry.sourceGroupId, true) || changed;
                    }
                }

                if (isDownstream) {
                    focusLines.add(entry);
                    changed = addDownstreamSource(entry.targetMatchId) || changed;

                    if (entry.sourceGroupId > 0) {
                        changed = addVisibleGroup(entry.sourceGroupId, false) || changed;
                    }
                }
            });
        }

        focusLines.forEach(function (entry) {
            entry.element.classList.add("is-focus-link");
            markTargetSlot(board, entry.targetMatchId, entry.targetSlot);
        });

        relatedMatchIds.forEach(function (matchId) {
            const matchElement = qs('.admin-bracket-match[data-match-id="' + matchId + '"]', board);
            const groupElement = matchElement?.closest(".admin-bracket-group");

            if (matchElement) {
                matchElement.classList.add("is-focus-related");
            }

            if (groupElement) {
                groupElement.classList.add("is-focus-stack");
            }
        });

        visibleGroupIds.forEach(function (groupId) {
            const groupElement = qs('.admin-bracket-group[data-group-id="' + groupId + '"]', board);

            if (!groupElement) {
                return;
            }

            groupElement.classList.add("is-focus-stack", "is-focus-related");
            qsa(".admin-bracket-match", groupElement).forEach(function (matchElement) {
                matchElement.classList.add("is-focus-related");
            });
        });

        selectedMatch.classList.add("is-focus-selected", "is-focus-related");
        selectedMatch.closest(".admin-bracket-group")?.classList.add("is-focus-stack");
        board.dataset.focusMatchId = String(selectedMatchId);
        board.classList.add("is-focus-mode");
    }

    function bindBracketFocusInteractions(board, scroller) {
        if (!board || board.dataset.focusInteractionsReady === "true") {
            return;
        }

        board.dataset.focusInteractionsReady = "true";

        board.addEventListener("click", function (event) {
            const matchElement = event.target.closest?.(".admin-bracket-match[data-match-id]");

            if (!matchElement || !board.contains(matchElement)) {
                if (board.classList.contains("is-focus-mode")) {
                    clearBracketFocus(board);
                }
                return;
            }

            event.preventDefault();
            event.stopPropagation();
            applyBracketFocus(board, matchElement);
        });

        board.addEventListener("keydown", function (event) {
            if (event.key !== "Enter" && event.key !== " ") {
                return;
            }

            const matchElement = event.target.closest?.(".admin-bracket-match[data-match-id]");
            if (!matchElement || !board.contains(matchElement)) {
                return;
            }

            event.preventDefault();
            applyBracketFocus(board, matchElement);
        });

        document.addEventListener("keydown", function (event) {
            if (event.key === "Escape") {
                clearBracketFocus(board);
            }
        });

        if (scroller) {
            scroller.addEventListener("click", function (event) {
                const matchElement = event.target.closest?.(".admin-bracket-match[data-match-id]");

                if (matchElement && board.contains(matchElement)) {
                    return;
                }

                clearBracketFocus(board);
            });
        }

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

        function isMatchCardEventTarget(target) {
            return !!target?.closest?.(".admin-bracket-match, button, a, input, select, textarea");
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

            if (isMatchCardEventTarget(event.target)) {
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

            if (isMatchCardEventTarget(event.target)) {
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

    function initBracketViewer(page, options) {
        if (!page) {
            return null;
        }

        if (page._adminTournamentBracketViewer) {
            return page._adminTournamentBracketViewer;
        }

        options = options || {};
        const tournamentId = toNumber(options.tournamentId || page.dataset.tournamentId);
    const board = qs("[data-bracket-board]", page);
    const loading = qs("[data-bracket-loading]", page);
    const errorBox = qs("[data-bracket-error]", page);
    const scroller = qs("[data-bracket-scroll]", page);
    const zoomValue = qs("[data-bracket-zoom-value]", page);
    const zoomOutButton = qs("[data-bracket-zoom-out]", page);
    const zoomInButton = qs("[data-bracket-zoom-in]", page);
    const zoomResetButton = qs("[data-bracket-zoom-reset]", page);
    const validationToggleButtons = qsa("[data-bracket-validation-toggle]", page);
    const summaryRefs = {
        rounds: qs("[data-stat-rounds]", page),
        groups: qs("[data-stat-groups]", page),
        matches: qs("[data-stat-matches]", page),
        completed: qs("[data-stat-completed]", page)
    };

    const minZoom = clampNumber(options.minZoom || page.dataset.bracketMinZoom || 0.55, 0.2, 2);
    const maxZoom = clampNumber(options.maxZoom || page.dataset.bracketMaxZoom || 1.6, minZoom, 3);
    const zoomStep = clampNumber(options.zoomStep || page.dataset.bracketZoomStep || 0.1, 0.05, 0.5);
    const defaultZoom = clampNumber(options.zoom || page.dataset.bracketZoom || 1, minZoom, maxZoom);
    let latestPayload = null;
    let latestLayout = null;
    let bracketZoom = defaultZoom;
    const validationRescueRoundIndexes = new Set();
    let validationPanel = null;
    let validationMode = "winner";

    function updateZoomControls() {
        if (zoomValue) {
            zoomValue.textContent = formatPercent(bracketZoom);
        }

        if (zoomOutButton) {
            zoomOutButton.disabled = bracketZoom <= minZoom + 0.001;
        }

        if (zoomInButton) {
            zoomInButton.disabled = bracketZoom >= maxZoom - 0.001;
        }
    }

    function applyBracketZoom(value) {
        bracketZoom = clampNumber(value, minZoom, maxZoom);

        if (board) {
            board.style.zoom = String(bracketZoom);
            board.style.setProperty("--admin-bracket-zoom", String(bracketZoom));
        }

        updateZoomControls();
        clearBracketFocus(board);
        return bracketZoom;
    }

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

    function ensureValidationPanel() {
        if (validationPanel) {
            return validationPanel;
        }

        const host = qs(".admin-tournament-bracket-panel", page) || page;
        validationPanel = document.createElement("div");
        validationPanel.className = "admin-bracket-validation-panel d-none";
        validationPanel.setAttribute("data-bracket-validation-panel", "true");
        validationPanel.innerHTML = [
            '<div class="admin-bracket-validation-panel__head">',
            '<strong><i class="fas fa-exclamation-triangle mr-1"></i> Kiểm tra luồng đi tiếp</strong>',
            '<button type="button" class="admin-bracket-validation-panel__close" data-bracket-validation-close title="Đóng"><i class="fas fa-times"></i></button>',
            "</div>",
            '<div class="admin-bracket-validation-panel__note">Chỉ tô đỏ để quản trị viên theo dõi nhánh đấu. Không chặn lưu, không sửa trận, không ghi dữ liệu.</div>',
            '<div class="admin-bracket-validation-panel__rounds" data-bracket-validation-rounds></div>',
            '<div class="admin-bracket-validation-panel__actions">',
            '<button type="button" class="admin-bracket-validation-panel__run" data-bracket-validation-run data-bracket-validation-mode="winner"><i class="fas fa-trophy mr-1"></i> Kiểm tra đội thắng</button>',
            '<button type="button" class="admin-bracket-validation-panel__run is-loser" data-bracket-validation-run data-bracket-validation-mode="loser"><i class="fas fa-life-ring mr-1"></i> Kiểm tra đội thua</button>',
            '<button type="button" class="admin-bracket-validation-panel__run" data-bracket-validation-run><i class="fas fa-search mr-1"></i> Kiểm tra nhánh đấu</button>',
            '<button type="button" class="admin-bracket-validation-panel__clear" data-bracket-validation-clear><i class="fas fa-eraser mr-1"></i> Xóa cảnh báo</button>',
            "</div>",
            '<div class="admin-bracket-validation-panel__summary" data-bracket-validation-summary></div>'
        ].join("");

        const scrollElement = qs("[data-bracket-scroll]", host);
        if (scrollElement && scrollElement.parentNode === host) {
            host.insertBefore(validationPanel, scrollElement);
        } else {
            host.appendChild(validationPanel);
        }

        qs("[data-bracket-validation-close]", validationPanel)?.addEventListener("click", function () {
            validationPanel.classList.add("d-none");
        });
        qsa("[data-bracket-validation-run]", validationPanel).forEach(function (button) {
            button.addEventListener("click", function () {
                runBracketValidation(button.dataset.bracketValidationMode);
            });
        });
        qs("[data-bracket-validation-clear]", validationPanel)?.addEventListener("click", function () {
            clearBracketValidation(board);
            updateValidationSummary([]);
        });
        qs("[data-bracket-validation-rounds]", validationPanel)?.addEventListener("change", function (event) {
            const checkbox = event.target.closest?.("[data-bracket-rescue-round]");
            if (!checkbox) {
                return;
            }

            if (checkbox.checked) {
                validationRescueRoundIndexes.add(String(checkbox.value));
            } else {
                validationRescueRoundIndexes.delete(String(checkbox.value));
            }

            runBracketValidation(validationMode);
        });

        return validationPanel;
    }

    function syncValidationRoundOptions() {
        const panel = ensureValidationPanel();
        const roundsBox = qs("[data-bracket-validation-rounds]", panel);
        const rounds = (latestLayout?.rounds || [])
            .map(function (round, roundIndex) {
                return {
                    roundIndex: roundIndex,
                    roundKey: trimToEmpty(round?.roundKey),
                    roundLabel: trimToEmpty(round?.roundLabel),
                    matchCount: toNumber(round?.matchCount)
                };
            })
            .filter(function (round) {
                return round.matchCount > 0;
            });

        if (!roundsBox) {
            return;
        }

        if (!rounds.length) {
            roundsBox.innerHTML = '<div class="admin-bracket-validation-panel__empty">Chưa có vòng đấu có trận để kiểm tra.</div>';
            return;
        }

        roundsBox.innerHTML = rounds.map(function (round) {
            const key = String(round.roundIndex);
            const checked = validationRescueRoundIndexes.has(key) ? " checked" : "";
            return [
                '<label class="admin-bracket-validation-round">',
                '<input type="checkbox" data-bracket-rescue-round value="' + escapeHtml(key) + '"' + checked + " />",
                '<span><b>' + escapeHtml(round.roundKey || ("R" + (round.roundIndex + 1))) + "</b> " + escapeHtml(round.roundLabel || ("Vòng " + (round.roundIndex + 1))) + "</span>",
                '<small>Tích chọn nếu vòng này là vòng cứu thua. Không tích là loại trực tiếp.</small>',
                "</label>"
            ].join("");
        }).join("");
    }

    function updateValidationSummary(issues, mode) {
        const panel = ensureValidationPanel();
        const summary = qs("[data-bracket-validation-summary]", panel);
        mode = normalizeValidationMode(mode || validationMode);
        if (!summary) {
            return;
        }

        const count = Array.isArray(issues) ? issues.length : 0;
        if (count === 0) {
            summary.className = "admin-bracket-validation-panel__summary is-ok";
            summary.innerHTML = '<i class="fas fa-check-circle mr-1"></i> Chưa phát hiện winner/loser bị đứng yên theo cấu hình hiện tại.';
            return;
        }

        const missingWinner = issues.filter(function (issue) { return issue.missingWinner; }).length;
        const missingLoser = issues.filter(function (issue) { return issue.missingLoser; }).length;
        summary.className = "admin-bracket-validation-panel__summary is-error";
        summary.innerHTML = [
            '<i class="fas fa-exclamation-circle mr-1"></i>',
            "Có " + count + " trận cần xem lại.",
            " Thiếu winner đi tiếp: " + missingWinner + ".",
            " Thiếu loser cứu thua: " + missingLoser + "."
        ].join("");
    }

    function runBracketValidation(mode) {
        validationMode = normalizeValidationMode(mode || validationMode);
        clearBracketFocus(board);
        const issues = applyBracketValidation(board, validationRescueRoundIndexes, validationMode);
        updateValidationSummary(issues, validationMode);
        return issues;
    }

    function toggleValidationPanel(mode) {
        const hasExplicitMode = !!trimToEmpty(mode);
        validationMode = normalizeValidationMode(mode || validationMode);
        const panel = ensureValidationPanel();
        syncValidationRoundOptions();
        if (hasExplicitMode) {
            panel.classList.remove("d-none");
        } else {
            panel.classList.toggle("d-none");
        }

        if (!panel.classList.contains("d-none")) {
            runBracketValidation(validationMode);
        }
    }

    function render(payload, renderOptions) {
        if (!board) {
            return;
        }

        renderOptions = renderOptions || {};

        clearBracketFocus(board);
        const layout = buildLayout(payload);
        latestLayout = layout;
        applyPanSpace(layout, scroller);
        board.classList.remove("is-measuring");
        applyBracketZoom(bracketZoom);
        clearBracketValidation(board);
        if (validationPanel) {
            syncValidationRoundOptions();
        }

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
        bindBracketFocusInteractions(board, scroller);
        board.innerHTML = buildBoardHtml(layout);
        window.requestAnimationFrame(function () {
            syncMeasuredLayout(board, layout);
            board.classList.remove("is-measuring");

            if (scroller && renderOptions.viewport) {
                scroller.scrollLeft = toNumber(renderOptions.viewport.left);
                scroller.scrollTop = toNumber(renderOptions.viewport.top);
            } else if (layout.initialScrollLeft > 0 && scroller) {
                scroller.scrollLeft = layout.initialScrollLeft;
            }

            const focusMatchId = toNumber(renderOptions.focusMatchId);
            if (focusMatchId > 0) {
                const focusMatch = qs('.admin-bracket-match[data-match-id="' + focusMatchId + '"]', board);
                if (focusMatch) {
                    applyBracketFocus(board, focusMatch);
                }
            }

            if (validationPanel && !validationPanel.classList.contains("d-none")) {
                runBracketValidation(validationMode);
            }

            page.dispatchEvent(new CustomEvent("adminbracket:rendered", {
                detail: {
                    payload: latestPayload,
                    layout: latestLayout,
                    focusMatchId: focusMatchId || null
                }
            }));
        });

        if (summaryRefs.rounds) summaryRefs.rounds.textContent = String(layout.summary.rounds);
        if (summaryRefs.groups) summaryRefs.groups.textContent = String(layout.summary.groups);
        if (summaryRefs.matches) summaryRefs.matches.textContent = String(layout.summary.matches);
        if (summaryRefs.completed) summaryRefs.completed.textContent = String(layout.summary.completed);
    }

    async function loadBracket(loadOptions) {
        if (!tournamentId) {
            setError("Thiếu tournamentId để tải sơ đồ.");
            return;
        }

        loadOptions = loadOptions || {};
        const preservedViewport = loadOptions.preserveViewport && scroller
            ? { left: scroller.scrollLeft, top: scroller.scrollTop }
            : null;

        setError("");
        setLoading(true);

        try {
            latestPayload = await fetchJson("/api/tournaments/" + tournamentId + "/rounds-with-matches");
            render(latestPayload, {
                viewport: preservedViewport,
                focusMatchId: loadOptions.focusMatchId
            });
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

        if (zoomOutButton) {
            zoomOutButton.addEventListener("click", function () {
                applyBracketZoom(bracketZoom - zoomStep);
            });
        }

        if (zoomInButton) {
            zoomInButton.addEventListener("click", function () {
                applyBracketZoom(bracketZoom + zoomStep);
            });
        }

        if (zoomResetButton) {
            zoomResetButton.addEventListener("click", function () {
                applyBracketZoom(defaultZoom);
            });
        }

        validationToggleButtons.forEach(function (button) {
            button.addEventListener("click", function () {
                toggleValidationPanel(button.dataset.bracketValidationMode);
            });
        });

        initDragScroller(scroller);
        window.addEventListener("resize", rerender);
        applyBracketZoom(bracketZoom);

        const api = {
            load: loadBracket,
            reload: loadBracket,
            rerender: rerender,
            setZoom: applyBracketZoom,
            zoomIn: function () { return applyBracketZoom(bracketZoom + zoomStep); },
            zoomOut: function () { return applyBracketZoom(bracketZoom - zoomStep); },
            resetZoom: function () { return applyBracketZoom(defaultZoom); },
            getZoom: function () { return bracketZoom; },
            getPayload: function () { return latestPayload; },
            getLayout: function () { return latestLayout; },
            clearFocus: function () { clearBracketFocus(board); },
            toggleValidationPanel: toggleValidationPanel,
            runValidation: runBracketValidation,
            clearValidation: function () {
                clearBracketValidation(board);
                updateValidationSummary([]);
            }
        };

        page._adminTournamentBracketViewer = api;

        if (options.autoLoad !== false) {
            loadBracket();
        }

        return api;
    }

    window.AdminTournamentBracket = window.AdminTournamentBracket || {};
    window.AdminTournamentBracket.init = initBracketViewer;

    qsa("#adminTournamentBracketPage, [data-admin-tournament-bracket-page]").forEach(function (page) {
        if (page.dataset.adminBracketDefer === "true") {
            return;
        }

        initBracketViewer(page);
    });
})();
