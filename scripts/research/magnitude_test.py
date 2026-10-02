"""Test runner for pre-registered design 4: r2-magnitude-vx30-state-vs-realized-baseline.

Registration: doc/research/preregistration-2026-09-27.md, section "Design 4". This script
implements that section as written: the one-sided level percentile of the spot volatility index
(the engine's cls_dislocations.regime_percentile) as the only feature, the state rule
STATE_t(d, side), the 3 x 3 grid, the two horizons, the trailing 21-day realized baseline, the
median excess statistic, the 5,000-shift circular placebo, the 63-day circular block bootstrap,
the fit-window selection and gate, and the single holdout run with every registered metric.

Usage:
    python scripts/research/magnitude_test.py --data-dir <dir with vx_curve.csv, vx_futures.csv,
        vix.csv, ovx.csv> --ledger <read-only copy of engine.db> --out <results.json> [--fit-only]

--fit-only computes the fit window (2011-2018) only and never touches a 2019+ outcome; it exists
so the runner can debug every code path on the fit window before the one holdout run. All inputs
are opened read-only (the ledger with mode=ro); the only file written is --out.

Not fitted, fixed by the registration: baseline window 21 days, sqrt(h) scaling, median
statistic, VX30 as primary target, 63-day blocks, 5,000 placebo shifts, episode gap 5 days,
alpha 0.05/18 per market (0.05/36 reported as a sensitivity), REL_21 >= 0.15, STRAT_21 >= 0.5 x
EXCESS_21, n_eff >= 30.
"""
import argparse
import hashlib
import json
import sqlite3
import sys
from datetime import datetime, timezone
from zoneinfo import ZoneInfo

import numpy as np
import pandas as pd

NY = ZoneInfo("America/New_York")
GRID = [(d, s) for d in (0.35, 0.40, 0.45) for s in ("high", "low", "both")]
HORIZONS = (5, 21)
BASE_WIN = 21
LEVEL_WIN = 1260
PLACEBO_N = 5000
PLACEBO_MIN_OFFSET = 63
BOOT_N = 2000
BLOCK = 63
BLOCK_SENSITIVITY = 21
EPISODE_GAP = 5
N_EFF_MIN = 30
ATTEMPTS_PER_MARKET = 18
ATTEMPTS_TOTAL = 36
ALPHA_18 = 0.05 / ATTEMPTS_PER_MARKET
ALPHA_36 = 0.05 / ATTEMPTS_TOTAL
REL_MIN = 0.15
KILL_P = 0.20
SEED = 20260927
FIT_START = "2011-01-03"
FIT_END = "2018-12-31"
HOLD_START = "2019-01-02"
EXTRAP = "extrap_F1F2_front_beyond_30d"
DAILY_JUMP_FLAG = 0.5


# ----------------------------------------------------------------------------- utilities

def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def ny_date(ms):
    return datetime.fromtimestamp(ms / 1000, timezone.utc).astimezone(NY).date()


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


def dstr(ts):
    return pd.Timestamp(ts).strftime("%Y-%m-%d")


# ----------------------------------------------------------------------------- feature

def level_percentile(closes):
    """Engine statistic: #{c_s <= c_t, s in [t-1260, t-1]} / min(t, 1260); undefined at t = 0."""
    n = len(closes)
    P = np.full(n, np.nan)
    N = np.zeros(n, dtype=int)
    for i in range(1, n):
        lo = max(0, i - LEVEL_WIN)
        w = closes[lo:i]
        N[i] = len(w)
        P[i] = np.count_nonzero(w <= closes[i]) / N[i]
    return P, N


def load_spot(path):
    df = pd.read_csv(path, parse_dates=["date"]).sort_values("date").reset_index(drop=True)
    if df["date"].duplicated().any():
        raise SystemExit(f"duplicate dates in {path}")
    P, N = level_percentile(df["close"].to_numpy(float))
    df["P"] = P
    df["N"] = N
    return df


def ledger_crosscheck(db_path, context, spot):
    """Compare recomputed P_t with cls_dislocations.regime_percentile, last row per NY date."""
    con = sqlite3.connect(f"file:{db_path}?mode=ro", uri=True)
    try:
        rows = con.execute(
            "select iv_observed_at_ms, as_of_ms, regime_percentile, regime_history "
            "from cls_dislocations where context=? order by as_of_ms, ledger_sequence",
            (context,)).fetchall()
    finally:
        con.close()
    last = {}
    for iv_ms, _as_of, pct, hist in rows:
        last[ny_date(iv_ms)] = (pct, hist)
    mine = {d.date(): (p, n) for d, p, n in zip(spot["date"], spot["P"], spot["N"])}
    overlap = sorted(set(last) & set(mine))
    n_match = 0
    max_diff = 0.0
    mismatches = []
    hist_mismatch = 0
    null_both = 0
    for d in overlap:
        pct, hist = last[d]
        p, n = mine[d]
        if hist != n:
            hist_mismatch += 1
        if pct is None and np.isnan(p):
            null_both += 1
            n_match += 1
            continue
        if pct is None or np.isnan(p):
            mismatches.append((str(d), pct, p))
            continue
        diff = abs(pct - p)
        max_diff = max(max_diff, diff)
        if diff < 1e-4:
            n_match += 1
        else:
            mismatches.append((str(d), pct, p))
    return {
        "context": context,
        "ledger_dates": len(last),
        "spot_dates": len(mine),
        "overlap_dates": len(overlap),
        "match_4dp": n_match,
        "mismatch": len(mismatches),
        "both_undefined": null_both,
        "max_abs_diff": max_diff,
        "history_length_mismatch": hist_mismatch,
        "ledger_only_dates": sorted(str(d) for d in set(last) - set(mine)),
        "spot_only_dates": sorted(str(d) for d in set(mine) - set(last)),
        "mismatch_examples": mismatches[:10],
    }


def state_mask(P, d, side):
    """STATE_t(d, side) with the registration's exact thresholds 0.5 +/- d.

    P_t is a ratio of integers (k / N) and 0.5 - 0.45 evaluates to 0.04999... in floating point,
    which would drop rows with P_t exactly 0.05 (k = 63 of 1,260); the tolerance restores the
    registered <= / >= comparisons (first run without it: 439 instead of 441 state days for
    VIX d = 0.45 both in the fit window; no other fit cell affected).
    """
    tol = 1e-9
    hi = P >= 0.5 + d - tol
    lo = P <= 0.5 - d + tol
    if side == "high":
        return hi
    if side == "low":
        return lo
    return hi | lo


# ----------------------------------------------------------------------------- markets

def add_targets(cal):
    """r_{t,h} = |log x_{t+h} - log x_t| / (sqrt(h) * trailing 21-day mean |daily log change|)."""
    cal["logx"] = np.log(cal["x"].to_numpy(float))
    cal["dlog"] = cal["logx"].diff()
    cal["b1"] = cal["dlog"].abs().rolling(BASE_WIN).mean()
    for h in HORIZONS:
        fwd = (cal["logx"].shift(-h) - cal["logx"]).abs()
        cal[f"r{h}"] = fwd / (np.sqrt(h) * cal["b1"])
        cal[f"raw{h}"] = fwd
    return cal


def build_vix_market(data_dir):
    cv = pd.read_csv(f"{data_dir}/vx_curve.csv", parse_dates=["trade_date", "f1_expiry", "f2_expiry"])
    cv = cv.sort_values("trade_date").reset_index(drop=True)
    spot = load_spot(f"{data_dir}/vix.csv")
    cal = cv[["trade_date", "vx30", "vx30_method", "f1_expiry", "f2_expiry", "vix_close"]].rename(
        columns={"trade_date": "date", "vx30": "x", "vx30_method": "method"})
    cal = cal.merge(spot[["date", "close", "P"]], on="date", how="left")
    both = cal["vix_close"].notna() & cal["close"].notna()
    checks = {
        "vx_dates_without_vix_close": [dstr(d) for d in cal.loc[cal["close"].isna(), "date"]],
        "vix_dates_without_vx_settlement": [dstr(d) for d in spot.loc[~spot["date"].isin(cal["date"]), "date"]],
        "vix_close_mismatch_curve_vs_vix_csv": int(((cal.loc[both, "vix_close"] - cal.loc[both, "close"]).abs() > 1e-9).sum()),
        "vx30_method_counts": cal["method"].value_counts().to_dict(),
    }
    cal = add_targets(cal)
    # same-contract front future (robustness target)
    fut = pd.read_csv(f"{data_dir}/vx_futures.csv", parse_dates=["trade_date", "expiry"])
    fut = fut[fut["contract_type"] == "M"]
    settle = {(d, e): s for d, e, s in zip(fut["trade_date"], fut["expiry"], fut["settle"].astype(float))}
    expiries = np.array(sorted(fut["expiry"].unique()))
    n = len(cal)
    dates = cal["date"].to_numpy()
    f1 = cal["f1_expiry"].to_numpy()
    f2 = cal["f2_expiry"].to_numpy()
    dc = np.full(n, np.nan)
    for t in range(1, n):
        a = settle.get((pd.Timestamp(dates[t]), pd.Timestamp(f1[t])))
        b = settle.get((pd.Timestamp(dates[t - 1]), pd.Timestamp(f1[t])))
        if a and b:
            dc[t] = np.log(a) - np.log(b)
    cal["dc"] = dc
    cal["b1_sc"] = pd.Series(np.abs(dc)).rolling(BASE_WIN).mean().to_numpy()
    sc_rank = {}
    for h in HORIZONS:
        r1 = np.full(n, np.nan)
        ranks = {"F1": 0, "F2": 0, "F3+": 0, "missing_settle": 0}
        for t in range(n - h):
            d_end = pd.Timestamp(dates[t + h])
            later = expiries[expiries > d_end]
            if len(later) == 0:
                ranks["missing_settle"] += 1
                continue
            c = pd.Timestamp(later[0])
            rank = "F1" if c == pd.Timestamp(f1[t]) else ("F2" if c == pd.Timestamp(f2[t]) else "F3+")
            s0 = settle.get((pd.Timestamp(dates[t]), c))
            s1 = settle.get((d_end, c))
            if s0 and s1:
                r1[t] = abs(np.log(s1) - np.log(s0))
                ranks[rank] += 1
            else:
                ranks["missing_settle"] += 1
        cal[f"r1_{h}"] = r1 / (np.sqrt(h) * cal["b1_sc"])
        sc_rank[h] = ranks
    checks["same_contract_rank_counts_all_dates"] = sc_rank
    checks["daily_abs_log_change_gt_0.5"] = {
        "vx30": [(dstr(d), round(float(v), 4)) for d, v in zip(cal["date"], cal["dlog"]) if abs(v) > DAILY_JUMP_FLAG],
        "same_contract_f1": [(dstr(d), round(float(v), 4)) for d, v in zip(cal["date"], cal["dc"]) if abs(v) > DAILY_JUMP_FLAG],
    }
    return {"name": "VIX/VX30", "tradable": True, "cal": cal, "spot": spot, "checks": checks, "context": "equity"}


def build_ovx_market(data_dir):
    spot = load_spot(f"{data_dir}/ovx.csv")
    cal = spot[["date", "close", "P"]].copy()
    cal["x"] = cal["close"]
    cal = add_targets(cal)
    checks = {
        "daily_abs_log_change_gt_0.5": {
            "ovx": [(dstr(d), round(float(v), 4)) for d, v in zip(cal["date"], cal["dlog"]) if abs(v) > DAILY_JUMP_FLAG]},
    }
    return {"name": "OVX (spot, not tradable)", "tradable": False, "cal": cal, "spot": spot, "checks": checks, "context": "oil"}


def window_rows(cal, start, end=None, need_forward=False):
    """Signal rows: dates in [start, end] with P_t and b_t defined (and t+21 available if asked)."""
    m = (cal["date"] >= pd.Timestamp(start)) & cal["P"].notna() & cal["b1"].notna()
    if end is not None:
        m &= cal["date"] <= pd.Timestamp(end)
    if need_forward:
        m &= pd.Series(np.arange(len(cal)) + max(HORIZONS) < len(cal))
    return np.where(m.to_numpy())[0]


# ----------------------------------------------------------------------------- statistics

def excess_stats(r, st):
    rs = r[st]
    med_all = float(np.median(r))
    med_st = float(np.median(rs)) if rs.size else np.nan
    return {
        "n_state": int(rs.size), "n_all": int(r.size),
        "median_state": med_st, "median_all": med_all,
        "excess": med_st - med_all, "rel": med_st / med_all - 1 if med_all else np.nan,
        "mean_state": float(rs.mean()) if rs.size else np.nan, "mean_all": float(r.mean()),
        "p75_state": float(np.percentile(rs, 75)) if rs.size else np.nan, "p75_all": float(np.percentile(r, 75)),
        "p90_state": float(np.percentile(rs, 90)) if rs.size else np.nan, "p90_all": float(np.percentile(r, 90)),
    }


def placebo_test(r, st, rng, n_shift=PLACEBO_N):
    """Circular shifts of the state indicator by offsets uniform on [63, N-63]; one-sided p."""
    N = len(r)
    med_all = np.median(r)
    obs = np.median(r[st]) - med_all if st.any() else np.nan
    if N <= 2 * PLACEBO_MIN_OFFSET or not st.any():
        return {"observed": obs, "sd": np.nan, "z": np.nan, "p": np.nan, "n_shifts": 0}
    offs = rng.integers(PLACEBO_MIN_OFFSET, N - PLACEBO_MIN_OFFSET + 1, size=n_shift)
    dist = np.empty(n_shift)
    for i, k in enumerate(offs):
        s = np.roll(st, k)
        dist[i] = np.median(r[s]) - med_all
    sd = float(dist.std(ddof=1))
    return {
        "observed": float(obs), "sd": sd, "z": float(obs / sd) if sd > 0 else np.nan,
        "p": float((1 + np.count_nonzero(dist >= obs)) / (n_shift + 1)),
        "placebo_mean": float(dist.mean()), "placebo_q95": float(np.quantile(dist, 0.95)),
        "placebo_q_alpha18": float(np.quantile(dist, 1 - ALPHA_18)), "n_shifts": int(n_shift),
    }


def block_bootstrap(r, st, rng, block=BLOCK, n_rep=BOOT_N):
    """Circular block bootstrap of aligned (state, r) rows; 95% percentile interval of EXCESS."""
    N = len(r)
    nb = int(np.ceil(N / block))
    ar = np.arange(block)
    out = np.full(n_rep, np.nan)
    for i in range(n_rep):
        starts = rng.integers(0, N, size=nb)
        idx = ((starts[:, None] + ar[None, :]) % N).ravel()[:N]
        rs, ss = r[idx], st[idx]
        if ss.any():
            out[i] = np.median(rs[ss]) - np.median(rs)
    lo, hi = np.nanpercentile(out, [2.5, 97.5])
    return {"block": block, "n_rep": n_rep, "n_valid": int(np.count_nonzero(~np.isnan(out))),
            "lo": float(lo), "hi": float(hi), "excludes_zero": bool(lo > 0 or hi < 0),
            "boot_mean": float(np.nanmean(out)), "boot_sd": float(np.nanstd(out, ddof=1))}


def stratified_excess(r, st, b):
    """State-day-count-weighted average over deciles of b of [median r state - median r all]."""
    dec = pd.qcut(b, 10, labels=False, duplicates="drop")
    num, den, table = 0.0, 0, []
    for q in sorted(np.unique(dec)):
        m = dec == q
        ms = m & st
        k = int(ms.sum())
        if k:
            diff = float(np.median(r[ms]) - np.median(r[m]))
            num += k * diff
            den += k
        else:
            diff = np.nan
        table.append({"decile": int(q), "n_all": int(m.sum()), "n_state": k,
                      "b_min": float(b[m].min()), "b_max": float(b[m].max()), "excess": diff})
    return (num / den if den else np.nan), table


def n_nonoverlap(positions, h):
    last = -10 ** 9
    n = 0
    for p in positions:
        if p >= last + h:
            n += 1
            last = p
    return n


def episodes(positions, gap=EPISODE_GAP):
    """Runs of state positions; a new run starts when the position difference exceeds gap."""
    if len(positions) == 0:
        return []
    eps = []
    start = prev = positions[0]
    n = 1
    for p in positions[1:]:
        if p - prev > gap:
            eps.append((start, prev, n))
            start, n = p, 0
        prev = p
        n += 1
    eps.append((start, prev, n))
    return eps


def horizon_block(r, st, b, positions, h, rng, with_boot=True):
    out = excess_stats(r, st)
    out["n_eff"] = n_nonoverlap(positions[st], h)
    out["placebo"] = placebo_test(r, st, rng)
    if with_boot:
        out["bootstrap_63"] = block_bootstrap(r, st, rng, BLOCK)
        out["bootstrap_21_sensitivity"] = block_bootstrap(r, st, rng, BLOCK_SENSITIVITY)
        strat, table = stratified_excess(r, st, b)
        out["strat"] = strat
        out["strat_table"] = table
        out["strat_ge_half_excess"] = bool(strat >= 0.5 * out["excess"]) if not np.isnan(strat) else None
    return out


def evaluate(mk, rows, d, side, rng, full):
    """All registered metrics for one (d, side) on one row set; the same code for fit and holdout."""
    cal = mk["cal"]
    P = cal["P"].to_numpy()[rows]
    b1 = cal["b1"].to_numpy()[rows]
    dates = cal["date"].to_numpy()[rows]
    st = state_mask(P, d, side)
    eps = episodes(rows[st])
    res = {
        "d": d, "side": side, "n_rows": int(len(rows)), "n_state": int(st.sum()),
        "fire_rate": float(st.mean()), "dec002_fire_rate_le_30pct": bool(st.mean() <= 0.30),
        "n_episodes": len(eps), "first_date": dstr(dates[0]), "last_date": dstr(dates[-1]),
        "horizons": {},
    }
    r = {h: cal[f"r{h}"].to_numpy()[rows] for h in HORIZONS}
    for h in HORIZONS:
        res["horizons"][h] = horizon_block(r[h], st, b1 * np.sqrt(h), rows, h, rng, with_boot=full)
    if not full:
        return res
    # descriptive side split at this d
    res["side_split"] = {}
    for s in ("high", "low"):
        ss = state_mask(P, d, s)
        res["side_split"][s] = {"n_state": int(ss.sum()), "horizons": {
            h: {**excess_stats(r[h], ss), "n_eff": n_nonoverlap(rows[ss], h),
                "placebo_p": placebo_test(r[h], ss, rng)["p"] if ss.any() else np.nan} for h in HORIZONS}}
    # entry-day-only version
    entry = np.zeros(len(rows), dtype=bool)
    pos_to_i = {p: i for i, p in enumerate(rows)}
    for s0, _e, _n in eps:
        entry[pos_to_i[s0]] = True
    res["entry_day_only"] = {"n_entries": int(entry.sum()), "horizons": {
        h: {**excess_stats(r[h], entry), "placebo_p": placebo_test(r[h], entry, rng)["p"]} for h in HORIZONS}}
    # same-contract F1 robustness and extrapolation exclusion (VX30 market only)
    if "r1_5" in cal:
        res["same_contract_f1"] = {}
        method = cal["method"].to_numpy()
        res["vx30_excluding_extrapolated"] = {}
        for h in HORIZONS:
            r1 = cal[f"r1_{h}"].to_numpy()[rows]
            ok = ~np.isnan(r1)
            res["same_contract_f1"][h] = {"n_dropped_undefined": int((~ok).sum()),
                                          **excess_stats(r1[ok], st[ok]),
                                          "placebo": placebo_test(r1[ok], st[ok], rng)}
            keep = np.array([method[p] != EXTRAP and method[p + h] != EXTRAP for p in rows])
            res["vx30_excluding_extrapolated"][h] = {"n_dropped": int((~keep).sum()),
                                                     **excess_stats(r[h][keep], st[keep]),
                                                     "placebo": placebo_test(r[h][keep], st[keep], rng)}
    # exclusion of the single largest 63-day block
    nblk = int(np.ceil(len(rows) / BLOCK))
    blk = np.arange(len(rows)) // BLOCK
    raw21 = cal["raw21"].to_numpy()[rows]
    med_all_21 = np.median(r[21])
    vol_score = np.array([raw21[blk == k].mean() for k in range(nblk)])
    contrib = np.array([np.sum((r[21][(blk == k) & st] - med_all_21)) if ((blk == k) & st).any() else -np.inf
                        for k in range(nblk)])
    res["excluding_largest_block"] = {}
    for label, score in (("most_volatile_block_mean_abs_21d_log_move", vol_score),
                         ("largest_state_contribution_block_sum_r21_minus_median", contrib)):
        k = int(np.argmax(score))
        keep = blk != k
        res["excluding_largest_block"][label] = {
            "block_index": k, "block_start": dstr(dates[blk == k][0]), "block_end": dstr(dates[blk == k][-1]),
            "block_state_days": int(st[blk == k].sum()),
            "horizons": {h: {**excess_stats(r[h][keep], st[keep]),
                             "placebo": placebo_test(r[h][keep], st[keep], rng)} for h in HORIZONS}}
    # per-year table with the largest contributing episode
    years = pd.DatetimeIndex(dates).year
    ep_rows = []
    for s0, e0, n_days in eps:
        i0, i1 = pos_to_i[s0], pos_to_i[e0]
        sel = np.zeros(len(rows), dtype=bool)
        sel[i0:i1 + 1] = st[i0:i1 + 1]
        ep_rows.append({"start": dstr(dates[i0]), "end": dstr(dates[i1]), "n_days": int(n_days),
                        "median_r5": float(np.median(r[5][sel])), "median_r21": float(np.median(r[21][sel])),
                        "contribution_21": float(np.sum(r[21][sel] - med_all_21)), "year": int(years[i0])})
    res["episodes"] = ep_rows
    res["per_year"] = []
    for y in sorted(set(years)):
        my = years == y
        ys = my & st
        row = {"year": int(y), "n_days": int(my.sum()), "n_state": int(ys.sum())}
        for h in HORIZONS:
            row[f"median_r{h}_state"] = float(np.median(r[h][ys])) if ys.any() else np.nan
            row[f"median_r{h}_all"] = float(np.median(r[h][my]))
        cands = [e for e in ep_rows if e["year"] == y]
        row["largest_episode"] = max(cands, key=lambda e: e["contribution_21"]) if cands else None
        res["per_year"].append(row)
    return res


def verdict(res, alpha):
    h5, h21 = res["horizons"][5], res["horizons"][21]
    P1 = h5["n_eff"] >= N_EFF_MIN and h21["n_eff"] >= N_EFF_MIN
    P2 = h21["placebo"]["p"] <= alpha
    P3 = h21["bootstrap_63"]["excludes_zero"]
    P4 = h21["rel"] >= REL_MIN
    P5 = h5["excess"] > 0
    P6 = bool(h21["strat_ge_half_excess"])
    K1 = h21["excess"] <= 0
    K2 = h5["placebo"]["p"] > KILL_P and h21["placebo"]["p"] > KILL_P
    partial_h5 = (h5["placebo"]["p"] <= alpha and h5["bootstrap_63"]["excludes_zero"] and h5["rel"] >= REL_MIN
                  and h21["excess"] > 0 and bool(h5["strat_ge_half_excess"]))
    all_pass = P2 and P3 and P4 and P5 and P6
    if not P1:
        status = "INCONCLUSIVE BY DESIGN"
        note = "too few independent state windows after 2018 to test (P1 failed); K1/K2 flags reported but not applied"
    elif K1 or K2:
        status = "KILL"
        note = "K1 (EXCESS_21 <= 0)" if K1 else "K2 (placebo p_5 > 0.20 and p_21 > 0.20)"
    elif all_pass:
        status = "PASS"
        note = "P1-P6 all hold at h = 21 with EXCESS_5 > 0"
    elif partial_h5 or (P2 and P3 and P5 and P6 and not P4):
        status = "PARTIAL"
        note = "P2-P6 hold at h = 5 only" if partial_h5 else "all hold except P4 (economic size)"
    else:
        status = "NOT PASSED"
        note = "neither PASS nor KILL nor INCONCLUSIVE"
    return {"alpha": alpha, "status": status, "note": note,
            "P1_n_eff": P1, "P2_p21": P2, "P3_ci21_excludes_zero": P3, "P4_rel21_ge_0.15": P4,
            "P5_excess5_gt_0": P5, "P6_strat21_ge_half": P6, "K1_excess21_le_0": K1, "K2_both_p_gt_0.20": K2,
            "partial_h5_conditions": partial_h5}


# ----------------------------------------------------------------------------- fit and holdout

def fit_market(mk, rng):
    cal = mk["cal"]
    rows = window_rows(cal, FIT_START, FIT_END)
    early = cal[(cal["date"] >= pd.Timestamp(FIT_START)) & (cal["date"] <= pd.Timestamp(FIT_END))
                & cal["P"].notna() & cal["b1"].isna()]["date"]
    P = cal["P"].to_numpy()[rows]
    b1 = cal["b1"].to_numpy()[rows]
    r = {h: cal[f"r{h}"].to_numpy()[rows] for h in HORIZONS}
    table = []
    for d, side in GRID:
        st = state_mask(P, d, side)
        row = {"d": d, "side": side, "n_state": int(st.sum()), "fire_rate": float(st.mean()),
               "n_episodes": len(episodes(rows[st])), "horizons": {}}
        for h in HORIZONS:
            hb = excess_stats(r[h], st)
            hb["n_eff"] = n_nonoverlap(rows[st], h)
            hb["placebo"] = placebo_test(r[h], st, rng)
            row["horizons"][h] = hb
        row["eligible"] = bool(row["horizons"][5]["n_eff"] >= N_EFF_MIN and row["horizons"][21]["n_eff"] >= N_EFF_MIN)
        z5, z21 = row["horizons"][5]["placebo"]["z"], row["horizons"][21]["placebo"]["z"]
        row["mean_z"] = float(np.mean([z5, z21]))
        table.append(row)
    eligible = [t for t in table if t["eligible"]]
    selected = max(eligible, key=lambda t: t["mean_z"]) if eligible else None
    gate = None
    if selected is not None:
        gate = {"p21_le_alpha18": selected["horizons"][21]["placebo"]["p"] <= ALPHA_18,
                "excess5_gt_0": selected["horizons"][5]["excess"] > 0}
        gate["cleared"] = bool(gate["p21_le_alpha18"] and gate["excess5_gt_0"])
    fit = {
        "window": {"first_signal_date": dstr(cal["date"].to_numpy()[rows[0]]),
                   "last_signal_date": dstr(cal["date"].to_numpy()[rows[-1]]), "n_signal_dates": int(len(rows)),
                   "signal_dates_dropped_undefined_baseline": [dstr(x) for x in early]},
        "unconditional_median_r": {h: float(np.median(r[h])) for h in HORIZONS},
        "spearman_P_b1": float(np.corrcoef(pd.Series(P).rank().to_numpy(), pd.Series(b1).rank().to_numpy())[0, 1]),
        "grid": table,
        "n_eligible": len(eligible),
        "selected": None if selected is None else {"d": selected["d"], "side": selected["side"], "mean_z": selected["mean_z"]},
        "gate": gate,
        "ranking_by_mean_z": [{"d": t["d"], "side": t["side"], "mean_z": t["mean_z"], "eligible": t["eligible"]}
                              for t in sorted(table, key=lambda t: -t["mean_z"])],
    }
    if selected is not None:
        fit["selected_full_diagnostics_in_sample"] = evaluate(mk, rows, selected["d"], selected["side"], rng, full=True)
    return fit, rows


def holdout_market(mk, d, side, rng):
    cal = mk["cal"]
    rows = window_rows(cal, HOLD_START, None, need_forward=True)
    res = evaluate(mk, rows, d, side, rng, full=True)
    res["verdict_alpha_18"] = verdict(res, ALPHA_18)
    res["verdict_alpha_36_sensitivity"] = verdict(res, ALPHA_36)
    res["unconditional_median_r"] = {h: res["horizons"][h]["median_all"] for h in HORIZONS}
    return res


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--data-dir", required=True)
    ap.add_argument("--ledger", required=True, help="read-only copy of engine.db")
    ap.add_argument("--out", required=True)
    ap.add_argument("--fit-only", action="store_true", help="never compute a 2019+ outcome")
    ap.add_argument("--seed", type=int, default=SEED)
    args = ap.parse_args()

    started = datetime.now(timezone.utc).isoformat()
    inputs = {f: sha256(f"{args.data_dir}/{f}") for f in ("vx_curve.csv", "vx_futures.csv", "vix.csv", "ovx.csv")}
    rng = np.random.default_rng(args.seed)

    out = {
        "design": "r2-magnitude-vx30-state-vs-realized-baseline",
        "registration": "doc/research/preregistration-2026-09-27.md, Design 4",
        "started_utc": started, "seed": args.seed, "fit_only": args.fit_only,
        "constants": {"grid": GRID, "horizons": HORIZONS, "baseline_window": BASE_WIN, "level_window": LEVEL_WIN,
                      "placebo_shifts": PLACEBO_N, "placebo_offset_range": f"[{PLACEBO_MIN_OFFSET}, N-{PLACEBO_MIN_OFFSET}]",
                      "bootstrap_reps": BOOT_N, "block": BLOCK, "block_sensitivity": BLOCK_SENSITIVITY,
                      "episode_gap": EPISODE_GAP, "n_eff_min": N_EFF_MIN, "alpha_18": ALPHA_18, "alpha_36": ALPHA_36,
                      "rel_min": REL_MIN, "kill_p": KILL_P, "fit": [FIT_START, FIT_END], "holdout_start": HOLD_START},
        "input_sha256": inputs,
        "markets": {},
    }
    markets = [build_vix_market(args.data_dir), build_ovx_market(args.data_dir)]
    for mk in markets:
        print(f"\n=== {mk['name']}", flush=True)
        entry = {"name": mk["name"], "tradable": mk["tradable"], "data_checks": mk["checks"]}
        entry["ledger_crosscheck"] = ledger_crosscheck(args.ledger, mk["context"], mk["spot"])
        lc = entry["ledger_crosscheck"]
        print(f" ledger P_t cross-check: {lc['match_4dp']}/{lc['overlap_dates']} match, max diff {lc['max_abs_diff']:.2e}, "
              f"history mismatches {lc['history_length_mismatch']}")
        fit, _rows = fit_market(mk, rng)
        entry["fit"] = fit
        print(f" fit rows {fit['window']['n_signal_dates']} ({fit['window']['first_signal_date']}..{fit['window']['last_signal_date']}); "
              f"uncond median r5 {fit['unconditional_median_r'][5]:.3f} r21 {fit['unconditional_median_r'][21]:.3f}; "
              f"Spearman(P,b1) {fit['spearman_P_b1']:.3f}")
        print("  d    side  fire%  n_eff5 n_eff21 eps | EX5    REL5   z5    p5     | EX21   REL21  z21   p21    | elig meanz")
        for t in fit["grid"]:
            h5, h21 = t["horizons"][5], t["horizons"][21]
            print(f"  {t['d']:.2f} {t['side']:5s} {100 * t['fire_rate']:5.1f} {h5['n_eff']:6d} {h21['n_eff']:7d} {t['n_episodes']:3d} | "
                  f"{h5['excess']:+.3f} {h5['rel']:+.3f} {h5['placebo']['z']:+5.2f} {h5['placebo']['p']:.4f} | "
                  f"{h21['excess']:+.3f} {h21['rel']:+.3f} {h21['placebo']['z']:+5.2f} {h21['placebo']['p']:.4f} | "
                  f"{'Y' if t['eligible'] else 'n'} {t['mean_z']:+.2f}")
        print(f" selected {fit['selected']}; gate {fit['gate']}")
        entry["holdout"] = None
        entry["holdout_outcomes_computed"] = False
        if fit["gate"] and fit["gate"]["cleared"] and not args.fit_only:
            d, side = fit["selected"]["d"], fit["selected"]["side"]
            ho = holdout_market(mk, d, side, rng)
            entry["holdout"] = ho
            entry["holdout_outcomes_computed"] = True
            v = ho["verdict_alpha_18"]
            print(f" HOLDOUT {ho['first_date']}..{ho['last_date']} rows {ho['n_rows']} state {ho['n_state']} fire {100 * ho['fire_rate']:.1f}% "
                  f"episodes {ho['n_episodes']}")
            for h in HORIZONS:
                hb = ho["horizons"][h]
                print(f"  h={h}: n_eff {hb['n_eff']} EXCESS {hb['excess']:+.4f} REL {hb['rel']:+.4f} med_state {hb['median_state']:.3f} "
                      f"med_all {hb['median_all']:.3f} p {hb['placebo']['p']:.4f} z {hb['placebo']['z']:+.2f} "
                      f"CI63 [{hb['bootstrap_63']['lo']:+.3f}, {hb['bootstrap_63']['hi']:+.3f}] STRAT {hb['strat']:+.4f}")
            print(f" VERDICT (alpha 0.05/18): {v['status']} - {v['note']}")
        elif fit["gate"] and not fit["gate"]["cleared"]:
            entry["result"] = ("KILL-IN-SAMPLE: no eligible grid combination clears the fit-window gate "
                               "(p_21 <= 0.00278 and EXCESS_5 > 0); the holdout is not run")
            print(" " + entry["result"])
        elif fit["gate"] is None:
            entry["result"] = "no eligible grid combination (n_eff >= 30 at both horizons); the holdout is not run"
            print(" " + entry["result"])
        out["markets"][mk["context"]] = entry

    primary = out["markets"]["equity"]
    out["passed"] = bool(primary.get("holdout") and primary["holdout"]["verdict_alpha_18"]["status"] == "PASS")
    out["finished_utc"] = datetime.now(timezone.utc).isoformat()
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(clean(out), f, indent=1)
    print(f"\npassed={out['passed']}; results written to {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
