"""Preliminary edge test (doc/briefs/markets-product-study.md, T1) on decisions recorded in the engine ledger.

Usage: python scripts/edge_test.py [engine.db] [output.json]. Opens the database read-only.

One state per market per New York date: the last decision recorded for that date.
Forward change of the reference index from that date's close to the close h trading days later,
using only later observations. Block bootstrap over 21-day blocks for intervals.
"""
import sqlite3, json, math, random
from datetime import datetime, timezone, timedelta
from zoneinfo import ZoneInfo

NY = ZoneInfo("America/New_York")
HORIZONS = [1, 5, 10, 21]
import sys
DB = sys.argv[1] if len(sys.argv) > 1 else "apps/engine/var/engine.db"
c = sqlite3.connect(f"file:{DB}?mode=ro", uri=True)

def ny_date(ms):
    return datetime.fromtimestamp(ms / 1000, timezone.utc).astimezone(NY).date()

cfg = {r[0]: r[1] for r in c.execute("select config_version, count(*) from dec_decisions group by 1")}

def series(inst):
    out = {}
    for ms, val in c.execute("select observed_at_ms, value from ing_signals where instrument=? and variant='IMPLIED_VOLATILITY' order by observed_at_ms", (inst,)):
        out[ny_date(ms)] = float(val)
    return out

def states(context):
    last = {}
    for ms, seq, outcome, score, disl, regime in c.execute(
        """select d.as_of_ms, d.ledger_sequence, d.outcome, d.composite_score, d.dislocation_value, x.regime
           from dec_decisions d join cls_dislocations x on x.dislocation_id = d.dislocation_id
           where d.context=? order by d.as_of_ms, d.ledger_sequence""", (context,)):
        last[ny_date(ms)] = (outcome, score, disl, regime)
    return last

def block_boot(values, stat, n=2000, block=21, seed=7):
    rnd = random.Random(seed)
    k = len(values)
    if k < block * 2:
        return (float("nan"), float("nan"))
    res = []
    for _ in range(n):
        sample = []
        while len(sample) < k:
            s = rnd.randrange(0, k - block)
            sample.extend(values[s:s + block])
        res.append(stat(sample[:k]))
    res.sort()
    return res[int(0.025 * n)], res[int(0.975 * n)]

def median(v):
    s = sorted(v); m = len(s) // 2
    return s[m] if len(s) % 2 else (s[m - 1] + s[m]) / 2

report = {"config_versions": cfg, "markets": {}}
for context, inst in [("equity", "VIX"), ("oil", "OVX")]:
    px = series(inst)
    dates = sorted(px)
    idx = {d: i for i, d in enumerate(dates)}
    st = states(context)
    rows = []
    for d, (outcome, score, disl, regime) in sorted(st.items()):
        if d not in idx:
            continue
        i = idx[d]
        fwd = {}
        for h in HORIZONS:
            if i + h < len(dates):
                fwd[h] = px[dates[i + h]] - px[d]
        rows.append({"date": d, "deploy": outcome.lower() == "deploy", "score": score, "disl": disl,
                     "regime": regime, "level": px[d], "fwd": fwd})
    m = {"days": len(rows), "first": str(rows[0]["date"]), "last": str(rows[-1]["date"]),
         "deploy_days": sum(r["deploy"] for r in rows), "horizons": {}}
    for h in HORIZONS:
        rr = [r for r in rows if h in r["fwd"]]
        dep = [r for r in rr if r["deploy"]]
        idl = [r for r in rr if not r["deploy"]]
        # directional bet: + score expects the index to rise
        bet = lambda r: math.copysign(1, r["score"]) * r["fwd"][h] if r["score"] else 0.0
        hit = lambda xs: sum(1 for r in xs if r["score"] and r["fwd"][h] != 0 and math.copysign(1, r["score"]) == math.copysign(1, r["fwd"][h])) / max(1, len(xs))
        dep_bets = [bet(r) for r in dep]
        up_all = sum(1 for r in rr if r["fwd"][h] > 0) / len(rr)
        pos = [r for r in dep if r["score"] > 0]; neg = [r for r in dep if r["score"] < 0]
        hm = {
            "n_all": len(rr), "n_deploy": len(dep), "n_deploy_pos": len(pos), "n_deploy_neg": len(neg),
            "median_abs_change_deploy": median([abs(r["fwd"][h]) for r in dep]) if dep else None,
            "median_abs_change_all": median([abs(r["fwd"][h]) for r in rr]),
            "hit_rate_deploy": hit(dep),
            "hit_rate_deploy_ci": block_boot([1.0 if (r["score"] and r["fwd"][h] and math.copysign(1, r["score"]) == math.copysign(1, r["fwd"][h])) else 0.0 for r in dep], lambda s: sum(s) / len(s)),
            "hit_rate_idle": hit(idl),
            "p_up_all_days": up_all,
            "p_up_after_pos_deploy": (sum(1 for r in pos if r["fwd"][h] > 0) / len(pos)) if pos else None,
            "p_down_after_neg_deploy": (sum(1 for r in neg if r["fwd"][h] < 0) / len(neg)) if neg else None,
            "mean_directional_points_deploy": sum(dep_bets) / len(dep_bets) if dep_bets else None,
            "mean_directional_points_ci": block_boot(dep_bets, lambda s: sum(s) / len(s)),
        }
        m["horizons"][h] = hm
    # the most extreme decile of |score| on deploy days, 5-day horizon
    rr = sorted([r for r in rows if r["deploy"] and 5 in r["fwd"]], key=lambda r: -abs(r["score"]))
    top = rr[: max(1, len(rr) // 10)]
    m["top_decile_5d"] = {"n": len(top), "min_abs_score": abs(top[-1]["score"]),
                          "hit_rate": sum(1 for r in top if r["fwd"][5] and math.copysign(1, r["score"]) == math.copysign(1, r["fwd"][5])) / len(top),
                          "mean_directional_points": sum(math.copysign(1, r["score"]) * r["fwd"][5] for r in top) / len(top)}
    report["markets"][context] = m

OUT = sys.argv[2] if len(sys.argv) > 2 else "apps/engine/var/edge-test.json"
json.dump(report, open(OUT, "w"), indent=1, default=str)
for ctx, m in report["markets"].items():
    print(f"\n=== {ctx}: {m['days']} days {m['first']}..{m['last']}, deploy days {m['deploy_days']}")
    for h, x in m["horizons"].items():
        print(f" h={h:>2} n_dep={x['n_deploy']:>4} (+{x['n_deploy_pos']}/-{x['n_deploy_neg']}) "
              f"hit_dep={x['hit_rate_deploy']:.3f} CI=({x['hit_rate_deploy_ci'][0]:.3f},{x['hit_rate_deploy_ci'][1]:.3f}) hit_idle={x['hit_rate_idle']:.3f} "
              f"| med|chg| dep={x['median_abs_change_deploy']:.2f} all={x['median_abs_change_all']:.2f} "
              f"| P(up|+dep)={x['p_up_after_pos_deploy'] if x['p_up_after_pos_deploy'] is None else round(x['p_up_after_pos_deploy'],3)} P(up|all)={x['p_up_all_days']:.3f} "
              f"P(down|-dep)={x['p_down_after_neg_deploy'] if x['p_down_after_neg_deploy'] is None else round(x['p_down_after_neg_deploy'],3)} "
              f"| dir pts={x['mean_directional_points_deploy']:+.2f} CI=({x['mean_directional_points_ci'][0]:+.2f},{x['mean_directional_points_ci'][1]:+.2f})")
    print(" top decile |score| 5d:", m["top_decile_5d"])
print("\nconfig versions used by decisions:", report["config_versions"])
