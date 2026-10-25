r"""
env_timeline.py - a time of day that follows the race: writes tracks\<id>\environment.json as a RECIPE.

2.2.0 (2026-10-22, the mod sanitised of everything taken from the original game): until 2.1.5 this script composed the
sky curves from keyframes dumped out of the game's own environment clips. Now the file only says what the race should
pass through, and HCT15.EnvComposer composes the curves in the game: the donor stage's own daylight is read off the
player's copy of the game as its clip leaves it, every other time of day is the plugin's own palette.

Presets (time = race progress, 0 = start, 1 = finish; --night-at scales them: the end state is reached at that progress):
  day                  the donor's own daylight, constant
  afternoon_to_night   day -> golden hour -> sunset -> dusk -> night; car lights on at 70 %
  sunset_to_night      golden hour -> sunset -> dusk -> night; car lights on at 40 %
  night                a clear moonlit night, car lights on the whole race
  golden_sea           golden hour, constant (the beaches)
  night_to_dawn        night -> first light -> sunrise -> the donor's day; car lights off at 78 %

Modifiers: --overcast START,END (the colours drained to grey, the sun dimmed, the cloud layer on), --ash START,END
(volcanic ash: an ash-brown sky, fog closing in, the snow object on so that Weather.cs runs its soot through it,
CloudTexture ash_clouds.png), --snow START,END (a snow-grey sky, fog closing in, the snow object on for weather.json
Snow's flakes), --aurora STRENGTH (the southern lights on the cloud layer from nightfall, CloudTexture aurora_clouds.png),
--storm START,END (2.2.4, a thunderstorm: the sky, clouds and fog a dark slate, the sun gone, the light dim - Santiago
inundado; the lightning itself is storm.json's, HCT15.Storm).
The textures in HCT15Content\assets\env are drawn by Tools\env_textures.py.

Usage (project root):  python Tools\env_timeline.py cl_illapel_sights [--preset afternoon_to_night] [--night-at 0.34]
                       [--overcast 0.2,0.9] [--aurora 0.9] [--ash 0.3,1.0] [--snow 0,0.6] [--storm 0.8,1.0] [--content <HCT15Content>]
The Track Editor's Ambiente panel calls build() / write() with the same settings.
"""
import argparse
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PRESETS = ["day", "afternoon_to_night", "sunset_to_night", "night", "golden_sea", "night_to_dawn"]


def _pair(x):
    """'0.2,0.9' / 0.5 / [0.2, 0.9] -> (start, end)"""
    if isinstance(x, (list, tuple)):
        v = [float(k) for k in x]
    else:
        v = [float(k) for k in str(x).split(",")]
    return tuple((v + v)[:2])


def build(preset="afternoon_to_night", night_at=1.0, overcast_=0, aurora_=0.0, ash=0, snow=0, storm=0):
    """The environment.json document for these settings (what main() writes; the Track Editor calls this too)."""
    if preset not in PRESETS: raise ValueError("unknown preset: %s" % preset)
    oc, sn, ah, au, st = _pair(overcast_), _pair(snow), _pair(ash), float(aurora_), _pair(storm)
    recipe = {"Preset": preset, "NightAt": round(float(night_at), 4)}
    words = []
    if max(oc) > 0.005: recipe["Overcast"] = [round(oc[0], 3), round(oc[1], 3)]; words.append("overcast %.2f -> %.2f" % oc)
    if max(sn) > 0.005: recipe["Snow"] = [round(sn[0], 3), round(sn[1], 3)]; words.append("snow %.2f -> %.2f" % sn)
    if max(ah) > 0.005: recipe["Ash"] = [round(ah[0], 3), round(ah[1], 3)]; words.append("volcanic ash %.2f -> %.2f" % ah)
    if max(st) > 0.005: recipe["Storm"] = [round(st[0], 3), round(st[1], 3)]; words.append("thunderstorm %.2f -> %.2f" % st)
    out = {"Mode": "progress", "Seconds": 0, "Recipe": recipe}
    if au > 0.005:
        recipe["Aurora"] = round(au, 3)
        recipe["AuroraFrom"] = round(float(night_at) * 0.92, 4)
        out["CloudTexture"] = "aurora_clouds.png"
        words.append("aurora australis %.2f from %.2f" % (au, recipe["AuroraFrom"]))
    if max(ah) > 0.005: out["CloudTexture"] = "ash_clouds.png"
    out["Comment"] = ("time of day = race progress (0 start .. 1 finish): preset '%s', end state at %.2f%s - a recipe, "
                      "composed in the game by HCT15.EnvComposer (the donor's own daylight + the plugin's palette)"
                      % (preset, float(night_at), (", " + ", ".join(words)) if words else ""))
    return out


def write(folder, out):
    """Write tracks/<id>/environment.json - a new file, never a rewrite in place: the plugin's Contenido folders share
    their files by hard link across versions."""
    path = os.path.join(folder, "environment.json")
    if os.path.exists(path): os.remove(path)
    with open(path, "w", encoding="utf-8") as f: json.dump(out, f, indent=1)
    return path


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("track")
    ap.add_argument("--preset", default="afternoon_to_night", choices=PRESETS)
    ap.add_argument("--content", default=os.path.join(ROOT, "Game", "HCT15Content"))
    ap.add_argument("--night-at", type=float, default=1.0, help="race progress where the end state is reached and held (0.34 = night from lap 2 of 3)")
    ap.add_argument("--overcast", default="0", help="how shut the sky is, 0..1 - one number, or START,END to build or clear over the race")
    ap.add_argument("--aurora", type=float, default=0.0, help="southern lights from nightfall on, 0..1")
    ap.add_argument("--snow", default="0", help="falling snow, START,END amounts 0..1 (weather.json Snow carries the flakes)")
    ap.add_argument("--ash", default="0", help="volcanic ash in the air, START,END amounts 0..1")
    ap.add_argument("--storm", default="0", help="a thunderstorm's darkness, START,END 0..1 (2.2.4)")
    a = ap.parse_args()
    out = build(a.preset, a.night_at, a.overcast, a.aurora, a.ash, a.snow, a.storm)
    folder = os.path.join(a.content, "tracks", a.track)
    if not os.path.isdir(folder): sys.exit("no such track folder: " + folder)
    print("wrote %s: %s" % (write(folder, out), out["Comment"]))


if __name__ == "__main__":
    main()
