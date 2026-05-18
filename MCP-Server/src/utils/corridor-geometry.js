/**
 * 走廊幾何計算模組
 * 用於計算走廊實際寬度 (支援任意角度)
 */

/**
 * 計算走廊實際寬度
 * @param {Array} segments - 邊界線段 [{StartX, StartY, EndX, EndY, Length}]
 * @param {Object} bbox - BoundingBox fallback {MinX, MinY, MaxX, MaxY}
 * @returns {Object} { width: number, method: string, details: object }
 */
export function calculateCorridorWidth(segments, bbox) {
    if (!segments || segments.length === 0) {
        return calculateBBoxWidth(bbox);
    }

    // 1. 找出最長的兩條平行線段
    const parallelPairs = findParallelSegments(segments);

    if (parallelPairs.length === 0) {
        console.warn('⚠️  找不到平行線段,使用 BoundingBox 估算');
        return calculateBBoxWidth(bbox);
    }

    // 2. 計算最短垂直距離
    const bestPair = parallelPairs[0];
    const width = calculatePerpendicularDistance(bestPair.seg1, bestPair.seg2);

    return {
        width,
        method: 'boundary_accurate',
        details: {
            segmentCount: segments.length,
            parallelPairs: parallelPairs.length,
            longestPairLength: Math.round(bestPair.avgLength)
        }
    };
}

/**
 * 找出平行線段對 (角度差 < 5°)
 * @param {Array} segments 
 * @returns {Array} 按平均長度排序的平行線段對
 */
function findParallelSegments(segments) {
    const ANGLE_THRESHOLD = 5; // 度
    const pairs = [];

    for (let i = 0; i < segments.length; i++) {
        for (let j = i + 1; j < segments.length; j++) {
            const seg1 = segments[i];
            const seg2 = segments[j];

            const angle1 = calculateAngle(seg1);
            const angle2 = calculateAngle(seg2);

            // 計算角度差 (考慮 0°/180° 的循環)
            let angleDiff = Math.abs(angle1 - angle2);
            if (angleDiff > 180) angleDiff = 360 - angleDiff;
            if (angleDiff > 90) angleDiff = 180 - angleDiff;

            if (angleDiff < ANGLE_THRESHOLD) {
                pairs.push({
                    seg1,
                    seg2,
                    avgLength: (seg1.Length + seg2.Length) / 2,
                    angleDiff
                });
            }
        }
    }

    // 按平均長度排序 (最長的平行線段對最可能是走廊兩側牆)
    return pairs.sort((a, b) => b.avgLength - a.avgLength);
}

/**
 * 計算線段角度 (0-360°)
 */
export function calculateAngle(seg) {
    const dx = seg.EndX - seg.StartX;
    const dy = seg.EndY - seg.StartY;
    let angle = Math.atan2(dy, dx) * 180 / Math.PI;
    if (angle < 0) angle += 360;
    return angle;
}

/**
 * 計算兩條平行線段之間的垂直距離
 * 使用點到線的距離公式
 */
function calculatePerpendicularDistance(seg1, seg2) {
    // 使用 seg1 的起點到 seg2 的垂直距離
    const distance = pointToLineDistance(
        { x: seg1.StartX, y: seg1.StartY },
        { x: seg2.StartX, y: seg2.StartY },
        { x: seg2.EndX, y: seg2.EndY }
    );

    return distance;
}

/**
 * 點到線的垂直距離
 * @param {Object} point - {x, y}
 * @param {Object} lineStart - {x, y}
 * @param {Object} lineEnd - {x, y}
 */
export function pointToLineDistance(point, lineStart, lineEnd) {
    const dx = lineEnd.x - lineStart.x;
    const dy = lineEnd.y - lineStart.y;

    // 線段長度
    const lineLength = Math.sqrt(dx * dx + dy * dy);

    if (lineLength === 0) {
        // 線段退化為點
        return Math.sqrt(
            Math.pow(point.x - lineStart.x, 2) +
            Math.pow(point.y - lineStart.y, 2)
        );
    }

    // 點到直線的距離公式: |ax + by + c| / sqrt(a^2 + b^2)
    // 直線方程: (y2-y1)x - (x2-x1)y + (x2-x1)y1 - (y2-y1)x1 = 0
    const numerator = Math.abs(
        dy * point.x - dx * point.y +
        dx * lineStart.y - dy * lineStart.x
    );

    return numerator / lineLength;
}

/**
 * BoundingBox 降級方法
 */
function calculateBBoxWidth(bbox) {
    if (!bbox) {
        return {
            width: 0,
            method: 'error',
            details: { error: 'No BoundingBox data' }
        };
    }

    const widthX = Math.abs(bbox.MaxX - bbox.MinX);
    const widthY = Math.abs(bbox.MaxY - bbox.MinY);

    return {
        width: Math.min(widthX, widthY),
        method: 'bbox_estimate',
        details: { widthX, widthY }
    };
}

// ============================================================
// 多區段走廊分析 (支援 L/T/十字型走廊)
// ============================================================

/**
 * 分析多區段走廊 (採用線段優先分析法)
 * 1. 線段篩選: 計算每條線段的 Ratio = 長度 / 寬度, 若 < 1.0 則剔除
 * 2. 配對分析: 對剩餘有效線段進行配對, 找出走廊區段
 * 
 * @param {Array} boundarySegments - 邊界線段
 * @param {number} minWidth - 最小寬度要求 (mm)
 * @returns {Object} 分析結果
 */
export function analyzeMultiSegmentCorridor(boundarySegments, minWidth = 1200) {
    if (!boundarySegments || boundarySegments.length === 0) {
        return {
            segments: [],
            minWidth: 0,
            allPass: false,
            failedSegments: [],
            error: 'No boundary segments'
        };
    }

    // 1. 線段預處理與篩選
    const analyzedSegments = boundarySegments.map((seg, index) => {
        // 找出與此線段平行的所有線段
        const parallelSegs = findParallelSegmentsForOne(seg, boundarySegments);

        let width = 0;
        let closestSeg = null;

        if (parallelSegs.length > 0) {
            // 計算距離並排序
            const distances = parallelSegs.map(pSeg => {
                const dist = calculatePerpendicularDistance(seg, pSeg);
                return { seg: pSeg, dist };
            })
                // 過濾掉極近的線段 (例如共線的牆壁分段, 距離接近0)
                // 這些不是走廊的寬度, 而是同一側牆壁的延伸
                .filter(d => d.dist > 100) // 100mm 閾值
                .sort((a, b) => a.dist - b.dist);

            if (distances.length > 0) {
                // 取最近的距離
                width = distances[0].dist;
                closestSeg = distances[0].seg;
            }
        }

        const ratio = width > 0 ? seg.Length / width : 0;

        return {
            ...seg,
            originalIndex: index,
            width,
            ratio,
            closestSeg,
            isValid: ratio >= 1.0 // 長寬比 >= 1.0 視為有效長邊
        };
    });

    const validSegments = analyzedSegments.filter(s => s.isValid);

    if (validSegments.length === 0) {
        return {
            segments: [],
            minWidth: 0,
            allPass: false,
            failedSegments: [],
            error: 'No valid corridor segments found (all segments are short edges)'
        };
    }

    // 2. 建立走廊區段 (由有效線段配對而成)
    const corridorSegments = [];
    const processedPairs = new Set(); // 避免重複配對

    validSegments.forEach(seg1 => {
        // 找出所有與 seg1 平行的有效線段
        const validParallelSegs = validSegments.filter(s =>
            s.originalIndex !== seg1.originalIndex &&
            isParallel(seg1, s)
        );

        validParallelSegs.forEach(seg2 => {
            // 產生唯一 ID 避免重複
            const pairId = [seg1.originalIndex, seg2.originalIndex].sort().join('-');

            if (!processedPairs.has(pairId)) {
                // 檢查投影重疊 (確認線段面對面)
                if (checkProjectionOverlap(seg1, seg2)) {
                    processedPairs.add(pairId);

                    const width = calculatePerpendicularDistance(seg1, seg2);

                    // 再次過濾極近距離 (雙重保險)
                    if (width > 100) {
                        // 計算區段中心點
                        const centerPoint = {
                            x: (seg1.StartX + seg1.EndX + seg2.StartX + seg2.EndX) / 4,
                            y: (seg1.StartY + seg1.EndY + seg2.StartY + seg2.EndY) / 4
                        };

                        // 計算長度 (取平均長度)
                        const length = (seg1.Length + seg2.Length) / 2;

                        // 計算方向
                        const direction = calculateAngle(seg1);

                        corridorSegments.push({
                            segmentIndex: corridorSegments.length,
                            width: Math.round(width * 10) / 10,
                            direction: Math.round(direction),
                            length: Math.round(length),
                            status: width >= minWidth ? 'PASS' : 'FAIL',
                            centerPoint,
                            boundaries: [seg1, seg2]
                        });
                    }
                }
            }
        });
    });

    if (corridorSegments.length === 0) {
        return {
            segments: [],
            minWidth: 0,
            allPass: false,
            failedSegments: [],
            error: 'No valid parallel pairs found among long edges'
        };
    }

    const minWidthValue = Math.min(...corridorSegments.map(r => r.width));
    const failedSegments = corridorSegments.filter(r => r.status === 'FAIL');

    return {
        segments: corridorSegments,
        minWidth: minWidthValue,
        allPass: failedSegments.length === 0,
        failedSegments,
        totalSegments: corridorSegments.length,
        debugInfo: {
            totalSegments: analyzedSegments.length,
            validSegments: validSegments.length,
            rejectedSegments: analyzedSegments.filter(s => !s.isValid).map(s => ({
                index: s.originalIndex,
                length: Math.round(s.Length),
                width: Math.round(s.width),
                ratio: s.ratio.toFixed(2)
            }))
        }
    };
}

/**
 * 找出與特定線段平行的所有線段
 */
function findParallelSegmentsForOne(targetSeg, allSegments) {
    return allSegments.filter(seg => {
        if (seg === targetSeg) return false;

        return isParallel(targetSeg, seg);
    });
}

/**
 * 判斷兩線段是否平行
 */
function isParallel(seg1, seg2) {
    const ANGLE_THRESHOLD = 5; // 允許 5 度誤差
    const angle1 = calculateAngle(seg1);
    const angle2 = calculateAngle(seg2);

    let angleDiff = Math.abs(angle1 - angle2);
    if (angleDiff > 180) angleDiff = 360 - angleDiff;
    if (angleDiff > 90) angleDiff = 180 - angleDiff;

    return angleDiff < ANGLE_THRESHOLD;
}

/**
 * 檢查兩線段是否有投影重疊
 * 用於確認兩平行線段是否真正「面對面」構成走廊
 */
function checkProjectionOverlap(seg1, seg2) {
    // 簡化版: 暫時回傳 true
    return true;
}
