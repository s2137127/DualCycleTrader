// Cloudflare Worker: fixed market endpoints only; no arbitrary URL forwarding.
const YAHOO = 'https://query1.finance.yahoo.com/v8/finance/chart/';
const TWSE = 'https://openapi.twse.com.tw/v1/exchangeReport/STOCK_DAY_ALL';
const TPEX = 'https://www.tpex.org.tw/openapi/v1/tpex_mainboard_quotes';

function response(body, status, origin, contentType = 'application/json') {
  return new Response(body, { status, headers: {
    'Content-Type': contentType,
    'Access-Control-Allow-Origin': origin,
    'Vary': 'Origin',
    'Cache-Control': 'no-store'
  }});
}

async function fetchJson(url) {
  const upstream = await fetch(url, { headers: { 'User-Agent': 'Mozilla/5.0' } });
  if (!upstream.ok) throw new Error(`行情來源回應 ${upstream.status}`);
  return await upstream.json();
}

export default {
  async fetch(request, env) {
    const origin = request.headers.get('Origin') || '';
    const allowed = (env.ALLOWED_ORIGINS || '').split(',').map(s => s.trim());
    if (!allowed.includes(origin)) return response(JSON.stringify({ error: 'Origin not allowed' }), 403, 'null');
    if (request.method === 'OPTIONS') return new Response(null, { headers: {
      'Access-Control-Allow-Origin': origin,
      'Access-Control-Allow-Methods': 'GET, OPTIONS',
      'Access-Control-Allow-Headers': 'Content-Type',
      'Vary': 'Origin'
    }});
    if (request.method !== 'GET') return response(JSON.stringify({ error: 'Method not allowed' }), 405, origin);
    try {
      const url = new URL(request.url);
      if (url.pathname === '/chart') {
        const symbol = url.searchParams.get('symbol') || '';
        const interval = url.searchParams.get('interval') || '';
        const days = Number(url.searchParams.get('days'));
        if (!/^(\^TWII|[0-9]{4}\.(TW|TWO))$/.test(symbol) ||
            interval !== '1d' || !Number.isInteger(days) || days < 5 || days > 730)
          return response(JSON.stringify({ error: 'Invalid chart request' }), 400, origin);
        const endpoint = `${YAHOO}${encodeURIComponent(symbol)}?interval=${interval}&range=${days}d&includePrePost=false`;
        return response(JSON.stringify(await fetchJson(endpoint)), 200, origin);
      }
      if (url.pathname === '/universe') {
        const twse = await fetchJson(TWSE);
        const tpex = await fetchJson(TPEX).catch(() => []);
        const stocks = [
          ...twse.map(x => ({ Symbol: `${x.Code}.TW`, Name: x.Name, Market: '上市' })),
          ...tpex.map(x => ({ Symbol: `${x.SecuritiesCompanyCode ?? x.Code ?? x['股票代號'] ?? x['證券代號']}.TWO`, Name: x.CompanyName ?? x.Name ?? x['股票名稱'] ?? x['證券名稱'], Market: '上櫃' }))
        ].filter(x => /^[0-9]{4}\.(TW|TWO)$/.test(x.Symbol));
        const unique = [...new Map(stocks.map(x => [x.Symbol, x])).values()]
          .sort((a, b) => a.Symbol.localeCompare(b.Symbol));
        return response(JSON.stringify(unique), 200, origin);
      }
      return response(JSON.stringify({ error: 'Not found' }), 404, origin);
    } catch (error) {
      return response(JSON.stringify({ error: String(error) }), 502, origin);
    }
  }
};
