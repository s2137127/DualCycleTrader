import { initializeApp } from 'https://www.gstatic.com/firebasejs/10.12.5/firebase-app.js';
import { getAuth, signInWithEmailAndPassword, signOut, onAuthStateChanged, connectAuthEmulator } from 'https://www.gstatic.com/firebasejs/10.12.5/firebase-auth.js';
import { getFirestore, doc, getDoc, setDoc, updateDoc, FieldPath, connectFirestoreEmulator } from 'https://www.gstatic.com/firebasejs/10.12.5/firebase-firestore.js';

let auth;
let db;

function requireUser() {
    if (!auth?.currentUser) throw new Error('請先登入 Firebase。');
    return auth.currentUser.uid;
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
        auth = getAuth(app);
        db = getFirestore(app);
        if (config.useEmulators) {
            connectAuthEmulator(auth, 'http://localhost:9099');
            connectFirestoreEmulator(db, 'localhost', 8080);
        }
        return await new Promise(resolve => onAuthStateChanged(auth, user => resolve(user?.email ?? null)));
    },
    async signIn(email, password) {
        const result = await signInWithEmailAndPassword(auth, email, password);
        return result.user.email;
    },
    async signOut() { await signOut(auth); },
    async getCandles(symbol, timeframe, fromMonth, toMonth) {
        const all = [];
        for (const month of monthsBetween(fromMonth, toMonth)) {
            const snapshot = await getDoc(candleRef(symbol, timeframe, month));
            if (snapshot.exists()) all.push(...Object.values(snapshot.data().bars ?? {}));
        }
        return JSON.stringify(all.sort((a, b) => a.Time.localeCompare(b.Time)));
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
