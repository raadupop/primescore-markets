"""Pre-registered test runner for design 1 "vx-roll-carry-5d" (basis).

Registration: doc/research/preregistration-2026-09-27.md, section "Design 1".

The script implements the registered rule exactly:

- calendar: VX trading dates in vx_curve.csv that also have a ledger VIX close;
- roll_t = (S_t(C_t) - VIX_t) / TD_t with C_t = F1 if f1_days > 14 else F2 and
  TD_t = VX trading dates strictly after t and strictly before C_t's expiry
  (full vx_curve.csv calendar);
- SHORT if roll_t >= theta, LONG if roll_t <= -theta;
- enter at the settlement of t+1 in the contract held on t+1, exit at the
  settlement of t+6 in the same contract, settles pulled from vx_futures.csv by
  (trade_date, expiry); P&L = dir x (S_exit - S_entry) - 0.10;
- non-overlapping trades, one contract, no sizing;
- theta chosen on 2011-2018 (grid {0.125, 0.150, 0.175, 0.200, 0.250},
  eligibility exposure <= 0.30 and n >= 30, highest fit t-statistic, ties to the
  lower theta), then ONE run on 2019-01-01 onward.

Data access is read-only. All paths are arguments. The fit record (fit table,
selected theta, SHA-256 hashes of the inputs, timestamp) is written to a
separate freeze file BEFORE any 2019+ outcome is computed, then the holdout is
run once and everything is written to the results JSON.

Usage:
    python basis_test.py --data-dir <dir> --out <results.json>
        [--vix-history <VIX_History.csv>] [--freeze-out <fit_frozen.json>]
        [--seed 20260927]
"""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import math
import sys

import numpy as np
import pandas as pd

GRID = [0.125, 0.150, 0.175, 0.200, 0.250]
HOLD = 5
LAG = 1
COST = 0.10
HELD_F1_MIN_DAYS = 14
EXPOSURE_CAP = 0.30
MIN_FIT_TRADES = 30
FIT_START = pd.Timestamp("2011-01-03")
FIT_END = pd.Timestamp("2018-12-31")
HOLDOUT_START = pd.Timestamp("2019-01-01")
HOLDOUT_END = pd.Timestamp("2026-09-22")
Z_ONE_SIDED_99 = 2.326
N_BOOT = 10_000
N_PLACEBO = 1_000
BLOCK_PRIMARY = 63
BLOCK_REPORT = 21
EXCL_WINDOWS = {
    "ex_2020_02_24_to_2020_04_30": (pd.Timestamp("2020-02-24"), pd.Timestamp("2020-04-30")),
    "ex_2024_08_01_to_2024_08_16": (pd.Timestamp("2024-08-01"), pd.Timestamp("2024-08-16")),
}


# --------------------------------------------------------------------------
# helpers
# --------------------------------------------------------------------------


def sha256_of(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def now_utc() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds")


def fnum(x, nd=4):
    if x is None:
        return None
    if isinstance(x, (float, np.floating)):
        if math.isnan(x) or math.isinf(x):
            return None
        return round(float(x), nd)
    if isinstance(x, (np.integer,)):
        return int(x)
    return x


def jsonable(obj):
    if isinstance(obj, dict):
        return {str(k): jsonable(v) for k, v in obj.items()}
    if isinstance(obj, (list, tuple)):
        return [jsonable(v) for v in obj]
    if isinstance(obj, (pd.Timestamp, dt.date, dt.datetime)):
        return str(obj)[:10]
    if isinstance(obj, (np.bool_,)):
        return bool(obj)
    if isinstance(obj, (np.integer,)):
        return int(obj)
    if isinstance(obj, (np.floating, float)):
        if isinstance(obj, float) and (math.isnan(obj) or math.isinf(obj)):
            return None
        if isinstance(obj, np.floating) and (np.isnan(obj) or np.isinf(obj)):
            return None
        return round(float(obj), 6)
    if isinstance(obj, np.ndarray):
        return [jsonable(v) for v in obj.tolist()]
    return obj


def log_binom_cdf(k: int, n: int, p: float) -> float:
    """P(X <= k) for X ~ Binomial(n, p)."""
    if p <= 0.0:
        return 1.0
    if p >= 1.0:
        return 1.0 if k >= n else 0.0
    total = 0.0
    lp, lq = math.log(p), math.log1p(-p)
    for i in range(0, k + 1):
        total += math.exp(
            math.lgamma(n + 1) - math.lgamma(i + 1) - math.lgamma(n - i + 1) + i * lp + (n - i) * lq
        )
    return min(1.0, total)


def clopper_pearson(k: int, n: int, alpha: float = 0.05):
    """Exact binomial two-sided interval by bisection (no scipy available)."""
    if n == 0:
        return None, None
    if k == 0:
        lo = 0.0
    else:
        a, b = 0.0, 1.0
        for _ in range(200):
            m = 0.5 * (a + b)
            # P(X >= k | m) = 1 - P(X <= k-1 | m)
            if 1.0 - log_binom_cdf(k - 1, n, m) < alpha / 2:
                a = m
            else:
                b = m
        lo = 0.5 * (a + b)
    if k == n:
        hi = 1.0
    else:
        a, b = 0.0, 1.0
        for _ in range(200):
            m = 0.5 * (a + b)
            if log_binom_cdf(k, n, m) < alpha / 2:
                b = m
            else:
                a = m
        hi = 0.5 * (a + b)
    return lo, hi


def sortino(x: np.ndarray):
    if len(x) == 0:
        return None
    downside = np.minimum(x, 0.0)
    dd = math.sqrt(float(np.mean(downside ** 2)))
    if dd == 0.0:
        return None
    return float(np.mean(x)) / dd


def trade_stats(pnl: np.ndarray) -> dict:
    n = len(pnl)
    if n == 0:
        return {"n": 0}
    mean = float(np.mean(pnl))
    sd = float(np.std(pnl, ddof=1)) if n > 1 else None
    t = mean / (sd / math.sqrt(n)) if sd not in (None, 0.0) else None
    wins = int((pnl > 0).sum())
    lo, hi = clopper_pearson(wins, n)
    best_idx = int(np.argmax(pnl))
    ex_best = float(np.mean(np.delete(pnl, best_idx))) if n > 1 else None
    return {
        "n": n,
        "mean": mean,
        "sd": sd,
        "t_stat": t,
        "hit_rate": wins / n,
        "hit_rate_ci95_exact": [lo, hi],
        "wins": wins,
        "median": float(np.median(pnl)),
        "worst": float(np.min(pnl)),
        "best": float(np.max(pnl)),
        "mean_ex_best": ex_best,
        "sortino_mar0": sortino(pnl),
        "total": float(np.sum(pnl)),
        "mde_one_sided_99": (Z_ONE_SIDED_99 * sd / math.sqrt(n)) if sd else None,
    }


def max_drawdown_windowed(daily: np.ndarray, window: int):
    """Largest peak-to-trough fall of cumulative P&L inside any `window`-day span."""
    cum = np.cumsum(daily)
    worst = 0.0
    n = len(cum)
    for i in range(n):
        lo = max(0, i - window + 1)
        peak = cum[lo : i + 1].max()
        worst = max(worst, peak - cum[i])
    return float(worst)


def max_drawdown(daily: np.ndarray):
    cum = np.cumsum(daily)
    peak = np.maximum.accumulate(cum)
    return float(np.max(peak - cum)) if len(cum) else 0.0


def circular_block_indices(rng, n: int, block: int) -> np.ndarray:
    nblocks = int(math.ceil(n / block))
    starts = rng.integers(0, n, size=nblocks)
    idx = (starts[:, None] + np.arange(block)[None, :]).reshape(-1) % n
    return idx[:n]


def block_bootstrap_total(rng, daily: np.ndarray, block: int, n_boot: int) -> np.ndarray:
    n = len(daily)
    out = np.empty(n_boot)
    for b in range(n_boot):
        out[b] = daily[circular_block_indices(rng, n, block)].sum()
    return out


# --------------------------------------------------------------------------
# data
# --------------------------------------------------------------------------


def load(data_dir: str):
    curve = pd.read_csv(
        f"{data_dir}/vx_curve.csv", parse_dates=["trade_date", "f1_expiry", "f2_expiry"]
    )
    fut = pd.read_csv(f"{data_dir}/vx_futures.csv", parse_dates=["trade_date", "expiry"])
    fut = fut[fut.contract_type == "M"].copy()
    if fut.duplicated(["trade_date", "expiry"]).any():
        raise SystemExit("duplicate (trade_date, expiry) rows in monthly futures")
    vix = pd.read_csv(f"{data_dir}/vix.csv", parse_dates=["date"]).rename(
        columns={"date": "trade_date", "close": "vix"}
    )
    dec = pd.read_csv(f"{data_dir}/decisions.csv", parse_dates=["date"])
    dec = dec[dec.context == "equity"][["date", "regime", "composite_score"]].rename(
        columns={"date": "trade_date"}
    )
    return curve, fut, vix, dec


def build_calendar(curve, fut, vix, dec):
    """Full VX calendar with features; strategy calendar = rows with a VIX close."""
    cur = curve.merge(vix, on="trade_date", how="left").merge(dec, on="trade_date", how="left")
    cur = cur.sort_values("trade_date").reset_index(drop=True)
    held_f1 = cur.f1_days > HELD_F1_MIN_DAYS
    cur["c_expiry"] = np.where(held_f1, cur.f1_expiry, cur.f2_expiry)
    cur["c_expiry"] = pd.to_datetime(cur["c_expiry"])
    cur["c_is_f1"] = held_f1
    settle_map = fut.set_index(["trade_date", "expiry"]).settle
    cur["c_settle"] = [
        settle_map.get((t, e), np.nan) for t, e in zip(cur.trade_date, cur.c_expiry)
    ]
    curve_settle = np.where(held_f1, cur.f1_settle, cur.f2_settle)
    mism = int((np.abs(cur.c_settle.values - curve_settle) > 1e-9).sum())
    missing = int(cur.c_settle.isna().sum())
    td_all = cur.trade_date.values
    cur["c_td"] = [
        int(((td_all > t) & (td_all < e)).sum()) for t, e in zip(td_all, cur.c_expiry.values)
    ]
    cur["basis_c"] = cur.c_settle - cur.vix
    cur["roll"] = cur.basis_c / cur.c_td
    # TD_t is undefined when the held contract expires after the last date on
    # file (the count of dates strictly before expiry is truncated): no signal.
    td_truncated = cur.c_expiry > cur.trade_date.max()
    cur.loc[td_truncated, "roll"] = np.nan
    cur["basis_f1"] = cur.f1_settle - cur.vix
    cur["slope_f2_f1"] = cur.f2_settle - cur.f1_settle
    strat = cur[cur.vix.notna() & (cur.trade_date >= FIT_START)].reset_index(drop=True)
    excluded_no_vix = cur[cur.vix.isna() & (cur.trade_date >= FIT_START)].trade_date.tolist()
    checks = {
        "held_contract_settle_mismatch_vs_curve": mism,
        "held_contract_settle_missing_in_futures": missing,
        "min_TD_on_strategy_calendar_with_defined_roll": int(strat.c_td[strat.roll.notna()].min()),
        "dates_with_TD_truncated_by_end_of_file_roll_set_undefined": [
            str(d)[:10] for d in strat.trade_date[strat.roll.isna().values]
        ],
        "vx_dates_without_vix_excluded_from_strategy_calendar": [str(d)[:10] for d in excluded_no_vix],
        "strategy_calendar_rows": int(len(strat)),
        "strategy_calendar_first": str(strat.trade_date.iloc[0])[:10],
        "strategy_calendar_last": str(strat.trade_date.iloc[-1])[:10],
    }
    return cur, strat, settle_map, checks


def vix_crosscheck(vix: pd.DataFrame, path: str | None):
    if not path:
        return {"performed": False, "reason": "no --vix-history path supplied"}
    try:
        cb = pd.read_csv(path)
    except OSError as exc:
        return {"performed": False, "reason": f"could not read {path}: {exc}"}
    cb["trade_date"] = pd.to_datetime(cb["DATE"], format="%m/%d/%Y")
    cb = cb[["trade_date", "CLOSE"]].rename(columns={"CLOSE": "cboe_close"})
    j = vix.merge(cb, on="trade_date", how="outer", indicator=True)
    j = j[(j.trade_date >= FIT_START) & (j.trade_date <= HOLDOUT_END)]
    both = j[j._merge == "both"]
    diff = both[(both.vix - both.cboe_close).abs() > 0.01]
    only_ledger = j[j._merge == "left_only"].trade_date
    only_cboe = j[j._merge == "right_only"].trade_date
    return {
        "performed": True,
        "source": path,
        "shared_dates": int(len(both)),
        "dates_differing_by_more_than_0_01": [
            {"date": str(r.trade_date)[:10], "ledger": float(r.vix), "cboe": float(r.cboe_close)}
            for r in diff.itertuples()
        ],
        "n_differing": int(len(diff)),
        "max_abs_diff_on_shared_dates": float((both.vix - both.cboe_close).abs().max()),
        "ledger_dates_missing_from_cboe": [str(d)[:10] for d in only_ledger],
        "cboe_dates_missing_from_ledger_2011_to_2026_09_22": [str(d)[:10] for d in only_cboe],
    }


# --------------------------------------------------------------------------
# strategy
# --------------------------------------------------------------------------


def raw_signal(strat: pd.DataFrame, theta: float, gate: bool = False, short_only: bool = False):
    r = strat.roll.values
    sig = np.zeros(len(strat), dtype=int)
    if theta <= 0.0:
        sig[r > 0.0] = -1
        if not short_only:
            sig[r < 0.0] = 1
    else:
        sig[r >= theta] = -1
        if not short_only:
            sig[r <= -theta] = 1
    gated_count = 0
    if gate:
        reg = strat.regime.values
        blocked = (sig == -1) & (reg == "high_vol")
        gated_count = int(blocked.sum())
        sig[blocked] = 0
    return sig, gated_count


def execute(
    strat: pd.DataFrame,
    sig: np.ndarray,
    settle_map,
    lag: int = LAG,
    hold: int = HOLD,
    cost: float = COST,
):
    """Non-overlapping execution of a signal array on the strategy calendar slice.

    Returns (trades DataFrame, daily P&L array aligned to `strat`, notes).
    """
    dates = strat.trade_date.values
    c_exp = strat.c_expiry.values
    c_td = strat.c_td.values
    n = len(strat)
    daily = np.zeros(n)
    rows = []
    infeasible = 0
    i = 0
    while i < n:
        s = sig[i]
        if s == 0:
            i += 1
            continue
        e = i + lag
        x = e + hold
        if x >= n:
            i += 1
            continue
        expiry = c_exp[e]
        if c_td[e] < hold:
            infeasible += 1
            i += 1
            continue
        path = [settle_map.get((pd.Timestamp(dates[k]), pd.Timestamp(expiry)), np.nan) for k in range(e, x + 1)]
        if any(np.isnan(p) for p in path):
            infeasible += 1
            i += 1
            continue
        d = float(s)
        gross = d * (path[-1] - path[0])
        net = gross - cost
        daily[e] -= cost
        for k in range(1, len(path)):
            daily[e + k] += d * (path[k] - path[k - 1])
        rows.append(
            {
                "signal_date": pd.Timestamp(dates[i]),
                "entry_date": pd.Timestamp(dates[e]),
                "exit_date": pd.Timestamp(dates[x]),
                "contract_expiry": pd.Timestamp(expiry),
                "dir": int(s),
                "side": "SHORT" if s < 0 else "LONG",
                "roll_at_signal": float(strat.roll.values[i]),
                "vix_at_signal": float(strat.vix.values[i]),
                "regime_at_signal": strat.regime.values[i],
                "td_at_entry": int(c_td[e]),
                "entry_settle": float(path[0]),
                "exit_settle": float(path[-1]),
                "gross": float(gross),
                "net": float(net),
                "entry_idx": int(e),
            }
        )
        i = x  # next signal check at the close of the exit day
    trades = pd.DataFrame(rows)
    notes = {"infeasible_signals_skipped": infeasible}
    return trades, daily, notes


def run_cell(strat_slice, theta, settle_map, lag=LAG, hold=HOLD, cost=COST, gate=False, short_only=False):
    sig, gated = raw_signal(strat_slice, theta, gate=gate, short_only=short_only)
    trades, daily, notes = execute(strat_slice, sig, settle_map, lag=lag, hold=hold, cost=cost)
    notes["gated_short_signals"] = gated
    notes["raw_signal_days_short"] = int((sig == -1).sum())
    notes["raw_signal_days_long"] = int((sig == 1).sum())
    return trades, daily, sig, notes


def summarize_cell(trades: pd.DataFrame, n_days: int, notes: dict) -> dict:
    pnl = trades.net.values if len(trades) else np.array([])
    st = trade_stats(pnl)
    st["n_short"] = int((trades.dir == -1).sum()) if len(trades) else 0
    st["n_long"] = int((trades.dir == 1).sum()) if len(trades) else 0
    st["days_in_market_share"] = (HOLD * len(trades)) / n_days if n_days else None
    st["calendar_days"] = int(n_days)
    st.update(notes)
    return st


def trades_to_records(trades: pd.DataFrame):
    if len(trades) == 0:
        return []
    cols = [
        "signal_date", "entry_date", "exit_date", "contract_expiry", "side", "roll_at_signal",
        "vix_at_signal", "regime_at_signal", "td_at_entry", "entry_settle", "exit_settle", "gross", "net",
    ]
    return jsonable(trades[cols].to_dict(orient="records"))


# --------------------------------------------------------------------------
# main
# --------------------------------------------------------------------------


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--data-dir", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--vix-history", default=None, help="Cboe VIX_History.csv for the cross-check")
    ap.add_argument("--freeze-out", default=None, help="fit-freeze JSON written before the holdout")
    ap.add_argument("--seed", type=int, default=20260927)
    args = ap.parse_args(argv)

    started = now_utc()
    files = ["vx_curve.csv", "vx_futures.csv", "vix.csv", "decisions.csv"]
    hashes = {f: sha256_of(f"{args.data_dir}/{f}") for f in files}
    if args.vix_history:
        hashes["VIX_History.csv"] = sha256_of(args.vix_history)

    curve, fut, vix, dec = load(args.data_dir)
    cur, strat, settle_map, checks = build_calendar(curve, fut, vix, dec)
    xcheck = vix_crosscheck(vix, args.vix_history)

    # ---------------- fit window (2011-2018) ----------------
    fit = strat[(strat.trade_date >= FIT_START) & (strat.trade_date <= FIT_END)].reset_index(drop=True)
    fit_days = len(fit)
    fit_table = {}
    fit_trades_by_theta = {}
    for th in GRID:
        tr, daily, sig, notes = run_cell(fit, th, settle_map)
        fit_trades_by_theta[th] = tr
        cell = summarize_cell(tr, fit_days, notes)
        cell["eligible_exposure"] = bool(cell["days_in_market_share"] <= EXPOSURE_CAP)
        cell["eligible_n"] = bool(cell["n"] >= MIN_FIT_TRADES)
        cell["eligible"] = bool(cell["eligible_exposure"] and cell["eligible_n"])
        # gate count in the fit window for the pre-registered secondary null
        _, gated = raw_signal(fit, th, gate=True)
        cell["fit_short_signal_days_in_high_vol"] = gated
        fit_table[f"{th:.3f}"] = cell
    eligible = [th for th in GRID if fit_table[f"{th:.3f}"]["eligible"]]
    if not eligible:
        raise SystemExit("no eligible grid cell in the fit window")
    best_t = max(fit_table[f"{th:.3f}"]["t_stat"] for th in eligible)
    theta_star = min(th for th in eligible if abs(fit_table[f"{th:.3f}"]["t_stat"] - best_t) < 1e-12)

    fit_record = {
        "design": "vx-roll-carry-5d",
        "fit_window": {"signal_start": str(FIT_START)[:10], "signal_end": str(FIT_END)[:10],
                       "exit_end": str(FIT_END)[:10], "calendar_days": fit_days},
        "grid": GRID,
        "fixed": {"hold_days": HOLD, "entry_lag_days": LAG, "cost_round_trip": COST,
                  "held_contract_rule": f"F1 if f1_days > {HELD_F1_MIN_DAYS} calendar days else F2",
                  "regime_gate_in_primary": False, "symmetric_theta": True},
        "eligibility": {"exposure_cap": EXPOSURE_CAP, "min_trades": MIN_FIT_TRADES, "eligible_thetas": eligible},
        "fit_table": fit_table,
        "selected_theta": theta_star,
        "selection_rule": "highest fit t-statistic among eligible cells; ties to the lower theta",
        "input_sha256": hashes,
        "fit_frozen_at_utc": now_utc(),
        "note": "written before any 2019+ outcome was computed",
    }
    if args.freeze_out:
        with open(args.freeze_out, "w", encoding="utf-8", newline="\n") as fh:
            json.dump(jsonable(fit_record), fh, indent=1)
            fh.write("\n")

    # ---------------- holdout: ONE run at theta_star ----------------
    holdout_log = []
    rng = np.random.default_rng(args.seed)
    ho = strat[(strat.trade_date >= HOLDOUT_START) & (strat.trade_date <= HOLDOUT_END)].reset_index(drop=True)
    ho_days = len(ho)

    trades, daily, sig, notes = run_cell(ho, theta_star, settle_map)
    holdout_log.append(f"primary run theta={theta_star} lag={LAG} hold={HOLD} cost={COST}")
    primary = summarize_cell(trades, ho_days, notes)
    pnl = trades.net.values
    n = len(pnl)

    # (a) trade-level iid bootstrap
    boot_means = np.array([np.mean(rng.choice(pnl, size=n, replace=True)) for _ in range(N_BOOT)]) if n else np.array([])
    holdout_log.append("trade-level iid bootstrap 10000")
    # (b) circular block bootstrap of the daily strategy P&L, 63-day blocks, total / n
    block_totals = block_bootstrap_total(rng, daily, BLOCK_PRIMARY, N_BOOT)
    block_means = block_totals / n if n else np.array([])
    holdout_log.append("circular block bootstrap of daily P&L, 63-day blocks, 10000")
    # 95 percent intervals with 21-day blocks (reporting requirement of the task text)
    block21_totals = block_bootstrap_total(rng, daily, BLOCK_REPORT, N_BOOT)
    block21_means = block21_totals / n if n else np.array([])
    holdout_log.append("circular block bootstrap of daily P&L, 21-day blocks, 10000 (95 percent interval)")
    # trade-level metrics with 21-day calendar blocks of trades
    nblk = int(math.ceil(ho_days / BLOCK_REPORT))
    blk_of_trade = (trades.entry_idx.values // BLOCK_REPORT) if n else np.array([], dtype=int)
    blk_trades = [pnl[blk_of_trade == b] for b in range(nblk)]
    tb = {"mean": [], "hit_rate": [], "median": [], "sortino": [], "mean_ex_best": []}
    for _ in range(N_BOOT):
        pick = rng.integers(0, nblk, size=nblk)
        x = np.concatenate([blk_trades[b] for b in pick]) if nblk else np.array([])
        if len(x) < 2:
            continue
        tb["mean"].append(np.mean(x))
        tb["hit_rate"].append(np.mean(x > 0))
        tb["median"].append(np.median(x))
        s = sortino(x)
        if s is not None:
            tb["sortino"].append(s)
        tb["mean_ex_best"].append(np.mean(np.delete(x, int(np.argmax(x)))))
    holdout_log.append("21-day block bootstrap of trades (by entry date block), 10000, for 95 percent intervals")

    def ci95(a):
        a = np.asarray(a)
        return [float(np.percentile(a, 2.5)), float(np.percentile(a, 97.5))] if len(a) else None

    # placebo: 1,000 circular shifts of the holdout signal series by U{63..252} days
    placebo_means = []
    placebo_ns = []
    shifts = rng.integers(63, 253, size=N_PLACEBO)
    for k in shifts:
        s_shift = np.roll(sig, int(k))
        ptr, _, _ = execute(ho, s_shift, settle_map)
        if len(ptr):
            placebo_means.append(float(ptr.net.mean()))
            placebo_ns.append(int(len(ptr)))
    placebo_means = np.array(placebo_means)
    placebo_pct = float(np.mean(placebo_means < primary["mean"]) * 100.0) if n else None
    holdout_log.append("placebo: 1000 circular shifts of the signal series, shift ~ U{63..252} trading days")

    primary_metrics = {
        **primary,
        "trade_bootstrap": {
            "lower_99_one_sided": float(np.percentile(boot_means, 1.0)) if n else None,
            "ci95": ci95(boot_means),
            "p_one_sided_mean_le_0": float(np.mean(boot_means <= 0.0)) if n else None,
        },
        "block_bootstrap_63d": {
            "lower_99_one_sided": float(np.percentile(block_means, 1.0)) if n else None,
            "ci95": ci95(block_means),
            "p_one_sided_mean_le_0": float(np.mean(block_means <= 0.0)) if n else None,
        },
        "block_bootstrap_21d_ci95": {
            "mean_per_trade_daily_series": ci95(block21_means),
            "mean_per_trade_trade_blocks": ci95(tb["mean"]),
            "hit_rate": ci95(tb["hit_rate"]),
            "median": ci95(tb["median"]),
            "sortino_mar0": ci95(tb["sortino"]),
            "mean_ex_best": ci95(tb["mean_ex_best"]),
        },
        "placebo": {
            "n_shifts": int(N_PLACEBO),
            "n_valid": int(len(placebo_means)),
            "real_mean_percentile": placebo_pct,
            "placebo_mean_of_means": float(np.mean(placebo_means)) if len(placebo_means) else None,
            "placebo_p50": float(np.percentile(placebo_means, 50)) if len(placebo_means) else None,
            "placebo_p99": float(np.percentile(placebo_means, 99)) if len(placebo_means) else None,
            "placebo_n_trades_mean": float(np.mean(placebo_ns)) if placebo_ns else None,
        },
        "max_drawdown_63d_window": max_drawdown_windowed(daily, BLOCK_PRIMARY),
        "max_drawdown_full": max_drawdown(daily),
        "daily_series_days": int(ho_days),
        "daily_series_total": float(daily.sum()),
    }

    # pass / kill evaluation (registered thresholds)
    cond = {
        "1_n_ge_30": bool(n >= 30),
        "2a_trade_boot_lb99_gt_0": bool(n and primary_metrics["trade_bootstrap"]["lower_99_one_sided"] > 0),
        "2b_block_boot_lb99_gt_0": bool(n and primary_metrics["block_bootstrap_63d"]["lower_99_one_sided"] > 0),
        "3_mean_ex_best_gt_0": bool(n and primary["mean_ex_best"] is not None and primary["mean_ex_best"] > 0),
        "4_real_mean_ge_placebo_p99": bool(n and placebo_pct is not None and placebo_pct >= 99.0),
    }
    passed = all(cond.values())
    kill = {
        "a_mean_le_0_10": bool(n and primary["mean"] <= 0.10),
        "b_mean_ex_best_le_0": bool(n and primary["mean_ex_best"] is not None and primary["mean_ex_best"] <= 0),
        "c_real_mean_le_placebo_p50": bool(n and placebo_pct is not None and placebo_pct <= 50.0),
    }
    killed = any(kill.values()) and not passed
    verdict = "PASS" if passed else ("KILL" if killed else "INCONCLUSIVE")
    if n < 30:
        verdict = "INCONCLUSIVE (n < 30)" if not killed else "KILL"

    # ---------------- appendix (after the primary result is recorded) ----------------
    appendix = {}

    def cell(tr, notes=None):
        return summarize_cell(tr, ho_days, notes or {})

    appendix["short_leg"] = cell(trades[trades.dir == -1])
    appendix["long_leg"] = cell(trades[trades.dir == 1])
    holdout_log.append("appendix: short and long legs")

    g_tr, g_daily, _, g_notes = run_cell(ho, theta_star, settle_map, gate=True)
    g_cell = cell(g_tr, g_notes)
    ng = len(g_tr)
    diffs = []
    if n and ng:
        for _ in range(N_BOOT):
            idx = circular_block_indices(rng, ho_days, BLOCK_PRIMARY)
            diffs.append(g_daily[idx].sum() / ng - daily[idx].sum() / n)
    appendix["gated_no_short_in_high_vol"] = {
        **g_cell,
        "paired_diff_gated_minus_ungated_mean_per_trade": (g_cell["mean"] - primary["mean"]) if (n and ng) else None,
        "paired_diff_ci95_block63": ci95(diffs),
        "shorts_removed_by_gate": int(n - ng) if n and ng else None,
    }
    holdout_log.append("appendix: gated variant and paired block-bootstrap difference")

    l0_tr, _, _, l0_notes = run_cell(ho, theta_star, settle_map, lag=0)
    appendix["lag_0_entry"] = cell(l0_tr, l0_notes)
    holdout_log.append("appendix: lag-0 entry")

    c20_tr, _, _, c20_notes = run_cell(ho, theta_star, settle_map, cost=0.20)
    appendix["cost_0_20"] = cell(c20_tr, c20_notes)
    holdout_log.append("appendix: cost 0.20")

    # always-short benchmark: short the held contract at t, cover at t+5, every day (overlapping)
    # and the same as consecutive non-overlapping 5-day trades
    dates = ho.trade_date.values
    c_exp = ho.c_expiry.values
    bench = []
    for i in range(ho_days - HOLD):
        exp = pd.Timestamp(c_exp[i])
        s0 = settle_map.get((pd.Timestamp(dates[i]), exp), np.nan)
        s1 = settle_map.get((pd.Timestamp(dates[i + HOLD]), exp), np.nan)
        if np.isnan(s0) or np.isnan(s1) or ho.c_td.values[i] < HOLD:
            bench.append(np.nan)
        else:
            bench.append(-(s1 - s0) - COST)
    bench = np.array(bench)
    valid = bench[~np.isnan(bench)]
    nonov = bench[::HOLD]
    nonov = nonov[~np.isnan(nonov)]
    appendix["always_short_benchmark"] = {
        "overlapping_all_days": trade_stats(valid),
        "non_overlapping_consecutive_5d": trade_stats(nonov),
    }
    holdout_log.append("appendix: always-short-front-contract benchmark")

    u_tr, _, _, u_notes = run_cell(ho, 0.0, settle_map, short_only=True)
    appendix["unconditional_contango_short_theta0"] = cell(u_tr, u_notes)
    holdout_log.append("appendix: unconditional contango-short (theta=0, basis>0)")

    appendix["all_grid_cells_holdout"] = {}
    for th in GRID:
        tr_th, _, _, notes_th = run_cell(ho, th, settle_map)
        appendix["all_grid_cells_holdout"][f"{th:.3f}"] = cell(tr_th, notes_th)
    holdout_log.append("appendix: all 5 grid cells on the holdout")

    per_year = {}
    if n:
        for y, grp in trades.groupby(trades.signal_date.dt.year):
            per_year[int(y)] = trade_stats(grp.net.values)
    appendix["per_calendar_year"] = per_year

    excl = {}
    for name, (a, b) in EXCL_WINDOWS.items():
        if n:
            overlap = (trades.entry_date <= b) & (trades.exit_date >= a)
            excl[name] = {**trade_stats(trades[~overlap].net.values), "trades_removed": int(overlap.sum()),
                          "removed_trades_total_net": float(trades[overlap].net.sum())}
    appendix["exclusions"] = excl
    holdout_log.append("appendix: per-year and exclusion windows")

    # VX30 21-trading-day forward change on signal dates vs all dates (descriptive)
    vx30 = ho.vx30.values
    fwd = np.full(ho_days, np.nan)
    fwd[: ho_days - 21] = vx30[21:] - vx30[:-21]
    ok = ~np.isnan(fwd)
    def desc(mask):
        x = fwd[mask & ok]
        return {"n": int(len(x)), "mean": float(np.mean(x)) if len(x) else None,
                "median": float(np.median(x)) if len(x) else None,
                "mean_abs": float(np.mean(np.abs(x))) if len(x) else None}
    appendix["vx30_21d_forward_change"] = {
        "all_dates": desc(np.ones(ho_days, dtype=bool)),
        "short_signal_dates": desc(sig == -1),
        "long_signal_dates": desc(sig == 1),
    }
    holdout_log.append("appendix: VX30 21-day forward change on signal dates vs all dates")

    # sign-run episodes and exposure
    def runs(mask):
        m = mask.astype(int)
        return int(((np.diff(np.concatenate([[0], m])) == 1)).sum())
    appendix["signal_structure_holdout"] = {
        "short_signal_days": int((sig == -1).sum()),
        "long_signal_days": int((sig == 1).sum()),
        "short_episodes": runs(sig == -1),
        "long_episodes": runs(sig == 1),
        "exposure_share": primary["days_in_market_share"],
        "trades_per_year": n / (ho_days / 252.0) if ho_days else None,
        "basis_f1_minus_vix_on_signal_dates": {
            "short": {"mean": float(ho.basis_f1.values[sig == -1].mean()) if (sig == -1).any() else None},
            "long": {"mean": float(ho.basis_f1.values[sig == 1].mean()) if (sig == 1).any() else None},
        },
        "slope_f2_minus_f1_on_signal_dates": {
            "short": {"mean": float(ho.slope_f2_f1.values[sig == -1].mean()) if (sig == -1).any() else None},
            "long": {"mean": float(ho.slope_f2_f1.values[sig == 1].mean()) if (sig == 1).any() else None},
        },
        "regime_at_signal_counts": trades.regime_at_signal.value_counts(dropna=False).to_dict() if n else {},
    }
    holdout_log.append("appendix: sign-run episodes, exposure, basis and slope on signal dates")

    result = {
        "design": "vx-roll-carry-5d",
        "script": "scripts/research/basis_test.py",
        "run_started_utc": started,
        "run_finished_utc": now_utc(),
        "seed": args.seed,
        "data_checks": checks,
        "vix_crosscheck": xcheck,
        "fit": fit_record,
        "attempts_in_fit_period": len(GRID),
        "holdout": {
            "window": {"signal_start": str(HOLDOUT_START)[:10], "exit_end": str(HOLDOUT_END)[:10],
                       "calendar_days": ho_days,
                       "first_signal_date_possible": str(ho.trade_date.iloc[0])[:10],
                       "last_signal_date_with_valid_exit": str(ho.trade_date.iloc[ho_days - 1 - LAG - HOLD])[:10],
                       "last_signal_date_with_defined_roll": str(ho.trade_date[ho.roll.notna()].iloc[-1])[:10]},
            "theta": theta_star,
            "primary": primary_metrics,
            "pass_conditions": cond,
            "kill_conditions": kill,
            "passed": passed,
            "verdict": verdict,
            "trades": trades_to_records(trades),
        },
        "appendix": appendix,
        "holdout_computation_log": holdout_log,
        "fit_trades_selected_theta": trades_to_records(fit_trades_by_theta[theta_star]),
    }
    with open(args.out, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(jsonable(result), fh, indent=1)
        fh.write("\n")

    print(f"selected theta {theta_star}; holdout n={n} mean={primary['mean']:.4f} verdict={verdict}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
