"""Pre-registered test runner for design 2 "catalyst-vx30-crush" (catalyst).

Registration: doc/research/preregistration-2026-09-27.md, section "Design 2".

The script implements the registered design exactly:

- events: scheduled FOMC decision days (fomc_dates.csv, kind = meeting,
  scheduled = True, status = verified_statement) and verified CPI release days
  (cpi_dates.csv, best_release_date, verification containing
  verified_bls_and_fred); an event must fall on a VX trading day (vx_curve.csv
  row) or it is dropped and listed; same-day FOMC + CPI counts once, tagged both;
- target: VX30 settle change in points from t-1 to t+j (post leg, SHORT) and
  from t-k to t-1 (pre leg, LONG), plus the strictly tradable check on the
  single monthly contract nearest to expiry among those expiring strictly more
  than 7 calendar days after the window's last day, gross and net of a 0.10
  point round-trip cost;
- placebo: for each event, the same window shifted by 6 to 15 trading days in
  either direction, excluding shifts whose window touches [e-3, e+3] of any
  scheduled FOMC/CPI event; excess = mean(event change) - mean(placebo change);
  placebo z and one-sided p from 10,000 random one-offset-per-event draws;
- fit: 2011-01-01 to 2018-12-31 only; j* = most negative post-leg z on
  {0, 1, 2, 3}; k* = most positive pre-leg z on {1, 2, 3, 5};
- holdout: ONE run on events from 2019-01-01 onward at the frozen j*, k*, with
  every registered metric, robustness variant and an attempt log.

Two phases keep the fit and the holdout apart: `--phase fit` writes the fit
table, j*, k*, SHA-256 hashes of the inputs and a timestamp to the results file
and computes nothing on 2019+ data; `--phase holdout` reloads that file,
checks that the recomputed fit is identical, then runs the holdout once and
writes the full results. Data access is read-only. All paths are arguments.

Usage:
    python catalyst_test.py --data-dir <dir> --out <results.json> --phase fit
        [--md <section.md>] [--seed 20260927]
    python catalyst_test.py --data-dir <dir> --out <results.json>
        --phase holdout [--md <section.md>] [--seed 20260927]
"""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import math
import os
import sys

import numpy as np
import pandas as pd

DESIGN = "catalyst-vx30-crush"
GRID_J = [0, 1, 2, 3]
GRID_K = [1, 2, 3, 5]
OFFSETS = list(range(-15, -5)) + list(range(6, 16))
EXCL_HALO = 3
SURVIVAL_DAYS = 7
COST = 0.10
N_DRAWS = 10000
N_BOOT = 10000
N_PERM = 10000
BLOCK_TRADING_DAYS = 21
M_BONFERRONI = 24
ALPHA_ADJ = 0.05 / M_BONFERRONI
FIT_START = pd.Timestamp("2011-01-01")
FIT_END = pd.Timestamp("2018-12-31")
HOLD_START = pd.Timestamp("2019-01-01")
DATA_FILES = ["vx_curve.csv", "vx_futures.csv", "fomc_dates.csv",
              "cpi_dates.csv", "decisions.csv", "vix.csv"]

# Registered pass / kill thresholds (points, probabilities).
PASS_EXCESS = 0.15
PASS_TRADABLE = 0.20
KILL_TRADABLE = 0.10
KILL_P = 0.10
H3_RATIO = 1.25
MIN_N = 30


# ----------------------------------------------------------------------------
# helpers
# ----------------------------------------------------------------------------

def sha256(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def r(x, nd=4):
    """Round for JSON; NaN -> None."""
    if x is None:
        return None
    if isinstance(x, (np.floating, float)):
        if math.isnan(float(x)):
            return None
        return round(float(x), nd)
    if isinstance(x, (np.integer,)):
        return int(x)
    return x


def fmt(x, nd=3):
    if x is None or (isinstance(x, float) and math.isnan(x)):
        return "n/a"
    return f"{x:.{nd}f}"


def fmtp(p):
    if p is None or (isinstance(p, float) and math.isnan(p)):
        return "n/a"
    return f"{p:.4f}"


# ----------------------------------------------------------------------------
# data
# ----------------------------------------------------------------------------

class Data:
    def __init__(self, data_dir: str):
        self.data_dir = data_dir
        cur = pd.read_csv(os.path.join(data_dir, "vx_curve.csv"),
                          parse_dates=["trade_date", "f1_expiry", "f2_expiry"])
        cur = cur.sort_values("trade_date").reset_index(drop=True)
        if cur.trade_date.duplicated().any():
            raise SystemExit("vx_curve.csv has duplicate trade dates")
        self.cur = cur
        self.cal = cur.trade_date.to_numpy().astype("datetime64[D]")
        self.pos = {d: i for i, d in enumerate(cur.trade_date)}
        self.vx30 = cur.vx30.to_numpy(dtype=float)
        self.method = cur.vx30_method.to_numpy()
        self.f1_days = cur.f1_days.to_numpy(dtype=float)
        self.basis = cur.f1_minus_vix.to_numpy(dtype=float)
        self.n = len(cur)

        fut = pd.read_csv(os.path.join(data_dir, "vx_futures.csv"),
                          parse_dates=["trade_date", "expiry"])
        fut = fut[fut.contract_type == "M"]
        self.settle = {(t.to_datetime64().astype("datetime64[D]"),
                        e.to_datetime64().astype("datetime64[D]")): float(s)
                       for t, e, s in zip(fut.trade_date, fut.expiry, fut.settle)}
        self.expiries = np.array(sorted(fut.expiry.unique())).astype("datetime64[D]")

        fomc = pd.read_csv(os.path.join(data_dir, "fomc_dates.csv"),
                           parse_dates=["decision_date"])
        fomc = fomc[(fomc.kind == "meeting")
                    & (fomc.scheduled.astype(str) == "True")
                    & (fomc.status == "verified_statement")]
        self.fomc_dates = sorted(set(fomc.decision_date))

        cpi = pd.read_csv(os.path.join(data_dir, "cpi_dates.csv"),
                          parse_dates=["best_release_date"])
        cpi = cpi[cpi.verification.astype(str).str.contains("verified_bls_and_fred")]
        self.cpi_dates = sorted(set(cpi.best_release_date))
        resched = cpi.earlier_scheduled_dates.notna() & (
            cpi.earlier_scheduled_dates.astype(str).str.strip() != "")
        self.cpi_rescheduled = set(cpi.best_release_date[resched])

        dec = pd.read_csv(os.path.join(data_dir, "decisions.csv"), parse_dates=["date"])
        dec = dec[dec.context == "equity"].sort_values("date")
        self.dec_dates = dec.date.to_numpy().astype("datetime64[D]")
        self.dec_regime = dec.regime.to_numpy()

        self.hashes = {f: sha256(os.path.join(data_dir, f)) for f in DATA_FILES}

    # -- events ---------------------------------------------------------------
    def build_events(self):
        """All scheduled events, pooled; returns (events DataFrame, dropped list)."""
        rows = {}
        for d in self.fomc_dates:
            rows.setdefault(d, {"fomc": False, "cpi": False})["fomc"] = True
        for d in self.cpi_dates:
            rows.setdefault(d, {"fomc": False, "cpi": False})["cpi"] = True
        recs, dropped = [], []
        for d in sorted(rows):
            tag = "both" if rows[d]["fomc"] and rows[d]["cpi"] else (
                "fomc" if rows[d]["fomc"] else "cpi")
            if d not in self.pos:
                dropped.append({"date": str(d.date()), "tag": tag,
                                "reason": "no VX settlement on event day"})
                continue
            recs.append({"date": d, "pos": self.pos[d], "tag": tag,
                         "is_fomc": rows[d]["fomc"], "is_cpi": rows[d]["cpi"],
                         "cpi_rescheduled": rows[d]["cpi"] and d in self.cpi_rescheduled,
                         "year": d.year})
        ev = pd.DataFrame(recs)
        # halo positions for placebo exclusion: every scheduled event, on the
        # calendar or not (a non-trading event date maps to the next VX day).
        near = np.zeros(self.n, dtype=bool)
        for d in sorted(rows):
            q = int(np.searchsorted(self.cal, np.datetime64(d, "D")))
            lo, hi = max(0, q - EXCL_HALO), min(self.n - 1, q + EXCL_HALO)
            near[lo:hi + 1] = True
        self.near = near
        self.near_cum = np.concatenate([[0], np.cumsum(near)])
        return ev, dropped

    def window_touches_event(self, a: int, b: int) -> bool:
        return (self.near_cum[b + 1] - self.near_cum[a]) > 0

    def regime_at(self, pos: int):
        d = self.cal[pos]
        i = int(np.searchsorted(self.dec_dates, d, side="right")) - 1
        if i < 0:
            return None, True
        return str(self.dec_regime[i]), bool(self.dec_dates[i] != d)

    def surviving_contract(self, a: int, b: int):
        """Nearest monthly expiry strictly more than SURVIVAL_DAYS after cal[b];
        returns (settle change, expiry, calendar days to expiry at entry) or NaNs."""
        last = self.cal[b]
        cands = self.expiries[self.expiries > last + np.timedelta64(SURVIVAL_DAYS, "D")]
        if len(cands) == 0:
            return math.nan, None, math.nan
        e = cands[0]
        s0 = self.settle.get((self.cal[a], e))
        s1 = self.settle.get((self.cal[b], e))
        if s0 is None or s1 is None:
            return math.nan, str(e), math.nan
        return s1 - s0, str(e), float((e - self.cal[a]).astype(int))


# ----------------------------------------------------------------------------
# windows and placebo
# ----------------------------------------------------------------------------

def window_bounds(pos: int, leg: str, w: int):
    if leg == "post":
        return pos - 1, pos + w
    return pos - w, pos - 1


def event_changes(data: Data, ev: pd.DataFrame, leg: str, w: int, limit_end=None):
    """Per-event VX30 change, log change and placebo matrices for one leg.

    Returns dict with arrays aligned to ev rows (NaN where undefined) and
    placebo matrices P (points) and PL (log) of shape (n_events, len(OFFSETS)).
    limit_end: last calendar position a window may use (fit phase)."""
    n = len(ev)
    x = np.full(n, np.nan)
    xl = np.full(n, np.nan)
    P = np.full((n, len(OFFSETS)), np.nan)
    PL = np.full((n, len(OFFSETS)), np.nan)
    trad = np.full(n, np.nan)
    trad_exp = [None] * n
    trad_days = np.full(n, np.nan)
    extrap = np.zeros(n, dtype=bool)
    regime = [None] * n
    regime_lag = np.zeros(n, dtype=bool)
    entry_basis = np.full(n, np.nan)
    entry_f1_days = np.full(n, np.nan)
    hi = data.n - 1 if limit_end is None else limit_end
    for i, pos in enumerate(ev.pos.to_numpy()):
        a, b = window_bounds(int(pos), leg, w)
        if a < 0 or b > hi:
            continue
        x[i] = data.vx30[b] - data.vx30[a]
        xl[i] = math.log(data.vx30[b]) - math.log(data.vx30[a])
        extrap[i] = (data.method[a] != "interp_F1F2" and data.method[a].startswith("extrap")) or (
            data.method[b].startswith("extrap"))
        trad[i], trad_exp[i], trad_days[i] = data.surviving_contract(a, b)
        regime[i], regime_lag[i] = data.regime_at(a)
        entry_basis[i] = data.basis[a]
        entry_f1_days[i] = data.f1_days[a]
        for k, o in enumerate(OFFSETS):
            aa, bb = a + o, b + o
            if aa < 0 or bb > hi:
                continue
            if data.window_touches_event(aa, bb):
                continue
            P[i, k] = data.vx30[bb] - data.vx30[aa]
            PL[i, k] = math.log(data.vx30[bb]) - math.log(data.vx30[aa])
    return {"x": x, "xl": xl, "P": P, "PL": PL, "trad": trad, "trad_exp": trad_exp,
            "trad_days": trad_days, "extrap": extrap, "regime": np.array(regime, dtype=object),
            "regime_lag": regime_lag, "entry_basis": entry_basis,
            "entry_f1_days": entry_f1_days}


def placebo_draws(P: np.ndarray, rng: np.random.Generator, n_draws: int) -> np.ndarray:
    """One random valid offset per event per draw; returns (n_draws, n_events)."""
    valid = ~np.isnan(P)
    counts = valid.sum(1)
    if (counts == 0).any():
        raise ValueError("event without valid placebo offsets reached placebo_draws")
    order = np.argsort(~valid, axis=1, kind="stable")
    Pc = np.take_along_axis(P, order, axis=1)
    idx = (rng.random((n_draws, P.shape[0])) * counts[None, :]).astype(int)
    return Pc[np.arange(P.shape[0])[None, :], idx]


def block_len(n_events: int, n_days: int) -> int:
    if n_events == 0:
        return 1
    return max(2, int(round(BLOCK_TRADING_DAYS * n_events / max(n_days, 1))))


def block_boot_mean(e: np.ndarray, L: int, rng: np.random.Generator, n_boot: int):
    """Circular block bootstrap of the mean of a date-ordered series."""
    n = len(e)
    if n == 0:
        return math.nan, math.nan
    nb = int(math.ceil(n / L))
    starts = rng.integers(0, n, (n_boot, nb))
    idx = (starts[:, :, None] + np.arange(L)[None, None, :]) % n
    idx = idx.reshape(n_boot, -1)[:, :n]
    means = e[idx].mean(1)
    return float(np.percentile(means, 2.5)), float(np.percentile(means, 97.5))


def leg_summary(x, P, sign, rng, n_days, log=None, label=None, n_draws=N_DRAWS,
                n_boot=N_BOOT):
    """Registered statistics for one directional leg.

    sign = -1: hypothesised negative change (H1 family); +1: positive (H2)."""
    keep = ~np.isnan(x) & (~np.isnan(P)).any(1)
    dropped = int((~np.isnan(x)).sum() - keep.sum())
    x = x[keep]
    P = P[keep]
    n = len(x)
    out = {"n": n, "n_dropped_no_placebo": dropped}
    if label is not None and log is not None:
        log.append(label)
    if n == 0:
        return out
    pm = np.nanmean(P, 1)
    draws = placebo_draws(P, rng, n_draws)
    D = draws.mean(1)
    mean_x = float(x.mean())
    excess = mean_x - float(pm.mean())
    sd = float(D.std(ddof=1))
    z = excess / sd if sd > 0 else math.nan
    if sign < 0:
        p = (1 + int((D <= mean_x).sum())) / (n_draws + 1)
    else:
        p = (1 + int((D >= mean_x).sum())) / (n_draws + 1)
    e = x - pm
    idx = rng.integers(0, n, (n_boot, n))
    B = e[idx].mean(1)
    ci = (float(np.percentile(B, 2.5)), float(np.percentile(B, 97.5)))
    Bx = x[idx].mean(1)
    ci_mean = (float(np.percentile(Bx, 2.5)), float(np.percentile(Bx, 97.5)))
    L = block_len(n, n_days)
    bci = block_boot_mean(e, L, rng, n_boot)
    hit = float((sign * x > 0).mean())
    ph = np.array([np.mean(sign * P[i][~np.isnan(P[i])] > 0) for i in range(n)])
    out.update({
        "mean": mean_x, "median": float(np.median(x)), "sd": float(x.std(ddof=1)) if n > 1 else math.nan,
        "placebo_mean": float(pm.mean()), "excess": excess, "placebo_sd_of_mean": sd,
        "z": z, "p_one_sided": p,
        "excess_ci95_iid": ci, "excess_ci95_block": bci, "block_len_events": L,
        "mean_ci95_iid": ci_mean,
        "hit_rate": hit, "placebo_hit_rate": float(ph.mean()),
        "mean_valid_offsets": float((~np.isnan(P)).sum(1).mean()),
    })
    return out


def magnitude_summary(x, P, rng, log=None, label=None, n_draws=N_DRAWS, n_boot=N_BOOT):
    keep = ~np.isnan(x) & (~np.isnan(P)).any(1)
    x = x[keep]
    P = P[keep]
    n = len(x)
    if label is not None and log is not None:
        log.append(label)
    if n == 0:
        return {"n": 0}
    ax = np.abs(x)
    med = float(np.median(ax))
    mean = float(ax.mean())
    draws = np.abs(placebo_draws(P, rng, n_draws))
    Dm = np.median(draws, 1)
    Dmean = draws.mean(1)
    placebo_med = float(Dm.mean())
    placebo_mean = float(Dmean.mean())
    pooled_med = float(np.nanmedian(np.abs(P)))
    p_med = (1 + int((Dm >= med).sum())) / (n_draws + 1)
    p_mean = (1 + int((Dmean >= mean).sum())) / (n_draws + 1)
    idx = rng.integers(0, n, (n_boot, n))
    ratios = np.median(ax[idx], 1) / placebo_med
    return {"n": n, "event_median_abs": med, "event_mean_abs": mean,
            "placebo_median_abs": placebo_med, "placebo_median_abs_pooled": pooled_med,
            "placebo_mean_abs": placebo_mean,
            "ratio_median": med / placebo_med if placebo_med > 0 else math.nan,
            "ratio_mean": mean / placebo_mean if placebo_mean > 0 else math.nan,
            "p_one_sided_median": p_med, "p_one_sided_mean": p_mean,
            "ratio_median_ci95_iid": (float(np.percentile(ratios, 2.5)),
                                      float(np.percentile(ratios, 97.5)))}


def tradable_summary(t, sign):
    """Strictly tradable surviving-contract statistics; sign -1 = short leg."""
    t = t[~np.isnan(t)]
    n = len(t)
    if n == 0:
        return {"n": 0}
    pnl_gross = sign * t
    pnl_net = pnl_gross - COST
    dd = float(np.sqrt(np.mean(np.minimum(pnl_net, 0.0) ** 2)))
    return {"n": n, "mean_change": float(t.mean()), "median_change": float(np.median(t)),
            "mean_pnl_gross": float(pnl_gross.mean()), "mean_pnl_net": float(pnl_net.mean()),
            "median_pnl_net": float(np.median(pnl_net)),
            "sortino_net": float(pnl_net.mean() / dd) if dd > 0 else math.nan,
            "downside_dev_net": dd, "worst_pnl_net": float(pnl_net.min()),
            "hit_rate_net": float((pnl_net > 0).mean()),
            "median_abs_change": float(np.median(np.abs(t)))}


# ----------------------------------------------------------------------------
# fit
# ----------------------------------------------------------------------------

def run_fit(data: Data, ev_all: pd.DataFrame, seed: int):
    rng = np.random.default_rng(seed)
    fit_ev = ev_all[(ev_all.date >= FIT_START) & (ev_all.date <= FIT_END)].reset_index(drop=True)
    fit_end_pos = data.pos[max(d for d in data.cur.trade_date if d <= FIT_END)]
    n_days = int(((data.cur.trade_date >= FIT_START) & (data.cur.trade_date <= FIT_END)).sum())
    counts = {"n_events": int(len(fit_ev)), "n_fomc": int(fit_ev.is_fomc.sum()),
              "n_cpi": int(fit_ev.is_cpi.sum()), "n_both": int((fit_ev.tag == "both").sum()),
              "n_trading_days": n_days}
    post, pre = {}, {}
    for j in GRID_J:
        w = event_changes(data, fit_ev, "post", j, limit_end=fit_end_pos)
        s = leg_summary(w["x"], w["P"], -1, rng, n_days)
        s["tradable"] = tradable_summary(w["trad"], -1)
        post[str(j)] = s
    for k in GRID_K:
        w = event_changes(data, fit_ev, "pre", k, limit_end=fit_end_pos)
        s = leg_summary(w["x"], w["P"], +1, rng, n_days)
        s["tradable"] = tradable_summary(w["trad"], +1)
        pre[str(k)] = s
    # A cell whose z is undefined (zero-length window: k = 1 gives t-1 to t-1,
    # excess identically 0, placebo sd 0) is counted as an attempt but cannot be
    # "the most positive z"; it is listed and excluded from the argmax.
    undefined = {"post": [j for j in GRID_J if not np.isfinite(post[str(j)]["z"])],
                 "pre": [k for k in GRID_K if not np.isfinite(pre[str(k)]["z"])]}
    j_cells = [j for j in GRID_J if j not in undefined["post"]]
    k_cells = [k for k in GRID_K if k not in undefined["pre"]]
    j_star = min(j_cells, key=lambda j: post[str(j)]["z"])
    k_star = max(k_cells, key=lambda k: pre[str(k)]["z"])
    return {"window": [str(FIT_START.date()), str(FIT_END.date())], "counts": counts,
            "post": post, "pre": pre, "j_star": j_star, "k_star": k_star,
            "undefined_cells": undefined,
            "selection_rule": "j* = most negative placebo z over GRID_J; "
                              "k* = most positive placebo z over GRID_K; cells with an "
                              "undefined z are counted as attempts but excluded from the argmax",
            "placebo_windows_restricted_to_fit_period": True}


# ----------------------------------------------------------------------------
# holdout
# ----------------------------------------------------------------------------

def verdict_directional(s, trad, s_top1, s_top3, sign, pooled=True):
    """Apply the registered pass / kill rules for an H1-type (sign -1) or
    H2-type (sign +1) leg. Returns dict with verdict and per-condition flags."""
    if s.get("n", 0) < MIN_N:
        return {"verdict": "INCONCLUSIVE", "reason": "n < 30 by design"}
    ex = s["excess"]
    p = s["p_one_sided"]
    tm = trad.get("mean_change", math.nan)
    ex1 = s_top1.get("excess", math.nan)
    ex3 = s_top3.get("excess", math.nan)
    if sign < 0:
        pass_flags = {"a_excess_le_-0.15": ex <= -PASS_EXCESS,
                      "b_p_le_alpha": p <= ALPHA_ADJ,
                      "c_tradable_mean_le_-0.20": tm <= -PASS_TRADABLE,
                      "d_sign_kept_ex_top3": (ex3 < 0) and (ex < 0)}
        kill_flags = {"a_wrong_sign": ex >= 0,
                      "b_p_gt_0.10": p > KILL_P,
                      "c_sign_flips_ex_top1": (ex1 >= 0) != (ex >= 0),
                      "d_tradable_mean_gt_-0.10": tm > -KILL_TRADABLE}
    else:
        pass_flags = {"a_excess_ge_+0.15": ex >= PASS_EXCESS,
                      "b_p_le_alpha": p <= ALPHA_ADJ,
                      "c_tradable_mean_ge_+0.20": tm >= PASS_TRADABLE,
                      "d_sign_kept_ex_top3": (ex3 > 0) and (ex > 0)}
        kill_flags = {"a_wrong_sign": ex <= 0,
                      "b_p_gt_0.10": p > KILL_P,
                      "c_sign_flips_ex_top1": (ex1 <= 0) != (ex <= 0),
                      "d_tradable_mean_lt_+0.10": tm < KILL_TRADABLE}
    if math.isnan(tm):
        pass_flags[list(pass_flags)[2]] = False
        kill_flags[list(kill_flags)[3]] = True
    verdict = "PASS" if all(pass_flags.values()) else (
        "KILL" if any(kill_flags.values()) else "INCONCLUSIVE")
    return {"verdict": verdict, "pass_conditions": pass_flags, "kill_conditions": kill_flags}


def exclude_top(w, k):
    """Return a mask that removes the k events with the largest |VX30 change|."""
    x = w["x"]
    order = np.argsort(-np.abs(np.nan_to_num(x, nan=-np.inf)))
    mask = np.ones(len(x), dtype=bool)
    mask[order[:k]] = False
    return mask


def sub(w, mask):
    return {key: (val[mask] if isinstance(val, np.ndarray) else
                  [v for v, m in zip(val, mask) if m]) for key, val in w.items()}


def per_year_table(ev, w, sign):
    rows = []
    for y in sorted(ev.year.unique()):
        m = (ev.year == y).to_numpy() & ~np.isnan(w["x"])
        x = w["x"][m]
        pm = np.nanmean(w["P"][m], 1) if m.any() else np.array([])
        t = w["trad"][m]
        t = t[~np.isnan(t)]
        rows.append({"year": int(y), "n": int(m.sum()),
                     "mean_vx30_change": r(x.mean()) if len(x) else None,
                     "excess": r(float(x.mean() - np.nanmean(pm))) if len(x) else None,
                     "hit_rate": r(float((sign * x > 0).mean())) if len(x) else None,
                     "tradable_n": int(len(t)),
                     "tradable_mean_change": r(t.mean()) if len(t) else None,
                     "tradable_mean_pnl_net": r(float((sign * t - COST).mean())) if len(t) else None})
    return rows


def run_holdout(data: Data, ev_all: pd.DataFrame, fit: dict, seed: int):
    log = []
    rng = np.random.default_rng(seed + 1)
    j, k = fit["j_star"], fit["k_star"]
    last_pos = data.n - 1
    hold = ev_all[ev_all.date >= HOLD_START].reset_index(drop=True)
    # events whose post window is incomplete at the end of the data are dropped and listed
    incomplete = hold[hold.pos + j > last_pos]
    hold = hold[hold.pos + j <= last_pos].reset_index(drop=True)
    n_days = int((data.cur.trade_date >= HOLD_START).sum())
    counts = {"n_events": int(len(hold)), "n_fomc": int(hold.is_fomc.sum()),
              "n_cpi": int(hold.is_cpi.sum()), "n_both": int((hold.tag == "both").sum()),
              "n_trading_days": n_days,
              "first_event": str(hold.date.min().date()), "last_event": str(hold.date.max().date()),
              "dropped_incomplete_window": [str(d.date()) for d in incomplete.date]}
    res = {"j_star": j, "k_star": k, "counts": counts, "attempt_log": log}

    post = event_changes(data, hold, "post", j)
    pre = event_changes(data, hold, "pre", k)
    res["data_flags"] = {
        "post_regime_taken_from_earlier_decision": [str(d.date()) for d, f in
                                                    zip(hold.date, post["regime_lag"]) if f],
        "post_regime_missing": [str(d.date()) for d, g in zip(hold.date, post["regime"]) if g is None],
        "post_tradable_missing": [str(d.date()) for d, t in zip(hold.date, post["trad"]) if math.isnan(t)],
        "pre_tradable_missing": [str(d.date()) for d, t in zip(hold.date, pre["trad"]) if math.isnan(t)],
        "post_extrapolated_vx30_events": int(post["extrap"].sum()),
        "pre_undefined_window": [str(d.date()) for d, x in zip(hold.date, pre["x"]) if math.isnan(x)],
        "events_without_placebo_post": [str(d.date()) for d, x, P in
                                        zip(hold.date, post["x"], post["P"])
                                        if not math.isnan(x) and np.isnan(P).all()],
        "events_without_placebo_pre": [str(d.date()) for d, x, P in
                                       zip(hold.date, pre["x"], pre["P"])
                                       if not math.isnan(x) and np.isnan(P).all()],
    }

    # ---- H1 pooled post leg -------------------------------------------------
    h1 = leg_summary(post["x"], post["P"], -1, rng, n_days, log, "H1 pooled post VX30 change, j*")
    h1_trad = tradable_summary(post["trad"], -1)
    log.append("H1 tradable surviving-contract change (gross, net 0.10)")
    m1 = exclude_top(post, 1)
    m3 = exclude_top(post, 3)
    h1_ex1 = leg_summary(post["x"][m1], post["P"][m1], -1, rng, n_days, log, "H1 excluding top-1 |change|")
    h1_ex3 = leg_summary(post["x"][m3], post["P"][m3], -1, rng, n_days, log, "H1 excluding top-3 |change|")
    top = [{"date": str(hold.date[i].date()), "tag": hold.tag[i], "vx30_change": r(post["x"][i])}
           for i in np.argsort(-np.abs(np.nan_to_num(post["x"], nan=-np.inf)))[:3]]
    mx = ~post["extrap"]
    h1_noext = leg_summary(post["x"][mx], post["P"][mx], -1, rng, n_days, log,
                           "H1 excluding events with extrapolated VX30 at entry or exit")
    h1_log = leg_summary(post["xl"], post["PL"], -1, rng, n_days, log, "H1 in log points")
    mr = ~hold.cpi_rescheduled.to_numpy()
    h1_never = leg_summary(post["x"][mr], post["P"][mr], -1, rng, n_days, log,
                           "H1 restricted to events never rescheduled")
    # tradable breakdowns
    fd = post["entry_f1_days"]
    t = post["trad"]
    by_days = {}
    for name, m in [("<=10", fd <= 10), ("11-20", (fd > 10) & (fd <= 20)), (">20", fd > 20)]:
        by_days[name] = tradable_summary(t[m], -1)
        by_days[name]["n_events"] = int(m.sum())
    log.append("H1 tradable by calendar days to F1 expiry at entry (<=10, 11-20, >20)")
    per_year = per_year_table(hold, post, -1)
    log.append("H1 per-year table 2019-2026 (VX30 and tradable)")
    trad_days_mean = float(np.nanmean(post["trad_days"]))
    res["H1"] = {"stats": h1, "tradable": h1_trad, "tradable_by_f1_days_at_entry": by_days,
                 "tradable_mean_days_to_expiry_of_surviving_contract": trad_days_mean,
                 "per_year": per_year, "top3_abs_change_events": top,
                 "robustness": {"ex_top1": h1_ex1, "ex_top3": h1_ex3,
                                "ex_extrapolated": h1_noext, "log_points": h1_log,
                                "never_rescheduled": h1_never}}
    res["H1"]["verdict"] = verdict_directional(h1, h1_trad, h1_ex1, h1_ex3, -1)

    # ---- H2 pooled pre leg --------------------------------------------------
    h2 = leg_summary(pre["x"], pre["P"], +1, rng, n_days, log, "H2 pooled pre VX30 change, k*")
    h2_trad = tradable_summary(pre["trad"], +1)
    log.append("H2 tradable surviving-contract change (gross, net 0.10)")
    p1 = exclude_top(pre, 1)
    p3 = exclude_top(pre, 3)
    h2_ex1 = leg_summary(pre["x"][p1], pre["P"][p1], +1, rng, n_days, log, "H2 excluding top-1 |change|")
    h2_ex3 = leg_summary(pre["x"][p3], pre["P"][p3], +1, rng, n_days, log, "H2 excluding top-3 |change|")
    h2_log = leg_summary(pre["xl"], pre["PL"], +1, rng, n_days, log, "H2 in log points")
    res["H2"] = {"stats": h2, "tradable": h2_trad,
                 "robustness": {"ex_top1": h2_ex1, "ex_top3": h2_ex3, "log_points": h2_log}}
    res["H2"]["verdict"] = verdict_directional(h2, h2_trad, h2_ex1, h2_ex3, +1)

    # ---- H3 magnitude -------------------------------------------------------
    h3 = magnitude_summary(post["x"], post["P"], rng, log, "H3 |VX30 change| post window vs placebo")
    h3_pre = magnitude_summary(pre["x"], pre["P"], rng, log, "H3 |VX30 change| pre window (k*) vs placebo")
    h3["median_abs_tradable_change"] = h1_trad.get("median_abs_change")
    h3_pre["median_abs_tradable_change"] = h2_trad.get("median_abs_change")
    if h3["n"] < MIN_N:
        v3 = "INCONCLUSIVE"
    elif h3["ratio_median"] >= H3_RATIO and h3["p_one_sided_median"] <= ALPHA_ADJ:
        v3 = "PASS (label: state predicts movement, not edge)"
    elif h3["event_median_abs"] <= h3["placebo_median_abs"] or h3["p_one_sided_median"] > KILL_P:
        v3 = "KILL"
    else:
        v3 = "INCONCLUSIVE"
    res["H3"] = {"post": h3, "pre": h3_pre, "verdict": v3}

    # ---- H4 regime contrast -------------------------------------------------
    keep = ~np.isnan(post["x"]) & (~np.isnan(post["P"])).any(1)
    e_pts = post["x"] - np.nanmean(post["P"], 1)
    e_log = post["xl"] - np.nanmean(post["PL"], 1)
    reg = post["regime"]
    buckets = {}
    for b in ["high_vol", "normal", "low_vol"]:
        m = keep & (reg == b)
        buckets[b] = {"n": int(m.sum()),
                      "mean_change": r(float(post["x"][m].mean())) if m.any() else None,
                      "excess": r(float(e_pts[m].mean())) if m.any() else None,
                      "excess_log": r(float(e_log[m].mean())) if m.any() else None,
                      "hit_rate": r(float((post["x"][m] < 0).mean())) if m.any() else None}
    log.append("H4 excess by regime bucket at t-1 (points and log)")
    hv = keep & (reg == "high_vol")
    nh = keep & (reg != "high_vol") & np.array([g is not None for g in reg])

    def perm_diff(e, hv, nh, rng):
        idx = np.where(hv | nh)[0]
        lab = hv[idx]
        vals = e[idx]
        obs = float(vals[lab].mean() - vals[~lab].mean())
        cnt = 0
        for _ in range(N_PERM):
            pl = rng.permutation(lab)
            d = vals[pl].mean() - vals[~pl].mean()
            if d <= obs:
                cnt += 1
        return obs, (1 + cnt) / (N_PERM + 1)

    d_pts, p_pts = perm_diff(e_pts, hv, nh, rng)
    log.append("H4 high_vol minus non-high_vol excess, permutation p (points)")
    d_log, p_log = perm_diff(e_log, hv, nh, rng)
    log.append("H4 high_vol minus non-high_vol excess, permutation p (log)")
    h1_killed = res["H1"]["verdict"]["verdict"] == "KILL"
    if h1_killed:
        v4 = "KILL (H1 killed)"
    elif int(hv.sum()) < MIN_N:
        v4 = "INCONCLUSIVE (high_vol n < 30)"
    elif d_pts >= 0:
        v4 = "KILL (high_vol excess not more negative than non-high_vol)"
    elif p_pts <= ALPHA_ADJ:
        v4 = "PASS"
    else:
        v4 = "INCONCLUSIVE"
    res["H4"] = {"buckets": buckets, "n_high_vol": int(hv.sum()), "n_non_high_vol": int(nh.sum()),
                 "diff_high_minus_nonhigh_points": d_pts, "perm_p_points": p_pts,
                 "diff_high_minus_nonhigh_log": d_log, "perm_p_log": p_log, "verdict": v4}

    # ---- H5 / H6 ------------------------------------------------------------
    for name, m, lbl in [("H5", hold.is_fomc.to_numpy(), "FOMC"), ("H6", hold.is_cpi.to_numpy(), "CPI")]:
        ws = sub(post, m)
        s = leg_summary(ws["x"], ws["P"], -1, rng, n_days, log, f"{name} {lbl} post VX30 change")
        tr = tradable_summary(ws["trad"], -1)
        log.append(f"{name} {lbl} tradable surviving-contract change")
        e1 = leg_summary(ws["x"][exclude_top(ws, 1)], ws["P"][exclude_top(ws, 1)], -1, rng, n_days,
                         log, f"{name} {lbl} excluding top-1")
        e3 = leg_summary(ws["x"][exclude_top(ws, 3)], ws["P"][exclude_top(ws, 3)], -1, rng, n_days,
                         log, f"{name} {lbl} excluding top-3")
        res[name] = {"subset": lbl, "stats": s, "tradable": tr,
                     "robustness": {"ex_top1": e1, "ex_top3": e3},
                     "verdict": verdict_directional(s, tr, e1, e3, -1)}

    # ---- descriptive --------------------------------------------------------
    bs = post["entry_basis"]
    desc = {}
    for name, m in [("basis_gt_0", bs > 0), ("basis_le_0", bs <= 0)]:
        mm = keep & m
        desc[name] = {"n": int(mm.sum()), "excess": r(float(e_pts[mm].mean())) if mm.any() else None,
                      "mean_change": r(float(post["x"][mm].mean())) if mm.any() else None}
    log.append("Descriptive: excess by basis sign at entry")
    yr = hold.year.to_numpy()
    for name, m in [("2019_2021", yr <= 2021), ("2022_2026", yr >= 2022)]:
        mm = keep & m
        desc[name] = {"n": int(mm.sum()), "excess": r(float(e_pts[mm].mean())) if mm.any() else None,
                      "mean_change": r(float(post["x"][mm].mean())) if mm.any() else None}
    log.append("Descriptive: excess 2019-2021 vs 2022-2026")
    neg_years = [row["year"] for row in per_year if row["mean_vx30_change"] is not None
                 and row["mean_vx30_change"] < 0]
    desc["years_with_negative_pooled_mean"] = {"count": len(neg_years), "of": len(per_year),
                                               "years": neg_years}
    log.append("Descriptive: count of years with negative pooled mean")
    res["descriptive"] = desc

    # ---- per-event record -------------------------------------------------------
    events = []
    for i in range(len(hold)):
        events.append({"date": str(hold.date[i].date()), "tag": hold.tag[i], "year": int(hold.year[i]),
                       "post_vx30_change": r(post["x"][i]), "post_vx30_log_change": r(post["xl"][i], 5),
                       "post_placebo_mean": r(float(np.nanmean(post["P"][i])))
                       if not np.isnan(post["P"][i]).all() else None,
                       "post_n_placebo_offsets": int((~np.isnan(post["P"][i])).sum()),
                       "post_tradable_change": r(post["trad"][i]), "post_tradable_expiry": post["trad_exp"][i],
                       "pre_vx30_change": r(pre["x"][i]), "pre_tradable_change": r(pre["trad"][i]),
                       "regime_t_minus_1": post["regime"][i], "basis_t_minus_1": r(post["entry_basis"][i]),
                       "f1_days_t_minus_1": r(post["entry_f1_days"][i], 0),
                       "vx30_extrapolated_in_window": bool(post["extrap"][i]),
                       "cpi_rescheduled": bool(hold.cpi_rescheduled[i])})
    res["events"] = events
    res["n_attempts_logged_on_holdout"] = len(log)
    return res


# ----------------------------------------------------------------------------
# markdown
# ----------------------------------------------------------------------------

def md_fit(results: dict) -> str:
    fit = results["fit"]
    L = []
    L.append(f"## Result: {DESIGN}")
    L.append("")
    L.append(f"Runner: `scripts/research/catalyst_test.py` (phase fit, run "
             f"{results['run']['fit_timestamp_utc']} UTC). Results JSON: `{results['run']['results_path']}`.")
    L.append("Input hashes (SHA-256) recorded before any 2019+ number was computed:")
    L.append("")
    for f, h in results["run"]["input_sha256"].items():
        L.append(f"- `{f}`: `{h}`")
    L.append("")
    c = fit["counts"]
    L.append(f"### Fit window {fit['window'][0]} to {fit['window'][1]}")
    L.append("")
    L.append(f"Events on VX trading days: {c['n_events']} distinct days ({c['n_fomc']} FOMC, "
             f"{c['n_cpi']} CPI, {c['n_both']} same-day pairs); {c['n_trading_days']} VX trading days. "
             f"Dropped (no VX settlement): {', '.join(d['date'] + ' (' + d['tag'] + ')' for d in results['events']['dropped']) or 'none'}. "
             "Placebo windows for the fit are restricted to end on or before 2018-12-31.")
    L.append("")
    L.append("| Leg | Window | n | Mean change | Placebo mean | Excess | Placebo z | One-sided p | Tradable mean change (n) |")
    L.append("| --- | --- | --- | --- | --- | --- | --- | --- | --- |")
    for j in GRID_J:
        s = fit["post"][str(j)]
        t = s["tradable"]
        L.append(f"| post (short) | j = {j} | {s['n']} | {fmt(s['mean'])} | {fmt(s['placebo_mean'])} | "
                 f"{fmt(s['excess'])} | {fmt(s['z'], 2)} | {fmtp(s['p_one_sided'])} | "
                 f"{fmt(t.get('mean_change'))} ({t.get('n')}) |")
    for k in GRID_K:
        s = fit["pre"][str(k)]
        t = s["tradable"]
        L.append(f"| pre (long) | k = {k} | {s['n']} | {fmt(s['mean'])} | {fmt(s['placebo_mean'])} | "
                 f"{fmt(s['excess'])} | {fmt(s['z'], 2)} | {fmtp(s['p_one_sided'])} | "
                 f"{fmt(t.get('mean_change'))} ({t.get('n')}) |")
    L.append("")
    und = fit.get("undefined_cells", {"post": [], "pre": []})
    und_txt = ""
    if und["post"] or und["pre"]:
        und_txt = (" Undefined cells (zero-length window, excess identically 0, z = 0/0), counted as "
                   "attempts but excluded from the argmax: "
                   + ", ".join([f"j = {j}" for j in und["post"]] + [f"k = {k}" for k in und["pre"]])
                   + ".")
    L.append(f"Selected and frozen: **j\\* = {fit['j_star']}** (most negative post-leg z), "
             f"**k\\* = {fit['k_star']}** (most positive pre-leg z). 8 in-sample evaluations; "
             f"Bonferroni m = {M_BONFERRONI} as registered (alpha_adj = {ALPHA_ADJ:.5f}).{und_txt}")
    L.append("")
    return "\n".join(L) + "\n"


def md_leg_row(name, s, extra=""):
    ci = s.get("excess_ci95_iid", (math.nan, math.nan))
    bci = s.get("excess_ci95_block", (math.nan, math.nan))
    return (f"| {name} | {s.get('n', 0)} | {fmt(s.get('mean'))} | {fmt(s.get('median'))} | "
            f"{fmt(s.get('placebo_mean'))} | {fmt(s.get('excess'))} | "
            f"[{fmt(ci[0])}, {fmt(ci[1])}] | [{fmt(bci[0])}, {fmt(bci[1])}] | "
            f"{fmt(s.get('z'), 2)} | {fmtp(s.get('p_one_sided'))} | "
            f"{fmt(s.get('hit_rate'))} / {fmt(s.get('placebo_hit_rate'))} |{extra}")


def md_holdout(results: dict) -> str:
    h = results["holdout"]
    c = h["counts"]
    L = []
    L.append(f"### Holdout 2019-01-01 onward, one run at j\\* = {h['j_star']}, k\\* = {h['k_star']} "
             f"(phase holdout, run {results['run']['holdout_timestamp_utc']} UTC)")
    L.append("")
    L.append(f"Events: {c['n_events']} distinct days ({c['n_fomc']} FOMC, {c['n_cpi']} CPI, "
             f"{c['n_both']} same-day), {c['first_event']} to {c['last_event']}, "
             f"{c['n_trading_days']} VX trading days. Dropped for an incomplete post window: "
             f"{', '.join(c['dropped_incomplete_window']) or 'none'}.")
    L.append("")
    L.append("Intervals: 95 percent, 10,000 resamples; 'iid' resamples events, 'block' is a circular block "
             f"bootstrap of the date-ordered per-event excess with blocks of {h['H1']['stats'].get('block_len_events')} "
             f"consecutive events (about {BLOCK_TRADING_DAYS} trading days). p is the one-sided placebo p "
             "from 10,000 one-offset-per-event draws. Hit rate = share of events with the hypothesised "
             "sign / same on placebo windows.")
    L.append("")
    L.append("| Hypothesis | n | Mean | Median | Placebo mean | Excess | Excess CI iid | Excess CI block | z | p | Hit / placebo hit |")
    L.append("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |")
    L.append(md_leg_row(f"H1 pooled post (short, t-1 to t+{h['j_star']})", h["H1"]["stats"]))
    L.append(md_leg_row(f"H2 pooled pre (long, t-{h['k_star']} to t-1)", h["H2"]["stats"]))
    L.append(md_leg_row("H5 FOMC post", h["H5"]["stats"]))
    L.append(md_leg_row("H6 CPI post", h["H6"]["stats"]))
    L.append("")
    L.append("Robustness (H1 unless stated; not separate hypotheses):")
    L.append("")
    L.append("| Variant | n | Mean | Median | Placebo mean | Excess | Excess CI iid | Excess CI block | z | p | Hit / placebo hit |")
    L.append("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |")
    rb = h["H1"]["robustness"]
    L.append(md_leg_row("H1 ex top-1 |change|", rb["ex_top1"]))
    L.append(md_leg_row("H1 ex top-3 |change|", rb["ex_top3"]))
    L.append(md_leg_row("H1 ex extrapolated VX30", rb["ex_extrapolated"]))
    L.append(md_leg_row("H1 log points", rb["log_points"]))
    L.append(md_leg_row("H1 never-rescheduled events", rb["never_rescheduled"]))
    L.append(md_leg_row("H2 ex top-1", h["H2"]["robustness"]["ex_top1"]))
    L.append(md_leg_row("H2 ex top-3", h["H2"]["robustness"]["ex_top3"]))
    L.append(md_leg_row("H2 log points", h["H2"]["robustness"]["log_points"]))
    L.append(md_leg_row("H5 ex top-1", h["H5"]["robustness"]["ex_top1"]))
    L.append(md_leg_row("H5 ex top-3", h["H5"]["robustness"]["ex_top3"]))
    L.append(md_leg_row("H6 ex top-1", h["H6"]["robustness"]["ex_top1"]))
    L.append(md_leg_row("H6 ex top-3", h["H6"]["robustness"]["ex_top3"]))
    L.append("")
    top = ", ".join(f"{t['date']} ({t['tag']}, {fmt(t['vx30_change'], 2)})" for t in h["H1"]["top3_abs_change_events"])
    L.append(f"Largest |VX30 change| events (post window): {top}.")
    L.append("")
    L.append("Strictly tradable check (surviving monthly contract, 7-calendar-day rule; short for the post leg, "
             "long for the pre leg; net = after 0.10 round trip):")
    L.append("")
    L.append("| Leg | n | Mean change | Median change | Mean P&L gross | Mean P&L net | Sortino (net) | Worst net | Hit (net > 0) |")
    L.append("| --- | --- | --- | --- | --- | --- | --- | --- | --- |")
    for name, t in [("H1 post, short", h["H1"]["tradable"]), ("H2 pre, long", h["H2"]["tradable"]),
                    ("H5 FOMC post, short", h["H5"]["tradable"]), ("H6 CPI post, short", h["H6"]["tradable"])]:
        L.append(f"| {name} | {t.get('n')} | {fmt(t.get('mean_change'))} | {fmt(t.get('median_change'))} | "
                 f"{fmt(t.get('mean_pnl_gross'))} | {fmt(t.get('mean_pnl_net'))} | {fmt(t.get('sortino_net'), 2)} | "
                 f"{fmt(t.get('worst_pnl_net'), 2)} | {fmt(t.get('hit_rate_net'))} |")
    L.append("")
    L.append("H1 tradable by calendar days to F1 expiry at entry:")
    L.append("")
    L.append("| Bucket | n | Mean change | Mean P&L net | Sortino (net) |")
    L.append("| --- | --- | --- | --- | --- |")
    for b, t in h["H1"]["tradable_by_f1_days_at_entry"].items():
        L.append(f"| {b} | {t.get('n')} | {fmt(t.get('mean_change'))} | {fmt(t.get('mean_pnl_net'))} | {fmt(t.get('sortino_net'), 2)} |")
    L.append("")
    L.append("H1 per year:")
    L.append("")
    L.append("| Year | n | Mean VX30 change | Excess | Hit | Tradable n | Tradable mean change | Tradable mean P&L net |")
    L.append("| --- | --- | --- | --- | --- | --- | --- | --- |")
    for row in h["H1"]["per_year"]:
        L.append(f"| {row['year']} | {row['n']} | {fmt(row['mean_vx30_change'])} | {fmt(row['excess'])} | "
                 f"{fmt(row['hit_rate'])} | {row['tradable_n']} | {fmt(row['tradable_mean_change'])} | "
                 f"{fmt(row['tradable_mean_pnl_net'])} |")
    L.append("")
    h3 = h["H3"]["post"]
    h3p = h["H3"]["pre"]
    L.append("H3 magnitude (|VX30 change|):")
    L.append("")
    L.append("| Window | n | Event median | Placebo median (draw mean / pooled) | Ratio | Ratio CI iid | p (median) | Event mean | Placebo mean | p (mean) | Median |tradable change| |")
    L.append("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |")
    for name, s in [(f"post, j = {h['j_star']}", h3), (f"pre, k = {h['k_star']}", h3p)]:
        ci = s.get("ratio_median_ci95_iid", (math.nan, math.nan))
        L.append(f"| {name} | {s.get('n')} | {fmt(s.get('event_median_abs'))} | "
                 f"{fmt(s.get('placebo_median_abs'))} / {fmt(s.get('placebo_median_abs_pooled'))} | "
                 f"{fmt(s.get('ratio_median'))} | [{fmt(ci[0])}, {fmt(ci[1])}] | {fmtp(s.get('p_one_sided_median'))} | "
                 f"{fmt(s.get('event_mean_abs'))} | {fmt(s.get('placebo_mean_abs'))} | {fmtp(s.get('p_one_sided_mean'))} | "
                 f"{fmt(s.get('median_abs_tradable_change'))} |")
    L.append("")
    h4 = h["H4"]
    L.append("H4 regime at t-1 (cfg v2 buckets, not refitted):")
    L.append("")
    L.append("| Bucket | n | Mean change | Excess (points) | Excess (log) | Hit |")
    L.append("| --- | --- | --- | --- | --- | --- |")
    for b, s in h4["buckets"].items():
        L.append(f"| {b} | {s['n']} | {fmt(s['mean_change'])} | {fmt(s['excess'])} | {fmt(s['excess_log'], 4)} | {fmt(s['hit_rate'])} |")
    L.append("")
    L.append(f"high_vol minus non-high_vol excess: {fmt(h4['diff_high_minus_nonhigh_points'])} points "
             f"(permutation p = {fmtp(h4['perm_p_points'])}, n_high = {h4['n_high_vol']}, n_other = {h4['n_non_high_vol']}); "
             f"log points {fmt(h4['diff_high_minus_nonhigh_log'], 4)} (p = {fmtp(h4['perm_p_log'])}).")
    L.append("")
    d = h["descriptive"]
    L.append("Descriptive only (no pass claim): excess by basis sign at entry: f1 - VIX > 0 "
             f"n = {d['basis_gt_0']['n']}, excess {fmt(d['basis_gt_0']['excess'])}; f1 - VIX <= 0 "
             f"n = {d['basis_le_0']['n']}, excess {fmt(d['basis_le_0']['excess'])}. "
             f"2019-2021 n = {d['2019_2021']['n']}, excess {fmt(d['2019_2021']['excess'])}; "
             f"2022-2026 n = {d['2022_2026']['n']}, excess {fmt(d['2022_2026']['excess'])}. "
             f"Years with negative pooled mean VX30 change: {d['years_with_negative_pooled_mean']['count']} "
             f"of {d['years_with_negative_pooled_mean']['of']} ({', '.join(str(y) for y in d['years_with_negative_pooled_mean']['years']) or 'none'}).")
    L.append("")
    L.append("Verdicts against the registered thresholds (pass: excess <= -0.15, p <= 0.00208, tradable mean "
             "<= -0.20, sign kept ex top-3; kill: wrong sign, p > 0.10, sign flips ex top-1, tradable mean "
             "> -0.10; mirrored for H2):")
    L.append("")
    L.append("| Hypothesis | Verdict | Pass conditions met | Kill conditions triggered |")
    L.append("| --- | --- | --- | --- |")
    for name in ["H1", "H2", "H5", "H6"]:
        v = h[name]["verdict"]
        pc = ", ".join(k for k, ok in v.get("pass_conditions", {}).items() if ok) or "none"
        kc = ", ".join(k for k, ok in v.get("kill_conditions", {}).items() if ok) or "none"
        L.append(f"| {name} | {v['verdict']} | {pc} | {kc} |")
    L.append(f"| H3 | {h['H3']['verdict']} | ratio >= 1.25 and p <= 0.00208 | median <= placebo or p > 0.10 |")
    L.append(f"| H4 | {h['H4']['verdict']} | | |")
    L.append("")
    mde = results.get("mde", {})
    L.append(f"Pre-registered MDE (80 percent power, alpha 0.00208): about 0.45 points; realised holdout SE of the "
             f"mean VX30 change {fmt(mde.get('se_mean'))} points (sd {fmt(mde.get('sd'))}, n {mde.get('n')}), "
             f"realised MDE {fmt(mde.get('mde_80'))} points.")
    L.append("")
    L.append(f"Attempt log: {h['n_attempts_logged_on_holdout']} quantities computed on 2019+ data, all listed in "
             f"`holdout.attempt_log` of the results JSON. In-sample grid evaluations: 8. Bonferroni m used: {M_BONFERRONI}.")
    L.append("")
    dev = results.get("deviations", [])
    L.append("Deviations and interpretations recorded by the runner:")
    L.append("")
    for x in dev:
        L.append(f"- {x}")
    L.append("")
    flags = h["data_flags"]
    L.append(f"Data flags: regime read from an earlier decision than t-1 on {len(flags['post_regime_taken_from_earlier_decision'])} events "
             f"({', '.join(flags['post_regime_taken_from_earlier_decision']) or 'none'}); tradable settle missing on "
             f"{len(flags['post_tradable_missing'])} post windows and {len(flags['pre_tradable_missing'])} pre windows; "
             f"{flags['post_extrapolated_vx30_events']} post windows touch an extrapolated VX30; events without any valid "
             f"placebo offset: post {len(flags['events_without_placebo_post'])}, pre {len(flags['events_without_placebo_pre'])}.")
    L.append("")
    passed = results.get("passed")
    L.append(f"**Overall: passed = {str(passed).lower()}** (H1 {h['H1']['verdict']['verdict']}, H2 {h['H2']['verdict']['verdict']}, "
             f"H3 {h['H3']['verdict']}, H4 {h['H4']['verdict']}, H5 {h['H5']['verdict']['verdict']}, "
             f"H6 {h['H6']['verdict']['verdict']}). {results.get('product_level', '')}")
    L.append("")
    return "\n".join(L) + "\n"


# ----------------------------------------------------------------------------
# main
# ----------------------------------------------------------------------------

def to_jsonable(obj):
    if isinstance(obj, dict):
        return {str(k): to_jsonable(v) for k, v in obj.items()}
    if isinstance(obj, (list, tuple)):
        return [to_jsonable(v) for v in obj]
    if isinstance(obj, (np.bool_,)):
        return bool(obj)
    if isinstance(obj, (np.integer,)):
        return int(obj)
    if isinstance(obj, (float, np.floating)):
        return r(obj, 6)
    if isinstance(obj, np.ndarray):
        return [to_jsonable(v) for v in obj.tolist()]
    if isinstance(obj, (pd.Timestamp, dt.date)):
        return str(obj)[:10]
    return obj


def write_json(path, obj):
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(to_jsonable(obj), fh, indent=1)
        fh.write("\n")


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--data-dir", required=True)
    ap.add_argument("--out", required=True, help="results JSON (fit phase creates it, holdout phase completes it)")
    ap.add_argument("--phase", choices=["fit", "holdout"], required=True)
    ap.add_argument("--md", help="write the markdown section for this phase to this file")
    ap.add_argument("--seed", type=int, default=20260927)
    args = ap.parse_args(argv)

    data = Data(args.data_dir)
    ev_all, dropped = data.build_events()
    now = dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%S")

    deviations = [
        "The registered pre-leg window runs from settle t-k to settle t-1, so the grid point k = 1 is a "
        "zero-length window (t-1 to t-1): its change is identically 0 for events and placebos and its "
        "placebo z is 0/0. The registration's rule 'k* = the k with the most positive z' is undefined "
        "for it. The cell is kept in the fit table and in the attempt count (m = 24 unchanged) but is "
        "excluded from the argmax; k* is chosen among k in {2, 3, 5}. The window was not reinterpreted "
        "(for example as t-k-1 to t-1). Decided on fit-window information only, before any 2019+ number.",
        "Fit-phase placebo windows are restricted to end on or before 2018-12-31, so no 2019 settle "
        "enters the fit even as a baseline (the registration is silent on this).",
        "An event with no valid placebo offset (all 20 shifted windows touch another event's halo or "
        "leave the data) is dropped from that leg's statistics and listed; none of its outcomes is used.",
        "The placebo halo [e-3, e+3] is built from every scheduled FOMC / verified CPI date, including "
        "the two event dates that are not VX trading days (mapped to the next VX day).",
        "'Excluding extrapolated VX30 dates' is read as excluding events whose entry or exit settle is a "
        "flagged extrapolation.",
        "The H3 placebo median is the mean of the 10,000 draw medians (the pooled median over all valid "
        "placebo windows is reported beside it); the pass ratio uses the draw-mean version.",
        "H5/H6 are judged with the H1 pass AND kill conditions on their subsample (the registration "
        "states the pass conditions explicitly and is read as mirroring the kill conditions).",
        "The 95 percent interval registered is an iid event bootstrap; a circular block bootstrap of the "
        "date-ordered per-event excess with about 21 trading days per block is reported beside it as "
        "asked by the workflow, and neither is a pass condition.",
        "Sortino uses the net P&L series with MAR = 0 (downside deviation = root mean square of "
        "negative net P&L).",
    ]

    if args.phase == "fit":
        fit = run_fit(data, ev_all, args.seed)
        results = {"design": DESIGN,
                   "run": {"fit_timestamp_utc": now, "input_sha256": data.hashes,
                           "results_path": args.out, "seed": args.seed,
                           "holdout_timestamp_utc": None},
                   "parameters": {"grid_j": GRID_J, "grid_k": GRID_K, "offsets": OFFSETS,
                                  "exclusion_halo_days": EXCL_HALO, "survival_days": SURVIVAL_DAYS,
                                  "cost_round_trip": COST, "n_draws": N_DRAWS, "n_boot": N_BOOT,
                                  "n_perm": N_PERM, "m_bonferroni": M_BONFERRONI, "alpha_adj": ALPHA_ADJ},
                   "events": {"dropped": dropped,
                              "n_all_on_calendar": int(len(ev_all))},
                   "fit": fit, "holdout": None, "passed": None, "deviations": deviations}
        write_json(args.out, results)
        if args.md:
            with open(args.md, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(md_fit(results))
        print(json.dumps({"phase": "fit", "j_star": fit["j_star"], "k_star": fit["k_star"],
                          "counts": fit["counts"]}, indent=1))
        return 0

    # holdout phase
    with open(args.out, encoding="utf-8") as fh:
        results = json.load(fh)
    if results.get("holdout") is not None:
        raise SystemExit("holdout already run for this results file; a second look needs a new pre-registration")
    for f, hsh in data.hashes.items():
        if results["run"]["input_sha256"].get(f) != hsh:
            raise SystemExit(f"input {f} changed since the fit was frozen")
    fit_check = run_fit(data, ev_all, args.seed)
    if fit_check["j_star"] != results["fit"]["j_star"] or fit_check["k_star"] != results["fit"]["k_star"]:
        raise SystemExit("recomputed fit does not reproduce the frozen j*, k*")
    # the frozen JSON stores floats rounded to 6 decimals, so agreement is
    # checked at that precision
    for j in GRID_J:
        if abs(fit_check["post"][str(j)]["excess"] - results["fit"]["post"][str(j)]["excess"]) > 1e-6:
            raise SystemExit("recomputed fit differs from the frozen fit table")
    for k in GRID_K:
        if abs(fit_check["pre"][str(k)]["excess"] - results["fit"]["pre"][str(k)]["excess"]) > 1e-6:
            raise SystemExit("recomputed fit differs from the frozen fit table")
    hold = run_holdout(data, ev_all, results["fit"], args.seed)
    results["holdout"] = hold
    results["run"]["holdout_timestamp_utc"] = now
    s = hold["H1"]["stats"]
    sd = s.get("sd", math.nan)
    n = s.get("n", 0)
    se = sd / math.sqrt(n) if n else math.nan
    results["mde"] = {"n": n, "sd": sd, "se_mean": se, "mde_80": (2.86 + 0.84) * se if n else math.nan,
                      "registered_mde_80": 0.45}
    verdicts = {k: (hold[k]["verdict"]["verdict"] if isinstance(hold[k]["verdict"], dict) else hold[k]["verdict"])
                for k in ["H1", "H2", "H3", "H4", "H5", "H6"]}
    results["verdicts"] = verdicts
    results["passed"] = any(v.startswith("PASS") for v in verdicts.values())
    results["passed_primary_H1"] = verdicts["H1"] == "PASS"
    h1k = verdicts["H1"] == "KILL"
    h2k = verdicts["H2"] == "KILL"
    h3p = verdicts["H3"].startswith("PASS")
    if h1k and h2k and not h3p:
        results["product_level"] = ("Product-level kill condition met: H1 and H2 killed and H3 not passed; the "
                                    "scheduled-catalyst thesis is refuted for VX futures with free data at this n.")
    else:
        results["product_level"] = "Product-level kill condition not met (see individual verdicts)."
    write_json(args.out, results)
    if args.md:
        with open(args.md, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(md_holdout(results))
    print(json.dumps({"phase": "holdout", "verdicts": verdicts, "passed": results["passed"],
                      "counts": hold["counts"]}, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
