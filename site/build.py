#!/usr/bin/env python3
"""Render index.html (at the repository root): theDotNetWay's landing page.

Python 3 standard library only.

    python3 site/build.py            # write index.html, or fail

The page is short: the headline, "Campfire, six ways" (Basecamp's harness, six implementations, the same sessions), an honest note
about the speed (what the old 5x floor was measured against, and what Phase 10 changed), the climb across Phases 0-10, what the
framework replaced layer by layer, and a row of proofs. Details sit in side panels, CSS :target drawers that work with JavaScript off;
with it on they also close on Esc or a click outside and give focus back.

**Every number on the page is computed here from a file in the repository, never typed.** A number is a value V (the value, what it
is, the files it came from, how it was worked out); the Ledger prints it as a link to its row in the "Every number" panel, and that
row links the files. After rendering, check() reads the page back and fails the build if any digit a reader can see is not inside an
element the ledger issued (data-src), outside code, or if any source file is missing. Identifiers with digits (commits, phase names,
test ids) are marked data-lit and must match a short list of shapes. Links to files are relative to the repository root until
meta.repo_url in site/data.json is set.

Copy and links are in site/data.json under "dotnetway". The rest of site/data.json (and site/collect_tokens.py, site/same_pixels.py,
site/workflows/) is the F# port's story, inherited from the fork; this page does not draw it.
"""
import glob
import html
import json
import math
import os
import re
import statistics
import sys
from html.parser import HTMLParser

SITE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(SITE)
DATA = json.load(open(os.path.join(SITE, 'data.json'), encoding='utf-8'))
D = DATA['dotnetway']
META = D['meta']

R = 'bench/results'
SIX = R + '/dotnetway-six-ways'
SIX_T = SIX + '/table.json'
PRE = SIX + '/before-phase10'
PRE_T = PRE + '/table.json'
STORED = R + '/phase7-final'
KLOC_NOW = R + '/dotnetway-landing/kloc.md'
GATE = R + '/dotnetway-p10'
PLAN = 'plans/dotnetway.md'

ROUTES = [('room_show', 'Room page'), ('messages_page', 'Messages page'), ('sidebar', 'Sidebar'), ('search', 'Search'), ('post_message', 'Post a message')]
RNAME = dict(ROUTES)
READS = [r for r, _ in ROUTES if r != 'post_message']
APPS = ['dotnetway', 'oncrack', 'fsharp', 'rails', 'rust', 'c']

# The phase gates' bench/quick runs (five workloads, c=1 and 16, 3 reps) against the stored medians of phase7-final.
PHASES = [('0', R + '/dotnetway-p0/quick', R + '/dotnetway-p0'),
          ('1', R + '/dotnetway-p1-fix/quick', R + '/dotnetway-p1-fix'),
          ('2', R + '/dotnetway-p2/quick', R + '/dotnetway-p2'),
          ('H', R + '/dotnetway-hybrid-2/new', None),
          ('3', R + '/dotnetway-p3/quick/new', R + '/dotnetway-p3'),
          ('4', R + '/dotnetway-p4/quick/new', R + '/dotnetway-p4'),
          ('5', R + '/dotnetway-p5/quick/new', R + '/dotnetway-p5'),
          ('6', R + '/dotnetway-p6/quick/new', R + '/dotnetway-p6'),
          ('7', R + '/dotnetway-p7/quick/new', R + '/dotnetway-p7'),
          ('8', R + '/dotnetway-p8/quick/new', R + '/dotnetway-p8'),
          ('9', R + '/dotnetway-p9/quick/new', R + '/dotnetway-p9'),
          ('10', R + '/dotnetway-p10/quick/new', R + '/dotnetway-p10')]

# Phase 10 on bench/upstream-quick, step by step (normal profile, same-session Rails).
STEPS = [('baseline', 'Before', R + '/phase10-baseline'), ('10.2', 'One connection', R + '/phase10-unit-10.2/upstream-quick'),
         ('10.3', 'Narrower miss', R + '/phase10-unit-10.3/upstream-quick-10.3b'), ('10.4', 'Page cache: room, messages', R + '/phase10-4'),
         ('10.5', 'Page cache: sidebar, search', R + '/phase10-5-normal'), ('gate', 'The gate', GATE + '/uq-normal')]


def e(s):
    return html.escape(str(s), quote=True)


def src_url(path):
    base = META.get('repo_url')
    if not base:
        return path
    kind = 'tree' if os.path.isdir(os.path.join(REPO, path.split('#')[0])) else 'blob'
    return '%s/%s/%s/%s' % (base.rstrip('/'), kind, META.get('branch', 'main'), path.rstrip('/'))


def lit(s, why='name'):
    return '<span data-lit="%s">%s</span>' % (e(why), e(s))


def prose(s):
    """Copy from site/data.json: escaped, with the few names that hold digits marked as names."""
    return re.sub(r'\b(UTF-8|HTML5)\b', lambda m: lit(m.group(1)), e(s))


def commit(s):
    return '<code>%s</code>' % e(s)


def flink(path, label=None):
    return '<a href="%s"><code>%s</code></a>' % (e(src_url(path)), e(label or path))


def ph(p):
    """A phase name as shown: 'Phase 4', 'Hybrid reads'."""
    return 'Hybrid reads' if p == 'H' else lit('Phase ' + p, 'phase')


# ================================================================ sources

class V:
    __slots__ = ('v', 'label', 'files', 'how')

    def __init__(self, v, label, files, how=''):
        self.v, self.label, self.files, self.how = v, label, list(files), how

    def over(self, o, label=None):
        return V(self.v / o.v, label or '%s, over %s' % (self.label, o.label), self.files + [f for f in o.files if f not in self.files],
                 '%s ÷ %s' % (g(self.v), g(o.v)))

    def minus(self, o, label):
        return V(self.v - o.v, label, self.files + [f for f in o.files if f not in self.files], '%s − %s' % (g(self.v), g(o.v)))


def g(x):
    return ('%.4g' % x) if isinstance(x, float) and abs(x) < 100 else format(round(x, 1), ',')


def vmin(vs, label):
    m = min(vs, key=lambda v: v.v)
    files = []
    for v in vs:
        files += [f for f in v.files if f not in files]
    return V(m.v, label, files, 'the smallest of ' + ', '.join('%.2f' % v.v for v in vs))


def vmax(vs, label):
    m = max(vs, key=lambda v: v.v)
    files = []
    for v in vs:
        files += [f for f in v.files if f not in files]
    return V(m.v, label, files, 'the largest of ' + ', '.join('%.2f' % v.v for v in vs))


class Ledger:
    def __init__(self):
        self.rows, self.by = [], {}

    def row(self, label, files, how, shown, block=False):
        for f in files:
            if not os.path.exists(os.path.join(REPO, f.split('#')[0])):
                raise SystemExit('build: source file missing: %s (for %s)' % (f, label))
        key = (label, tuple(files), how, shown)
        if key not in self.by:
            rid = 's-%d' % (len(self.rows) + 1)
            self.by[key] = rid
            self.rows.append({'id': rid, 'label': label, 'files': files, 'how': how, 'shown': shown, 'block': block})
        return self.by[key]

    def n(self, v, fmt='{:,.0f}', link=True):
        shown = fmt.format(v.v)
        rid = self.row(v.label, v.files, v.how, shown)
        if link:
            return '<a class="sn" href="#%s" data-src="%s">%s</a>' % (rid, rid, e(shown))
        return '<span class="sn" data-src="%s">%s</span>' % (rid, e(shown))

    def x(self, v, link=True):
        return self.n(v, '{:.2f}×', link)

    def d1(self, v, link=True):
        return self.n(v, '{:.1f}', link)

    def block(self, label, files, how):
        rid = self.row(label, files, how, '(every number in the block)', block=True)
        return 'data-src="%s"' % rid


L = Ledger()
_J = {}


def jload(rel):
    if not os.path.exists(os.path.join(REPO, rel)):
        raise SystemExit('build: source file missing: %s' % rel)
    if rel not in _J:
        _J[rel] = json.load(open(os.path.join(REPO, rel), encoding='utf-8'))
    return _J[rel]


def text(rel):
    if not os.path.exists(os.path.join(REPO, rel)):
        raise SystemExit('build: source file missing: %s' % rel)
    return open(os.path.join(REPO, rel), encoding='utf-8').read()


def md(rel, pattern, label, group=1):
    ms = list(re.finditer(pattern, text(rel)))
    if len(ms) != 1:
        raise SystemExit('build: %r matches %d times in %s (want 1)' % (pattern, len(ms), rel))
    return V(float(ms[0].group(group).replace(',', '')), label, [rel], 'written in the file: "%s"' % ms[0].group(0).strip())


def cpus(spec):
    n = 0
    for part in spec.split(','):
        a, _, b = part.partition('-')
        n += (int(b) - int(a) + 1) if b else 1
    return n


# ---------------------------------------------------------------- the six-ways table

GROUP_NORMAL, GROUP_MIXED = 'Each route alone', 'Reads while a writer posts 10/s'


def raw_median(sources, key, mixed):
    vals = []
    for f in sources:
        d = jload(f)
        rows = d['mixed_http' if mixed else 'http'] or []
        vals += [x['rps'] for x in rows if x.get('route') == key and x.get('conc') == 16]
    return statistics.median(vals), len(vals)


def six_cell(table_rel, app, key, mixed, stat='median'):
    t = jload(table_rel)
    row = next(r for r in t['rows'] if r['key'] == key and (r['group'] != GROUP_NORMAL) == mixed)
    c = dict(row['values'][app])
    if table_rel == PRE_T:
        # The first run's table.json was written before its sessions moved into before-phase10/; its sources still name the old paths.
        c['sources'] = [PRE + f[len(SIX):] if f.startswith(SIX + '/') and not f.startswith(PRE + '/') else f for f in c['sources']]
    if stat == 'median':
        m, n = raw_median(c['sources'], key, mixed)
        if n != c['n'] or abs(m - c['median']) > 0.06:
            raise SystemExit('build: %s %s %s: table.json says %s over %d rounds, the round files give %s over %d' % (table_rel, app, key, c['median'], c['n'], m, n))
    name = next(a['name'] for a in t['apps'] if a['id'] == app)
    era = 'after Phase 10' if table_rel == SIX_T else 'before Phase 10 (the first run)'
    return V(c[stat], '%s, %s, %s, req/s at c=16, %s of %d rounds over %s, six-ways bench %s' % (
        name, RNAME[key].lower(), 'with a writer posting' if mixed else 'alone', stat, c['n'], ' and '.join(row['sessions']), era),
        [table_rel] + c['sources'], '%s of the rounds: %s' % (stat, ', '.join(g(float(x)) for x in sorted(_round_vals(c['sources'], key, mixed)))) if stat == 'median' else 'the %s of the rounds' % stat)


def _round_vals(sources, key, mixed):
    out = []
    for f in sources:
        d = jload(f)
        rows = d['mixed_http' if mixed else 'http'] or []
        out += [x['rps'] for x in rows if x.get('route') == key and x.get('conc') == 16]
    return out


# ---------------------------------------------------------------- bin/kloc

def kloc(rel, project=None, col='corrected'):
    t = text(rel)
    if project is None:
        m = re.search(r'\*\*Total\*\* \| \*\*([\d,]+) \([\d.]+ KLOC\)\*\* \| \*\*([\d,]+) \([\d.]+ KLOC\)\*\*', t)
        v = float(m.group(1 if col == 'cloc' else 2).replace(',', ''))
        return V(v, 'backend lines of code by upstream\'s rules (bin/kloc, %s column), %s' % (col, rel.rsplit('/', 1)[0].split('/')[-1]), [rel],
                 'the Total row of bin/kloc\'s table, %s column' % col)
    m = re.search(r'^\| %s \| ([\d,]+) \| ([\d,]+) \|$' % re.escape(project), t, re.M)
    v = float(m.group(1 if col == 'cloc' else 2).replace(',', '')) if m else 0.0
    return V(v, '%s, lines of code (bin/kloc, %s column), %s' % (project, col, rel.rsplit('/', 1)[0].split('/')[-1]), [rel],
             ('the %s row, %s column' % (project, col)) if m else 'no %s row: the project is gone' % project)


def kloc_projects(rel):
    return [m.group(1) for m in re.finditer(r'^\| (Campfire\.[A-Za-z]+) \|', text(rel), re.M)]


# ---------------------------------------------------------------- bench/quick

def qmed(d, app, route, conc):
    files = sorted(os.path.relpath(f, REPO) for f in glob.glob(os.path.join(REPO, d, app + '-[0-9].json')))
    vals = [x['rps'] for f in files for x in jload(f)['http'] if x['route'] == route and x['conc'] == conc]
    if len(vals) != 3 or len(files) != 3:
        raise SystemExit('build: %s %s %s c=%d: %d samples in %d files (want 3)' % (d, app, route, conc, len(vals), len(files)))
    who = 'pinned Rails (stored)' if app == 'reference' else 'theDotNetWay'
    return V(statistics.median(vals), '%s, %s, c=%d, req/s, median of 3 reps (bench/quick, %s)' % (who, RNAME[route].lower(), conc, d), files,
             'median of ' + ', '.join(g(float(x)) for x in sorted(vals)))


# ---------------------------------------------------------------- upstream-quick summaries

def uq(d, app, route):
    rel = d + '/summary.json'
    c = jload(rel)['table'][route][app]
    return V(c['median'], '%s, %s, req/s at c=16, median of %d rounds (bench/upstream-quick, %s)' % (
        {'rails': 'current Rails', 'dotnetway': 'theDotNetWay', 'fsharp': 'F# port'}[app], RNAME[route].lower(), len(c['runs']), d), [rel],
        'median of ' + ', '.join(g(float(x)) for x in c['runs']))


# ---------------------------------------------------------------- the pixel gate

def pixel_gate():
    out = {'seeds': {}, 'engines': set(), 'viewports': set(), 'bps': set(), 'themes': set(), 'states': set(), 'files': [], 'differ': [], 'rerun_cells': 0, 'rerun_equal': 0}
    for f in sorted(glob.glob(os.path.join(REPO, GATE, 'full', '**', 'report.json'), recursive=True)):
        rel = os.path.relpath(f, REPO)
        rerun = '/rerun/' in rel
        seed = rel.split('/')[-2]
        out['files'].append(rel)
        for r in jload(rel)['results']:
            ly = {x['layer']: x for x in r['layers']}
            if 'pixels' not in ly:
                continue
            eq = all(ly.get(k, {}).get('equal') for k in ('pixels', 'aria', 'loose'))
            if rerun:
                out['rerun_cells'] += 1
                out['rerun_equal'] += eq
                continue
            s = out['seeds'].setdefault(seed, {'cells': 0, 'pixels': 0, 'aria': 0, 'loose': 0})
            s['cells'] += 1
            for k in ('pixels', 'aria', 'loose'):
                s[k] += bool(ly.get(k, {}).get('equal'))
            engine, view, theme = r['cell'].split('-')
            out['engines'].add(engine)
            (out['bps'] if view.startswith('bp') else out['viewports']).add(view)
            out['themes'].add(theme)
            out['states'].add((seed, r['state']))
            if not eq:
                out['differ'].append((seed, r['state'], r['cell']))
    tls = {'cells': 0, 'equal': 0, 'file': GATE + '/tls/default/report.json'}
    for r in jload(tls['file'])['results']:
        ly = {x['layer']: x for x in r['layers']}
        if 'pixels' in ly:
            tls['cells'] += 1
            tls['equal'] += all(ly.get(k, {}).get('equal') for k in ('pixels', 'aria', 'loose'))
    out['tls'] = tls
    return out


# ================================================================ facts

def facts():
    F = {}
    t = jload(SIX_T)
    F['apps'] = t['apps']
    F['conc'] = V(t['concurrency'], 'concurrent clients in every cell of the six-ways table', [SIX_T], 'table.json "concurrency"')
    meta_n = jload(SIX + '/normal-1/summary.json')['metadata']
    meta_m = jload(SIX + '/mixed-1/mixed-summary.json')['metadata']
    F['meta_n'], F['meta_m'] = meta_n, meta_m
    F['srv_cpus'] = V(cpus(meta_n['server_cpus']), 'server CPUs per app (the harness pins the app to cpus %s)' % meta_n['server_cpus'], [SIX + '/normal-1/summary.json'], 'count of metadata.server_cpus')
    F['cli_cpus'] = V(cpus(meta_n['client_cpus']), 'load-generator CPUs (cpus %s)' % meta_n['client_cpus'], [SIX + '/normal-1/summary.json'], 'count of metadata.client_cpus')
    F['secs'] = V(meta_n['duration'], 'seconds per round', [SIX + '/normal-1/summary.json'], 'metadata.duration')
    F['rounds'] = V(meta_n['rounds'], 'rounds per app per session', [SIX + '/normal-1/summary.json'], 'metadata.rounds')
    F['wps'] = V(meta_m['mixed_writer']['maximum_writes_per_second'], 'posts a second by the mixed profile\'s writer', [SIX + '/mixed-1/mixed-summary.json'], 'metadata.mixed_writer.maximum_writes_per_second')
    sess = sorted({s for r in t['rows'] for s in r['sessions']})
    F['sessions'] = sess
    F['n_sess'] = V(len(sess), 'sessions of the six-ways bench after Phase 10, every app in each', [SIX_T], 'distinct sessions named in table.json rows')
    F['n_rounds'] = V(t['rows'][0]['values']['dotnetway']['n'], 'rounds pooled into each cell', [SIX_T], 'table.json values.n')
    F['six'], F['pre'] = {}, {}
    for key, nm in ROUTES:
        for mixed in (False, True):
            if mixed and key == 'post_message':
                continue
            for a in APPS:
                for stat in ('median', 'min', 'max'):
                    F['six'][a, key, mixed, stat] = six_cell(SIX_T, a, key, mixed, stat)
                F['pre'][a, key, mixed] = six_cell(PRE_T, a, key, mixed)
    F['pre_apps'] = jload(PRE_T)['apps']
    # theDotNetWay over the same sessions' Rails
    F['xr'] = {(k, m): F['six']['dotnetway', k, m, 'median'].over(F['six']['rails', k, m, 'median'], 'theDotNetWay over current Rails, %s, %s, pooled medians after Phase 10' % (RNAME[k].lower(), 'with a writer' if m else 'alone'))
               for k, _ in ROUTES for m in (False, True) if not (m and k == 'post_message')}
    F['xr_pre'] = {(k, m): F['pre']['dotnetway', k, m].over(F['pre']['rails', k, m], 'theDotNetWay over current Rails, %s, %s, pooled medians before Phase 10 (first run)' % (RNAME[k].lower(), 'with a writer' if m else 'alone'))
                   for k, _ in ROUTES for m in (False, True) if not (m and k == 'post_message')}
    F['xr_lo'] = vmin([F['xr'][k, False] for k, _ in ROUTES], 'theDotNetWay over current Rails, the thinnest of the five routes alone, after Phase 10')
    F['xr_hi'] = vmax([F['xr'][k, False] for k, _ in ROUTES], 'theDotNetWay over current Rails, the widest of the five routes alone, after Phase 10')
    F['xm_lo'] = vmin([F['xr'][k, True] for k in READS], 'theDotNetWay over current Rails, the thinnest of the four reads with a writer, after Phase 10')
    F['xm_hi'] = vmax([F['xr'][k, True] for k in READS], 'theDotNetWay over current Rails, the widest of the four reads with a writer, after Phase 10')
    F['pre_lo'] = vmin([F['xr_pre'][k, False] for k in READS], 'theDotNetWay over current Rails, the thinnest of the four reads alone, before Phase 10')
    F['pre_hi'] = vmax([F['xr_pre'][k, False] for k in READS], 'theDotNetWay over current Rails, the widest of the four reads alone, before Phase 10')
    F['pre_mlo'] = vmin([F['xr_pre'][k, True] for k in READS], 'theDotNetWay over current Rails, the thinnest read with a writer, before Phase 10')
    F['pre_mhi'] = vmax([F['xr_pre'][k, True] for k in READS], 'theDotNetWay over current Rails, the widest read with a writer, before Phase 10')
    F['gain'] = {(k, m): F['six']['dotnetway', k, m, 'median'].over(F['pre']['dotnetway', k, m], 'theDotNetWay after Phase 10 over before, %s, %s' % (RNAME[k].lower(), 'with a writer' if m else 'alone'))
                 for k, _ in ROUTES for m in (False, True) if not (m and k == 'post_message')}
    # where theDotNetWay stands against each other column
    F['ahead'] = {}
    rows = [(k, m) for k, _ in ROUTES for m in (False, True) if not (m and k == 'post_message')]
    F['n_rows'] = V(len(rows), 'rows of the six-ways table', [SIX_T], 'len(rows)')
    for a in APPS[1:]:
        F['ahead'][a] = V(sum(F['six']['dotnetway', k, m, 'median'].v > F['six'][a, k, m, 'median'].v for k, m in rows),
                          'rows of the six-ways table in which theDotNetWay\'s pooled median is above %s\'s' % a, [SIX_T], 'count of rows where dotnetway median > %s median' % a)
    # the old floor's Rails
    F['pin'] = {(k, c): qmed(STORED, 'reference', k, c) for k, _ in ROUTES for c in (1, 16)}
    F['pin_vs_cur'] = F['six']['rails', 'room_show', False, 'median'].over(F['pin']['room_show', 16], 'current Rails on upstream\'s harness over the pinned Rails of bench/quick, room page at c=16')
    # KLOC
    F['k_now'] = kloc(KLOC_NOW)
    F['k_now_cloc'] = kloc(KLOC_NOW, col='cloc')
    F['k_rust'] = md(KLOC_NOW, r'Rust f9dca47: ([\d.]+) KLOC', 'the Rust port (f9dca47), KLOC by upstream\'s rules, as onCrack counted it (bin/kloc prints the stored count)')
    F['k_rails'] = md(KLOC_NOW, r'Rails 0aa339d: ([\d.]+)\.', 'Rails (0aa339d), KLOC by upstream\'s rules (bin/kloc prints the stored count)')
    F['k_p0'] = kloc(R + '/dotnetway-p0/kloc.md')
    F['k_now_k'] = V(F['k_now'].v / 1000, 'theDotNetWay today, KLOC by upstream\'s rules, corrected column', F['k_now'].files, F['k_now'].how + ' / 1000')
    F['k_now_cloc_k'] = V(F['k_now_cloc'].v / 1000, 'theDotNetWay today, KLOC as cloc 2.10 prints it', F['k_now_cloc'].files, F['k_now_cloc'].how + ' / 1000')
    F['k_p0_k'] = V(F['k_p0'].v / 1000, 'the F# port at the fork (Phase 0), KLOC by upstream\'s rules, corrected column', F['k_p0'].files, F['k_p0'].how + ' / 1000')
    # phases
    F['ph'] = []
    for p, qd, kd in PHASES:
        row = {'p': p, 'qd': qd}
        if kd:
            row['k'] = kloc(kd + '/kloc.md')
        else:
            row['k'] = md(PLAN, r'\*\*(35,408) corrected \(35\.4 KLOC\)\*\*', 'backend lines of code after the hybrid reads (unit H.2, bin/kloc, corrected column, written in the Phase log)')
            row['k'] = V(row['k'].v, row['k'].label, row['k'].files, row['k'].how)
        row['q'] = {(k, c): qmed(qd, 'dotnetway', k, c) for k, _ in ROUTES for c in (1, 16)}
        row['x'] = {(k, c): row['q'][k, c].over(F['pin'][k, c], 'theDotNetWay over pinned Rails, %s, c=%d, %s' % (RNAME[k].lower(), c, ph_plain(p))) for k, _ in ROUTES for c in (1, 16)}
        row['thin'] = vmin([row['x'][k, 16] for k, _ in ROUTES], 'theDotNetWay over pinned Rails, the thinnest of the five workloads at c=16, %s' % ph_plain(p))
        F['ph'].append(row)
    F['ph_up'] = {'9': vmin([F['xr_pre'][k, False] for k, _ in ROUTES], 'theDotNetWay over current Rails on upstream\'s harness, thinnest of the five routes, Phase 9 (first six-ways run)'),
                  '10': F['xr_lo']}
    # Phase 10 step by step
    F['steps'] = []
    for sid, nm, d in STEPS:
        st = {'id': sid, 'name': nm, 'dir': d, 'x': {}}
        for k, _ in ROUTES:
            st['x'][k] = uq(d, 'dotnetway', k).over(uq(d, 'rails', k), 'theDotNetWay over the same session\'s Rails, %s, upstream-quick %s' % (RNAME[k].lower(), sid))
        F['steps'].append(st)
    # proofs
    F['pix'] = pixel_gate()
    # The closing commit's bin/verify (the Phases 9-10 verifier's run, after the security review's fixes and tests), not the gate's.
    F['verify'] = {k: md(R + '/dotnetway-p9p10-verify/verify.log', r'(?m)^  %s: (\d+)$' % k, 'bin/verify on the closing commit (Phases 9-10 verification): tests %s' % k) for k in ('total', 'succeeded', 'failed', 'skipped')}
    F['cookie'] = md(GATE + '/cookie-roundtrip.log', r'cookie-roundtrip: (\d+)/\d+ checks passed', 'bin/cookie-roundtrip at the Phase 10 gate: checks passed')
    F['cookie_n'] = md(GATE + '/cookie-roundtrip.log', r'cookie-roundtrip: \d+/(\d+) checks passed', 'bin/cookie-roundtrip at the Phase 10 gate: checks run')
    F['assets'] = md(GATE + '/asset-check.log', r'asset-check: (\d+) checks, \d+ failed', 'bin/asset-check at the Phase 10 gate: checks')
    F['assets_f'] = md(GATE + '/asset-check.log', r'asset-check: \d+ checks, (\d+) failed', 'bin/asset-check at the Phase 10 gate: failed')
    dbd = text(GATE + '/db-differential.log')
    if 'rollback ok' not in dbd or 'agree' not in dbd:
        raise SystemExit('build: the Phase 10 gate\'s db-differential log does not say the schemas agree and the rollback was ok')
    sec = re.findall(r'^### .*[Ss]ecurity review.*$', text(PLAN), re.M)
    F['sec'] = V(len(sec), 'security reviews in the Phase log (one per risky layer, then Phases 9 and 10 together)', [PLAN], 'count of "### ... security review ..." headings')
    F['sec_titles'] = sec
    return F


def ph_plain(p):
    return 'hybrid reads (unit H.2)' if p == 'H' else 'Phase %s gate' % p


# ================================================================ helpers for the page

FX = {}


def panel(pid, kicker, title, body, back):
    return ('<div class="panel" id="%s" role="dialog" aria-modal="true" aria-labelledby="%s-t">'
            '<a class="scrim" href="#%s" data-close tabindex="-1" aria-hidden="true"></a>'
            '<div class="sheet" tabindex="-1"><header class="sheet-head"><div><p class="kicker">%s</p><h2 id="%s-t">%s</h2></div>'
            '<a class="x" href="#%s" data-close aria-label="Close panel"><span aria-hidden="true">&times;</span></a></header>'
            '<div class="sheet-body">%s<p class="back"><a href="#%s" data-close>&larr; Back to the page</a></p></div></div></div>'
            % (pid, pid, back, kicker, pid, title, back, body, back))


def more(pid, label='The details'):
    return '<a class="more" href="#%s"><span>%s</span> <span aria-hidden="true">&rarr;</span></a>' % (pid, label)


def srcline(*paths, lead='Sources'):
    return '<p class="src">%s: %s.</p>' % (lead, ', '.join(flink(p) for p in paths))


def tbl(head, rows, caption=None, cls=''):
    th = ''.join('<th scope="col"%s>%s</th>' % (' class="num"' if num else '', h) for h, num in head)
    body = ''.join('<tr>%s</tr>' % ''.join(('<th scope="row">%s</th>' % c) if i == 0 else ('<td class="num">%s</td>' % c if head[i][1] else '<td>%s</td>' % c)
                                          for i, c in enumerate(r)) for r in rows)
    return '<div class="tw %s"><table>%s<thead><tr>%s</tr></thead><tbody>%s</tbody></table></div>' % (
        cls, ('<caption>%s</caption>' % caption) if caption else '', th, body)


def appname(a, apps=None):
    return next(x['name'] for x in (apps or FX['F']['apps']) if x['id'] == a)


def appcommit(a, apps=None):
    return next(x['commit'] for x in (apps or FX['F']['apps']) if x['id'] == a)


def group_label(F, mixed):
    return GROUP_MIXED.replace('10', L.n(F['wps'])) if mixed else GROUP_NORMAL


def claims_check(F):
    rows = [(k, m) for k, _ in ROUTES for m in (False, True) if not (m and k == 'post_message')]
    for k, m in rows:
        if not F['xr'][k, m].v > 1:
            raise SystemExit('build: theDotNetWay is not ahead of Rails on %s (%s); the hero says it is ahead in every row' % (k, m))
    if F['ahead']['oncrack'].v != 0 or F['ahead']['rust'].v != 0 or F['ahead']['c'].v != 0:
        raise SystemExit('build: theDotNetWay is now ahead of onCrack, Rust or C in some row; the copy says it is behind them in every row')
    if not F['xr_lo'].v >= 5:
        raise SystemExit('build: a normal route is under 5x Rails; the copy says every route clears the floor')
    if not F['pre_lo'].v < 1:
        raise SystemExit('build: the first run had no read under 1x Rails; the "about the speed" copy is stale')
    if not F['k_now_cloc'].v < F['k_rust'].v * 1000:
        raise SystemExit('build: theDotNetWay is no longer under Rust\'s count')


# ================================================================ sections

def hero(F):
    pills = ''.join('<li>%s</li>' % e(p) for p in D['pills'])
    stack = ''.join('<li style="--i:%d"><b>%s</b><span>%s</span></li>' % (i, e(a), e(b)) for i, (a, b) in enumerate(D['stack']))
    tiles = [
        ('<span class="big">%s</span><span class="unit">KLOC</span>' % L.d1(F['k_now_k']),
         'of backend F#, by upstream&#39;s counting rules (%s as cloc prints it). Rust&#39;s port is %s, Rails %s. The port it forked from was %s.'
         % (L.d1(F['k_now_cloc_k']), L.d1(F['k_rust']), L.d1(F['k_rails']), L.d1(F['k_p0_k']))),
        ('<span class="big">%s<i>&ndash;</i>%s</span>' % (L.x(F['xr_lo']), L.x(F['xr_hi'])),
         'current Rails, each route alone, on Basecamp&#39;s own harness in the same sessions. %s&ndash;%s while a writer posts.' % (L.x(F['xm_lo']), L.x(F['xm_hi']))),
        ('<span class="big">%s<i>/</i>%s</span>' % (L.n(F['pix_eq']), L.n(F['pix_cells'])),
         'screenshots pixel-equal to Rails on the full matrix: %s seeds, %s engines, %s viewports and %s breakpoint widths, light and dark. Accessibility tree and DOM equal too.'
         % (L.n(F['pix_seeds']), L.n(F['pix_engines']), L.n(F['pix_views']), L.n(F['pix_bps']))),
    ]
    tiles_html = ''.join('<li class="tile"><p class="tile-n">%s</p><p class="tile-l">%s</p></li>' % t for t in tiles)
    return ('<section class="hero" id="top" aria-labelledby="hero-h"><div class="aura" aria-hidden="true"></div><div class="wrap hero-grid">'
            '<div class="hero-copy"><p class="kicker rise">Basecamp&#39;s ONCE Campfire &middot; the third Campfire in F#</p>'
            '<h1 id="hero-h" class="rise">Campfire,<br>the .NET way.</h1>'
            '<p class="lede rise">ASP.NET Core, EF Core with Dapper on the hot reads, Kestrel, the framework&#39;s middleware and OutputCache, Falco.Markup, AngleSharp. '
            'The code a .NET team would write. Every page is pixel-identical to Rails, the database and cookies are Rails&#39; own, and it serves '
            '%s&ndash;%s the requests of current Rails on Basecamp&#39;s harness.</p>'
            '<ul class="pills rise" aria-label="What it is built on">%s</ul></div>'
            '<div class="stack rise" aria-hidden="true"><ol>%s</ol></div></div>'
            '<div class="wrap"><ul class="tiles rise">%s</ul>'
            '<p class="honest rise"><b>Not the fastest Campfire.</b> In the table below it is ahead of Rails in all %s rows and of the F# port in %s; '
            'onCrack, C and Rust are ahead of it in every row. It gives up speed for the framework&#39;s way, on purpose. <em>Every number on this page links to the file it came from.</em></p></div></section>'
            % (L.x(F['xr_lo']), L.x(F['xr_hi']), pills, stack, tiles_html, L.n(F['n_rows']), L.n(F['ahead']['fsharp'])))


def six_table(F):
    apps = F['apps']
    head = ['<th scope="col" class="c-grp"><span class="sr">Group</span></th><th scope="col" class="c-wl">Workload</th>']
    for a in apps:
        head.append('<th scope="col" class="c-app%s"><span class="an">%s</span><span class="ac">%s</span></th>'
                    % (' feat' if a['id'] == 'dotnetway' else '', e(a['name']), lit(a['commit'], 'commit')))
    body = []
    for mixed in (False, True):
        keys = READS if mixed else [k for k, _ in ROUTES]
        glabel = group_label(F, mixed)
        body.append('<tbody class="%s"><tr class="grp-row"><th colspan="%d" scope="rowgroup">%s</th></tr>' % ('g-mix' if mixed else 'g-alone', len(apps) + 1, glabel))
        for i, k in enumerate(keys):
            vals = {a['id']: F['six'][a['id'], k, mixed, 'median'] for a in apps}
            best = max(vals, key=lambda a: vals[a].v)
            cells = []
            if i == 0:
                cells.append('<th scope="rowgroup" class="c-grp" rowspan="%d">%s</th>' % (len(keys), glabel))
            cells.append('<th scope="row" class="c-wl">%s <small>req/s</small></th>' % RNAME[k])
            for a in apps:
                cls = ['num']
                if a['id'] == best:
                    cls.append('best')
                if a['id'] == 'dotnetway':
                    cls.append('feat')
                extra = ('<small class="xr">%s Rails</small>' % L.x(F['xr'][k, mixed])) if a['id'] == 'dotnetway' else ''
                cells.append('<td class="%s">%s%s</td>' % (' '.join(cls), L.n(vals[a['id']]), extra))
            body.append('<tr%s>%s</tr>' % (' class="last"' if (mixed and i == len(keys) - 1) else '', ''.join(cells)))
        body.append('</tbody>')
    return ('<div class="sw-scroll"><table class="sw"><caption class="sr">Campfire, six ways: requests per second</caption><thead><tr>%s</tr></thead>%s</table></div>'
            % (''.join(head), ''.join(body)))


def six_ways(F):
    topo = F['meta_n']['topology']
    cache = []
    for a in APPS:
        tp = topo[a]
        if 'response_cache_mb' in tp:
            c = '%s <span data-lit="unit">MB</span> response cache' % L.n(V(float(tp['response_cache_mb']), '%s: response cache size the harness records' % appname(a), [SIX + '/normal-1/summary.json'], 'metadata.topology.%s.response_cache_mb' % a))
        elif 'cache_bytes' in tp:
            c = '%s <span data-lit="unit">MiB</span> cache' % L.n(V(float(tp['cache_bytes']) / 2 ** 20, 'C: cache size the harness records, MiB', [SIX + '/normal-1/summary.json'], 'metadata.topology.c.cache_bytes / 2^20'))
        else:
            c = 'no response cache'
        cache.append('<li><b>%s</b> %s. %s</li>' % (e(appname(a)), c, prose(D['apps'][a])))
    return ('<section class="band" id="six" aria-labelledby="six-h"><div class="wrap">'
            '<p class="kicker reveal">The table</p><h2 id="six-h" class="reveal">Campfire, six ways.</h2>'
            '<p class="sub reveal">Requests per second at %s clients, %s server CPUs per app, Basecamp&#39;s <code>once-campfire-verification</code> harness, '
            'all six apps in the same %s sessions. Higher is better; the fastest in each row is shaded, theDotNetWay is outlined. Every cell is the median of %s rounds.</p>'
            '<figure class="reveal sw-fig">%s<figcaption>'
            '<p><b>Method.</b> Upstream&#39;s <code>bin/benchmark</code> on this project&#39;s Linux VM (not Basecamp&#39;s or Ryan Rodemoyer&#39;s rig): %s rounds of %s s per app per session, '
            'the app on %s CPUs and the load generator on %s, every response checked against the seed&#39;s contract and every post audited in the database. '
            'The writer group adds one client posting %s messages a second into the room. Numbers from other rigs or older sessions are not mixed in. '
            '<b>Upstream&#39;s contract asks a page cache to re-check the session and the room membership on every hit;</b> theDotNetWay and onCrack (its fresh mode) do it on every request, '
            'stricter than upstream&#39;s own, which serves the session from its epoch.</p>'
            '<ul class="apps">%s</ul><p class="btw">%s %s %s</p></figcaption></figure></div></section>'
            % (L.n(F['conc']), L.n(F['srv_cpus']), L.n(F['n_sess']), L.n(F['n_rounds']), six_table(F),
               L.n(F['rounds']), L.n(F['secs']), L.n(F['srv_cpus']), L.n(F['cli_cpus']), L.n(F['wps']), ''.join(cache),
               more('p-method', 'Method, sessions, images'), more('p-ranges', 'Every cell with its range'), more('p-steps', ph('10') + ', step by step')))


def bars(F):
    """Before and after Phase 10, x the same sessions' Rails, per row. HTML bars; every label is a ledger link."""
    hi = max(F['xr'][k, m].v for k, m in F['xr']) * 1.2
    rows = []
    for mixed in (False, True):
        keys = READS if mixed else [k for k, _ in ROUTES]
        rows.append('<li class="bb-g">%s</li>' % group_label(F, mixed))
        for k in keys:
            b, a = F['xr_pre'][k, mixed], F['xr'][k, mixed]
            rows.append('<li class="bb"><span class="bb-l">%s</span><span class="bb-t">'
                        '<span class="bb-b pre" style="--w:%.2f%%"><span class="bb-v">%s</span></span>'
                        '<span class="bb-b now" style="--w:%.2f%%"><span class="bb-v">%s</span></span></span></li>'
                        % (RNAME[k], 100 * b.v / hi, L.x(b), 100 * a.v / hi, L.x(a)))
    floor = 100 * 5 / hi
    return ('<figure class="bbars reveal" style="--floor:%.2f%%;--one:%.2f%%"><div class="bb-key"><span><i class="k-pre"></i>%s, the first run</span><span><i class="k-now"></i>after %s</span>'
            '<span><i class="k-floor"></i>the floor, five times Rails</span><span><i class="k-one"></i>Rails</span></div><ol>%s</ol>'
            '<figcaption>theDotNetWay over the same sessions&#39; current Rails (pooled medians of each run). Before: %s; after: %s.</figcaption></figure>'
            % (floor, 100 / hi, ph('9'), ph('10'), ''.join(rows), flink(PRE_T), flink(SIX_T)))


def speed(F):
    pin_c1, pin_c16 = F['pin']['room_show', 1], F['pin']['room_show', 16]
    cur = F['six']['rails', 'room_show', False, 'median']
    return ('<section class="band alt" id="speed" aria-labelledby="speed-h"><div class="wrap speed-grid">'
            '<div class="speed-copy"><p class="kicker reveal">About the speed</p><h2 id="speed-h" class="reveal">Five times which Rails?</h2>'
            '<p class="reveal">Through %s, every phase was held to a floor of five times Rails, measured with <code>bench/quick</code> against stored numbers for an old, pinned Rails: '
            'about %s room pages a second with one client, %s with sixteen. Every phase cleared it, which is where the earlier &ldquo;five times Rails&rdquo; came from.</p>'
            '<p class="reveal">Basecamp&#39;s harness runs current Rails with a page cache, and it serves %s room pages a second: %s the pinned one. '
            'The first run there put theDotNetWay at <b>%s&ndash;%s Rails on the reads</b> (%s&ndash;%s with a writer). The room page was slower than Rails&#39;.</p>'
            '<p class="reveal">%s found why: about half a millisecond of CPU a page, most of it the data layer opening a pooled connection and preparing a statement for every one of nine statements, and the JIT still warming up on the first route. '
            'It opened one connection per request, set the tiering delay to zero, moved one slow read to Dapper, and added a page cache in the four GET actions on HybridCache that re-reads the session and the membership on every hit. '
            'The framework&#39;s OutputCache could not do it: it never stores a response that sets a cookie, and the room page always does.</p>'
            '<p class="reveal"><b>Now every route alone is %s&ndash;%s current Rails</b>, and the new floor is measured on Basecamp&#39;s harness, in the same session. %s</p></div>'
            '%s</div></section>'
            % (ph('9'), L.n(pin_c1), L.n(pin_c16), L.n(cur), L.n(cur.over(pin_c16, 'current Rails over the pinned Rails of bench/quick, room page at c=16'), '{:.0f}×'),
               L.x(F['pre_lo']), L.x(F['pre_hi']), L.x(F['pre_mlo']), L.x(F['pre_mhi']), ph('10'),
               L.x(F['xr_lo']), L.x(F['xr_hi']), more('p-steps', 'Each step, measured'), bars(F)))


# ---------------------------------------------------------------- the climb chart

def climb(F):
    pts = F['ph']
    n = len(pts)
    W, H = 1000, 560
    X0, X1 = 70, 870
    xs = [X0 + (X1 - X0) * (i + 0.5) / n for i in range(n)]
    # KLOC panel
    KT, KB, KMAX = 52, 214, 42000
    ky = lambda v: KB - (KB - KT) * v / KMAX
    # speed panel
    ST, SB = 290, 510
    smax = max(max(r['thin'].v for r in pts), max(v.v for v in F['ph_up'].values())) * 1.12
    sy = lambda v: SB - (SB - ST) * v / smax
    files = sorted({f for r in pts for f in r['k'].files + r['thin'].files} | {f for v in F['ph_up'].values() for f in v.files})
    blk = L.block('the climb chart: backend KLOC (corrected) at each phase gate, and theDotNetWay over Rails, the thinnest of the five workloads at c=16, against the pinned Rails (bench/quick) and, for Phases 9 and 10, current Rails on upstream\'s harness',
                  files, 'bars: bin/kloc totals; solid line: each gate\'s bench/quick medians over phase7-final\'s stored Rails medians, smallest of five; teal: six-ways pooled medians over the same sessions\' Rails, smallest of five')
    s = ['<svg viewBox="0 0 %d %d" role="img" aria-labelledby="climb-cap" preserveAspectRatio="none">' % (W, H)]
    s.append('<text class="cl-ph" x="%d" y="30">Backend KLOC, corrected (bin/kloc)</text>' % X0)
    for v in range(0, KMAX + 1, 10000):
        s.append('<line class="cl-grid" x1="%d" x2="%d" y1="%.1f" y2="%.1f"/><text class="cl-tick" x="%d" y="%.1f" text-anchor="end">%d</text>' % (X0, X1, ky(v), ky(v), X0 - 10, ky(v) + 4, v // 1000))
    for v, cls, nm in ((F['k_rust'].v * 1000, 'cl-rust', 'Rust %.1f' % F['k_rust'].v), (F['k_rails'].v * 1000, 'cl-rails', 'Rails %.1f' % F['k_rails'].v)):
        s.append('<line class="%s" x1="%d" x2="%d" y1="%.1f" y2="%.1f"/><text class="cl-ref" x="%d" y="%.1f">%s</text>' % (cls, X0, X1, ky(v), ky(v), X1 + 10, ky(v) + 4, nm))
    bw = (X1 - X0) / n * 0.56
    for i, r in enumerate(pts):
        y = ky(r['k'].v)
        s.append('<rect class="cl-bar%s" x="%.1f" y="%.1f" width="%.1f" height="%.1f" rx="4"/>' % (' cl-last' if i == n - 1 else '', xs[i] - bw / 2, y, bw, KB - y))
        s.append('<text class="cl-bv" x="%.1f" y="%.1f" text-anchor="middle">%.1f</text>' % (xs[i], y - 7, r['k'].v / 1000))
    s.append('<text class="cl-ph" x="%d" y="%d">The thinnest workload, times Rails</text>' % (X0, ST - 22))
    step = 2 if smax < 16 else 5
    v = 0
    while v <= smax:
        s.append('<line class="cl-grid" x1="%d" x2="%d" y1="%.1f" y2="%.1f"/><text class="cl-tick" x="%d" y="%.1f" text-anchor="end">%d&times;</text>' % (X0, X1, sy(v), sy(v), X0 - 10, sy(v) + 4, v))
        v += step
    s.append('<line class="cl-floor" x1="%d" x2="%d" y1="%.1f" y2="%.1f"/><text class="cl-ref cl-floor-t" x="%d" y="%.1f">the floor, 5&times;</text>' % (X0, X1, sy(5), sy(5), X0 + 236, sy(5) + 16))
    line = ' '.join('%s%.1f %.1f' % ('M' if i == 0 else 'L', xs[i], sy(r['thin'].v)) for i, r in enumerate(pts))
    area = line + ' L%.1f %d L%.1f %d Z' % (xs[-1], SB, xs[0], SB)
    s.append('<path class="cl-area" d="%s"/><path class="cl-line" d="%s"/>' % (area, line))
    i9, i10 = [i for i, r in enumerate(pts) if r['p'] in ('9', '10')]
    s.append('<path class="cl-up" d="M%.1f %.1f L%.1f %.1f"/>' % (xs[i9], sy(F['ph_up']['9'].v), xs[i10], sy(F['ph_up']['10'].v)))
    for i, r in enumerate(pts):
        s.append('<circle class="cl-pt" cx="%.1f" cy="%.1f" r="5.5"/>' % (xs[i], sy(r['thin'].v)))
    for p, i in (('9', i9), ('10', i10)):
        s.append('<circle class="cl-upt" cx="%.1f" cy="%.1f" r="6.5"/>' % (xs[i], sy(F['ph_up'][p].v)))
    yp, yu = sy(pts[-1]['thin'].v), sy(F['ph_up']['10'].v)
    if abs(yp - yu) < 30:
        mid = (yp + yu) / 2
        yp, yu = (mid - 15, mid + 15) if yp <= yu else (mid + 15, mid - 15)
    s.append('<text class="cl-lab cl-lab-pin" x="%d" y="%.1f">pinned Rails<tspan x="%d" dy="14" class="cl-lab-s">bench/quick</tspan></text>' % (X1 + 10, yp - 4, X1 + 10))
    s.append('<text class="cl-lab cl-lab-up" x="%d" y="%.1f">current Rails<tspan x="%d" dy="14" class="cl-lab-s">upstream&#39;s harness</tspan></text>' % (X1 + 10, yu + 2, X1 + 10))
    for i, r in enumerate(pts):
        s.append('<text class="cl-x" x="%.1f" y="%d" text-anchor="middle">%s</text>' % (xs[i], SB + 26, 'H' if r['p'] == 'H' else 'P' + r['p']))
    s.append('</svg>')
    # columns with hover cards
    cols = []
    for i, r in enumerate(pts):
        name, what = D['phases'][r['p']]
        trs = ''.join('<tr><th scope="row">%s</th><td>%s</td><td>%s</td></tr>' % (RNAME[k], L.n(r['q'][k, 16]), L.x(r['x'][k, 16])) for k, _ in ROUTES)
        up = ''
        if r['p'] in F['ph_up']:
            src = F['xr_pre'] if r['p'] == '9' else F['xr']
            up = ('<p class="cd-up"><b>On upstream&#39;s harness, against current Rails:</b> %s.</p>'
                  % ', '.join('%s %s' % (RNAME[k].lower(), L.x(src[k, False])) for k, _ in ROUTES))
        flip = ' flip' if i >= n / 2 else ''
        cols.append('<div class="cl-col%s" tabindex="0" style="left:%.3f%%;width:%.3f%%" aria-label="%s">'
                    '<span class="cl-hit" aria-hidden="true"></span>'
                    '<div class="cd" role="group"><p class="cd-k">%s</p><p class="cd-h">%s</p><p class="cd-w">%s</p>'
                    '<p class="cd-kloc"><b>%s</b> KLOC, corrected</p>'
                    '<table><thead><tr><th scope="col">c=%s, <code>bench/quick</code></th><th scope="col">req/s</th><th scope="col">&times; pinned Rails</th></tr></thead><tbody>%s</tbody></table>%s'
                    '<p class="cd-src">%s</p></div></div>'
                    % (flip, 100 * (xs[i] - (X1 - X0) / n / 2) / W, 100 * ((X1 - X0) / n) / W, e(name),
                       ph(r['p']), e(name), prose(what), L.d1(V(r['k'].v / 1000, r['k'].label + ', in thousands', r['k'].files, r['k'].how + ' / 1000')),
                       L.n(V(16, 'concurrency of the c=16 column of bench/quick', [r['qd'] + '/dotnetway-1.json'], 'http[].conc')), trs, up, flink(r['qd'], r['qd'].replace('bench/results/', ''))))
    last = pts[-1]
    return ('<section class="band" id="climb" aria-labelledby="climb-h"><div class="wrap">'
            '<p class="kicker reveal">The climb</p><h2 id="climb-h" class="reveal">Lines down. Speed lost, then found.</h2>'
            '<p class="sub reveal">From the fork (%s KLOC) to %s KLOC, one layer a phase. The data layer&#39;s move to EF Core cost the pages most of their speed, Dapper on the hot reads made them about half again as fast, and %s, the page cache, took the room page from %s to %s requests a second on <code>bench/quick</code>.</p>'
            '<figure class="climb reveal" %s><div class="cl-scroll"><div class="cl-chart">%s%s</div></div>'
            '<figcaption id="climb-cap"><span><b>Which Rails.</b> The violet line is each phase gate&#39;s <code>bench/quick</code> run over the stored medians of the pinned Rails (%s); the teal pair is the six-ways bench over current Rails in the same sessions. Both show the thinnest of the five workloads at sixteen clients. '
            'Hover, tap or tab to a phase for its numbers.</span>%s</figcaption></figure></div></section>'
            % (L.d1(F['k_p0_k']), L.d1(F['k_now_k']), ph('10'), L.n(F['ph'][-2]['q']['room_show', 16]), L.n(last['q']['room_show', 16]),
               blk, ''.join(s), ''.join(cols), flink(STORED), more('p-climb', 'Every phase, as a table')))


# ---------------------------------------------------------------- layers

def layers(F):
    cards = []
    for ly in D['layers']:
        b, a = ly['before'], ly['after']
        kb = [kloc(R + '/%s/kloc.md' % b, p) for p in ly['projects']]
        ka = [kloc(R + '/%s/kloc.md' % a, p) for p in ly['projects']]
        sb = V(sum(v.v for v in kb), '%s before (%s), lines of code, corrected' % (' + '.join(ly['projects']), b), [R + '/%s/kloc.md' % b], ' + '.join(g(v.v) for v in kb))
        sa = V(sum(v.v for v in ka), '%s after (%s), lines of code, corrected' % (' + '.join(ly['projects']), a), [R + '/%s/kloc.md' % a], ' + '.join(g(v.v) for v in ka))
        hk = next(r['k'] for r in F['ph'] if r['p'] == 'H')
        tb = hk if ly.get('tot_before') == 'H' else kloc(R + '/%s/kloc.md' % b)
        ta = hk if ly.get('tot_after') == 'H' else kloc(R + '/%s/kloc.md' % a)
        projs = ', '.join(lit(p, 'project') if re.search(r'\d', p) else e(p) for p in ly['projects'])
        pills = ''.join('<li>%s</li>' % e(p) for p in ly['pills'])
        why = ('<p class="ly-why"><b>Why Dapper is still the .NET way.</b> %s</p>' % prose(D['dapper_why'])) if ly.get('dapper') else ''
        cards.append('<li class="ly%s%s reveal"><p class="ly-k">%s</p><h3>%s</h3>'
                     '<div class="ly-ft"><p class="ly-from"><span class="lab">In the port</span>%s</p><p class="ly-to"><span class="lab">Now</span>%s</p></div>%s'
                     '<p class="ly-n"><span class="ly-proj">%s</span> <b>%s</b> <span class="arr" aria-hidden="true">&rarr;</span> <b>%s</b> lines'
                     '<span class="ly-tot">backend %s &rarr; %s</span></p><ul class="pills sm">%s</ul></li>'
                     % (' wide' if ly.get('dapper') else '', ' added' if ly.get('added') else '', ph(ly['phase']) if ly['phase'] != 'H' else 'Between ' + ph('2') + ' and ' + ph('3'),
                        e(ly['name']), prose(ly['from']), prose(ly['to']), why, projs, L.n(sb), L.n(sa), L.n(tb), L.n(ta), pills))
    return ('<section class="band alt" id="layers" aria-labelledby="layers-h"><div class="wrap">'
            '<p class="kicker reveal">Layer by layer</p><h2 id="layers-h" class="reveal">What the framework replaced.</h2>'
            '<p class="sub reveal">It started as a fork of the F# port and swapped one layer a phase, each behind the pixel gate, the cookie round trip and the database differential. '
            'Hand-written code stays only where Rails parity needs it: the cookie formats, Loofah&#39;s sanitizer rules, Rails&#39; markup quirks.</p>'
            '<ol class="lys">%s</ol><p class="btw reveal">%s</p></div></section>' % (''.join(cards), more('p-layers', 'Lines per project, phase by phase')))


# ---------------------------------------------------------------- proof

def proof(F):
    P = F['pix']
    v = F['verify']
    tiles = [
        ('Full pixel gate', '%s<i>/</i>%s' % (L.n(F['pix_eq']), L.n(F['pix_cells'])),
         'cells equal to Rails on screenshots, the accessibility tree and the loose DOM, at the ' + ph('10') + ' gate: %s seeds, %s browser engines, %s viewports, light and dark, and a sweep of %s breakpoint widths. '
         'One WebKit cell differed at first by %s pixels (a WebP avatar decode flake carried since %s); alone it was equal %s times of %s.'
         % (L.n(F['pix_seeds']), L.n(F['pix_engines']), L.n(F['pix_views']), L.n(F['pix_bps']), L.n(F['flake_px']), ph('4'), L.n(F['rerun_eq']), L.n(F['rerun_n'])), 'p-pixels'),
        ('Over TLS', '%s<i>/</i>%s' % (L.n(F['tls_eq']), L.n(F['tls_n'])), 'cells equal with the app serving HTTPS from its own certificate cache.', 'p-pixels'),
        ('Cookie round trip', '%s<i>/</i>%s' % (L.n(F['cookie']), L.n(F['cookie_n'])),
         'checks: sign in on Rails, switch the same storage to theDotNetWay and post, then back. Nobody is signed out by an upgrade or a rollback.', None),
        ('Database differential', 'Equal', 'Fixtures and a long scenario written by Active Record and by EF Core compare equal; the schemas agree; Rails boots on the database theDotNetWay wrote.', None),
        ('Tests', L.n(v['succeeded']), 'passed of %s in <code>bin/verify</code> on the closing commit, %s failed, %s skipped (Linux-only or reference-image tests, run elsewhere), with the markup lint, the Dapper and EF guards and the zero-compilation check.'
         % (L.n(v['total']), L.n(v['failed']), L.n(v['skipped'])), None),
        ('Security reviews', L.n(F['sec']), 'independent reviews of the risky layers: views, rich text, assets, the front server, the integrations, then the page cache and the close. Findings were fixed, or accepted with a recorded reason, before each phase closed.', 'p-security'),
        ('Assets', '%s' % L.n(F['assets']), 'network checks on the asset routes (fingerprints, encodings, probes for traversal and dotfiles), %s failed.' % L.n(F['assets_f']), None),
        ('Known differences', 'Listed', 'Every difference a person or another system could notice has a README entry, the reference file it departs from and a test that pins it.', 'readme'),
    ]
    out = []
    for h, n, txt, link in tiles:
        tail = ''
        if link == 'readme':
            tail = '<a class="more" href="%s"><span>The list</span> <span aria-hidden="true">&rarr;</span></a>' % e(src_url('README.md') + '#known-differences')
        elif link:
            tail = more(link)
        out.append('<li class="pr reveal"><p class="pr-k">%s</p><p class="pr-n">%s</p><p class="pr-t">%s</p>%s</li>' % (h, n, txt, tail))
    return ('<section class="band" id="proof" aria-labelledby="proof-h"><div class="wrap">'
            '<p class="kicker reveal">Check it</p><h2 id="proof-h" class="reveal">Checked against Rails, cell by cell.</h2>'
            '<ol class="prs">%s</ol></div></section>' % ''.join(out))


def more_row():
    links = [('p-ledger', 'Every number'), ('p-method', 'The method'), ('p-ranges', 'Every cell&#39;s range'), ('p-steps', ph('10') + ', step by step'),
             ('p-climb', 'Every phase'), ('p-layers', 'Lines per project'), ('p-pixels', 'The pixel gate'), ('p-security', 'The security reviews')]
    return ('<section class="more-row" id="more" aria-label="Everything else"><div class="wrap"><p class="kicker">Everything else</p>'
            '<p class="links">%s</p></div></section>' % ''.join(more(p, l) for p, l in links))


# ================================================================ panels

def p_method(F):
    env = []
    for s in F['sessions']:
        hostf = SIX + '/%s.host' % s
        t = text(hostf)
        r = re.findall(r'"ratio": ([\d.]+)', t)
        if len(r) != 2:
            raise SystemExit('build: %s: want a before and an after probe' % hostf)
        env.append([lit(s, 'session'), L.n(V(float(r[0]), 'host probe ratio before session %s (1.00 is the quietest this VM has shown)' % s, [hostf], 'the "before" line\'s ratio'), '{:.3f}'),
                    L.n(V(float(r[1]), 'host probe ratio after session %s' % s, [hostf], 'the "after" line\'s ratio'), '{:.3f}')])
    imgs = []
    meta = F['meta_n']
    for a in APPS:
        sr = meta['source_revisions'][a]
        imgs.append([e(appname(a)), commit(sr['head'][:7]), 'clean' if not sr['dirty'] else 'dirty', commit(meta['images'][a].replace('sha256:', '')[:12])])
    body = ''.join([
        '<p class="lead">Upstream&#39;s harness, run the way onCrack&#39;s round five ran it, with every app of the table in every session. The pooled figure of a cell is the median over all its rounds; the range is in the next panel.</p>',
        '<pre><code>bin/benchmark --workspace /mnt/cfv/ws --rounds 3 --duration 8 --concurrencies 16 --cpus 0-3 --client-cpus 4-7 \\\n'
        '  --apps rails,rust,c,fsharp,oncrack,dotnetway --routes room_show,messages_page,sidebar,search,post_message\n'
        '# the writer group: the same, with --mixed-write-rate 10 and the four reads</code></pre>',
        '<h3>Sessions and the host</h3><p>The probe is a fixed spin on every VM CPU, against the quietest run this VM has shown; a ratio close to one is a quiet host.</p>',
        tbl([('Session', False), ('Probe before', True), ('Probe after', True)], env),
        '<h3>What ran</h3>', tbl([('App', False), ('Commit', False), ('Tree', False), ('Image', False)], imgs, 'From the session&#39;s summary.json'),
        '<p>theDotNetWay runs with its page cache (HybridCache, fresh authorization, %s MB, the default). onCrack runs its fresh-authorization mode; its faster cached decision is not run. '
        'The F# port has no page cache: it is a translation of Rust&#39;s port from before Rust had one. Search orders by message id in every app but theDotNetWay and the port, which keep the pinned Rails&#39; creation-time order.</p>'
        % lit(F['meta_n']['topology']['dotnetway']['response_cache_mb'], 'size'),
        srcline(SIX + '/README.md', SIX + '/drive.sh', SIX + '/aggregate.py', SIX + '/normal-1/summary.json', SIX + '/mixed-1/mixed-summary.json', SIX + '/verification-adapter-dotnetway.diff', 'plans/design-perf.md'),
    ])
    return panel('p-method', 'Campfire, six ways', 'The method', body, 'six')


def p_ranges(F):
    out = ['<p class="lead">Each cell&#39;s pooled median with the smallest and largest round, from <code>table.json</code>, which <code>aggregate.py</code> made from the round files. '
           'The build recomputes every median from the round files and stops if one disagrees.</p>']
    for mixed in (False, True):
        keys = READS if mixed else [k for k, _ in ROUTES]
        rows = []
        for k in keys:
            rows.append([RNAME[k]] + ['%s <span class="rng">%s&ndash;%s</span>' % (L.n(F['six'][a, k, mixed, 'median']), L.n(F['six'][a, k, mixed, 'min']), L.n(F['six'][a, k, mixed, 'max'])) for a in APPS])
        out.append('<h3>%s</h3>' % group_label(F, mixed))
        out.append(tbl([('req/s', False)] + [(e(appname(a)), True) for a in APPS], rows, cls='wide'))
    out.append(srcline(SIX_T, SIX + '/table.md', SIX + '/aggregate.py'))
    return panel('p-ranges', 'Campfire, six ways', 'Every cell, with its range', ''.join(out), 'six')


def p_steps(F):
    rows = []
    for st in F['steps']:
        rows.append(['%s <small>%s</small>' % (e(st['name']), flink(st['dir'], st['dir'].replace('bench/results/', '')))] + [L.x(st['x'][k]) for k, _ in ROUTES])
    pre_rows, now_rows = [], []
    for k, _ in ROUTES:
        pre_rows.append([RNAME[k]] + [L.n(F['pre'][a, k, False]) for a in APPS])
    body = ''.join([
        '<p class="lead">' + ph('10') + ' measured every unit with <code>bench/upstream-quick</code>: Basecamp&#39;s harness over current Rails, the F# port and theDotNetWay in one session, at least two rounds. '
        'Each cell is theDotNetWay over that session&#39;s own Rails.</p>',
        tbl([('Step', False)] + [(RNAME[k], True) for k, _ in ROUTES], rows, cls='wide'),
        '<p>One connection per request took the room page from about two thirds of Rails to twice Rails. Narrowing a miss moved the one read that still built a LINQ expression on every call to Dapper. '
        'The page cache did the rest, on the room and messages pages first, then the sidebar and search. Posting stayed above five times Rails throughout.</p>',
        '<h3>The first six-ways run, before %s</h3>' % ph('10'),
        tbl([('Each route alone, req/s', False)] + [('%s <small>%s</small>' % (e(appname(a, F['pre_apps'])), lit(appcommit(a, F['pre_apps']), 'commit')), True) for a in APPS], pre_rows, cls='wide'),
        srcline(*([st['dir'] + '/summary.json' for st in F['steps']] + [PRE_T, PRE + '/README.md', 'plans/design-perf.md'])),
    ])
    return panel('p-steps', 'About the speed', ph('10') + ', step by step', body, 'speed')


def p_climb(F):
    rows = []
    for r in F['ph']:
        rows.append([ph(r['p']), L.n(r['k'])] + ['%s <small>%s</small>' % (L.n(r['q'][k, 16]), L.x(r['x'][k, 16])) for k, _ in ROUTES] + [L.x(r['thin'])])
    body = ''.join([
        '<p class="lead">Each phase gate&#39;s <code>bench/quick</code> run (theDotNetWay alone, three reps, sixteen clients) against the stored medians of the pinned Rails, and <code>bin/kloc</code>&#39;s corrected total. '
        'The hybrid reads were a unit between %s and %s with their own run. The %s run was on a loaded host.</p>' % (ph('2'), ph('3'), ph('0')),
        tbl([('Phase', False), ('Lines', True)] + [(RNAME[k], True) for k, _ in ROUTES] + [('Thinnest', True)], rows, cls='wide'),
        '<h3>The pinned Rails</h3>',
        tbl([('Rails, stored', False)] + [(RNAME[k], True) for k, _ in ROUTES], [['c=' + L.n(V(c, 'bench/quick concurrency', [STORED + '/reference-1.json'], 'http[].conc'))] + [L.n(F['pin'][k, c]) for k, _ in ROUTES] for c in (16, 1)]),
        srcline(STORED + '.md', PLAN, *[r['qd'] for r in F['ph']]),
    ])
    return panel('p-climb', 'The climb', 'Every phase', body, 'climb')


def p_layers(F):
    dirs = [(p, kd) for p, _, kd in PHASES if kd] + [('now', 'bench/results/dotnetway-landing')]
    projs = []
    for _, kd in dirs:
        for p in kloc_projects(kd + '/kloc.md'):
            if p not in projs:
                projs.append(p)
    rows = []
    for p in projs:
        rows.append([e(p)] + [L.n(kloc(kd + '/kloc.md', p)) if p in kloc_projects(kd + '/kloc.md') else '&middot;' for _, kd in dirs])
    rows.append(['<b>Total</b>'] + [L.n(kloc(kd + '/kloc.md')) for _, kd in dirs])
    head = [('Project', False)] + [(('P' + p) if p != 'now' else 'Now', True) for p, _ in dirs]
    head = [(lit(h, 'phase') if re.search(r'\d', h) else h, n) for h, n in head]
    body = ''.join([
        '<p class="lead"><code>bin/kloc</code> at every phase gate: upstream&#39;s cloc rules, the corrected column (cloc counts <code>(*</code> in an F# string as the start of a comment and drops lines; the corrected count is the higher, honest one).</p>',
        tbl(head, rows, cls='wide'),
        srcline('bin/kloc', KLOC_NOW, *[kd + '/kloc.md' for _, kd in dirs[:-1]]),
    ])
    return panel('p-layers', 'Layer by layer', 'Lines per project', body, 'layers')


def p_pixels(F):
    P = F['pix']
    rows = []
    for seed, s in sorted(P['seeds'].items(), key=lambda kv: -kv[1]['cells']):
        mk = lambda k, lab: L.n(V(s[k], '%s seed, cells %s at the Phase 10 gate, first pass' % (seed, lab), P['files'], 'count over report.json results'))
        rows.append([e(seed), mk('cells', 'with a screenshot'), mk('pixels', 'pixel-equal'), mk('aria', 'aria-equal'), mk('loose', 'loose-DOM-equal')])
    diff = ''.join('<li><code>%s</code> %s, <code>%s</code></li>' % (e(st), e(seed), e(cell)) for seed, st, cell in P['differ'])
    body = ''.join([
        '<p class="lead">The full matrix: every state on Chromium, Firefox and WebKit, desktop, laptop, tablet and phone, light and dark, with the breakpoint sweep, against the Rails reference on the same seed and a frozen clock. '
        'A cell passes when its screenshot is pixel-equal, its accessibility tree is equal and its live DOM is equal after the loose normalization. The server, network and Cable layers are reported, not gated.</p>',
        tbl([('Seed', False), ('Cells', True), ('Screenshots', True), ('aria', True), ('Loose DOM', True)], rows, 'First pass, by seed'),
        '<p>Differed on the first pass:</p><ul>%s</ul><p>Re-run alone: %s of %s equal on all three layers. Over TLS: %s of %s.</p>' % (diff, L.n(F['rerun_eq']), L.n(F['rerun_n']), L.n(F['tls_eq']), L.n(F['tls_n'])),
        srcline(GATE + '/full/', GATE + '/full/rerun/', F['pix']['tls']['file'], 'bin/pixel-parity'),
    ])
    return panel('p-pixels', 'Check it', 'The pixel gate', body, 'proof')


def p_security(F):
    items = ''.join('<li>%s</li>' % e(re.sub(r'^###\s*', '', re.sub(r'\s*\(\d{4}-\d\d-\d\d\)$', '', t))).replace('Phase ', 'Phase&nbsp;') for t in F['sec_titles'])
    items = re.sub(r'(Phase&nbsp;|Phases )(\d+)( and )?(\d+)?', lambda m: m.group(1) + lit(m.group(2), 'phase') + (m.group(3) or '') + (lit(m.group(4), 'phase') if m.group(4) else ''), items)
    body = ''.join([
        '<p class="lead">Each review read the layer&#39;s code and the paths beside it, tried the attacks that layer invites (XSS through the views and rich text, traversal through assets and storage, header and host tricks at the front server, SSRF through the integrations, a stale or cross-session page from the cache), '
        'and fixed what it found before the phase closed. The record of each is its Phase log entry.</p>',
        '<ol class="plain">%s</ol>' % items,
        srcline(PLAN, 'plans/design-perf.md', 'plans/design-close.md', R + '/security-p9p10/'),
    ])
    return panel('p-security', 'Check it', 'The security reviews', body, 'proof')


def p_ledger():
    rows = []
    for r in L.rows:
        files = ' '.join(flink(f, f.replace('bench/results/', '')) for f in r['files'][:8])
        if len(r['files']) > 8:
            files += ' <small>and %s more</small>' % ('<span data-lit="count">%d</span>' % (len(r['files']) - 8))
        rows.append('<li id="%s"><span class="lg-id">%s</span><b class="lg-v">%s</b><span class="lg-l">%s</span><small class="lg-h">%s</small><span class="lg-f">%s</span></li>'
                    % (r['id'], r['id'], e(r['shown']), e(r['label']), e(r['how']), files))
    body = ('<p class="lead">Every number on the page, in order of first use, with the files it was computed from and how. <code>site/build.py</code> computes each one when it builds the page '
            'and refuses to build if anything a reader can see has a digit in it and no row here. %s rows.</p><ol class="ledger">%s</ol>' % ('<span data-lit="count">%d</span>' % len(L.rows), ''.join(rows)))
    return panel('p-ledger', 'Every number', 'The ledger', '<div data-ledger>%s</div>' % body, 'more')


# ================================================================ the check

LIT_SHAPES = {
    'commit': [r'[0-9a-f]{7,12}'],
    'phase': [r'Phase \d+', r'P\d+', r'\d+'],
    'project': [r'Campfire\.\w+'],
    'unit': [r'MB', r'MiB'],
    'size': [r'\d+'],
    'count': [r'\d+'],
    'name': [r'H\d', r'D\d', r'UTF-8', r'HTML5', r'37signals'],
    'session': [r'(?:normal|mixed)-\d'],
}


class Checker(HTMLParser):
    VOID = {'area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'source', 'track', 'wbr'}
    EXEMPT = {'script', 'style', 'code', 'pre', 'title'}

    def __init__(self, ids):
        super().__init__(convert_charrefs=True)
        self.ids, self.stack, self.errors, self.lits, self.spans, self.links = ids, [], [], [], set(), set()

    def ctx(self):
        return any(s[1] for s in self.stack)

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        ok = False
        if 'data-src' in a:
            if a['data-src'] not in self.ids:
                self.errors.append('data-src %s is not a ledger row' % a['data-src'])
            (self.links if tag == 'a' else self.spans).add(a['data-src'])
            ok = True
        if 'data-lit' in a:
            ok = True
            self.lits.append(a['data-lit'])
            if a['data-lit'] not in LIT_SHAPES:
                self.errors.append('data-lit with an unknown reason: %s' % a['data-lit'])
        if 'data-ledger' in a or tag in self.EXEMPT:
            ok = True
        for k in ('aria-label', 'alt', 'title'):
            if a.get(k) and re.search(r'\d', a[k]) and not (ok or self.ctx()):
                self.errors.append('a digit in %s="%s" with no source' % (k, a[k]))
        if tag in self.VOID:
            return
        self.stack.append((tag, ok, a.get('data-lit')))

    def handle_startendtag(self, tag, attrs):
        self.handle_starttag(tag, attrs)
        if tag not in self.VOID:
            self.stack.pop()

    def handle_endtag(self, tag):
        if tag in self.VOID:
            return
        for i in range(len(self.stack) - 1, -1, -1):
            if self.stack[i][0] == tag:
                del self.stack[i:]
                return
        self.errors.append('stray </%s>' % tag)

    def handle_data(self, data):
        if not re.search(r'\d', data):
            return
        litv = next((s[2] for s in reversed(self.stack) if s[2]), None)
        if litv:
            if not any(re.fullmatch(p, data.strip()) for p in LIT_SHAPES.get(litv, [])):
                self.errors.append('data-lit "%s" holds %r, which is not an identifier shape' % (litv, data.strip()))
            return
        if not self.ctx():
            where = '/'.join(s[0] for s in self.stack[-4:])
            self.errors.append('a number with no source: %r (in %s)' % (data.strip()[:80], where))


def check(page):
    c = Checker({r['id'] for r in L.rows})
    c.feed(page)
    c.close()
    for rid in sorted(c.spans - c.links):
        row = next(r for r in L.rows if r['id'] == rid)
        if not row['block']:
            c.errors.append('%s (%s) is shown only as plain text; it must also appear as a link to its row' % (rid, row['label']))
    for r in L.rows:
        for f in r['files']:
            if not os.path.exists(os.path.join(REPO, f.split('#')[0])):
                c.errors.append('source file missing: %s' % f)
    return c


# ================================================================ CSS and JS

CSS = r"""
:root{--bg:#f6f5fb;--bg-2:#eeecf7;--surface:#ffffff;--ink:#16131f;--ink-2:#3d384d;--mute:#6b6680;--line:#e2dfed;--line-2:#d3cfe2;
  --acc:#5b3fd6;--acc-2:#7c5cf5;--acc-soft:#ece7ff;--teal:#0b8f86;--warn:#c2410c;--best:#e7e4f2;--best-ink:#16131f;
  --display:-apple-system,BlinkMacSystemFont,"SF Pro Display","Helvetica Neue",Inter,system-ui,sans-serif;--text:-apple-system,BlinkMacSystemFont,"SF Pro Text","Helvetica Neue",Inter,system-ui,sans-serif;
  --mono:ui-monospace,"SF Mono",Menlo,Consolas,monospace;color-scheme:light}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]){--bg:#0c0a14;--bg-2:#12101d;--surface:#17142a;--ink:#f1eefb;--ink-2:#c9c3dd;--mute:#958fab;--line:#2a2640;--line-2:#383352;
  --acc:#a48dff;--acc-2:#8b6cf0;--acc-soft:#221c40;--teal:#3fd2c7;--warn:#fb923c;--best:#2a2546;--best-ink:#ffffff;color-scheme:dark}}
:root[data-theme="dark"]{--bg:#0c0a14;--bg-2:#12101d;--surface:#17142a;--ink:#f1eefb;--ink-2:#c9c3dd;--mute:#958fab;--line:#2a2640;--line-2:#383352;
  --acc:#a48dff;--acc-2:#8b6cf0;--acc-soft:#221c40;--teal:#3fd2c7;--warn:#fb923c;--best:#2a2546;--best-ink:#ffffff;color-scheme:dark}
*{box-sizing:border-box}
html{-webkit-text-size-adjust:100%;scroll-behavior:smooth;scroll-padding-top:64px}
@media (prefers-reduced-motion:reduce){html{scroll-behavior:auto}}
body{margin:0;background:var(--bg);color:var(--ink);font:400 17px/1.55 var(--text);-webkit-font-smoothing:antialiased;overflow-x:hidden}
html.locked,html.locked body{overflow:hidden}
a{color:var(--acc)}
code{font:0.86em var(--mono);background:var(--bg-2);border:1px solid var(--line);border-radius:5px;padding:.05em .3em;overflow-wrap:anywhere}
pre code{display:block;padding:14px 16px;overflow-x:auto;white-space:pre;border-radius:10px;font-size:12.5px;line-height:1.6}
.wrap{max-width:1180px;margin:0 auto;padding:0 32px}
@media (max-width:720px){.wrap{padding:0 16px}}
[data-lit]{white-space:nowrap}
.sr{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0);white-space:nowrap}
.skip{position:absolute;left:-999px;top:8px;z-index:100;background:var(--surface);padding:8px 12px;border-radius:8px}.skip:focus{left:8px}
.kicker{font:600 13px/1.2 var(--text);letter-spacing:.12em;text-transform:uppercase;color:var(--acc);margin:0 0 14px}
h1,h2,h3{font-family:var(--display);letter-spacing:-.025em;color:var(--ink)}
h2{font-size:clamp(34px,5vw,60px);line-height:1.02;font-weight:700;margin:0 0 18px}
.sub{font-size:clamp(17px,1.6vw,20px);color:var(--ink-2);max-width:820px;margin:0 0 34px}
a.sn{color:inherit;text-decoration:underline;text-decoration-color:color-mix(in srgb,var(--acc) 45%,transparent);text-decoration-thickness:1.5px;text-underline-offset:3px;font-variant-numeric:tabular-nums}
a.sn:hover{text-decoration-color:var(--acc)}
.more{display:inline-flex;align-items:center;gap:0;white-space:nowrap;font-weight:600;font-size:15px;color:var(--acc);text-decoration:none;padding:9px 15px;border:1px solid var(--line-2);border-radius:999px;background:var(--surface);margin:4px 8px 4px 0;transition:border-color .2s,transform .2s}
.more>span+span{margin-left:6px}.more:hover{border-color:var(--acc);transform:translateY(-1px)}
.btw{margin:22px 0 0}
.src{font-size:13px;color:var(--mute);margin-top:18px}.src a{color:var(--mute)}
/* bar */
.bar{position:sticky;top:0;z-index:20;background:color-mix(in srgb,var(--bg) 80%,transparent);backdrop-filter:saturate(1.6) blur(18px);-webkit-backdrop-filter:saturate(1.6) blur(18px);border-bottom:1px solid var(--line)}
.bar .wrap{display:flex;align-items:center;gap:18px;height:56px}
.brand{font:700 18px/1 var(--display);letter-spacing:-.02em;color:var(--ink);text-decoration:none;white-space:nowrap}.brand span{color:var(--acc)}
.bar nav{margin-left:auto;overflow-x:auto;scrollbar-width:none}.bar nav::-webkit-scrollbar{display:none}
.bar ol{display:flex;gap:4px;list-style:none;margin:0;padding:0}
.bar nav a{display:block;padding:7px 11px;border-radius:999px;color:var(--ink-2);text-decoration:none;font-size:14px;white-space:nowrap}.bar nav a:hover{background:var(--bg-2);color:var(--ink)}
.theme{flex:none;width:36px;height:36px;border-radius:50%;border:1px solid var(--line-2);background:var(--surface);color:var(--ink);cursor:pointer;font-size:15px}
html:not(.js) .theme{display:none}
@media (max-width:860px){.bar nav{display:none}.theme{margin-left:auto}}
/* hero */
.hero{position:relative;overflow:hidden;padding:84px 0 70px}
.aura{position:absolute;inset:-20% -10% auto -10%;height:120%;pointer-events:none;
  background:radial-gradient(40% 50% at 18% 18%,color-mix(in srgb,var(--acc) 26%,transparent),transparent 70%),radial-gradient(36% 46% at 86% 12%,color-mix(in srgb,var(--teal) 18%,transparent),transparent 70%),radial-gradient(50% 40% at 60% 90%,color-mix(in srgb,var(--acc-2) 12%,transparent),transparent 70%);filter:blur(10px)}
.hero-grid{position:relative;display:grid;grid-template-columns:minmax(0,1.3fr) minmax(0,.85fr);gap:56px;align-items:end}
.hero h1{font-size:clamp(48px,6.6vw,92px);line-height:.94;font-weight:800;letter-spacing:-.045em;margin:0 0 26px;
  background:linear-gradient(100deg,var(--ink) 0%,var(--ink) 52%,var(--acc) 100%);-webkit-background-clip:text;background-clip:text;color:transparent}
.lede{font-size:clamp(18px,1.9vw,22px);line-height:1.5;color:var(--ink-2);margin:0 0 26px;max-width:640px}
.pills{display:flex;flex-wrap:wrap;gap:8px;list-style:none;margin:0;padding:0}
.pills li{font:600 13px/1 var(--text);padding:8px 12px;border-radius:999px;background:var(--surface);border:1px solid var(--line-2);color:var(--ink-2)}
.pills.sm li{font-size:12px;padding:6px 10px}
.stack ol{list-style:none;margin:0;padding:0;display:grid;gap:10px;perspective:900px}
.stack li{display:flex;justify-content:space-between;gap:16px;align-items:baseline;padding:15px 18px;border-radius:14px;background:color-mix(in srgb,var(--surface) 86%,transparent);
  border:1px solid var(--line-2);box-shadow:0 1px 0 rgba(255,255,255,.4) inset,0 14px 30px -18px color-mix(in srgb,var(--acc) 50%,transparent);
  transform:rotateX(14deg) translateZ(calc(var(--i) * -4px));transform-origin:50% 100%;backdrop-filter:blur(6px)}
.stack li b{font:650 16px/1.2 var(--display);letter-spacing:-.01em}
.stack li span{font-size:13px;color:var(--mute);text-align:right}
.stack li:nth-child(1){border-color:color-mix(in srgb,var(--acc) 55%,var(--line-2))}
.stack li:last-child{background:var(--acc-soft)}
.tiles{list-style:none;padding:0;margin:56px 0 0;display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:16px}
.tile{background:var(--surface);border:1px solid var(--line);border-radius:22px;padding:26px 26px 24px;box-shadow:0 30px 60px -40px rgba(40,20,90,.35)}
.tile-n{margin:0 0 10px;font-family:var(--display);font-weight:750;letter-spacing:-.04em;line-height:1}
.tile-n .big{font-size:clamp(36px,3.7vw,52px);font-variant-numeric:tabular-nums;white-space:nowrap}.tile-n i{font-style:normal;color:var(--mute);font-weight:400;margin:0 .04em}
.tile-n .unit{font-size:22px;color:var(--mute);margin-left:8px;font-weight:600;letter-spacing:0}
.tile-n a.sn{text-decoration-thickness:2px;text-underline-offset:6px}
.tile-l{margin:0;color:var(--ink-2);font-size:15.5px}
.honest{margin:26px 0 0;max-width:900px;color:var(--ink-2)}.honest b{color:var(--ink)}
@media (max-width:980px){.hero-grid{grid-template-columns:1fr;gap:36px}.tiles{grid-template-columns:1fr}.stack li{transform:none}}
@media (max-width:720px){.hero{padding:44px 0 48px}}
/* bands */
.band{padding:110px 0;border-top:1px solid var(--line)}
.band.alt{background:var(--bg-2)}
@media (max-width:720px){.band{padding:64px 0}}
/* six ways */
.sw-fig{margin:0}
.sw-scroll{overflow-x:auto;-webkit-overflow-scrolling:touch;border-radius:18px;background:var(--surface);border:1px solid var(--line);padding:22px 24px 10px}
.sw{border-collapse:separate;border-spacing:0;width:100%;min-width:900px;font-variant-numeric:tabular-nums}
.sw th,.sw td{padding:15px 12px;border-bottom:1px solid var(--line);text-align:right;font-size:16px;white-space:nowrap}
.sw thead th{border-bottom:1.5px solid var(--ink);vertical-align:bottom;padding-top:8px}
.sw .c-wl{text-align:left;font-weight:500;width:170px}.sw a.sn{text-decoration-color:transparent}.sw a.sn:hover{text-decoration-color:var(--acc)}.sw .c-wl small{font-size:11.5px;color:var(--mute);font-weight:400;margin-left:6px}
.sw .c-grp{text-align:left;font-weight:500;font-size:13px;color:var(--mute);white-space:normal;width:120px;border-bottom:1.5px solid var(--ink);vertical-align:middle}
.sw thead .c-grp{border-bottom:1.5px solid var(--ink)}
.sw tbody tr:last-child td,.sw tbody tr:last-child th.c-wl{border-bottom:1.5px solid var(--ink)}
.sw .c-app{text-align:center}.sw .an{display:block;font-weight:600;font-size:16px}.sw .ac{display:block;font:11.5px var(--mono);color:var(--mute);margin-top:3px}
.sw td{text-align:center}
.sw td.best{background:var(--best);font-weight:750;color:var(--best-ink)}
.sw .feat{box-shadow:inset 2px 0 0 var(--acc),inset -2px 0 0 var(--acc)}
.sw thead .feat{box-shadow:inset 2px 0 0 var(--acc),inset -2px 0 0 var(--acc),inset 0 2px 0 var(--acc);border-radius:10px 10px 0 0;color:var(--acc)}
.sw tr.last td.feat{box-shadow:inset 2px 0 0 var(--acc),inset -2px 0 0 var(--acc),inset 0 -2px 0 var(--acc)}
.sw td.feat{background:color-mix(in srgb,var(--acc-soft) 60%,transparent);font-weight:650}
.sw td.feat .xr{display:block;font-size:11.5px;font-weight:600;color:var(--acc);margin-top:2px}
.sw .grp-row{display:none}
.sw .grp-row th{text-align:left;font-size:12.5px;font-weight:600;color:var(--mute);letter-spacing:.06em;text-transform:uppercase;padding:18px 12px 8px;border-bottom:1px solid var(--line)}
@media (max-width:720px){
  .sw-scroll{margin:0 -16px;border-radius:0;border-left:0;border-right:0;padding:12px 0 6px}
  .sw{min-width:740px}.sw .c-wl{width:118px;white-space:normal}.sw .c-wl small{display:none}
  .bbars{padding:18px 16px}.sw td.c-grp,.sw th.c-grp{display:none}.sw .grp-row{display:table-row}
  .sw .c-wl{position:sticky;left:0;background:var(--surface);z-index:1;box-shadow:6px 0 10px -8px rgba(0,0,0,.25)}
  .sw .grp-row th{position:sticky;left:0}
  .sw th,.sw td{padding:12px 9px;font-size:14.5px}
}
.sw-fig figcaption{margin-top:26px;display:grid;grid-template-columns:minmax(0,1.1fr) minmax(0,1fr);gap:28px;font-size:15px;color:var(--ink-2)}
.sw-fig figcaption p{margin:0}
.apps{list-style:none;margin:0;padding:0;display:grid;gap:9px}.apps li{font-size:14px;line-height:1.45}.apps b{color:var(--ink)}
.sw-fig .btw{grid-column:1/-1}
@media (max-width:900px){.sw-fig figcaption{grid-template-columns:1fr}}
/* speed */
.speed-grid{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:56px;align-items:start}
.speed-copy p{color:var(--ink-2);font-size:17px}.speed-copy p b{color:var(--ink)}
@media (max-width:980px){.speed-grid{grid-template-columns:1fr;gap:24px}}
.bbars{margin:0;background:var(--surface);border:1px solid var(--line);border-radius:22px;padding:24px;position:sticky;top:80px}
@media (max-width:980px){.bbars{position:static}}
.bbars ol{list-style:none;margin:16px 0 0;padding:0;position:relative}
.bb-key{display:flex;flex-wrap:wrap;gap:8px 16px;font-size:12.5px;color:var(--mute)}.bb-key i{display:inline-block;width:14px;height:8px;border-radius:3px;margin-right:6px;vertical-align:middle}
.k-pre{background:var(--line-2)}.k-now{background:var(--acc)}.k-floor{border-top:2px dashed var(--warn);height:0!important}.k-one{border-top:2px solid var(--mute);height:0!important}
.bb-g{font:600 12px/1 var(--text);letter-spacing:.08em;text-transform:uppercase;color:var(--mute);margin:18px 0 8px}
.bb{display:grid;grid-template-columns:minmax(84px,118px) minmax(0,1fr);gap:10px;align-items:center;margin:0 0 9px}
.bb-l{font-size:14px;color:var(--ink-2)}
.bb-t{position:relative;display:grid;gap:3px;padding:2px 0;width:calc(100% - 58px)}
.bb-t::before{content:"";position:absolute;top:-4px;bottom:-4px;left:var(--floor);border-left:2px dashed var(--warn);opacity:.75;z-index:1}
.bb-t::after{content:"";position:absolute;top:-4px;bottom:-4px;left:var(--one);border-left:2px solid var(--mute);opacity:.55;z-index:1}
.bb-b{display:block;height:16px;border-radius:0 6px 6px 0;width:var(--w);position:relative;min-width:3px}
.bb-b.pre{background:var(--line-2)}.bb-b.now{background:linear-gradient(90deg,var(--acc-2),var(--acc))}
.bb-v{position:absolute;left:calc(100% + 6px);top:50%;transform:translateY(-50%);font-size:12.5px;font-weight:600;white-space:nowrap;color:var(--ink-2)}
.bb-b.now .bb-v{color:var(--acc)}
.bbars figcaption{margin-top:16px;font-size:13px;color:var(--mute)}.bbars figcaption code{font-size:11px}
.js.anim .bb-b{transform-origin:0 50%;transform:scaleX(0);transition:transform 1.1s cubic-bezier(.2,.7,.1,1)}
.js.anim .bbars.in .bb-b{transform:none}.js.anim .bbars.in .bb-b.now{transition-delay:.25s}
/* climb */
.climb{margin:0}
.cl-scroll{position:relative}
.cl-chart{position:relative;aspect-ratio:1000/560;background:var(--surface);border:1px solid var(--line);border-radius:22px}
.cl-chart svg{position:absolute;inset:0;width:100%;height:100%;overflow:visible}
.cl-grid{stroke:var(--line);stroke-width:1}.cl-tick{fill:var(--mute);font-size:12px}.cl-x{fill:var(--ink-2);font-size:13px;font-weight:600}
.cl-ph{fill:var(--mute);font-size:12px;font-weight:600;letter-spacing:.08em;text-transform:uppercase}
.cl-rust{stroke:var(--ink);stroke-dasharray:7 6;stroke-width:1.5;opacity:.6}.cl-rails{stroke:var(--warn);stroke-dasharray:2 5;stroke-width:2}
.cl-ref{fill:var(--ink-2);font-size:12px;font-weight:650}.cl-floor{stroke:var(--warn);stroke-width:1.6;stroke-dasharray:6 5}.cl-floor-t{fill:var(--warn)}
.cl-bar{fill:color-mix(in srgb,var(--acc) 22%,var(--surface))}.cl-bar.cl-last{fill:var(--acc)}
.cl-bv{fill:var(--ink-2);font-size:11.5px;font-weight:600}
.cl-line{fill:none;stroke:var(--acc);stroke-width:3.5;stroke-linejoin:round;stroke-linecap:round}.cl-area{fill:var(--acc);opacity:.08}
.cl-pt{fill:var(--surface);stroke:var(--acc);stroke-width:3}.cl-up{stroke:var(--teal);stroke-width:3.5;stroke-dasharray:1 7;stroke-linecap:round;fill:none}
.cl-upt{fill:var(--teal);stroke:var(--surface);stroke-width:2.5}
.cl-lab{font-size:12.5px;font-weight:650}.cl-lab-s{font-size:10.5px;font-weight:500;fill:var(--mute)}.cl-lab-pin{fill:var(--acc)}.cl-lab-up{fill:var(--teal)}
.cl-col{position:absolute;top:0;bottom:0;outline:none;cursor:pointer}
.cl-hit{position:absolute;inset:6px 2px;border-radius:12px;transition:background .2s}
.cl-col:hover .cl-hit,.cl-col:focus .cl-hit,.cl-col:focus-within .cl-hit{background:color-mix(in srgb,var(--acc) 8%,transparent)}
.cl-col:focus-visible .cl-hit{box-shadow:0 0 0 2px var(--acc)}
.cd{display:none;position:absolute;left:60%;top:24%;z-index:6;width:400px;max-width:86vw;background:var(--surface);color:var(--ink);border:1px solid var(--line-2);border-radius:16px;
  padding:14px 16px;box-shadow:0 24px 60px -20px rgba(20,10,60,.45);font-size:13.5px;line-height:1.45;cursor:default;text-align:left}
.cl-col.flip .cd{left:auto;right:60%}
.cl-col:hover .cd,.cl-col:focus .cd,.cl-col:focus-within .cd{display:block}
.cd p{margin:0 0 6px}.cd-k{font-size:11.5px;font-weight:650;letter-spacing:.08em;text-transform:uppercase;color:var(--acc)}.cd-h{font:700 17px/1.2 var(--display)}.cd-w{color:var(--ink-2)}
.cd-kloc{font-size:13px}.cd table{width:100%;border-collapse:collapse;margin:6px 0;font-variant-numeric:tabular-nums}
.cd th,.cd td{padding:4px 2px;border-top:1px solid var(--line);text-align:right;font-size:12.5px}.cd th:first-child{text-align:left;font-weight:500}.cd thead th{font-weight:600;color:var(--mute);font-size:11px;border-top:0}
.cd-up{color:var(--teal)}.cd-up b{color:var(--ink)}.cd-src{font-size:11px;color:var(--mute)}.cd-src code{font-size:10.5px}
.climb figcaption{margin-top:18px;display:flex;gap:24px;align-items:flex-start;justify-content:space-between;color:var(--ink-2);font-size:15px}
@media (max-width:720px){
  .cl-scroll{overflow-x:auto;margin:0 -16px;padding:0 16px 8px;overscroll-behavior-x:contain}.cl-chart{min-width:820px}
  .cd,.cl-col.flip .cd{position:fixed;left:8px;right:8px;top:auto;bottom:8px;width:auto;max-width:none;max-height:70vh;overflow:auto;z-index:60}
  .climb figcaption{flex-direction:column}
}
.js.anim .cl-line{stroke-dasharray:var(--len);stroke-dashoffset:var(--len);transition:stroke-dashoffset 1.8s cubic-bezier(.6,0,.2,1) .2s}
.js.anim .climb.in .cl-line{stroke-dashoffset:0}
/* layers */
.lys{list-style:none;margin:0;padding:0;display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}
.ly{background:var(--surface);border:1px solid var(--line);border-radius:22px;padding:24px 26px;display:flex;flex-direction:column;gap:12px}
.ly.wide{grid-column:1/-1;background:linear-gradient(135deg,var(--surface),color-mix(in srgb,var(--acc-soft) 70%,var(--surface)));border-color:color-mix(in srgb,var(--acc) 35%,var(--line))}
.ly.added{border-style:dashed}
.ly-k{margin:0;font-size:12px;font-weight:650;letter-spacing:.08em;text-transform:uppercase;color:var(--acc)}
.ly h3{margin:0;font-size:26px;line-height:1.1}
.ly-ft{display:grid;grid-template-columns:1fr 1fr;gap:14px}
.ly-ft p{margin:0;font-size:14.5px;color:var(--ink-2)}.ly-ft .lab{display:block;font-size:11.5px;font-weight:650;letter-spacing:.08em;text-transform:uppercase;color:var(--mute);margin-bottom:4px}
.ly-to{border-left:2px solid var(--acc);padding-left:12px}
.ly-why{margin:0;font-size:15px;color:var(--ink-2);max-width:900px}.ly-why b{color:var(--ink)}
.ly-n{margin:auto 0 0;font-size:14px;color:var(--mute);display:flex;flex-wrap:wrap;gap:4px 8px;align-items:baseline}.ly-n b{font-size:20px;color:var(--ink);font-variant-numeric:tabular-nums}
.ly-proj{font-size:12.5px}.ly-tot{flex-basis:100%;font-size:12.5px}
@media (max-width:860px){.lys{grid-template-columns:1fr}.ly-ft{grid-template-columns:1fr}}
/* proof */
.prs{list-style:none;margin:0;padding:0;display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:14px}
.pr{background:var(--surface);border:1px solid var(--line);border-radius:20px;padding:20px 20px 18px;display:flex;flex-direction:column}
.pr:last-child{grid-column:1/-1;flex-direction:row;flex-wrap:wrap;align-items:baseline;gap:0 18px}.pr:last-child .pr-t{flex:1 1 360px}.pr:first-child{grid-column:span 2;background:linear-gradient(160deg,var(--surface),var(--acc-soft))}
.pr-k{margin:0 0 8px;font-size:12px;font-weight:650;letter-spacing:.08em;text-transform:uppercase;color:var(--mute)}
.pr-n{margin:0 0 8px;font:750 clamp(30px,3.2vw,44px)/1 var(--display);letter-spacing:-.035em;font-variant-numeric:tabular-nums}.pr-n i{font-style:normal;color:var(--mute);font-weight:400}
.pr-t{margin:0 0 10px;font-size:14px;color:var(--ink-2);flex:1}
.pr .more{align-self:flex-start;font-size:13.5px;padding:7px 12px}
@media (max-width:1000px){.prs{grid-template-columns:repeat(2,minmax(0,1fr))}}
@media (max-width:560px){.prs{grid-template-columns:1fr}.pr:first-child{grid-column:auto}}
.more-row{padding:56px 0;border-top:1px solid var(--line)}.more-row .links{margin:0}
footer.site{padding:40px 0 64px;border-top:1px solid var(--line);color:var(--mute);font-size:13.5px}footer.site p{max-width:900px}
/* reveal: only when the page is visible and motion is welcome; without JS everything is simply there */
@media (prefers-reduced-motion:no-preference){
  .js.anim .reveal,.js.anim .rise{opacity:0;transform:translateY(18px);transition:opacity .8s ease,transform .9s cubic-bezier(.2,.7,.1,1)}
  .js.anim .reveal.in,.js.anim .rise.in{opacity:1;transform:none}
  .js.anim .rise:nth-child(2){transition-delay:.06s}.js.anim .rise:nth-child(3){transition-delay:.12s}.js.anim .rise:nth-child(4){transition-delay:.18s}
  .stack li{animation:float 7s ease-in-out infinite;animation-delay:calc(var(--i) * -.6s)}
  @keyframes float{50%{translate:0 -4px}}
}
@media (prefers-reduced-motion:reduce){.js.anim .bb-b{transform:none!important}.js.anim .cl-line{stroke-dasharray:none}}
/* panels */
.panel{position:fixed;inset:0;z-index:70;visibility:hidden;pointer-events:none}
html:not(.js) .panel:target,html:not(.js) .panel:has(:target),.panel.open{visibility:visible;pointer-events:auto}
.scrim{position:absolute;inset:0;background:rgba(8,5,20,.45);opacity:0;cursor:default}.js.anim .scrim{transition:opacity .3s}
html:not(.js) .panel:target .scrim,html:not(.js) .panel:has(:target) .scrim,.panel.open .scrim{opacity:1}
.sheet{position:absolute;top:0;right:0;bottom:0;width:min(860px,100%);background:var(--bg);color:var(--ink);box-shadow:-24px 0 60px rgba(0,0,0,.28);overflow-y:auto;overscroll-behavior:contain;
  transform:translateX(102%);outline:none}
.js.anim .sheet{transition:transform .42s cubic-bezier(.2,.7,.1,1)}
html:not(.js) .panel:target .sheet,html:not(.js) .panel:has(:target) .sheet,.panel.open .sheet{transform:none}
@media (prefers-reduced-motion:reduce){.sheet,.scrim{transition:none}}
.sheet-head{position:sticky;top:0;z-index:2;display:flex;gap:16px;align-items:flex-start;justify-content:space-between;padding:22px 28px 16px;background:color-mix(in srgb,var(--bg) 97%,transparent);backdrop-filter:blur(14px);-webkit-backdrop-filter:blur(14px);border-bottom:1px solid var(--line)}
.sheet-head h2{font-size:clamp(28px,4vw,40px);margin:0}.sheet-head .kicker{margin-bottom:8px}
.x{flex:none;display:grid;place-items:center;width:38px;height:38px;border-radius:50%;border:1px solid var(--line-2);background:var(--surface);color:var(--ink);text-decoration:none;font-size:22px}
.sheet-body{padding:8px 28px 48px;font-size:15.5px}.sheet-body h3{font-size:22px;margin:32px 0 10px}.sheet-body .lead{font-size:17px;color:var(--ink-2)}
.back{margin-top:30px}
@media (max-width:720px){.sheet{box-shadow:none}.sheet-head{padding:16px 16px 12px}.sheet-body{padding:4px 16px 40px}}
.tw{overflow-x:auto;margin:14px 0;border:1px solid var(--line);border-radius:12px;background:var(--surface)}
.tw table{border-collapse:collapse;width:100%;font-variant-numeric:tabular-nums;font-size:14px}
.tw caption{text-align:left;padding:10px 12px 0;color:var(--mute);font-size:12.5px}
.tw th,.tw td{padding:9px 12px;border-bottom:1px solid var(--line);text-align:left;vertical-align:top}
.tw tr:last-child th,.tw tr:last-child td{border-bottom:0}
.tw thead th{font-size:12px;color:var(--mute);font-weight:600}
.tw .num{text-align:right;white-space:nowrap}.tw small{display:block;color:var(--mute);font-size:11.5px}.tw .rng{display:block;color:var(--mute);font-size:11.5px}
.tw.wide table{min-width:640px}
.plain{padding-left:20px}.plain li{margin:4px 0}
.ledger{list-style:none;padding:0;margin:14px 0}
.ledger li{display:grid;grid-template-columns:52px 1fr;gap:2px 12px;padding:10px 12px;border:1px solid var(--line);border-radius:10px;margin:0 0 6px;background:var(--surface)}
.ledger li:target,.ledger li.flash{border-color:var(--acc);background:var(--acc-soft)}
.lg-id{grid-row:span 4;font:11px var(--mono);color:var(--mute);padding-top:3px}.lg-v{font-size:16px}.lg-l{font-size:14px}.lg-h{color:var(--mute);font-size:12.5px;overflow-wrap:anywhere}
.lg-f{font-size:12px;overflow-wrap:anywhere}.lg-f a{margin-right:6px;color:var(--mute)}.lg-f code{font-size:11px}
@media print{.bar,.theme,.panel{display:none}.js.anim .reveal,.js.anim .rise{opacity:1;transform:none}}
"""

JS = r"""
(function(){
  var root=document.documentElement;
  var btn=document.querySelector('.theme');
  function current(){var t=root.getAttribute('data-theme');if(t)return t;return matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light';}
  function label(){if(btn){var c=current();btn.textContent=c==='dark'?'☀':'☾';btn.setAttribute('aria-label','Switch to '+(c==='dark'?'light':'dark')+' theme');}}
  if(btn){btn.addEventListener('click',function(){var next=current()==='dark'?'light':'dark';root.setAttribute('data-theme',next);try{localStorage.setItem('theme',next);}catch(e){}label();});label();}
  var reduce=matchMedia('(prefers-reduced-motion: reduce)').matches;
  // the climb line's length, for the draw-in
  var pl=document.querySelector('.cl-line');if(pl&&pl.getTotalLength){try{pl.style.setProperty('--len',pl.getTotalLength().toFixed(0));}catch(e){}}
  // animate only on a visible page with motion welcome; otherwise everything is already in its final state
  var els=[].slice.call(document.querySelectorAll('.reveal,.rise,.bbars,.climb'));
  if(!reduce&&document.visibilityState==='visible'&&'IntersectionObserver' in window){
    root.classList.add('anim');
    var io=new IntersectionObserver(function(es){es.forEach(function(en){if(en.isIntersecting){en.target.classList.add('in');io.unobserve(en.target);}});},{rootMargin:'0px 0px -8% 0px',threshold:0.06});
    els.forEach(function(el){io.observe(el);});
    setTimeout(function(){els.forEach(function(el){el.classList.add('in');});},4000);
  }
  // on a phone the chart scrolls sideways: start at the latest phase
  var sc=document.querySelector('.cl-scroll');if(sc&&sc.scrollWidth>sc.clientWidth)sc.scrollLeft=sc.scrollWidth;
  // side panels: :target drawers without JS; with it, opened by class so Esc, click-outside and focus all work
  var mainEl=document.getElementById('main'),barEl=document.querySelector('.bar'),footEl=document.querySelector('footer.site');
  var openP=null,opener=null;
  function panelOf(el){return el&&el.closest?el.closest('.panel'):null;}
  function setInert(on){[mainEl,barEl,footEl].forEach(function(x){if(x){if(on)x.setAttribute('inert','');else x.removeAttribute('inert');}});}
  function reveal(p,t){var s=p.querySelector('.sheet');if(!t||t===p){s.scrollTop=0;return;}
    setTimeout(function(){var r=t.getBoundingClientRect(),sr=s.getBoundingClientRect();s.scrollTop+=r.top-sr.top-(t.tagName==='LI'?sr.height/3:120);
      if(t.tagName==='LI'){t.classList.remove('flash');void t.offsetWidth;t.classList.add('flash');}},60);}
  function openPanel(p,from,push,t,hash){
    if(openP===p){if(push)history.pushState({panel:p.id},'','#'+(hash||p.id));reveal(p,t);return;}
    if(openP)closePanel(true,true);
    openP=p;opener=from||null;p.classList.add('open');root.classList.add('locked');setInert(true);
    if(push)history.pushState({panel:p.id},'','#'+(hash||p.id));
    reveal(p,t);setTimeout(function(){p.querySelector('.sheet').focus({preventScroll:true});},40);
  }
  function closePanel(silent,keepFocus){
    if(!openP)return;var p=openP;openP=null;p.classList.remove('open');root.classList.remove('locked');setInert(false);
    if(!silent){if(history.state&&history.state.panel===p.id){history.back();}else if(location.hash){history.replaceState(null,'',location.pathname+location.search);}}
    if(!keepFocus&&opener){opener.focus({preventScroll:true});}
  }
  document.addEventListener('click',function(ev){
    var a=ev.target.closest&&ev.target.closest('a');if(!a)return;
    if(a.hasAttribute('data-close')){ev.preventDefault();closePanel(false);return;}
    var h=a.getAttribute('href')||'';
    if(h.charAt(0)==='#'&&h.length>1){var t=document.getElementById(h.slice(1)),p=panelOf(t);
      if(p){ev.preventDefault();openPanel(p,openP?opener:a,true,t,h.slice(1));}
      else if(openP&&t){ev.preventDefault();closePanel(false,true);setTimeout(function(){t.scrollIntoView({behavior:reduce?'auto':'smooth'});},60);}}
  });
  document.addEventListener('keydown',function(ev){
    if(!openP)return;
    if(ev.key==='Escape'){ev.preventDefault();closePanel(false);return;}
    if(ev.key==='Tab'){var f=[].filter.call(openP.querySelectorAll('a[href],button,[tabindex]:not([tabindex="-1"])'),function(x){return x.offsetParent!==null;});
      if(!f.length)return;var first=f[0],last=f[f.length-1];
      if(ev.shiftKey&&(document.activeElement===first||document.activeElement===openP.querySelector('.sheet'))){ev.preventDefault();last.focus();}
      else if(!ev.shiftKey&&document.activeElement===last){ev.preventDefault();first.focus();}}
  });
  window.addEventListener('popstate',function(){var id=location.hash.slice(1),t=id&&document.getElementById(id),p=panelOf(t);
    if(p)openPanel(p,null,false,t);else if(openP)closePanel(true);});
  var h0=location.hash.slice(1),t0=h0&&document.getElementById(h0),p0=panelOf(t0);if(p0){window.scrollTo(0,0);openPanel(p0,null,false,t0);}
})();
"""


# ================================================================ build

def build():
    F = facts()
    FX['F'] = F
    P = F['pix']
    files = P['files']
    F['pix_cells'] = V(sum(s['cells'] for s in P['seeds'].values()), 'cells with a screenshot in the Phase 10 gate\'s full pixel matrix, every seed', files, 'count of results with a pixels layer, outside rerun/')
    F['pix_eq'] = V(sum(s['pixels'] for s in P['seeds'].values()) + P['rerun_equal'] // max(P['rerun_cells'], 1) * len(P['differ']),
                    'cells pixel-equal to Rails in the Phase 10 gate\'s full matrix, after the one differing cell was re-run alone', files,
                    'first-pass pixel-equal cells (%d) + the differing cells whose every re-run was equal on all layers (%d)' % (sum(s['pixels'] for s in P['seeds'].values()), len(P['differ']) if P['rerun_equal'] == P['rerun_cells'] else 0))
    if P['rerun_equal'] != P['rerun_cells'] or len(P['differ']) != 1 or any(s['aria'] != s['cells'] or s['loose'] != s['cells'] for s in P['seeds'].values()):
        raise SystemExit('build: the pixel gate is not what the copy says (one cell differed at first, equal on every re-run, aria and loose equal everywhere)')
    F['pix_seeds'] = V(len(P['seeds']), 'seeds in the full pixel matrix', files, 'distinct seed folders')
    F['pix_engines'] = V(len(P['engines']), 'browser engines in the full pixel matrix (%s)' % ', '.join(sorted(P['engines'])), files, 'distinct engines in the cell names')
    F['pix_bps'] = V(len(P['bps']), 'breakpoint widths in the sweep (%s)' % ', '.join(sorted(P['bps'])), files, 'distinct bp* viewports in the cell names')
    F['pix_views'] = V(len(P['viewports']), 'viewports in the full pixel matrix (%s)' % ', '.join(sorted(P['viewports'])), files, 'distinct viewports in the cell names')
    F['rerun_eq'] = V(P['rerun_equal'], 're-runs of the differing cell that were equal on all three layers', [f for f in files if '/rerun/' in f], 'count')
    F['rerun_n'] = V(P['rerun_cells'], 're-runs of the differing cell', [f for f in files if '/rerun/' in f], 'count')
    seed, st, cell = P['differ'][0]
    rel = GATE + '/full/search/default/report.json'
    px = next(l for r in jload(rel)['results'] if r['state'] == st and r['cell'] == cell for l in r['layers'] if l['layer'] == 'pixels')['pixels']
    F['flake_px'] = V(px['differentPixels'], 'pixels that differed in %s %s on the first pass' % (st, cell), [rel], 'the pixels layer of that result')
    F['tls_eq'] = V(P['tls']['equal'], 'TLS cells equal on all three layers at the Phase 10 gate', [P['tls']['file']], 'count')
    F['tls_n'] = V(P['tls']['cells'], 'TLS cells at the Phase 10 gate', [P['tls']['file']], 'count')
    claims_check(F)
    nav = ''.join('<li><a href="%s">%s</a></li>' % h for h in
                  [('#six', 'Six ways'), ('#speed', 'The speed'), ('#climb', 'The climb'), ('#layers', 'Layers'), ('#proof', 'Check it'), ('#p-ledger', 'Every number')])
    main = ''.join([hero(F), six_ways(F), speed(F), climb(F), layers(F), proof(F), more_row()])
    panels = [p_method(F), p_ranges(F), p_steps(F), p_climb(F), p_layers(F), p_pixels(F), p_security(F)]
    panels.append(p_ledger())
    body = ''.join([
        '<a class="skip" href="#main">Skip to content</a>',
        '<header class="bar"><div class="wrap"><a class="brand" href="#top">the<span>DotNet</span>Way</a>'
        '<nav aria-label="Sections"><ol>%s</ol></nav><button class="theme" type="button" aria-label="Switch theme"></button></div></header>' % nav,
        '<main id="main">', main, '</main>',
        '<footer class="site"><div class="wrap"><p><b>Sources.</b> %s (the plan and the Phase log: every decision, gate and measured number), %s, %s, %s, %s.</p>'
        '<p>theDotNetWay is a fork of <a href="%s">the F# port</a> of <a href="%s">Basecamp&#39;s ONCE Campfire</a>; <a href="%s">onCrack</a> is its sibling. Campfire, the Rust port, the verification harness and the vectors are &copy; '
        '<span data-lit="name">37signals</span>, LLC, MIT. The table&#39;s shape is <a href="%s">Ryan Rodemoyer&#39;s &ldquo;Campfire, seven ways&rdquo;</a>; none of its numbers are used. '
        'Generated by <code>site/build.py</code>, which refuses to show a number it cannot source.</p></div></footer>'
        % (flink(PLAN), flink('README.md'), flink('AGENTS.md'), flink(SIX + '/'), flink(GATE + '/'), e(META['port_url']), e(META['upstream_url']), e(META['oncrack_url']), e(META['ryan_url'])),
        '<div class="panels">%s</div>' % ''.join(panels),
    ])
    page = ('<!doctype html>\n<html lang="en"><head><meta charset="utf-8">'
            '<meta name="viewport" content="width=device-width, initial-scale=1">'
            '<title>theDotNetWay</title><meta name="description" content="%s">'
            '<meta name="color-scheme" content="light dark">'
            '<script>document.documentElement.className+=" js";try{var t=localStorage.getItem("theme");if(t==="light"||t==="dark")document.documentElement.setAttribute("data-theme",t)}catch(e){}</script>'
            '<style>%s</style></head><body>%s<script>%s</script></body></html>\n'
            % (e(META['description']), CSS.strip(), body, JS.strip()))
    c = check(page)
    if c.errors:
        sys.stderr.write('build: %d problem(s); index.html not written:\n  %s\n' % (len(c.errors), '\n  '.join(c.errors[:200])))
        sys.exit(1)
    out = os.path.join(REPO, 'index.html')
    with open(out, 'w', encoding='utf-8') as fh:
        fh.write(page)
    print('wrote %s (%s bytes; %d numbers in the ledger, %d identifiers marked as names)' % (out, format(len(page.encode('utf-8')), ','), len(L.rows), len(c.lits)))


if __name__ == '__main__':
    build()
