// =========================================================================
// factory-map.js
//
// Thin JSInterop layer over Leaflet for /factory/map.
// - Leaflet is loaded as a global (window.L) from /lib/leaflet/leaflet.js
// - Uses CRS.Simple — coordinates are raw Unreal X/Y centimetres; the
//   server hands us Unreal Y verbatim and we negate it on display so
//   north (Unreal -Y) is up on screen.
// =========================================================================

let map = null;
let categoryLayers = {};
let geojson = null;
let backdropLayer = null;

const STYLE = {
    'resource-node': { color: '#FFC53D', radius: 3.5, opacity: 0.85, label: 'Resource nodes' },
    'miner':         { color: '#FA9549', radius: 6,   opacity: 1.0,  label: 'Miners' },
    'building':      { color: '#5FB0C9', radius: 5,   opacity: 1.0,  label: 'Production buildings' },
    'belt':          { color: '#7BB66B', radius: 1.8, opacity: 0.5,  label: 'Conveyor belts' },
    'generator':     { color: '#E5604A', radius: 6,   opacity: 1.0,  label: 'Generators' },
};

// Resource-node sub-styles by BP kind. Geysers stand out in blue (water /
// geothermal) and deposits/fracking get distinct hues so they're not lost in
// the mining-node sea.
const NODE_KIND_STYLE = {
    'MiningNode':         { color: '#FFC53D', radius: 3.5, opacity: 0.85 },
    'Geyser':             { color: '#5FB0C9', radius: 5,   opacity: 1.0  },
    'Deposit':            { color: '#9A9AA0', radius: 2.2, opacity: 0.6  },
    'FrackingCore':       { color: '#B388EB', radius: 5,   opacity: 1.0  },
    'FrackingSatellite':  { color: '#7E5DC5', radius: 4,   opacity: 0.9  },
};

export async function initialize(element, featureCollection) {
    await ensureLeafletLoaded();
    if (map) {
        map.remove();
        map = null;
    }

    geojson = featureCollection;

    map = L.map(element, {
        crs: L.CRS.Simple,
        minZoom: -8,
        maxZoom: 3,
        zoomSnap: 0.25,
        zoomDelta: 0.5,
        attributionControl: false,
        zoomControl: true,
        preferCanvas: true, // canvas renderer is faster for many markers
    });

    addBackdrop(featureCollection);
    categoryLayers = buildCategoryLayers(featureCollection);

    const bounds = computeBounds(featureCollection.features);
    if (bounds) {
        map.fitBounds(bounds, { padding: [40, 40] });
    } else {
        map.setView([0, 0], 0);
    }

    // Show everything by default.
    Object.values(categoryLayers).forEach(l => l.addTo(map));

    return Object.keys(categoryLayers).map(k => ({
        category: k,
        label: STYLE[k]?.label ?? k,
        color: STYLE[k]?.color ?? '#FFFFFF',
        count: categoryLayers[k]._featureCount ?? 0,
    }));
}

export function setLayerVisible(category, visible) {
    const layer = categoryLayers[category];
    if (!layer || !map) return;
    if (visible && !map.hasLayer(layer)) layer.addTo(map);
    if (!visible && map.hasLayer(layer)) map.removeLayer(layer);
}

export function dispose() {
    if (map) {
        map.remove();
        map = null;
    }
    categoryLayers = {};
    geojson = null;
    backdropLayer = null;
}

// -------------------------------------------------------------------------
// Internals
// -------------------------------------------------------------------------

async function ensureLeafletLoaded() {
    if (window.L) return;

    // Inject Leaflet CSS once.
    if (!document.querySelector('link[data-leaflet]')) {
        const css = document.createElement('link');
        css.rel = 'stylesheet';
        css.href = '/lib/leaflet/leaflet.css';
        css.dataset.leaflet = '1';
        document.head.appendChild(css);
    }

    // Inject Leaflet script and wait for it.
    await new Promise((resolve, reject) => {
        const existing = document.querySelector('script[data-leaflet]');
        if (existing) {
            // Already injected by a previous navigation; wait for window.L.
            const check = () => window.L ? resolve() : setTimeout(check, 30);
            check();
            return;
        }
        const script = document.createElement('script');
        script.src = '/lib/leaflet/leaflet.js';
        script.dataset.leaflet = '1';
        script.onload = resolve;
        script.onerror = () => reject(new Error('Failed to load /lib/leaflet/leaflet.js'));
        document.head.appendChild(script);
    });
}

function unrealToLatLng(x, y) {
    // Leaflet [lat, lng] = [screen-y, screen-x]. Negate Unreal Y so north
    // (Unreal -Y direction) points up on screen. Scale down so the numbers
    // are more manageable for Leaflet's zoom range — Unreal coords are in
    // centimetres and the world spans roughly ±400,000 cm.
    const k = 0.001; // 1 unit = 10 metres
    return [-y * k, x * k];
}

function computeBounds(features) {
    if (!features || features.length === 0) return null;
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    for (const f of features) {
        const [x, y] = f.geometry.coordinates;
        if (x < minX) minX = x;
        if (y < minY) minY = y;
        if (x > maxX) maxX = x;
        if (y > maxY) maxY = y;
    }
    return [unrealToLatLng(minX, minY), unrealToLatLng(maxX, maxY)];
}

function buildCategoryLayers(featureCollection) {
    const layers = {};
    for (const cat of Object.keys(STYLE)) {
        layers[cat] = L.layerGroup();
        layers[cat]._featureCount = 0;
    }

    for (const feature of featureCollection.features) {
        const [x, y] = feature.geometry.coordinates;
        const cat = feature.properties.category;
        let style = STYLE[cat] ?? { color: '#FFFFFF', radius: 4, opacity: 1 };

        // Resource nodes get per-kind sub-styling.
        if (cat === 'resource-node') {
            const kind = feature.properties.nodeKind;
            const sub = NODE_KIND_STYLE[kind];
            if (sub) style = { ...style, ...sub };
        }

        const target = layers[cat] ?? (layers[cat] = L.layerGroup());

        const marker = L.circleMarker(unrealToLatLng(x, y), {
            radius: style.radius,
            color: style.color,
            fillColor: style.color,
            fillOpacity: style.opacity,
            weight: 1,
            opacity: style.opacity,
        });

        marker.bindTooltip(tooltipText(feature), {
            direction: 'top',
            offset: [0, -4],
            opacity: 0.95,
            className: 'fx-map-tooltip',
        });
        marker.bindPopup(popupHtml(feature));

        target.addLayer(marker);
        target._featureCount = (target._featureCount ?? 0) + 1;
    }
    return layers;
}

// -------------------------------------------------------------------------
// Backdrops
//
// Several user-selectable options (ADR-0015):
// - terrain / biome / water: wiki-sourced game maps from
//   /lib/maps/{terrain.jpg, biome.jpg, water.png}
// - procedural: IDW heightfield from resource-node Z samples
//   (kept as a fallback for modded maps or users who prefer it)
// - none: no backdrop, dark canvas only
//
// Selection is persisted in localStorage under 'erp-map-backdrop'.
// Default: 'terrain'.
// -------------------------------------------------------------------------

const MAP_BACKDROPS = {
    'terrain': { kind: 'image', src: '/lib/maps/terrain.jpg', label: 'Terrain (wiki)' },
    'biome':   { kind: 'image', src: '/lib/maps/biome.jpg',   label: 'Biome (wiki)' },
    'water':   { kind: 'image', src: '/lib/maps/water.png',   label: 'Water (wiki)' },
    'procedural': { kind: 'procedural', label: 'Procedural (from save data)' },
    'none':    { kind: 'none', label: 'None (dark canvas)' },
};
const DEFAULT_BACKDROP = 'terrain';

function readBackdropChoice() {
    try {
        const saved = localStorage.getItem('erp-map-backdrop');
        if (saved && MAP_BACKDROPS[saved]) return saved;
    } catch (_) { /* localStorage may be unavailable */ }
    return DEFAULT_BACKDROP;
}

function addBackdrop(featureCollection) {
    const choice = readBackdropChoice();
    const backdrop = MAP_BACKDROPS[choice];
    if (!backdrop || backdrop.kind === 'none') return;

    if (backdrop.kind === 'image') {
        addImageBackdrop(featureCollection, backdrop.src);
        return;
    }
    if (backdrop.kind === 'procedural') {
        addTopographicBackdrop(featureCollection);
        return;
    }
}

function addImageBackdrop(featureCollection, imageSrc) {
    const samples = collectElevationSamples(featureCollection);
    // Use the same padded resource-node extent as the procedural backdrop
    // so markers align with the image. Wiki maps cover (approximately) the
    // playable world, which is what the resource nodes inhabit.
    const ext = samples.length >= 3
        ? computeUnrealExtent(samples, 0.15)
        : { minX: -400000, maxX: 400000, minY: -400000, maxY: 400000 };

    const sw = unrealToLatLng(ext.minX, ext.maxY);
    const ne = unrealToLatLng(ext.maxX, ext.minY);
    backdropLayer = L.imageOverlay(imageSrc, [sw, ne], {
        interactive: false,
        opacity: 1.0,
        className: 'fx-map-backdrop',
    });
    backdropLayer.addTo(map);
}

function addTopographicBackdrop(featureCollection) {
    const samples = collectElevationSamples(featureCollection);
    if (samples.length < 3) {
        // Not enough samples for IDW — skip backdrop, map will just be
        // dark canvas.
        return;
    }

    // Wider canvas extent than the data so the map doesn't end abruptly
    // at the outermost resource node. Pad by ~15%.
    const ext = computeUnrealExtent(samples, 0.15);
    const RES = 192;
    const canvas = renderHeightCanvas(samples, ext, RES);

    const sw = unrealToLatLng(ext.minX, ext.maxY); // south-west on Leaflet
    const ne = unrealToLatLng(ext.maxX, ext.minY); // north-east
    const bounds = [sw, ne];

    backdropLayer = L.imageOverlay(canvas.toDataURL('image/png'), bounds, {
        interactive: false,
        opacity: 1.0,
        className: 'fx-topo-backdrop',
    });
    backdropLayer.addTo(map);
}

function collectElevationSamples(featureCollection) {
    const samples = [];
    for (const f of featureCollection.features) {
        if (f.properties?.category !== 'resource-node') continue;
        const [x, y] = f.geometry.coordinates;
        const z = f.properties.z;
        if (typeof z !== 'number' || !Number.isFinite(z)) continue;
        samples.push([x, y, z]);
    }
    return samples;
}

function computeUnrealExtent(samples, padFraction) {
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    for (const [x, y] of samples) {
        if (x < minX) minX = x;
        if (y < minY) minY = y;
        if (x > maxX) maxX = x;
        if (y > maxY) maxY = y;
    }
    const padX = (maxX - minX) * padFraction;
    const padY = (maxY - minY) * padFraction;
    return {
        minX: minX - padX, maxX: maxX + padX,
        minY: minY - padY, maxY: maxY + padY,
    };
}

function renderHeightCanvas(samples, ext, res) {
    // Spatial hash for fast nearest-neighbour queries. Buckets cover the
    // extent in BUCKET × BUCKET cells; each bucket holds the indices of
    // samples that fall inside it.
    const BUCKETS = 16;
    const cellW = (ext.maxX - ext.minX) / BUCKETS;
    const cellH = (ext.maxY - ext.minY) / BUCKETS;
    const buckets = Array.from({ length: BUCKETS * BUCKETS }, () => []);
    for (let i = 0; i < samples.length; i++) {
        const [x, y] = samples[i];
        const bx = clamp(Math.floor((x - ext.minX) / cellW), 0, BUCKETS - 1);
        const by = clamp(Math.floor((y - ext.minY) / cellH), 0, BUCKETS - 1);
        buckets[by * BUCKETS + bx].push(i);
    }

    // First pass: interpolate Z per pixel via IDW (k=6, power=2) against
    // the up-to-9-bucket neighbourhood. Track min/max for the colormap.
    const heights = new Float32Array(res * res);
    let zMin = Infinity, zMax = -Infinity;
    for (let py = 0; py < res; py++) {
        const sy = ext.minY + ((py + 0.5) / res) * (ext.maxY - ext.minY);
        const by = clamp(Math.floor((sy - ext.minY) / cellH), 0, BUCKETS - 1);
        for (let px = 0; px < res; px++) {
            const sx = ext.minX + ((px + 0.5) / res) * (ext.maxX - ext.minX);
            const bx = clamp(Math.floor((sx - ext.minX) / cellW), 0, BUCKETS - 1);
            const z = idwAtPoint(sx, sy, samples, buckets, bx, by, BUCKETS, 6);
            heights[py * res + px] = z;
            if (z < zMin) zMin = z;
            if (z > zMax) zMax = z;
        }
    }

    // Hillshading — compute slope normals using the heights grid and
    // dot with a NW-from-above light direction.
    const canvas = document.createElement('canvas');
    canvas.width = res;
    canvas.height = res;
    const ctx = canvas.getContext('2d');
    const img = ctx.createImageData(res, res);
    const data = img.data;
    const zRange = (zMax - zMin) || 1;

    // Light direction (normalised): from NW at 45° altitude.
    const lx = -0.6, ly = 0.6, lz = 0.6;
    const lLen = Math.hypot(lx, ly, lz);
    const Lx = lx / lLen, Ly = ly / lLen, Lz = lz / lLen;

    // Slope exaggeration — Unreal Z is in centimetres, span ~50k cm
    // (500 m), but the grid pixel size is much smaller (~400 cm per
    // pixel here). Without exaggeration the surface looks flat.
    const slopeScale = 8.0 / zRange;

    for (let py = 0; py < res; py++) {
        for (let px = 0; px < res; px++) {
            const i = py * res + px;
            const z = heights[i];
            const t = (z - zMin) / zRange;
            const base = elevationColor(t);

            // Sobel-ish gradient for hillshade.
            const zL = heights[i - (px > 0 ? 1 : 0)];
            const zR = heights[i + (px < res - 1 ? 1 : 0)];
            const zU = heights[i - (py > 0 ? res : 0)];
            const zD = heights[i + (py < res - 1 ? res : 0)];
            const dzdx = (zR - zL) * slopeScale;
            const dzdy = (zD - zU) * slopeScale;
            // Surface normal ~ (-dzdx, -dzdy, 1) normalised.
            const nx = -dzdx, ny = -dzdy, nz = 1;
            const nLen = Math.hypot(nx, ny, nz) || 1;
            let shade = (nx * Lx + ny * Ly + nz * Lz) / nLen;
            shade = Math.max(0.55, Math.min(1.15, 0.85 + shade * 0.45));

            const r = clamp(base[0] * shade, 0, 255) | 0;
            const g = clamp(base[1] * shade, 0, 255) | 0;
            const b = clamp(base[2] * shade, 0, 255) | 0;
            const o = i * 4;
            data[o + 0] = r;
            data[o + 1] = g;
            data[o + 2] = b;
            data[o + 3] = 255;
        }
    }
    ctx.putImageData(img, 0, 0);
    return canvas;
}

function idwAtPoint(x, y, samples, buckets, bx, by, BUCKETS, k) {
    // Gather candidates from up to 9 neighbouring buckets.
    const cand = [];
    for (let dy = -1; dy <= 1; dy++) {
        const yy = by + dy;
        if (yy < 0 || yy >= BUCKETS) continue;
        for (let dx = -1; dx <= 1; dx++) {
            const xx = bx + dx;
            if (xx < 0 || xx >= BUCKETS) continue;
            const bucket = buckets[yy * BUCKETS + xx];
            for (let i = 0; i < bucket.length; i++) cand.push(bucket[i]);
        }
    }

    // Expand to 5×5 buckets if too few candidates (edges, sparse areas).
    if (cand.length < k) {
        cand.length = 0;
        for (let dy = -2; dy <= 2; dy++) {
            const yy = by + dy;
            if (yy < 0 || yy >= BUCKETS) continue;
            for (let dx = -2; dx <= 2; dx++) {
                const xx = bx + dx;
                if (xx < 0 || xx >= BUCKETS) continue;
                const bucket = buckets[yy * BUCKETS + xx];
                for (let i = 0; i < bucket.length; i++) cand.push(bucket[i]);
            }
        }
    }

    // Fall back to full scan if still nothing nearby — rare, edges only.
    if (cand.length === 0) {
        for (let i = 0; i < samples.length; i++) cand.push(i);
    }

    // Partial selection of k nearest. For small k vs N (k=6, N=20-50 per
    // bucket window) a sort is fine.
    const distSq = new Array(cand.length);
    for (let i = 0; i < cand.length; i++) {
        const s = samples[cand[i]];
        const dx = s[0] - x, dy = s[1] - y;
        distSq[i] = dx * dx + dy * dy;
    }
    const order = cand.map((_, i) => i).sort((a, b) => distSq[a] - distSq[b]);
    const take = Math.min(k, order.length);

    let sumW = 0, sumWz = 0;
    for (let i = 0; i < take; i++) {
        const idx = order[i];
        const d2 = distSq[idx];
        if (d2 < 1) return samples[cand[idx]][2]; // exact hit
        const w = 1 / d2; // power=2
        sumW += w;
        sumWz += w * samples[cand[idx]][2];
    }
    return sumWz / sumW;
}

// Color ramp for normalised elevation [0..1].
// Stops chosen to look "Satisfactory-natural" — dark water, sand at shore,
// rich grass mid-elevation, brown rock, snow on peaks. Returns [r, g, b].
const ELEVATION_STOPS = [
    [0.00, [22, 38, 58]],     // deep water
    [0.18, [40, 78, 110]],    // shallow water
    [0.22, [195, 175, 115]],  // beach / sand
    [0.32, [120, 145, 75]],   // light grass
    [0.55, [70, 105, 55]],    // forest
    [0.75, [110, 95, 75]],    // brown rock
    [0.88, [150, 145, 140]],  // light rock
    [1.00, [235, 235, 245]],  // snow
];

function elevationColor(t) {
    t = clamp(t, 0, 1);
    for (let i = 1; i < ELEVATION_STOPS.length; i++) {
        const [t1, c1] = ELEVATION_STOPS[i];
        if (t <= t1) {
            const [t0, c0] = ELEVATION_STOPS[i - 1];
            const u = (t - t0) / (t1 - t0 || 1);
            return [
                c0[0] + (c1[0] - c0[0]) * u,
                c0[1] + (c1[1] - c0[1]) * u,
                c0[2] + (c1[2] - c0[2]) * u,
            ];
        }
    }
    return ELEVATION_STOPS[ELEVATION_STOPS.length - 1][1];
}

function clamp(v, lo, hi) {
    return v < lo ? lo : v > hi ? hi : v;
}

function tooltipText(feature) {
    const p = feature.properties;
    return `${p.category} · ${shortName(p.kind)}`;
}

function popupHtml(feature) {
    const p = feature.properties;
    const rows = [
        `<div class="fx-popup-title">${escapeHtml(p.category)}</div>`,
        `<div class="fx-popup-kind">${escapeHtml(shortName(p.kind))}</div>`,
    ];
    // For production buildings, surface the human-readable recipe name first.
    if (p.recipeName) {
        rows.push(`<div class="fx-popup-row"><span>recipe</span><span>${escapeHtml(p.recipeName)}</span></div>`);
    }
    const skip = new Set(['category', 'kind', 'recipeName']);
    for (const [k, v] of Object.entries(p)) {
        if (skip.has(k) || v == null) continue;
        // Shorten verbose object-ref paths (e.g. resourceNode) to the trailing segment.
        const display = typeof v === 'string' && v.includes('.') ? shortName(v) : String(v);
        rows.push(`<div class="fx-popup-row"><span>${escapeHtml(k)}</span><code>${escapeHtml(display)}</code></div>`);
    }
    return `<div class="fx-popup">${rows.join('')}</div>`;
}

function shortName(typePath) {
    if (!typePath) return '(unknown)';
    const dot = typePath.lastIndexOf('.');
    return dot < 0 ? typePath : typePath.substring(dot + 1);
}

function escapeHtml(s) {
    return String(s).replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
}
