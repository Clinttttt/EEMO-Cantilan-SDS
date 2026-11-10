"""Give each light-only colour in the office portal's stylesheets a dark alternative without changing its light value.

A colour X becomes var(--dark-<role>, X). The --dark-* properties exist only while the dark theme is on, so in light mode and
in print every declaration resolves to exactly what it was. Stripping the wrappers gives back the original bytes; the script
asserts that for every file before writing it. Safe to run again: anything already wrapped is left alone.

Usage: dark_tokens.py <client-root> [--write]
"""
import collections
import colorsys
import glob
import os
import re
import sys

ROOT = sys.argv[1]
WRITE = "--write" in sys.argv

WRAPPED = re.compile(r"var\(--dark-[a-z0-9-]+, ((?:[^()]|\((?:[^()]|\([^()]*\))*\))*)\)")
VALUE_TOKEN = re.compile(
    r"var\(--dark-[a-z0-9-]+, (?:[^()]|\((?:[^()]|\([^()]*\))*\))*\)"  # already done
    r"|url\((?:[^()]|\([^()]*\))*\)"
    r"|var\(--[a-z0-9-]+(?:,(?:[^()]|\([^()]*\))*)?\)"
    r"|rgba?\([^()]*\)"
    r"|#[0-9a-fA-F]{3,8}\b"
    r"|(?<![-\w])(?:white|black)(?![-\w])"
)

FILL_TOKENS = {"navy": "navy-fill", "navy-2": "navy-2-fill", "navy-3": "navy-3-fill", "accent": "accent-fill",
               "accent-hover": "accent-hover-fill", "green": "green-fill", "red": "red-fill"}
ALWAYS_FILLED = set(FILL_TOKENS) | {"gold", "gold-light", "civic-panel", "civic-panel-deep"}
INK_TOKENS = {"navy", "navy-2", "navy-3", "text"}
DEFINED = set()
stats = collections.Counter()
left = collections.Counter()


def parse(lit):
    lit = lit.strip().lower()
    if lit == "white":
        return 255, 255, 255, 1.0
    if lit == "black":
        return 0, 0, 0, 1.0
    if lit.startswith("#"):
        h = lit[1:]
        if len(h) in (3, 4):
            h = "".join(c * 2 for c in h)
        if len(h) not in (6, 8):
            return None
        a = int(h[6:8], 16) / 255 if len(h) == 8 else 1.0
        return int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a
    m = re.match(r"rgba?\(\s*(\d+)\s*[, ]\s*(\d+)\s*[, ]\s*(\d+)\s*(?:[,/]\s*([\d.]+%?)\s*)?\)", lit)
    if not m:
        return None
    a = m.group(4)
    alpha = 1.0 if a is None else (float(a[:-1]) / 100 if a.endswith("%") else float(a))
    return int(m.group(1)), int(m.group(2)), int(m.group(3)), alpha


def hls(rgba):
    r, g, b, _ = rgba
    h, l, s = colorsys.rgb_to_hls(r / 255, g / 255, b / 255)
    return h * 360, l, s, max(r, g, b) - min(r, g, b)


def hue_bucket(h):
    if h >= 330 or h < 25:
        return "red"
    if h < 70:
        return "amber"
    if h < 180:
        return "green"
    return "blue"


def is_filled(rgba):
    """A background that text sits on as a coloured fill rather than a page surface."""
    _, l, _, chroma = hls(rgba)
    if rgba[3] < 0.5:
        return False
    return l < 0.75 and not (chroma <= 12 and l >= 0.7)


def background_role(rgba):
    h, l, _, chroma = hls(rgba)
    r, g, b, a = rgba
    if a < 1:
        if l < 0.3 and a <= 0.15:
            return "tint"
        if min(r, g, b) >= 240 and a >= 0.5:
            return "surface-muted"
        return None
    if chroma <= 12:
        if l >= 0.995:
            return "surface"
        if l >= 0.955:
            return "surface-muted"
        if l >= 0.88:
            return "surface-sunken"
        if l >= 0.72:
            return "border"
        return None
    if l >= 0.8:
        return hue_bucket(h) + "-tint"
    return None


def text_role(rgba):
    h, l, s, chroma = hls(rgba)
    if l >= 0.75:
        return None  # already light: it sits on a band
    if rgba[3] < 1:
        return "text-tertiary" if l < 0.3 else None
    if s >= 0.35 and l >= 0.27:
        bucket = hue_bucket(h)
        return "accent-text" if bucket == "blue" else bucket + "-text"
    if l < 0.27:
        return "text"
    if l < 0.4:
        return "text-secondary"
    return "text-tertiary"


def border_role(rgba, prop):
    h, l, _, chroma = hls(rgba)
    r, g, b, a = rgba
    if a < 1:
        return "border" if l < 0.3 and a <= 0.3 else None
    if min(r, g, b) >= 250:
        return None if prop.endswith("-top-color") else "surface"  # a spinner's bright arc, or a ring on a light surface
    if l >= 0.68:
        return "border" if chroma <= 30 else hue_bucket(h) + "-border"
    if l < 0.27:
        return "border-strong"
    return None


def declarations(body, base):
    """Yield (prop, start, end) of each declaration's value, skipping comments and anything inside parentheses."""
    i, n = 0, len(body)
    start = 0
    depth = 0
    while i <= n:
        ch = body[i] if i < n else ";"
        if body.startswith("/*", i):
            end = body.find("*/", i + 2)
            i = n if end < 0 else end + 2
            if depth == 0 and not body[start:i - 0].replace("/*", "").strip():
                start = i
            continue
        if ch in "\"'":
            i += 1
            while i < n and body[i] != ch:
                i += 2 if body[i] == "\\" else 1
            i += 1
            continue
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
        elif ch == ";" and depth == 0:
            decl = body[start:i]
            decl_clean = re.sub(r"/\*.*?\*/", lambda m: " " * len(m.group()), decl, flags=re.S)
            colon = decl_clean.find(":")
            if colon > 0:
                yield decl_clean[:colon].strip().lower(), base + start + colon + 1, base + i
            start = i + 1
        i += 1


def leaf_rules(css):
    """Yield (at-rule ancestors, body start, body end) of every rule that holds declarations."""
    i, n = 0, len(css)
    stack = []
    prelude_start = 0
    while i < n:
        if css.startswith("/*", i):
            end = css.find("*/", i + 2)
            i = n if end < 0 else end + 2
            continue
        ch = css[i]
        if ch in "\"'":
            i += 1
            while i < n and css[i] != ch:
                i += 2 if css[i] == "\\" else 1
        elif ch == "{":
            prelude = re.sub(r"/\*.*?\*/", "", css[prelude_start:i], flags=re.S)
            stack.append([prelude.split(";")[-1].strip(), i + 1, True])
            if len(stack) > 1:
                stack[-2][2] = False
            prelude_start = i + 1
        elif ch == "}":
            prelude, start, leaf = stack.pop()
            if leaf:
                yield [p[0] for p in stack], prelude, start, i
            prelude_start = i + 1
        i += 1
    assert not stack, "unbalanced braces"


def context_of(prop):
    if prop.startswith("--"):
        return None
    if prop in ("color", "fill", "stroke", "caret-color", "-webkit-text-fill-color", "text-decoration-color"):
        return "text"
    if prop.startswith("background"):
        return "bg"
    if prop.startswith("border") or prop.startswith("outline") or prop == "column-rule":
        return "border"
    if prop in ("box-shadow", "-webkit-box-shadow"):
        return "shadow"
    return None


def rule_is_filled(css, spans):
    for prop, start, end in spans:
        if context_of(prop) != "bg":
            continue
        # A color-mix over the surface is a soft tint, not a solid fill: its text follows the theme.
        value = re.sub(r"color-mix\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\)", "", css[start:end])
        for m in VALUE_TOKEN.finditer(value):
            token = m.group()
            if token.startswith("var(--dark-"):
                token = WRAPPED.match(token).group(1)
            var = re.match(r"var\(--([a-z0-9-]+)", token)
            if var:
                if var.group(1) in ALWAYS_FILLED:
                    return True
                continue
            rgba = parse(token) if not token.startswith("url") else None
            if rgba and is_filled(rgba):
                return True
    return False


def rewrite_value(value, ctx, prop, filled):
    def replace(m):
        token = m.group()
        if token.startswith(("var(--dark-", "url(")):
            return token
        var = re.fullmatch(r"var\(--([a-z0-9-]+)(?:,\s*(.*))?\)", token, re.S)
        if var:
            name, fallback = var.group(1), var.group(2)
            role = None
            if ctx == "bg":
                role = FILL_TOKENS.get(name)
            elif ctx == "text" and name in ("surface", "bg-card"):
                role = "on-fill"
            elif ctx == "text" and name.startswith("civic-panel") and not filled:
                role = "accent-text"
            elif ctx == "text" and filled and name in INK_TOKENS:
                role = "navy-fill"
            if role:
                stats["var:" + role] += 1
                return f"var(--dark-{role}, {token})"
            if fallback and name not in DEFINED:  # a token nothing declares: its fallback is the colour that renders
                inner = VALUE_TOKEN.sub(replace, fallback)
                return token.replace(fallback, inner, 1)
            return token
        rgba = parse(token)
        if rgba is None:
            left[("unparsed", token)] += 1
            return token
        role = None
        if ctx == "bg":
            role = background_role(rgba)
        elif ctx == "text":
            role = None if filled else text_role(rgba)
        elif ctx == "border":
            role = None if filled and min(rgba[:3]) >= 250 else border_role(rgba, prop)
        elif ctx == "shadow":
            role = "none" if min(rgba[:3]) >= 240 and rgba[3] >= 0.4 else None
        if role is None:
            left[(ctx, re.sub(r"\s+", "", token.lower()))] += 1
            return token
        stats[role] += 1
        return f"var(--dark-{role}, {token})"

    return VALUE_TOKEN.sub(replace, value)


def rewrite(css):
    edits = []
    for ancestors, prelude, start, end in leaf_rules(css):
        if any(re.match(r"@media\s+print", a, re.I) for a in ancestors):
            continue
        if prelude.lower().startswith(("@font-face", "@page")):
            continue
        spans = list(declarations(css[start:end], start))
        filled = rule_is_filled(css, spans)
        # A seal, logo, QR code or signature was drawn for a white ground, and a QR code must stay scannable on one.
        keeps_white_ground = re.search(r"\bimg\b|seal|logo|qr|signature", prelude, re.I) is not None
        for prop, vstart, vend in spans:
            ctx = context_of(prop)
            if ctx is None or (ctx == "bg" and keeps_white_ground):
                continue
            value = css[vstart:vend]
            base = value
            if ctx == "text" and not filled:  # an earlier run took a soft tint for a fill
                base = re.sub(r"var\(--dark-navy-fill, (var\(--[a-z0-9-]+\))\)", r"\1", value)
            new = rewrite_value(base, ctx, prop, filled)
            if new != value:
                edits.append((vstart, vend, new))
    for vstart, vend, new in reversed(edits):
        css = css[:vstart] + new + css[vend:]
    return css


def strip(css):
    while True:
        stripped = WRAPPED.sub(r"\1", css)
        if stripped == css:
            return css
        css = stripped


def main():
    files = [p for p in glob.glob(os.path.join(ROOT, "Components", "**", "*.razor.css"), recursive=True)
             if os.sep + "Payor" + os.sep not in p]
    files.append(os.path.join(ROOT, "wwwroot", "app.css"))
    for path in files:
        with open(path, encoding="utf-8") as f:
            DEFINED.update(re.findall(r"(?:^|[{;\s])--([a-z0-9-]+)\s*:", f.read()))
    changed = 0
    for path in files:
        with open(path, "rb") as f:
            raw = f.read()
        original = raw.decode("utf-8")
        new = rewrite(original)
        assert strip(new) == strip(original), f"light values changed in {path}"
        assert new.count("{") == original.count("{") and new.count("}") == original.count("}"), path
        if new == original:
            continue
        changed += 1
        if WRITE:
            with open(path, "wb") as f:
                f.write(new.encode("utf-8"))
    print(f"{'wrote' if WRITE else 'would change'} {changed} of {len(files)} files, {sum(stats.values())} declarations values")
    print("roles:", ", ".join(f"{k} {v}" for k, v in stats.most_common()))
    for ctx in ("bg", "text", "border", "shadow", "unparsed"):
        items = [(k[1], v) for k, v in left.items() if k[0] == ctx]
        items.sort(key=lambda x: -x[1])
        print(f"left {ctx} {sum(v for _, v in items)}: " + ", ".join(f"{k} {v}" for k, v in items[:40]))


main()
