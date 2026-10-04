import { initializeApp } from 'https://www.gstatic.com/firebasejs/10.12.5/firebase-app.js';
import { getFirestore, doc, getDoc, getDocFromServer, setDoc, updateDoc, FieldPath, connectFirestoreEmulator } from 'https://www.gstatic.com/firebasejs/10.12.5/firebase-firestore.js';

let db;
let cacheDatabase;
let dataChanged = false;
const candleMemory = new Map();
const sessionRevisions = new Map();
const memoryLimit = 96;
const sharedUserId = '4qjqaWnyjQW5HVYAuaezaxBJYP02';

function cacheKey(symbol, timeframe, month) {
    return `${requireUser()}|${symbol}|${timeframe}|${month}`;
}

function revisionKey(uid) { return `dual-cycle-revision:${uid}`; }

function savedRevision(uid) {
    try { return localStorage.getItem(revisionKey(uid)); }
    catch { return sessionRevisions.get(uid) ?? null; }
}

function saveRevision(uid, value) {
    sessionRevisions.set(uid, value);
    try { localStorage.setItem(revisionKey(uid), value); }
    catch { /* The cache still works for this browser session. */ }
}

function remember(key, bars) {
    candleMemory.delete(key);
    candleMemory.set(key, bars);
    if (candleMemory.size > memoryLimit) candleMemory.delete(candleMemory.keys().next().value);
}

function openCache() {
    if (!cacheDatabase) {
        cacheDatabase = new Promise(resolve => {
            if (!('indexedDB' in window)) { resolve(null); return; }
            try {
                const request = indexedDB.open('dual-cycle-candles', 1);
                request.onupgradeneeded = () => request.result.createObjectStore('candles');
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => resolve(null);
                request.onblocked = () => resolve(null);
            } catch { resolve(null); }
        });
    }
    return cacheDatabase;
}

async function cachedBars(key) {
    if (candleMemory.has(key)) {
        const bars = candleMemory.get(key);
        remember(key, bars);
        return bars;
    }
    const database = await openCache();
    if (!database) return undefined;
    try {
        return await new Promise(resolve => {
            const request = database.transaction('candles', 'readonly').objectStore('candles').get(key);
            request.onsuccess = () => {
                if (request.result !== undefined) remember(key, request.result);
                resolve(request.result);
            };
            request.onerror = () => resolve(undefined);
        });
    } catch { return undefined; }
}

async function cachedMany(keys) {
    const values = keys.map(key => candleMemory.has(key) ? candleMemory.get(key) : undefined);
    if (values.every(value => value !== undefined)) return values;
    const database = await openCache();
    if (!database) return values;
    try {
        return await new Promise(resolve => {
            const transaction = database.transaction('candles', 'readonly');
            const store = transaction.objectStore('candles');
            keys.forEach((key, index) => {
                if (values[index] !== undefined) return;
                const request = store.get(key);
                request.onsuccess = () => { values[index] = request.result; };
            });
            transaction.oncomplete = () => resolve(values);
            transaction.onerror = () => resolve(values);
            transaction.onabort = () => resolve(values);
        });
    } catch { return values; }
}

async function loadBars(symbol, timeframe, month, cached) {
    if (cached !== undefined) return cached;
    const snapshot = await getDoc(candleRef(symbol, timeframe, month));
    const bars = snapshot.exists() ? Object.values(snapshot.data().bars ?? {}) : [];
    await storeBars(cacheKey(symbol, timeframe, month), bars);
    return bars;
}

async function storeBars(key, bars) {
    remember(key, bars);
    const database = await openCache();
    if (!database) return;
    try {
        await new Promise(resolve => {
            const transaction = database.transaction('candles', 'readwrite');
            transaction.objectStore('candles').put(bars, key);
            transaction.oncomplete = resolve;
            transaction.onerror = resolve;
            transaction.onabort = resolve;
        });
    } catch { /* Browser storage is optional. */ }
}

async function storeMany(entries) {
    if (!entries.length) return;
    for (const [key, bars] of entries) remember(key, bars);
    const database = await openCache();
    if (!database) return;
    try {
        await new Promise(resolve => {
            const transaction = database.transaction('candles', 'readwrite');
            const store = transaction.objectStore('candles');
            for (const [key, bars] of entries) store.put(bars, key);
            transaction.oncomplete = resolve;
            transaction.onerror = resolve;
            transaction.onabort = resolve;
        });
    } catch { /* Browser storage is optional. */ }
}

async function clearCache() {
    candleMemory.clear();
    const database = await openCache();
    if (!database) return;
    try {
        await new Promise(resolve => {
            const transaction = database.transaction('candles', 'readwrite');
            transaction.objectStore('candles').clear();
            transaction.oncomplete = resolve;
            transaction.onerror = resolve;
            transaction.onabort = resolve;
        });
    } catch { /* Browser storage is optional. */ }
}

function requireUser() {
    return sharedUserId;
}

function candleRef(symbol, timeframe, month) {
    const uid = requireUser();
    const safe = encodeURIComponent(symbol);
    return doc(db, 'users', uid, 'candles', `${safe}_${timeframe}_${month}`);
}

function monthsBetween(start, end) {
    if (start.length === 4) {
        const result = [];
        for (let year = Number(start); year <= Number(end); year++) result.push(String(year));
        return result;
    }
    const result = [];
    let year = Number(start.slice(0, 4));
    let month = Number(start.slice(4, 6));
    const lastYear = Number(end.slice(0, 4));
    const lastMonth = Number(end.slice(4, 6));
    while (year < lastYear || (year === lastYear && month <= lastMonth)) {
        result.push(`${year}${String(month).padStart(2, '0')}`);
        month++;
        if (month === 13) { month = 1; year++; }
    }
    return result;
}

function candleKey(candle) {
    return candle.Time.replace(/[-:T]/g, '').slice(0, 12);
}

function sameCandle(left, right) {
    return candleKey(left) === candleKey(right) &&
        ['Open', 'High', 'Low', 'Close', 'Volume'].every(field =>
            Number(left[field]) === Number(right[field]));
}

window.stockApp = {
    async initialize(config) {
        const app = initializeApp(config);
        db = getFirestore(app);
        if (config.useEmulators) {
            connectFirestoreEmulator(db, 'localhost', 8080);
        }
    },
    async syncRevision() {
        const uid = requireUser();
        const snapshot = await getDocFromServer(doc(db, 'users', uid, 'state', 'data-revision'));
        const revision = snapshot.exists() ? snapshot.data().value : '';
        if (savedRevision(uid) !== revision) {
            await clearCache();
            saveRevision(uid, revision);
            return true;
        }
        return false;
    },
    async publishRevision() {
        if (!dataChanged) return;
        const uid = requireUser();
        const revision = `${Date.now()}-${Math.random()}`;
        await setDoc(doc(db, 'users', uid, 'state', 'data-revision'), { value: revision });
        saveRevision(uid, revision);
        dataChanged = false;
    },
    async getCandles(symbol, timeframe, fromMonth, toMonth) {
        const periods = monthsBetween(fromMonth, toMonth);
        const groups = await Promise.all(periods.map(async month => {
            const key = cacheKey(symbol, timeframe, month);
            return loadBars(symbol, timeframe, month, await cachedBars(key));
        }));
        const all = groups.flat();
        return JSON.stringify(all.sort((a, b) => a.Time.localeCompare(b.Time)));
    },
    async getCandlesBatch(symbols, timeframe, fromMonth, toMonth) {
        const periods = monthsBetween(fromMonth, toMonth);
        const entries = symbols.flatMap(symbol => periods.map(month => ({ symbol, month })));
        const cached = await cachedMany(entries.map(({ symbol, month }) => cacheKey(symbol, timeframe, month)));
        const groups = await Promise.all(entries.map(async ({ symbol, month }, index) => {
            if (cached[index] !== undefined) return cached[index];
            const snapshot = await getDoc(candleRef(symbol, timeframe, month));
            return snapshot.exists() ? Object.values(snapshot.data().bars ?? {}) : [];
        }));
        await storeMany(entries.flatMap(({ symbol, month }, index) => cached[index] === undefined
            ? [[cacheKey(symbol, timeframe, month), groups[index]]] : []));
        const result = [];
        for (let index = 0; index < symbols.length; index++) {
            const bars = groups.slice(index * periods.length, (index + 1) * periods.length).flat();
            result.push(bars.sort((a, b) => a.Time.localeCompare(b.Time)));
        }
        return JSON.stringify(result);
    },
    async getLatest(symbol, timeframe) {
        const now = new Date();
        for (let offset = 0; offset < (timeframe === 'D' ? 3 : 24); offset++) {
            const monthDate = new Date(now.getFullYear(), now.getMonth() - offset, 1);
            const month = timeframe === 'D' ? String(now.getFullYear() - offset)
                : `${monthDate.getFullYear()}${String(monthDate.getMonth() + 1).padStart(2, '0')}`;
            const snapshot = await getDoc(candleRef(symbol, timeframe, month));
            if (snapshot.exists()) {
                const keys = Object.keys(snapshot.data().bars ?? {}).sort();
                if (keys.length) return snapshot.data().bars[keys.at(-1)].Time;
            }
        }
        return null;
    },
    async upsertCandles(symbol, timeframe, json, preserveExisting) {
        const groups = new Map();
        for (const candle of JSON.parse(json)) {
            const month = candleKey(candle).slice(0, timeframe === 'D' ? 4 : 6);
            if (!groups.has(month)) groups.set(month, new Map());
            groups.get(month).set(candleKey(candle), candle);
        }
        const counts = { added: 0, updated: 0, skipped: 0 };
        for (const [month, values] of groups) {
            const ref = candleRef(symbol, timeframe, month);
            const snapshot = await getDoc(ref);
            const existing = snapshot.exists() ? (snapshot.data().bars ?? {}) : {};
            const changed = {};
            for (const [key, candle] of values) {
                if (!(key in existing)) { changed[key] = candle; counts.added++; }
                else if (!preserveExisting && !sameCandle(existing[key], candle)) {
                    changed[key] = candle; counts.updated++;
                } else counts.skipped++;
            }
            if (Object.keys(changed).length) {
                if (!snapshot.exists()) await setDoc(ref, { symbol, timeframe, month, bars: changed }, { merge: true });
                else {
                    const args = [];
                    for (const [key, candle] of Object.entries(changed)) {
                        args.push(new FieldPath('bars', key), candle);
                    }
                    await updateDoc(ref, ...args);
                }
                await storeBars(cacheKey(symbol, timeframe, month), Object.values({ ...existing, ...changed }));
                dataChanged = true;
            }
        }
        return counts;
    },
    async getJson(name) {
        const snapshot = await getDoc(doc(db, 'users', requireUser(), 'state', name));
        return snapshot.exists() ? snapshot.data().json : null;
    },
    async putJson(name, json) {
        await setDoc(doc(db, 'users', requireUser(), 'state', name), { json });
    },
    download(name, content, mime) {
        const link = document.createElement('a');
        link.href = URL.createObjectURL(new Blob([content], { type: mime }));
        link.download = name;
        link.click();
        setTimeout(() => URL.revokeObjectURL(link.href), 1000);
    },
    downloadBase64(name, encoded, mime) {
        const binary = atob(encoded);
        const bytes = new Uint8Array(binary.length);
        for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
        this.download(name, bytes, mime);
    }
};
