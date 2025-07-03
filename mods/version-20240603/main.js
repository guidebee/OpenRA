import Random from './random.js';
import PriorityArray from './priority-array.js';

window.debugUtils = {
    Random,
};

let ready = false;
let running = false;
let dirty = true;
let info;
let entityInfo;
const codeMap = {};

const debugDiv = document.getElementById("debug");

function die(err) {
    throw new Error(err);
}

function framePreview(color) {
    const canvas = document.getElementById("canvas");
    canvas.style.borderColor = color;
}

function blankPreview(color) {
    const canvas = document.getElementById("canvas");
    const ctx = canvas.getContext("2d");
    ctx.fillStyle = color;
    ctx.fillRect(0, 0, canvas.width, canvas.height);
}

function breakpoint() {}

async function progress(status) {
    const statusLine = document.getElementById("status-line");
    statusLine.textContent = status;
    console.log(status);
    await new Promise((resolve, reject) => {
        requestAnimationFrame(resolve);
    });
}

function dump2d(label, data, w, h, points) {
    const canvas = document.createElement("canvas");
    canvas.width = w;
    canvas.height = h;
    canvas.style.width = `${w*6}px`;
    canvas.style.height = `${h*6}px`;
    const ctx = canvas.getContext("2d");
    const min = Math.min(...data.map(v => (Number.isNaN(v) ? Infinity : v)));
    const max = Math.max(...data.map(v => (Number.isNaN(v) ? -Infinity : v)));
    const stretch = Math.max(-min, max);
    const hasNaN = data.some(v => Number.isNaN(v));
    for (let y = 0; y < h; y++) {
        for (let x = 0; x < w; x++) {
            let v = data[y * w + x];
            const r =
                  Number.isNaN(v) ? 255
                                  : v < 0 ? (255 * -v / stretch)
                                          : 0;
            const g =
                  Number.isNaN(v) ? 255
                                  : v > 0 ? (255 * v / stretch)
                                          : 0;
            const b = (((x & 4) ^ (y & 4)) ? 1 : 0) * (((x & 16) ^ (y & 16)) ? 96 : 64);
            ctx.fillStyle = `rgb(${r},${g},${b})`;
            ctx.fillRect(x, y, 1, 1);
        }
    }
    if (points ?? null !== null) {
        for (let point of points) {
            ctx.fillStyle = point.debugColor ?? "white";
            ctx.beginPath();
            ctx.arc(point.x + 0.5, point.y + 0.5, point.debugRadius ?? 1, 0, Math.PI * 2);
            ctx.fill();
        }
    }
    const log = document.createElement("pre");
    log.textContent = `${label}: ${w} * ${h}; ${min} to ${max}${hasNaN ? " !!!contains NaN!!!" : ""}; ${(points ?? null !== null) ? points.length : "[n/a]"} entities`;
    debugDiv.append(log);
    debugDiv.append(canvas);
}

const EXTERNAL_BIAS = 1000000;

// Replaceability like this isn't a perfect system. It can produce
// tiling errors if a template is only partially targeted for
// replacement. However, this is fairly rare and not a huge concern.

// Area cannot be replaced by a tile or obstructing entity.
const REPLACEABILITY_NONE = 0;
// Area must be replaced by a different tile, and may optionally be given an entity.
const REPLACEABILITY_TILE = 1;
// Area must be given an entity, but the underlying tile must not change.
const REPLACEABILITY_ENTITY = 2;
// Area can be replaced by a tile and/or entity.
const REPLACEABILITY_ANY = 3;

// Area is unplayable by land/naval units.
const PLAYABILITY_UNPLAYABLE = 0;
// Area is unplayable by land/naval units, but should count as being "within" a playable region.
// This usually applies to random rock or river tiles in largely passable templates.
const PLAYABILITY_PARTIAL = 1;
// Area is playable by either land or naval units.
const PLAYABILITY_PLAYABLE = 2;

const DEGREES_0   = 0;
const DEGREES_90  = Math.PI * 0.5;
const DEGREES_180 = Math.PI * 1;
const DEGREES_270 = Math.PI * 1.5;
const DEGREES_360 = Math.PI * 2;
const DEGREES_120 = Math.PI * (2 / 3);
const DEGREES_240 = Math.PI * (4 / 3);

const COS_0   = 1;
const COS_90  = 0;
const COS_180 = -1;
const COS_270 = 0;
const COS_360 = 1;
const COS_120 = -0.5;
const COS_240 = -0.5;

const SIN_0   = 0;
const SIN_90  = 1;
const SIN_180 = 0;
const SIN_270 = -1;
const SIN_360 = 0;
const SIN_120 = 0.86602540378443864676;
const SIN_240 = -0.86602540378443864676;

function cosSnap(angle) {
    switch (angle) {
    case DEGREES_0:   return COS_0;
    case DEGREES_90:  return COS_90;
    case DEGREES_180: return COS_180;
    case DEGREES_270: return COS_270;
    case DEGREES_360: return COS_360;
    case DEGREES_120: return COS_120;
    case DEGREES_240: return COS_240;
    default: return Math.cos(angle);
    }
}
function sinSnap(angle) {
    switch (angle) {
    case DEGREES_0:   return SIN_0;
    case DEGREES_90:  return SIN_90;
    case DEGREES_180: return SIN_180;
    case DEGREES_270: return SIN_270;
    case DEGREES_360: return SIN_360;
    case DEGREES_120: return SIN_120;
    case DEGREES_240: return SIN_240;
    default: return Math.sin(angle);
    }
}

const DIRECTION_R  = 0;
const DIRECTION_RD = 1;
const DIRECTION_D  = 2;
const DIRECTION_LD = 3;
const DIRECTION_L  = 4;
const DIRECTION_LU = 5;
const DIRECTION_U  = 6;
const DIRECTION_RU = 7;
const DIRECTION_NONE = -1;

function letterToDirection(letter) {
    switch (letter) {
    case 'R' : return DIRECTION_R;
    case 'RD': return DIRECTION_RD;
    case 'D' : return DIRECTION_D;
    case 'LD': return DIRECTION_LD;
    case 'L' : return DIRECTION_L;
    case 'LU': return DIRECTION_LU;
    case 'U' : return DIRECTION_U;
    case 'RU': return DIRECTION_RU;
    case 'N':  return DIRECTION_NONE;
    default: die('Bad direction letter: ' + letter);
    }
}

function mirrorXY(x, y, size, mirror) {
    switch (mirror) {
    case 0:
        die("avoid calling mirrorXY for mirror === 0");
    case 1:
        return [           x, size - 1 - y];
    case 2:
        return [           y,            x];
    case 3:
        return [size - 1 - x,            y];
    case 4:
        return [size - 1 - y, size - 1 - x];
    default:
        die("bad mirror direction");
    }
}

// Perlin noise may not be the best. It is not isotropic. (It has grid-related artifacts.)
function perlinNoise2d(random, size) {
    const noise = new Float32Array(size * size).fill(0.0);
    const vecX = new Float32Array(size * size);
    const vecY = new Float32Array(size * size);
    // Unit length divided by number of dot products to do.
    const D = 1 / 4;
    for (let y = 0; y <= size; y++) {
        for (let x = 0; x <= size; x++) {
            const phase = 2 * Math.PI * random.f32x();
            const vx = Math.cos(phase);
            const vy = Math.sin(phase);
            if (x > 0 && y > 0) {
                noise[(y-1) * size + (x-1)] += vx * -D + vy * -D;
            }
            if (x < size && y > 0) {
                noise[(y-1) * size + (x  )] += vx *  D + vy * -D;
            }
            if (x > 0 && y < size) {
                noise[(y  ) * size + (x-1)] += vx * -D + vy *  D;
            }
            if (x < size && y < size) {
                noise[(y  ) * size + (x  )] += vx *  D + vy *  D;
            }
        }
    }
    return noise;
}

function arrayQuantile (array, q) {
    array.length > 0 || die("Cannot get quantile of empty array");
    let i = q * (array.length - 1);
    if (i < 0) {
        i = 0;
    }
    if (i > array.length - 1) {
        i = array.length - 1;
    }
    const l = i | 0;
    if (l === i) {
        return array[l];
    }
    const u = l + 1;
    const w = i - l;
    const v = array[l] * (1-w) + array[u] * w;
    return v;
};

function interpolate2d(grid, w, h, x, y) {
    let xa = Math.floor(x) | 0;
    let xb = Math.ceil(x) | 0;
    let ya = Math.floor(y) | 0;
    let yb = Math.ceil(y) | 0;
    const xbw = x - xa;
    const ybw = y - ya;
    const xaw = 1.0 - xbw;
    const yaw = 1.0 - ybw;
    if (xa < 0) {
        xa = 0;
        xb = 0;
    } else if (xb > w - 1) {
        xa = w - 1;
        xb = w - 1;
    }
    if (ya < 0) {
        ya = 0;
        yb = 0;
    } else if (yb > w - 1) {
        ya = w - 1;
        yb = w - 1;
    }
    const naa = grid[ya * w + xa];
    const nba = grid[ya * w + xb];
    const nab = grid[yb * w + xa];
    const nbb = grid[yb * w + xb];
    return (naa * xaw + nba * xbw) * yaw + (nab * xaw + nbb * xbw) * ybw;
}

function fractalNoise2d(params) {
    const random = params.random ?? die("need random");
    const size = params.size ?? die("need size");
    // Arguably "feature length"s
    let wavelengths = params.wavelengths;
    if (!wavelengths) {
        wavelengths = new Float32Array(Math.log2(size));
        for (let i = 0; i < wavelengths.length; i++) {
            wavelengths[i] = (1 << i) * (params.wavelengthScale ?? 1.0);
        }
    }
    const amp_func = params.amp_func ?? ((wavelength) => (wavelength / size / wavelengths.length));

    const noise = new Float32Array(size * size).fill(0);
    for (let wavelength of wavelengths) {
        const amps = amp_func(wavelength);
        const sub_size = ((size / wavelength) | 0) + 2;
        const sub_noise = perlinNoise2d(random, sub_size);
        // Offsets should align to grid.
        // (current implementation has bias.)
        const offsetX = (random.f32x() * wavelength) | 0;
        const offsetY = (random.f32x() * wavelength) | 0;
        for (let y = 0; y < size; y++) {
            for (let x = 0; x < size; x++) {
                noise[y * size + x] += 
                    amps * interpolate2d(sub_noise,
                                         sub_size,
                                         sub_size,
                                         (offsetX + x) / wavelength,
                                         (offsetY + y) / wavelength);
            }
        }
    }

    return noise;
}

function fractalNoise2dWithSymetry(params) {
    const modded_params = Object.assign({}, params);
    // Need higher resolution due to cropping and rotation artifacts
    const size = params.size ?? die("need size");
    const template_size = size * 2 + 2;
    modded_params.size = template_size;
    const template = fractalNoise2d(modded_params);
    const rotations = params.rotations ?? 2;
    let noise = new Float32Array(size * size);
    // This -1 is required to compensate for the top-left vs the center of a grid square 
    const o = (size - 1) / 2;
    const to = template_size / 2;
    if (rotations < 1) {
        die("rotations must be >= 1");
    }
    for (let rotation = 0; rotation < rotations; rotation++) {
        const angle = rotation * 2 * Math.PI / rotations;
        const cos_angle = cosSnap(angle);
        const sin_angle = sinSnap(angle);
        for (let y = 0; y < size; y++) {
            for (let x = 0; x < size; x++) {
                // xy # corner noise space
                // xy - o # middle noise space
                // (xy - o) * Math.SQRT2 # middle temp space
                // R * ((xy - o) * Math.SQRT2) # middle temp space rotate
                // R * ((xy - o) * Math.SQRT2) + to # corner temp space rotate
                const mtx = (x - o) * Math.SQRT2;
                const mty = (y - o) * Math.SQRT2;
                const tx = (mtx * cos_angle - mty * sin_angle) + to;
                const ty = (mtx * sin_angle + mty * cos_angle) + to;
                noise[y * size + x] +=
                    interpolate2d(
                        template,
                        template_size,
                        template_size,
                        tx,
                        ty
                    ) / rotations;
            }
        }
    }
    if ((params.mirror ?? 0) !== 0) {
        const unmirrored = noise;
        noise = new Float32Array(size * size);
        for (let y = 0; y < size; y++) {
            for (let x = 0; x < size; x++) {
                const [tx, ty] = mirrorXY(x, y, size, params.mirror);
                noise[y * size + x] = unmirrored[y * size + x] + unmirrored[ty * size + tx];
            }
        }
    }
    return noise;
}

function terrainColor(terrainType) {
    return ("#" + info.Tileset.Terrain["TerrainType@"+terrainType].Color) ?? die("bad terrain type");
}

function writeU8(buffer, i, value) {
    buffer[i] = value & 0xff;
}
function writeU16(buffer, i, value) {
    buffer[i] = value & 0xff;
    buffer[i+1] = (value >> 8) & 0xff;
}
function writeU32(buffer, i, value) {
    buffer[i] = value & 0xff;
    buffer[i+1] = (value >> 8) & 0xff;
    buffer[i+2] = (value >> 16) & 0xff;
    buffer[i+3] = (value >> 24) & 0xff;
}

function calibrateHeightInPlace(values, target, fraction) {
    const sorted = values.slice().sort();
    const adjustment = target - arrayQuantile(sorted, fraction);
    for (let i = 0; i < values.length; i++) {
        values[i] += adjustment;
    }
}

function calculateRoominess(elevations, size, roomyEdges) {
    roomyEdges ??= false;
    const roominess = new Int32Array(size * size);
    // This could be more efficient, but this is just a PoC.
    let current = null;
    let next = [];
    // Find shores and map boundary
    for (let cy = 0; cy < size; cy++) {
        for (let cx = 0; cx < size; cx++) {
            let pCount = 0;
            let nCount = 0;
            for (let oy = -1; oy <= 1; oy++) {
                for (let ox = -1; ox <= 1; ox++) {
                    let x = cx + ox;
                    let y = cy + oy;
                    if (x < 0 || x >= size || y < 0 || y >= size) {
                        // Boundary
                    } else if (elevations[y * size + x] >= 0) {
                        pCount++;
                    } else {
                        nCount++;
                    }
                }
            }
            if (roomyEdges && nCount + pCount !== 9) {
                continue;
            }
            if (pCount !== 9 && nCount !== 9) {
                roominess[cy * size + cx] = (elevations[cy * size + cx] >= 0 ? 1 : -1);
                next.push({x:cx, y:cy});
            }
        }
    }
    if (next.length === 0) {
        // There were no shores. Use size or -size as appropriate.
        roominess.fill(elevations[0] >= 0 ? size : -size);
        return roominess;
    }
    let distance = 2;
    while (next.length !== 0) {
        current = next;
        next = [];
        for (let point of current) {
            let cx = point.x;
            let cy = point.y;
            for (let oy = -1; oy <= 1; oy++) {
                for (let ox = -1; ox <= 1; ox++) {
                    if (ox === 0 && oy === 0) {
                        continue;
                    }
                    let x = cx + ox;
                    let y = cy + oy;
                    if (x < 0 || x >= size || y < 0 || y >= size) {
                        continue;
                    }
                    if (roominess[y * size + x] !== 0) {
                        continue;
                    }
                    roominess[y * size + x] = (elevations[y * size + x] >= 0 ? distance : -distance);
                    next.push({x, y});
                }
            }
        }
        distance++;
    }

    return roominess;
}

// Find the (x,y) of one of the roomiest spaces.
function findRandomMax(random, values, size, cap) {
    cap ??= Infinity;
    let candidates = [];
    let best = -Infinity;
    size ?? die("size required");
    for (let i = 0; i < values.length; i++) {
        if (best < cap && values[i] > best) {
            if (values[i] >= cap) {
                best = cap;
            } else {
                best = values[i];
            }
            candidates = [];
        }
        if (values[i] === best) {
            candidates.push(i);
        }
    }
    const choice = random.pick(candidates);
    const y = (choice / size) | 0;
    const x = choice % size;
    return {x, y, value: best};
}

function rotateAndMirror(originals, size, rotations, mirror) {
    const projections = [];
    // This -1 is required to compensate for the top-left vs the center of a grid square.
    const o = (size - 1) / 2;
    if (rotations < 1) {
        die("rotations must be >= 1");
    }
    for (let original of originals) {
        for (let rotation = 0; rotation < rotations; rotation++) {
            const angle = rotation * 2 * Math.PI / rotations;
            const cos_angle = cosSnap(angle);
            const sin_angle = sinSnap(angle);
            const relOrigX = original.x - o;
            const relOrigY = original.y - o;
            const projX = Math.round((relOrigX * cos_angle - relOrigY * sin_angle) + o) | 0;
            const projY = Math.round((relOrigX * sin_angle + relOrigY * cos_angle) + o) | 0;
            if (projX < 0 || projX >= size || projY < 0 || projY >= size) {
                die("Rotation projection is out of bounds. Check rotations setting is acceptable.");
            }
            projections.push(Object.assign({}, original, {x: projX, y: projY, original: original}));

            if (mirror ?? 0 !== 0) {
                const [mx, my] = mirrorXY(projX, projY, size, mirror);
                projections.push(Object.assign({}, original, {x: mx, y: my, original: original}));
            }
        }
    }
    return projections;
}

function calculateSpawnPreferences(roominess, size, centralReservation, spawnRegionSize, mirror) {
    const preferences = roominess.map(r => Math.min(r, spawnRegionSize));
    const centralReservationSq = centralReservation * centralReservation;
    
    // This -1 is required to compensate for the top-left vs the center of a grid square.
    const o = (size - 1) / 2;

    // Mark areas close to the center or mirror lines as last resort.
    for (let y = 0; y < size; y++) {
        for (let x = 0; x < size; x++) {
            if (preferences[y * size + x] <= 1) {
                continue;
            }
            if (mirror ?? 0 !== 0) {
                switch (mirror) {
                case 1:
                    if (Math.abs(y - o) <= centralReservation) {
                        preferences[y * size + x] = 1;
                    }
                    break;
                case 2:
                    if (Math.abs(x - y) <= centralReservation * Math.SQRT2) {
                        preferences[y * size + x] = 1;
                    }
                    break;
                case 3:
                    if (Math.abs(x - o) <= centralReservation) {
                        preferences[y * size + x] = 1;
                    }
                    break;
                case 4:
                    if (Math.abs((size - 1 - x) - y) <= centralReservation * Math.SQRT2) {
                        preferences[y * size + x] = 1;
                    }
                    break;
                default:
                    die("bad mirror direction");
                }
            } else {
                const rx = x - o;
                const ry = y - o;
                if (rx*rx + ry*ry <= centralReservationSq) {
                    preferences[y * size + x] = 1;
                }
            }
        }
    }

    return preferences;
}

function reserveCircleInPlace(grid, size, cx, cy, r, setTo, invert) {
    invert ??= false;
    let minX;
    let minY;
    let maxX;
    let maxY;
    if (invert) {
        minX = 0;
        minY = 0;
        maxX = size - 1;
        maxY = size - 1;
    } else {
        minX = cx - r;
        minY = cy - r;
        maxX = cx + r;
        maxY = cy + r;
        if (minX < 0) { minX = 0; }
        if (minY < 0) { minX = 0; }
        if (maxX >= size) { maxX = size - 1; }
        if (maxY >= size) { maxY = size - 1; }
    }
    const rSq = r * r;
    for (let y = minY; y <= maxY; y++) {
        for (let x = minX; x <= maxX; x++) {
            const rx = x - cx;
            const ry = y - cy;
            const thisRSq = rx*rx + ry*ry;
            if (rx*rx + ry*ry <= rSq !== invert) {
                if (typeof(setTo) === "function") {
                    grid[y * size + x] = setTo(thisRSq, grid[y * size + x]);
                } else {
                    grid[y * size + x] = setTo;
                }
            }
        }
    }
}

function zoneColor(type) {
    switch(type) {
    case "mine":
        return "#ff8000";
    case "gmine":
        return "#8040ff";
    default:
        die(`unknown zone type "${type}"`);
    }
}

function generateFeatureRing(random, location, type, radius1, radius2, params) {
    radius1 ?? die("bad radius1");
    Number.isNaN(radius1) && die("radius1 is NaN");
    radius2 ?? die("bad radius2");
    Number.isNaN(radius2) && die("radius2 is NaN");
    radius1 <= radius2 || die("radius1 was greater than radius2");
    const features = [];
    const ring = [];
    const radius = (radius1 + radius2) / 2;
    const circumference = (radius * Math.PI * 2);
    let ringBudget = circumference | 0;
    const randomMineType = function() {
        if (random.f32x() < params.gemUpgrade) {
            return "gmine";
        } else {
            return "mine";
        }
    }
    switch (type) {
    case "spawn":
        for (let i = 0; i < params.spawnMines; i++) {
            const feature = {
                type: randomMineType(),
                radius: params.spawnOre,
                size: params.spawnOre * 2 - 1,
            };
            ring.push(feature);
            ringBudget -= feature.size;
        }
        break;
    case "expansion":
        const mines = 1 + ((random.f32x() * circumference * params.expansionMines) | 0);
        for (let i = 0; i < mines && ringBudget > 0; i++) {
            const radius = (random.f32x() * params.expansionOre) | 0;
            const feature = {
                type: randomMineType(),
                radius,
                size: radius * 2 - 1,
            };
            ring.push(feature);
            ringBudget -= feature.size;
        }
        break;
    case "empty":
        break;
    default:
        die("Bad feature ring type");
    }
    while (ringBudget > 0) {
        const feature = {
            type: "spacer",
            radius: 1,
            size: 1,
        };
        ring.push(feature);
        ringBudget -= feature.size;
    }

    random.shuffleInPlace(ring);
    // The feature list always starts on a boundary, so avoid that
    // bias by using a random start angle.
    let angle = random.f32x() * Math.PI * 2;
    let anglePerUnit = Math.PI * 2 / circumference;
    for (let feature of ring) {
        switch (feature.type) {
        case "spacer":
            angle += feature.radius * anglePerUnit;
            break;
        case "mine":
        case "gmine":
            {
                angle += feature.radius * anglePerUnit;
                // This may create an inward density bias.
                const r =
                      radius2 - radius1 <= feature.size
                      ? (radius1 + radius2) / 2
                      : feature.radius + radius1 + random.f32x() * (radius2 - radius1 - feature.radius * 2);
                const rx = r * Math.cos(angle);
                const ry = r * Math.sin(angle);
                features.push({
                    x: Math.round(location.x + rx) | 0,
                    y: Math.round(location.y + ry) | 0,
                    type: feature.type,
                    radius: feature.radius,
                    debugRadius: feature.radius,
                    debugColor: zoneColor(feature.type),
                });
                angle += (feature.radius - 1) * anglePerUnit;
            }
            break;
        }
    }
    return features;
}

function gaussianKernel1D(radius, standardDeviation) {
    const size = radius * 2 + 1;
    const kernel = new Float32Array(size);
    const dsd2 = 2 * (standardDeviation**2);
    let total = 0;
    for (let x = -radius; x <= radius; x++) {
        const value = Math.exp(-(x**2 / dsd2));
        kernel[x + radius] = value;
        total += value;
    }
    // Instead of dividing by Math.sqrt(Math.PI * dsd2), divide by the total.
    for (let i = 0; i < size; i++) {
        kernel[i] /= total;
    }
    return kernel;
}

function kernelBlur(input, size, kernel, kernelW, kernelH, kernelX, kernelY) {
    const output = new Float32Array(size * size);
    for (let cy = 0; cy < size; cy++) {
        for (let cx = 0; cx < size; cx++) {
            let total = 0;
            let samples = 0;
            for (let ky = 0; ky < kernelH; ky++) {
                for (let kx = 0; kx < kernelW; kx++) {
                    const x = cx + kx - kernelX;
                    const y = cy + ky - kernelY;
                    if (x < 0 || x >= size || y < 0 || y >= size) {
                        continue;
                    }
                    total += input[y * size + x] * kernel[ky * kernelW + kx];
                    samples++;
                }
            }
            output[cy * size + cx] = total / samples;
        }
    }
    return output;
}

function medianBlur(input, size, radius, extendOut, threshold) {
    extendOut ??= false;
    const halfThreshold = (threshold ?? 0) / 2;
    const output = new Float32Array(size * size);
    let changes = 0;
    let signChanges = 0;
    for (let cy = 0; cy < size; cy++) {
        for (let cx = 0; cx < size; cx++) {
            const ci = cy * size + cx;
            const values = [];
            for (let oy = -radius; oy <= radius; oy++) {
                for (let ox = -radius; ox <= radius; ox++) {
                    let x = cx + ox;
                    let y = cy + oy;
                    if (extendOut) {
                        if (x >= size) x = size - 1;
                        if (x < 0) x = 0;
                        if (y >= size) y = size - 1;
                        if (y < 0) y = 0;
                    } else {
                        if (x < 0 || x >= size || y < 0 || y >= size) {
                            continue;
                        }
                    }
                    const i = y * size + x;
                    values.push(input[i]);
                }
            }
            values.sort((a, b) => (a - b));
            if (threshold !== 0) {
                const l = arrayQuantile(values, 0.5 - halfThreshold);
                const u = arrayQuantile(values, 0.5 + halfThreshold);
                if (l <= input[ci] && input[ci] <= u) {
                    output[ci] = input[ci];
                    continue;
                }
            }
            output[ci] = arrayQuantile(values, 0.5);
            changes++;
            if (Math.sign(output[ci]) !== Math.sign(input[ci])) {
                signChanges++;
            }
        }
    }
    return [output, changes, signChanges];
}

function erodeAndDilate(input, size, foregroundLand, width) {
    const foreground = foregroundLand ? 1 : -1;
    const output = new Float32Array(size * size).fill(foregroundLand ? -1 : 1);
    const sizeM1 = size - 1;
    for (let cy = 1 - width; cy < size; cy++) {
        center: for (let cx = 1 - width; cx < size; cx++) {
            for (let ry = 0; ry < width; ry++) {
                for (let rx = 0; rx < width; rx++) {
                    const x = cx + rx;
                    const y = cy + ry;
                    if (x < 0 || x >= size || y < 0 || y >= size) {
                        continue;
                    }
                    if ((input[y * size + x] >= 0) !== foregroundLand) {
                        continue center;
                    }
                }
            }
            for (let ry = 0; ry < width; ry++) {
                for (let rx = 0; rx < width; rx++) {
                    const x = cx + rx;
                    const y = cy + ry;
                    if (x < 0 || x >= size || y < 0 || y >= size) {
                        continue;
                    }
                    output[y * size + x] = foreground;
                }
            }
        }
    }
    
    let changes = 0;
    for (let i = 0; i < input.length; i++) {
        if ((input[i] >= 0) !== (output[i] >= 0)) {
            changes++;
        }
    }
    return [output, changes];
}

function fixThinMassesInPlace(input, size, growLand, width) {
    const grow = growLand ? 1 : -1;
    const sizeM1 = size - 1;
    const cornerMaskSize = width + 1;
    // Zero means ignore.
    const cornerMask = new Uint32Array(cornerMaskSize * cornerMaskSize);

    // Diagonal steps cause problems for template path laying loop shortcuts. Disabled.
    const allowDiagonalSteps = false;
    for (let y = 0; y < cornerMaskSize; y++) {
        for (let x = 0; x < cornerMaskSize; x++) {
            cornerMask[y * cornerMaskSize + x] = (allowDiagonalSteps ? 0 : 1) + width + width - x - y;
        }
    }
    cornerMask[0] = 0;

    // Higher number indicates a thinner area.
    const thinness = new Uint32Array(size * size);
    const setThinness = function(x, y, v) {
        if (x < 0 || x >= size || y < 0 || y >= size) {
            return;
        }
        if ((input[y * size + x] >= 0) === growLand) {
            return;
        }
        thinness[y * size + x] = Math.max(v, thinness[y * size + x]);
    };
    for (let cy = 0; cy < size; cy++) {
        for (let cx = 0; cx < size; cx++) {
            if ((input[cy * size + cx] >= 0) === growLand) {
                // This isn't coastline.
                continue;
            }
            const l = (input[cy * size + Math.max(cx - 1, 0)] >= 0) === growLand;
            const r = (input[cy * size + Math.min(cx + 1, sizeM1)] >= 0) === growLand;
            const u = (input[Math.max(cy - 1, 0) * size + cx] >= 0) === growLand;
            const d = (input[Math.min(cy + 1, sizeM1) * size + cx] >= 0) === growLand;
            const lu = l && u;
            const ru = r && u;
            const ld = l && d;
            const rd = r && d;
            for (let ry = 0; ry < cornerMaskSize; ry++) {
                for (let rx = 0; rx < cornerMaskSize; rx++) {
                    if (rd) {
                        const x = cx + rx;
                        const y = cy + ry;
                        setThinness(x, y, cornerMask[ry * cornerMaskSize + rx]);
                    }
                    if (ru) {
                        const x = cx + rx;
                        const y = cy - ry;
                        setThinness(x, y, cornerMask[ry * cornerMaskSize + rx]);
                    }
                    if (ld) {
                        const x = cx - rx;
                        const y = cy + ry;
                        setThinness(x, y, cornerMask[ry * cornerMaskSize + rx]);
                    }
                    if (lu) {
                        const x = cx - rx;
                        const y = cy - ry;
                        setThinness(x, y, cornerMask[ry * cornerMaskSize + rx]);
                    }
                }
            }
        }
    }

    const thinnest = Math.max(...thinness);
    if (thinnest === 0) {
        // No fixes
        return [0, 0];
    }
    
    let changes = 0;
    for (let y = 0; y < size; y++) {
        for (let x = 0; x < size; x++) {
            const i = y * size + x;
            if (thinness[i] === thinnest) {
                input[i] = grow;
                changes++;
            }
        }
    }

    // Fixes made, with potentially more that can be in another pass.
    return [thinnest, changes];
}

function fixThinMassesInPlaceFull(input, size, growLand, width) {
    let thinnest;
    let changes;
    let changesAcc;
    [thinnest, changes] = fixThinMassesInPlace(input, size, growLand, width);
    changesAcc = changes;
    while (changes > 0) {
        [, changes] = fixThinMassesInPlace(input, size, growLand, width);
        changesAcc += changes;
    }
    return [thinnest, changesAcc];
}

// Finds the local variance of points in a 2d grid (using a square sample area).
// Sample areas are centered on data points, so output is size * size.
function variance2d(input, size, radius) {
    const output = new Float32Array(size * size);
    for (let cy = 0; cy < size; cy++) {
        for (let cx = 0; cx < size; cx++) {
            let total = 0;
            let samples = 0;
            for (let ry = -radius; ry <= radius; ry++) {
                for (let rx = -radius; rx <= radius; rx++) {
                    const y = cy + ry;
                    const x = cx + rx;
                    if (x < 0 || x >= size || y < 0 || y >= size) {
                        continue;
                    }
                    total += input[y * size + x];
                    samples++;
                }
            }
            const mean = total / samples;
            let sumOfSquares = 0;
            for (let ry = -radius; ry <= radius; ry++) {
                for (let rx = -radius; rx <= radius; rx++) {
                    const y = cy + ry;
                    const x = cx + rx;
                    if (x < 0 || x >= size || y < 0 || y >= size) {
                        continue;
                    }
                    sumOfSquares += (mean - input[y * size + x]) ** 2;
                }
            }
            output[cy * size + cx] = sumOfSquares / samples;
        }
    }
    return output;
}

// Finds the local variance of points in a 2d grid (using a square sample area).
// Sample areas are centered on data point corners, so output is (size + 1) * (size + 1).
function gridVariance2d(input, size, radius) {
    const outSize = size + 1;
    const output = new Float32Array(outSize * outSize);
    for (let cy = 0; cy <= size; cy++) {
        for (let cx = 0; cx <= size; cx++) {
            let total = 0;
            let samples = 0;
            for (let ry = -radius; ry < radius; ry++) {
                for (let rx = -radius; rx < radius; rx++) {
                    const y = cy + ry;
                    const x = cx + rx;
                    if (x < 0 || x >= size || y < 0 || y >= size) {
                        continue;
                    }
                    total += input[y * size + x];
                    samples++;
                }
            }
            const mean = total / samples;
            let sumOfSquares = 0;
            for (let ry = -radius; ry < radius; ry++) {
                for (let rx = -radius; rx < radius; rx++) {
                    const y = cy + ry;
                    const x = cx + rx;
                    if (x < 0 || x >= size || y < 0 || y >= size) {
                        continue;
                    }
                    sumOfSquares += (mean - input[y * size + x]) ** 2;
                }
            }
            output[cy * outSize + cx] = sumOfSquares / samples;
        }
    }
    return output;
}

function zip2(a, b, f) {
    a.length === b.length || die("arrays do not have equal length");
    const c = a.slice();
    for (let i = 0; i < c.length; i++) {
        c[i] = f(a[i], b[i], i);
    }
    return c;
}

async function fixTerrain(elevation, size, terrainSmoothing, smoothingThreshold, minimumThickness, bias, debugLabel) {
    if (typeof(debugLabel) === "undefined") {
        debugLabel = "(unlabelled)";
    }
    await progress(`${debugLabel}: fixing terrain anomalies: primary median blur`);
    // Make height discrete -1 and 1.
    let landmass = elevation.map(v => (v >= 0 ? 1 : -1));
    dump2d(`${debugLabel}: unsmoothed terrain`, landmass.map(v=>Math.sign(v)), size, size);
    // Primary smoothing
    [landmass, ] = medianBlur(landmass, size, terrainSmoothing ?? 0, true);
    dump2d(`${debugLabel}: smoothed terrain`, landmass.map(v=>Math.sign(v)), size, size);
    for (let i1 = 0; i1 < /*max passes*/16; i1++) {
        for (let i2 = 0; i2 < size; i2++) {
            await progress(`${debugLabel}: fixing terrain anomalies: ${i1}: threshold smoothing ${i2}`);
            let signChanges;
            let signChangesAcc = 0;
            for (let r = 1; r <= terrainSmoothing ?? 0; r++) {
                [landmass, , signChanges] = medianBlur(landmass, size, r, true, smoothingThreshold ?? 0.5);
                signChangesAcc += signChanges;
            }
            dump2d(`${debugLabel}: threshold smoothed terrain (round ${i1},${i2}: ${signChangesAcc} sign changes)`, landmass.map(v=>Math.sign(v)), size, size);
            if (signChangesAcc === 0) {
                break;
            }
        }
        let changesAcc = 0;
        let changes;
        let thinnest;
        await progress(`${debugLabel}: fixing terrain anomalies: ${i1}: erode and dilate pos`);
        [landmass, changes] = erodeAndDilate(landmass, size, true, minimumThickness);
        changesAcc += changes;
        dump2d(`${debugLabel}: erodeAndDilate pos (round ${i1}: ${changes} fixes)`, landmass.map(v=>Math.sign(v)), size, size);
        await progress(`${debugLabel}: fixing terrain anomalies: ${i1}: fix thin masses pos`);
        [thinnest, changes] = fixThinMassesInPlaceFull(landmass, size, true, minimumThickness);
        changesAcc += changes;
        dump2d(`${debugLabel}: fixThinMassesInPlace pos (round ${i1}: ${thinnest} tightness, ${changes} fixes)`, landmass.map(v=>Math.sign(v)), size, size);

        const midFixLandmass = landmass.slice();

        await progress(`${debugLabel}: fixing terrain anomalies: ${i1}: erode and dilate neg`);
        [landmass, changes] = erodeAndDilate(landmass, size, false, minimumThickness);
        changesAcc += changes;
        dump2d(`${debugLabel}: erodeAndDilate neg (round ${i1}: ${changes} fixes)`, landmass.map(v=>Math.sign(v)), size, size);
        await progress(`${debugLabel}: fixing terrain anomalies: ${i1}: fix thin masses neg`);
        [thinnest, changes] = fixThinMassesInPlaceFull(landmass, size, false, minimumThickness);
        changesAcc += changes;
        dump2d(`${debugLabel}: fixThinMassesInPlace neg (round ${i1}: ${thinnest} tightness, ${changes} fixes)`, landmass.map(v=>Math.sign(v)), size, size);
        if (changesAcc === 0) {
            break;
        }
        console.log(`${debugLabel}: Thinness corrections were made. Running extra passes.`);
        if (i1 >= 8 && i1 % 4 === 0) {
            console.log(`${debugLabel}: Struggling to stablize terrain. Leveling problematic regions.`);
            await progress(`${debugLabel}: fixing terrain anomalies: ${i1}: leveling problematic regions`);
            const diff = zip2(midFixLandmass, landmass, (a, b)=>(a!==b ? 1 : 0));
            dump2d(`${debugLabel}: unstable (round ${i1})`, diff, size, size);
            for (let y = 0; y < size; y++) {
                for (let x = 0; x < size; x++) {
                    const i = y * size + x;
                    if (diff[i] === 1) {
                        reserveCircleInPlace(landmass, size, x, y, minimumThickness * 2, bias);
                    }
                }
            }
            dump2d(`${debugLabel}: leveled (round ${i1})`, landmass, size, size);
        }
    }
    return landmass;
}

// Use to trace paths around terrain features
// The caller must set type information on the returned paths.
function zeroLinesToPaths(elevation, size) {
    // There is redundant memory/iteration, but I don't care enough.

    // These are really only the signs of the gradients.
    const gradientH = new Int8Array(size * size);
    const gradientV = new Int8Array(size * size);
    for (let y = 0; y < size; y++) {
        for (let x = 1; x < size; x++) {
            const i = y * size + x;
            const l = elevation[i-1] >= 0 ? 1 : 0;
            const r = elevation[i] >= 0 ? 1 : 0;
            gradientV[i] = r - l;
        }
    }
    for (let y = 1; y < size; y++) {
        for (let x = 0; x < size; x++) {
            const i = y * size + x;
            const u = elevation[i-size] >= 0 ? 1 : 0;
            const d = elevation[i] >= 0 ? 1 : 0;
            gradientH[i] = d - u;
        }
    }

    // Looping paths contain the start/end point twice.
    const paths = [];
    const tracePath = function(sx, sy, direction) {
        const points = [];
        let x = sx;
        let y = sy;
        points.push({x, y});
        do {
            switch (direction) {
            case DIRECTION_R:
                gradientH[y * size + x] = 0;
                x++;
                break;
            case DIRECTION_D:
                gradientV[y * size + x] = 0;
                y++;
                break;
            case DIRECTION_L:
                x--;
                gradientH[y * size + x] = 0;
                break;
            case DIRECTION_U:
                y--;
                gradientV[y * size + x] = 0;
                break;
            }
            points.push({x, y});
            const i = y * size + x;
            const r = x < size && gradientH[i] > 0;
            const d = y < size && gradientV[i] < 0;
            const l = x > 0 && gradientH[i - 1] < 0;
            const u = y > 0 && gradientV[i - size] > 0;
            if (direction == DIRECTION_R && u) {
                direction = DIRECTION_U;
            } else if (direction == DIRECTION_D && r) {
                direction = DIRECTION_R;
            } else if (direction == DIRECTION_L && d) {
                direction = DIRECTION_D;
            } else if (direction == DIRECTION_U && l) {
                direction = DIRECTION_L;
            } else if (r) {
                direction = DIRECTION_R;
            } else if (d) {
                direction = DIRECTION_D;
            } else if (l) {
                direction = DIRECTION_L;
            } else if (u) {
                direction = DIRECTION_U;
            } else {
                // Dead end (not a loop)
                break;
            }
        } while (x != sx || y != sy);
        paths.push({
            points,
        });
    };
    // Trace non-loops (from edge of map)
    for (let n = 1; n < size; n++) {
        {
            const x = n;
            const y = 0;
            if (gradientV[y * size + x] < 0) {
                tracePath(x, y, DIRECTION_D);
            }
        }
        {
            const x = n;
            const y = size - 1;
            if (gradientV[y * size + x] > 0) {
                tracePath(x, y+1, DIRECTION_U);
            }
        }
        {
            const x = 0;
            const y = n;
            if (gradientH[y * size + x] > 0) {
                tracePath(x, y, DIRECTION_R);
            }
        }
        {
            const x = size - 1;
            const y = n;
            if (gradientH[y * size + x] < 0) {
                tracePath(x+1, y, DIRECTION_L);
            }
        }
    }
    // Trace loops
    for (let y = 0; y < size; y++) {
        for (let x = 0; x < size; x++) {
            const i = y * size + x;
            if (gradientH[i] > 0) {
                tracePath(x, y, DIRECTION_R);
            } else if (gradientH[i] < 0) {
                tracePath(x+1, y, DIRECTION_L);
            }
            if (gradientV[i] < 0) {
                tracePath(x, y, DIRECTION_D);
            } else if (gradientV[i] > 0) {
                tracePath(x, y+1, DIRECTION_U);
            }
        }
    }

    return paths;
}

function maskPaths(paths, mask, gridSize) {
    const newPaths = [];
    const isGood = function(point) {
        return mask[point.y * gridSize + point.x] >= 0;
    };
    for (const path of paths) {
        const points = path.points;
        const isLoop = points[0].x === points[points.length-1].x && points[0].y === points[points.length-1].y;
        let firstBad;
        for (firstBad = 0; firstBad < points.length; firstBad++) {
            if (!isGood(points[firstBad])) {
                break;
            }
        }
        if (firstBad === points.length) {
            // The path is entirely within the mask already.
            newPaths.push(path);
            continue;
        }
        const startAt = isLoop ? firstBad : 0;
        let i = startAt;
        const wrapAt = isLoop ? points.length - 1 : points.length;
        if (wrapAt === 0) {
            // Single-point path?!
            die("single point paths should not exist");
        }
        startAt < wrapAt || die("assertion failure");
        let currentPath = null;;
        do {
            if (isGood(points[i])) {
                if (currentPath === null) {
                    currentPath = Object.assign({}, path, {points: []});
                }
                currentPath.points.push(points[i])
            } else {
                if (currentPath !== null) {
                    if (currentPath.points.length > 1) {
                        newPaths.push(currentPath);
                    }
                    currentPath = null;
                }
            }
            i++;
            if (i === wrapAt) {
                i = 0;
            }
        } while (i !== startAt);
        if (currentPath !== null) {
            if (currentPath.points.length > 1) {
                newPaths.push(currentPath);
            }
        }
    }
    return newPaths;
}

function tweakPath(path, size) {
    size ?? die("need size");
    const points = path.points;
    const len = path.points.length;
    const lst = len - 1;
    const tweakedPath = Object.assign({}, path);
    // tweakedPath.permittedTemplates = path.permittedTemplates;
    const isLoop = points[0].x === points[lst].x && points[0].y === points[lst].y;
    tweakedPath.isLoop = isLoop;
    if (isLoop) {
        // Closed loop. Find the longest straight
        // (nrlen excludes the repeated point at the end.)
        const nrlen = len - 1;
        let prevDim = -1;
        let scanStart = -1;
        let bestScore = -1;
        let bestBend = -1;
        let prevBend = -1;
        let prevI = 0;
        for (let i = 1;; i++) {
            if (i === nrlen) {
                i = 0;
            }
            const dim = points[i].x === points[prevI].x ? 1 : 0;
            if (prevDim !== -1 && prevDim !== dim) {
                if (scanStart === -1) {
                    // This is technically just after the bend. But that's fine.
                    scanStart = i;
                } else {
                    let score = prevI - prevBend;
                    if (score < 0) {
                        score += nrlen;
                    }
                    if (score > bestScore) {
                        bestBend = prevBend;
                        bestScore = score;
                    }
                    if (i === scanStart) {
                        break;
                    }
                }
                prevBend = prevI;
            }
            prevDim = dim;
            prevI = i;
        }
        const favouritePoint = (bestBend + (bestScore >> 1)) % nrlen;
        // Repeat the start at the end.
        tweakedPath.points = [...points.slice(favouritePoint, nrlen), ...points.slice(0, favouritePoint + 1)];
        tweakedPath.startDirN = calculateDirectionPoints(tweakedPath.points[0], tweakedPath.points[1]);
        tweakedPath.endDirN = calculateDirectionPoints(tweakedPath.points[0], tweakedPath.points[1]);
    } else {
        const extend = function(point, extensionLength) {
            const ox = (point.x === 0)    ? -1
                  : (point.x === size) ?  1
                  : 0;
            const oy = (point.y === 0)    ? -1
                  : (point.y === size) ?  1
                  : 0;
            if (ox === 0 && oy === 0) {
                // We're not on an edge, so don't extend.
                return [];
            }
            const extension = [];
            let newPoint = point;
            for (let i = 0; i < extensionLength; i++) {
                newPoint = Object.assign({}, point, {x: newPoint.x + ox, y: newPoint.y + oy});
                extension.push(newPoint);
            }
            return extension;
        };
        // Open paths. Extend if beyond edges.
        const startExt = extend(points[0], /*extensionLength=*/4).reverse();
        const endExt = extend(points[lst], /*extensionLength=*/4);
        tweakedPath.points = [...startExt, ...points, ...endExt];
        tweakedPath.startDirN = calculateDirectionPoints(tweakedPath.points[0], tweakedPath.points[1]);
        tweakedPath.endDirN = calculateDirectionPoints(tweakedPath.points[tweakedPath.points.length - 2], tweakedPath.points[tweakedPath.points.length - 1]);
    }
    return tweakedPath;
}

function calculateDirectionXY(dx, dy) {
    if (dx > 0) {
        if (dy > 0) {
            return DIRECTION_RD;
        } else if (dy < 0) {
            return DIRECTION_RU;
        } else {
            return DIRECTION_R;
        }
    } else if (dx < 0) {
        if (dy > 0) {
            return DIRECTION_LD;
        } else if (dy < 0) {
            return DIRECTION_LU;
        } else {
            return DIRECTION_L;
        }
        return DIRECTION_L;
    } else {
        if (dy > 0) {
            return DIRECTION_D;
        } else if (dy < 0) {
            return DIRECTION_U;
        } else {
            die("Bad direction");
        }
    }
}
function calculateDirectionPoints(now, next) {
    const dx = next.x - now.x;
    const dy = next.y - now.y;
    return calculateDirectionXY(dx, dy);
}

function reverseDirection(direction) {
    if (direction === DIRECTION_NONE) {
        return DIRECTION_NONE;
    }
    return direction ^ 4;
}

function paintTemplate(tiles, size, px, py, template) {
    for (const [tx, ty] of template.Shape) {
        const x = px + tx;
        const y = py + ty;
        if (x < 0 || x >= size || y < 0 || y >= size) {
            continue;
        }
        const ti = ty * template.SizeX + tx;
        const i = y * size + x;
        tiles[i] = `t${template.Id}i${ti}`;
    }
}

function tilePath(tiles, tilesSize, path, random, minimumThickness) {
    let minPointX = Infinity;
    let minPointY = Infinity;
    let maxPointX = -Infinity;
    let maxPointY = -Infinity;
    for (const point of path.points) {
        if (point.x < minPointX) {
            minPointX = point.x;
        }
        if (point.y < minPointY) {
            minPointY = point.y;
        }
        if (point.x > maxPointX) {
            maxPointX = point.x;
        }
        if (point.y > maxPointY) {
            maxPointY = point.y;
        }
    }
    const maxDeviation = (minimumThickness - 1) >> 1;
    minPointX -= maxDeviation;
    minPointY -= maxDeviation;
    maxPointX += maxDeviation;
    maxPointY += maxDeviation;
    const points = path.points.map(point => Object.assign({}, point, {x: point.x - minPointX, y: point.y - minPointY}));

    const isLoop = path.isLoop;

    // grid points (not squares), so these are offset 0.5 from tile centers.
    const sizeX = 1 + maxPointX - minPointX;
    const sizeY = 1 + maxPointY - minPointY;
    const sizeXY = sizeX * sizeY;

    const MAX_DEVIATION = 0xffffffff;
    // Bit masks of 8-angle directions which are considered a positive progress
    // traversal. Template choices with an overall negative progress traversal
    // are rejected.
    const directions = new Uint8Array(sizeXY).fill(0);
    // How far away from the path this point is.
    const deviations = new Uint32Array(sizeXY).fill(MAX_DEVIATION);
    // Bit masks of 8-angle directions which define whether it's permitted
    // to traverse from one point to a given neighbour.
    const traversables = new Uint8Array(sizeXY).fill(0);
    {
        const gradientX = new Int32Array(sizeXY).fill(0);
        const gradientY = new Int32Array(sizeXY).fill(0);
        for (let pointI = 0; pointI < points.length; pointI++) {
            if (isLoop && pointI == 0) {
                // Same as last point.
                continue;
            }
            const point = points[pointI];
            const pointPrevI = pointI - 1;
            const pointNextI = pointI + 1;
            let directionX = 0;
            let directionY = 0;
            if (pointNextI < points.length) {
                directionX += points[pointNextI].x - point.x;
                directionY += points[pointNextI].y - point.y;
            }
            if (pointPrevI >= 0) {
                directionX += point.x - points[pointPrevI].x;
                directionY += point.y - points[pointPrevI].y;
            }
            for (let deviation = 0; deviation <= maxDeviation; deviation++) {
                const minX = point.x - deviation;
                const minY = point.y - deviation;
                const maxX = point.x + deviation;
                const maxY = point.y + deviation;
                for (let y = minY; y <= maxY; y++) {
                    for (let x = minX; x <= maxX; x++) {
                        const i = y * sizeX + x;
                        if (deviation < deviations[i]) {
                            deviations[i] = deviation;
                        }
                        if (deviation === maxDeviation) {
                            gradientX[i] += directionX;
                            gradientY[i] += directionY;
                            if (x > minX) {
                                traversables[i] |= 1 << DIRECTION_L;
                            }
                            if (x < maxX) {
                                traversables[i] |= 1 << DIRECTION_R;
                            }
                            if (y > minY) {
                                traversables[i] |= 1 << DIRECTION_U;
                            }
                            if (y < maxY) {
                                traversables[i] |= 1 << DIRECTION_D;
                            }
                            if (x > minX && y > minY) {
                                traversables[i] |= 1 << DIRECTION_LU;
                            }
                            if (x > minX && y < maxY) {
                                traversables[i] |= 1 << DIRECTION_LD;
                            }
                            if (x > maxX && y > minY) {
                                traversables[i] |= 1 << DIRECTION_RU;
                            }
                            if (x > maxX && y < maxY) {
                                traversables[i] |= 1 << DIRECTION_RD;
                            }
                        }
                    }
                }
            }
        }
        // Probational
        for (let i = 0; i < sizeXY; i++) {
            if (gradientX[i] === 0 && gradientY[i] === 0) {
                directions[i] = 0;
                continue;
            }
            const direction = calculateDirectionXY(gradientX[i], gradientY[i]);
            //     direction: 0123456701234567
            //                 UUU DDD UUU DDD
            //                 R LLL RRR LLL R
            directions[i] = (0b100000111000001 >> (7 - direction)) & 0b11111111;
        }
    }

    const start = points[0];
    const end = points[points.length-1];
    const mainType = path.type ?? die ("Path is missing type");
    const mainTypeN = info.typeNs[mainType] ?? die(`Bad main type ${mainType}`);
    const startType = path.startType ?? mainType;
    const startTypeN = info.typeNs[startType] ?? die(`Bad start type ${startType}`);
    const endType = path.endType ?? mainType;
    const endTypeN = info.typeNs[endType] ?? die(`Bad end type ${endType}`);
    const startBorderN = info.borderNs[startTypeN][path.startDirN];
    const endBorderN = info.borderNs[endTypeN][path.endDirN];

    const templates = path.permittedTemplates ?? info.templatesByType[path.type] ?? die(`missing templatesByType entry for ${mainType}`);
;
    const templatesByStartBorder = [];
    const templatesByEndBorder = [];
    const MAX_SCORE = 0xffffffff;

    // Z refers to the "layer", mapped from a "border".
    // i  =              y * SizeX + x // i does not dictate a layer.
    // il = z * SizeXY + y * SizeX + x // il is for a specific layer.
    const borderToZ = [];
    const zToBorder = [];

    {
        const assignForBorder = function(border) {
            if (typeof(borderToZ[border]) !== "undefined") {
                return;
            }
            borderToZ[border] = zToBorder.length;
            zToBorder.push(border);
            templatesByStartBorder[border] = [];
            templatesByEndBorder[border] = [];
        };
        for (const template of templates) {
            assignForBorder(template.StartBorderN);
            assignForBorder(template.EndBorderN);
        }
        for (const template of templates) {
            templatesByStartBorder[template.StartBorderN].push(template);
            templatesByEndBorder[template.EndBorderN].push(template);
        }
    }

    const sizeXYZ = sizeXY * zToBorder.length;
    const priorities = new PriorityArray(sizeXYZ).fill(-Infinity);
    const scores = new Uint32Array(sizeXYZ).fill(MAX_SCORE);

    // Assumes both f and t are in the sizeX/sizeY bounds.
    // Lower (closer to zero) scores are better matches.
    // Higher scores are worse matches.
    // MAX_SCORE means totally unacceptable.
    const scoreTemplate = function(template, fx, fy) {
        const expectStartTypeN = (fx === start.x && fy === start.y) ? startTypeN : mainTypeN;
        if (template.StartTypeN !== expectStartTypeN) {
            return MAX_SCORE;
        }
        const expectEndTypeN = (fx + template.MovesX === end.x && fy + template.MovesY === end.y) ? endTypeN : mainTypeN;
        if (template.EndTypeN !== expectEndTypeN) {
            return MAX_SCORE;
        }
        let deviationAcc = 0;
        let progressionAcc = 0;
        const lastPointI = template.RelPathND.length - 1;
        for (let pointI = 0; pointI <= lastPointI; pointI++) {
            const point = template.RelPathND[pointI];
            const px = fx + point.x;
            const py = fy + point.y;
            const pi = py * sizeX + px;
            if (px < 0 || px >= sizeX || py < 0 || py >= sizeY) {
                // Intermediate point escapes array bounds.
                return MAX_SCORE;
            }
            if (pointI < lastPointI) {
                if ((traversables[pi] & point.dm) === 0) {
                    // Next point escapes traversable area.
                    return MAX_SCORE;
                }
                if ((directions[pi] & point.dm) === point.dm) {
                    progressionAcc++;
                } else if ((directions[pi] & point.dmr) === point.dmr) {
                    progressionAcc--;
                }
            }
            if (pointI > 0) {
                // Don't double-count the template's path's starts and ends
                deviationAcc += deviations[pi];
            }
        }
        if (progressionAcc < 0) {
            // It's moved backwards
            return MAX_SCORE;
        }
        // Satisfies all requirements.
        return deviationAcc;
    }

    const updateFrom = function(fx, fy, fb) {
        const fi = fy * sizeX + fx;
        const fil = borderToZ[fb] * sizeXY + fi;
        const fscore = scores[fil];
        template_loop: for (const template of templatesByStartBorder[fb]) {
            const tx = fx + template.MovesX;
            const ty = fy + template.MovesY;
            const ti = ty * sizeX + tx;
            if (tx < 0 || tx >= sizeX || ty < 0 || ty >= sizeY) {
                continue template_loop;
            }
            // Most likely to fail. Check first.
            if (deviations[ti] === MAX_DEVIATION) {
                // End escapes bounds.
                continue template_loop;
            }

            const templateScore = scoreTemplate(template, fx, fy);
            if (templateScore === MAX_SCORE) {
                continue template_loop;
            }

            const tscore = fscore + templateScore;
            const tb = template.EndBorderN;
            const til = borderToZ[tb] * sizeXY + ti;
            if (tscore < scores[til]) {
                scores[til] = tscore;
                priorities.set(til, -tscore);
            }
        }
        priorities.set(fil, -Infinity);
    };

    const sx = start.x;
    const sy = start.y;
    const si = sy * sizeX + sx;
    const sb = startBorderN;
    const sil = borderToZ[sb] * sizeXY + si;
    {
        scores[sil] = 0;
        updateFrom(sx, sy, sb);
        // Needed in case we loop back to the start.
        scores[sil] = MAX_SCORE;
    }
    for (;;) {
        const fil = priorities.getMaxIndex() | 0;
        if (priorities.get(fil) === -Infinity) {
            break;
        }
        const fz = (fil / sizeXY) | 0;
        const fb = zToBorder[fz];
        const fi = (fil % sizeXY) | 0;
        const fy = (fi / sizeX) | 0;
        const fx = (fi % sizeX) | 0;
        updateFrom(fx, fy, fb);
    }

    // Trace back and update tiles
    const resultPath = [
        {
            x: end.x + minPointX,
            y: end.y + minPointY,
        }
    ];

    const traceBackStep = function(tx, ty, tb) {
        const ti = ty * sizeX + tx;
        const til = borderToZ[tb] * sizeXY + ti;
        const tscore = scores[til];
        const candidates = [];
        template_loop: for (const template of templatesByEndBorder[tb]) {
            const fx = tx - template.MovesX;
            const fy = ty - template.MovesY;
            const fi = fy * sizeX + fx;
            if (fx < 0 || fx >= sizeX || fy < 0 || fy >= sizeY) {
                continue template_loop;
            }
            // Most likely to fail. Check first.
            if (deviations[fi] === MAX_DEVIATION) {
                // Start escapes bounds.
                continue template_loop;
            }

            const templateScore = scoreTemplate(template, fx, fy);
            if (templateScore === MAX_SCORE) {
                continue template_loop;
            }

            const fscore = tscore - templateScore;
            const fil = borderToZ[template.StartBorderN] * sizeXY + fi;
            if (fscore === scores[fil]) {
                candidates.push(template);
            }
        }
        candidates.length >= 1 || die("Assertion failure");
        const template = random.pick(candidates);
        const fx = tx - template.MovesX;
        const fy = ty - template.MovesY;
        const templateInfo = info.Tileset.Templates[template.Name];
        paintTemplate(tiles, tilesSize, fx - template.OffsetX + minPointX, fy - template.OffsetY + minPointY, templateInfo);
        // Skip end point as it is recorded in the previous template.
        for (let i = template.RelPathND.length - 2; i >= 0; i--) {
            const point = template.RelPathND[i];
            resultPath.push({
                x: fx + point.x + minPointX,
                y: fy + point.y + minPointY,
            });
        }
        return {
            x: fx,
            y: fy,
            b: template.StartBorderN,
        };
    };

    {
        let tx = end.x;
        let ty = end.y;
        let tb = endBorderN;
        let ti = ty * sizeX + tx;
        let til = borderToZ[tb] * sizeXY + ti;
        if (scores[til] === MAX_SCORE) {
            die("Could not fit tiles for path");
        }
        console.log(`Path ${path.type}[${path.isLoop ? "looped " : ""}${path.points.length}] has error score ${scores[til]} (${scores[til] / path.points.length} per point)`);
        let p = traceBackStep(tx, ty, tb);
        // We previously set this to MAX_SCORE in case we were a loop. Reset it for getting back to the start.
        scores[sil] = 0;
        // No need to check direction. If that is an issue, I have bigger problems to worry about.
        while (p.x !== sx || p.y !== sy) {
            p = traceBackStep(p.x, p.y, p.b);
        }
    }

    // Traced back in reverse, so reverse the reversal.
    return resultPath.reverse();
}

function identifyReplaceableTiles(tiles, size) {
    const sizeSize = size * size;
    const output = new Uint8Array(sizeSize);
    for (let n = 0; n < sizeSize; n++) {
        output[n] = info.replaceabilityMap[tiles[n]] ?? REPLACEABILITY_ANY;
    }
    return output;
}

// If there's a template which doesn't have a tile in its top-left
// corner, this method has biases against it.
function obstructArea(tiles, entities, size, mask, permittedObstacles, random, replaceability) {
    replaceability ??= identifyReplaceableTiles(tiles, size);
    const obstaclesByArea = [];
    for (const obstacle of permittedObstacles) {
        obstaclesByArea[obstacle.Area] ??= [];
        obstaclesByArea[obstacle.Area].push(obstacle);
    }
    obstaclesByArea.reverse();
    const obstacleTotalArea = permittedObstacles.map(t => t.Area).reduce((a, b) => a + b);
    const obstacleTotalWeight = permittedObstacles.map(t => t.Weight).reduce((a, b) => a + b);
    // Give 1-by-1 entities the final pass, as they are most flexible.
    obstaclesByArea.push(permittedObstacles.filter(o => {o.Entity && o.Area === 1}));
    const sizeSize = size * size;
    const maskIndices = new Uint32Array(sizeSize);
    const remaining = new Uint8Array(sizeSize);
    let maskArea = 0;
    for (let n = 0; n < sizeSize; n++) {
        if (mask[n] > 0) {
            remaining[n] = 1;
            maskIndices[maskArea] = n;
            maskArea++;
        } else {
            remaining[n] = 0;
        }
    }
    const indices = new Uint32Array(sizeSize);
    let indexCount;

    const refreshIndices = function() {
        indexCount = 0;
        for (const n of maskIndices) {
            if (remaining[n]) {
                indices[indexCount] = n;
                indexCount++;
            }
        }
        random.shuffleInPlace(indices, indexCount);
    };
    const reserveObstacle = function(px, py, shape, contract) {
        for (const [ox, oy] of shape) {
            const x = px + ox;
            const y = py + oy;
            if (x < 0 || x >= size || y < 0 || y >= size) {
                continue;
            }
            const i = y * size + x;
            if (!remaining[i]) {
                // Can't reserve - not the right shape
                return REPLACEABILITY_NONE;
            }
            contract &= replaceability[i];
            if (contract === REPLACEABILITY_NONE) {
                // Can't reserve - obstruction choice doesn't comply
                // with replaceability of original tiles.
                return REPLACEABILITY_NONE;
            }
        }
        // Can reserve. Commit.
        for (const [ox, oy] of shape) {
            const x = px + ox;
            const y = py + oy;
            if (x < 0 || x >= size || y < 0 || y >= size) {
                continue;
            }
            const i = y * size + x;
            remaining[i] = 0;
        }
        return contract;
    };

    for (const obstacles of obstaclesByArea) {
        if (typeof(obstacles) === "undefined" || obstacles.length === 0) {
            continue;
        }
        const obstacleArea = obstacles[0].Area;
        const obstacleWeights = obstacles.map(o => o.Weight);
        const obstacleWeightForArea = obstacleWeights.reduce((a, b) => a + b);
        let remainingQuota =
            obstacleArea === 1
                ? Infinity
                : (maskArea * obstacleWeightForArea / obstacleTotalWeight);
        refreshIndices();
        for (const n of indices) {
            const obstacle = random.pickWeighted(obstacles, obstacleWeights);
            const py = (n / size) | 0;
            const px = (n % size) | 0;
            if (px === 80 && py === 97) {
                breakpoint();
            }
            const inContract =
                obstacle.Template ? REPLACEABILITY_TILE
                                  : obstacle.Tile ? REPLACEABILITY_ANY
                                                  : REPLACEABILITY_ENTITY;
            const contract = reserveObstacle(px, py, obstacle.Shape, inContract);
            if (contract !== REPLACEABILITY_NONE) {
                if (obstacle.Template) {
                    paintTemplate(tiles, size, px, py, obstacle.Template);
                } else if (obstacle.Entity) {
                    entities.push({
                        type: obstacle.Entity.type,
                        owner: "Neutral",
                        x: px,
                        y: py,
                    });
                    if (contract === REPLACEABILITY_TILE) {
                        obstacle.Tile ?? die("assertion failure");
                        // Contract requires us to replace the tile as well.
                        for (const [ox, oy] of obstacle.Entity.Shape) {
                            const x = px + ox;
                            const y = py + oy;
                            tiles[y * size + x] = obstacle.Tile;
                        }
                    }
                } else {
                    die("assertion failure");
                }
            }
            remainingQuota -= obstacleArea;
            if (remainingQuota <= 0) {
                break;
            }
        }
    }
}

function findPlayableRegions(tiles, entities, size) {
    const regions = [];
    const regionMask = new Uint32Array(size * size);
    const playable = new Uint8Array(size * size);
    for (let n = 0; n < size * size; n++) {
        playable[n] = info.playabilityMap[tiles[n]] ?? die("missing tile playability info");
    }
    const externalCircle = new Uint8Array(size * size);
    const externalCircleCenter = ((size - 1) / 2);
    reserveCircleInPlace(
        externalCircle,
        size,
        externalCircleCenter,
        externalCircleCenter,
        size / 2 - 1,
        1,
        /*invert=*/true
    );
    for (const entity of entities) {
        const def = info.EntityInfo[entity.type];
        for (const [ox, oy] of def.Shape) {
            const x = entity.x + ox;
            const y = entity.y + oy;
            if (x < 0 || x >= size || y < 0 || y >= size) {
                continue;
            }
            playable[y * size + x] = 0;
        }
    }
    const addToRegion = function(region, i) {
        regionMask[i] = region.id;
        region.area++;
        if (externalCircle[i]) {
            region.externalCircle = true;
        }
    };
    const fill = function(region, startX, startY) {
        addToRegion(region, startY * size + startX);
        let next = [[startX, startY]];
        const spread = [[1, 0], [-1, 0], [0, 1], [0, -1]];
        while (next.length !== 0) {
            const current = next;
            next = [];
            for (const [cx, cy] of current) {
                for (const [ox, oy] of spread) {
                    let x = cx + ox;
                    let y = cy + oy;
                    if (x < 0 || x >= size || y < 0 || y >= size) {
                        continue;
                    }
                    const i = y * size + x;
                    if (regionMask[i] === 0 && playable[i]) {
                        addToRegion(region, i);
                        next.push([x, y]);
                    }
                }
            }
        }
    };
    for (let startY = 0; startY < size; startY++) {
        for (let startX = 0; startX < size; startX++) {
            const startI = startY * size + startX;
            if (regionMask[startI] === 0 && playable[startI]) {
                const region = {
                    area: 0,
                    id: regions.length + 1,
                    externalCircle: false,
                };
                regions.push(region);
                fill(region, startX, startY);
            }
        }
    }
    return [regionMask, regions];
}

// Creates a size*size Int8Array where the values mean the following about the area:
//   -1: Closest path travels anti-clockwise around it
//    0: Unknown (no paths)
//   +1: Closest path travels clockwise around it
function pathChirality(size, paths) {
    const chirality = new Int8Array(size * size);
    let next = [];
    const seedChirality = function(x, y, v, firstPass) {
        if (x < 0 || x >= size || y < 0 || y >= size) {
            return;
        }
        if (firstPass) {
            // Some paths which overlap or go back on themselves
            // might fight for chirality. Vote on it.
            chirality[y * size + x] += v;
        } else {
            if (chirality[y * size + x] !== 0) {
                return;
            }
            chirality[y * size + x] = v;
        }
        next.push([x, y]);
    }
    for (const path of paths) {
        for (let i = 1; i < path.length; i++) {
            const fx = path[i - 1].x;
            const fy = path[i - 1].y;
            const tx = path[i    ].x;
            const ty = path[i    ].y;
            const direction = calculateDirectionXY(tx - fx, ty - fy);
            switch (direction) {
            case DIRECTION_R:
                seedChirality(fx    , fy    ,  1, true);
                seedChirality(fx    , fy - 1, -1, true);
                break;
            case DIRECTION_D:
                seedChirality(fx - 1, fy    ,  1, true);
                seedChirality(fx    , fy    , -1, true);
                break;
            case DIRECTION_L:
                seedChirality(fx - 1, fy - 1,  1, true);
                seedChirality(fx - 1, fy    , -1, true);
                break;
            case DIRECTION_U:
                seedChirality(fx    , fy - 1,  1, true);
                seedChirality(fx - 1, fy - 1, -1, true);
                break;
            default:
                die("unsupported direction");
            }
        }
    }
    dump2d("partial chirality", chirality, size, size);
    // Spread out
    const spread = [[1, 0], [-1, 0], [0, 1], [0, -1]];
    while (next.length !== 0) {
        const current = next;
        next = [];
        for (const [cx, cy] of current) {
            for (const [ox, oy] of spread) {
                seedChirality(cx + ox, cy + oy, chirality[cy * size + cx]);
            }
        }
    }

    return chirality;
}

async function generateMap(params) {
    const size = params.size ?? die("need size");

    // Terrain generation
    if (params.water < 0.0 || params.water > 1.0) {
        die("water fraction must be between 0 and 1 inclusive");
    }
    if (params.mountain < 0.0 || params.mountain > 1.0) {
        die("mountain fraction must be between 0 and 1 inclusive");
    }
    if (params.water + params.mountain > 1.0) {
        die("water and mountain fractions combined must not exceed 1");
    }
    const random = new Random(params.seed);

    await progress("elevation: generating noise");
    let elevation = fractalNoise2dWithSymetry({
        random,
        size,
        rotations: params.rotations,
        mirror: params.mirror,
        wavelengthScale: (params.wavelengthScale ?? 1.0),
    });

    {
        const min = Math.min(...elevation);
        dump2d("uncalibrated terrain (zero-rebased)", elevation.map(v => v-min), size, size);
    }
    if (params.terrainSmoothing) {
        await progress("elevation: applying gaussian blur");
        const radius = params.terrainSmoothing;
        const kernel = gaussianKernel1D(radius, radius);
        elevation = kernelBlur(elevation, size, kernel, radius * 2 + 1, 1, radius, 0);
        elevation = kernelBlur(elevation, size, kernel, 1, radius * 2 + 1, 0, radius);
        const min = Math.min(...elevation);
        dump2d("guassian-smoothed terrain (zero-rebased)", elevation.map(v => v-min), size, size);
    }
    calibrateHeightInPlace(
        elevation,
        0.0,
        params.water,
    );
    const externalCircleCenter = ((size - 1) / 2);
    if (params.externalCircularBias !== 0) {
        await progress("elevation: reserving external circle");
        reserveCircleInPlace(
            elevation,
            size,
            externalCircleCenter,
            externalCircleCenter,
            size / 2 - (params.minimumLandSeaThickness + params.minimumMountainThickness),
            params.externalCircularBias > 0 ? EXTERNAL_BIAS : -EXTERNAL_BIAS,
            /*invert=*/true
        );
    }
    {
        dump2d("calibrated terrain", elevation, size, size);
    }
    await progress("land planning: fixing terrain anomalies");
    let landPlan = await fixTerrain(elevation, size, params.terrainSmoothing, params.smoothingThreshold, params.minimumLandSeaThickness, /*bias=*/(params.water < 0.5 ? 1 : -1), "land planning");

    let forests = null;
    if (params.forests > 0) {
        await progress("forests: generating noise");
        // Generate this now so that the noise isn't effected by random settings.
        await progress("generating forest map");
        forests = fractalNoise2dWithSymetry({
            random,
            size,
            rotations: params.rotations,
            mirror: params.mirror,
            wavelengthScale: params.wavelengthScale,
            amp_func: (wavelength => (wavelength**params.forestClumpiness)),
        });
    }

    const tiles = new Array(size * size).fill(null);
    const resources = new Uint8Array(size * size);
    const resourceDensities = new Uint8Array(size * size);

    await progress("coastlines: tracing coastlines");
    let coastlines = zeroLinesToPaths(landPlan, size);
    coastlines = coastlines.map(coastline => tweakPath(coastline, size));
    await progress("coastlines: fitting and laying tiles");

    const layedCoastlines = [];
    for (const coastline of coastlines) {
        coastline.type = "Coastline";
        layedCoastlines.push(
            tilePath(tiles, size, coastline, random, params.minimumLandSeaThickness)
        );
    }
    await progress("coastlines: filling land and water");
    const coastlineChirality = pathChirality(size, layedCoastlines);
    dump2d("coastline chirality", coastlineChirality, size, size,
        layedCoastlines.flat().map(v => ({
            debugRadius: 0.5,
            debugColor: "white",
            x: v.x - 0.5,
            y: v.y - 0.5,
        }))
    );
    for (let n = 0; n < tiles.length; n++) {
        if (tiles[n] !== null) {
            continue;
        }
        if (coastlineChirality[n] > 0) {
            tiles[n] = 't255';
        } else if (coastlineChirality[n] < 0) {
            tiles[n] = 't1i0';
        } else {
            // There weren't any coastlines
            if (landPlan[n] >= 0) {
                tiles[n] = 't255';
            } else {
                tiles[n] = 't1i0';
            }
        }
    }

    if (params.externalCircularBias > 0) {
        await progress("creating circular cliff map border");
        const cliffRing = new Int8Array(size * size).fill(-1);
        reserveCircleInPlace(
            cliffRing,
            size,
            externalCircleCenter,
            externalCircleCenter,
            size / 2 - (params.minimumLandSeaThickness),
            1,
            /*invert=*/true
        );
        let cliffs = zeroLinesToPaths(cliffRing, size);
        cliffs = cliffs.map(cliff => tweakPath(cliff, size));
        cliffs.forEach(cliff => {
            cliff.type = "Cliff";
            if (!cliff.isLoop) {
                cliff.startType = "Clear";
                cliff.endType = "Clear";
            }
        });
        for (const cliff of cliffs) {
            tilePath(tiles, size, cliff, random, params.minimumMountainThickness);
        }
    }
    if (params.mountains > 0.0 || params.externalCircularBias > 0) {
        await progress("mountains: calculating elevation roughness");
        const roughness = gridVariance2d(elevation, size, params.roughnessRadius).map(v => Math.sqrt(v));
        dump2d("roughness (as standard deviation)", roughness, size + 1, size + 1);
        calibrateHeightInPlace(
            roughness,
            0.0,
            1.0 - params.roughness,
        );
        dump2d("roughness calibrated", roughness, size + 1, size + 1);
        const cliffMask = roughness.map(v => (v >= 0 ? 1 : -1));
        dump2d("cliffMaskBin", cliffMask, size + 1, size + 1);
        const mountainElevation = elevation.slice();
        let cliffPlan = landPlan;
        if (params.externalCircularBias > 0) {
            reserveCircleInPlace(
                cliffPlan,
                size,
                externalCircleCenter,
                externalCircleCenter,
                size / 2 - (params.minimumLandSeaThickness + params.minimumMountainThickness),
                -1,
                /*invert=*/true
            );
        }
        for (let altitude = 1; altitude <= params.maximumAltitude; altitude++) {
            await progress(`mountains: altitude ${altitude}: determining eligible area for cliffs`);
            // Limit mountain area to the existing mountain space (starting with all available land)
            const roominess = calculateRoominess(cliffPlan, size, true);
            let available = 0;
            let total = 0;
            for (let n = 0; n < mountainElevation.length; n++) {
                if (roominess[n] < params.minimumTerrainContourSpacing) {
                    // Too close to existing cliffs (or coastline)
                    mountainElevation[n] = -1;
                } else {
                    available++;
                }
                total++;
            }
            const availableFraction = available / total;
            calibrateHeightInPlace(
                mountainElevation,
                0.0,
                1.0 - availableFraction * params.mountains,
            );
            dump2d(`mountains at altitude ${altitude}`, mountainElevation, size, size);
            await progress(`mountains: altitude ${altitude}: fixing terrain anomalies`);
            cliffPlan = await fixTerrain(mountainElevation, size, params.terrainSmoothing, params.smoothingThreshold, params.minimumMountainThickness, /*bias=*/-1, `mountains: altitude ${altitude}`);
            await progress(`mountains: altitude ${altitude}: tracing cliffs`);
            let cliffs = zeroLinesToPaths(cliffPlan, size);
            await progress(`mountains: altitude ${altitude}: appling roughness mask to cliffs`);
            cliffs = maskPaths(cliffs, cliffMask, size + 1);
            cliffs = cliffs.filter(cliff => cliff.points.length >= params.minimumCliffLength);
            if (cliffs.length === 0) {
                break;
            }
            await progress(`mountains: altitude ${altitude}: fitting and laying tiles`);
            cliffs = cliffs.map(cliff => tweakPath(cliff, size));
            cliffs.forEach(cliff => {
                cliff.type = "Cliff";
                if (!cliff.isLoop) {
                    cliff.startType = "Clear";
                    cliff.endType = "Clear";
                }
            });
            for (const cliff of cliffs) {
                tilePath(tiles, size, cliff, random, params.minimumMountainThickness);
            }
        }
    }

    const entities = [];
    const players = [];

    if (forests !== null) {
        await progress(`forests: planting trees`);
        {
            const min = Math.min(...forests);
            dump2d("uncalibrated forests (zero-rebased)", forests.map(v => v-min), size, size);
        }
        calibrateHeightInPlace(
            forests,
            0.0,
            1.0 - params.forests,
        );
        dump2d("calibrated forests", forests, size, size);
        for (let n = 0; n < size * size; n++) {
            switch (codeMap[tiles[n]].Type) {
            case "Clear":
                // Preserve forest
                break;
            default:
                forests[n] = -1;
                break;
            }
        }
        obstructArea(tiles, entities, size, forests, info.ObstacleInfo.Forest, random);
    }

    const playableArea = new Uint8Array(size * size);
    {
        await progress(`determining playable regions`);
        const [regionMask, regions] = findPlayableRegions(tiles, entities, size);
        dump2d("playable regions", regionMask, size, size);
        let largest = null;
        for (const region of regions) {
            if (params.externalCircularBias > 0 && region.externalCircle) {
                continue;
            }
            if (largest === null || region.area > largest.area) {
                largest = region;
            }
        }
        largest || die("could not find a playable region");
        if (params.denyWalledAreas) {
            await progress(`obstructing semi-unreachable areas`);
            const obstructionMask = regionMask.map(v => (v !== largest.id));
            obstructArea(tiles, entities, size, obstructionMask, info.ObstacleInfo.Land, random);
        }
        for (let n = 0; n < size * size; n++) {
            playableArea[n] = (regionMask[n] === largest.id) ? 1 : 0;
        }
        dump2d("chosen playable area", playableArea, size, size);
    }

    if (params.createEntities) {
        await progress(`entities: determining eligible space`);
        const zones = [];
        const zoneable = new Int8Array(size * size);
        for (let n = 0; n < tiles.length; n++) {
            zoneable[n] = (playableArea[n] && codeMap[tiles[n]].Type === 'Clear') ? 1 : -1;
        }
        switch (params.rotations) {
        case 1:
        case 2:
        case 4:
            break;
        default:
            // Non 1, 2, 4 rotations need entity placement confined to a circle, regardless of externalCircularBias
            reserveCircleInPlace(
                zoneable,
                size,
                externalCircleCenter,
                externalCircleCenter,
                size / 2 - 1,
                -1,
                /*invert=*/true
            );
            break;
        }
        if (params.rotations > 1 || params.mirror !== 0) {
            // Reserve the center of the map - otherwise it will mess with rotations
            const midPoint = (size >> 1) * (size + 1);
            zoneable[midPoint] = -1;
            zoneable[midPoint + 1] = -1;
            zoneable[midPoint + size] = -1;
            zoneable[midPoint + size + 1] = -1;
        }
        let roominess = calculateRoominess(zoneable, size);

        // Spawn generation
        await progress(`entities: zoning for spawns`);
        for (let iteration = 0; iteration < params.players; iteration++) {
            roominess = calculateRoominess(roominess, size);
            const spawnPreference = calculateSpawnPreferences(roominess, size, params.centralReservation, params.spawnRegionSize, params.mirror);
            dump2d("zoneable", zoneable, size, size);
            dump2d("player roominess", roominess, size, size);
            const templatePlayer = findRandomMax(random, spawnPreference, size, params.spawnRegionSize);
            const room = templatePlayer.value - 1;
            const radius1 = Math.min(params.spawnBuildSize, room);
            const radius2 = Math.min(params.spawnRegionSize, room);
            templatePlayer.debugColor = "white";
            templatePlayer.debugRadius = 2;
            templatePlayer.radius = params.spawnBuildSize;
            templatePlayer.owner = "Neutral";
            templatePlayer.type = "mpspawn";
            players.push(
                ...rotateAndMirror(
                    [templatePlayer],
                    size,
                    params.rotations,
                    params.mirror,
                )
            );

            const spawnZones = generateFeatureRing(random, templatePlayer, "spawn", radius1, radius2, params);
            zones.push(
                ...rotateAndMirror(
                    [templatePlayer, ...spawnZones],
                    size,
                    params.rotations,
                    params.mirror,
                )
            );
            for (let zone of zones) {
                reserveCircleInPlace(roominess, size, zone.x, zone.y, zone.radius, -1);
            }
        }
        players.forEach((el, i) => {
            el.number = i;
            el.name = `Multi${i}`;
        });
        entities.push(...players);

        // Expansions
        await progress(`entities: zoning for expansions`);
        for (let i = 0; i < (params.maximumExpansions ?? 0); i++) {
            roominess = calculateRoominess(roominess, size);
            dump2d(`expansion roominess ${i}`, roominess, size, size);
            const templateExpansion = findRandomMax(random, roominess, size, params.maximumExpansionSize + params.expansionBorder);
            const room = templateExpansion.value - 1;
            let radius2 = room - params.expansionBorder;
            if (radius2 < params.minimumExpansionSize) {
                break;
            }
            if (radius2 > params.maximumExpansionSize) {
                radius2 = params.maximumExpansionSize;
            }
            const radius1 = Math.min(Math.min(params.expansionInner, room), radius2);

            const expansionZones = generateFeatureRing(random, templateExpansion, "expansion", radius1, radius2, params);
            zones.push(
                ...rotateAndMirror(
                    expansionZones,
                    size,
                    params.rotations,
                    params.mirror,
                )
            );
            for (let zone of zones) {
                reserveCircleInPlace(roominess, size, zone.x, zone.y, zone.radius, -1);
            }
        }

        // Neutral buildings
        await progress(`entities: zoning for tech structures`);
        {
            params.maximumBuildings >= params.minimumBuildings || die("maximumBuildings must be at least minimumBuildings");
            const targetBuildingCount =
                (params.maximumBuildings ?? 0 !== 0)
                    ? params.minimumBuildings + random.u32() % (params.maximumBuildings + 1 - params.minimumBuildings)
                    : 0;
            for (let i = 0; i < targetBuildingCount; i++) {
                roominess = calculateRoominess(roominess, size);
                dump2d(`building roominess ${i}`, roominess, size, size);
                const templateBuilding = findRandomMax(random, roominess, size, 3);
                if (templateBuilding.value < 3) {
                    break;
                }
                templateBuilding.radius = 2;
                templateBuilding.type = random.pickWeighted(
                    ["fcom", "hosp", "miss", "bio", "oilb"],
                    [
                        params.weightFcom,
                        params.weightHosp,
                        params.weightMiss,
                        params.weightBio,
                        params.weightOilb,
                    ],
                );
                const entityInfo = info.EntityInfo[templateBuilding.type] ?? die("missing entity info");
                templateBuilding.debugRadius = entityInfo.debugRadius;
                templateBuilding.debugColor = entityInfo.debugColor;
                templateBuilding.x += ((entityInfo.w - 1) / 2) % 1.0;
                templateBuilding.y += ((entityInfo.h - 1) / 2) % 1.0;
                zones.push(
                    ...rotateAndMirror(
                        [templateBuilding],
                        size,
                        params.rotations,
                        params.mirror,
                    )
                );
                for (let zone of zones) {
                    reserveCircleInPlace(roominess, size, zone.x, zone.y, zone.radius, -1);
                }
            }
        }

        await progress(`entities: converting zones to entities`);
        for (const zone of zones) {
            switch (zone.type) {
            case "mine":
                entities.push({
                    type: "mine",
                    owner: "Neutral",
                    x: zone.x,
                    y: zone.y,
                });
                reserveCircleInPlace(resources, size, zone.x, zone.y, zone.radius, 1);
                // Density seems to be a constant in map format
                reserveCircleInPlace(resourceDensities, size, zone.x, zone.y, zone.radius, 12);
                break;
            case "gmine":
                entities.push({
                    type: "gmine",
                    owner: "Neutral",
                    x: zone.x,
                    y: zone.y,
                });
                reserveCircleInPlace(resources, size, zone.x, zone.y, zone.radius, 2);
                // Density seems to be a constant in map format
                reserveCircleInPlace(resourceDensities, size, zone.x, zone.y, zone.radius, 3);
                break;
            case "fcom":
            case "hosp":
            case "bio":
            case "oilb":
            case "miss":
                const entityInfo = info.EntityInfo[zone.type];
                const x = zone.x - ((entityInfo.w - 1) / 2);
                const y = zone.y - ((entityInfo.h - 1) / 2);
                entities.push({
                    type: zone.type,
                    owner: "Neutral",
                    x: x | 0,
                    y: y | 0,
                });
                break;
            // Default ignore
            }
        }

        // Debug output
        dump2d("zones", zoneable.map(x=>(x>0?1:0)), size, size, zones);
        dump2d("entities", zoneable.map(x=>(x>0?1:0)), size, size, entities);
        dump2d("resources", resources, size, size);
        dump2d("post-entity roominess", roominess, size, size);
    }

    // Remove any ore that goes outside of the bounds of clear tiles.
    // (This may introduce some significant bias to certain players!)
    await progress(`clearing unreachable resources`);
    for (let n = 0; n < resources.length; n++) {
        if (codeMap[tiles[n]].Type !== "Clear") {
            resources[n] = 0;
            resourceDensities[n] = 0;
        }
    }

    if (params.enforceSymmetry) {
        await progress(`symmetry enforcement: analysing`);
        // const equitability = new Uint8Array(size * size).fill(1);
        const checkPoint = function(x, y, base) {
            const i = y * size + x;
            switch (base) {
            case "River":
            case "Rock":
            case "Water":
                return true;
            case "Beach":
            case "Clear":
            case "Rough":
                switch (codeMap[tiles[i]].Type) {
                case "River":
                case "Rock":
                case "Water":
                    return false;
                case "Beach":
                case "Clear":
                case "Rough":
                    return true;
                default:
                    die("ambiguous symmetry policy");
                }
            default:
                die("ambiguous symmetry policy");
            }
        }
        const checkRotatedPoints = function(x, y, base) {
            switch (params.rotations) {
            case 1:
                return checkPoint(x, y, base);
            case 2:
                return (
                    checkPoint(           x,            y, base) &&
                    checkPoint(size - 1 - x, size - 1 - y, base)
                );
            case 4:
                return (
                    checkPoint(           x,            y, base) &&
                    checkPoint(size - 1 - y,            x, base) &&
                    checkPoint(size - 1 - x, size - 1 - y, base) &&
                    checkPoint(           y, size - 1 - x, base)
                );
            default:
                die("cannot enforce symmetry for rotations other than 1, 2, or 4");
            }
        }
        const obstructionMask = new Uint8Array(size * size);
        for (let y = 0; y < size; y++) {
            for (let x = 0; x < size; x++) {
                const i = y * size + x;
                const base = codeMap[tiles[i]].Type;
                let equitable = checkRotatedPoints(x, y, base);
                if (params.mirror !== 0) {
                    equitable &&= checkRotatedPoints(...mirrorXY(x, y, size, params.mirror), base);
                }
                obstructionMask[i] = equitable ? 0 : 1;
            }
        }
        await progress(`symmetry enforcement: obstructing`);
        obstructArea(tiles, entities, size, obstructionMask, info.ObstacleInfo.FillSymmetry, random);
    }

    // Assign missing indexes
    await progress(`assigning indexes to pick-any templates`);
    for (let n = 0; n < tiles.length; n++) {
        if (codeMap[tiles[n]].Codes.length > 1) {
            tiles[n] = random.pick(codeMap[tiles[n]].Codes);
        }
    }

    // Compilation
    const map = {
        size,
        random,
        elevation,
        tiles: tiles,
        types: Array(size * size),
        resources,
        resourceDensities,
        bin: {},
        players,
        entities,
    };

    await progress(`compiling: map.bin`);
    map.bin.u8format = 2;
    map.bin.u16width = size + 2;
    map.bin.u16height = size + 2;
    map.bin.gridSize = map.bin.u16width * map.bin.u16height;
    map.bin.u32tileOffset = 17;
    map.bin.u32heightMapOffset = 0;
    map.bin.u32resourcesOffset = map.bin.u32tileOffset + 3 * map.bin.gridSize;
    map.bin.size = map.bin.u32resourcesOffset + 2 * map.bin.gridSize;
    map.bin.data = new Uint8Array(map.bin.size);

    writeU8(map.bin.data, 0, map.bin.u8format);
    writeU16(map.bin.data, 1, map.bin.u16width);
    writeU16(map.bin.data, 3, map.bin.u16height);
    writeU32(map.bin.data, 5, map.bin.u32tileOffset);
    writeU32(map.bin.data, 9, map.bin.u32heightMapOffset);
    writeU32(map.bin.data, 13, map.bin.u32resourcesOffset);
    // Clear map data to empty grass (including out-of-bounds area.
    for (let n = 0; n < map.bin.gridSize; n++) {
        writeU16(map.bin.data, map.bin.u32tileOffset + n * 3, 255 /* Grass */);
        writeU8(map.bin.data, map.bin.u32tileOffset + n * 3 + 2, 0 /* Index 0 */);

        writeU8(map.bin.data, map.bin.u32resourcesOffset + n * 2, 0 /* Type */);
        writeU8(map.bin.data, map.bin.u32resourcesOffset + n * 2 + 1, 0 /* Density */);
    }

    const tiRe = /^t(\d+)i(\d+)$/;
    // OpenRA map format scans vertically. See dataGridN.
    for (let x = 0; x < size; x++) {
        for (let y = 0; y < size; y++) {
            const n = y * size + x;
            const ti = map.tiles[n].match(tiRe);
            const t = ti[1] | 0;
            const i = ti[2] | 0;
            map.types[n] = codeMap[map.tiles[n]].Type;

            // +1 for x and y to account for the out-of-bounds area.
            const dataGridN = (x + 1) * map.bin.u16width + (y + 1);
            const dataTileOffset = map.bin.u32tileOffset + dataGridN * 3;
            writeU16(map.bin.data, dataTileOffset, t);
            writeU8(map.bin.data, dataTileOffset + 2, i);
            const dataResourceOffset = map.bin.u32resourcesOffset + dataGridN * 2;
            writeU8(map.bin.data, dataResourceOffset, map.resources[n]);
            writeU8(map.bin.data, dataResourceOffset + 1, map.resourceDensities[n]);
        }
    }

    const mapName = params.customName !== "" ? params.customName : `Random Map @${Date.now()}`;

    // OpenRA's yaml isn't proper YAML - it's something weird and
    // specific to OpenRA called MiniYAML. So, I can't do something
    // normal and have to dump it out myself...
    await progress(`compiling: map.yaml`);
    map.yaml =
`MapFormat: 12
RequiresMod: ra
Title: ${mapName}
Author: OpenRA Random Map Generator Prototype
Tileset: TEMPERAT
MapSize: ${size+2},${size+2}
Bounds: 1,1,${size},${size}
Visibility: Lobby
Categories: Conquest

Players:
\tPlayerReference@Neutral:
\t\tName: Neutral
\t\tOwnsWorld: True
\t\tNonCombatant: True
\t\tFaction: england
\tPlayerReference@Creeps:
\t\tName: Creeps
\t\tNonCombatant: True
\t\tFaction: england
`;
    if (players.length > 0) {
        const enemies = players.map(player => player.name).join(", ");
        map.yaml += `\t\tEnemies: ${enemies}\n`;
    }
    for (const player of players) {
        map.yaml +=
`\tPlayerReference@${player.name}:
\t\tName: ${player.name}
\t\tPlayable: True
\t\tFaction: Random
\t\tEnemies: Creeps
`;
    }
    {
        map.yaml += "Actors:\n";
        let num = 0;
        for (const entity of entities) {
            entity.type ?? die("Entity is missing type");
            entity.owner ?? die("Entity is missing owner");
            entity.x ?? die("Entity is missing location");
            entity.y ?? die("Entity is missing location");
            const def = info.EntityInfo[entity.type] ?? {
                OffsetX: 0,
                OffsetY: 0,
            };
            const x = 1 + entity.x + def.OffsetX;
            const y = 1 + entity.y + def.OffsetY;
            // +1 to x and y to compensate for out-of-bounds
            map.yaml +=
`\tActor${num++}: ${entity.type}
\t\tOwner: ${entity.owner}
\t\tLocation: ${x},${y}
`;
        }
    }

    return map;
}

function createPreview(map, ctx) {
    const size = map.size;
    // Generate preview
    for (let y = 0; y < size; y++) {
        for (let x = 0; x < size; x++) {
            const i = y * size + x;
            if (map.resources[i] === 0) {
                ctx.fillStyle = terrainColor(map.types[i]);
            } else {
                switch (map.resources[i]) {
                case 1:
                    ctx.fillStyle = "#948060";
                    break;
                case 2:
                    ctx.fillStyle = "#8470ff";
                    break;
                default:
                    ctx.fillStyle = "black";
                    break;
                }
            }
            // ctx.fillStyle = "rgb(0, "+(map.elevation[y * size + x]*5000+128)+", 0)";
            ctx.fillRect(x, y, 1, 1);
        }
    }
    for (const entity of map.entities) {
        const entityInfo =
            info.EntityInfo[entity.type]
                ?? {Shape: [[0, 0]], terrainType: null};
        if (entityInfo.terrainType !== null) {
            ctx.fillStyle = terrainColor(entityInfo.terrainType);
        } else {
            ctx.fillStyle = "white";
        }
        for (const [mx, my] of entityInfo.Shape) {
            ctx.fillRect(entity.x + mx, entity.y + my, 1, 1);
        }
    }
    for (const player of map.players) {
        ctx.strokeStyle = "#ffffff";
        ctx.lineWidth = 2;
        ctx.fillStyle = "#808080";
        ctx.beginPath();
        ctx.arc(player.x + 0.5, player.y + 0.5, 2, 0, Math.PI * 2);
        ctx.stroke();
        ctx.fill();
    }
}

export async function generate() {
    if (!ready) die("not ready");

    await progress("setting up");

    debugDiv.replaceChildren();

    const canvas = document.getElementById("canvas");

    const saveBin = document.getElementById("saveBin");
    const saveYaml = document.getElementById("saveYaml");
    const savePng = document.getElementById("savePng");
    const saveSettings = document.getElementById("saveSettings");

    const settings = readSettings();

    const linkToMap = document.getElementById("linkToMap");
    history.replaceState({}, "", location.origin + location.pathname + "?settings=" + btoa(JSON.stringify(settings)));
    linkToMap.href = location.href;

    const map = await generateMap(settings);
    window.map = map;

    const size = settings.size;

    canvas.width = size;
    canvas.height = size;

    await progress("rendering preview");
    const ctx = canvas.getContext("2d");
    ctx.fillStyle = "black";
    ctx.fillRect(0, 0, size, size);
    createPreview(map, ctx);

    await progress("generating file links");
    {
        const blob = new Blob([map.bin.data], {type: 'application/octet-stream'});
        saveBin.href = URL.createObjectURL(blob);
    }
    {
        const blob = new Blob([map.yaml], {type: 'application/octet-stream'});
        saveYaml.href = URL.createObjectURL(blob);
    }
    {
        const content = location.href + "\n\n" + JSON.stringify(readSettings(), null, 2) + "\n";
        const blob = new Blob([content], {type: 'text/plain'});
        saveSettings.href = URL.createObjectURL(blob);
    }
    {
        savePng.href = canvas.toDataURL();
    }

    if (dirty) {
        framePreview("yellow");
    } else {
        framePreview("green");
    }
}

const settingsMetadata = {
    seed: {init: -2024525772, type: "int"},
    size: {init: 96, type: "int"},
    rotations: {init: 2, type: "int"},
    mirror: {init: 0, type: "int"},
    players: {init: 1, type: "int"},
    customName: {init: "", type: "string"},

    wavelengthScale: {init: 1.0, type: "float"},
    water: {init: 0.5, type: "float"},
    mountains: {init: 0.1, type: "float"},
    forests: {init: 0.025, type: "float"},
    externalCircularBias: {init: 0, type: "int"},
    terrainSmoothing: {init: 4, type: "int"},
    smoothingThreshold: {init: 0.33, type: "float"},
    minimumLandSeaThickness: {init: 5, type: "int"},
    minimumMountainThickness: {init: 5, type: "int"},
    maximumAltitude: {init: 8, type: "int"},
    roughnessRadius: {init: 5, type: "int"},
    roughness: {init: 0.5, type: "float"},
    minimumTerrainContourSpacing: {init: 6, type: "int"},
    minimumCliffLength: {init: 10, type: "int"},
    forestClumpiness: {init: 0.5, type: "float"},
    denyWalledAreas: {init: true, type: "bool"},
    enforceSymmetry: {init: false, type: "bool"},

    createEntities: {init: true, type: "bool"},
    startingMines: {init: 3, type: "int"},
    startingOre: {init: 3, type: "int"},
    centralReservation: {init: 16, type: "int"},
    spawnRegionSize: {init: 16, type: "int"},
    spawnBuildSize: {init: 8, type: "int"},
    spawnMines: {init: 3, type: "int"},
    spawnOre: {init: 3, type: "int"},
    maximumExpansions: {init: 4, type: "int"},
    minimumExpansionSize: {init: 2, type: "int"},
    maximumExpansionSize: {init: 12, type: "int"},
    expansionInner: {init: 4, type: "int"},
    expansionBorder: {init: 4, type: "int"},
    expansionMines: {init: 0.02, type: "float"},
    expansionOre: {init: 5, type: "int"},
    gemUpgrade: {init: 0.1, type: "float"},
    minimumBuildings: {init: 0, type: "int"},
    maximumBuildings: {init: 3, type: "int"},
    weightFcom: {init: 1, type: "float"},
    weightHosp: {init: 2, type: "float"},
    weightMiss: {init: 2, type: "float"},
    weightBio: {init: 0, type: "float"},
    weightOilb: {init: 8, type: "float"},
};

function camelToKebab(str) {
    return str.replaceAll(/(?=[A-Z])/g, '-').toLowerCase();
}

function markDirty() {
    dirty = true;
    framePreview("yellow");
}

export function readSettings() {
    const settings = {};
    for (const settingName of Object.keys(settingsMetadata)) {
        const type = settingsMetadata[settingName].type;
        const elementName = "setting-" + camelToKebab(settingName);
        const el = document.getElementById(elementName) ?? die(`Missing setting element ${elementName}`);
        let value;
        switch (type) {
        case "int":
            value = el.value | 0;
            break;
        case "float":
            value = Number(el.value);
            break;
        case "string":
            value = el.value;
            break;
        case "bool":
            value = el.checked;
            break;
        default:
            die(`Unknown type ${type}`);
        }
        settings[settingName] = value;
    }
    return settings;
}

// Settings which aren't supplied are set to the default (init) values.
export function writeSettings(settings) {
    for (const settingName of Object.keys(settingsMetadata)) {
        const type = settingsMetadata[settingName].type;
        const elementName = "setting-" + camelToKebab(settingName);
        const el = document.getElementById(elementName) ?? die(`Missing setting element ${elementName}`);
        const value = settings[settingName] ?? settingsMetadata[settingName].init;
        switch (type) {
        case "int":
            el.value = value;
            el.type = "text";
            break;
        case "float":
            el.value = value;
            el.type = "text";
            break;
        case "string":
            el.value = value;
            el.type = "text";
            break;
        case "bool":
            el.checked = value;
            el.type = "checkbox";
            break;
        default:
            die(`Unknown type ${type}`);
        }
        el.onchange = markDirty;
        el.oninput = markDirty;
    }
}

export function configurePreset(generateRandom) {
    let preset = document.getElementById("preset").value;
    document.getElementById("preset").value = "placeholder";
    let randomPresets = null;
    switch (preset) {
    case "random":
        randomPresets = [
            "continents",
            "plains",
            "woodlands",
            "mountains",
            "wetlands",
            "puddles",
            "oceanic",
            "lange-islands",
            "lake-district",
        ];
        break;
    case "random-land":
        randomPresets = [
            "plains",
            "woodlands",
            "mountains",
        ];
        break;
    case "random-land-water":
        randomPresets = [
            "continents",
            "wetlands",
            "puddles",
            "oceanic",
            "lange-islands",
            "lake-district",
        ];
        break;
    default:
        // Not random
        break;
    }
    if (randomPresets !== null) {
        preset = randomPresets[(Math.random() * randomPresets.length) | 0];
    }

    const old = readSettings();
    const settings = {
        seed: old.seed,
        size: old.size,
        rotations: old.rotations,
        mirror: old.mirror,
        players: old.players,
    };
    switch (preset) {
    case "placeholder":
        return;
    case "---":
        return;
    case "basic":
        break;
    case "continents":
        break;
    case "plains":
        settings.water = 0.0;
        settings.wavelengthScale = 0.2;
        break;
    case "woodlands":
        settings.water = 0.0;
        settings.wavelengthScale = 0.2;
        settings.forests = 0.1;
        break;
    case "mountains":
        settings.water = 0.0;
        settings.wavelengthScale = 0.2;
        settings.mountains = 0.9;
        break;
    case "wetlands":
        settings.water = 0.5;
        settings.wavelengthScale = 0.2;
        break;
    case "wetlands-narrow":
        settings.water = 0.5;
        settings.wavelengthScale = 0.05;
        settings.forests = 0.0
        break;
    case "puddles":
        settings.water = 0.2;
        settings.wavelengthScale = 0.2;
        break;
    case "oceanic":
        settings.water = 0.8;
        settings.wavelengthScale = 0.2;
        settings.forests = 0.0
        break;
    case "large-islands":
        settings.water = 0.75;
        settings.wavelengthScale = 0.5;
        settings.forests = 0.0
        break;
    case "lake-district":
        settings.water = 0.2;
        settings.wavelengthScale = 0.2;
        settings.mountains = 1.0;
        break;
    default:
        die(`Unknown preset ${preset}`);
    }
    writeSettings(settings);
    if (generateRandom) {
        randomSeed();
        beginGenerate();
    }
}

export function beginGenerate() {
    if (running) {
        return;
    }
    const statusLine = document.getElementById("status-line");
    const saveLinks = document.getElementById("save-links");
    requestAnimationFrame(function() {
        running = true;
        dirty = false;
        statusLine.textContent = "starting";
        saveLinks.style.visibility = "hidden";
        framePreview("grey");
        blankPreview("black");
        (async function () {
            try {
                progress("Beginning...");
                await generate();
                await progress(`Done!`);
                saveLinks.style.visibility = "visible";
            } catch (err) {
                const log = document.createElement("pre");
                log.textContent = `Generation failed: ${err.message}\n${err.stack ?? ""}`;
                log.style.color = "red";
                debugDiv.append(log);
                framePreview("red");
                blankPreview("grey");
                statusLine.textContent = "Error. Check debugging information below.";
                console.error(err);
            }
            running = false;
        })();
    });
};

window.generate = generate;
window.beginGenerate = beginGenerate;
window.configurePreset = configurePreset;

window.randomSeed = function() {
    // This isn't great.
    const seed = (Math.random() * 0x100000000) & 0xffffffff;
    document.getElementById("setting-seed").value = seed;
    console.log(seed);
};

window.settingsToJson = function() {
    try {
        document.getElementById("settings-json").value = JSON.stringify(readSettings(), null, 2);
    } catch (err) {
        alert("Could not dump settings to JSON:\n" + err.message);
    }
};
window.jsonToSettings = function(shouldGenerate) {
    try {
        writeSettings(JSON.parse(document.getElementById("settings-json").value));
    } catch (err) {
        alert("Could not load settings from JSON:\n" + err.message);
        return;
    }
    if (shouldGenerate) {
        beginGenerate();
    }
};

Promise.all([
    fetch("temperat-info.json")
        .then((response) => response.json()),
])
    .then(([data]) => {
        info = data;
        window.info = info;
        info.codeMap = codeMap;
        info.tileCount = 0;
        for (const tiIndex of Object.keys(info.TileInfo)) {
            const ti = info.TileInfo[tiIndex];
            codeMap[tiIndex] = ti;
            for (let code of ti.Codes) {
                codeMap[code] = ti;
            }
            info.tileCount++;
        }
        const sizeRe = /^(\d+),(\d+)$/;
        for (const template of Object.values(info.Tileset.Templates)) {
            let [, x, y] = template.Size.match(sizeRe);
            template.SizeX = x | 0;
            template.SizeY = y | 0;
            // This is not necessarily x * y!
            template.Area = Object.values(template.Tiles).length;
            template.Shape = [];
            for (let y = 0; y < template.SizeY; y++) {
                for (let x = 0; x < template.SizeX; x++) {
                    const i = y * template.SizeX + x;
                    if (typeof(template.Tiles[i]) === "undefined") {
                        continue;
                    }
                    template.Shape.push([x, y]);
                }
            }
        }
        for (const entityName of Object.keys(info.EntityInfo)) {
            const entity = info.EntityInfo[entityName];
            entity.type = entityName;
            // Note that we don't count any dirt beneath the building in these sizes:
            entity.w ??= 1;
            entity.h ??= 1;
            entity.OffsetX ??= 0;
            entity.OffsetY ??= 0;
            entity.w |= 0;
            entity.h |= 0;
            entity.OffsetX |= 0;
            entity.OffsetY |= 0;
            entity.Area = entity.w * entity.h;
            entity.Shape = [];
            for (let y = 0; y < entity.h; y++) {
                for (let x = 0; x < entity.w; x++) {
                    entity.Shape.push([x, y]);
                }
            }
            entity.debugColor ??= "white";
            entity.debugRadius ??= 1;
            entity.terrainType ??= null;
        }
        for (const obstacleCategory of Object.values(info.ObstacleInfo)) {
            for (const obstacle of obstacleCategory) {
                obstacle.Template = info.Tileset.Templates[obstacle.TemplateName] ?? null;
                obstacle.Entity = info.EntityInfo[obstacle.EntityName] ?? null;
                if (obstacle.Template && obstacle.Entity) {
                    die("Obstacle should be either template or entity - not both.");
                } else if (obstacle.Template) {
                    obstacle.Area = obstacle.Template.Area;
                    obstacle.Shape = obstacle.Template.Shape;
                } else if (obstacle.Entity) {
                    obstacle.Area = obstacle.Entity.Area;
                    obstacle.Shape = obstacle.Entity.Shape;
                } else {
                    die("Obstacle have either template or entity.");
                }
                obstacle.Weight = Number(obstacle.Weight);
            }
        }
        info.typeNs = {
            "Coastline": 0,
            "Clear": 1,
            "Cliff": 2,
        };
        info.borderNs = [
            [0, 1, 2, 3, 4, 5, 6, 7], // 0: Coastline
            [8, 9, 10, 11, 12, 13, 14, 15], // 1: Clear
            [16, 17, 18, 19, 20, 21, 22, 23], // 2: Cliff
        ];
        info.templatesByType = {
            "Coastline": [],
            "Clear": [],
            "Cliff": [],
        };
        for (const templateName of Object.keys(info.TemplatePaths).toSorted()) {
            const template = info.TemplatePaths[templateName];
            template.Path = template.Path.map(p => ({x: p[0] | 0, y: p[1] | 0}));
            template.PathND = template.PathND.map(p => ({x: p[0] | 0, y: p[1] | 0}));
            template.Name = templateName;
            template.StartDir = letterToDirection(template.StartDir);
            template.EndDir = letterToDirection(template.EndDir);
            template.StartType ??= template.Type;
            template.EndType ??= template.Type;
            template.StartTypeN = info.typeNs[template.StartType];
            template.EndTypeN = info.typeNs[template.EndType];
            template.StartBorderN = info.borderNs[template.StartTypeN][template.StartDir];
            template.EndBorderN = info.borderNs[template.EndTypeN][template.EndDir];
            template.MovesX = template.Path[template.Path.length-1].x - template.Path[0].x;
            template.MovesY = template.Path[template.Path.length-1].y - template.Path[0].y;
            template.OffsetX = template.Path[0].x;
            template.OffsetY = template.Path[0].y;
            template.Progress = template.Path.length - 1;
            template.ProgressLow = Math.ceil(template.Progress / 2);
            template.ProgressHigh = Math.floor(template.Progress * 1.5);
            info.templatesByType[template.Type].push(template);
            // Last point has no direction.
            for (let i = 0; i < template.PathND.length - 1; i++) {
                template.PathND[i].d = calculateDirectionPoints(template.PathND[i], template.PathND[i+1]);
            }
            template.PathND[template.PathND.length - 1].d = DIRECTION_NONE;
            template.RelPathND = template.PathND.map(p => ({
                x: p.x - template.OffsetX,
                y: p.y - template.OffsetY,
                d: p.d, // direction
                dm: 1 << p.d, // direction mask
                dmr: 1 << reverseDirection(p.d), // direction mask reverse
            }));
        }

        info.replaceabilityMap = {};
        info.playabilityMap = {};
        for (const tileName of Object.keys(info.TileInfo)) {
            const tile = info.TileInfo[tileName];
            switch (tile.Type) {
            case "Beach":
            case "Clear":
            case "Gems":
            case "Ore":
            case "Road":
            case "Rough":
            case "Water":
                info.playabilityMap[tileName] = PLAYABILITY_PLAYABLE;
                break;
            default:
                info.playabilityMap[tileName] = PLAYABILITY_UNPLAYABLE;
                break;
            }
        }

        // Category-based behavior overrides
        info.replaceabilityMap["t1i0"] = REPLACEABILITY_TILE;
        for (const templateName of Object.keys(info.Tileset.Templates)) {
            const template = info.Tileset.Templates[templateName];
            for (const ti of Object.keys(template.Tiles)) {
                const tile = `t${template.Id}i${ti}`;
                switch (template.Categories) {
                case "Cliffs":
                    if (template.Tiles[ti] === "Rock") {
                        info.replaceabilityMap[tile] = REPLACEABILITY_NONE;
                    } else {
                        info.replaceabilityMap[tile] = REPLACEABILITY_ENTITY;
                    }
                    break;
                case "Beach":
                    info.replaceabilityMap[tile] = REPLACEABILITY_TILE;
                    if (info.playabilityMap[tile] === PLAYABILITY_UNPLAYABLE) {
                        info.playabilityMap[tile] = PLAYABILITY_PARTIAL;
                    }
                default:
                    // Do nothing
                    break;
                }
            }
        }

        ready = true;
        const base64Settings = (new URLSearchParams(location.search)).get("settings");
        if (base64Settings !== null) {
            writeSettings(JSON.parse(atob(base64Settings)));
        } else {
            writeSettings({});
        }
        // Hack: requestAnimationFrame so that the generation status shows up.
        beginGenerate();
    })
    .catch((err) => {
        alert("Failure in early startup. Check console.");
        console.error(err);
    });
