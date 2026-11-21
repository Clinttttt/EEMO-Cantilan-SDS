// End-to-end check of the INF-4D panel compliance matrix (docs/planning/PANEL_COMPLIANCE_MATRIX_20261105.md).
//
//   node tools/compliance-e2e/run.mjs              Local: a new demo database, the demo seed, this checkout's API and Client,
//                                                  then API and browser checks of every system row of the matrix.
//   node tools/compliance-e2e/run.mjs production   Anonymous, read-only checks of the live sites. Signs in nowhere, writes nothing.
//
// Local needs STALLTRACK_E2E_CONNECTION, a connection string to a PostgreSQL on this machine. Each run creates its own database,
// stalltrack_demo_e2e_<time>, on that server (the seed refuses any other host or name). Set PayMongo__SecretKey to a TEST key
// (sk_test_...) to add the sandbox checkout check; a live key is refused. Ports 5117, 7097, 7167 and 9333 must be free, and
// Chrome must be installed (or CHROME_PATH set). Report and screenshots: artifacts/compliance-e2e/<mode>-<time>/report.md.
// Exit code 1 when a check fails.
import { spawn, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const mode = process.argv[2] ?? 'local';
if (!['local', 'production'].includes(mode)) {
  console.error('Use: node tools/compliance-e2e/run.mjs [local|production]');
  process.exit(2);
}
const stamp = new Date().toISOString().slice(0, 19).replace(/[-:]/g, '').replace('T', '_');
const out = path.join(root, 'artifacts', 'compliance-e2e', `${mode}-${stamp}`);
fs.mkdirSync(out, { recursive: true });

const results = [];
const shots = [];
async function check(row, name, fn) {
  try {
    results.push({ row, name, outcome: 'PASS', note: String((await fn()) ?? '') });
  } catch (e) {
    results.push({ row, name, outcome: e.skip ? 'SKIP' : 'FAIL', note: e.message });
  }
  const r = results.at(-1);
  console.log(`${r.outcome.padEnd(4)}  ${row} | ${name}${r.note ? ` — ${r.note}` : ''}`);
}
function must(condition, message) {
  if (!condition) throw new Error(message);
}
function skip(message) {
  throw Object.assign(new Error(message), { skip: true });
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const peso = (n) => `₱${Number(n).toLocaleString('en-PH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
const same = (a, b) => Math.abs(a - b) < 0.005;
const sum = (months) => months.reduce((a, m) => a + m.amount, 0);
const period = (m) => `${m.year}-${String(m.month).padStart(2, '0')}`;

async function http(base, token, method, url, body) {
  const r = await fetch(base + url, {
    method,
    headers: { 'content-type': 'application/json', ...(token ? { authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await r.text();
  let json = null;
  try { json = JSON.parse(text); } catch { /* not JSON */ }
  return { status: r.status, json, text };
}

// ---------------------------------------------------------------- local: processes

const procs = [];
let tmp;
function stopAll() {
  for (const p of procs.splice(0)) {
    if (p.exitCode !== null) continue;
    // dotnet run starts the app as a child; only a tree kill stops both.
    if (process.platform === 'win32') spawnSync('taskkill', ['/pid', String(p.pid), '/T', '/F'], { stdio: 'ignore' });
    else p.kill('SIGTERM');
  }
  if (tmp) {
    try { fs.rmSync(tmp, { recursive: true, force: true }); } catch { /* Chrome may hold its profile a moment longer */ }
  }
}
process.on('SIGINT', () => { stopAll(); process.exit(130); });

function start(name, file, args, env = {}) {
  const log = fs.openSync(path.join(out, `${name}.log`), 'w');
  const p = spawn(file, args, { cwd: root, env: { ...process.env, ...env }, stdio: ['ignore', log, log] });
  procs.push(p);
  return p;
}
const inUse = (port) => new Promise((resolve) => {
  const s = net.connect(port, '127.0.0.1', () => { s.destroy(); resolve(true); });
  s.on('error', () => resolve(false));
});
async function ready(url, what, p, minutes = 8) {
  for (const end = Date.now() + minutes * 60000; Date.now() < end; await sleep(2000)) {
    must(p.exitCode === null, `${what} stopped while starting; see ${out}`);
    try {
      if ((await fetch(url, { redirect: 'manual' })).status < 500) return;
    } catch { /* not listening yet */ }
  }
  throw new Error(`${what} did not start within ${minutes} minutes; see ${out}`);
}

// ---------------------------------------------------------------- local: API checks

const API = 'http://localhost:5117';

async function apiChecks(password, phones) {
  const as = {};
  const signIn = async (url, body) => {
    const r = await http(API, null, 'POST', url, body);
    must(r.json?.accessToken, `${url} refused (${r.status})`);
    return r.json.accessToken;
  };
  await check('Sign-in', 'Head, Admin, two payors and a collector sign in', async () => {
    as.head = await signIn('/api/AdminAuth/login', { username: 'demo.head', password, municipalityCode: 'demo' });
    as.admin = await signIn('/api/AdminAuth/login', { username: 'demo.admin', password, municipalityCode: 'demo' });
    as.payor1 = await signIn('/api/PayorAuth/login', { contactNumber: phones[0], password });
    as.payor2 = await signIn('/api/PayorAuth/login', { contactNumber: phones[1], password });
    as.collector = await signIn('/api/CollectorAuth/login', { usernameOrEmployeeId: 'demo.collector1', password });
  });
  if (!as.collector) return null;

  const get = async (who, url) => http(API, as[who], 'GET', url);
  const post = async (who, url, body) => http(API, as[who], 'POST', url, body);
  const json = async (who, url) => {
    const r = await get(who, url);
    must(r.status === 200, `${url} answered ${r.status}`);
    return r.json;
  };
  const or = (n) => `E2E${stamp.replace('_', '')}-${n}`;

  const policy = await json('head', '/api/billing-policy');
  const rules = policy.current;
  const [space] = await json('payor1', '/api/payor/balances');
  const stall = space.stallId;
  const statement = await json('head', `/api/Stalls/${stall}/statement`);
  const [Y, M] = statement.asOf.split('-').map(Number);
  const oldest = statement.unpaidMonths[0];
  const history = async () => json('head', `/api/Stalls/${stall}/payment-history`);
  const payAhead = async () => json('head', `/api/Payments/stall/${stall}/pay-ahead`);
  const setRules = async (who, changes) => post(who, '/api/billing-policy',
    { effectiveFrom: statement.asOf, rules: { ...rules, ...changes }, governingReference: policy.inForce.governingReference });
  const payAheadPost = async (months, expectedTotal, orNumber) => post('head', '/api/Payments/pay-ahead',
    { stallId: stall, firstYear: months[0].year, firstMonth: months[0].month, months: months.length, expectedTotal, orNumber });
  const arrearsLine = async () => (await json('head', `/api/official-reports/monthly-income?year=${Y}`))
    .groups.flatMap((g) => g.rows).find((row) => row.key === 'ARREARS')?.months[M - 1].total ?? null;

  await check('Arrears carried forward', 'The statement splits the previous balance into Arrears and Delinquent', async () => {
    must(statement.previousBalance > 0, "the payor's space shows no previous balance");
    must(same(statement.arrearsAmount + statement.delinquentAmount, statement.previousBalance),
      `Arrears ${statement.arrearsAmount} + Delinquent ${statement.delinquentAmount} is not the previous balance ${statement.previousBalance}`);
    return `previous balance ${peso(statement.previousBalance)}: Arrears ${peso(statement.arrearsAmount)} (${statement.arrearsMonths} months), Delinquent ${peso(statement.delinquentAmount)} (${statement.delinquentMonths} months)`;
  });

  await check('Clear billing presentation', 'Previous balance + utilities + penalties + this month = total due, the same total as the portal', async () => {
    const parts = statement.previousBalance + statement.utilityBalance + statement.penaltiesDue + statement.currentBalance;
    must(same(parts, statement.totalDue), `the parts add to ${parts}, the total says ${statement.totalDue}`);
    must(same(space.outstandingBalance, statement.totalDue), `portal ${space.outstandingBalance}, statement ${statement.totalDue}`);
    return `${peso(statement.totalDue)} on both`;
  });

  await check('Oldest month first', "The office's payment for this month is refused while an older month is open", async () => {
    must(oldest && oldest.year * 12 + oldest.month < Y * 12 + M, 'no older open month to test with');
    const r = await post('head', '/api/Payments/record', { stallId: stall, year: Y, month: M, status: 'Paid', partialAmount: null, remarks: 'compliance e2e', orNumber: or(0) });
    must(r.status === 409 && /oldest/i.test(r.json?.error ?? ''), `expected 409 oldest-first, got ${r.status} ${r.text.slice(0, 160)}`);
    return r.json.error;
  });

  await check('Oldest month first', 'An online checkout for this month is refused too', async () => {
    const r = await post('payor1', '/api/onlinepayments/initiate', { stallId: stall, year: Y, month: M });
    must(r.status === 409 && /oldest/i.test(r.json?.error ?? ''), `expected 409 oldest-first, got ${r.status} ${r.text.slice(0, 160)}`);
  });

  await check('Oldest month first', 'Pay ahead is held until the oldest month is paid', async () => {
    const r = await payAhead();
    must(r.heldBy && r.counts.length === 0, `pay ahead offered ${JSON.stringify(r.counts)}`);
  });

  const arrearsBefore = await arrearsLine();
  await check('Excess or wrong amounts', "An office amount over the month's bill records only the bill", async () => {
    const entered = oldest.unpaid + 1000;
    const r = await post('head', '/api/Payments/record',
      { stallId: stall, year: oldest.year, month: oldest.month, status: 'Partial', partialAmount: entered, remarks: 'compliance e2e', orNumber: or(1) });
    must(r.status === 200, `the payment was refused: ${r.status} ${r.text.slice(0, 160)}`);
    const after = await json('head', `/api/Stalls/${stall}/statement`);
    must(!after.unpaidMonths.some((m) => period(m) === period(oldest)), `${period(oldest)} is still open`);
    const fell = statement.previousBalance - after.previousBalance;
    must(same(fell, oldest.unpaid), `the balance fell by ${fell}; the month owed ${oldest.unpaid}`);
    return `${peso(entered)} entered on ${period(oldest)}; the month closed and the balance fell by ${peso(fell)}, its bill`;
  });

  await check('Arrears carried forward', 'Old rent paid today reports on the Monthly Income Arrears line', async () => {
    if (oldest.year >= Y) skip('the oldest open month is this year, so it is not Arrears');
    const after = await arrearsLine();
    must(arrearsBefore !== null && same(after - arrearsBefore, oldest.unpaid), `the Arrears line went ${arrearsBefore} → ${after}, expected +${oldest.unpaid}`);
    return `${period({ year: Y, month: M })} Arrears ${peso(arrearsBefore)} → ${peso(after)}`;
  });

  await check('Excess or wrong amounts', 'An online capture that differs is held under Needs attention', async () => {
    const held = (await json('head', '/api/onlinepayments/exceptions')).find((x) => x.kind === 'NeedsDecision' && !same(x.capturedAmount, x.amount));
    must(held, 'no held mismatch in the list');
    return `${held.reference}: ${peso(held.capturedAmount)} captured for a ${peso(held.amount)} month`;
  });

  await check('Billing policy visible', 'Head and Admin read the rules in force; a payor and an anonymous caller cannot', async () => {
    const codes = [await get('head', '/api/billing-policy'), await get('admin', '/api/billing-policy'), await get('payor1', '/api/billing-policy'),
      await http(API, null, 'GET', '/api/billing-policy')].map((r) => r.status);
    must(codes.join() === '200,200,403,401', `got ${codes.join(', ')} for Head, Admin, payor, anonymous`);
    must(policy.inForce?.governingReference, 'the rules in force carry no reference');
    return `in force from ${policy.inForce.effectiveFrom} under "${policy.inForce.governingReference}", ${policy.versions.length} version(s)`;
  });

  await check('Due dates', "The statement's due date is the Head's due day, and portal items carry it", async () => {
    const expected = `${Y}-${String(M).padStart(2, '0')}-${String(rules.dueDay).padStart(2, '0')}`;
    must(statement.currentDueDate === expected, `the statement says ${statement.currentDueDate}, the rules say day ${rules.dueDay}`);
    const items = await json('payor1', '/api/payor/payable-items');
    must(items.length && items.filter((i) => i.kind === 'Monthly').every((i) => i.dueDate), 'a monthly portal item has no due date');
    return `due day ${rules.dueDay}, ${rules.graceDays} grace days`;
  });

  await check('Oldest month first', 'Only the Head can turn it off, and then pay ahead opens', async () => {
    const admin = await setRules('admin', { oldestMonthFirst: false });
    must(admin.status === 403, `Admin got ${admin.status}`);
    const head = await setRules('head', { oldestMonthFirst: false });
    must(head.status === 200, `Head got ${head.status} ${head.text.slice(0, 160)}`);
    const r = await payAhead();
    must(!r.heldBy && r.counts.length, `pay ahead is still held: ${r.heldBy}`);
  });

  let options;
  await check('Advance payment', "Pay ahead offers months up to the Head's limit and refuses more", async () => {
    options = await payAhead();
    const limit = Math.max(...options.counts.filter((c) => c < 12));
    must(limit >= 2 && limit <= rules.maxAdvanceMonths + 1, `offers ${JSON.stringify(options.counts)} with ${rules.maxAdvanceMonths} months ahead allowed`);
    const tooMany = options.months.slice(0, limit + 1);
    const r = await payAheadPost(tooMany, sum(tooMany), or(2));
    must(r.status >= 400 && r.status < 500, `${limit + 1} months were accepted (${r.status})`);
    return `offers ${options.counts.join(', ')} months (this month + ${rules.maxAdvanceMonths} ahead, or a year); ${limit + 1} refused: ${r.json?.error ?? r.status}`;
  });

  await check('Excess or wrong amounts', "Pay ahead refuses a total that differs from the months' rent", async () => {
    const two = options.months.slice(0, 2);
    const r = await payAheadPost(two, sum(two) + 100, or(2));
    must(r.status >= 400 && r.status < 500, `a wrong total was accepted (${r.status})`);
    return r.json?.error ?? String(r.status);
  });

  await check('Advance payment', 'Two months ahead on one receipt, each month recorded', async () => {
    const two = options.months.slice(0, 2);
    const r = await payAheadPost(two, sum(two), or(2));
    must(r.status === 200, `refused: ${r.status} ${r.text.slice(0, 160)}`);
    const h = await history();
    const rows = two.map((m) => h.find((x) => x.period === period(m)));
    must(rows.every((x) => x?.status === 'Paid' && x.orNumber === or(2)), `months read ${JSON.stringify(rows.map((x) => x && [x.period, x.status, x.orNumber]))}`);
    return `${two.map(period).join(', ')}, ${peso(sum(two))} on OR ${or(2)}`;
  });

  await check('Annual payment', 'The Head can turn it off and on again', async () => {
    must((await payAhead()).counts.includes(12), 'a year is not offered');
    must((await setRules('head', { oldestMonthFirst: false, annualPaymentAllowed: false })).status === 200, 'the Head could not turn it off');
    const off = await payAhead();
    must(!off.counts.includes(12), `still offers a year: ${JSON.stringify(off.counts)}`);
    must((await setRules('head', { oldestMonthFirst: false })).status === 200, 'the Head could not turn it back on');
    must((await payAhead()).counts.includes(12), 'a year is not offered again');
  });

  await check('Annual payment', 'A whole year on one receipt at the office, each month recorded', async () => {
    const year = (await payAhead()).months.slice(0, 12);
    must(year.length === 12, `only ${year.length} months offered`);
    const r = await payAheadPost(year, sum(year), or(3));
    must(r.status === 200, `refused: ${r.status} ${r.text.slice(0, 160)}`);
    const h = await history();
    const rows = year.map((m) => h.find((x) => x.period === period(m)));
    must(rows.every((x) => x?.status === 'Paid' && x.orNumber === or(3)), `months read ${JSON.stringify(rows.map((x) => x && [x.period, x.status, x.orNumber]))}`);
    return `${period(year[0])} to ${period(year[11])}, ${peso(sum(year))} on OR ${or(3)}`;
  });

  await check('Oldest month first', 'Turned back on, it holds pay ahead again', async () => {
    const r = await setRules('head', {});
    must(r.status === 200, `the Head could not restore the rules: ${r.status}`);
    must((await payAhead()).heldBy, 'pay ahead is not held');
  });

  await check('Interest and penalties', "Late penalties are assessed by themselves at the Head's rate", async () => {
    must(rules.chargesPenalty && rules.penaltyValue > 0, 'the demo rules charge no penalty');
    const s = await json('head', '/api/penalties/summary');
    must(s.outstandingAssessments > 0, 'no penalty assessed');
    must(statement.penaltiesDue > 0, 'the statement shows no penalty');
    return `${rules.penaltyValue} ${rules.penaltyBasis}: ${s.outstandingAssessments} assessed, ${peso(s.outstandingAssessed)} outstanding; this space ${peso(statement.penaltiesDue)}`;
  });

  let headInbox;
  await check('Notifications', 'Head and payor inboxes hold reminders, and reading one marks it read', async () => {
    headInbox = (await json('head', '/api/inbox')).items;
    const mine = (await json('payor1', '/api/payor/inbox')).items;
    must(headInbox.length && mine.length, `Head ${headInbox.length}, payor ${mine.length} messages`);
    const unread = mine.find((i) => !i.read);
    must(unread, 'no unread payor message');
    must((await post('payor1', `/api/payor/inbox/${unread.id}/read`)).status < 300, 'marking it read was refused');
    must((await json('payor1', '/api/payor/inbox')).items.find((i) => i.id === unread.id)?.read === true, 'it is still unread');
    return `Head ${headInbox.length}, payor ${mine.length}, e.g. "${mine.map((i) => i.title).find((t) => /penalt/i.test(t)) ?? mine[0].title}"`;
  });

  await check('Audit log', "Today's receipt is on the trail with its actor and time", async () => {
    const trail = await json('head', '/api/Audit/trail?pageSize=100&includeOptions=false');
    const e = trail.items.find((i) => i.details?.includes(or(1)));
    must(e && e.actorName === 'demo.head' && e.loggedAtUtc, `no entry for OR ${or(1)}`);
    return `${e.action} by ${e.actorDisplayName} at ${e.loggedAtUtc}: ${e.details}`;
  });

  await check('Monthly reports', 'Monthly Income and the month-end report answer for this month', async () => {
    const mi = await json('head', `/api/official-reports/monthly-income?year=${Y}&month=${M}`);
    await json('head', `/api/Facilities/month-end-report?year=${Y}&month=${M}`);
    must(mi.groups?.length, 'Monthly Income has no groups');
    return `Monthly Income ${mi.groups.length} groups, ${mi.groups.flatMap((g) => g.rows).length} lines`;
  });

  await check('Realistic January to December data', 'Every month of the year so far has collections', async () => {
    const rows = (await json('head', `/api/official-reports/monthly-income?year=${Y}`)).groups.flatMap((g) => g.rows);
    const totals = Array.from({ length: M }, (_, i) => rows.reduce((a, r) => a + r.months[i].total, 0));
    const empty = totals.slice(0, M - 1).flatMap((t, i) => (t > 0 ? [] : [i + 1]));
    must(!empty.length, `no collections in month(s) ${empty.join(', ')}`);
    return `${peso(totals.reduce((a, t) => a + t, 0))} from January to ${statement.asOf}`;
  });

  await check('Collector app', "The collector's menu carries each facility's short name", async () => {
    const menu = await json('collector', '/api/Mobile/menu');
    must(menu.facilities.length && menu.facilities.every((f) => f.shortName), 'a facility has no short name');
    return menu.facilities.map((f) => `${f.code} → ${f.shortName}`).join(', ');
  });

  await check('Payor portal', "A payor cannot read another payor's space", async () => {
    const [other] = await json('payor2', '/api/payor/balances');
    const r = await get('payor1', `/api/payor/stalls/${other.stallId}/history`);
    must(r.status === 403 || r.status === 404, `got ${r.status}`);
    return `answered ${r.status}`;
  });

  await check('Failed transactions', 'A second checkout for the same month reuses the open one (PayMongo sandbox)', async () => {
    if (!process.env.PayMongo__SecretKey) skip('PayMongo__SecretKey is not set');
    const next = statement.unpaidMonths[1];
    const body = { stallId: stall, year: next.year, month: next.month };
    const a = await post('payor1', '/api/onlinepayments/initiate', body);
    const b = await post('payor1', '/api/onlinepayments/initiate', body);
    must(a.status === 200 && b.status === 200 && a.json.reference === b.json.reference,
      `${a.status} ${a.json?.reference ?? a.text.slice(0, 120)} / ${b.status} ${b.json?.reference ?? b.text.slice(0, 120)}`);
    return `${period(next)}: ${a.json.reference}, ${new URL(a.json.checkoutUrl).host}`;
  });

  return { stall, facility: space.facility, or1: or(1), headInboxTitle: headInbox?.[0]?.title, reference: policy.inForce?.governingReference };
}

// ---------------------------------------------------------------- local: browser checks (headless Chrome over CDP)

async function browserChecks(chromePath, password, facts) {
  start('chrome', chromePath, ['--headless=new', '--remote-debugging-port=9333', `--user-data-dir=${path.join(tmp, 'chrome')}`,
    '--ignore-certificate-errors', '--no-first-run', '--no-default-browser-check', 'about:blank']);
  let targets;
  for (let i = 0; i < 40 && !targets; i++) {
    try { targets = await (await fetch('http://127.0.0.1:9333/json')).json(); } catch { await sleep(500); }
  }
  must(targets, 'Chrome did not start');
  const ws = new WebSocket(targets.find((t) => t.type === 'page').webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });
  let id = 0;
  const pending = new Map();
  const errors = new Set();
  ws.onmessage = (e) => {
    const m = JSON.parse(e.data);
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
    if (m.method === 'Runtime.exceptionThrown') errors.add((m.params.exceptionDetails.exception?.description ?? m.params.exceptionDetails.text).split('\n')[0]);
  };
  const send = (method, params = {}) => new Promise((resolve) => { const i = ++id; pending.set(i, resolve); ws.send(JSON.stringify({ id: i, method, params })); });
  const js = async (expression) => {
    const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (r.result?.exceptionDetails) throw new Error(r.result.exceptionDetails.exception?.description ?? r.result.exceptionDetails.text);
    return r.result?.result?.value;
  };
  const until = async (expression, what, seconds = 30) => {
    for (const end = Date.now() + seconds * 1000; Date.now() < end; await sleep(500)) {
      try { if (await js(expression)) return; } catch { /* page still loading */ }
    }
    throw new Error(`${what} did not appear within ${seconds} seconds`);
  };
  const go = async (route) => { await send('Page.navigate', { url: `https://localhost:7167${route}` }); await sleep(1500); };
  const shot = async (name) => {
    // A Head is offered an authenticator after signing in; "Not now" keeps the offer out of the evidence.
    await js(`[...document.querySelectorAll('button')].find((b) => b.textContent.trim() === 'Not now')?.click()`);
    await sleep(600);
    const r = await send('Page.captureScreenshot', { format: 'png' });
    fs.writeFileSync(path.join(out, name), Buffer.from(r.result.data, 'base64'));
    shots.push(name);
    return name;
  };
  const shows = (text) => `!!document.body && document.body.innerText.includes(${JSON.stringify(text)})`;
  const healthy = async () => must(await js(`(() => { const e = document.getElementById('blazor-error-ui'); return !e || getComputedStyle(e).display === 'none'; })()`),
    'the page shows the unhandled-error banner');
  const type = (selector, text) => js(`(() => { const el = document.querySelector(${JSON.stringify(selector)}); el.focus();
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(el, ${JSON.stringify(text)});
    el.dispatchEvent(new Event('input', { bubbles: true })); el.dispatchEvent(new Event('change', { bubbles: true })); })()`);
  const light = () => js(`localStorage.setItem('st-theme', 'light'); window.stallTrackApplyTheme && window.stallTrackApplyTheme()`);

  await send('Page.enable');
  await send('Runtime.enable');
  await send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false });

  await check('Sign-in page', 'A plain /login names the demo office, not Cantilan', async () => {
    await go('/login');
    await until(`!!document.querySelector('#login-username') && /demo/i.test(document.body.innerText)`, "the demo office's sign-in form", 60);
    await light();
    must(!/Cantilan|MEEDO/i.test(await js('document.body.innerText')), 'the page names Cantilan or MEEDO');
    return shot('sign-in.png');
  });

  await check('Sign-in page', 'The Head signs in', async () => {
    // The prerendered form shows before Blazor connects, and a click before that is lost, so try again until it goes through.
    for (let attempt = 1; attempt <= 8; attempt++) {
      if (await js(`location.pathname !== '/login'`)) return attempt > 1 ? `signed in on try ${attempt}` : '';
      await type('#login-username', 'demo.head');
      await type('#login-password', password);
      await js(`document.querySelector('button.setup-submit')?.click()`);
      await until(`location.pathname !== '/login'`, 'the signed-in office', 10).catch(() => {});
    }
    must(await js(`location.pathname !== '/login'`), 'still on the sign-in page after 8 tries');
  });

  const pages = [
    ['Billing policy visible', '/settings/billing-policy', shows(facts.reference), 'billing-policy.png'],
    ['Clear billing presentation', `/profile/${facts.facility.toLowerCase()}/${facts.stall}`, shows('Statement of Account'), 'statement.png'],
    ['Excess or wrong amounts', '/online-payments', shows('Needs attention'), 'online-payments.png'],
    ['Interest and penalties', '/operations/fines', `document.title.includes('Fines')`, 'fines.png'],
    ['Notifications', '/notifications', shows(facts.headInboxTitle), 'notifications.png'],
    ['Audit log', '/audit-trail', shows(facts.or1), 'audit-trail.png'],
    ['Monthly reports', '/reports', `document.title.includes('Financial Reports')`, 'reports.png'],
  ];
  for (const [row, route, expected, file] of pages) {
    await check(row, `${route} opens for the Head`, async () => {
      await go(route);
      await until(expected, route, 45);
      await healthy();
      return shot(file);
    });
  }

  await check('Dark and light mode', 'The toggle turns the office dark, a reload keeps it, and print stays white', async () => {
    await go('/settings/billing-policy');
    await until(shows(facts.reference), 'the billing policy page', 45);
    await light();
    await js(`document.querySelector('[data-st-theme-toggle]').click()`);
    must(await js(`document.documentElement.getAttribute('data-st-theme')`) === 'dark', 'the toggle did not turn the page dark');
    await send('Page.reload');
    await sleep(1500);
    await until(`document.documentElement.getAttribute('data-st-theme') === 'dark' && ${shows(facts.reference)}`, 'dark mode after a reload', 45);
    const name = await shot('dark-mode.png');
    await send('Emulation.setEmulatedMedia', { media: 'print' });
    const paper = await js(`getComputedStyle(document.body).backgroundColor`);
    await send('Emulation.setEmulatedMedia', { media: '' });
    await light();
    must(paper === 'rgb(255, 255, 255)', `printing in dark mode paints ${paper}`);
    return name;
  });

  ws.close();
  return [...errors];
}

async function local() {
  const server = process.env.STALLTRACK_E2E_CONNECTION;
  must(server, 'Set STALLTRACK_E2E_CONNECTION to a connection string for a PostgreSQL on this machine.');
  const key = process.env.PayMongo__SecretKey ?? '';
  must(!key || key.startsWith('sk_test_'), 'PayMongo__SecretKey is not a test key. Unset it, or use an sk_test_ key.');
  for (const port of [5117, 7097, 7167, 9333]) must(!(await inUse(port)), `Port ${port} is in use. Stop the running API, Client or Chrome first.`);
  const chromePath = [process.env.CHROME_PATH, 'C:/Program Files/Google/Chrome/Application/chrome.exe', 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', '/usr/bin/google-chrome'].find((p) => p && fs.existsSync(p));
  must(chromePath, 'Chrome was not found. Set CHROME_PATH.');

  const database = `stalltrack_demo_e2e_${stamp.replace('_', '')}`;
  const connection = `${server.replace(/;?\s*Database=[^;]*/i, '').replace(/;\s*$/, '')};Database=${database}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'stalltrack-e2e-'));
  const signinFile = path.join(tmp, 'signin.txt');

  console.log(`Seeding ${database} ...`);
  const seed = spawnSync('dotnet', ['run', '-c', 'Release', '--project', 'tools/demo-tenant', '--', 'seed', signinFile],
    { cwd: root, env: { ...process.env, STALLTRACK_DEMO_CONNECTION: connection }, encoding: 'utf8', maxBuffer: 1 << 26 });
  fs.writeFileSync(path.join(out, 'seed.log'), `${seed.stdout ?? ''}${seed.stderr ?? ''}`);
  must(seed.status === 0, `The seed failed; see ${path.join(out, 'seed.log')}`);
  const signin = fs.readFileSync(signinFile, 'utf8');
  const password = signin.match(/^Head: \S+ \/ (\S+)/m)[1];
  const phones = signin.match(/^Payors: (.*) \/ /m)[1].split(', ');

  console.log('Starting the API ...');
  const api = start('api', 'dotnet', ['run', '-c', 'Release', '--project', 'EEMOCantilanSDS.Api', '--launch-profile', 'https'],
    { ConnectionStrings__DefaultConnection: connection });
  await ready(`${API}/health`, 'The API', api);
  const facts = await apiChecks(password, phones);

  let consoleErrors = [];
  if (facts) {
    console.log('Starting the Client ...');
    const client = start('client', 'dotnet', ['run', '-c', 'Release', '--project', 'EEMOCantilanSDS.Client', '--launch-profile', 'https']);
    await ready('http://localhost:5198/login', 'The Client', client);
    consoleErrors = await browserChecks(chromePath, password, facts);
  }
  return [
    `- Database: \`${database}\` (left in place; drop it when done). ${seed.stdout.match(/Demo office seeded:.*/)?.[0] ?? ''}`,
    `- PayMongo sandbox: ${key ? 'test key set' : 'not set, so the checkout check is skipped'}`,
    `- Browser console exceptions: ${consoleErrors.length ? consoleErrors.map((e) => `\`${e}\``).join('; ') : 'none'}`,
  ];
}

// ---------------------------------------------------------------- production: anonymous and read-only

async function production() {
  const api = 'https://api.stalltrack.site';
  const portal = 'https://payor.stalltrack.site';
  const page = async (url) => {
    const r = await fetch(url);
    must(r.ok, `${url} answered ${r.status}`);
    return r.text();
  };
  await check('Deployed', 'The API answers its health check', async () => (await page(`${api}/health`)).slice(0, 40));
  await check('Deployed', 'The office sign-in page loads', async () => {
    must(/StallTrack/i.test(await page('https://console.stalltrack.site/login')), 'the page does not say StallTrack');
  });
  await check('Billing policy visible', 'The rules need a sign-in', async () => {
    const r = await fetch(`${api}/api/billing-policy`);
    must(r.status === 401, `answered ${r.status}`);
  });
  await check('Annual payment', 'The payor portal says to pay several months or a year at the office', async () => {
    const queue = [...(await page(portal)).matchAll(/(?:src|href)="([^"]+\.js)"/g)].map((m) => m[1]);
    const seen = new Set();
    while (queue.length && seen.size < 150) {
      const file = queue.shift();
      if (seen.has(file)) continue;
      seen.add(file);
      const code = await page(new URL(file, `${portal}/`).href);
      if (code.includes('a whole year on one receipt')) return `in ${file}`;
      for (const m of code.matchAll(/((?:chunk|main)-[\w-]+\.js)/g)) queue.push(m[1]);
    }
    throw new Error(`not found in ${seen.size} scripts`);
  });
  await check('Collector app', 'The latest collector APK downloads', async () => {
    const r = await fetch('https://github.com/Clinttttt/EEMO-Cantilan-SDS/releases/latest/download/stalltrack-collector-latest.apk', { method: 'HEAD' });
    must(r.ok, `answered ${r.status}`);
    return `${(Number(r.headers.get('content-length')) / 1048576).toFixed(1)} MB`;
  });
  await check('Collector app', 'The API tells installed apps a version', async () => {
    const v = JSON.parse(await page(`${api}/api/mobile/version`));
    return `latest ${v.latestVersion} (code ${v.latestVersionCode}), download ${v.apkUrl}`;
  });
  return ['- Anonymous requests only: nothing signed in, nothing written.'];
}

// ---------------------------------------------------------------- report

let header = [];
try {
  header = await (mode === 'local' ? local() : production());
} catch (e) {
  results.push({ row: 'Setup', name: 'Prepare the run', outcome: 'FAIL', note: e.message });
  console.log(`FAIL  Setup — ${e.message}`);
} finally {
  stopAll();
}
const commit = spawnSync('git', ['rev-parse', '--short', 'HEAD'], { cwd: root, encoding: 'utf8' }).stdout.trim();
const count = (o) => results.filter((r) => r.outcome === o).length;
const cell = (s) => s.replace(/\|/g, '\\|').replace(/\r?\n/g, ' ');
fs.writeFileSync(path.join(out, 'report.md'), [
  `# Compliance end-to-end check (${mode})`,
  '',
  `- Run: ${new Date().toISOString()}, commit \`${commit}\``,
  ...header,
  '',
  `**${count('PASS')} passed, ${count('FAIL')} failed, ${count('SKIP')} skipped.** Rows follow [the compliance matrix](../../../docs/planning/PANEL_COMPLIANCE_MATRIX_20261105.md).`,
  '',
  '| Matrix row | Check | Result | Detail |',
  '|---|---|---|---|',
  ...results.map((r) => `| ${cell(r.row)} | ${cell(r.name)} | ${r.outcome} | ${r.note.endsWith('.png') ? `[${r.note}](${r.note})` : cell(r.note)} |`),
  '',
  ...(shots.length ? ['## Screenshots', '', ...shots.map((s) => `### ${s}\n\n![${s}](${s})\n`)] : []),
].join('\n'));
console.log(`\n${count('PASS')} passed, ${count('FAIL')} failed, ${count('SKIP')} skipped. Report: ${path.join(out, 'report.md')}`);
process.exit(count('FAIL') ? 1 : 0);
