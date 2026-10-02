"""Pre-registered test runner for design 3 "vrp-stretched-state-compressed-premium" (vrp).

Registration: doc/research/preregistration-2026-09-27.md, section "Design 3".

The script implements the registered design exactly:

- feature p_t = #{VIX_{t-k} <= VIX_t, k = 1..1260} / 1260, recomputed from the Cboe
  VIX_History.csv (right-continuous, today's close excluded from the window);
- target Y_t = ln(VIX_t) - ln(RV_t), RV_t = 100 * sqrt((252/21) * sum r_{t+1..t+21}^2)
  on the SPX trading calendar (SPX closes strictly after t); secondary D_t = VIX_t - RV_t
  and Q_t = VIX_t^2 - RV_t^2;
- rule S_t(q) = 1 if p_t >= q, q in {0.75, 0.80, 0.85}; signal events and non-signal
  comparison windows are non-overlapping 21-SPX-day forward windows built first-come,
  walking forward in time, separately on signal and non-signal days;
- fit on start dates 2011-01-03..2018-11-30 (every forward window ends by 2018-12-31);
  q* = argmin Welch t subject to n_events >= 15 and firing rate <= 30%, ties to the
  smaller q; the fit record is frozen to a file BEFORE any 2019+ outcome is computed;
- ONE holdout run of q* on start dates 2019-01-02 through the last complete window,
  reporting M1..M6 and the pass / kill / inconclusive / not-supported verdict with a
  Bonferroni m = 3 one-sided alpha of 0.05 / 3.

Data access is read-only (files opened for reading, SQLite opened with mode=ro). All
paths are arguments. Every quantity computed on 2019+ data is listed in the attempt log
of the results file, including the non-registered variants reported for the record.

Usage:
    python vrp_test.py --vix-history <VIX_History.csv> --spx <spx.csv>
        --spx-cboe <SPX_History.csv> --vix-ledger <vix.csv> --decisions <decisions.csv>
        --engine-db <engine.db> --out <results.json> [--freeze-out <fit_frozen.json>]
        [--seed 20260927]
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import sqlite3
from datetime import datetime, timezone
from zoneinfo import ZoneInfo

import numpy as np
import pandas as pd

NY = ZoneInfo("America/New_York")

DESIGN = "vrp-stretched-state-compressed-premium"
GRID = [0.75, 0.80, 0.85]
N_WIN = 1260
H = 21
FIT_START = "2011-01-03"
FIT_LAST_START = "2018-11-30"
FIT_LAST_OUTCOME = "2018-12-31"
HOLDOUT_START = "2019-01-02"
MIN_FIT_EVENTS = 15
FIRE_CAP = 0.30
M_BONF = 3
ALPHA = 0.05 / M_BONF
N_BOOT = 10_000
BLOCK = 63
DEFAULT_SEED = 20260927
PASS_DELTA_FLOOR = -0.05
MIN_EVENTS = 30
SCORE_PCT = 0.85
TOL_P = 1e-4
TOL_RET = 1e-4

# Designer's disclosed fit-window looks (start dates through 2018-12-31), for comparison only.
DESIGNER_FIT = {
    0.75: {"n_events": 26, "delta": -0.108, "t": -1.21, "fire_rate": 0.152},
    0.80: {"n_events": 23, "delta": -0.078, "t": -0.91, "fire_rate": 0.128},
    0.85: {"n_events": 18, "delta": -0.116, "t": -1.32, "fire_rate": 0.091},
}
DESIGNER_HOLDOUT_STATE_COUNTS = {
    0.75: {"fire_rate": 0.302, "n_events": 45, "events_2020": 12, "events_2022": 10},
    0.80: {"fire_rate": 0.243, "n_events": 39, "events_2020": 12, "events_2022": 9},
    0.85: {"fire_rate": 0.175, "n_events": 33, "events_2020": 10, "events_2022": 7},
}


# ----------------------------------------------------------------------------- utilities

def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def ny_date(ms):
    return datetime.fromtimestamp(ms / 1000, timezone.utc).astimezone(NY).strftime("%Y-%m-%d")


def clean(obj):
    """Convert numpy / pandas scalars for JSON; NaN becomes null."""
    if isinstance(obj, dict):
        return {str(k): clean(v) for k, v in obj.items()}
    if isinstance(obj, (list, tuple)):
        return [clean(v) for v in obj]
    if isinstance(obj, np.ndarray):
        return [clean(v) for v in obj.tolist()]
    if isinstance(obj, (np.bool_, bool)):
        return bool(obj)
    if isinstance(obj, (np.integer, int)):
        return int(obj)
    if isinstance(obj, (np.floating, float)):
        return None if np.isnan(obj) else float(obj)
    if isinstance(obj, (pd.Timestamp, datetime)):
        return obj.strftime("%Y-%m-%d")
    return obj


# ----------------------------------------------------------------------------- distributions

def _betacf(a, b, x):
    max_it, eps, fpmin = 500, 3e-14, 1e-300
    qab, qap, qam = a + b, a + 1.0, a - 1.0
    c, d = 1.0, 1.0 - qab * x / qap
    if abs(d) < fpmin:
        d = fpmin
    d = 1.0 / d
    h = d
    for m in range(1, max_it + 1):
        m2 = 2 * m
        aa = m * (b - m) * x / ((qam + m2) * (a + m2))
        d = 1.0 + aa * d
        d = fpmin if abs(d) < fpmin else d
        c = 1.0 + aa / c
        c = fpmin if abs(c) < fpmin else c
        d = 1.0 / d
        h *= d * c
        aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2))
        d = 1.0 + aa * d
        d = fpmin if abs(d) < fpmin else d
        c = 1.0 + aa / c
        c = fpmin if abs(c) < fpmin else c
        d = 1.0 / d
        de = d * c
        h *= de
        if abs(de - 1.0) < eps:
            break
    return h


def betainc(a, b, x):
    """Regularised incomplete beta I_x(a, b)."""
    if x <= 0.0:
        return 0.0
    if x >= 1.0:
        return 1.0
    lbeta = math.lgamma(a + b) - math.lgamma(a) - math.lgamma(b)
    bt = math.exp(lbeta + a * math.log(x) + b * math.log(1.0 - x))
    if x < (a + 1.0) / (a + b + 2.0):
        return bt * _betacf(a, b, x) / a
    return 1.0 - bt * _betacf(b, a, 1.0 - x) / b


def t_cdf(t, df):
    """Student t CDF P(T <= t)."""
    if math.isnan(t) or math.isnan(df) or df <= 0:
        return float("nan")
    x = df / (df + t * t)
    tail = 0.5 * betainc(df / 2.0, 0.5, x)  # P(T > |t|)
    return tail if t < 0 else 1.0 - tail


def t_ppf(pr, df):
    lo, hi = -100.0, 100.0
    for _ in range(300):
        mid = 0.5 * (lo + hi)
        if t_cdf(mid, df) < pr:
            lo = mid
        else:
            hi = mid
    return 0.5 * (lo + hi)


def wilson(k, n, z=1.959964):
    if n == 0:
        return (float("nan"), float("nan"))
    ph = k / n
    den = 1.0 + z * z / n
    centre = (ph + z * z / (2 * n)) / den
    half = z * math.sqrt(ph * (1 - ph) / n + z * z / (4 * n * n)) / den
    return (centre - half, centre + half)


# ----------------------------------------------------------------------------- data

def load_cboe_vix(path):
    df = pd.read_csv(path)
    df["date"] = pd.to_datetime(df["DATE"], format="%m/%d/%Y").dt.strftime("%Y-%m-%d")
    df = df[["date", "CLOSE"]].rename(columns={"CLOSE": "vix"}).sort_values("date").reset_index(drop=True)
    if df["date"].duplicated().any():
        raise SystemExit(f"duplicate dates in {path}")
    return df


def load_cboe_spx(path):
    df = pd.read_csv(path)
    df["date"] = pd.to_datetime(df["DATE"], format="%m/%d/%Y").dt.strftime("%Y-%m-%d")
    return df[["date", "SPX"]].rename(columns={"SPX": "close"}).sort_values("date").reset_index(drop=True)


def load_spx(path):
    df = pd.read_csv(path, dtype={"date": str}).sort_values("date").reset_index(drop=True)
    if df["date"].duplicated().any():
        raise SystemExit(f"duplicate dates in {path}")
    return df[["date", "close"]]


def regime_percentile(closes):
    """Engine statistic on a full window: #{c_s <= c_t, s in [t-1260, t-1]} / 1260; NaN before."""
    n = len(closes)
    p = np.full(n, np.nan)
    for i in range(N_WIN, n):
        w = closes[i - N_WIN:i]
        p[i] = np.count_nonzero(w <= closes[i]) / N_WIN
    return p


def ledger_gate(db_path, p_by_date):
    """Recomputed p_t against cls_dislocations.regime_percentile on every equity row with
    regime_history = 1260, keyed by the NY date of the VIX observation the row scored."""
    con = sqlite3.connect(f"file:{db_path}?mode=ro", uri=True)
    try:
        rows = con.execute(
            "select iv_observed_at_ms, as_of_ms, regime_percentile, regime_history "
            "from cls_dislocations where context='equity' and regime_history=? "
            "order by as_of_ms, ledger_sequence", (N_WIN,)).fetchall()
    finally:
        con.close()
    per_date = {}
    for iv_ms, _as_of, pct, _hist in rows:
        per_date.setdefault(ny_date(iv_ms), set()).add(pct)
    mism = []
    multi = 0
    max_diff = 0.0
    n_match = 0
    missing = []
    for d, pcts in sorted(per_date.items()):
        if len(pcts) > 1:
            multi += 1
        if d not in p_by_date or np.isnan(p_by_date[d]):
            missing.append(d)
            continue
        diff = max(abs(x - p_by_date[d]) for x in pcts)
        max_diff = max(max_diff, diff)
        if diff < TOL_P:
            n_match += 1
        else:
            mism.append({"date": d, "ledger": sorted(pcts), "recomputed": p_by_date[d]})
    return {
        "ledger_rows_full_window": len(rows),
        "ledger_dates_full_window": len(per_date),
        "first_date": min(per_date) if per_date else None,
        "last_date": max(per_date) if per_date else None,
        "dates_with_conflicting_ledger_values": multi,
        "match_1e-4": n_match,
        "mismatch": len(mism),
        "missing_in_recomputation": missing,
        "max_abs_diff": max_diff,
        "passed": len(mism) == 0 and len(missing) == 0,
        "mismatch_examples": mism[:10],
    }


def vix_identity_gate(cboe, ledger_path):
    led = pd.read_csv(ledger_path, dtype={"date": str})
    m = led.merge(cboe, on="date", how="left", suffixes=("_ledger", "_cboe"))
    m = m.rename(columns={"close": "ledger", "vix": "cboe"})
    missing = m[m["cboe"].isna()]
    both = m.dropna(subset=["cboe"])
    diff = (both["ledger"] - both["cboe"]).abs()
    bad = both[diff > 0.005]
    return {
        "ledger_dates": int(len(led)),
        "shared_dates": int(len(both)),
        "ledger_dates_absent_from_cboe": missing["date"].tolist(),
        "max_abs_diff": float(diff.max()) if len(both) else None,
        "dates_differing_by_more_than_0.005": bad["date"].tolist(),
        "passed": len(bad) == 0 and len(missing) == 0,
    }


def spx_gate(spx, cboe_spx, first="2011-01-01"):
    a = spx[spx["date"] >= "1990-01-01"].reset_index(drop=True)
    b = cboe_spx.reset_index(drop=True)
    a["r"] = np.log(a["close"]).diff()
    b["r"] = np.log(b["close"]).diff()
    a = a[a["date"] >= first]
    b = b[b["date"] >= first]
    m = a.merge(b, on="date", how="outer", suffixes=("_yahoo", "_cboe"), indicator=True)
    only_y = m[m["_merge"] == "left_only"]["date"].tolist()
    only_c = m[m["_merge"] == "right_only"]["date"].tolist()
    both = m[m["_merge"] == "both"].copy()
    both["dr"] = (both["r_yahoo"] - both["r_cboe"]).abs()
    bad = both[both["dr"] > TOL_RET].sort_values("date")
    return {
        "first_date_checked": first,
        "shared_dates": int(len(both)),
        "yahoo_only_dates": only_y,
        "cboe_only_dates": only_c,
        "max_abs_return_diff": float(both["dr"].max()),
        "n_discrepant_return_dates": int(len(bad)),
        "discrepant_return_dates": [
            {"date": r.date, "yahoo_close": r.close_yahoo, "cboe_close": r.close_cboe,
             "yahoo_logret": r.r_yahoo, "cboe_logret": r.r_cboe}
            for r in bad.itertuples()],
        "passed_clean": int(len(bad)) == 0 and not only_y and not only_c,
    }


def build_panel(cboe_vix, spx):
    """One row per start date: Cboe VIX dates that are SPX trading days with a full 1,260 window
    and a complete 21-SPX-day forward window."""
    spx = spx.reset_index(drop=True)
    px = spx["close"].to_numpy(float)
    r = np.zeros(len(px))
    r[1:] = np.log(px[1:] / px[:-1])
    cs = np.cumsum(r * r)
    si = {d: i for i, d in enumerate(spx["date"])}
    cboe_vix = cboe_vix.copy()
    cboe_vix["p"] = regime_percentile(cboe_vix["vix"].to_numpy(float))
    rows = []
    n_spx = len(px)
    for d, v, p in zip(cboe_vix["date"], cboe_vix["vix"], cboe_vix["p"]):
        if d not in si or np.isnan(p):
            continue
        i = si[d]
        if i + H > n_spx - 1:
            continue
        ss = cs[i + H] - cs[i]  # sum of r_{i+1..i+H}^2, strictly after t
        rv = 100.0 * math.sqrt((252.0 / H) * ss)
        rows.append((d, i, v, p, rv))
    df = pd.DataFrame(rows, columns=["date", "si", "vix", "p", "rv"])
    df["Y"] = np.log(df["vix"]) - np.log(df["rv"])
    df["D"] = df["vix"] - df["rv"]
    df["Q"] = df["vix"] ** 2 - df["rv"] ** 2
    df["year"] = df["date"].str[:4].astype(int)
    return df, spx, r


# ----------------------------------------------------------------------------- statistics

def nonoverlap(si_values, mask):
    """First-come non-overlapping forward windows: accept row k if its start is at least H SPX
    days after the last accepted start (windows [t+1, t+21] then share no day)."""
    out = []
    last = -10 ** 9
    for k in np.flatnonzero(mask):
        s = si_values[k]
        if s >= last + H:
            out.append(k)
            last = s
    return np.array(out, dtype=int)


def group_desc(y):
    y = np.asarray(y, float)
    n = len(y)
    if n == 0:
        return {"n": 0}
    out = {"n": int(n), "mean": float(y.mean()), "sd": float(y.std(ddof=1)) if n > 1 else float("nan"),
           "median": float(np.median(y)), "n_neg": int(np.count_nonzero(y < 0)),
           "p_neg": float(np.count_nonzero(y < 0) / n), "p10": float(np.quantile(y, 0.10)),
           "min": float(y.min()), "max": float(y.max())}
    k = max(1, int(math.floor(0.10 * n)))
    out["worst_decile_mean"] = float(np.sort(y)[:k].mean())
    out["p_neg_wilson95"] = list(wilson(out["n_neg"], n))
    return out


def welch(a, b):
    a = np.asarray(a, float)
    b = np.asarray(b, float)
    na, nb = len(a), len(b)
    if na < 2 or nb < 2:
        return {"delta": float("nan"), "se": float("nan"), "t": float("nan"), "df": float("nan"),
                "p_one_sided": float("nan")}
    va, vb = a.var(ddof=1), b.var(ddof=1)
    se = math.sqrt(va / na + vb / nb)
    delta = a.mean() - b.mean()
    t = delta / se if se > 0 else float("nan")
    df = se ** 4 / ((va / na) ** 2 / (na - 1) + (vb / nb) ** 2 / (nb - 1))
    return {"delta": float(delta), "se": float(se), "t": float(t), "df": float(df),
            "p_one_sided": float(t_cdf(t, df))}


def contrast(panel, ev_idx, win_idx, col="Y"):
    """Signal events versus non-signal windows on one target column."""
    a = panel[col].to_numpy(float)[ev_idx]
    b = panel[col].to_numpy(float)[win_idx]
    res = welch(a, b)
    ga, gb = group_desc(a), group_desc(b)
    res.update({
        "signal_events": ga, "non_signal_windows": gb,
        "p_neg_ratio": (ga["p_neg"] / gb["p_neg"]) if gb.get("n", 0) and gb.get("p_neg", 0) > 0 else None,
        "sign_metric_in_direction": (ga.get("p_neg", float("nan")) > gb.get("p_neg", float("nan"))) if ga["n"] and gb["n"] else None,
    })
    return res


def day_contrast(y, mask):
    a, b = y[mask], y[~mask]
    res = welch(a, b)
    res.update({"signal_days": group_desc(a), "non_signal_days": group_desc(b)})
    return res


def stationary_indices(n, rng, block):
    p = 1.0 / block
    idx = np.empty(n, dtype=int)
    filled = 0
    while filled < n:
        start = int(rng.integers(n))
        length = int(rng.geometric(p))
        length = min(length, n - filled)
        idx[filled:filled + length] = (start + np.arange(length)) % n
        filled += length
    return idx


def block_bootstrap(y, masks, rng, n_boot=N_BOOT, block=BLOCK):
    """Stationary block bootstrap of the daily series. Returns, for each named mask, the
    resampled Delta_days distribution, plus the resampled unconditional mean."""
    n = len(y)
    names = list(masks)
    out = {k: np.full(n_boot, np.nan) for k in names}
    umean = np.empty(n_boot)
    for b in range(n_boot):
        idx = stationary_indices(n, rng, block)
        yy = y[idx]
        umean[b] = yy.mean()
        for k in names:
            mm = masks[k][idx]
            ns = int(mm.sum())
            if 0 < ns < n:
                out[k][b] = yy[mm].mean() - yy[~mm].mean()
    return out, umean


def boot_summary(dist):
    d = dist[~np.isnan(dist)]
    return {
        "n_resamples": int(len(dist)),
        "n_valid": int(len(d)),
        "mean": float(d.mean()),
        "sd": float(d.std(ddof=1)),
        "one_sided_upper_98.33": float(np.quantile(d, 1 - ALPHA)),
        "one_sided_lower_98.33": float(np.quantile(d, ALPHA)),
        "two_sided_95": [float(np.quantile(d, 0.025)), float(np.quantile(d, 0.975))],
        "share_ge_0": float(np.mean(d >= 0)),
    }


def overlaps_block(si_values, block_si):
    """Rows whose forward window [si+1, si+21] shares a day with [block_si+1, block_si+21]."""
    return np.abs(si_values - block_si) <= H - 1


def window_contains(si_values, target_si_set):
    out = np.zeros(len(si_values), dtype=bool)
    for k, s in enumerate(si_values):
        for ts in target_si_set:
            if s + 1 <= ts <= s + H:
                out[k] = True
                break
    return out


# ----------------------------------------------------------------------------- fit

def fit_cell(panel, q):
    mask = panel["p"].to_numpy() >= q
    si = panel["si"].to_numpy()
    ev = nonoverlap(si, mask)
    win = nonoverlap(si, ~mask)
    c = contrast(panel, ev, win)
    return {
        "q": q, "n_days": int(len(panel)), "n_signal_days": int(mask.sum()),
        "fire_rate": float(mask.mean()), "n_signal_events": int(len(ev)),
        "n_non_signal_windows": int(len(win)),
        "mean_signal": c["signal_events"]["mean"], "mean_non_signal": c["non_signal_windows"]["mean"],
        "sd_signal": c["signal_events"]["sd"], "sd_non_signal": c["non_signal_windows"]["sd"],
        "delta": c["delta"], "welch_se": c["se"], "welch_t": c["t"], "welch_df": c["df"],
        "p_neg_signal": c["signal_events"]["p_neg"], "p_neg_non_signal": c["non_signal_windows"]["p_neg"],
        "eligible": bool(len(ev) >= MIN_FIT_EVENTS and mask.mean() <= FIRE_CAP),
        "points_D_mean_signal": float(panel["D"].to_numpy()[ev].mean()),
        "points_D_mean_non_signal": float(panel["D"].to_numpy()[win].mean()),
        "designer_disclosed": DESIGNER_FIT[q],
    }


def select_q(table):
    elig = [c for c in table if c["eligible"]]
    if not elig:
        return None, "no eligible grid cell"
    best = min(elig, key=lambda c: (c["welch_t"], c["q"]))
    return best["q"], "argmin Welch t among eligible cells (n_events >= 15, fire rate <= 30%), ties to smaller q"


# ----------------------------------------------------------------------------- holdout

def evaluate_rule(panel, mask, rng_for_boot=None, label="", log=None):
    """Full event/window and day-level evaluation of one binary rule on the holdout panel."""
    si = panel["si"].to_numpy()
    y = panel["Y"].to_numpy(float)
    ev = nonoverlap(si, mask)
    win = nonoverlap(si, ~mask)
    res = {
        "label": label,
        "n_days": int(len(panel)), "n_signal_days": int(mask.sum()), "fire_rate": float(mask.mean()),
        "n_signal_events": int(len(ev)), "n_non_signal_windows": int(len(win)),
        "signal_event_dates": panel["date"].to_numpy()[ev].tolist(),
        "events_per_year": panel.iloc[ev].groupby("year").size().to_dict(),
        "windows_per_year": panel.iloc[win].groupby("year").size().to_dict(),
        "signal_days_per_year": panel[mask].groupby("year").size().to_dict(),
        "M1_log_Y": contrast(panel, ev, win, "Y"),
        "M2_days_log_Y": day_contrast(y, mask),
        "M5c_points_D": contrast(panel, ev, win, "D"),
        "M5c_variance_Q": contrast(panel, ev, win, "Q"),
    }
    if log is not None:
        log.append(f"{label}: events/windows built; M1 (Y), M2 point estimate (days), M3, M5c (D, Q) computed")
    return res, ev, win


def per_year(panel, ev, win, mask):
    y = panel["Y"].to_numpy(float)
    out = {}
    for yr in sorted(panel["year"].unique()):
        rows = panel["year"].to_numpy() == yr
        e = [k for k in ev if rows[k]]
        w = [k for k in win if rows[k]]
        me = float(y[e].mean()) if e else None
        mw = float(y[w].mean()) if w else None
        dm = rows & mask
        dn = rows & ~mask
        out[int(yr)] = {
            "n_days": int(rows.sum()), "n_signal_days": int(dm.sum()),
            "n_events": len(e), "n_windows": len(w),
            "mean_Y_events": me, "mean_Y_windows": mw,
            "delta_events": (me - mw) if (me is not None and mw is not None) else None,
            "mean_Y_signal_days": float(y[dm].mean()) if dm.any() else None,
            "mean_Y_non_signal_days": float(y[dn].mean()) if dn.any() else None,
            "delta_days": (float(y[dm].mean() - y[dn].mean()) if (dm.any() and dn.any()) else None),
            "p_neg_events": (float(np.mean(y[e] < 0)) if e else None),
            "p_neg_windows": (float(np.mean(y[w] < 0)) if w else None),
        }
    return out


def exclusion_variant(panel, ev, win, drop_rows, name, note):
    keep_ev = ev[~drop_rows[ev]]
    keep_win = win[~drop_rows[win]]
    c = contrast(panel, keep_ev, keep_win, "Y")
    return {
        "name": name, "definition": note,
        "n_rows_excluded_from_panel": int(drop_rows.sum()),
        "events_dropped": int(len(ev) - len(keep_ev)), "windows_dropped": int(len(win) - len(keep_win)),
        "dropped_event_dates": panel["date"].to_numpy()[ev[drop_rows[ev]]].tolist(),
        "dropped_window_dates": panel["date"].to_numpy()[win[drop_rows[win]]].tolist(),
        "M1_log_Y": c,
        "M3": {"p_neg_signal_events": c["signal_events"].get("p_neg"),
               "p_neg_non_signal_windows": c["non_signal_windows"].get("p_neg"),
               "in_direction": c["sign_metric_in_direction"]},
    }


def score_table(panel, dec_path, fit_panel):
    dec = pd.read_csv(dec_path, dtype={"date": str})
    dec = dec[dec["context"] == "equity"][["date", "composite_score", "regime"]]
    dec = dec.drop_duplicates("date", keep="last")
    fit = fit_panel.merge(dec, on="date", how="left")
    fit_abs = fit["composite_score"].abs().dropna()
    thr = float(np.quantile(fit_abs.to_numpy(), SCORE_PCT))
    hp = panel.merge(dec, on="date", how="left")
    abs_score = hp["composite_score"].abs()
    mask = (abs_score >= thr).fillna(False).to_numpy(bool)
    y = hp["Y"].to_numpy(float)
    by_regime = {}
    for reg, g in hp.groupby(hp["regime"].fillna("missing")):
        by_regime[reg] = {"n_days": int(len(g)), "mean_Y": float(g["Y"].mean()),
                          "p_neg": float((g["Y"] < 0).mean()), "mean_D": float(g["D"].mean())}
    deciles = {}
    valid = abs_score.notna()
    try:
        bins = pd.qcut(abs_score[valid], 10, duplicates="drop")
        for b, g in hp[valid].groupby(bins, observed=True):
            deciles[str(b)] = {"n_days": int(len(g)), "mean_Y": float(g["Y"].mean()),
                               "p_neg": float((g["Y"] < 0).mean())}
    except ValueError as exc:
        deciles = {"error": str(exc)}
    return thr, mask, {
        "threshold_abs_composite_score_fit_p85": thr,
        "fit_dates_with_score": int(len(fit_abs)),
        "holdout_dates_without_decision": int((~valid).sum()),
        "holdout_share_abs_score_zero": float((abs_score == 0).mean()),
        "descriptive_by_regime_label_all_holdout_days": by_regime,
        "descriptive_by_abs_score_decile_all_holdout_days": deciles,
        "note": "descriptive only, never gating (registration: Features, secondary)",
    }, y


# ----------------------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--vix-history", required=True, help="Cboe VIX_History.csv (DATE, OPEN, HIGH, LOW, CLOSE)")
    ap.add_argument("--spx", required=True, help="spx.csv (Yahoo ^GSPC: date, open, high, low, close, volume)")
    ap.add_argument("--spx-cboe", required=True, help="Cboe SPX_History.csv (DATE, SPX) cross-check")
    ap.add_argument("--vix-ledger", required=True, help="ledger export vix.csv (date, close)")
    ap.add_argument("--decisions", required=True, help="ledger export decisions.csv")
    ap.add_argument("--engine-db", required=True, help="read-only ledger copy engine.db")
    ap.add_argument("--out", required=True, help="results JSON path")
    ap.add_argument("--freeze-out", default=None, help="fit freeze JSON written before the holdout")
    ap.add_argument("--seed", type=int, default=DEFAULT_SEED)
    args = ap.parse_args()

    attempt_log = []
    deviations = []
    started = datetime.now(timezone.utc).isoformat()
    inputs = {k: {"path": v, "sha256": sha256(v)} for k, v in {
        "vix_history": args.vix_history, "spx": args.spx, "spx_cboe": args.spx_cboe,
        "vix_ledger": args.vix_ledger, "decisions": args.decisions, "engine_db": args.engine_db}.items()}

    cboe_vix = load_cboe_vix(args.vix_history)
    spx = load_spx(args.spx)
    cboe_spx = load_cboe_spx(args.spx_cboe)

    # ---- M6 gates (no outcome read)
    p_full = regime_percentile(cboe_vix["vix"].to_numpy(float))
    p_by_date = dict(zip(cboe_vix["date"], p_full))
    gates = {
        "G1_regime_percentile_identity": ledger_gate(args.engine_db, p_by_date),
        "G2_vix_ledger_vs_cboe": vix_identity_gate(cboe_vix, args.vix_ledger),
        "G3_spx_yahoo_vs_cboe_returns": spx_gate(spx, cboe_spx),
    }
    vix_no_spx = [d for d in cboe_vix["date"] if d >= FIT_START and d not in set(spx["date"])]
    gates["G4_vix_rows_without_spx_close_dropped"] = {"n": len(vix_no_spx), "dates": vix_no_spx}
    if not gates["G1_regime_percentile_identity"]["passed"]:
        raise SystemExit("G1 failed: recomputed p_t does not reproduce the ledger regime_percentile")
    if not gates["G2_vix_ledger_vs_cboe"]["passed"]:
        raise SystemExit("G2 failed: ledger VIX differs from Cboe")
    spx_bad_dates = [x["date"] for x in gates["G3_spx_yahoo_vs_cboe_returns"]["discrepant_return_dates"]]
    if spx_bad_dates:
        deviations.append(
            "G3: Yahoo and Cboe SPX log returns differ by more than 1e-4 on "
            f"{len(spx_bad_dates)} dates ({', '.join(spx_bad_dates)}); Yahoo is used as registered and every "
            "metric that gates the pass is also reported with the windows containing these dates excluded, "
            "as the registration requires.")

    panel, spx_cal, _r = build_panel(cboe_vix, spx)
    si_of = {d: i for i, d in enumerate(spx_cal["date"])}

    # ---- fit: the registration names 2018-11-30 as the last start date "so that every fit forward
    # window ends on or before 2018-12-31"; December 2018 had 19 SPX sessions, so a 2018-11-30 start
    # ends 2019-01-03. The intent (no 2019 SPX return in the fit) governs: the last start date is the
    # latest date whose 21-day window ends on or before 2018-12-31. The literal-cutoff table is
    # recorded alongside for comparison; selection uses the strict table.
    fit_lit = panel[(panel["date"] >= FIT_START) & (panel["date"] <= FIT_LAST_START)].reset_index(drop=True)
    end_idx = int(np.searchsorted(spx_cal["date"].to_numpy(), FIT_LAST_OUTCOME, side="right")) - 1
    fit = panel[(panel["date"] >= FIT_START) & (panel["si"] + H <= end_idx)].reset_index(drop=True)
    last_fit_window_end = spx_cal["date"].iloc[int(fit["si"].max()) + H]
    if last_fit_window_end > FIT_LAST_OUTCOME:
        raise SystemExit("fit window leaks past 2018-12-31")
    fit_table = [fit_cell(fit, q) for q in GRID]
    q_star, rule = select_q(fit_table)
    fit_table_literal = [fit_cell(fit_lit, q) for q in GRID]
    q_star_literal, _ = select_q(fit_table_literal)
    if fit["date"].iloc[-1] != FIT_LAST_START:
        deviations.append(
            f"Fit cutoff: the registered last start date {FIT_LAST_START} has a forward window ending "
            f"{spx_cal['date'].iloc[int(fit_lit['si'].max()) + H]} (December 2018 had 19 SPX sessions), which "
            "contradicts the registration's stated purpose that no SPX return from 2019 enters the fit. The runner "
            f"used the last start date whose window ends on or before {FIT_LAST_OUTCOME}: {fit['date'].iloc[-1]} "
            f"({len(fit_lit) - len(fit)} start dates fewer). The literal-cutoff table (which reads the SPX returns of "
            f"2019-01-02 and 2019-01-03) is recorded for comparison; it selects q* = {q_star_literal}.")
    uncond_fit = group_desc(fit["Y"].to_numpy(float))
    fit_record = {
        "window": {"first_start_date": fit["date"].iloc[0], "last_start_date": fit["date"].iloc[-1],
                   "registered_last_start_date": FIT_LAST_START,
                   "last_forward_window_end": last_fit_window_end, "n_start_dates": int(len(fit)),
                   "n_start_dates_literal_cutoff": int(len(fit_lit))},
        "unconditional_Y": uncond_fit,
        "unconditional_D_mean": float(fit["D"].mean()),
        "table": fit_table,
        "table_literal_cutoff_2018_11_30": fit_table_literal,
        "selected_q_literal_cutoff": q_star_literal,
        "selected_q": q_star,
        "selection_rule": rule,
        "designer_expected_q": 0.85,
        "designer_cutoff_note": "designer's disclosed cells used start dates through 2018-12-31; the runner's "
                                "strict cutoff 2018-11-30 removes about 20 start dates whose windows end in "
                                "January 2019; differences per cell are visible against designer_disclosed",
        "frozen_at_utc": datetime.now(timezone.utc).isoformat(),
        "inputs": inputs,
    }
    if args.freeze_out:
        with open(args.freeze_out, "w", encoding="utf-8", newline="\n") as f:
            json.dump(clean(fit_record), f, indent=2)
    if q_star is None:
        raise SystemExit("no eligible q in the fit window; nothing to run on the holdout")
    if q_star != 0.85:
        deviations.append(f"fit selected q* = {q_star}, not the designer's expected 0.85 (allowed: the registration says the runner's strict-cutoff fit decides)")

    # ---- holdout: ONE run of q*
    hold = panel[panel["date"] >= HOLDOUT_START].reset_index(drop=True)
    attempt_log.append(f"holdout panel built: {len(hold)} start dates {hold['date'].iloc[0]}..{hold['date'].iloc[-1]}")
    y = hold["Y"].to_numpy(float)
    si = hold["si"].to_numpy()
    mask_star = hold["p"].to_numpy() >= q_star
    primary, ev, win = evaluate_rule(hold, mask_star, label=f"q*={q_star} (registered)", log=attempt_log)

    # M2 + M5g + non-registered rules via one shared bootstrap (same resamples, seed 20260927)
    other_masks = {f"q={q} (non-registered)": hold["p"].to_numpy() >= q for q in GRID if q != q_star}
    thr_score, mask_score, score_desc, _ = score_table(hold, args.decisions, fit)
    masks = {"primary": mask_star, "score": mask_score, **other_masks}
    rng = np.random.default_rng(args.seed)
    boot, umean = block_bootstrap(y, masks, rng)
    attempt_log.append("M2: stationary block bootstrap (block 63, 10000 resamples, seed %d) of Delta_days for q*, "
                       "for the two non-registered q and for the |score| rule; M5g unconditional mean from the same resamples" % args.seed)
    m2 = {"delta_days": primary["M2_days_log_Y"]["delta"],
          "mean_signal_days": primary["M2_days_log_Y"]["signal_days"]["mean"],
          "mean_non_signal_days": primary["M2_days_log_Y"]["non_signal_days"]["mean"],
          "n_signal_days": primary["n_signal_days"], "n_non_signal_days": int(len(hold) - primary["n_signal_days"]),
          "bootstrap": boot_summary(boot["primary"]),
          "method": "stationary bootstrap of the daily (signal, Y) pairs in start-date order, geometric block "
                    "lengths with mean 63 trading days, circular wrap, 10,000 resamples, seed %d" % args.seed}

    # M3
    c1 = primary["M1_log_Y"]
    c2 = primary["M2_days_log_Y"]
    m3 = {
        "events": {"p_neg_signal_events": c1["signal_events"]["p_neg"], "wilson95_signal": c1["signal_events"]["p_neg_wilson95"],
                   "n_signal_events": c1["signal_events"]["n"],
                   "p_neg_non_signal_windows": c1["non_signal_windows"]["p_neg"], "wilson95_non_signal": c1["non_signal_windows"]["p_neg_wilson95"],
                   "n_non_signal_windows": c1["non_signal_windows"]["n"],
                   "ratio": c1["p_neg_ratio"], "in_direction": c1["sign_metric_in_direction"]},
        "days": {"p_neg_signal_days": c2["signal_days"]["p_neg"], "wilson95_signal": c2["signal_days"]["p_neg_wilson95"],
                 "p_neg_non_signal_days": c2["non_signal_days"]["p_neg"], "wilson95_non_signal": c2["non_signal_days"]["p_neg_wilson95"],
                 "ratio": (c2["signal_days"]["p_neg"] / c2["non_signal_days"]["p_neg"]) if c2["non_signal_days"]["p_neg"] > 0 else None},
    }
    attempt_log.append("M3: sign metric on events/windows and on all days")

    # M4
    t_crit = t_ppf(ALPHA, c1["df"]) if not math.isnan(c1["df"]) else float("nan")
    m4 = {"welch_df": c1["df"], "t_crit_one_sided_alpha_0.0167": t_crit, "se_delta": c1["se"],
          "MDE_80": float((abs(t_crit) + 0.84) * c1["se"]) if not math.isnan(t_crit) else None,
          "in_sample_effect_range": [-0.116, -0.078],
          "power_note": "power against the fit-window effect = P(t < t_crit | true Delta = fit Delta)",
          "power_vs_fit_delta": {}}
    for d_true in (-0.078, -0.108, -0.116):
        if not math.isnan(t_crit):
            m4["power_vs_fit_delta"][str(d_true)] = float(t_cdf(t_crit - d_true / c1["se"], c1["df"]))
    attempt_log.append("M4: MDE and power from realised holdout SE and Welch df")

    # M5a exclusions
    k_maxrv = int(np.argmax(hold["rv"].to_numpy()))
    drop_maxrv = overlaps_block(si, si[k_maxrv])
    ex_maxrv = exclusion_variant(
        hold, ev, win, drop_maxrv, "excluding the largest-RV block",
        "block = the 21-SPX-day forward window of the holdout start date with the largest RV "
        f"({hold['date'].iloc[k_maxrv]}, RV {hold['rv'].iloc[k_maxrv]:.2f}); every event and comparison window "
        "whose forward window shares at least one day with it is dropped")
    k_fav = int(ev[np.argmin(y[ev])]) if len(ev) else None
    if k_fav is not None:
        drop_fav = overlaps_block(si, si[k_fav])
        ex_fav = exclusion_variant(
            hold, ev, win, drop_fav, "excluding the most favourable block",
            "block = the forward window of the signal event with the lowest Y "
            f"({hold['date'].iloc[k_fav]}, Y {y[k_fav]:.3f}); every event and comparison window whose forward "
            "window shares at least one day with it is dropped")
    else:
        ex_fav = None
    deviations.append("M5a interpretation: the registration does not say how a 'block' maps onto the non-overlapping "
                      "samples; the runner defines it as the 21-SPX-day forward window of one start date and drops every "
                      "event and comparison window overlapping it (at most two per group).")
    attempt_log.append("M5a: Delta_mean and M3 excluding the largest-RV block and excluding the most favourable block")

    # M5b per year
    m5b = per_year(hold, ev, win, mask_star)
    attempt_log.append("M5b: per-year table")

    # M5d composite-score rule
    score_res, ev_s, win_s = evaluate_rule(hold, mask_score, label=f"|composite_score| >= fit p85 ({thr_score:.4f}) (non-registered, M5d)", log=attempt_log)
    score_res["M2_bootstrap"] = boot_summary(boot["score"])
    score_res.pop("signal_event_dates", None)
    score_res.update(score_desc)

    # M5e non-selected q
    m5e = {}
    for lab, mk in other_masks.items():
        r, _e, _w = evaluate_rule(hold, mk, label=lab, log=attempt_log)
        r["M2_bootstrap"] = boot_summary(boot[lab])
        r.pop("signal_event_dates", None)
        m5e[lab] = r

    # M5f medians and worst decile are inside group_desc; M5g unconditional
    m5g = {"mean_Y_all_holdout_days": float(y.mean()), "sd": float(y.std(ddof=1)), "n_days": int(len(y)),
           "p_neg_all_days": float(np.mean(y < 0)), "median": float(np.median(y)),
           "mean_D_points": float(hold["D"].mean()), "mean_Q_variance_points": float(hold["Q"].mean()),
           "bootstrap_mean_Y": boot_summary(umean),
           "share_of_resamples_with_mean_le_0": float(np.mean(umean <= 0))}
    attempt_log.append("M5g: unconditional holdout premium with block-bootstrap interval")

    # SPX-discrepancy exclusion (G3 finding): gating metrics without the windows containing bad returns
    spx_excl = None
    if spx_bad_dates:
        bad_si = {si_of[d] for d in spx_bad_dates if d in si_of}
        drop_bad = window_contains(si, bad_si)
        ex = exclusion_variant(hold, ev, win, drop_bad, "excluding windows containing Yahoo/Cboe discrepant returns",
                               f"start dates whose forward window contains any of {spx_bad_dates}")
        keep = ~drop_bad
        rng2 = np.random.default_rng(args.seed)
        boot2, _u2 = block_bootstrap(y[keep], {"primary": mask_star[keep]}, rng2)
        ex["M2_days_log_Y"] = day_contrast(y[keep], mask_star[keep])
        ex["M2_bootstrap"] = boot_summary(boot2["primary"])
        ex["n_days_kept"] = int(keep.sum())
        spx_excl = ex
        attempt_log.append("G3 follow-up: M1, M2 (with its own bootstrap, same seed), M3 excluding windows containing the discrepant SPX dates")

    # ---- verdict
    n_ev = primary["n_signal_events"]
    delta = c1["delta"]
    p_welch = c1["p_one_sided"]
    upper = m2["bootstrap"]["one_sided_upper_98.33"]
    conds = {
        "i_n_signal_events_ge_30": {"value": n_ev, "passed": n_ev >= MIN_EVENTS},
        "ii_delta_mean_le_-0.05_and_welch_p_lt_0.0167": {"delta_mean": delta, "welch_p_one_sided": p_welch,
                                                        "passed": bool(delta <= PASS_DELTA_FLOOR and p_welch < ALPHA)},
        "iii_bootstrap_upper_98.33_of_delta_days_lt_0": {"upper_bound": upper, "passed": bool(upper < 0)},
        "iv_sign_metric_in_direction": {"p_neg_signal_events": c1["signal_events"]["p_neg"],
                                        "p_neg_non_signal_windows": c1["non_signal_windows"]["p_neg"],
                                        "passed": bool(c1["sign_metric_in_direction"])},
        "v_fire_rate_le_0.30": {"fire_rate": primary["fire_rate"], "passed": bool(primary["fire_rate"] <= FIRE_CAP)},
    }
    all_pass = all(v["passed"] for v in conds.values())
    if spx_excl is not None:
        cx = spx_excl["M1_log_Y"]
        conds["spx_discrepancy_exclusion_also_passes"] = {
            "delta_mean": cx["delta"], "welch_p_one_sided": cx["p_one_sided"],
            "bootstrap_upper_98.33": spx_excl["M2_bootstrap"]["one_sided_upper_98.33"],
            "sign_in_direction": cx["sign_metric_in_direction"],
            "passed": bool(cx["delta"] <= PASS_DELTA_FLOOR and cx["p_one_sided"] < ALPHA
                           and spx_excl["M2_bootstrap"]["one_sided_upper_98.33"] < 0 and cx["sign_metric_in_direction"])}
        all_pass = all_pass and conds["spx_discrepancy_exclusion_also_passes"]["passed"]
    if n_ev < MIN_EVENTS:
        verdict = "INCONCLUSIVE BY DESIGN (n_signal_events < 30)"
    elif delta >= 0 or not c1["sign_metric_in_direction"]:
        verdict = "KILL (Delta_mean >= 0 or P(Y<0 | events) <= P(Y<0 | windows))"
    elif all_pass:
        verdict = "PASS"
    else:
        verdict = "NOT SUPPORTED AT PRE-REGISTERED POWER (Delta_mean < 0 and sign in direction, but a pass condition fails)"

    results = {
        "design": DESIGN,
        "registration": "doc/research/preregistration-2026-09-27.md, Design 3",
        "run_started_utc": started,
        "run_finished_utc": datetime.now(timezone.utc).isoformat(),
        "seed": args.seed,
        "constants": {"N": N_WIN, "H": H, "grid": GRID, "alpha_one_sided": ALPHA, "m_bonferroni": M_BONF,
                      "block_length": BLOCK, "n_boot": N_BOOT, "pass_delta_floor": PASS_DELTA_FLOOR,
                      "min_events": MIN_EVENTS, "fire_cap": FIRE_CAP},
        "inputs": inputs,
        "M6_gates": gates,
        "fit": fit_record,
        "holdout": {
            "sample": {"first_start_date": hold["date"].iloc[0], "last_start_date": hold["date"].iloc[-1],
                       "last_forward_window_end": spx_cal["date"].iloc[int(hold["si"].max()) + H],
                       "n_start_dates": int(len(hold)), "q_star": q_star,
                       "n_signal_days": primary["n_signal_days"], "fire_rate": primary["fire_rate"],
                       "n_signal_events": primary["n_signal_events"], "n_non_signal_windows": primary["n_non_signal_windows"],
                       "events_per_year": primary["events_per_year"], "windows_per_year": primary["windows_per_year"],
                       "signal_days_per_year": primary["signal_days_per_year"],
                       "signal_event_dates": primary["signal_event_dates"],
                       "designer_state_only_counts": DESIGNER_HOLDOUT_STATE_COUNTS[q_star]},
            "M1_primary": c1,
            "M2_primary_confirmation": m2,
            "M3_sign_metric": m3,
            "M4_mde": m4,
            "M5_robustness": {
                "a_exclusions": {"largest_rv_block": ex_maxrv, "most_favourable_block": ex_fav},
                "b_per_year": m5b,
                "c_points_D": primary["M5c_points_D"],
                "c_variance_Q": primary["M5c_variance_Q"],
                "d_composite_score_rule": score_res,
                "e_non_selected_q": m5e,
                "f_medians_and_worst_decile": {"signal_events": {k: c1["signal_events"].get(k) for k in ("median", "p10", "worst_decile_mean", "min")},
                                               "non_signal_windows": {k: c1["non_signal_windows"].get(k) for k in ("median", "p10", "worst_decile_mean", "min")}},
                "g_unconditional_premium": m5g,
            },
            "spx_discrepancy_exclusion": spx_excl,
        },
        "pass_conditions": conds,
        "verdict": verdict,
        "passed": bool(verdict == "PASS"),
        "attempts_in_fit_period": len(GRID),
        "attempt_log_holdout": attempt_log,
        "deviations_from_registration": deviations,
    }
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(clean(results), f, indent=2)

    print(f"design {DESIGN}: q* = {q_star} (fit: " + "; ".join(
        f"q={c['q']} n={c['n_signal_events']} delta={c['delta']:+.3f} t={c['welch_t']:+.2f} fire={c['fire_rate']:.3f}"
        for c in fit_table) + ")")
    print(f"holdout {hold['date'].iloc[0]}..{hold['date'].iloc[-1]}: {len(hold)} start dates, fire {primary['fire_rate']:.3f}, "
          f"events {n_ev}, windows {primary['n_non_signal_windows']}")
    print(f"M1 Delta_mean {delta:+.4f} (sig {c1['signal_events']['mean']:.4f} sd {c1['signal_events']['sd']:.3f} | "
          f"non {c1['non_signal_windows']['mean']:.4f} sd {c1['non_signal_windows']['sd']:.3f}) SE {c1['se']:.4f} "
          f"t {c1['t']:+.3f} df {c1['df']:.1f} p {p_welch:.4f}")
    print(f"M2 Delta_days {m2['delta_days']:+.4f} upper98.33 {upper:+.4f} 95% {m2['bootstrap']['two_sided_95']}")
    print(f"M3 P(Y<0) events {c1['signal_events']['p_neg']:.3f} vs windows {c1['non_signal_windows']['p_neg']:.3f}")
    print(f"verdict: {verdict}")
    print(f"results written to {args.out}")


if __name__ == "__main__":
    main()
