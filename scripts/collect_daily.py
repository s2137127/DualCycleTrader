"""Refresh Taiwan daily candles in the Firestore collection used by the web app."""

import argparse
import concurrent.futures
import datetime as dt
import json
import os
import random
import time
import urllib.error
import urllib.parse
import urllib.request
from zoneinfo import ZoneInfo


UID = "4qjqaWnyjQW5HVYAuaezaxBJYP02"
ORIGIN = "https://s2137127.github.io"
TAIPEI = ZoneInfo("Asia/Taipei")


def request(url, *, method="GET", body=None, attempts=4):
    payload = None if body is None else json.dumps(body).encode("utf-8")
    headers = {"Origin": ORIGIN, "User-Agent": "DualCycleTrader/daily-collector"}
    if payload is not None:
        headers["Content-Type"] = "application/json"
    for attempt in range(attempts):
        try:
            req = urllib.request.Request(url, data=payload, headers=headers, method=method)
            with urllib.request.urlopen(req, timeout=40) as response:
                return json.load(response)
        except urllib.error.HTTPError as error:
            if error.code == 404 and method == "GET":
                return None
            if error.code not in (429, 500, 502, 503, 504) or attempt == attempts - 1:
                detail = error.read(300).decode("utf-8", "replace")
                raise RuntimeError(f"HTTP {error.code} at {url}: {detail}") from error
        except (urllib.error.URLError, TimeoutError) as error:
            if attempt == attempts - 1:
                raise RuntimeError(f"Request failed at {url}: {error}") from error
        time.sleep(min(2 ** attempt + random.random(), 12))
    raise AssertionError("unreachable")


def candle_key(candle):
    return candle["Time"].replace("-", "").replace(":", "").replace("T", "")[:12]


def chart(worker, symbol, days):
    query = urllib.parse.urlencode({"symbol": symbol, "interval": "1d", "days": days})
    data = request(f"{worker}/chart?{query}")
    result = data["chart"]["result"][0]
    quote = result["indicators"]["quote"][0]
    bars = []
    for index, timestamp in enumerate(result.get("timestamp", [])):
        if quote["close"][index] is None:
            continue
        date = dt.datetime.fromtimestamp(timestamp, TAIPEI)
        bars.append({
            "Time": date.strftime("%Y-%m-%dT%H:%M:%S"),
            "Open": quote["open"][index] or 0,
            "High": quote["high"][index] or 0,
            "Low": quote["low"][index] or 0,
            "Close": quote["close"][index],
            "Volume": quote["volume"][index] or 0,
        })
    return sorted(bars, key=lambda bar: bar["Time"])


def firestore_value(value):
    if isinstance(value, str):
        return {"stringValue": value}
    if isinstance(value, int):
        return {"integerValue": str(value)}
    return {"doubleValue": value}


def candle_value(candle):
    return {"mapValue": {"fields": {
        key: firestore_value(value) for key, value in candle.items()
    }}}


def bars_from_document(document):
    if document is None:
        return {}
    entries = document.get("fields", {}).get("bars", {}).get("mapValue", {}).get("fields", {})
    return {key: {field: next(iter(value.values())) for field, value in entry["mapValue"]["fields"].items()}
            for key, entry in entries.items()}


def latest_date(document):
    bars = bars_from_document(document)
    return max((bar["Time"][:10] for bar in bars.values()), default=None)


def same_candle(old, new):
    return old.get("Time") == new["Time"] and all(
        float(old.get(field, 0)) == float(new[field])
        for field in ("Open", "High", "Low", "Close", "Volume")
    )


def document_url(root, symbol, year):
    # The browser stores encodeURIComponent(symbol) inside the document ID.
    # REST paths need a second encoding for the literal percent sign in ^TWII.
    name = f"{urllib.parse.quote(symbol, safe='')}_D_{year}"
    return f"{root}/users/{UID}/candles/{urllib.parse.quote(name, safe='')}"


def update_symbol(root, worker, symbol, target_date, year, dry_run):
    url = document_url(root, symbol, year)
    document = request(url)
    current = latest_date(document)
    if current is None:
        previous = request(document_url(root, symbol, year - 1))
        current = latest_date(previous)
    if current is not None and current > target_date.isoformat():
        return "current"

    days = 510 if current is None else min(max((target_date - dt.date.fromisoformat(current)).days + 10, 10), 730)
    fresh = chart(worker, symbol, days)
    if not fresh:
        return "missing"
    # Only write bars through the latest market date, preserving other years and fields.
    fresh = [bar for bar in fresh if bar["Time"][:4] == str(year) and
             bar["Time"][:10] <= target_date.isoformat()]
    existing = bars_from_document(document)
    changed = {key: bar for bar in fresh if (key := candle_key(bar)) not in existing or
               not same_candle(existing[key], bar)}
    if dry_run:
        return "would_update" if changed else "current"
    if not changed:
        return "current"
    fields = {"bars": {"mapValue": {"fields": {
        key: candle_value(bar) for key, bar in changed.items()
    }}}}
    query = [("updateMask.fieldPaths", f"bars.`{key}`") for key in changed]
    if document is None:
        fields.update({"symbol": firestore_value(symbol), "timeframe": firestore_value("D"),
                       "month": firestore_value(str(year))})
        query.extend(("updateMask.fieldPaths", name) for name in ("symbol", "timeframe", "month"))
    request(f"{url}?{urllib.parse.urlencode(query)}", method="PATCH", body={"fields": fields})
    return "updated"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--limit", type=int, default=0)
    args = parser.parse_args()
    project = os.environ["FIREBASE_PROJECT_ID"].strip()
    worker = os.environ["MARKET_API_BASE_URL"].rstrip("/")
    if not project or not worker.startswith("https://"):
        raise ValueError("FIREBASE_PROJECT_ID and HTTPS MARKET_API_BASE_URL are required")
    root = f"https://firestore.googleapis.com/v1/projects/{project}/databases/(default)/documents"
    today = dt.datetime.now(TAIPEI).date()
    if today.weekday() >= 5 and not args.dry_run:
        print(f"{today}: weekend; no Taiwan market close")
        return

    market = chart(worker, "^TWII", 10)
    if not market:
        raise RuntimeError("Market daily candle is unavailable")
    if market[-1]["Time"][:10] != today.isoformat() and not args.dry_run:
        print(f"{today}: market daily candle not available; skip (holiday or delayed source)")
        return
    target_date = dt.date.fromisoformat(market[-1]["Time"][:10]) if args.dry_run else today
    year = target_date.year
    universe = request(f"{worker}/universe")
    symbols = [stock["Symbol"] for stock in universe if stock.get("Symbol", "").endswith((".TW", ".TWO"))]
    if args.limit:
        symbols = symbols[:args.limit]
    print(f"{target_date}: checking market index and {len(symbols)} stocks")
    results = {"current": 0, "updated": 0, "missing": 0, "would_update": 0, "failed": 0}
    errors = []
    jobs = ["^TWII", *symbols]
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as executor:
        futures = {executor.submit(update_symbol, root, worker, symbol, target_date, year, args.dry_run): symbol
                   for symbol in jobs}
        for future in concurrent.futures.as_completed(futures):
            try:
                results[future.result()] += 1
            except Exception as error:
                results["failed"] += 1
                errors.append(f"{futures[future]}: {error}")
    if not args.dry_run and results["updated"]:
        url = f"{root}/users/{UID}/state/data-revision"
        value = f"{int(time.time() * 1000)}-daily-collector"
        request(url, method="PATCH", body={"fields": {"value": firestore_value(value)}})
    print(json.dumps(results, ensure_ascii=False))
    for error in errors[:20]:
        print(error)
    if errors:
        raise SystemExit(f"{len(errors)} stock updates failed")


if __name__ == "__main__":
    main()
