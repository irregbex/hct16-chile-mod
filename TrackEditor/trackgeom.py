#!/usr/bin/env python3
r"""
trackgeom.py - lap geometry helpers shared by the Track Editor server and the command line.

The one job here: SEPARATE OVERLAPPING SECTIONS. A real circuit squeezed into a game lap (a 5 km
F1 track into 600 units, a city loop into a grid of streets) has stretches where the outbound and
return legs are closer than the game's road is wide (5 lanes = 15.3 units, i.e. 130 m at Yas Marina's
scale). The game draws both strips on top of each other; the editor shows them as one blob. spread()
pushes such stretches apart, symmetrically, with a smooth falloff along the lap, then re-opens any
bend it made too tight (the engine wants under 3.5 deg per 0.4-unit step) and resamples to 0.4.
The start/finish stays at waypoint 0; the scenery is re-filed by the caller (items keep their
world position - see item_world / item_from_world).

    python trackgeom.py <race id> [--apply] [--margin 1.0] [--content <HCT15Content>]
"""
import json
import math
import os
import shutil
import sys

LANE = 1.527
PROP_MARGIN = 0.44 + 1.6
SPACING = 0.4
MAX_TURN = 3.4
DEFAULT_LANES = 5


# ----------------------------------------------------------------------------- lanes
def sections_of(content_dir, donor):
    try:
        doc = json.load(open(os.path.join(content_dir, "donor_lanes.json"), encoding="utf-8"))
    except Exception:
        return [{"P": 0.0, "Lanes": DEFAULT_LANES}]
    for s in doc.get("Stages") or []:
        if s.get("Id") == donor:
            secs = sorted(s.get("Sections") or [], key=lambda x: x["P"])
            return secs or [{"P": 0.0, "Lanes": s.get("Lanes") or DEFAULT_LANES}]
    return [{"P": 0.0, "Lanes": DEFAULT_LANES}]


def lanes_at(secs, p):
    p %= 1.0
    lanes = secs[-1]["Lanes"]
    for s in secs:
        if s["P"] <= p:
            lanes = s["Lanes"]
        else:
            break
    return lanes


def road_half(secs, i, n):
    return lanes_at(secs, i / n) * LANE


# ----------------------------------------------------------------------------- lap maths
def right(wp, i):
    n = len(wp)
    a, c = wp[(i - 1) % n], wp[(i + 1) % n]
    tx, tz = c[0] - a[0], c[2] - a[2]
    m = math.hypot(tx, tz) or 1.0
    return tz / m, -tx / m


def turn_deg(wp, i):
    n = len(wp)
    a, b, c = wp[(i - 1) % n], wp[i], wp[(i + 1) % n]
    ax, az, bx, bz = b[0] - a[0], b[2] - a[2], c[0] - b[0], c[2] - b[2]
    return math.degrees(math.atan2(ax * bz - az * bx, ax * bx + az * bz))


def lap_length(wp):
    n = len(wp)
    return sum(math.hypot(wp[(i + 1) % n][0] - wp[i][0], wp[(i + 1) % n][2] - wp[i][2]) for i in range(n))


def open_bends(wp, max_turn=MAX_TURN, passes=400):
    n = len(wp)
    for _ in range(passes):
        any_ = False
        for i in range(n):
            if abs(turn_deg(wp, i)) <= max_turn:
                continue
            a, c, b = wp[(i - 1) % n], wp[(i + 1) % n], wp[i]
            b[0] += ((a[0] + c[0]) / 2 - b[0]) * 0.5
            b[2] += ((a[2] + c[2]) / 2 - b[2]) * 0.5
            any_ = True
        if not any_:
            break
    return wp


def resample(wp, spacing=SPACING):
    n = len(wp)
    out, carry = [], 0.0
    for i in range(n):
        a, b = wp[i], wp[(i + 1) % n]
        seg = math.hypot(b[0] - a[0], b[2] - a[2])
        if seg < 1e-6:
            continue
        pos = carry
        while pos < seg:
            t = pos / seg
            out.append([a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t])
            pos += spacing
        carry = pos - seg
    return out if len(out) > 10 else wp


def _cells(wp, size):
    cells = {}
    for i, w in enumerate(wp):
        cells.setdefault((math.floor(w[0] / size), math.floor(w[2] / size)), []).append(i)
    return cells


def overlaps(wp, secs, margin=1.0, min_gap_steps=40):
    """(i, j, dist, need) for every pair of waypoints on different sections whose roads overlap (i < j)."""
    n = len(wp)
    size = 2 * DEFAULT_LANES * LANE + 8
    cells = _cells(wp, size)
    out = []
    for i, w in enumerate(wp):
        cx, cz = math.floor(w[0] / size), math.floor(w[2] / size)
        hi = road_half(secs, i, n)
        for dx in (-1, 0, 1):
            for dz in (-1, 0, 1):
                for j in cells.get((cx + dx, cz + dz), ()):
                    if j <= i:
                        continue
                    dj = j - i
                    if min(dj, n - dj) < min_gap_steps:
                        continue
                    need = hi + road_half(secs, j, n) + margin
                    dist = math.hypot(w[0] - wp[j][0], w[2] - wp[j][2])
                    if dist < need:
                        out.append((i, j, dist, need))
    return out


def overlap_indices(wp, secs, margin=0.0):
    s = set()
    for i, j, _, _ in overlaps(wp, secs, margin):
        s.add(i); s.add(j)
    return s


def overlap_fraction(wp, secs, gap_steps):
    s = set()
    for i, j, _, _ in overlaps(wp, secs, 0.0, gap_steps):
        s.add(i); s.add(j)
    return len(s) / max(1, len(wp))


def stretch_minor_axis(wp, k):
    """Scale the lap by k along its narrow principal axis (about the centroid). A circuit squeezed into a
    game lap is usually too narrow for its own road width; widening it is what lets the legs come apart."""
    n = len(wp)
    cx = sum(p[0] for p in wp) / n; cz = sum(p[2] for p in wp) / n
    sxx = sum((p[0] - cx) ** 2 for p in wp) / n; szz = sum((p[2] - cz) ** 2 for p in wp) / n; sxz = sum((p[0] - cx) * (p[2] - cz) for p in wp) / n
    # minor eigenvector of the 2x2 covariance
    tr, det = sxx + szz, sxx * szz - sxz * sxz
    lam = tr / 2 - math.sqrt(max(0.0, tr * tr / 4 - det))
    if abs(sxz) > 1e-9:
        ax, az = lam - szz, sxz
    else:
        ax, az = (1.0, 0.0) if sxx < szz else (0.0, 1.0)
    m = math.hypot(ax, az) or 1.0; ax /= m; az /= m
    out = []
    for p in wp:
        dx, dz = p[0] - cx, p[2] - cz
        along = dx * ax + dz * az
        out.append([p[0] + (k - 1) * along * ax, p[1], p[2] + (k - 1) * along * az])
    return out


def spread(wp, secs, margin=1.0, passes=30, log=None, stretches=(1.0, 1.15, 1.3, 1.45, 1.6)):
    """Push overlapping sections apart, widening the lap along its narrow axis first when the legs are too
    densely stacked to move. Returns (new waypoints, remaining overlapping pairs, passes used, stretch used).
    Success = no overlap between stretches more than 100 steps (40 units of road) apart; hairpins and
    chicanes overlapping themselves within that is normal for this game (retail stages: 15-57%)."""
    best = None
    for k in stretches:
        w2 = wp if k == 1.0 else resample(open_bends(stretch_minor_axis(wp, k)))
        new, rem, used = _spread(w2, secs, margin, passes, log)
        far = overlap_fraction(new, secs, 100)
        near = overlap_fraction(new, secs, 40)
        if log:
            log(f"stretch x{k:.2f}: {used} pass(es), lap {lap_length(new):.0f} u, overlap {near * 100:.0f}% (hairpins) / {far * 100:.0f}% (distant stretches)")
        cand = (far, rem, new, used, k)
        if best is None or (cand[0], cand[1]) < (best[0], best[1]):
            best = cand
        if far == 0.0:
            break
    far, rem, new, used, k = best
    return new, rem, used, k


def _spread(wp, secs, margin, passes, log=None):
    wp = [list(p) for p in wp]
    sigma_steps = 25            # the push is shared with 10 units of road either side
    reach = sigma_steps * 3
    weights = [math.exp(-(k * k) / (2.0 * sigma_steps * sigma_steps)) for k in range(reach + 1)]
    used = 0
    for it in range(passes):
        n = len(wp)
        viol = overlaps(wp, secs, margin)
        if not viol:
            break
        used = it + 1
        vec = [[0.0, 0.0] for _ in range(n)]
        wsum = [0.0] * n
        for i, j, dist, need in viol:
            if dist < 1e-6:
                ux, uz = right(wp, i)
            else:
                ux, uz = (wp[i][0] - wp[j][0]) / dist, (wp[i][2] - wp[j][2]) / dist
            push = (need - dist) * 0.5 * 0.7          # each side takes half the deficit, damped
            for k in range(-reach, reach + 1):
                w = weights[abs(k)]
                a, b = (i + k) % n, (j + k) % n
                vec[a][0] += w * push * ux; vec[a][1] += w * push * uz; wsum[a] += w
                vec[b][0] -= w * push * ux; vec[b][1] -= w * push * uz; wsum[b] += w
        for i in range(n):
            if wsum[i] <= 0:
                continue
            mx, mz = vec[i][0] / wsum[i], vec[i][1] / wsum[i]
            m = math.hypot(mx, mz)
            if m > 2.0:                                # never more than 2 units per pass
                mx, mz = mx / m * 2.0, mz / m * 2.0
            wp[i][0] += mx; wp[i][2] += mz
        open_bends(wp)
        wp = resample(wp)
    remaining = overlaps(wp, secs, margin)
    return wp, len(remaining), used


# ----------------------------------------------------------------------------- scenery (PropDresser rules)
def is_landmark(kind):
    return kind in ("tower", "church", "grandstand", "attraction", "monument")


def across(it):
    return it["D"] if it.get("D", 0) > 0.3 else max(0.5, it.get("W", 0) * 0.8)


def effective_dist(it):
    d = max(0.0, it.get("Dist", 0.0))
    if it.get("Asset") or it.get("Kind") == "tree":
        return d
    s = it.get("S", 1.0) or 1.0
    if it.get("Kind") == "photo":
        half = max(2.0, it.get("W", 0)) * 0.525
    elif is_landmark(it.get("Kind")):
        half = min(max(3.0, it.get("W", 0)), max(3.0, across(it))) * 0.9 * s / 2
    else:
        half = across(it) * s / 2
    return max(d, half + 0.3)


def item_world(wp, secs, it):
    n = len(wp)
    i = min(max(round((it["P"] % 1.0) * n) % n, 0), n - 1)
    r = right(wp, i)
    k = (road_half(secs, i, n) + PROP_MARGIN + effective_dist(it)) * (-1 if it.get("Side", 1) < 0 else 1)
    return wp[i][0] + r[0] * k, wp[i][2] + r[1] * k


def item_from_world(wp, secs, it, x, z):
    n = len(wp)
    best, bd = 0, float("inf")
    for i in range(n):
        d = (x - wp[i][0]) ** 2 + (z - wp[i][2]) ** 2
        if d < bd:
            bd, best = d, i
    r = right(wp, best)
    lat = (x - wp[best][0]) * r[0] + (z - wp[best][2]) * r[1]
    it["P"] = round(best / n, 5)
    it["Side"] = -1 if lat < 0 else 1
    it["Dist"] = round(max(0.5, abs(lat) - road_half(secs, best, n) - PROP_MARGIN), 2)


def refile_scenery(wp_old, wp_new, secs, scenery):
    """Every item keeps its world position; P / Side / Dist are recomputed against the new lap."""
    for name in ("Buildings", "Trees", "Landmarks"):
        for it in scenery.get(name) or []:
            x, z = item_world(wp_old, secs, it)
            item_from_world(wp_new, secs, it, x, z)


# ----------------------------------------------------------------------------- CLI
def main():
    args = sys.argv[1:]
    if not args or args[0].startswith("-"):
        print(__doc__); sys.exit(1)
    tid, apply, margin, content = args[0], "--apply" in args, 1.0, None
    if "--margin" in args:
        margin = float(args[args.index("--margin") + 1])
    if "--content" in args:
        content = args[args.index("--content") + 1]
    here = os.path.dirname(os.path.abspath(__file__))
    content = content or os.path.join(os.path.dirname(os.path.dirname(here)), "Game", "HCT15Content")
    d = os.path.join(content, "tracks", tid)
    race = json.load(open(os.path.join(d, "race.json"), encoding="utf-8"))
    sp = os.path.join(d, "scenery.json")
    scenery = json.load(open(sp, encoding="utf-8")) if os.path.exists(sp) else None
    flat = race["WaypointsFlat"]
    wp = [flat[i:i + 3] for i in range(0, len(flat), 3)]
    secs = sections_of(content, race.get("DonorScene"))
    before = overlaps(wp, secs, margin)
    print(f"{tid}: {len(wp)} waypoints, lap {lap_length(wp):.0f} u, {len(before)} overlapping pairs "
          f"({len(overlap_indices(wp, secs))} waypoints on overlapping road)")
    new, remaining, used, k = spread(wp, secs, margin, log=print)
    bad = sum(1 for i in range(len(new)) if abs(turn_deg(new, i)) > 3.5)
    print(f"chosen: stretch x{k:.2f}, {used} pass(es): {len(new)} waypoints, lap {lap_length(new):.0f} u, {remaining} overlapping pairs left, {bad} steps over 3.5 deg")
    moved = [math.hypot(new[min(i, len(new) - 1)][0] - wp[min(i, len(wp) - 1)][0], new[min(i, len(new) - 1)][2] - wp[min(i, len(wp) - 1)][2]) for i in range(0, min(len(wp), len(new)), 25)]
    print(f"road moved by up to {max(moved):.1f} u (sampled)")
    if not apply:
        print("dry run - add --apply to write race.json (+ scenery.json re-filed); .bak files are kept once")
        return
    rp = os.path.join(d, "race.json")
    if not os.path.exists(rp + ".bak"):
        shutil.copy2(rp, rp + ".bak")
    race["WaypointsFlat"] = [round(v, 4) for p in new for v in p]
    tmp = rp + ".tmp"   # a new file, never a rewrite in place (content folders may share files by hard link)
    with open(tmp, "w", encoding="utf-8", newline="\n") as f:
        json.dump(race, f, indent=1, ensure_ascii=False); f.write("\n")
    os.replace(tmp, rp)
    if scenery is not None:
        if not os.path.exists(sp + ".bak"):
            shutil.copy2(sp, sp + ".bak")
        refile_scenery(wp, new, secs, scenery)
        tmp = sp + ".tmp"
        with open(tmp, "w", encoding="utf-8", newline="\n") as f:
            json.dump(scenery, f, ensure_ascii=False)
        os.replace(tmp, sp)
    print("written")


if __name__ == "__main__":
    main()
