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

    addBackdrop();
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
        const style = STYLE[cat] ?? { color: '#FFFFFF', radius: 4, opacity: 1 };
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

function addBackdrop() {
    // Subtle SVG grid backdrop covering a generous area around the world bounds.
    // No actual game-art tiles — see ADR-0013 for why.
    const gridSize = 50; // each grid square = 50 Leaflet units = 500m
    const extent = 1000; // ±1000 Leaflet units covers the whole world
    const svg = makeGridSvg(gridSize, extent);
    const bounds = [[-extent, -extent], [extent, extent]];
    backdropLayer = L.svgOverlay(svg, bounds, { interactive: false, opacity: 0.5 });
    backdropLayer.addTo(map);
}

function makeGridSvg(gridSize, extent) {
    const ns = 'http://www.w3.org/2000/svg';
    const svg = document.createElementNS(ns, 'svg');
    const span = extent * 2;
    svg.setAttribute('viewBox', `${-extent} ${-extent} ${span} ${span}`);
    svg.setAttribute('preserveAspectRatio', 'none');

    // Minor grid (every gridSize units, faint).
    const minor = document.createElementNS(ns, 'g');
    minor.setAttribute('stroke', 'rgba(250, 149, 73, 0.06)');
    minor.setAttribute('stroke-width', '0.5');
    minor.setAttribute('vector-effect', 'non-scaling-stroke');
    for (let v = -extent; v <= extent; v += gridSize) {
        const h = document.createElementNS(ns, 'line');
        h.setAttribute('x1', -extent); h.setAttribute('y1', v);
        h.setAttribute('x2',  extent); h.setAttribute('y2', v);
        minor.appendChild(h);
        const w = document.createElementNS(ns, 'line');
        w.setAttribute('x1', v); w.setAttribute('y1', -extent);
        w.setAttribute('x2', v); w.setAttribute('y2',  extent);
        minor.appendChild(w);
    }
    svg.appendChild(minor);

    // Major grid (every 5 × gridSize, brighter).
    const major = document.createElementNS(ns, 'g');
    major.setAttribute('stroke', 'rgba(250, 149, 73, 0.18)');
    major.setAttribute('stroke-width', '1');
    major.setAttribute('vector-effect', 'non-scaling-stroke');
    for (let v = -extent; v <= extent; v += gridSize * 5) {
        const h = document.createElementNS(ns, 'line');
        h.setAttribute('x1', -extent); h.setAttribute('y1', v);
        h.setAttribute('x2',  extent); h.setAttribute('y2', v);
        major.appendChild(h);
        const w = document.createElementNS(ns, 'line');
        w.setAttribute('x1', v); w.setAttribute('y1', -extent);
        w.setAttribute('x2', v); w.setAttribute('y2',  extent);
        major.appendChild(w);
    }
    svg.appendChild(major);

    // Origin crosshair.
    const origin = document.createElementNS(ns, 'g');
    origin.setAttribute('stroke', 'rgba(250, 149, 73, 0.4)');
    origin.setAttribute('stroke-width', '1.5');
    origin.setAttribute('vector-effect', 'non-scaling-stroke');
    const cx = document.createElementNS(ns, 'line');
    cx.setAttribute('x1', -gridSize); cx.setAttribute('y1', 0);
    cx.setAttribute('x2',  gridSize); cx.setAttribute('y2', 0);
    origin.appendChild(cx);
    const cy = document.createElementNS(ns, 'line');
    cy.setAttribute('x1', 0); cy.setAttribute('y1', -gridSize);
    cy.setAttribute('x2', 0); cy.setAttribute('y2',  gridSize);
    origin.appendChild(cy);
    svg.appendChild(origin);

    return svg;
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
    const skip = new Set(['category', 'kind']);
    for (const [k, v] of Object.entries(p)) {
        if (skip.has(k) || v == null) continue;
        rows.push(`<div class="fx-popup-row"><span>${escapeHtml(k)}</span><code>${escapeHtml(String(v))}</code></div>`);
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
