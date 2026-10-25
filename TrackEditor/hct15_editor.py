#!/usr/bin/env python3
r"""
hct15_editor.py - HCT 1.6 Track Editor (browser) - the local server half.

Edits the mod's own content under Game\HCT15Content\ - tracks\<id>\race.json + scenery.json,
assets\models\<id>\ (OBJ assets), campaigns\<folder>\campaign.json + cup.json - i.e. exactly the
files the game reads (ContentRegistry, PropDresser, AssetLibrary, ModCampaign). It never touches
HorizonChase_Data, so it needs no UnityPy: plain Python 3.

Usage:
    python hct15_editor.py                      (finds Game\ from its own location)
    python hct15_editor.py "C:\...\Game"        (the folder with HorizonChase.exe and HCT15Content\)
    options: --content <dir>  --port 8360  --no-browser

Then the page opens itself at http://localhost:8360 . KEEP THE WINDOW OPEN; Ctrl+C stops it.

Every write stays inside HCT15Content; deletions move the folder to HCT15Tools\TrackEditor\trash\
(outside the content folder, so the game does not load it any more).
"""
import base64
import json
import os
import re
import shutil
import subprocess
import sys
import threading
import time
import traceback
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import unquote, urlparse, parse_qs

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import trackgeom  # noqa: E402  (lap geometry: separate overlapping sections)
try:
    import gameassets  # noqa: E402  (the game's own props, pulled from the level files - not in the public release)
except ImportError:
    gameassets = None
NO_GAMEASSETS = (False, "not included in this copy of the editor")

PORT = 8360
LANGS = ["en", "es", "pt", "fr", "de", "it"]
LANE = 1.527
PROP_MARGIN = 0.44 + 1.6

GAME_DIR = None       # folder with HorizonChase.exe
CONTENT = None        # HCT15Content
DECODED = None        # Reference\...\tracks_json (optional)
TOOLS = None          # project Tools\ (optional: osm_overlay.py)
TRASH = None
lock = threading.RLock()
game_proc = None
jobs = {}


# ----------------------------------------------------------------------------- helpers
def jload(path, default=None):
    try:
        with open(path, encoding="utf-8-sig") as f:
            return json.load(f)
    except FileNotFoundError:
        return default
    except Exception as e:
        raise ValueError(f"{os.path.basename(path)} unreadable: {e}")


def jsave(path, data, indent=1):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=indent, ensure_ascii=False)
        f.write("\n")
    os.replace(tmp, path)


# Every write is a NEW file (temp + os.replace), never a rewrite in place: the plugin's Contenido folders share
# their files across versions by hard link, and opening an existing name for writing would change every version.
def wbytes(path, data):
    tmp = path + ".tmp"
    with open(tmp, "wb") as fh:
        fh.write(data)
    os.replace(tmp, path)


def wtext(path, text):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    os.replace(tmp, path)


def safe_copy(src, dst):
    """shutil.copy2 opens dst for writing: unlink it first so a hard-linked dst is replaced, not rewritten."""
    if os.path.lexists(dst):
        os.remove(dst)
    shutil.copy2(src, dst)


def save_scenery(path, sc):
    """One item per line: readable diffs, and JsonUtility does not care about whitespace."""
    lists = ["Buildings", "Trees", "Landmarks"]
    # a track may raise the block / tree caps for itself (PropDresser reads these); keep them
    caps = [(k, int(sc[k])) for k in ("MaxBuildings", "MaxTrees") if sc.get(k)]
    out = ["{"]
    for li, name in enumerate(lists):
        items = sc.get(name) or []
        out.append(f' "{name}": [')
        for k, it in enumerate(items):
            out.append("  " + json.dumps(norm_item(it), ensure_ascii=False, separators=(", ", ": ")) + ("," if k < len(items) - 1 else ""))
        out.append(" ]" + ("," if li < len(lists) - 1 or caps else ""))
    for ci, (k, v) in enumerate(caps):
        out.append(f' "{k}": {v}' + ("," if ci < len(caps) - 1 else ""))
    out.append("}")
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(out) + "\n")
    os.replace(tmp, path)


def norm_item(it):
    """SceneryItem with every field the game knows, in a stable order."""
    def f(k, d=0.0):
        try:
            return round(float(it.get(k, d)), 4)
        except (TypeError, ValueError):
            return d
    return {
        "P": f("P"), "Side": int(it.get("Side", 1) or 1), "Dist": f("Dist"), "W": f("W"), "D": f("D"), "H": f("H"),
        "S": f("S", 1.0) or 1.0, "Kind": str(it.get("Kind", "") or ""), "Name": str(it.get("Name", "") or ""),
        "Photo": str(it.get("Photo", "") or ""), "Asset": str(it.get("Asset", "") or ""), "Yaw": f("Yaw"),
    }


def safe_id(s):
    s = re.sub(r"[^a-z0-9]+", "_", (s or "").lower()).strip("_")
    return s


def safe_rel(rel):
    """A relative path that cannot leave its folder."""
    rel = (rel or "").replace("\\", "/")
    parts = [p for p in rel.split("/") if p not in ("", ".")]
    if any(p == ".." or ":" in p for p in parts):
        raise ValueError("bad path")
    return "/".join(parts)


def tracks_dir():
    return os.path.join(CONTENT, "tracks")


def track_dir(tid):
    tid = safe_id(tid)
    if not tid:
        raise ValueError("bad track id")
    d = os.path.join(tracks_dir(), tid)
    if os.path.isdir(d):
        return d
    # a race file that lives elsewhere (flat file or campaign folder): find it
    for t in list_tracks():
        if t["id"] == tid:
            return t["dir"]
    raise FileNotFoundError(f"track '{tid}' not found")


def race_path(tid):
    d = track_dir(tid)
    p = os.path.join(d, "race.json")
    if os.path.exists(p):
        return p
    for f in os.listdir(d):
        if f.lower().endswith(".race.json"):
            r = jload(os.path.join(d, f), {})
            if r.get("ItemId") == tid:
                return os.path.join(d, f)
    raise FileNotFoundError(f"race.json of '{tid}' not found")


def lap_units(flat):
    L = 0.0
    n = len(flat) // 3
    for i in range(n):
        j = (i + 1) % n
        dx = flat[j * 3] - flat[i * 3]
        dz = flat[j * 3 + 2] - flat[i * 3 + 2]
        L += (dx * dx + dz * dz) ** 0.5
    return L


def b64file(path):
    with open(path, "rb") as f:
        return base64.b64encode(f.read()).decode("ascii")


def mime_of(name):
    ext = os.path.splitext(name)[1].lower()
    return {".html": "text/html; charset=utf-8", ".js": "application/javascript; charset=utf-8",
            ".css": "text/css; charset=utf-8", ".json": "application/json", ".png": "image/png",
            ".jpg": "image/jpeg", ".jpeg": "image/jpeg", ".gif": "image/gif", ".webp": "image/webp",
            ".obj": "text/plain; charset=utf-8", ".mtl": "text/plain; charset=utf-8", ".txt": "text/plain; charset=utf-8",
            ".svg": "image/svg+xml", ".ico": "image/x-icon"}.get(ext, "application/octet-stream")


# ----------------------------------------------------------------------------- content listing
_tracks_cache = {"t": 0, "list": None}


def list_tracks(force=False):
    """Every race file under HCT15Content (the game's FindContent rule), with the folder it lives in."""
    with lock:
        if not force and _tracks_cache["list"] is not None and time.time() - _tracks_cache["t"] < 2.0:
            return _tracks_cache["list"]
        out = []
        for root, dirs, files in os.walk(CONTENT):
            dirs[:] = [d for d in dirs if not d.startswith("_") and d.lower() != "history"]
            for f in files:
                fl = f.lower()
                if fl == "race.json" or fl.endswith(".race.json"):
                    p = os.path.join(root, f)
                    try:
                        r = jload(p, {})
                    except ValueError as e:
                        out.append({"id": os.path.basename(root), "error": str(e), "dir": root, "file": p})
                        continue
                    if not r or not r.get("ItemId"):
                        continue
                    flat = r.get("WaypointsFlat") or []
                    names = dict(zip(r.get("DisplayNameLangs") or [], r.get("DisplayNameTexts") or []))
                    city = jload(os.path.join(root, "city.json"), None) if fl == "race.json" else None
                    out.append({
                        "id": r["ItemId"], "dir": root, "file": p, "flat_file": fl != "race.json",
                        "display": r.get("DisplayName") or names.get("en") or r["ItemId"], "names": names,
                        "donor": r.get("DonorScene", ""), "ui_donor": r.get("UiDonor", ""),
                        "cup": r.get("CupId", ""), "city": r.get("CityId", ""), "laps": r.get("NumberOfLaps", 3),
                        "p2p": bool(r.get("PointToPoint")), "pit_side": r.get("PitSide", 1),
                        "wp_count": len(flat) // 3, "lap_units": round(lap_units(flat), 1) if flat else 0,
                        "has_scenery": os.path.exists(os.path.join(root, "scenery.json")),
                        "has_osm": os.path.exists(os.path.join(root, "osm_overlay.json")),
                        "has_card": os.path.exists(os.path.join(root, "card.png")),
                        "has_layout": os.path.exists(os.path.join(root, "layout.png")),
                        "has_bak": os.path.exists(p + ".bak"),
                        "city_display": (city or {}).get("DisplayName", ""),
                        "mtime": os.path.getmtime(p),
                    })
        out.sort(key=lambda t: (t.get("display") or t["id"]).lower())
        _tracks_cache["list"] = out
        _tracks_cache["t"] = time.time()
        return out


def list_campaigns():
    out = []
    cdir = os.path.join(CONTENT, "campaigns")
    if not os.path.isdir(cdir):
        return out
    for folder in sorted(os.listdir(cdir)):
        d = os.path.join(cdir, folder)
        cj = os.path.join(d, "campaign.json")
        if not os.path.isfile(cj):
            continue
        try:
            c = jload(cj, {})
            cup = jload(os.path.join(d, "cup.json"), None)
        except ValueError as e:
            out.append({"folder": folder, "error": str(e)})
            continue
        out.append({
            "folder": folder, "name": c.get("Name", folder), "mode": c.get("ModeName", ""),
            "formula1": bool(c.get("Formula1")), "freeplay": bool(c.get("FreePlay")), "pits": c.get("Pits", True),
            "default_race": c.get("DefaultRace", ""), "rounds": c.get("Rounds") or [],
            "cup": cup, "has_blade": os.path.exists(os.path.join(d, "blade.png")),
            "has_logo": os.path.exists(os.path.join(d, "logo.png")),
        })
    return out


def retail_dump():
    """Cups / cities / races of the shipped game from _container_dump.txt (ids the menus know)."""
    p = os.path.join(CONTENT, "_container_dump.txt")
    cups, cities, races = [], [], []
    if not os.path.exists(p):
        return {"cups": cups, "cities": cities, "races": races}
    sec = None
    for line in open(p, encoding="utf-8", errors="replace"):
        line = line.rstrip("\n")
        if line.startswith("== "):
            sec = line.strip("= ").lower()
            continue
        if not line.strip() or sec is None:
            continue
        t = line.split("\t")
        kv = {}
        for x in t[2:]:
            if "=" in x:
                k, v = x.split("=", 1)
                kv[k] = v
        row = {"id": t[0], "name": t[1] if len(t) > 1 else t[0]}
        row.update(kv)
        if sec.startswith("cups"):
            cups.append(row)
        elif sec.startswith("cities"):
            cities.append(row)
        elif sec.startswith("races"):
            races.append(row)
    return {"cups": cups, "cities": cities, "races": races}


def list_donors():
    """Every shipped stage with its lane sections (donor_lanes.json) plus what the decoded stage knows."""
    doc = jload(os.path.join(CONTENT, "donor_lanes.json"), {}) or {}
    gp = os.path.join(CONTENT, "assets", "game_props")
    have_props = set(os.listdir(gp)) if os.path.isdir(gp) else set()
    out = []
    for s in doc.get("Stages") or []:
        sid = s.get("Id", "")
        if not sid:
            continue
        row = {"id": sid, "lanes": s.get("Lanes", 5), "sections": s.get("Sections") or [],
               "has_gameprops": sid in have_props, "dlc": sid.startswith("season1_"), "decoded": False}
        if DECODED and os.path.exists(os.path.join(DECODED, sid + ".json")):
            row["decoded"] = True
        out.append(row)
    out.sort(key=lambda r: r["id"])
    return out


def decoded_stage(stage):
    stage = safe_id(stage)
    if not DECODED:
        raise FileNotFoundError("decoded stages are not available on this machine")
    p = os.path.join(DECODED, stage + ".json")
    t = jload(p)
    if t is None:
        raise FileNotFoundError(f"no decoded stage '{stage}'")
    root = t.get("root_pos") or [0, 0, 0]
    gd = ((t.get("leveldata") or {}).get("GameplayData")) or {}
    flat = []
    for w in t.get("waypoints") or []:
        p3 = w["pos"]
        flat.extend((round(p3[0] + root[0], 4), round(p3[1] + root[1], 4), round(p3[2] + root[2], 4)))
    om = gd.get("OffsetMultiplier") or {}
    sections = []
    for ti in gd.get("TextureInfos") or []:
        try:
            sections.append({"P": round(float(ti.get("SplinePosition", 0)), 4), "Lanes": int(ti["TrackInfo"]["NumberOfLanes"])})
        except (KeyError, TypeError, ValueError):
            pass
    return {"stage": stage, "root": root, "flat": flat,
            "offset_multiplier": [om.get("x", 20.0), om.get("y", 15.0)],
            "curve_force": gd.get("CurveForceFactor", 1.0),
            "finish": gd.get("FinishLinePercentage", 0.0), "laps": gd.get("NumberOfLaps", 3),
            "sections": sections, "lap_units": round(lap_units(flat), 1) if flat else 0}


# ----------------------------------------------------------------------------- assets
def models_dir():
    return os.path.join(CONTENT, "assets", "models")


def asset_folder(aid):
    r"""'<id>' -> assets\models\<id>; '<track>/<id>' -> tracks\<track>\assets\<id>."""
    aid = (aid or "").strip()
    if "/" in aid:
        tid, sub = aid.split("/", 1)
        d = os.path.join(track_dir(tid), "assets", safe_id(sub))
    else:
        d = os.path.join(models_dir(), safe_id(aid))
    if not os.path.isdir(d):
        raise FileNotFoundError(f"asset '{aid}' not found")
    return d


def describe_asset(d, track_id=None):
    objs = [f for f in os.listdir(d) if f.lower().endswith(".obj")]
    if not objs:
        return None
    obj = sorted(objs)[0]
    try:
        meta = jload(os.path.join(d, "asset.json"), None) or {}
        err = ""
    except ValueError as e:
        meta, err = {}, str(e)
    base = {"Id": "", "Name": "", "Category": "prop", "Description": "", "Scale": 1.0, "Yaw": 0.0,
            "GroundOffset": 0.0, "Recenter": True, "DoubleSided": False}
    base.update({k: v for k, v in meta.items() if k in base})
    if not base["Id"]:
        base["Id"] = os.path.basename(d)
    aid = base["Id"] if track_id is None else f"{track_id}/{base['Id']}"
    files = os.listdir(d)
    rig = [f for f in files if f.lower().endswith(".rig.json")]
    tex = [f for f in files if os.path.splitext(f)[1].lower() in (".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tga") and f.lower() != "thumb.png"]
    objp = os.path.join(d, obj)
    thumb = os.path.join(d, "thumb.png")
    return {"id": aid, "folder": os.path.basename(d), "dir": d, "obj": obj, "track_id": track_id,
            "mtl": [f for f in files if f.lower().endswith(".mtl")], "textures": tex, "rig": rig[0] if rig else "",
            "meta": base, "meta_error": err, "obj_size": os.path.getsize(objp), "obj_mtime": os.path.getmtime(objp),
            "has_thumb": os.path.exists(thumb) and os.path.getmtime(thumb) >= os.path.getmtime(objp)}


def list_assets():
    out = []
    md = models_dir()
    os.makedirs(md, exist_ok=True)
    for name in sorted(os.listdir(md)):
        d = os.path.join(md, name)
        if os.path.isdir(d):
            a = describe_asset(d)
            if a:
                out.append(a)
    td = tracks_dir()
    if os.path.isdir(td):
        for t in sorted(os.listdir(td)):
            a_dir = os.path.join(td, t, "assets")
            if os.path.isdir(a_dir):
                for name in sorted(os.listdir(a_dir)):
                    d = os.path.join(a_dir, name)
                    if os.path.isdir(d):
                        a = describe_asset(d, t)
                        if a:
                            out.append(a)
    return out


def list_game_props():
    gp = os.path.join(CONTENT, "assets", "game_props")
    out = {}
    if not os.path.isdir(gp):
        return out
    for donor in sorted(os.listdir(gp)):
        idx = os.path.join(gp, donor, "index.json")
        if os.path.isfile(idx):
            try:
                doc = jload(idx, {}) or {}
            except ValueError:
                continue
            props = doc.get("Props") or []
            for p in props:
                p["has_thumb"] = os.path.exists(os.path.join(gp, donor, safe_file(p.get("Name", "")) + ".png"))
            out[donor] = {"donor": doc.get("Donor", donor), "props": props,
                          "has_meshes": os.path.exists(os.path.join(gp, donor, "meshes.json"))}
    return out


def safe_file(s):
    """GameProps.SafeFile: letters, digits, _ and - survive, everything else becomes _."""
    out = "".join(c if (c.isalnum() or c in "_-") else "_" for c in (s or ""))
    return out or "prop"


def data_dir():
    return os.path.join(GAME_DIR, "HorizonChase_Data")


def start_thread_job(name, fn):
    """A job that runs a Python function; fn(progress, log) - progress(done, total, label)."""
    jid = f"{name}_{int(time.time() * 1000)}"
    job = {"id": jid, "name": name, "lines": [], "done": False, "ok": None, "started": time.time(), "progress": {"done": 0, "total": 0, "label": ""}}
    jobs[jid] = job

    def progress(done, total, label):
        job["progress"] = {"done": done, "total": total, "label": label}

    def log(line):
        job["lines"].append(line)

    def run():
        try:
            fn(progress, log)
            job["ok"] = True
        except Exception as e:
            job["lines"].append(f"error: {type(e).__name__}: {e}")
            traceback.print_exc()
            job["ok"] = False
        job["done"] = True
    threading.Thread(target=run, daemon=True).start()
    return job


def fix_relative_names(obj_path, d):
    """mtllib / map_Kd lines may carry folders; inside the library everything sits flat beside the .obj."""
    lines = open(obj_path, encoding="utf-8", errors="replace").read().splitlines()
    changed = False
    for i, line in enumerate(lines):
        s = line.strip()
        if s.startswith("mtllib "):
            name = os.path.basename(s[7:].strip().replace("\\", "/"))
            if s[7:].strip() != name:
                lines[i] = "mtllib " + name
                changed = True
    if changed:
        wtext(obj_path, "\n".join(lines) + "\n")
    for f in os.listdir(d):
        if not f.lower().endswith(".mtl"):
            continue
        p = os.path.join(d, f)
        ml = open(p, encoding="utf-8", errors="replace").read().splitlines()
        ch = False
        for i, line in enumerate(ml):
            s = line.strip()
            if s.startswith("map_"):
                t = s.split()
                name = os.path.basename(t[-1].replace("\\", "/"))
                if t[-1] != name:
                    ml[i] = " ".join(t[:-1] + [name])
                    ch = True
        if ch:
            wtext(p, "\n".join(ml) + "\n")


def import_asset(body):
    """Mirrors AssetLibrary.Import: a unique id folder, the obj + mtl + textures copied flat, asset.json written."""
    files = body.get("files") or []
    objs = [f for f in files if f["name"].lower().endswith(".obj")]
    if len(objs) != 1:
        raise ValueError("exactly one .obj file is needed")
    track_id = body.get("track_id") or None
    if track_id:
        root = os.path.join(track_dir(track_id), "assets")
    else:
        root = models_dir()
    os.makedirs(root, exist_ok=True)
    base = safe_id(body.get("id") or os.path.splitext(objs[0]["name"])[0]) or "asset"
    aid, k = base, 2
    while os.path.isdir(os.path.join(root, aid)):
        aid = f"{base}_{k}"
        k += 1
    d = os.path.join(root, aid)
    os.makedirs(d)
    written = []
    for f in files:
        name = os.path.basename(f["name"].replace("\\", "/"))
        if not name or name.startswith("."):
            continue
        if name.lower().endswith(".obj"):
            name = aid + ".obj"
        data = base64.b64decode(f["data_b64"])
        wbytes(os.path.join(d, name), data)
        written.append(name)
    fix_relative_names(os.path.join(d, aid + ".obj"), d)
    meta = {"Id": aid, "Name": "", "Category": "prop", "Description": "", "Scale": 1.0, "Yaw": 0.0,
            "GroundOffset": 0.0, "Recenter": True, "DoubleSided": False}
    for k2, v in (body.get("meta") or {}).items():
        if k2 in meta and k2 != "Id":
            meta[k2] = v
    if not meta["Name"]:
        meta["Name"] = base.replace("_", " ").strip().capitalize()
    jsave(os.path.join(d, "asset.json"), meta, indent=2)
    full = aid if not track_id else f"{track_id}/{aid}"
    return {"id": full, "dir": d, "files": written, "asset": describe_asset(d, track_id)}


# ----------------------------------------------------------------------------- track writes
def snapshot(rp, sp):
    hist = os.path.join(os.path.dirname(rp), "history")
    os.makedirs(hist, exist_ok=True)
    stamp = time.strftime("%Y-%m-%d_%H-%M-%S")
    safe_copy(rp, os.path.join(hist, stamp + ".race.json"))
    if sp and os.path.exists(sp):
        safe_copy(sp, os.path.join(hist, stamp + ".scenery.json"))
    old = sorted(f for f in os.listdir(hist) if f.endswith(".race.json"))
    for f in old[:-20]:
        os.remove(os.path.join(hist, f))
        s = os.path.join(hist, f.replace(".race.json", ".scenery.json"))
        if os.path.exists(s):
            os.remove(s)
    return stamp


def history_list(tid):
    hist = os.path.join(track_dir(tid), "history")
    if not os.path.isdir(hist):
        return []
    return sorted((f[:-len(".race.json")] for f in os.listdir(hist) if f.endswith(".race.json")), reverse=True)


def clean_race(race):
    """The race dict as the game's CustomRaceDefJson reads it; unknown keys (Geo...) are kept."""
    r = dict(race)
    flat = [round(float(v), 4) for v in (r.get("WaypointsFlat") or [])]
    if len(flat) < 30 or len(flat) % 3:
        raise ValueError("a race needs at least 10 waypoints (x, y, z each)")
    r["WaypointsFlat"] = flat
    r["RootPos"] = [float(v) for v in (r.get("RootPos") or [0, 0, 0])][:3]
    for k in ("NumberOfLaps", "TokenPrize", "NumberOfCoins", "NumberOfNitros", "PitSide"):
        if k in r:
            r[k] = int(r[k])
    for k in ("FinishLinePercentage", "CurveForceFactor", "PitStart", "PitEnd", "EndPercentage"):
        if k in r:
            r[k] = round(float(r[k]), 5)
    if "OffsetMultiplier" in r:
        r["OffsetMultiplier"] = [float(v) for v in r["OffsetMultiplier"]][:2]
    langs = r.get("DisplayNameLangs") or []
    texts = r.get("DisplayNameTexts") or []
    pairs = [(l, t) for l, t in zip(langs, texts) if l and t]
    r["DisplayNameLangs"] = [l for l, _ in pairs]
    r["DisplayNameTexts"] = [t for _, t in pairs]
    r["ItemName"] = r.get("ItemName") or ("race_" + r["ItemId"])
    return r


def list_music():
    """Race songs the plugin can pin to a track (MusicPlayer: HCT15Music\\Race\\ and files straight in HCT15Music\\),
    as the '/'-separated path under HCT15Music that a race.json "Music" names. HCT15Music sits next to HCT15Content."""
    root = os.path.join(os.path.dirname(CONTENT), "HCT15Music")
    out = []
    if not os.path.isdir(root):
        return out
    for f in os.listdir(root):
        if f.lower().endswith((".ogg", ".wav")) and os.path.isfile(os.path.join(root, f)):
            out.append(f)
    race = os.path.join(root, "Race")
    for dp, dirs, files in os.walk(race):
        for f in files:
            if f.lower().endswith((".ogg", ".wav")):
                out.append(os.path.relpath(os.path.join(dp, f), root).replace("\\", "/"))
    return sorted(out, key=str.lower)


def save_track(tid, race, scenery):
    with lock:
        rp = race_path(tid)
        d = os.path.dirname(rp)
        sp = os.path.join(d, "scenery.json")
        race = clean_race(race)
        if race.get("ItemId") != tid:
            raise ValueError("ItemId cannot change here - use 'Save as'")
        if not os.path.exists(rp + ".bak"):
            safe_copy(rp, rp + ".bak")
        jsave(rp, race)
        if scenery is not None:
            if os.path.exists(sp) and not os.path.exists(sp + ".bak"):
                safe_copy(sp, sp + ".bak")
            save_scenery(sp, scenery)
        stamp = snapshot(rp, sp)
        list_tracks(force=True)
        return {"saved": rp, "stamp": stamp, "history": history_list(tid)}


def save_as(tid, new_id, display, race, scenery):
    with lock:
        src = track_dir(tid)
        new_id = safe_id(new_id)
        if len(new_id) < 3:
            raise ValueError("the new id needs at least 3 characters")
        dst = os.path.join(tracks_dir(), new_id)
        if os.path.isdir(dst) or any(t["id"] == new_id for t in list_tracks(True)):
            raise ValueError(f"'{new_id}' exists already")
        os.makedirs(tracks_dir(), exist_ok=True)
        shutil.copytree(src, dst, ignore=shutil.ignore_patterns("history", "*.bak", "*.tmp"))
        # a flat-file track copied in: normalise to race.json
        for f in os.listdir(dst):
            if f.lower().endswith(".race.json"):
                os.remove(os.path.join(dst, f))
        r = clean_race(race)
        r["ItemId"] = new_id
        r["ItemName"] = "race_" + new_id
        r["DisplayName"] = display or ((race.get("DisplayName") or tid) + " (edit)")
        if not display:
            r["DisplayNameLangs"], r["DisplayNameTexts"] = [], []
        r["CityId"] = new_id + "_city"
        jsave(os.path.join(dst, "race.json"), r)
        save_scenery(os.path.join(dst, "scenery.json"), scenery or {"Buildings": [], "Trees": [], "Landmarks": []})
        cityp = os.path.join(dst, "city.json")
        city = jload(cityp, None) if os.path.exists(cityp) else None
        if city is None:
            city = {"ItemId": "", "ItemName": "", "DisplayName": r["DisplayName"], "CupId": r.get("CupId", ""), "TokenCost": 0, "PrizeCar": ""}
        city["ItemId"] = new_id + "_city"
        city["ItemName"] = "city_" + new_id
        city["RacesIds"] = [new_id]
        jsave(cityp, city)
        list_tracks(force=True)
        return {"id": new_id, "dir": dst}


def oval_flat(length, width_ratio=0.55, spacing=0.4):
    """A rounded rectangle of the given lap length (units), 0.4-unit spacing, x east / z north, start on the south straight heading east."""
    import math
    # two straights of 2a each plus two semicircles of radius r = a * width_ratio: L = 4a + 2*pi*r
    a = length / (4.0 + 2.0 * math.pi * width_ratio)   # half-length of each straight
    r = a * width_ratio
    pts = []
    def arc(cx, cz, a0, a1, n):
        for k in range(n):
            t = a0 + (a1 - a0) * k / n
            pts.append((cx + r * math.cos(t), cz + r * math.sin(t)))
    n_str = max(2, int(round(2 * a / spacing)))
    n_arc = max(2, int(round(math.pi * r / spacing)))
    for k in range(n_str):
        pts.append((-a + 2 * a * k / n_str, -r))
    arc(a, 0, -math.pi / 2, math.pi / 2, n_arc)
    for k in range(n_str):
        pts.append((a - 2 * a * k / n_str, r))
    arc(-a, 0, math.pi / 2, 3 * math.pi / 2, n_arc)
    flat = []
    for x, z in pts:
        flat.extend((round(x, 4), 0.0, round(z - 250.0, 4)))
    return flat


def new_track(body):
    with lock:
        tid = safe_id(body.get("id"))
        if len(tid) < 3:
            raise ValueError("the id needs at least 3 characters (letters, digits, underscore)")
        if any(t["id"] == tid for t in list_tracks(True)):
            raise ValueError(f"'{tid}' exists already")
        display = (body.get("display") or tid).strip()
        donor = safe_id(body.get("donor") or "santiago_01")
        ui = safe_id(body.get("ui_donor") or "") or (donor if not donor.startswith("season1_") else "santiago_01")
        src = body.get("source") or {"kind": "oval"}
        kind = src.get("kind", "oval")
        base = {
            "ItemId": tid, "ItemName": "race_" + tid, "DisplayName": display, "CityId": tid + "_city",
            "CupId": body.get("cup_id") or "98_chile", "DonorScene": donor, "UiDonor": ui,
            "Formula1Grid": bool(body.get("f1grid")), "NumberOfLaps": int(body.get("laps") or 3),
            "FinishLinePercentage": 0.0, "TokenPrize": 3, "NumberOfCoins": 30, "NumberOfNitros": 3,
            "RootPos": [0.0, 0.0, -250.0], "WaypointsFlat": [], "OffsetMultiplier": [20.0, 15.0], "CurveForceFactor": 1.0,
            "RoadStripTexture": "", "DayTexture": "", "NightTexture": "", "AlphaTexture": "",
            "DisplayNameLangs": [], "DisplayNameTexts": [], "PitStart": 0.9, "PitEnd": 0.985, "PitSide": 1,
            "PointToPoint": False, "EndPercentage": 1.0,
        }
        scenery = {"Buildings": [], "Trees": [], "Landmarks": []}
        d = os.path.join(tracks_dir(), tid)
        if kind == "copy":
            from_id = src.get("from")
            fr = jload(race_path(from_id))
            fs = jload(os.path.join(track_dir(from_id), "scenery.json"), scenery) or scenery
            keep = dict(fr)
            for k in ("ItemId", "ItemName", "DisplayName", "CityId", "DisplayNameLangs", "DisplayNameTexts"):
                keep.pop(k, None)
            base.update(keep)
            base.update({"ItemId": tid, "ItemName": "race_" + tid, "DisplayName": display, "CityId": tid + "_city",
                         "DisplayNameLangs": [], "DisplayNameTexts": []})
            if body.get("donor"):
                base["DonorScene"] = donor
                base["UiDonor"] = ui
            scenery = fs
            os.makedirs(tracks_dir(), exist_ok=True)
            shutil.copytree(track_dir(from_id), d, ignore=shutil.ignore_patterns("history", "*.bak", "*.tmp", "race.json", "scenery.json", "city.json", "*.race.json"))
        elif kind == "decoded":
            st = decoded_stage(src.get("stage") or donor)
            base["WaypointsFlat"] = st["flat"]
            base["RootPos"] = [float(v) for v in st["root"]]
            base["OffsetMultiplier"] = [float(v) for v in st["offset_multiplier"]]
            base["CurveForceFactor"] = float(st["curve_force"])
            base["NumberOfLaps"] = int(body.get("laps") or st.get("laps") or 3)
        else:
            base["WaypointsFlat"] = oval_flat(float(src.get("length") or 500.0), float(src.get("ratio") or 0.55))
        # offset multiplier from the decoded donor when we have it
        if kind != "copy" and DECODED and os.path.exists(os.path.join(DECODED, donor + ".json")):
            try:
                ds = decoded_stage(donor)
                base["OffsetMultiplier"] = [float(v) for v in ds["offset_multiplier"]]
                base["CurveForceFactor"] = float(ds["curve_force"])
            except Exception:
                pass
        names = body.get("names") or {}
        pairs = [(l, names[l]) for l in LANGS if names.get(l)]
        base["DisplayNameLangs"] = [l for l, _ in pairs]
        base["DisplayNameTexts"] = [t for _, t in pairs]
        os.makedirs(d, exist_ok=True)
        jsave(os.path.join(d, "race.json"), clean_race(base))
        save_scenery(os.path.join(d, "scenery.json"), scenery)
        city = {"ItemId": tid + "_city", "ItemName": "city_" + tid, "DisplayName": body.get("city_display") or display,
                "CupId": base["CupId"], "TokenCost": 0, "PrizeCar": "", "RacesIds": [tid]}
        jsave(os.path.join(d, "city.json"), city)
        list_tracks(force=True)
        return {"id": tid, "dir": d}


# ----------------------------------------------------------------------------- ambience (time of day + weather)
# The Ambiente panel writes the two files the game reads for a track's sky and weather:
#   environment.json  time of day over race progress (HCT15.EnvironmentTimeline), composed by Tools\env_timeline.py
#                     build() from the retail clips; "Editor" keeps the panel's settings so they reload as set.
#                     Preset "donor" = no file: the race keeps its donor stage's own sky.
#   weather.json      rain / snow / ash amounts over race progress + grip (HCT15.Weather).
# Both are written as new files (jsave = temp + os.replace), never rewritten in place: the plugin's Contenido
# folders share their files across versions by hard link.
AMBIENT_PRESETS = ["donor", "day", "afternoon_to_night", "sunset_to_night", "night", "night_to_dawn"]
AMBIENT_DEFAULT = {"preset": "donor", "nightAt": 1.0, "overcast": [0, 0], "aurora": 0.0, "ash": [0, 0], "snow": [0, 0],
                   "rain": [0, 0], "rainSound": True, "rainGrip": 1.0, "snowGrip": 1.0, "ashGrip": 1.0}
_env_mod = None


def env_module():
    global _env_mod
    if _env_mod is None:
        here = os.path.join(HERE, "env_timeline.py")   # the public release ships it beside the editor
        path = (here if os.path.isfile(here) else None) or find_up(HERE, os.path.join("Tools", "env_timeline.py"), False) \
            or (TOOLS and os.path.join(TOOLS, "env_timeline.py"))
        if not path or not os.path.isfile(path):
            raise FileNotFoundError("Tools\\env_timeline.py not found - the time of day cannot be built from this editor copy")
        import importlib.util
        spec = importlib.util.spec_from_file_location("env_timeline", path)
        _env_mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(_env_mod)
    return _env_mod


def _curve_pair(w, key):
    """weather.json amount -> [start, end]: a two-key curve, the constant, or the first/last of a longer curve."""
    v = w.get(key + "V")
    if isinstance(v, list) and v:
        return [float(v[0]), float(v[-1])]
    c = float(w.get(key, 0) or 0)
    return [c, c]


def ambient_get(tid):
    d = track_dir(tid)
    env = jload(os.path.join(d, "environment.json"), None)
    w = jload(os.path.join(d, "weather.json"), None) or {}
    s = dict(AMBIENT_DEFAULT)
    if env:
        ed = env.get("Editor")
        if isinstance(ed, dict):
            s.update({k: ed[k] for k in AMBIENT_DEFAULT if k in ed})
        else:
            m = re.search(r"preset '([a-z_]+)', end state reached at ([0-9.]+), overcast ([0-9.]+) -> ([0-9.]+)", env.get("Comment", ""))
            if m:      # written by env_timeline.py from the command line: read its settings back from the comment
                s.update(preset=m.group(1), nightAt=float(m.group(2)), overcast=[float(m.group(3)), float(m.group(4))])
                # the aurora too (2026-10-01: a save from the panel rebuilt Coyhaique's and Punta Arenas' skies without it)
                ma = re.search(r"aurora australis ([0-9.]+)", env.get("Comment", ""))
                if ma:
                    s["aurora"] = float(ma.group(1))
            else:
                s["preset"] = "custom"
    s["rain"] = _curve_pair(w, "Rain")
    s["snow"] = _curve_pair(w, "Snow") if ("Snow" in w or "SnowV" in w) else s["snow"]
    s["ash"] = _curve_pair(w, "Ash") if ("Ash" in w or "AshV" in w) else s["ash"]
    s["rainSound"] = bool(w.get("Sound", True))
    s["rainGrip"] = float(w.get("Grip", 1.0))
    s["snowGrip"] = float(w.get("SnowGrip", 1.0))
    s["ashGrip"] = float(w.get("AshGrip", 1.0))
    nf = jload(os.path.join(d, "nightfx.json"), None) or {}
    fw = nf.get("Fireworks") or None
    lamps = nf.get("Lamps") or []
    s["fireworks"] = bool(fw)
    s["fireworksEvery"] = float((fw or {}).get("Every", 1.4))
    s["windows"] = bool(nf.get("Windows", False))
    s["lamps"] = bool(lamps)
    s["lampsEvery"] = float(lamps[0].get("Every", 0.01)) if lamps else 0.01
    race = jload(race_path(tid), {}) or {}
    donor = race.get("DonorScene", "")
    return {"settings": s, "has_environment": env is not None, "has_weather": bool(w), "comment": w.get("Comment", ""),
            "presets": AMBIENT_PRESETS, "has_nightfx": bool(nf), "night_lights": len(nf.get("Lights") or []),
            "donor": donor, "donor_sky": (stage_skies().get(donor) or {}).get("sky", ""), "donor_clip": (stage_skies().get(donor) or {}).get("clip", "")}


def nightfx_save(d, s):
    """tracks\\<id>\\nightfx.json (HCT15.NightFx): the panel's switches; the hand-placed "Lights" list is kept as it is."""
    p = os.path.join(d, "nightfx.json")
    nf = jload(p, None) or {}
    if s.get("fireworks"):
        fw = dict(nf.get("Fireworks") or {})
        fw.setdefault("From", 0.0); fw.setdefault("To", 1.0); fw.setdefault("Sound", True); fw.setdefault("Volume", 0.8)
        fw["Every"] = round(max(0.3, min(10.0, float(s.get("fireworksEvery", 1.4)))), 2)
        nf["Fireworks"] = fw
    else:
        nf.pop("Fireworks", None)
    nf["Windows"] = bool(s.get("windows"))
    if s.get("lamps"):
        run = dict((nf.get("Lamps") or [{}])[0])
        run.setdefault("From", 0.0); run.setdefault("To", 1.0); run.setdefault("Side", 2); run.setdefault("Dist", 1.0)
        run.setdefault("H", 5.5); run.setdefault("Colour", "#ffcf80")
        run["Every"] = round(max(0.003, min(0.1, float(s.get("lampsEvery", 0.01)))), 4)
        nf["Lamps"] = [run] + list((nf.get("Lamps") or [])[1:])
    else:
        nf.pop("Lamps", None)
    if not nf.get("Fireworks") and not nf.get("Lamps") and not nf.get("Windows") and not nf.get("Lights"):
        if os.path.exists(p):
            shutil.move(p, trash_path("nightfx", os.path.basename(d)) + ".json")
            return "nightfx.json -> trash (no night effects)"
        return None
    jsave(p, nf)
    return "nightfx.json"


def _amounts(w, key, pair):
    a, b = (max(0.0, min(1.0, float(x))) for x in pair)
    if _curve_pair(w, key) == [round(a, 3), round(b, 3)] and (key in w or key + "V" in w):
        return      # untouched in the panel: keep the file's own curve (it may have more than two keys)
    for k in (key, key + "T", key + "V"):
        w.pop(k, None)
    if abs(a - b) < 1e-4:
        if a > 0.0 or key == "Rain":
            w[key] = round(a, 3)
    else:
        w[key + "T"] = [0.0, 1.0]
        w[key + "V"] = [round(a, 3), round(b, 3)]


def ambient_save(tid, body):
    s = dict(AMBIENT_DEFAULT)
    s.update({k: body[k] for k in AMBIENT_DEFAULT if k in body})
    preset = s["preset"]
    if preset not in AMBIENT_PRESETS:
        raise ValueError(f"unknown time of day '{preset}'")
    snow, ash, aur = [float(x) for x in s["snow"]], [float(x) for x in s["ash"]], float(s["aurora"])
    if preset == "donor" and (max(snow) > 0.01 or max(ash) > 0.01 or aur > 0.01 or max(float(x) for x in s["overcast"]) > 0.01):
        raise ValueError("snow, ash, aurora and clouds need a time of day of the mod's own - pick Day, Night, ... instead of the donor's sky")
    with lock:
        d = track_dir(tid)
        envp = os.path.join(d, "environment.json")
        written = []
        if preset == "donor":
            if os.path.exists(envp):
                dst = trash_path("environment", tid) + ".json"
                shutil.move(envp, dst)
                written.append("environment.json -> trash (the donor's sky)")
        else:
            e = env_module()
            out = e.build(preset, float(s["nightAt"]), s["overcast"], aur, ash, snow)
            out["Editor"] = s
            jsave(envp, out)
            written.append(f"environment.json ({len(out['Curves'])} curves, {preset})")
        wp = os.path.join(d, "weather.json")
        w = jload(wp, None) or {}
        _amounts(w, "Rain", s["rain"])
        _amounts(w, "Snow", snow)
        _amounts(w, "Ash", ash)
        w["Sound"] = bool(s["rainSound"])
        w["Grip"] = round(max(0.0, min(2.0, float(s["rainGrip"]))), 2)
        w["SnowGrip"] = round(max(0.0, min(2.0, float(s["snowGrip"]))), 2)
        w["AshGrip"] = round(max(0.0, min(2.0, float(s["ashGrip"]))), 2)
        if "comment" in body:
            w["Comment"] = str(body["comment"])
        jsave(wp, w)
        written.append("weather.json")
        if any(k in body for k in ("fireworks", "windows", "lamps")):
            nfw = nightfx_save(d, body)
            if nfw: written.append(nfw)
        return {"written": written, **ambient_get(tid)}


_stage_skies = None


def stage_skies():
    """Tools\\assets\\stage_skies.json: what sky each retail stage plays (day, night, sunset, rain...) - what 'the donor
    stage's own sky' means for a track without environment.json."""
    global _stage_skies
    if _stage_skies is None:
        path = find_up(HERE, os.path.join("Tools", "assets", "stage_skies.json"), False) or (TOOLS and os.path.join(TOOLS, "assets", "stage_skies.json"))
        _stage_skies = (jload(path, {}) or {}).get("Stages", {}) if path and os.path.isfile(path) else {}
    return _stage_skies


def ambient_all():
    """Every track's time of day in one table (the "Day / night" dialog): preset, when night falls, its campaigns."""
    camps = list_campaigns()
    skies = stage_skies()
    rows = []
    for t in list_tracks():
        if t.get("error") or t.get("flat_file"):
            continue
        try:
            g = ambient_get(t["id"])
        except Exception as e:      # a broken environment.json must not hide the other 186 tracks
            rows.append({"id": t["id"], "display": t.get("display", t["id"]), "error": str(e)})
            continue
        s = g["settings"]
        rows.append({"id": t["id"], "display": t.get("display", t["id"]), "laps": t.get("laps", 3),
                     "donor": t.get("donor", ""), "donor_sky": (skies.get(t.get("donor", "")) or {}).get("sky", ""),
                     "campaigns": [c["folder"] for c in camps if any(r.get("RaceId") == t["id"] for r in c.get("rounds", []))],
                     "preset": s["preset"], "nightAt": s["nightAt"], "fireworks": s.get("fireworks", False),
                     "windows": s.get("windows", False), "lamps": s.get("lamps", False), "lights": g.get("night_lights", 0)})
    return {"tracks": rows, "presets": AMBIENT_PRESETS}


def ambient_all_save(body):
    """[{id, preset, nightAt}] -> each track's environment.json rebuilt as in the Ambience tab; weather and lights kept."""
    done, skipped = [], []
    for ch in body.get("changes") or []:
        tid = safe_id(ch.get("id"))
        cur = ambient_get(tid)["settings"]
        if cur["preset"] == "custom" and ch.get("preset") == "custom":
            skipped.append(tid); continue
        cur["preset"] = ch.get("preset", cur["preset"])
        if "nightAt" in ch:
            cur["nightAt"] = max(0.2, min(1.0, float(ch["nightAt"])))
        ambient_save(tid, cur)
        done.append(tid)
    return {"saved": done, "skipped": skipped, **ambient_all()}


def trash_path(kind, name):
    os.makedirs(TRASH, exist_ok=True)
    return os.path.join(TRASH, f"{kind}_{safe_id(name)}_{time.strftime('%Y%m%d_%H%M%S')}")


def delete_track(tid):
    with lock:
        d = track_dir(tid)
        if os.path.normpath(d) == os.path.normpath(tracks_dir()) or os.path.normpath(d) == os.path.normpath(CONTENT):
            raise ValueError("this race file does not live in its own folder; delete the file by hand")
        dst = trash_path("track", tid)
        shutil.move(d, dst)
        # campaigns that list it: leave the round (the game skips unknown races), tell the user
        refs = [c["folder"] for c in list_campaigns() if any(r.get("RaceId") == tid for r in c.get("rounds", []))]
        list_tracks(force=True)
        return {"trashed": dst, "campaign_refs": refs}


# ----------------------------------------------------------------------------- campaigns
def campaign_dir(folder):
    d = os.path.join(CONTENT, "campaigns", safe_id(folder))
    if not os.path.isfile(os.path.join(d, "campaign.json")):
        raise FileNotFoundError(f"campaign '{folder}' not found")
    return d


def campaign_add(folder, body):
    with lock:
        d = campaign_dir(folder)
        cj = os.path.join(d, "campaign.json")
        c = jload(cj)
        rid = safe_id(body.get("race_id"))
        rp = race_path(rid)
        race = jload(rp)
        rounds = c.setdefault("Rounds", [])
        existing = next((r for r in rounds if r.get("RaceId") == rid), None)
        title = body.get("title") or race.get("DisplayName") or rid
        rnd = existing or {"RaceId": rid}
        rnd["Title"] = title
        rnd["Laps"] = int(body.get("laps") or race.get("NumberOfLaps") or 3)
        if body.get("subtitle") is not None:
            rnd["Subtitle"] = body.get("subtitle") or ""
        if body.get("description") is not None:
            rnd["Description"] = body.get("description") or ""
        langs = race.get("DisplayNameLangs") or []
        texts = race.get("DisplayNameTexts") or []
        if langs and body.get("use_race_names", True) and not existing:
            rnd["TitleLangs"], rnd["TitleTexts"] = list(langs), list(texts)
        if not existing:
            pos = body.get("position")
            if isinstance(pos, int) and 0 <= pos <= len(rounds):
                rounds.insert(pos, rnd)
            else:
                rounds.append(rnd)
        if not c.get("DefaultRace"):
            c["DefaultRace"] = rid
        jsave(cj, c)
        # the cup: the race's city must be listed for the menus to show it
        cupp = os.path.join(d, "cup.json")
        cup = jload(cupp, None)
        changed = []
        if cup and cup.get("ItemId"):
            cid = race.get("CityId") or ""
            if cid and cid not in (cup.get("CityIds") or []):
                cup.setdefault("CityIds", []).append(cid)
                jsave(cupp, cup)
                changed.append("cup.json")
            if race.get("CupId") != cup["ItemId"]:
                race["CupId"] = cup["ItemId"]
                jsave(rp, race)
                changed.append("race.json CupId")
            cityp = os.path.join(os.path.dirname(rp), "city.json")
            city = jload(cityp, None)
            if city and city.get("CupId") != cup["ItemId"]:
                city["CupId"] = cup["ItemId"]
                jsave(cityp, city)
                changed.append("city.json CupId")
        list_tracks(force=True)
        return {"ok": True, "rounds": len(rounds), "changed": changed}


def campaign_remove(folder, body):
    with lock:
        d = campaign_dir(folder)
        cj = os.path.join(d, "campaign.json")
        c = jload(cj)
        rid = safe_id(body.get("race_id"))
        before = len(c.get("Rounds") or [])
        c["Rounds"] = [r for r in (c.get("Rounds") or []) if r.get("RaceId") != rid]
        if c.get("DefaultRace") == rid:
            c["DefaultRace"] = c["Rounds"][0]["RaceId"] if c["Rounds"] else ""
        jsave(cj, c)
        return {"ok": True, "removed": before - len(c["Rounds"])}


def campaign_reorder(folder, body):
    with lock:
        d = campaign_dir(folder)
        cj = os.path.join(d, "campaign.json")
        c = jload(cj)
        order = body.get("order") or []
        by = {r.get("RaceId"): r for r in (c.get("Rounds") or [])}
        new = [by[r] for r in order if r in by] + [r for r in (c.get("Rounds") or []) if r.get("RaceId") not in order]
        c["Rounds"] = new
        jsave(cj, c)
        return {"ok": True}


# ----------------------------------------------------------------------------- game launch, jobs
def game_running():
    global game_proc
    if game_proc is not None and game_proc.poll() is None:
        return {"running": True, "pid": game_proc.pid, "ours": True}
    game_proc = None
    if os.name == "nt":
        try:
            out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq HorizonChase.exe", "/FO", "CSV", "/NH"],
                                 capture_output=True, text=True, timeout=5).stdout
            if "HorizonChase.exe" in out:
                pid = int(out.split(",")[1].strip('"'))
                return {"running": True, "pid": pid, "ours": False}
        except Exception:
            pass
    return {"running": False}


def launch_game(body):
    global game_proc
    exe = os.path.join(GAME_DIR, "HorizonChase.exe")
    if not os.path.exists(exe):
        raise FileNotFoundError("HorizonChase.exe not found next to HCT15Content (run Tools\\Assemble-Game.ps1)")
    st = game_running()
    if st["running"]:
        raise RuntimeError("the game is already running (pid %d) - close it first" % st["pid"])
    logs = os.path.join(GAME_DIR, "Logs")
    os.makedirs(logs, exist_ok=True)
    args = [exe, "-logFile", os.path.join(logs, "hct15-editor-test.txt")]
    mode = body.get("mode", "race")
    rid = safe_id(body.get("race_id"))
    if mode == "editor":
        args += ["-hct15-editor", rid]
    elif rid:
        args += ["-hct15-race", rid]
        if body.get("car"):
            args += ["-hct15-pick", body["car"]]
    for extra in body.get("extra") or []:
        if isinstance(extra, str) and extra.startswith("-") and " " not in extra:
            args.append(extra)
    game_proc = subprocess.Popen(args, cwd=GAME_DIR)
    return {"pid": game_proc.pid, "args": args[1:]}


def start_job(name, cmd, cwd):
    jid = f"{name}_{int(time.time() * 1000)}"
    job = {"id": jid, "name": name, "lines": [], "done": False, "ok": None, "started": time.time()}
    jobs[jid] = job

    def run():
        try:
            p = subprocess.Popen(cmd, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
                                 encoding="utf-8", errors="replace", bufsize=1)
            for line in p.stdout:
                job["lines"].append(line.rstrip("\n"))
                if len(job["lines"]) > 2000:
                    del job["lines"][:500]
            p.wait()
            job["ok"] = p.returncode == 0
            job["lines"].append(f"[exit {p.returncode}]")
        except Exception as e:
            job["lines"].append("error: " + str(e))
            job["ok"] = False
        job["done"] = True
        list_tracks(force=True)
    threading.Thread(target=run, daemon=True).start()
    return job


# ----------------------------------------------------------------------------- HTTP
class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *a):
        pass

    def _send(self, code, body, ctype="application/json", cache=False):
        data = body if isinstance(body, bytes) else json.dumps(body, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "max-age=3600" if cache else "no-store")
        self.end_headers()
        self.wfile.write(data)

    def _file(self, path, cache=False):
        if not os.path.isfile(path):
            return self._send(404, {"error": "not found: " + os.path.basename(path)})
        with open(path, "rb") as f:
            self._send(200, f.read(), mime_of(path), cache)

    def _json(self):
        n = int(self.headers.get("Content-Length", 0))
        raw = self.rfile.read(n) if n else b""
        return json.loads(raw.decode("utf-8")) if raw else {}

    def _err(self, e):
        tb = traceback.extract_tb(e.__traceback__)
        loc = " -> ".join(f"{os.path.basename(f.filename)}:{f.lineno}" for f in tb[-2:])
        code = 404 if isinstance(e, FileNotFoundError) else 400 if isinstance(e, (ValueError, RuntimeError, KeyError)) else 500
        if code == 500:
            print(f"[api error] {self.path}: {type(e).__name__}: {e}  [{loc}]")
            traceback.print_exc()
        self._send(code, {"error": f"{e}" if code != 500 else f"{type(e).__name__}: {e}  [{loc}]"})

    # ------------------------------------------------------------------ GET
    def do_GET(self):
        try:
            u = urlparse(self.path)
            path = unquote(u.path)
            q = {k: v[0] for k, v in parse_qs(u.query).items()}
            if path in ("/", "/index.html"):
                return self._file(os.path.join(HERE, "index.html"))
            if path.startswith("/js/") or path.startswith("/lib/") or path.startswith("/css/"):
                rel = safe_rel(path)
                return self._file(os.path.join(HERE, rel), cache=path.startswith("/lib/"))
            if path == "/favicon.ico":
                return self._send(204, b"", "image/x-icon")
            if path == "/api/state":
                dump = retail_dump()
                return self._send(200, {
                    "game_dir": GAME_DIR, "content_dir": CONTENT, "has_decoded": bool(DECODED), "has_tools": bool(TOOLS),
                    "exe_exists": os.path.exists(os.path.join(GAME_DIR, "HorizonChase.exe")),
                    "tracks": list_tracks(force=q.get("force") == "1"), "campaigns": list_campaigns(),
                    "retail": dump, "donors": list_donors(), "music": list_music(),
                    "settings": jload(os.path.join(CONTENT, "hct15.settings.json"), {}) or {},
                    "game": game_running(),
                    "extract": dict(zip(("available", "reason"), gameassets.availability(data_dir()) if gameassets else NO_GAMEASSETS)),
                })
            m = re.match(r"^/api/track/([^/]+)$", path)
            if m:
                tid = safe_id(m.group(1))
                rp = race_path(tid)
                d = os.path.dirname(rp)
                photos_dir = os.path.join(d, "photos")
                photos = sorted(f for f in os.listdir(photos_dir)) if os.path.isdir(photos_dir) else []
                files = sorted(f for f in os.listdir(d) if os.path.isfile(os.path.join(d, f)))
                return self._send(200, {
                    "id": tid, "dir": d, "race_file": rp, "race": jload(rp),
                    "scenery": jload(os.path.join(d, "scenery.json"), None) or {"Buildings": [], "Trees": [], "Landmarks": []},
                    "has_osm": os.path.exists(os.path.join(d, "osm_overlay.json")),
                    "has_bak": os.path.exists(rp + ".bak"), "history": history_list(tid),
                    "photos": ["photos/" + f for f in photos if os.path.splitext(f)[1].lower() in (".png", ".jpg", ".jpeg", ".webp")],
                    "files": files, "has_card": "card.png" in files, "has_layout": "layout.png" in files,
                    "campaigns": [c["folder"] for c in list_campaigns() if any(r.get("RaceId") == tid for r in c.get("rounds", []))],
                })
            m = re.match(r"^/api/track/([^/]+)/osm$", path)
            if m:
                return self._file(os.path.join(track_dir(m.group(1)), "osm_overlay.json"))
            if path == "/api/ambient/all":
                return self._send(200, ambient_all())
            m = re.match(r"^/api/track/([^/]+)/ambient$", path)
            if m:
                return self._send(200, ambient_get(safe_id(m.group(1))))
            m = re.match(r"^/api/track/([^/]+)/file/(.+)$", path)
            if m:
                return self._file(os.path.join(track_dir(m.group(1)), safe_rel(m.group(2))))
            m = re.match(r"^/api/track/([^/]+)/history/([^/]+)$", path)
            if m:
                tid, name = safe_id(m.group(1)), m.group(2)
                rp = race_path(tid)
                d = os.path.dirname(rp)
                if name == "bak":
                    race = jload(rp + ".bak")
                    sc = jload(os.path.join(d, "scenery.json.bak"), None)
                    if race is None:
                        raise FileNotFoundError("no .bak original exists yet (it is made on the first save)")
                else:
                    name = re.sub(r"[^0-9_\-]", "", name)
                    race = jload(os.path.join(d, "history", name + ".race.json"))
                    sc = jload(os.path.join(d, "history", name + ".scenery.json"), None)
                    if race is None:
                        raise FileNotFoundError("snapshot not found")
                return self._send(200, {"race": race, "scenery": sc})
            if path == "/api/assets":
                return self._send(200, {"models_dir": models_dir(), "assets": list_assets(), "game_props": list_game_props(),
                                        "budgets": {"tree": 300, "prop": 600, "building": 2000, "landmark": 5000}})
            if path == "/api/asset/file":
                d = asset_folder(q.get("id", ""))
                return self._file(os.path.join(d, safe_rel(q.get("name", ""))))
            m = re.match(r"^/api/gameprops/([^/]+)/(.+)$", path)
            if m:
                return self._file(os.path.join(CONTENT, "assets", "game_props", safe_id(m.group(1)), safe_rel(m.group(2))), cache=True)
            m = re.match(r"^/api/campaign/([^/]+)/file/(.+)$", path)
            if m:
                return self._file(os.path.join(campaign_dir(m.group(1)), safe_rel(m.group(2))))
            if path == "/api/decoded":
                if not DECODED:
                    return self._send(200, {"stages": []})
                return self._send(200, {"stages": sorted(f[:-5] for f in os.listdir(DECODED) if f.endswith(".json"))})
            m = re.match(r"^/api/decoded/([^/]+)$", path)
            if m:
                return self._send(200, decoded_stage(m.group(1)))
            if path == "/api/game/status":
                return self._send(200, game_running())
            m = re.match(r"^/api/job/([^/]+)$", path)
            if m:
                job = jobs.get(m.group(1))
                if not job:
                    raise FileNotFoundError("no such job")
                since = int(q.get("since", 0))
                return self._send(200, {"id": job["id"], "done": job["done"], "ok": job["ok"], "progress": job.get("progress"),
                                        "lines": job["lines"][since:], "total": len(job["lines"])})
            self._send(404, {"error": "not found"})
        except Exception as e:
            self._err(e)

    # ----------------------------------------------------------------- POST
    def do_POST(self):
        try:
            path = unquote(urlparse(self.path).path)
            body = self._json()
            m = re.match(r"^/api/track/([^/]+)/save$", path)
            if m:
                return self._send(200, save_track(safe_id(m.group(1)), body["race"], body.get("scenery")))
            m = re.match(r"^/api/track/([^/]+)/saveas$", path)
            if m:
                return self._send(200, save_as(safe_id(m.group(1)), body.get("new_id"), body.get("display"), body["race"], body.get("scenery")))
            if path == "/api/track/new":
                return self._send(200, new_track(body))
            m = re.match(r"^/api/track/([^/]+)/ambient$", path)
            if m:
                return self._send(200, ambient_save(safe_id(m.group(1)), body))
            if path == "/api/ambient/all":
                return self._send(200, ambient_all_save(body))
            if path == "/api/geometry/spread":
                # push lap sections that are closer than a road width apart; the page re-files its scenery itself
                flat = body.get("flat") or []
                wp = [flat[i:i + 3] for i in range(0, len(flat) - 2, 3)]
                if len(wp) < 10:
                    raise ValueError("not enough waypoints")
                secs = trackgeom.sections_of(CONTENT, body.get("donor"))
                margin = float(body.get("margin", 1.0))
                before = len(trackgeom.overlaps(wp, secs, margin))
                lines = []
                new, remaining, used, k = trackgeom.spread(wp, secs, margin, log=lines.append)
                bad = sum(1 for i in range(len(new)) if abs(trackgeom.turn_deg(new, i)) > 3.5)
                return self._send(200, {"flat": [round(v, 4) for p in new for v in p], "before": before, "remaining": remaining,
                                        "passes": used, "stretch": k, "tight": bad, "lap": round(trackgeom.lap_length(new), 1),
                                        "near": round(trackgeom.overlap_fraction(new, secs, 40) * 100), "far": round(trackgeom.overlap_fraction(new, secs, 100) * 100), "log": lines})
            m = re.match(r"^/api/track/([^/]+)/delete$", path)
            if m:
                return self._send(200, delete_track(safe_id(m.group(1))))
            m = re.match(r"^/api/track/([^/]+)/upload$", path)
            if m:
                d = track_dir(m.group(1))
                written = []
                for f in body.get("files") or []:
                    rel = safe_rel(f["path"])
                    if not rel or os.path.splitext(rel)[1].lower() not in (".png", ".jpg", ".jpeg", ".webp"):
                        raise ValueError("only png/jpg/webp files go into a track folder")
                    full = os.path.join(d, rel)
                    os.makedirs(os.path.dirname(full), exist_ok=True)
                    wbytes(full, base64.b64decode(f["data_b64"]))
                    written.append(rel)
                list_tracks(force=True)
                return self._send(200, {"written": written})
            if path == "/api/assets/import":
                return self._send(200, import_asset(body))
            if path == "/api/asset/meta":
                d = asset_folder(body["id"])
                meta = jload(os.path.join(d, "asset.json"), None) or {}
                for k in ("Name", "Category", "Description", "Scale", "Yaw", "GroundOffset", "Recenter", "DoubleSided"):
                    if k in (body.get("meta") or {}):
                        meta[k] = body["meta"][k]
                meta["Id"] = meta.get("Id") or os.path.basename(d)
                jsave(os.path.join(d, "asset.json"), meta, indent=2)
                return self._send(200, {"ok": True, "asset": describe_asset(d, body["id"].split("/")[0] if "/" in body["id"] else None)})
            if path == "/api/asset/thumb":
                d = asset_folder(body["id"])
                wbytes(os.path.join(d, "thumb.png"), base64.b64decode(body["png_b64"]))
                return self._send(200, {"ok": True})
            if path == "/api/asset/delete":
                d = asset_folder(body["id"])
                dst = trash_path("asset", body["id"].replace("/", "_"))
                shutil.move(d, dst)
                return self._send(200, {"trashed": dst})
            m = re.match(r"^/api/campaign/([^/]+)/(add|remove|reorder)$", path)
            if m:
                fn = {"add": campaign_add, "remove": campaign_remove, "reorder": campaign_reorder}[m.group(2)]
                return self._send(200, fn(m.group(1), body))
            if path == "/api/gameprops/extract":
                ok, why = gameassets.availability(data_dir()) if gameassets else NO_GAMEASSETS
                if not ok:
                    raise RuntimeError(why)
                donor = body.get("donor") or "all"
                stages = "all" if donor == "all" else [safe_id(donor)]
                force = bool(body.get("force", donor != "all"))
                job = start_thread_job("extract", lambda progress, log: gameassets.extract(stages, data_dir(), CONTENT, progress, log, force))
                return self._send(200, {"job": job["id"]})
            if path == "/api/gameprops/thumb":
                d = os.path.join(CONTENT, "assets", "game_props", safe_id(body["donor"]))
                if not os.path.isdir(d):
                    raise FileNotFoundError("no such donor folder")
                wbytes(os.path.join(d, safe_file(body["name"]) + ".png"), base64.b64decode(body["png_b64"]))
                return self._send(200, {"ok": True})
            if path == "/api/game/launch":
                return self._send(200, launch_game(body))
            if path == "/api/open":
                kind = body.get("kind")
                if kind == "track":
                    target = track_dir(body.get("id"))
                elif kind == "asset":
                    target = asset_folder(body.get("id"))
                elif kind == "campaign":
                    target = campaign_dir(body.get("id"))
                elif kind == "models":
                    target = models_dir()
                else:
                    target = CONTENT
                if os.name == "nt":
                    os.startfile(target)  # noqa
                return self._send(200, {"opened": target})
            if path == "/api/osm/generate":
                if not TOOLS or not os.path.exists(os.path.join(TOOLS, "osm_overlay.py")):
                    raise FileNotFoundError("Tools\\osm_overlay.py is not available from here")
                tid = safe_id(body.get("track_id"))
                track_dir(tid)
                cmd = [sys.executable, "-u", os.path.join(TOOLS, "osm_overlay.py"), "--only", tid, "--all"]
                if body.get("force"):
                    cmd.append("--force")
                job = start_job("osm", cmd, os.path.dirname(TOOLS))
                return self._send(200, {"job": job["id"]})
            self._send(404, {"error": "not found"})
        except Exception as e:
            self._err(e)


# ----------------------------------------------------------------------------- main
def find_up(start, rel, is_dir=True):
    d = os.path.abspath(start)
    for _ in range(8):
        p = os.path.join(d, rel)
        if (os.path.isdir(p) if is_dir else os.path.isfile(p)):
            return p
        nd = os.path.dirname(d)
        if nd == d:
            break
        d = nd
    return None


def _pause():
    if os.name == "nt" and sys.stdin and sys.stdin.isatty():
        try:
            input("\nPress Enter to close...")
        except EOFError:
            pass


def find_steam_game():
    r"""The Steam copy of Horizon Chase Turbo (app 389140) with the mod installed, found as the mod's installer finds it:
    Steam's folder from the registry, its libraries from libraryfolders.vdf, the game's folder from appmanifest_389140."""
    if os.name != "nt":
        return None
    import winreg
    roots = []
    for hive, key in ((winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam"), (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Valve\Steam"),
                      (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Valve\Steam")):
        for name in ("SteamPath", "InstallPath"):
            try:
                with winreg.OpenKey(hive, key) as k:
                    roots.append(os.path.normpath(winreg.QueryValueEx(k, name)[0]))
            except OSError:
                pass
    libs = []
    for r in roots:
        if not os.path.isdir(r):
            continue
        libs.append(r)
        vdf = os.path.join(r, "steamapps", "libraryfolders.vdf")
        if os.path.isfile(vdf):
            with open(vdf, encoding="utf-8", errors="replace") as f:
                libs += [os.path.normpath(m.replace("\\\\", "\\")) for m in re.findall(r'"path"\s+"([^"]+)"', f.read())]
    for lib in dict.fromkeys(libs):
        acf = os.path.join(lib, "steamapps", "appmanifest_389140.acf")
        if not os.path.isfile(acf):
            continue
        with open(acf, encoding="utf-8", errors="replace") as f:
            m = re.search(r'"installdir"\s+"([^"]+)"', f.read())
        d = m and os.path.join(lib, "steamapps", "common", m.group(1))
        if d and os.path.isdir(os.path.join(d, "HCT15Content")):
            return d
    return None


def main():
    global GAME_DIR, CONTENT, DECODED, TOOLS, TRASH
    args = sys.argv[1:]
    port = PORT
    open_browser = True
    content = None
    game = None
    i = 0
    while i < len(args):
        a = args[i]
        if a == "--port":
            port = int(args[i + 1]); i += 1
        elif a == "--content":
            content = args[i + 1]; i += 1
        elif a == "--no-browser":
            open_browser = False
        elif not a.startswith("-"):
            game = a
        i += 1
    if game is None:
        # Game\HCT15Tools\TrackEditor\ -> Game\ ; Tools\TrackEditor\ -> ..\Game\
        cand = [os.path.abspath(os.path.join(HERE, "..", "..")), os.path.abspath(os.path.join(HERE, "..", "..", "Game"))]
        game = next((c for c in cand if os.path.isdir(os.path.join(c, "HCT15Content")) or os.path.isfile(os.path.join(c, "HorizonChase.exe"))), None)
    if game is None and content is None:
        game = find_steam_game()
        if game:
            print(f"Horizon Chase Turbo found in Steam: {game}")
    if game is None and content is None:
        print("error: could not find the game with the mod installed. Install HCT 1.6 first (INSTALL.cmd), or run")
        print("  python hct15_editor.py \"<the Horizon Chase Turbo folder>\"   or   --content \"<a HCT15Content folder>\"")
        _pause(); sys.exit(1)
    GAME_DIR = os.path.abspath(game) if game else os.path.dirname(os.path.abspath(content))
    CONTENT = os.path.abspath(content) if content else os.path.join(GAME_DIR, "HCT15Content")
    if not os.path.isdir(CONTENT):
        print(f"error: content folder not found: {CONTENT}")
        _pause(); sys.exit(1)
    # the decoded stages and the Tools\ scripts live in the project, next to Game\ - look up from the game folder
    # first, then from wherever this script runs (Tools\TrackEditor\ inside the project)
    dec_rel = os.path.join("Reference", "Decoders", "TrackDecoder", "Decoded-Tracks", "tracks_json")
    DECODED = find_up(GAME_DIR, dec_rel) or find_up(HERE, dec_rel)
    tools_ok = lambda base: find_up(base, os.path.join("Tools", "osm_overlay.py"), False)
    TOOLS = find_up(GAME_DIR, "Tools") if tools_ok(GAME_DIR) else (find_up(HERE, "Tools") if tools_ok(HERE) else None)
    # deleted tracks/assets: beside the game in the project (HCT15Tools), else beside this script - never inside the
    # content folder (the game would load them) nor loose in a Steam game folder
    TRASH = os.path.join(GAME_DIR, "HCT15Tools", "TrackEditor", "trash") if os.path.isdir(os.path.join(GAME_DIR, "HCT15Tools")) \
        else os.path.join(HERE, "trash")
    srv = None
    for p in range(port, port + 10):
        try:
            srv = ThreadingHTTPServer(("127.0.0.1", p), Handler)
            port = p
            break
        except OSError:
            print(f"port {p} is in use, trying {p + 1}...")
    if srv is None:
        print("error: no free port found. Close other editor windows.")
        _pause(); sys.exit(1)
    url = f"http://127.0.0.1:{port}"     # the numeric address: no localhost/IPv6 lookup surprises in the browser
    n = len(list_tracks())
    print(f"HCT 1.6 Track Editor  -  {url}")
    print(f"  content : {CONTENT}  ({n} tracks)")
    print(f"  game    : {GAME_DIR}" + ("" if os.path.exists(os.path.join(GAME_DIR, 'HorizonChase.exe')) else "  (HorizonChase.exe not found - 'Test in game' is off)"))
    print(f"  decoded : {DECODED or 'not available (new tracks from shipped stages is off)'}")
    print("KEEP THIS WINDOW OPEN while the editor is in use. Ctrl+C stops it.")
    print(f"If no browser tab appears, open {url} yourself (do NOT double-click index.html - it needs this server).")
    if open_browser:
        def _open():
            try:
                if not webbrowser.open(url):
                    print("  (could not start a browser automatically - open the address above by hand)")
            except Exception as ex:
                print(f"  (could not start a browser automatically: {ex} - open the address above by hand)")
        threading.Timer(0.6, _open).start()
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        print("\nEditor stopped.")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:
        print("\n--- The editor crashed with this error: ---")
        traceback.print_exc()
        _pause()
