#!/usr/bin/env python3
"""HUD·인터페이스 목업 생성기.

    python3 tools/hud_mock.py        # docs/img/ui_*.svg 다섯 장을 다시 그린다

docs/09_ui_hud.md의 그림 원본이다. 배치와 상태를 고치면 여기서 고치고 다시 돌린다.
치수는 1280x720 기준이다. 색은 문서 11장의 표와 같다.
"""
import os

W, H = 1280, 720
OUT = os.path.join(os.path.dirname(__file__), "..", "docs", "img")
FONT = "'Noto Sans KR','NanumGothic','Malgun Gothic','Apple SD Gothic Neo',sans-serif"

BG, PANEL, LINE = "#141a22", "#202834", "#4a5a6e"
TEXT, MUTED = "#e8ecf1", "#93a1b2"
GOLD, AMBER, RED, MAGENTA, FROST, VIRT = "#e2b93b", "#e8963a", "#d2553f", "#c0489b", "#7cc4e8", "#6fd3c7"
IRON, CRYSTAL = "#8f9bb0", "#a98be0"


class Svg:
    def __init__(self):
        self.o = []
        self.defs = []

    def add(self, s):
        self.o.append(s)

    def rect(self, x, y, w, h, fill=PANEL, stroke=None, r=0, sw=1, op=None, dash=None):
        a = f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{r}" fill="{fill}"'
        if stroke:
            a += f' stroke="{stroke}" stroke-width="{sw}"'
        if op is not None:
            a += f' opacity="{op}"'
        if dash:
            a += f' stroke-dasharray="{dash}"'
        self.add(a + "/>")

    def text(self, x, y, s, size=13, fill=TEXT, anchor="start", bold=False, op=None):
        w = ' font-weight="700"' if bold else ""
        o = f' opacity="{op}"' if op is not None else ""
        s = s.replace("&", "&amp;").replace("<", "&lt;")
        self.add(f'<text x="{x}" y="{y}" font-size="{size}" fill="{fill}" text-anchor="{anchor}"{w}{o}>{s}</text>')

    def line(self, x1, y1, x2, y2, stroke=LINE, sw=1, dash=None, op=None):
        d = f' stroke-dasharray="{dash}"' if dash else ""
        o = f' opacity="{op}"' if op is not None else ""
        self.add(f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{stroke}" stroke-width="{sw}"{d}{o}/>')

    def circle(self, x, y, r, fill="none", stroke=None, sw=1, op=None, dash=None):
        a = f'<circle cx="{x}" cy="{y}" r="{r}" fill="{fill}"'
        if stroke:
            a += f' stroke="{stroke}" stroke-width="{sw}"'
        if op is not None:
            a += f' opacity="{op}"'
        if dash:
            a += f' stroke-dasharray="{dash}"'
        self.add(a + "/>")

    def path(self, d, fill="none", stroke=None, sw=1, op=None, dash=None):
        a = f'<path d="{d}" fill="{fill}"'
        if stroke:
            a += f' stroke="{stroke}" stroke-width="{sw}" stroke-linejoin="round" stroke-linecap="round"'
        if op is not None:
            a += f' opacity="{op}"'
        if dash:
            a += f' stroke-dasharray="{dash}"'
        self.add(a + "/>")

    def save(self, name, w=W, h=H):
        head = (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}" '
                f'font-family="{FONT}">')
        body = head + "<defs>" + "".join(self.defs) + "</defs>" + "\n".join(self.o) + "</svg>\n"
        os.makedirs(OUT, exist_ok=True)
        with open(os.path.join(OUT, name), "w", encoding="utf-8") as f:
            f.write(body)
        print("wrote", name)


# ---------------------------------------------------------------- 작은 부품

def diamond(s, x, y, r, fill="none", stroke=None, sw=1.5, op=None, dash=None):
    s.path(f"M{x},{y-r} L{x+r},{y} L{x},{y+r} L{x-r},{y} Z", fill, stroke, sw, op, dash)


def star4(s, x, y, r, fill="none", stroke=None, sw=1.5, op=None):
    q = r * 0.32
    s.path(f"M{x},{y-r} L{x+q},{y-q} L{x+r},{y} L{x+q},{y+q} L{x},{y+r} L{x-q},{y+q} L{x-r},{y} L{x-q},{y-q} Z",
           fill, stroke, sw, op)


def lock(s, x, y, c=MUTED):
    s.rect(x - 5, y - 2, 10, 8, c, r=1.5)
    s.path(f"M{x-3},{y-2} v-3 a3,3 0 0 1 6,0 v3", stroke=c, sw=1.6)


def shield(s, x, y, c=TEXT, fill="none"):
    s.path(f"M{x-8},{y-9} h16 v8 q0,8 -8,12 q-8,-4 -8,-12 Z", fill, c, 1.8)


def bell_icon(s, x, y, c=GOLD, cracked=False):
    s.circle(x, y, 17, PANEL, c, 2)
    s.path(f"M{x-8},{y+5} q2,-3 2,-8 a6,6 0 0 1 12,0 q0,5 2,8 Z", c)
    s.circle(x, y + 8, 2.2, c)
    if cracked:
        s.path(f"M{x-1},{y-10} l3,6 l-5,4 l4,7", stroke=PANEL, sw=2.2)


def res_icon(s, kind, x, y):
    if kind == "ore":
        diamond(s, x, y, 7, GOLD)
    elif kind == "iron":
        s.rect(x - 6, y - 6, 12, 12, IRON, r=2)
    elif kind == "crystal":
        s.path(f"M{x},{y-8} L{x+6},{y-2} L{x},{y+8} L{x-6},{y-2} Z", CRYSTAL)
    elif kind == "magic":
        s.circle(x, y, 6, MAGENTA)


def key(s, x, y, label, w=None, c=TEXT):
    """입력 표시. 모든 화면에서 같은 모양."""
    w = w or max(20, 9 * len(label) + 8)
    s.rect(x, y, w, 18, "#0f141b", c, r=4, sw=1.2)
    s.text(x + w / 2, y + 13.5, label, 11, c, "middle", True)
    return w


def chip(s, x, y, label, c=MUTED, fill="none"):
    w = 12 * len(label) + 14
    s.rect(x, y, w, 20, fill, c, r=10)
    s.text(x + w / 2, y + 14.5, label, 11.5, c, "middle")
    return w


def callout(s, n, x, y):
    s.circle(x, y, 11, "#0b0f14", GOLD, 1.5)
    s.text(x, y + 4.5, str(n), 12, GOLD, "middle", True)


def hatch(s, pid, c, bg="none"):
    s.defs.append(f'<pattern id="{pid}" width="6" height="6" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">'
                  f'<rect width="6" height="6" fill="{bg}"/><rect width="2.5" height="6" fill="{c}"/></pattern>')


# ---------------------------------------------------------------- HUD 부품

def wall_hp(s, x, y, hp=820, mx=1000, ghost=0, flawless=None, debt=None, locked=0):
    """왼쪽 위. 성벽 내구도 + 흠 없음 + 부채."""
    bw = 220
    ph = 44 + (24 if flawless is not None else 0) + (20 if debt is not None else 0)
    s.rect(x - 8, y - 8, bw + 60, ph, "#0b0f14", r=8, op=0.55)
    shield(s, x + 10, y + 12, GOLD)
    s.text(x + 26, y + 9, "성벽", 11, MUTED)
    s.rect(x + 26, y + 14, bw, 12, "#0b0f14", LINE, r=3)
    f = bw * hp / mx
    if ghost:
        s.rect(x + 26, y + 14, bw * (hp + ghost) / mx, 12, "#f3e9d0", r=3)
    s.rect(x + 26, y + 14, f, 12, GOLD if hp / mx > 0.3 else RED, r=3)
    if locked:
        lw = bw * locked
        s.rect(x + 26 + bw - lw, y + 14, lw, 12, "url(#hx_m)", MAGENTA, r=3)
    s.text(x + 26 + bw, y + 9, f"{hp} / {mx}", 12, TEXT, "end", True)
    cx = x + 26
    if flawless is not None:
        c = GOLD if flawless else MUTED
        s.circle(cx + 5, y + 44, 4, c if flawless else "none", c, 1.5)
        s.text(cx + 16, y + 48, "흠 없음" if flawless else "흠 없음 깨짐", 11, c)
        s.text(cx + 62, y + 48, "고요 +10초" if flawless else "", 11, MUTED)
    if debt is not None:
        dy = y + (70 if flawless is not None else 48)
        s.text(x + 26, dy + 1, "부채", 10.5, MAGENTA)
        s.rect(x + 54, dy - 6, 140, 6, "#0b0f14", LINE, r=2)
        s.rect(x + 54, dy - 6, 140 * debt, 6, MAGENTA, r=2)
        for t in (0.33, 0.66):
            s.line(x + 54 + 140 * t, dy - 9, x + 54 + 140 * t, dy + 3, TEXT, 1.2)
        s.text(x + 200, dy + 1, f"{int(debt*100)}%", 10.5, MAGENTA)


def resources(s, x, y, items):
    """오른쪽 위. 가진 자원만 보인다."""
    w = 74 * len(items)
    s.rect(x - w - 8, y - 8, w + 16, 34, "#0b0f14", r=8, op=0.55)
    cx = x - w
    for kind, n in items:
        res_icon(s, kind, cx + 10, y + 9)
        s.text(cx + 24, y + 14, str(n), 15, TEXT, bold=True)
        cx += 74


def bell(s, cx, y, state, label, big, fill=0.0, color=GOLD, alarm=None, ret=None, margin=None,
         cracked=False, coarse=False, ticks=0, sub=None):
    """가운데 위. 한 자리에서 '지금의 위협'을 말한다.
    fill 0~1, alarm=경보 구간 비율, ret=귀환선 위치 비율, margin=(글, 색)."""
    bw = 260
    x0 = cx - bw / 2
    s.rect(x0 - 34, y - 22, bw + 110, 58, "#0b0f14", r=10, op=0.55)
    bell_icon(s, x0 - 8, y + 7, color, cracked)
    s.text(x0 + 16, y - 4, label, 12, color, bold=True)
    if sub:
        s.text(x0 + bw, y - 4, sub, 11, MUTED, "end")
    s.rect(x0 + 14, y + 2, bw - 14, 12, "#0b0f14", LINE, r=3)
    tw = bw - 14
    if coarse:
        for i in range(3):
            on = i < round(fill * 3)
            s.rect(x0 + 16 + i * (tw / 3), y + 4, tw / 3 - 4, 8, color if on else "none", LINE, r=2, op=0.6 if on else 1)
    else:
        if alarm:
            s.rect(x0 + 14, y + 2, tw * alarm, 12, "url(#hx_a)", r=3)
        s.rect(x0 + 14, y + 2, tw * fill, 12, color, r=3, op=0.95)
        for i in range(1, ticks):
            s.line(x0 + 14 + tw * i / ticks, y + 2, x0 + 14 + tw * i / ticks, y + 14, "#0b0f14", 1.5)
    s.text(x0 + bw + 10, y + 15, big, 22, TEXT if color != RED else "#ffd9d0", bold=True)
    if ret is not None:
        rx = x0 + 14 + tw * ret
        s.line(rx, y - 2, rx, y + 20, TEXT, 2)
        s.path(f"M{rx-5},{y+20} h10 v9 h-10 Z", "#0b0f14", TEXT, 1.4)   # 뒷문 표식
        s.path(f"M{rx-2},{y+29} v-5 h4 v5", stroke=TEXT, sw=1.2)
        if margin:
            s.text(rx + 10, y + 30, margin[0], 12, margin[1], bold=True)


def wall_strip(s, cx, y, lanes=None, hot=(), idle=False):
    """성벽 띠. 곶에서만. 레인마다 가장 높은 적의 높이."""
    w, h = 260, 40
    x0 = cx - w / 2
    s.rect(x0 - 6, y - 4, w + 12, h + 8, "#0b0f14", r=6, op=0.55)
    zw = w / 3
    for i in range(3):
        c = RED if i in hot else LINE
        s.rect(x0 + i * zw + 1, y, zw - 2, 7, RED if i in hot else "#3a4656", c, r=1.5)
        s.rect(x0 + i * zw + 1, y + 9, zw - 2, h - 9, "#1a212b", c if i in hot else None, r=1.5, op=0.9)
    if idle:
        s.text(cx, y + 30, "이상 없음", 11, MUTED, "middle")
        return
    lw = w / 6
    for i, (hgt, n) in enumerate(lanes):
        lx = x0 + lw * i + lw / 2
        s.line(lx, y + 10, lx, y + h - 1, "#2c3642", 1)
        if n:
            ey = y + h - 3 - (h - 14) * hgt
            c = "#ffffff" if hgt > 0.83 else RED
            s.circle(lx, ey, 3.6, RED, c if hgt > 0.83 else None, 1.4)
            if n > 1:
                s.text(lx + 6, ey + 4, str(n), 9.5, TEXT)


def slot(s, x, y, keyl, name=None, sz=52, cd=None, locked=None, sigil=None, runes=0, charge=None,
         dim=False, ring=None):
    """기술 칸. 아이콘 + 키 + 상태."""
    s.rect(x, y, sz, sz, "#0f141b", LINE if not ring else ring, r=7, sw=2 if ring else 1.4)
    if name and not locked:
        s.text(x + sz / 2, y + sz / 2 + 2, name, 11, TEXT if not dim else MUTED, "middle")
    if cd:
        s.rect(x + 1, y + 1, sz - 2, (sz - 2) * 0.62, "#000", r=6, op=0.62)
        s.text(x + sz / 2, y + sz / 2 + 7, str(cd), 20, TEXT, "middle", True)
    if locked:
        lock(s, x + sz / 2, y + sz / 2 - 6)
        s.text(x + sz / 2, y + sz / 2 + 16, locked, 10.5, MUTED, "middle")
    if sigil:
        s.path(f"M{x+sz-14},{y} h7 a7,7 0 0 1 7,7 v7 Z", sigil)
    for i in range(runes):
        s.circle(x + 8 + i * 9, y + sz - 7, 3, CRYSTAL)
    if charge is not None:
        s.rect(x + 3, y + sz - 6, (sz - 6), 3, "#0b0f14")
        s.rect(x + 3, y + sz - 6, (sz - 6) * charge, 3, GOLD)
    kw = max(20, 9 * len(keyl) + 8)
    key(s, x + sz / 2 - kw / 2, y - 12, keyl)


def skillbar(s, cy, hp=72, mining=False):
    """가운데 아래. 왕 체력 + 기본기 + 기술 + 궁극 + 대시."""
    x = 640 - 232
    s.rect(x - 14, cy - 42, 492, 104, "#0b0f14", r=10, op=0.55)
    s.text(x, cy - 24, "왕", 11, MUTED)
    s.rect(x + 18, cy - 32, 300, 9, "#0b0f14", LINE, r=3)
    s.rect(x + 18, cy - 32, 300 * hp / 100, 9, "#c9d6e2", r=3)
    s.text(x + 326, cy - 23, f"{hp}", 12, TEXT, bold=True)
    y = cy
    n = ("직선 5칸", "원형 2칸") if mining else ("검기", "내려찍기")
    slot(s, x, y, "LMB", "채굴" if mining else "베기", 48)
    slot(s, x + 56, y, "RMB", "패링", 48, dim=mining)
    slot(s, x + 128, y, "Q", n[0], sigil=FROST, runes=1)
    slot(s, x + 188, y, "E", n[1], cd=None if mining else 3)
    slot(s, x + 248, y, "R", locked="2장")
    slot(s, x + 324, y, "F", locked="3장")
    slot(s, x + 400, y, "Space", "대시", 48)
    return x


def topbar(s, active):
    """왕좌 메뉴 공통 머리."""
    s.rect(0, 0, W, H, "#10151c")
    s.rect(0, 0, W, 56, "#0b0f14")
    tabs = ["각성", "성", "왕", "성물", "기록"]
    key(s, 24, 19, "Q")
    x = 60
    for t in tabs:
        on = t == active
        tw = 22 * len(t) + 36
        if on:
            s.rect(x, 10, tw, 36, PANEL, GOLD, r=8, sw=1.5)
        s.text(x + tw / 2, 34, t, 16, GOLD if on else MUTED, "middle", on)
        x += tw + 6
    key(s, x + 4, 19, "E")
    s.text(x + 44, 33, "왕좌 · 고요 · 정지", 12, MUTED)
    resources(s, W - 24, 18, [("ore", 124), ("iron", 18), ("crystal", 3)])


def hintbar(s, items, y=H - 30):
    """맨 아래 입력 안내. 어느 화면이든 같은 자리."""
    s.rect(0, y - 8, W, 38, "#0b0f14")
    x = 24
    for k, label in items:
        x += key(s, x, y, k) + 6
        s.text(x, y + 13, label, 12, MUTED)
        x += 13 * len(label) + 18


# ---------------------------------------------------------------- 1. 성벽 HUD

def screen_wall():
    s = Svg()
    hatch(s, "hx_a", AMBER)
    hatch(s, "hx_m", MAGENTA)
    s.rect(0, 0, W, H, BG)
    # 성 안
    s.rect(0, 0, W, 250, "#222b36")
    s.text(640, 120, "↑ 뒷문 · 곶", 13, MUTED, "middle", op=0.7)
    s.text(170, 150, "왕좌", 13, MUTED, "middle", op=0.7)
    s.text(1090, 150, "종탑", 13, MUTED, "middle", op=0.7)
    # 성벽 위 통로
    s.rect(90, 250, 1100, 70, "#3a4656", "#56667c")
    for x in (456, 823):
        s.line(x, 250, x, 320, "#56667c", 1, "4 4")
    # 시설 칸
    s.rect(250, 210, 40, 40, "#2c3642", GOLD, r=5)
    s.text(270, 235, "발리", 11, GOLD, "middle")
    s.rect(620, 210, 40, 40, "none", MUTED, r=5, dash="5 4")
    s.text(640, 237, "+", 22, MUTED, "middle")
    s.rect(990, 210, 40, 40, "#2c3642", GOLD, r=5)
    s.text(1010, 235, "가시", 11, GOLD, "middle")
    # 절벽 3단
    for i, c in enumerate(("#4d3f3a", "#3d3a41", "#2e313a")):
        s.rect(90, 320 + i * 115, 1100, 115, c)
        s.text(102, 320 + i * 115 + 18, f"{3 - i}단", 11, MUTED, op=0.8)
    s.rect(0, 665, W, 55, "#0c0f14")
    s.rect(90, 320, 1100, 20, GOLD, op=0.13)
    s.line(90, 340, 1190, 340, GOLD, 1, "6 5", 0.7)
    # 레인과 적
    lanes = [300 + i * 136 for i in range(6)]
    for lx in lanes:
        s.line(lx, 340, lx, 665, "#000", 1, "2 8", 0.35)
    foes = [(0, 600), (0, 505), (1, 560), (1, 430), (2, 640), (3, 590), (3, 520), (3, 470), (4, 610), (5, 545), (5, 400)]
    for li, y in foes:
        s.circle(lanes[li], y, 9, "#b9553f", "#5e2a20", 1.5)
    # 턱밑 띠의 적 — 윤곽선
    s.circle(lanes[2], 331, 9, "#b9553f", "#ffffff", 2.5)
    s.circle(lanes[4], 333, 9, "#b9553f", "#ffffff", 2.5)
    # 빙결
    s.circle(lanes[1], 430, 9, "#9fb7c9", FROST, 2)
    diamond(s, lanes[1], 412, 5, FROST)
    # 엘리트 — 체력·붙잡기 막대
    ex, ey = lanes[3], 395
    s.circle(ex, ey, 14, "#8e3a2c", "#5e2a20", 2)
    s.rect(ex - 22, ey - 30, 44, 5, "#0b0f14", r=1)
    s.rect(ex - 22, ey - 30, 30, 5, RED, r=1)
    s.rect(ex - 22, ey - 23, 44, 3, "#0b0f14", r=1)
    s.rect(ex - 22, ey - 23, 16, 3, TEXT, r=1)
    # 추락
    s.line(lanes[0], 360, lanes[0], 470, TEXT, 1.5, "3 5", 0.6)
    s.circle(lanes[0], 474, 8, "#b9553f", TEXT, 1)
    # 바위턱의 석궁수
    s.rect(1100, 428, 70, 8, "#5a5a62")
    s.path("M1135,412 l9,15 h-18 Z", "#b9553f", "#5e2a20", 1.5)
    # 왕 + 왕의 귀환
    kx, ky = lanes[2] + 30, 288
    s.circle(kx, ky, 20, "none", GOLD, 3, dash="80 46")
    s.path(f"M{kx},{ky} L{kx-34},{ky+50} A60,60 0 0 0 {kx+34},{ky+50} Z", GOLD, op=0.16)
    s.circle(kx, ky, 11, GOLD, "#fff", 1.5)
    s.text(kx + 28, ky - 8, "귀환 +42%", 11.5, GOLD, bold=True)
    # HUD
    wall_hp(s, 28, 24, 820, 1000, ghost=45, flawless=False, debt=0.41)
    bell(s, 600, 34, "raid", "습격 7 / 12", "14", fill=0.45, color=RED, sub="남은 적")
    # 편성 예고 (망루)
    px = 822
    s.rect(px, 14, 138, 50, "#0b0f14", r=8, op=0.55)
    s.text(px + 8, 28, "다음 · 망루", 10.5, MUTED)
    s.circle(px + 16, 46, 6, "#b9553f"); s.text(px + 26, 51, "9", 13, TEXT, bold=True)
    s.rect(px + 46, 40, 12, 12, "#b9553f", r=2); s.text(px + 62, 51, "3", 13, TEXT, bold=True)
    s.path(f"M{px+92},{40} l7,12 h-14 Z", "#b9553f"); s.text(px + 104, 51, "2", 13, TEXT, bold=True)
    resources(s, W - 24, 24, [("ore", 124), ("iron", 18), ("crystal", 3)])
    skillbar(s, 640)
    # 화면 밖 위협
    s.path("M1262,470 l-16,-12 v24 Z", RED)
    s.rect(1118, 458, 122, 24, "#0b0f14", RED, r=5, op=0.9)
    s.text(1179, 474, "성문길 · 공성 2", 11.5, "#ffd9d0", "middle")
    # 번호
    for n, x, y in ((1, 300, 30), (2, 438, 30), (3, 976, 30), (4, 1040, 30), (5, 380, 640), (6, 1100, 446),
                    (7, lanes[2] - 22, 322), (8, ex + 36, ey - 24), (9, 676, 220), (10, kx + 100, ky - 12)):
        callout(s, n, x, y)
    s.save("ui_hud_wall.svg")


# ---------------------------------------------------------------- 2. 곶 HUD

CAPE = [
    "###########################",
    "###########################",
    "###########################",
    "########oo#################",
    "########oo#################",
    "################SS#########",
    "##########HH#oo#SS#########",
    "##########HH#oo############",
    "#########....K.############",
    "#########.#####ii##########",
    "#########.#####ii##########",
    "####......#################",
    "####.######################",
    "####.######################",
    "####.######################",
    "####.######################",
]


def screen_cape():
    s = Svg()
    hatch(s, "hx_a", AMBER)
    hatch(s, "hx_m", MAGENTA)
    s.defs.append('<radialGradient id="fog" cx="50%" cy="53%" r="42%"><stop offset="0" stop-color="#0b0f14" stop-opacity="0"/>'
                  '<stop offset="0.55" stop-color="#0b0f14" stop-opacity="0.15"/>'
                  '<stop offset="1" stop-color="#0b0f14" stop-opacity="0.97"/></radialGradient>')
    T = 48
    ox, oy = -8, -36
    col = {"#": "#4b453b", "H": "#3f4654", ".": "#262e38", "K": "#262e38", "o": "#4b453b", "s": "#4b453b",
           "i": "#3f4654", "I": "#3f4654", "S": "#35302a"}
    kx = ky = 0
    for r, row in enumerate(CAPE):
        for c, ch in enumerate(row):
            x, y = ox + c * T, oy + r * T
            s.rect(x, y, T, T, col[ch], "#1b2129", sw=1)
            if ch in "os":
                diamond(s, x + 16, y + 18, 6, GOLD); diamond(s, x + 31, y + 30, 5, GOLD)
            if ch in "iI":
                s.rect(x + 12, y + 14, 10, 10, IRON, r=2); s.rect(x + 27, y + 26, 8, 8, IRON, r=2)
            if ch == "K":
                kx, ky = x + T / 2, y + T / 2
    # 못 캐는 암반 — 자물쇠 + 필요한 단계
    lx, ly = ox + 11 * T, oy + 7 * T
    s.rect(lx - 21, ly - 12, 42, 22, "#0b0f14", MUTED, r=5)
    lock(s, lx - 10, ly - 1)
    s.text(lx + 4, ly + 4, "Ⅱ", 12, MUTED, bold=True)
    # 봉인실
    sx, sy = ox + 16 * T, oy + 5 * T
    s.rect(sx, sy, T * 2, T * 2, "none", CRYSTAL, r=3, sw=2)
    s.text(sx + T, sy + T + 5, "봉인실", 12, CRYSTAL, "middle")
    # 겨냥한 타일 3칸 (위쪽) + 금
    for dc in (-1, 0, 1):
        s.rect(kx - T / 2 + dc * T + 2, ky - T * 1.5 + 2, T - 4, T - 4, "none", "#ffffff", sw=2, r=2, op=0.85)
    s.path(f"M{kx-10},{ky-T-14} l8,10 l-6,8 l10,9", stroke="#0b0f14", sw=2)
    s.rect(kx - 18, ky - T - 4 + 22, 36, 4, "#0b0f14"); s.rect(kx - 18, ky - T + 18, 22, 4, TEXT)
    # 빛의 실 — 끌려오는 자원
    pts = [(kx - 30, ky + 6), (kx - 70, ky + 4), (kx - 110, ky + 2), (kx - 150, ky + 6), (kx - 180, ky + 30),
           (kx - 188, ky + 70), (kx - 190, ky + 110), (kx - 192, ky + 146), (kx - 230, ky + 150),
           (kx - 270, ky + 148), (kx - 310, ky + 150), (kx - 350, ky + 148)]
    s.path("M" + " L".join(f"{x},{y}" for x, y in [(kx, ky)] + pts), stroke=GOLD, sw=1.2, op=0.6)
    for i, (x, y) in enumerate(pts):
        if i in (3, 7):
            s.rect(x - 5, y - 5, 10, 10, IRON, r=2)
        else:
            diamond(s, x, y, 6, GOLD)
    # 왕 + 비운 시간 고리
    s.circle(kx, ky, 132, "none", GOLD, 1, 0.25, "3 6")
    s.circle(kx, ky, 20, "none", "#3a4656", 3)
    s.circle(kx, ky, 20, "none", GOLD, 3, dash="88 38")
    s.path(f"M{kx},{ky} L{kx-26},{ky-46} A54,54 0 0 1 {kx+26},{ky-46} Z", GOLD, op=0.15)
    s.circle(kx, ky, 11, GOLD, "#fff", 1.5)
    # 해무
    s.rect(0, 0, W, H, "url(#fog)")
    # 상호작용 안내 (해무 위)
    key(s, sx + T - 30, sy - 26, "C")
    s.text(sx + T - 4, sy - 12, "열기", 12.5, TEXT, bold=True)
    s.text(kx + 30, ky + 34, "비운 42초", 11, GOLD)
    # HUD
    wall_hp(s, 28, 24, 1000, 1000)
    bell(s, 600, 34, "calm", "고요", "0:31", fill=0.31, alarm=0.15, ret=0.14, margin=("귀환 14초 · 여유 +17", GOLD))
    wall_strip(s, 610, 86, idle=True)
    # 물때
    s.rect(806, 14, 96, 50, "#0b0f14", r=8, op=0.55)
    s.path("M816,44 q6,-8 12,0 t12,0 t12,0", stroke=FROST, sw=2)
    s.text(816, 30, "물때 · 4장~", 10.5, MUTED)
    s.text(858, 52, "1:12", 14, FROST, bold=True)
    resources(s, W - 24, 24, [("ore", 124), ("iron", 18), ("crystal", 3)])
    skillbar(s, 640, hp=100, mining=True)
    # 운반
    cx, cy = 1010, 628
    s.rect(cx - 12, cy - 26, 258, 84, "#0b0f14", r=10, op=0.55)
    s.text(cx, cy - 6, "운반", 11, MUTED)
    s.text(cx + 34, cy - 4, "12 / 16", 17, AMBER, bold=True)
    s.text(cx + 234, cy - 5, "속도 −16%", 12, AMBER, "end")
    for i in range(16):
        c = GOLD if i < 8 else (AMBER if i < 12 else "none")
        s.rect(cx + i * 14.6, cy + 8, 11, 14, c, LINE if i >= 12 else None, r=2)
    s.line(cx + 8 * 14.6 - 2, cy + 4, cx + 8 * 14.6 - 2, cy + 26, TEXT, 1.2)
    key(s, cx, cy + 32, "G"); s.text(cx + 26, cy + 45, "내려놓기", 11.5, MUTED)
    # 뒷문 방향
    s.path("M208,708 l-12,-16 h24 Z", TEXT)
    s.rect(228, 684, 96, 24, "#0b0f14", TEXT, r=5, op=0.9)
    s.text(276, 700, "뒷문 84m", 11.5, TEXT, "middle")
    for n, x, y in ((1, 438, 30), (2, 668, 66), (3, 462, 106), (4, 924, 30), (5, 984, 618), (6, 344, 696),
                    (7, kx + 104, ky + 30), (8, kx + 90, ky - 72), (9, lx - 36, ly), (10, sx + T + 50, sy - 18),
                    (11, kx - 196, ky + 86)):
        callout(s, n, x, y)
    s.save("ui_hud_cape.svg")


# ---------------------------------------------------------------- 3. 종 게이지 상태

def sheet_bell():
    s = Svg()
    hatch(s, "hx_a", AMBER)
    hatch(s, "hx_m", MAGENTA)
    rows = 7
    hh = 104
    s.rect(0, 0, 1000, rows * hh + 20, BG)
    notes = [
        ("고요 · 여유 있음", "채움 = 남은 시간. 빗금 = 경보 구간. 흰 선 = 귀환선"),
        ("고요 · 떠날 때", "채움 끝이 귀환선에 닿음. 여유 0 → 선이 맥동"),
        ("경보 · 지각", "테두리 붉은 맥동 + 종소리. 지각 초를 붉게"),
        ("습격 · 성벽에서", "채움 = 남은 적. 숫자 = 남은 수"),
        ("습격 · 곶에서", "같은 자리 + 아래 성벽 띠. 맞는 구역 깜박임"),
        ("총공세 · 보스", "채움 = 보스 체력. 눈금 = 단계 경계"),
        ("5장 · 종 없음", "금 간 종. 3칸 어림. 숫자 숨김. 망루 있으면 숫자 유지"),
    ]
    for i, (t, d) in enumerate(notes):
        y = 20 + i * hh
        s.line(20, y - 8, 980, y - 8, "#232c38", 1)
        s.text(30, y + 30, t, 15, TEXT, bold=True)
        s.text(30, y + 52, d, 12, MUTED)
        cx, by = 700, y + 30
        if i == 0:
            bell(s, cx, by, "", "고요", "0:31", 0.31, GOLD, alarm=0.15, ret=0.14, margin=("귀환 14초 · 여유 +17", GOLD))
        elif i == 1:
            bell(s, cx, by, "", "고요", "0:22", 0.22, GOLD, alarm=0.15, ret=0.21, margin=("귀환 21초 · 여유 0", AMBER))
        elif i == 2:
            s.rect(cx - 166, by - 24, 374, 62, "none", RED, r=11, sw=2.5)
            bell(s, cx, by, "", "경보", "0:09", 0.09, AMBER, alarm=0.15, ret=0.14, margin=("귀환 14초 · 지각 5", RED))
        elif i == 3:
            bell(s, cx, by, "", "습격 7 / 12", "14", 0.45, RED, sub="남은 적")
        elif i == 4:
            bell(s, cx, by - 16, "", "습격 7 / 12", "14", 0.45, RED, sub="남은 적")
            wall_strip(s, cx + 10, by + 22, [(0.3, 2), (0.9, 1), (0.55, 3), (0.2, 1), (0, 0), (0.7, 2)], hot=(0,))
        elif i == 5:
            bell(s, cx, by, "", "총공세 · 그룸", "62%", 0.62, MAGENTA, ticks=3)
        else:
            bell(s, cx, by, "", "떨림", "≈", 0.67, MUTED, cracked=True, coarse=True)
    s.save("ui_bell_states.svg", 1000, rows * hh + 20)


# ---------------------------------------------------------------- 4. 각성 트리

def node(s, kind, x, y, state, label=None):
    """state: on(깨움) / can(가능) / poor(자원 부족) / lock(장 잠김) / far(선행 없음) / virt(가상) / off(버린 분기)"""
    c = {"on": GOLD, "can": GOLD, "poor": RED, "lock": "#56667c", "far": "#3a4656", "virt": VIRT, "off": "#3a4656"}[state]
    fill = GOLD if state == "on" else "#10151c"
    dash = "4 3" if state == "virt" else None
    if state == "can":
        s.circle(x, y, 19 if kind in "sv" else 13, "none", GOLD, 1, 0.45)
    if kind == "s":
        diamond(s, x, y, 14, fill, c, 2, dash=dash)
    elif kind == "v":
        diamond(s, x, y, 10, fill if state == "on" else "#10151c", c, 1.6, dash=dash)
    elif kind == "x":
        star4(s, x, y, 12, fill, c, 1.6)
    elif kind == "n":
        s.rect(x - 11, y - 11, 22, 22, fill, c, r=3, sw=2)
    else:
        s.circle(x, y, 8, fill, c, 1.8, dash=dash)
    if state == "off":
        s.line(x - 8, y - 8, x + 8, y + 8, "#56667c", 1.6)
    if state == "lock" and kind in "sn":
        lock(s, x, y - 1, "#7d8da1")
    if label:
        s.text(x, y + 36, label, 11, TEXT if state in ("on", "can", "virt") else MUTED, "middle")


def screen_tree():
    s = Svg()
    topbar(s, "각성")
    # 왼쪽 — 공명
    s.rect(20, 72, 196, 520, PANEL, r=10)
    s.text(36, 98, "공명", 14, TEXT, bold=True)
    s.text(200, 98, "3 · 6 · 9", 11, MUTED, "end")
    tags = [("추락", 4, 1), ("연쇄", 3, 0), ("수호", 2, 0), ("처형", 1, 0), ("표식", 1, 0), ("지대", 0, 0), ("설치", 0, 0), ("소환", 0, 0)]
    for i, (t, n, v) in enumerate(tags):
        y = 126 + i * 56
        hi = i == 0
        if hi:
            s.rect(28, y - 16, 180, 48, "#2a3442", GOLD, r=7)
        s.text(40, y + 2, t, 13, GOLD if n >= 3 else TEXT, bold=n >= 3)
        s.text(196, y + 2, f"{n}" + (f" +{v}" if v else ""), 12, VIRT if v else MUTED, "end", True)
        for k in range(9):
            c = GOLD if k < n else (VIRT if k < n + v else "none")
            s.rect(40 + k * 17 + (k // 3) * 3, y + 12, 13, 7, c, LINE if k >= n + v else None, r=1.5)
    # 가운데 — 별자리
    s.rect(228, 72, 664, 520, "#0d1218", r=10)
    bx = [350, 560, 770]
    names = ["섬광", "철벽", "파멸"]
    ty = [470, 392, 314, 236, 158]
    for i, y in enumerate(ty):
        s.text(246, y + 4, f"{i+1}", 12, MUTED, op=0.8)
        s.line(262, y, 876, y, "#182029", 1)
    chap = {2: "2장", 3: "3장", 4: "4장"}
    for i, lab in chap.items():
        s.rect(236, ty[i] + 39, 648, 0.1, "none", "#56667c", sw=1, dash="2 6")
        lock(s, 866, ty[i] - 4, "#56667c")
        s.text(866, ty[i] + 20, lab, 10, MUTED, "middle")
    stem = (560, 548)
    for j in range(6):
        x = 485 + j * 30
        s.circle(x, 548, 7, GOLD if j < 3 else "#10151c", GOLD if j < 4 else "#3a4656", 1.8)
    s.text(560, 578, "줄기", 11, MUTED, "middle")
    for b, x in enumerate(bx):
        s.text(x, 96, names[b], 14, TEXT, "middle", True)
        s.path(f"M{stem[0]},{stem[1]-10} Q{x},{stem[1]-20} {x},{ty[0]+16}", stroke="#56667c", sw=1.5)
        s.line(x, ty[0], x, ty[4], "#2c3642", 1.5)
        for y in ty:
            s.line(x - 62, y, x + 62, y, "#2c3642", 1.2)
    # 섬광
    x = bx[0]
    node(s, "s", x, ty[0], "on", "검기"); node(s, "p", x + 60, ty[0], "on")
    node(s, "s", x, ty[1], "can", "질풍베기"); node(s, "v", x - 60, ty[1], "on"); node(s, "v", x - 34, ty[1] - 26, "off")
    node(s, "p", x + 40, ty[1], "virt"); node(s, "p", x + 68, ty[1], "poor")
    node(s, "n", x - 60, ty[2], "lock", "진명"); node(s, "s", x, ty[2], "lock"); node(s, "p", x + 60, ty[2], "lock")
    node(s, "s", x - 60, ty[3], "far"); node(s, "p", x + 30, ty[3], "far"); node(s, "p", x + 60, ty[3], "far")
    node(s, "s", x - 60, ty[4], "far"); node(s, "p", x + 60, ty[4], "far")
    # 철벽
    x = bx[1]
    node(s, "s", x, ty[0], "can", "왕의 방패"); node(s, "p", x + 60, ty[0], "can")
    node(s, "s", x, ty[1], "far"); node(s, "v", x - 60, ty[1], "far"); node(s, "p", x + 60, ty[1], "far")
    node(s, "n", x - 60, ty[2], "lock"); node(s, "s", x, ty[2], "lock"); node(s, "p", x + 60, ty[2], "lock")
    node(s, "s", x - 60, ty[3], "far"); node(s, "p", x + 60, ty[3], "far")
    node(s, "s", x - 60, ty[4], "far"); node(s, "p", x + 60, ty[4], "far")
    # 파멸
    x = bx[2]
    node(s, "s", x, ty[0], "on", "내려찍기"); node(s, "p", x + 60, ty[0], "on")
    node(s, "s", x, ty[1], "virt", "대지 가르기"); node(s, "v", x - 60, ty[1], "can"); node(s, "v", x - 34, ty[1] - 26, "can")
    node(s, "p", x + 60, ty[1], "can")
    node(s, "n", x - 60, ty[2], "lock"); node(s, "s", x, ty[2], "lock"); node(s, "p", x + 60, ty[2], "lock")
    node(s, "s", x - 60, ty[3], "far"); node(s, "p", x + 60, ty[3], "far")
    node(s, "s", x - 60, ty[4], "far"); node(s, "p", x + 60, ty[4], "far")
    # 교차
    for xa, y, st in ((455, ty[1], "far"), (665, ty[1], "far"), (455, ty[3], "far"), (665, ty[3], "far")):
        s.line(xa - 40, y, xa + 40, y, "#2c3642", 1.2, "3 4")
        node(s, "x", xa, y, st)
    # 초점
    fx, fy = bx[0], ty[1]
    s.rect(fx - 24, fy - 24, 48, 48, "none", "#ffffff", r=6, sw=2)
    # 범례
    lx, ly = 246, 560
    for k, lab in (("s", "기술"), ("v", "변형"), ("p", "수동"), ("x", "교차"), ("n", "진명")):
        pass
    # 오른쪽 — 상세
    px = 904
    s.rect(px, 72, 356, 520, PANEL, r=10)
    diamond(s, px + 30, 106, 13, "#10151c", GOLD, 2)
    s.text(px + 54, 104, "질풍베기", 19, TEXT, bold=True)
    s.text(px + 54, 122, "기술 · 섬광 · 2단계", 11.5, MUTED)
    s.line(px + 16, 138, px + 340, 138, LINE)
    s.text(px + 18, 164, "돌진 베기 · 돌진 중 무적", 14, TEXT)
    stats = [("피해", "28"), ("밀어냄", "3"), ("거리", "8m"), ("재사용", "7초")]
    for i, (k, v) in enumerate(stats):
        x = px + 18 + i * 82
        s.rect(x, 178, 76, 46, "#161d26", r=6)
        s.text(x + 38, 195, k, 10.5, MUTED, "middle")
        s.text(x + 38, 215, v, 16, TEXT, "middle", True)
    s.text(px + 18, 250, "곶", 11.5, MUTED)
    s.text(px + 48, 250, "지나간 4칸 · 강도 15", 13, TEXT)
    s.text(px + 18, 280, "태그", 11.5, MUTED)
    s.text(px + 48, 280, "없음", 12.5, MUTED)
    s.line(px + 16, 302, px + 340, 302, LINE)
    s.text(px + 18, 326, "변형 · 3단계", 11.5, MUTED)
    lock(s, px + 96, 321, MUTED)
    s.rect(px + 18, 336, 156, 54, "#161d26", LINE, r=6)
    s.text(px + 30, 357, "A  긴 돌진", 13, MUTED, bold=True); s.text(px + 30, 377, "거리 12m · 재사용 6초", 11.5, MUTED)
    s.rect(px + 182, 336, 156, 54, "#161d26", LINE, r=6)
    s.text(px + 194, 357, "B  올려치기", 13, MUTED, bold=True); s.text(px + 194, 377, "끝에서 올려 벤다 · 밀어냄 8", 11.5, MUTED)
    s.text(px + 18, 408, "둘 중 하나", 11, MUTED)
    s.line(px + 16, 424, px + 340, 424, LINE)
    s.text(px + 18, 452, "비용", 11.5, MUTED)
    res_icon(s, "ore", px + 60, 448)
    s.text(px + 74, 453, "14", 16, TEXT, bold=True)
    s.text(px + 338, 453, "124 → 110", 13, MUTED, "end")
    # 길게 눌러 확정
    s.rect(px + 18, 474, 320, 44, "#161d26", GOLD, r=8, sw=1.5)
    s.rect(px + 19, 475, 130, 42, GOLD, r=7, op=0.28)
    key(s, px + 118, 487, "C")
    s.text(px + 146, 501, "길게 · 깨우기", 14, GOLD, bold=True)
    key(s, px + 18, 536, "F"); s.text(px + 44, 549, "가상 투자", 12, MUTED)
    key(s, px + 124, 536, "Q/E/R"); s.text(px + 182, 549, "칸에 장착", 12, MUTED)
    # 아래 — 장착 + 가상 투자
    s.rect(20, 604, 520, 72, PANEL, r=10)
    s.text(36, 646, "장착", 12, MUTED)
    slot(s, 84, 622, "Q", "검기", 46, sigil=FROST, runes=1)
    slot(s, 142, 622, "E", "내려찍기", 46, sigil=RED)
    slot(s, 200, 622, "R", locked="2장", sz=46)
    slot(s, 270, 622, "F", locked="3장", sz=46)
    s.text(344, 640, "빌드", 12, MUTED)
    for i in range(3):
        s.rect(384 + i * 48, 624, 40, 34, "#161d26", GOLD if i == 0 else LINE, r=6)
        s.text(404 + i * 48, 646, str(i + 1), 14, GOLD if i == 0 else MUTED, "middle", True)
    s.rect(552, 604, 708, 72, "#12201f", VIRT, r=10, sw=1.5)
    s.text(572, 634, "가상 투자 2", 14, VIRT, bold=True)
    res_icon(s, "ore", 580, 654); s.text(594, 659, "124 → 102", 14, TEXT, bold=True)
    s.text(720, 634, "추락 4 → 5", 12.5, TEXT); s.text(720, 658, "새 기술 · 대지 가르기", 12.5, TEXT)
    s.rect(960, 620, 150, 40, VIRT, r=8)
    key(s, 976, 631, "C"); s.text(1004, 646, "길게 · 확정", 13.5, "#0b0f14", bold=True)
    s.rect(1122, 620, 120, 40, "none", MUTED, r=8)
    key(s, 1136, 631, "X"); s.text(1164, 646, "비우기", 13.5, MUTED)
    hintbar(s, [("WASD", "이동"), ("C", "깨우기"), ("F", "가상"), ("T", "태그 강조"), ("1 2 3", "빌드"), ("Esc", "닫기")], H - 26)
    s.save("ui_tree.svg")


# ---------------------------------------------------------------- 5. 성 탭

def screen_castle():
    s = Svg()
    topbar(s, "성")
    # 가운데 — 성벽 도면
    s.rect(20, 72, 872, 400, "#0d1218", r=10)
    s.text(40, 100, "성벽", 14, TEXT, bold=True)
    s.text(872, 100, "시설 2 / 3 · 1장 상한 Ⅰ단계", 12, MUTED, "end")
    wx, wy, ww = 60, 210, 792
    hatch(s, "hx_l", "#3a4656")
    zw = ww / 3
    for i in range(3):
        s.rect(wx + i * zw, wy, zw - 4, 60, "#3a4656", "#56667c", r=3)
        s.text(wx + i * zw + 12, wy + 78, f"구역 {i+1}", 11.5, MUTED)
        s.rect(wx + i * zw, wy + 90, zw - 4, 150, "#1a212b", r=3)
    # 칸
    def fslot(x, name, lv, st):
        c = {"built": GOLD, "empty": MUTED, "focus": "#ffffff"}[st]
        s.rect(x, wy - 70, 64, 64, "#161d26" if st != "empty" else "none", c, r=8, sw=2,
               dash=None if st == "built" else "6 4")
        if st == "built":
            s.text(x + 32, wy - 36, name, 12.5, GOLD, "middle", True)
            for k in range(3):
                s.rect(x + 14 + k * 13, wy - 22, 10, 5, GOLD if k < lv else "none", LINE if k >= lv else None, r=1)
        else:
            s.text(x + 32, wy - 28, "+", 28, c, "middle")
    fslot(wx + 100, "발리스타", 1, "built")
    fslot(wx + zw + 100, "", 0, "focus")
    s.rect(wx + zw + 94, wy - 76, 76, 76, "none", "#ffffff", r=11, sw=1, op=0.5)
    fslot(wx + 2 * zw + 100, "가시 난간", 1, "built")
    # 잠긴 칸
    for x in (wx + 20, wx + zw + 180, wx + 2 * zw + 20):
        s.rect(x, wy - 62, 48, 48, "url(#hx_l)", "#3a4656", r=6)
        lock(s, x + 24, wy - 40, "#7d8da1")
        s.text(x + 24, wy - 20, "2장~", 10, MUTED, "middle")
    # 경로
    routes = [(wx + zw * 0.5, "좌 절벽면", "3장"), (wx + zw * 1.5, "중앙 절벽면", None), (wx + zw * 2.5, "바위턱", None)]
    for x, n, lk in routes:
        s.path(f"M{x},{wy+205} v-100 m-8,10 l8,-10 l8,10", stroke=RED if not lk else "#3a4656", sw=2.5)
        s.text(x, wy + 228, n + (f" · {lk}" if lk else ""), 12, TEXT if not lk else MUTED, "middle")
    # 오른쪽 — 지을 것
    px = 904
    s.rect(px, 72, 356, 592, PANEL, r=10)
    s.text(px + 18, 100, "구역 2 · 빈 칸", 15, TEXT, bold=True)
    s.text(px + 338, 100, "짓기", 12, MUTED, "end")
    cards = [("망루", "경보 +5초 · 편성 예고", None, 10, True, False),
             ("발리스타", "자동 사격 · 피해 40 · 4초", "표식", 10, False, False),
             ("가시 난간", "턱밑 띠 초당 6 · 등반 −30%", "수호", 10, False, False),
             ("수리 작업대", "고요 중 자동 수리", "수호", 10, False, False),
             ("기름 솥", "", "지대", 0, False, True),
             ("성문 방책", "", "수호", 0, False, True)]
    for i, (n, d, tag, cost, focus, locked) in enumerate(cards):
        y = 116 + i * 78
        s.rect(px + 14, y, 328, 70, "#161d26" if not locked else "#1a212b", "#ffffff" if focus else (None if locked else LINE),
               r=8, sw=2 if focus else 1)
        s.rect(px + 24, y + 11, 48, 48, "#0f141b", GOLD if not locked else "#3a4656", r=6)
        if locked:
            lock(s, px + 48, y + 34, "#7d8da1")
            s.text(px + 84, y + 30, n, 14, MUTED, bold=True)
            s.text(px + 84, y + 50, "2장~", 11.5, MUTED)
            continue
        s.text(px + 84, y + 28, n, 14.5, TEXT, bold=True)
        s.text(px + 84, y + 50, d, 12, MUTED)
        if tag:
            chip(s, px + 84 + 16 * len(n) + 8, y + 12, tag)
        res_icon(s, "iron", px + 300, y + 24)
        s.text(px + 312, y + 29, str(cost), 14, TEXT, bold=True)
    y = 116 + 6 * 78 + 8
    s.rect(px + 14, y, 328, 44, "#161d26", GOLD, r=8, sw=1.5)
    s.rect(px + 15, y + 1, 90, 42, GOLD, r=7, op=0.28)
    key(s, px + 84, y + 13, "C")
    s.text(px + 112, y + 27, "길게 · 망루 짓기", 14, GOLD, bold=True)
    s.text(px + 330, y + 27, "18 → 8", 12.5, MUTED, "end")
    # 아래 — 성벽 관리
    s.rect(20, 484, 872, 180, PANEL, r=10)
    shield(s, 48, 518, GOLD)
    s.text(66, 514, "내구도", 12, MUTED)
    s.text(66, 536, "820 / 1000", 20, TEXT, bold=True)
    s.rect(200, 512, 400, 16, "#0b0f14", LINE, r=4)
    s.rect(200, 512, 328, 16, GOLD, r=4)
    s.rect(528, 512, 40, 16, VIRT, r=0, op=0.55)
    s.text(548, 548, "+100", 12, VIRT, "middle", True)
    bx = 40
    for t, sub, kk, c in (("수리 +50", "흑철 1", "R", GOLD), ("강화 Ⅰ", "최대 1,150 · 흑철 12", "T", GOLD)):
        s.rect(bx, 580, 250, 62, "#161d26", c, r=8)
        key(s, bx + 14, 602, kk)
        s.text(bx + 46, 606, t, 15, TEXT, bold=True)
        s.text(bx + 46, 628, sub, 12, MUTED)
        bx += 266
    s.rect(bx, 580, 300, 62, "none", LINE, r=8, dash="6 4")
    key(s, bx + 14, 602, "X")
    s.text(bx + 46, 606, "되돌리기", 15, TEXT, bold=True)
    s.text(bx + 46, 628, "전액 환급 · 남은 3 / 3", 12, MUTED)
    s.text(640, 536, "수리 2회 예정", 12, VIRT)
    hintbar(s, [("WASD", "칸 이동"), ("C", "짓기"), ("R", "수리"), ("X", "되돌리기"), ("Q E", "탭"), ("Esc", "닫기")], H - 26)
    s.save("ui_castle.svg")


if __name__ == "__main__":
    screen_wall()
    screen_cape()
    sheet_bell()
    screen_tree()
    screen_castle()
