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
let dotNetCallback = null; // Optional Blazor handle for resource-node clicks (#42).

const STYLE = {
    'resource-node': { color: '#FFC53D', radius: 3.5, opacity: 0.85, label: 'Resource nodes' },
    'miner':         { color: '#FA9549', radius: 6,   opacity: 1.0,  label: 'Miners' },
    'building':      { color: '#5FB0C9', radius: 5,   opacity: 1.0,  label: 'Production buildings' },
    'belt':          { color: '#7BB66B', radius: 1.8, opacity: 0.5,  label: 'Conveyor belts' },
    'generator':     { color: '#E5604A', radius: 6,   opacity: 1.0,  label: 'Generators' },
};

// Fallback styling for resource nodes whose resource isn't known yet (no
// manual override + no entry in the bundled dataset). Once the resource is
// known we render the wiki icon (Desc_OreIron_C.png etc.) instead. Geysers
// don't need ore identification (always geothermal) and deposits are small
// destructible ore piles — both can stay dot-shaped.
const NODE_KIND_FALLBACK = {
    'MiningNode':         { color: '#FFC53D', size: 10, label: 'Mining node (unknown ore)' },
    'Geyser':             { color: '#5FB0C9', size: 12, label: 'Geothermal geyser' },
    'Deposit':            { color: '#9A9AA0', size: 7,  label: 'Resource deposit' },
    'FrackingCore':       { color: '#B388EB', size: 12, label: 'Fracking core (unknown resource)' },
    'FrackingSatellite':  { color: '#7E5DC5', size: 9,  label: 'Fracking satellite (unknown resource)' },
};

export async function initialize(element, featureCollection, callback) {
    await ensureLeafletLoaded();
    if (map) {
        map.remove();
        map = null;
    }

    geojson = featureCollection;
    dotNetCallback = callback ?? null;

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
    dotNetCallback = null;
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
    const visit = (x, y) => {
        if (x < minX) minX = x;
        if (y < minY) minY = y;
        if (x > maxX) maxX = x;
        if (y > maxY) maxY = y;
    };
    for (const f of features) {
        const g = f.geometry;
        if (g.type === 'LineString') {
            for (const [x, y] of g.coordinates) visit(x, y);
        } else {
            const [x, y] = g.coordinates;
            visit(x, y);
        }
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
        const cat = feature.properties.category;
        const style = STYLE[cat] ?? { color: '#FFFFFF', radius: 4, opacity: 1 };

        const target = layers[cat] ?? (layers[cat] = L.layerGroup());
        const shape = buildShape(feature, style);
        if (!shape) continue;

        shape.bindTooltip(tooltipText(feature), {
            direction: 'top',
            offset: [0, -4],
            opacity: 0.95,
            className: 'fx-map-tooltip',
        });
        shape.bindPopup(popupHtml(feature));

        // Resource-node markers are click-editable when a Blazor callback is
        // wired up (#42 manual overrides). Other categories don't need this.
        if (cat === 'resource-node' && dotNetCallback) {
            const reference = feature.properties.kind;
            shape.on('click', () => {
                // invokeMethodAsync is the standard Blazor JS-interop entry; the
                // C# side has a [JSInvokable] method matching this signature.
                dotNetCallback.invokeMethodAsync('OnResourceNodeClicked', reference)
                    .catch(err => console.error('Resource-node click callback failed:', err));
            });
        }

        target.addLayer(shape);
        target._featureCount = (target._featureCount ?? 0) + 1;
    }
    return layers;
}

function buildShape(feature, style) {
    const g = feature.geometry;
    if (g.type === 'LineString') {
        const latlngs = g.coordinates.map(([x, y]) => unrealToLatLng(x, y));
        // Belts use the canvas renderer (preferCanvas: true on the map) so even
        // dense Mk1 cluster routes stay performant. Stroke weight is small but
        // visible; tier-coloured via the category STYLE.
        return L.polyline(latlngs, {
            color: style.color,
            weight: 1.5,
            opacity: Math.min(0.9, (style.opacity ?? 0.5) + 0.3),
        });
    }
    const [x, y] = g.coordinates;
    const latlng = unrealToLatLng(x, y);

    // Resource nodes render as wiki item icons when the resource is known;
    // otherwise fall back to a coloured dot keyed by BP kind (#61).
    if (feature.properties.category === 'resource-node') {
        return L.marker(latlng, { icon: buildResourceNodeIcon(feature) });
    }

    return L.circleMarker(latlng, {
        radius: style.radius,
        color: style.color,
        fillColor: style.color,
        fillOpacity: style.opacity,
        weight: 1,
        opacity: style.opacity,
    });
}

function buildResourceNodeIcon(feature) {
    const p = feature.properties;
    const kind = p.nodeKind ?? 'MiningNode';
    const resource = typeof p.resource === 'string' && p.resource.length > 0 ? p.resource : null;

    // Resource known → wiki ore icon. The <img> falls back to the
    // coloured-dot div via onerror when an icon is missing on disk (assets
    // are dev-local per ADR-0016, not every Desc_*_C will have a PNG yet).
    if (resource) {
        const size = kind === 'FrackingSatellite' ? 18 : 22;
        const half = size / 2;
        const safeResource = String(resource).replace(/[^A-Za-z0-9_]/g, '');
        const fallbackColor = NODE_KIND_FALLBACK[kind]?.color ?? '#FFC53D';
        const html =
            `<div class="fx-node-icon" style="width:${size}px;height:${size}px;">
                <img src="/assets/icons/items/${safeResource}.png"
                     alt=""
                     onerror="this.style.display='none';this.parentElement.classList.add('fx-node-icon--missing');this.parentElement.style.backgroundColor='${fallbackColor}';" />
             </div>`;
        return L.divIcon({
            html,
            className: 'fx-node-divicon',
            iconSize: [size, size],
            iconAnchor: [half, half],
        });
    }

    const fb = NODE_KIND_FALLBACK[kind] ?? NODE_KIND_FALLBACK['MiningNode'];
    const half = fb.size / 2;
    const html =
        `<div class="fx-node-dot fx-node-dot--${kind.toLowerCase()}"
              style="width:${fb.size}px;height:${fb.size}px;background-color:${fb.color};"></div>`;
    return L.divIcon({
        html,
        className: 'fx-node-divicon',
        iconSize: [fb.size, fb.size],
        iconAnchor: [half, half],
    });
}

// -------------------------------------------------------------------------
// Backdrops
//
// User-selectable wiki-sourced game maps (ADR-0015) or no backdrop:
// - terrain / biome / water: /lib/maps/{terrain.jpg, biome.jpg, water.png}
// - none: dark canvas only
//
// Selection persisted in localStorage under 'erp-map-backdrop'.
// Default: 'terrain'.
//
// Per-image `bounds` in Unreal cm align the imagery with marker positions
// (issue #43). The three wiki images share the same projection — a 5000×5000
// (or 2500×2500) square covering the playable world centered at world
// origin (Grass Fields). When `bounds` is absent we fall back to the padded
// resource-node extent, which is roughly correct but pixel-imprecise.
// -------------------------------------------------------------------------

const WIKI_MAP_BOUNDS = { minX: -324698, maxX: 425302, minY: -375000, maxY: 375000 };

const MAP_BACKDROPS = {
    'terrain': { kind: 'image', src: '/lib/maps/terrain.jpg', label: 'Terrain (wiki)', bounds: WIKI_MAP_BOUNDS },
    'biome':   { kind: 'image', src: '/lib/maps/biome.jpg',   label: 'Biome (wiki)',   bounds: WIKI_MAP_BOUNDS },
    'water':   { kind: 'image', src: '/lib/maps/water.png',   label: 'Water (wiki)',   bounds: WIKI_MAP_BOUNDS },
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
        const ext = backdrop.bounds ?? resourceNodeExtent(featureCollection, 0.15);
        addImageBackdrop(backdrop.src, ext);
    }
}

function addImageBackdrop(imageSrc, ext) {
    const sw = unrealToLatLng(ext.minX, ext.maxY);
    const ne = unrealToLatLng(ext.maxX, ext.minY);
    backdropLayer = L.imageOverlay(imageSrc, [sw, ne], {
        interactive: false,
        opacity: 1.0,
        className: 'fx-map-backdrop',
    });
    backdropLayer.addTo(map);
}

function resourceNodeExtent(featureCollection, padFraction) {
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    let count = 0;
    for (const f of featureCollection.features) {
        if (f.properties?.category !== 'resource-node') continue;
        const [x, y] = f.geometry.coordinates;
        if (x < minX) minX = x;
        if (y < minY) minY = y;
        if (x > maxX) maxX = x;
        if (y > maxY) maxY = y;
        count++;
    }
    if (count < 3) {
        // Fallback to nominal Satisfactory world bounds.
        return { minX: -400000, maxX: 400000, minY: -400000, maxY: 400000 };
    }
    const padX = (maxX - minX) * padFraction;
    const padY = (maxY - minY) * padFraction;
    return {
        minX: minX - padX, maxX: maxX + padX,
        minY: minY - padY, maxY: maxY + padY,
    };
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
