#!/usr/bin/env python3
"""
Generates the default SVG art for OpenFabric into src/OpenFabric.Game/Assets/{items,buildings,resources,terrain,research,ui}.

One file per thing, named after its id. Replace any file with your own SVG and it is used instead (the game falls back
to nothing for a missing file, so keep the names). Re-running this script OVERWRITES files, so only run it for files you
have not customised (use --only <category> and/or --keep-existing).

Requires: pip install fonttools   (to extract the Material Icons glyph outlines)
Usage:    python3 tools/gen_assets.py [--keep-existing] [--only items,buildings,...]
"""
import argparse, json, os, re, random, zlib, glob

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "src/OpenFabric.Game/Assets")
DATA = os.path.join(ROOT, "data")
FONT = os.path.join(ASSETS, "fonts/MaterialIcons-Regular.ttf")
CODEPOINTS = os.path.join(ASSETS, "fonts/MaterialIcons-Regular.codepoints")

from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen

font = TTFont(FONT)
gs = font.getGlyphSet()
cmap = font.getBestCmap()
names = {}
for line in open(CODEPOINTS):
    p = line.split()
    if len(p) == 2:
        names[p[0]] = int(p[1], 16)


def glyph(name):
    """SVG path data of a Material icon in a 24x24 box (y down)."""
    cp = names.get(name) or names["help_outline"]
    pen = SVGPathPen(gs, ntos=lambda v: ("%.2f" % v).rstrip("0").rstrip("."))
    gs[cmap[cp]].draw(TransformPen(pen, (24 / 512, 0, 0, -24 / 512, 0, 24)))
    return pen.getCommands()


def jload(name):
    s = open(os.path.join(DATA, name)).read()
    s = re.sub(r"^\s*//.*$", "", s, flags=re.M)
    return json.loads(s)


def rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def hexc(c):
    return "#%02x%02x%02x" % tuple(max(0, min(255, int(v))) for v in c)


def shade(h, f):
    return hexc(tuple(v * f for v in rgb(h)))


def mix(h, other, t):
    a, b = rgb(h), rgb(other)
    return hexc(tuple(a[i] + (b[i] - a[i]) * t for i in range(3)))


def rng(key):
    return random.Random(zlib.crc32(key.encode()))


def write(cat, name, svg, keep):
    d = os.path.join(ASSETS, cat)
    os.makedirs(d, exist_ok=True)
    p = os.path.join(d, name + ".svg")
    if keep and os.path.exists(p):
        return 0
    open(p, "w").write(svg)
    return 1


SVG_HEAD = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 %s %s" width="%s" height="%s">'


# ------------------------------------------------------------------------------------------------ items / ui / research
def item_svg(icon, color):
    g = glyph(icon)
    return (SVG_HEAD % (24, 24, 64, 64) +
            f'<path d="{g}" fill="none" stroke="#000" stroke-opacity=".55" stroke-width="1.4" stroke-linejoin="round"/>'
            f'<path d="{g}" fill="{color}"/></svg>\n')


def ui_svg(icon):
    return SVG_HEAD % (24, 24, 64, 64) + f'<path d="{glyph(icon)}" fill="#ffffff"/></svg>\n'


def research_svg(icon, tier):
    accent = ["#7bd88f", "#7bd8d0", "#7ba8d8", "#9c7bd8", "#d87bc2", "#d8a67b", "#d8d87b"][tier % 7]
    s = 24 * 0.62
    return (SVG_HEAD % (24, 24, 64, 64) +
            f'<circle cx="12" cy="12" r="11" fill="#1d2536" stroke="{accent}" stroke-width="1.5"/>'
            f'<g transform="translate({12 - s / 2},{12 - s / 2}) scale({s / 24})"><path d="{glyph(icon)}" fill="{accent}"/></g></svg>\n')


# ------------------------------------------------------------------------------------------------ buildings
DIRECTIONAL = {"belt", "splitter", "merger", "underground", "loader"}


def building_svg(b):
    u = 32
    w, h = b["w"] * u, b["h"] * u
    c = b.get("color", "#888888")
    kind = b["kind"]
    out = [SVG_HEAD % (w, h, w * 2, h * 2)]
    cx, cy = w / 2, h / 2
    if kind == "belt":
        out.append(f'<rect width="{w}" height="{h}" fill="{shade(c, .30)}"/>')
        out.append(f'<rect y="2" width="{w}" height="3" fill="{shade(c, .7)}"/><rect y="{h - 5}" width="{w}" height="3" fill="{shade(c, .7)}"/>')
        for x in (6, 17):  # chevrons pointing east
            out.append(f'<path d="M{x} 9 L{x + 9} 16 L{x} 23 L{x + 4} 23 L{x + 13} 16 L{x + 4} 9 Z" fill="{mix(c, "#ffffff", .35)}" opacity=".9"/>')
    elif kind == "pole":
        out.append(f'<circle cx="{cx}" cy="{cy}" r="9" fill="{shade(c, .35)}" stroke="#000" stroke-opacity=".5" stroke-width="1.5"/>'
                   f'<circle cx="{cx}" cy="{cy}" r="5" fill="{c}"/><circle cx="{cx - 1.5}" cy="{cy - 1.5}" r="1.8" fill="#fff" opacity=".6"/>')
    elif kind == "pipe":
        out.append(f'<rect x="8" y="8" width="16" height="16" rx="4" fill="{shade(c, .5)}" stroke="{c}" stroke-width="2"/>'
                   f'<circle cx="{cx}" cy="{cy}" r="4" fill="{shade(c, .25)}"/>')
    else:
        out.append(f'<rect x="1" y="1" width="{w - 2}" height="{h - 2}" rx="5" fill="{shade(c, .24)}"/>')
        out.append(f'<rect x="3" y="3" width="{w - 6}" height="{h - 6}" rx="4" fill="{shade(c, .42)}"/>')
        out.append(f'<rect x="1" y="1" width="{w - 2}" height="{h - 2}" rx="5" fill="none" stroke="{shade(c, 1.0)}" stroke-width="1.6"/>')
        # panel lines + bolts give machines some character
        if b["w"] * b["h"] >= 2:
            for bx, by in ((6, 6), (w - 6, 6), (6, h - 6), (w - 6, h - 6)):
                out.append(f'<circle cx="{bx}" cy="{by}" r="1.6" fill="{shade(c, .9)}"/>')
        if b["w"] * b["h"] >= 4:
            out.append(f'<rect x="10" y="{h - 12}" width="{w - 20}" height="3" rx="1.5" fill="{shade(c, .22)}"/>')
        s = min(w, h) * 0.58
        gsz = s / 24
        out.append(f'<g transform="translate({cx - s / 2},{cy - s / 2}) scale({gsz})">'
                   f'<path d="{glyph(b["icon"])}" fill="none" stroke="#000" stroke-opacity=".45" stroke-width="{1.2 / gsz * .5}"/>'
                   f'<path d="{glyph(b["icon"])}" fill="#ffffff" opacity=".93"/></g>')
        if kind in DIRECTIONAL:
            out.append(f'<path d="M{w - 11} {cy - 6} L{w - 3} {cy} L{w - 11} {cy + 6} Z" fill="{mix(c, "#ffffff", .5)}"/>')
    out.append("</svg>\n")
    return "".join(out)


# ------------------------------------------------------------------------------------------------ resources
def rock(r, cx, cy, rad, color):
    n = 8
    pts = []
    for i in range(n):
        a = i / n * 6.2832
        rr = rad * r.uniform(.72, 1.0)
        pts.append((cx + rr * __import__("math").cos(a), cy + rr * __import__("math").sin(a) * .85))
    P = lambda ps: " ".join("%.1f,%.1f" % p for p in ps)
    hi = [(cx + (x - cx) * .55 - rad * .12, cy + (y - cy) * .55 - rad * .18) for x, y in pts]
    return (f'<polygon points="{P(pts)}" fill="{color}" stroke="{shade(color, .45)}" stroke-width="1.6" stroke-linejoin="round"/>'
            f'<polygon points="{P(hi)}" fill="{mix(color, "#ffffff", .4)}" opacity=".85"/>')


def resource_svg(res):
    item, color = res["item"], res["color"]
    r = rng("res:" + item)
    out = [SVG_HEAD % (64, 64, 128, 128)]
    if res.get("kind") == "fluid":
        if "oil" in item:
            out.append('<ellipse cx="32" cy="36" rx="22" ry="16" fill="#120a1f" stroke="#000" stroke-width="1.5"/>'
                       '<ellipse cx="26" cy="31" rx="9" ry="4" fill="#6a4aa8" opacity=".55"/><ellipse cx="38" cy="40" rx="6" ry="2.5" fill="#3b7bbf" opacity=".45"/>')
        elif "gas" in item:
            out.append('<ellipse cx="32" cy="40" rx="18" ry="11" fill="#3a3f48" stroke="#000" stroke-width="1.5"/><ellipse cx="32" cy="38" rx="11" ry="6" fill="#15181d"/>'
                       + "".join(f'<circle cx="{26 + i * 6}" cy="{26 - i * 4 + r.randint(-2, 2)}" r="{3 + i}" fill="#cfe8ff" opacity="{.5 - i * .1:.2f}"/>' for i in range(4)))
        else:
            out.append('<ellipse cx="32" cy="40" rx="18" ry="11" fill="#3a2418" stroke="#000" stroke-width="1.5"/><ellipse cx="32" cy="39" rx="11" ry="6" fill="#ff7a2a"/>'
                       '<ellipse cx="32" cy="38" rx="6" ry="3" fill="#ffd45a"/>'
                       + "".join(f'<path d="M{24 + i * 8} 30 q-4 -8 0 -14 q4 -6 0 -12" stroke="#e8e8e8" stroke-width="3" fill="none" opacity=".4" stroke-linecap="round"/>' for i in range(3)))
    else:
        spots = [(22, 24, 13), (43, 28, 11), (30, 45, 12), (49, 47, 7)]
        for x, y, rad in spots:
            out.append(rock(r, x + r.uniform(-2, 2), y + r.uniform(-2, 2), rad, color))
    out.append("</svg>\n")
    return "".join(out)


# ------------------------------------------------------------------------------------------------ terrain (tileable 64x64)
def terrain_svg(name):
    r = rng("terrain:" + name)
    base = {"deep_water": "#1b3b6f", "water": "#2f6db0", "sand": "#d9c88f", "grass": "#5f9a4a",
            "forest": "#2f6b3a", "rock": "#7b7b80", "cliff": "#3f3f48", "snow": "#eef3f7"}[name]
    o = [SVG_HEAD % (64, 64, 64, 64), f'<rect width="64" height="64" fill="{base}"/>']
    M = 5  # keep details away from the edges so the tile repeats cleanly
    rp = lambda: (r.uniform(M, 64 - M), r.uniform(M, 64 - M))
    if name == "grass":
        for _ in range(26):
            x, y = rp(); hgt = r.uniform(3, 6); col = shade(base, r.uniform(.78, 1.18))
            o.append(f'<path d="M{x:.1f} {y:.1f} l-1.2 -{hgt:.1f} l2.4 {hgt * .2:.1f} Z" fill="{col}"/>')
        for _ in range(3):
            x, y = rp(); o.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="1.1" fill="{r.choice(["#f4e04a", "#f6f6f6", "#f09ad0"])}"/>')
    elif name == "forest":
        for x, y, rad in ((18, 20, 12), (44, 26, 13), (28, 47, 12), (52, 52, 8)):
            o.append(f'<circle cx="{x + 2}" cy="{y + 3}" r="{rad}" fill="#173a20" opacity=".7"/>')
            o.append(f'<circle cx="{x}" cy="{y}" r="{rad}" fill="{shade(base, .85)}" stroke="#14301a" stroke-width="1"/>')
            o.append(f'<circle cx="{x - rad * .3:.1f}" cy="{y - rad * .3:.1f}" r="{rad * .55:.1f}" fill="{shade(base, 1.3)}" opacity=".8"/>')
    elif name == "sand":
        for _ in range(22):
            x, y = rp(); o.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{r.uniform(.5, 1.4):.1f}" fill="{shade(base, r.uniform(.8, 1.12))}"/>')
        for _ in range(4):
            x, y = rp(); o.append(f'<path d="M{x - 7:.1f} {y:.1f} q7 -4 14 0" stroke="{shade(base, .86)}" stroke-width="1.2" fill="none"/>')
    elif name in ("water", "deep_water"):
        lt = mix(base, "#ffffff", .22 if name == "water" else .12)
        for _ in range(7):
            x, y = rp(); o.append(f'<path d="M{x - 8:.1f} {y:.1f} q4 -3 8 0 t8 0" stroke="{lt}" stroke-width="1.4" fill="none" opacity=".75" stroke-linecap="round"/>')
    elif name in ("rock", "cliff"):
        for _ in range(9):
            x, y = rp(); rad = r.uniform(5, 10)
            pts = " ".join("%.1f,%.1f" % (x + rad * r.uniform(.6, 1) * __import__("math").cos(a / 6 * 6.2832), y + rad * r.uniform(.6, 1) * __import__("math").sin(a / 6 * 6.2832) * .8) for a in range(6))
            o.append(f'<polygon points="{pts}" fill="{shade(base, r.uniform(.8, 1.25))}" stroke="{shade(base, .6)}" stroke-width="1"/>')
        if name == "cliff":
            for _ in range(4):
                x, y = rp(); o.append(f'<path d="M{x - 9:.1f} {y:.1f} l4 -3 5 3 l5 -3 5 3" stroke="#14141a" stroke-width="1.6" fill="none"/>')
    elif name == "snow":
        for _ in range(12):
            x, y = rp(); o.append(f'<ellipse cx="{x:.1f}" cy="{y:.1f}" rx="{r.uniform(3, 8):.1f}" ry="{r.uniform(1.5, 3):.1f}" fill="#c9d9e8" opacity=".6"/>')
        for _ in range(8):
            x, y = rp(); o.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r=".9" fill="#ffffff"/>')
    o.append("</svg>\n")
    return "".join(o)


# ------------------------------------------------------------------------------------------------ main
def ui_names():
    used = set()
    lit = re.compile(r'"([a-z][a-z0-9_]*)"')
    for f in glob.glob(os.path.join(ROOT, "src/OpenFabric.Game/**/*.cs"), recursive=True):
        if "/obj/" in f or "/bin/" in f:
            continue
        for line in open(f):
            if not re.search(r"[Ii]con|Button\(|CatIcons|toolbar|Tab", line):
                continue
            for m in lit.findall(line):
                if m in names:
                    used.add(m)
    used.update(["help_outline", "close", "person_pin_circle", "chevron_right", "arrow_forward", "arrow_back", "arrow_upward", "arrow_downward",
                 "link_off", "lightbulb", "groups", "person", "search", "sort", "check", "lock", "navigation"])
    return sorted(n for n in used if n in names)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--keep-existing", action="store_true")
    ap.add_argument("--only", default="items,buildings,resources,terrain,research,ui")
    a = ap.parse_args()
    only = set(a.only.split(","))
    k = a.keep_existing
    n = 0
    if "items" in only:
        for it in jload("items.json"):
            n += write("items", it["id"], item_svg(it.get("icon", "help_outline"), it.get("color", "#ffffff")), k)
    if "buildings" in only:
        for b in jload("buildings.json"):
            n += write("buildings", b["id"], building_svg(b), k)
    if "resources" in only:
        for r_ in jload("resources.json"):
            n += write("resources", r_["item"], resource_svg(r_), k)
    if "terrain" in only:
        for t in ("deep_water", "water", "sand", "grass", "forest", "rock", "cliff", "snow"):
            n += write("terrain", t, terrain_svg(t), k)
    if "research" in only:
        for rs in jload("research.json"):
            n += write("research", rs["id"], research_svg(rs.get("icon", "science"), rs.get("tier", 0)), k)
    if "ui" in only:
        for nm in ui_names():
            n += write("ui", nm, ui_svg(nm), k)
    print("wrote", n, "files into", ASSETS)


if __name__ == "__main__":
    main()
