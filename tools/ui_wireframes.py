#!/usr/bin/env python3
"""화면 설계서(docs/09_screen_design.md)의 와이어프레임을 그린다.

각성 트리 화면은 data/trees/sword.json의 노드를 그대로 배치한다.
노드를 고치고 다시 돌리면 그림이 따라온다. 나머지 화면은 이 파일 안의 배치가 원본이다.

실행:  python3 tools/ui_wireframes.py
출력:  docs/img/ui_battle.svg         전투 화면 (성벽 위, 습격 중)
       docs/img/ui_hud_states.svg     HUD 요소의 상태
       docs/img/ui_tree.svg           각성 트리 화면
       docs/img/ui_castle.svg         시설 건설 화면
       docs/img/ui_throne.svg         왕좌 메뉴
"""
from __future__ import annotations

import json
import math
import pathlib
from xml.sax.saxutils import escape

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / "docs" / "img"
W, H = 1600, 900                      # 기준 해상도 1920×1080의 5/6

BG, PANEL, PANEL2 = "#11151b", "#19202a", "#232c38"
LINE, DIM, TEXT, SUB = "#5a6678", "#333c49", "#d8dee6", "#8f9aaa"
GOLD, RED, CYAN, GREEN, VIOLET = "#e6b85c", "#e2645d", "#62c6d6", "#7fc48a", "#b58be0"
FONT = "'Noto Sans KR','Apple SD Gothic Neo','Malgun Gothic','NanumGothic',sans-serif"
TAGS = ["연쇄", "설치", "추락", "수호", "표식", "지대", "소환", "처형"]


class Svg:
    def __init__(self, w=W, h=H, title=""):
        self.w, self.h, self.title, self.e = w, h, title, []

    def add(self, s):
        self.e.append(s)

    def rect(self, x, y, w, h, fill=PANEL, stroke=LINE, rx=4, sw=1, dash=None, op=None):
        a = f' stroke-dasharray="{dash}"' if dash else ""
        a += f' opacity="{op}"' if op is not None else ""
        self.add(f'<rect x="{x:.1f}" y="{y:.1f}" width="{w:.1f}" height="{h:.1f}" rx="{rx}" '
                 f'fill="{fill}" stroke="{stroke}" stroke-width="{sw}"{a}/>')

    def line(self, x1, y1, x2, y2, stroke=LINE, sw=1, dash=None, op=None):
        a = f' stroke-dasharray="{dash}"' if dash else ""
        a += f' opacity="{op}"' if op is not None else ""
        self.add(f'<line x1="{x1:.1f}" y1="{y1:.1f}" x2="{x2:.1f}" y2="{y2:.1f}" '
                 f'stroke="{stroke}" stroke-width="{sw}"{a}/>')

    def circle(self, x, y, r, fill="none", stroke=LINE, sw=1, dash=None, op=None):
        a = f' stroke-dasharray="{dash}"' if dash else ""
        a += f' opacity="{op}"' if op is not None else ""
        self.add(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{r:.1f}" fill="{fill}" stroke="{stroke}" '
                 f'stroke-width="{sw}"{a}/>')

    def poly(self, pts, fill="none", stroke=LINE, sw=1, dash=None, op=None):
        a = f' stroke-dasharray="{dash}"' if dash else ""
        a += f' opacity="{op}"' if op is not None else ""
        p = " ".join(f"{x:.1f},{y:.1f}" for x, y in pts)
        self.add(f'<polygon points="{p}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}"{a}/>')

    def path(self, d, fill="none", stroke=LINE, sw=1, dash=None, op=None):
        a = f' stroke-dasharray="{dash}"' if dash else ""
        a += f' opacity="{op}"' if op is not None else ""
        self.add(f'<path d="{d}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}"{a}/>')

    def text(self, x, y, s, size=13, fill=TEXT, anchor="start", weight=400, op=None):
        a = f' opacity="{op}"' if op is not None else ""
        self.add(f'<text x="{x:.1f}" y="{y:.1f}" font-size="{size}" fill="{fill}" '
                 f'text-anchor="{anchor}" font-weight="{weight}"{a}>{escape(str(s))}</text>')

    def para(self, x, y, s, width, size=13, fill=TEXT, lh=1.5, weight=400):
        """폭에 맞춰 줄을 나눈다. 다음 줄의 y를 돌려준다."""
        for ln in wrap(s, width, size):
            self.text(x, y, ln, size, fill, weight=weight)
            y += size * lh
        return y

    def bar(self, x, y, w, h, frac, color=GOLD, back=DIM):
        self.rect(x, y, w, h, fill=back, stroke=LINE, rx=2)
        if frac > 0:
            self.rect(x + 1, y + 1, (w - 2) * frac, h - 2, fill=color, stroke="none", rx=1)

    def chip(self, x, y, s, size=12, fill=PANEL2, stroke=LINE, color=TEXT, h=None, weight=400):
        w = tw(s, size) + 16
        h = h or size + 10
        self.rect(x, y, w, h, fill=fill, stroke=stroke, rx=h / 2)
        self.text(x + w / 2, y + h / 2 + size * 0.36, s, size, color, "middle", weight)
        return x + w + 6

    def key(self, x, y, s, size=11):
        """입력 힌트. 키 모양의 작은 상자."""
        w = max(tw(s, size) + 10, 20)
        self.rect(x, y, w, size + 8, fill=BG, stroke=SUB, rx=3)
        self.text(x + w / 2, y + size + 2.5, s, size, TEXT, "middle", 700)
        return x + w + 5

    def badge(self, n, x, y):
        """설명 표와 짝을 이루는 번호."""
        self.circle(x, y, 11, fill="#f3f5f8", stroke=BG, sw=2)
        self.text(x, y + 4.5, n, 12, BG, "middle", 700)

    def button(self, x, y, w, h, s, primary=False, size=13, disabled=False):
        fill = GOLD if primary else PANEL2
        col = BG if primary else (SUB if disabled else TEXT)
        self.rect(x, y, w, h, fill=fill, stroke=GOLD if primary else LINE, rx=5,
                  dash="4 3" if disabled else None)
        self.text(x + w / 2, y + h / 2 + size * 0.36, s, size, col, "middle", 700 if primary else 500)

    def render(self):
        head = (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {self.w} {self.h}" '
                f'width="{self.w}" height="{self.h}" font-family="{FONT}" role="img">'
                f'<title>{escape(self.title)}</title>'
                f'<rect width="{self.w}" height="{self.h}" fill="{BG}"/>')
        return head + "\n" + "\n".join(self.e) + "\n</svg>\n"

    def save(self, name):
        OUT.mkdir(parents=True, exist_ok=True)
        (OUT / name).write_text(self.render(), encoding="utf-8")
        print(f"  {name}  ({len(self.e)}개 요소)")


def tw(s, size):
    """글자 폭 어림. 한글은 1em, 나머지는 0.56em."""
    return sum(size if ord(c) > 0x2E7F else size * 0.56 for c in str(s))


def wrap(s, width, size):
    out, cur = [], ""
    for word in str(s).split(" "):
        trial = (cur + " " + word).strip()
        if tw(trial, size) <= width or not cur:
            cur = trial
        else:
            out.append(cur)
            cur = word
    if cur:
        out.append(cur)
    return out


def polar(cx, cy, r, deg):
    """위쪽이 0°, 시계 방향."""
    a = math.radians(deg)
    return cx + r * math.sin(a), cy - r * math.cos(a)


def diamond(x, y, r):
    return [(x, y - r), (x + r, y), (x, y + r), (x - r, y)]


def ngon(x, y, r, n, rot=0):
    return [polar(x, y, r, rot + 360 * i / n) for i in range(n)]


def star4(x, y, r):
    pts = []
    for i in range(8):
        pts.append(polar(x, y, r if i % 2 == 0 else r * 0.42, 45 * i))
    return pts


def sigil(s, x, y, kind, r=7):
    """인장은 색과 모양 둘 다로 구분한다 (GDD 15.4)."""
    if kind == "화염":
        s.poly([(x, y - r), (x + r, y + r * 0.8), (x - r, y + r * 0.8)], fill="#e8843c", stroke=BG)
    elif kind == "서리":
        s.poly(ngon(x, y, r, 6), fill="#7fd0f0", stroke=BG)
    elif kind == "뇌전":
        s.poly([(x - r * 0.2, y - r), (x + r * 0.7, y - r), (x + r * 0.1, y - r * 0.1), (x + r * 0.8, y - r * 0.1),
                (x - r * 0.5, y + r), (x - r * 0.1, y + r * 0.1), (x - r * 0.7, y + r * 0.1)], fill="#f0dc5a", stroke=BG)
    elif kind == "성광":
        s.poly(star4(x, y, r * 1.15), fill="#f5efd0", stroke=BG)
    elif kind == "공허":
        s.circle(x, y, r * 0.85, fill=BG, stroke=VIOLET, sw=2.5)
    else:
        s.circle(x, y, r * 0.8, fill="none", stroke=SUB, dash="2 2")


# --------------------------------------------------------------------------
# 공통: 왕좌 메뉴의 위쪽 띠
def topbar(s, active, res=(124, 18, 3, 2), pending=None):
    s.rect(0, 0, W, 64, fill=PANEL, stroke="none", rx=0)
    s.line(0, 64, W, 64)
    x = s.key(24, 22, "Q")
    for name in ["왕좌", "레갈리아", "성", "기록", "체계"]:
        w = tw(name, 15) + 36
        if name == active:
            s.rect(x, 12, w, 40, fill=PANEL2, stroke=GOLD, rx=5)
            s.text(x + w / 2, 38, name, 15, GOLD, "middle", 700)
        else:
            s.text(x + w / 2, 38, name, 15, SUB, "middle")
        x += w + 4
    s.key(x + 4, 22, "E")
    # 멈춘 종 게이지
    s.circle(760, 32, 15, stroke=GOLD, sw=2)
    s.path("M760,17 A15,15 0 0 1 773,39.5", stroke=GOLD, sw=4)
    s.text(784, 29, "고요 0:47 남음", 14, TEXT, weight=700)
    s.text(784, 47, "메뉴가 열려 있는 동안 멈춘다", 11, SUB)
    names = ["성광석", "흑철", "룬 수정", "마정"]
    x = 1090
    for i, (n, v) in enumerate(zip(names, res)):
        s.text(x, 28, n, 11, SUB)
        s.text(x, 48, v, 17, GOLD if i == 0 else TEXT, weight=700)
        if pending and pending[i]:
            s.text(x + tw(str(v), 17) + 6, 48, f"→ {v - pending[i]}", 13, CYAN, weight=700)
        x += 128 if (pending and pending[i]) else 104
    return 64


def hints(s, items, y=868):
    s.line(0, y - 14, W, y - 14, stroke=DIM)
    x = 24
    for k, label in items:
        x = s.key(x, y - 4, k)
        s.text(x, y + 10, label, 12, SUB)
        x += tw(label, 12) + 22


# --------------------------------------------------------------------------
# 1. 전투 화면
def enemy(s, x, y, kind="등반병", band=False, hp=None, grip=None, frozen=False, label=None):
    stroke, sw = ("#ffffff", 3) if band else ("#7a3a36", 1.5)
    fill = "#b9534d"
    if kind == "등반병":
        s.circle(x, y, 9, fill=fill, stroke=stroke, sw=sw)
    elif kind == "방패병":
        s.circle(x, y, 11, fill=fill, stroke=stroke, sw=sw)
        s.line(x - 9, y - 13, x + 9, y - 13, stroke=TEXT, sw=3)
    elif kind == "그림자 칼":
        s.poly(diamond(x, y, 12), fill="#8f4d86", stroke=stroke, sw=sw)
    elif kind == "짐꾼 거인":
        s.circle(x, y, 22, fill="#8f4d86", stroke=stroke, sw=sw)
    elif kind == "석궁수":
        s.poly([(x, y - 11), (x + 10, y + 8), (x - 10, y + 8)], fill=fill, stroke=stroke, sw=sw)
    elif kind == "공성병":
        s.rect(x - 15, y - 15, 30, 30, fill=fill, stroke=stroke, rx=3, sw=sw)
    r = 22 if kind == "짐꾼 거인" else 12
    if frozen:
        s.poly(ngon(x, y, r + 7, 6), stroke="#7fd0f0", sw=2)
    if hp is not None:
        s.bar(x - 18, y + r + 6, 36, 5, hp, RED)
    if grip is not None:
        s.bar(x - 18, y + r + (13 if hp is not None else 6), 36, 5, grip, CYAN)
    if label:
        s.text(x, y - r - 8, label, 11, TEXT, "middle")


def slot(s, x, y, key, name, sig=None, cd=None, locked=None, size=62, runes=0):
    s.rect(x, y, size, size, fill=PANEL if not locked else BG, stroke=LINE if not locked else DIM,
           rx=6, dash="4 3" if locked else None)
    if locked:
        s.text(x + size / 2, y + size / 2 - 2, "잠김", 12, SUB, "middle")
        s.text(x + size / 2, y + size / 2 + 14, locked, 10, SUB, "middle")
    else:
        for i, ln in enumerate(wrap(name, size - 8, 12)[:2]):
            s.text(x + size / 2, y + 28 + i * 15, ln, 12, TEXT, "middle", 500, op=0.3 if cd else None)
        if cd:
            s.text(x + size / 2, y + size / 2 + 8, cd, 21, "#ffffff", "middle", 700)
        if sig:
            sigil(s, x + size - 11, y + 11, sig, 6)
        for i in range(runes):
            s.circle(x + 9 + i * 10, y + size - 8, 3, fill=GOLD if i == 0 else "none", stroke=GOLD)
    kw = max(tw(key, 11) + 10, 20)
    s.rect(x + size / 2 - kw / 2, y + size - 4, kw, 18, fill=BG, stroke=SUB, rx=3)
    s.text(x + size / 2, y + size + 9.5, key, 11, TEXT, "middle", 700)


def battle():
    s = Svg(title="전투 화면 — 성벽 위, 습격 중 (2장 기준)")
    # --- 세계 (배치만 보인다. 축척이 아니다) ---
    s.rect(0, 0, W, 300, fill="#151b23", stroke="none", rx=0)                 # 성 안쪽
    s.rect(0, 300, W, 86, fill="#2a3340", stroke="none", rx=0)                # 성벽 위 통로
    tiers = [(386, 124, "#3a3f47", "3단  12~18m"), (510, 124, "#2e3239", "2단  6~12m"),
             (634, 124, "#23262c", "1단  0~6m")]
    for y, h, col, _ in tiers:
        s.rect(0, y, W, h, fill=col, stroke="none", rx=0)
    s.rect(0, 758, W, 142, fill="#0c0e12", stroke="none", rx=0)               # 벼랑 아래
    s.rect(0, 386, 1400, 62, fill="#ffffff", stroke="none", rx=0, op=0.07)    # 턱밑 띠
    s.line(0, 448, 1400, 448, stroke=TEXT, sw=1, dash="2 6", op=0.6)
    s.line(0, 386, W, 386, stroke=TEXT, sw=4)                                 # 성벽 턱
    s.line(0, 300, W, 300, stroke=LINE, sw=2)
    for x in (470, 940):                                                      # 구역 경계
        s.line(x, 300, x, 386, stroke=LINE, dash="6 5")
    for x, n in ((16, "구역 1"), (486, "구역 2"), (956, "구역 3")):
        s.text(x, 320, n, 12, SUB)
    s.text(260, 150, "왕좌 (재기)", 13, SUB, "middle")
    s.text(800, 214, "안뜰", 13, SUB, "middle")
    s.text(800, 160, "↑ 뒷문 · 곶 · 바다", 12, SUB, "middle")
    s.text(1250, 150, "종탑", 13, SUB, "middle")
    # 성문탑과 성문길
    s.rect(1400, 270, 200, 630, fill="#1d232c", stroke=LINE, rx=0)
    s.rect(1400, 270, 200, 116, fill="#343e4d", stroke=LINE, rx=0)
    s.text(1500, 336, "성문탑", 13, TEXT, "middle")
    s.text(1500, 480, "성문길", 13, SUB, "middle")
    s.line(1500, 500, 1500, 880, stroke=LINE, dash="3 8")
    # 레인 (화면에 다섯 줄. 여섯째 줄은 왼쪽 화면 밖)
    lanes = [150, 410, 670, 930, 1190]
    for x in lanes:
        s.line(x, 386, x, 880, stroke="#ffffff", sw=1, dash="2 10", op=0.22)
    # 바위턱
    s.rect(18, 500, 80, 26, fill="#4a4f58", stroke=LINE, rx=2)
    s.rect(1290, 500, 96, 26, fill="#4a4f58", stroke=LINE, rx=2)
    s.text(1338, 544, "바위턱", 11, SUB, "middle")
    # 높이 눈금
    for y, h, _, name in tiers:
        s.text(280, y + h - 8, name, 11, SUB, "middle")
    s.text(1392, 402, "턱밑 띠 3m — 근접이 닿는다", 11, TEXT, "end")
    s.text(1000, 800, "벼랑 아래 — 적이 나타나는 곳", 12, SUB, "middle")

    # 시설 (성벽 안쪽 가장자리의 슬롯)
    for x, name, st in ((120, "망루", "Ⅰ"), (560, "발리스타", "Ⅱ"), (1030, "가시 난간", "Ⅰ"), (1290, "빈 칸", "")):
        empty = name == "빈 칸"
        s.rect(x, 252, 96, 44, fill=PANEL, stroke=LINE if not empty else DIM, rx=4, dash="4 3" if empty else None)
        s.text(x + 48, 272, name, 12, TEXT if not empty else SUB, "middle", 500)
        if st:
            s.text(x + 48, 288, st, 11, GOLD, "middle", 700)
    s.path("M 656 274 A 14 14 0 1 1 670 260", stroke=CYAN, sw=3)             # 발리스타 재장전
    s.line(608, 300, 668, 470, stroke=GOLD, sw=1, dash="6 4", op=0.7)         # 발리스타 조준선
    s.rect(940, 380, 460, 8, fill=GREEN, stroke="none", rx=0, op=0.55)        # 가시 난간이 덮는 턱

    # 적
    enemy(s, 150, 700)
    enemy(s, 150, 590, "방패병")
    enemy(s, 410, 664)
    enemy(s, 410, 560)
    enemy(s, 410, 470, "그림자 칼")
    enemy(s, 670, 420, "방패병", band=True, hp=0.55, grip=0.3)
    enemy(s, 670, 500, frozen=True)
    enemy(s, 670, 610)
    enemy(s, 930, 404, band=True)
    enemy(s, 930, 520, "짐꾼 거인", hp=0.8, grip=0.9)
    enemy(s, 930, 690)
    enemy(s, 1190, 432, band=True)
    enemy(s, 1190, 640)
    enemy(s, 1338, 486, "석궁수")
    s.line(1330, 478, 790, 350, stroke=RED, sw=1.5, dash="8 5", op=0.8)       # 석궁 조준 예고
    # 성벽 위에 올라선 적
    enemy(s, 1120, 362, band=True)
    # 떨어지는 적
    s.path("M 760 430 C 800 520, 790 640, 772 742", stroke="#ffffff", sw=2, dash="3 5", op=0.8)
    s.circle(774, 744, 9, fill="none", stroke="#b9534d", sw=2, dash="3 3")
    s.text(808, 600, "−146", 15, "#ffffff", weight=700)
    s.text(808, 616, "추락", 11, SUB)

    # 왕
    kx, ky = 720, 344
    s.path(f"M {kx} {ky} L {kx - 62} {ky + 74} A 96 96 0 0 0 {kx + 62} {ky + 74} Z",
           fill=GOLD, stroke="none", op=0.18)                                  # 공격 호
    s.circle(kx, ky, 26, stroke=GOLD, sw=1, dash="2 4")
    s.path(f"M {kx} {ky - 26} A 26 26 0 1 1 {kx - 24.7} {ky - 8}", stroke=GOLD, sw=4)   # 귀환 고리
    s.circle(kx, ky, 12, fill=GOLD, stroke=BG, sw=2)
    s.text(kx, ky + 4.5, "왕", 11, BG, "middle", 700)
    s.chip(kx + 34, ky - 40, "왕의 귀환 +38% · 3.2초", 12, fill=BG, stroke=GOLD, color=GOLD, weight=700)

    # --- HUD ---
    # 성벽 내구도
    s.rect(20, 18, 380, 66, fill=BG, stroke=LINE, op=0.92)
    s.text(34, 42, "성벽", 14, TEXT, weight=700)
    s.text(72, 42, "강화 Ⅰ", 11, SUB)
    s.text(386, 42, "742 / 1,150", 14, TEXT, "end", 700)
    s.bar(34, 52, 352, 14, 742 / 1150, GOLD)
    s.rect(34 + 352 * 0.645, 52, 352 * 0.04, 14, fill=RED, stroke="none", rx=0)          # 방금 깎인 만큼
    s.text(34, 79, "◇ 흠 없는 방어 깨짐", 10.5, SUB)
    # 종 게이지
    s.rect(640, 14, 320, 84, fill=BG, stroke=RED, sw=2, op=0.94, rx=8)
    s.circle(690, 56, 28, stroke=DIM, sw=5)
    s.path("M 690 28 A 28 28 0 1 1 662.6 62", stroke=RED, sw=5)
    s.text(690, 62, "종", 16, TEXT, "middle", 700)
    s.text(734, 46, "습격 7 / 12", 18, TEXT, weight=700)
    s.text(734, 66, "남은 적 16 · 등장 0:21 남음", 12, SUB)
    s.text(734, 84, "절벽 13 · 바위턱 1 · 성문길 2", 11, SUB)
    s.rect(660, 104, 280, 16, fill="none", stroke=DIM, rx=3, dash="4 3")
    s.text(800, 116, "보스 체력 막대 (총공세에서만)", 10.5, SUB, "middle")
    # 자원
    s.rect(1196, 18, 384, 54, fill=BG, stroke=LINE, op=0.92)
    for i, (n, v) in enumerate((("성광석", 124), ("흑철", 18), ("룬 수정", 3), ("마정", 2))):
        s.text(1212 + i * 94, 38, n, 11, SUB)
        s.text(1212 + i * 94, 60, v, 17, GOLD if i == 0 else TEXT, weight=700)
    # 왕과 기술
    s.rect(20, 770, 620, 112, fill=BG, stroke=LINE, op=0.92)
    s.text(34, 794, "왕", 14, TEXT, weight=700)
    s.bar(60, 782, 220, 14, 0.86, GREEN)
    s.text(288, 794, "86 / 100", 12, TEXT)
    s.text(368, 794, "부채", 12, SUB)
    s.rect(400, 784, 120, 10, fill="none", stroke=DIM, rx=2, dash="4 3")
    s.text(528, 794, "공허 인장을 받은 뒤", 10, SUB)
    x = 34
    slot(s, x, 804, "좌클릭", "기본 3연타", size=58)
    slot(s, x + 68, 804, "우클릭", "패링", size=58)
    slot(s, x + 150, 804, "Q", "내려찍기", sig="서리", size=58, runes=2)
    slot(s, x + 218, 804, "E", "대지 가르기", sig="화염", cd="4.2", size=58, runes=1)
    slot(s, x + 286, 804, "R", "왕의 방패", size=58, runes=1)
    slot(s, x + 368, 804, "F", "", locked="3장", size=58)
    slot(s, x + 450, 804, "Space", "대시", cd="1.1", size=58)
    # 운반과 바로가기
    s.rect(1330, 822, 250, 60, fill=BG, stroke=LINE, op=0.92)
    s.text(1346, 846, "운반", 12, SUB)
    s.text(1346, 868, "0 / 16", 17, SUB, weight=700)
    kx2 = s.key(1432, 842, "Tab")
    s.text(kx2, 857, "각성", 12, SUB)
    kx2 = s.key(1432, 862, "M")
    s.text(kx2, 877, "지도", 12, SUB)
    # 화면 가장자리 경고
    s.poly([(4, 650), (26, 634), (26, 666)], fill=RED, stroke="none")
    s.rect(30, 630, 104, 40, fill=BG, stroke=RED, rx=4, op=0.94)
    s.text(40, 647, "왼쪽 레인", 11, TEXT, weight=700)
    s.text(40, 663, "가장 높이 2단", 11, SUB)
    s.poly([(1596, 660), (1574, 644), (1574, 676)], fill=RED, stroke="none")
    s.rect(1420, 640, 150, 40, fill=BG, stroke=RED, rx=4, op=0.94)
    s.text(1430, 657, "성문길 · 공성병 2", 11, TEXT, weight=700)
    s.text(1430, 673, "방책까지 18m", 11, SUB)
    # 경보 테두리 설명
    s.rect(3, 3, W - 6, H - 6, fill="none", stroke=RED, sw=2, rx=0, dash="14 10", op=0.5)

    for n, x, y in ((1, 410, 18), (2, 630, 18), (3, 1186, 18), (4, 20, 760), (5, 646, 836), (6, 1320, 818),
                    (7, 676, 356), (8, 976, 394), (9, 628, 432), (10, 872, 604), (11, 146, 626), (12, 1586, 630),
                    (13, 548, 246), (14, 1372, 470), (15, 950, 110), (16, 1560, 880)):
        s.badge(n, x, y)
    s.save("ui_battle.svg")


# --------------------------------------------------------------------------
# 1-b. HUD 요소의 상태
def hud_states():
    s = Svg(W, 760, "HUD 요소의 상태 — 종 게이지, 성벽 띠, 기술 칸, 적 표시")

    def head(y, n, t, sub):
        s.badge(n, 34, y - 5)
        s.text(54, y, t, 16, TEXT, weight=700)
        s.text(54 + tw(t, 16) + 14, y, sub, 12, SUB)

    # A. 종 게이지
    head(36, "A", "종 게이지", "화면 위 가운데. 언제나 보인다. 색과 고리의 모양, 글자가 같은 것을 세 번 말한다")
    states = [
        ("고요", GOLD, 0.53, "다음 습격 0:47", "망루: 등반병 7 · 방패병 2 · 석궁수 2", "고리가 줄어든다"),
        ("경보", RED, 0.15, "종이 울린다 0:12", "화면 테두리가 맥동한다 · 종소리", "습격 15초 전 (망루 20초)"),
        ("습격", RED, 1.0, "습격 7 / 12", "남은 적 16 · 등장 0:21 남음", "고리가 남은 적만큼 찬다"),
        ("5장 — 종 없음", VIOLET, 0.3, "레갈리아가 떤다 0:06", "경보는 8초. 패드 진동을 함께 쓴다", "종 자리에 레갈리아"),
    ]
    for i, (name, col, frac, t1, t2, note) in enumerate(states):
        x = 34 + i * 386
        s.rect(x, 54, 366, 96, fill=PANEL, stroke=col if name != "고요" else LINE, rx=8, sw=2 if name != "고요" else 1,
               dash="10 6" if name == "경보" else None)
        s.circle(x + 50, 102, 28, stroke=DIM, sw=5)
        a = 360 * frac
        if frac >= 1:
            s.circle(x + 50, 102, 28, stroke=col, sw=5)
        else:
            ex, ey = polar(x + 50, 102, 28, a)
            s.path(f"M {x + 50} 74 A 28 28 0 {1 if a > 180 else 0} 1 {ex:.1f} {ey:.1f}", stroke=col, sw=5)
        s.text(x + 50, 108, "종" if i < 3 else "검", 15, TEXT, "middle", 700)
        s.text(x + 92, 92, t1, 17, TEXT, weight=700)
        s.text(x + 92, 112, t2, 11.5, SUB)
        s.text(x + 92, 132, note, 11.5, col)
        s.text(x + 4, 168, name, 12, SUB)
    s.chip(1300, 158, "물때 1:32  (4장부터 종 옆에)", 11.5, color=CYAN, stroke=CYAN)

    # B. 성벽 띠
    head(214, "B", "성벽 띠", "곶에 나가 있을 때만. 종 게이지 바로 아래. 레인마다 가장 높은 적의 높이를 점으로 찍는다")
    bx, by, bw, bh = 34, 232, 700, 150
    s.rect(bx, by, bw, bh, fill=PANEL, stroke=LINE, rx=6)
    s.rect(bx + 14, by + 14, 560, 16, fill="#2a3340", stroke=LINE, rx=2)                     # 성벽 위
    s.rect(bx + 14 + 373, by + 14, 187, 16, fill=RED, stroke=RED, rx=2, op=0.75)             # 공격받는 구역
    for i, n in enumerate(("구역 1", "구역 2", "구역 3 — 맞는 중")):
        s.text(bx + 14 + 93 + i * 187, by + 26, n, 10.5, TEXT, "middle", 700 if i == 2 else 400)
    heights = [0.2, 0.55, 0.35, 0.8, 1.0, 0.93]
    for i, hgt in enumerate(heights):
        x = bx + 14 + 47 + i * 93
        s.line(x, by + 32, x, by + 126, stroke=DIM, sw=6)
        for t in (1, 2):
            s.line(x - 8, by + 32 + 31 * t, x + 8, by + 32 + 31 * t, stroke=LINE)
        y = by + 126 - 84 * hgt
        top = hgt >= 0.9
        s.circle(x, y, 7, fill=RED if top else "#b9534d", stroke="#ffffff" if top else BG, sw=2)
        s.text(x + 14, y + 4, ("3", "5", "2", "4", "6", "3")[i], 11, TEXT)
    s.text(bx + 14, by + 142, "점 = 그 레인에서 가장 높은 적 · 숫자 = 레인의 적 수 · 흰 테두리 = 턱밑 띠 또는 성벽 위", 10.5, SUB)
    s.rect(bx + 588, by + 14, 98, 52, fill=PANEL2, stroke=LINE, rx=3)
    s.text(bx + 637, by + 34, "성문길", 11, SUB, "middle")
    s.text(bx + 637, by + 54, "공성병 2", 12, TEXT, "middle", 700)
    s.rect(bx + 588, by + 74, 98, 52, fill=PANEL2, stroke=DIM, rx=3, dash="4 3")
    s.text(bx + 637, by + 94, "공중", 11, SUB, "middle")
    s.text(bx + 637, by + 114, "3장부터", 11, SUB, "middle")
    # 비운 시간과 돌아가는 길
    s.rect(760, 232, 400, 150, fill=PANEL, stroke=LINE, rx=6)
    s.circle(812, 290, 30, stroke=DIM, sw=5)
    s.path("M 812 260 A 30 30 0 1 1 786 305", stroke=GOLD, sw=5)
    s.text(812, 286, "42초", 13, TEXT, "middle", 700)
    s.text(812, 302, "+38%", 11, GOLD, "middle", 700)
    s.text(860, 270, "비운 시간 고리", 14, TEXT, weight=700)
    y = s.para(860, 290, "뒷문을 나선 뒤 10초부터 차기 시작해 60초에 가득 찬다. 가운데 숫자가 돌아갔을 때의 왕의 귀환 보너스다.", 286, 11.5, SUB)
    s.text(776, 356, "성벽까지 9초", 13, TEXT, weight=700)
    s.text(776, 373, "지금 걸음(운반 12/16, −16%)으로 잰 시간. 경보보다 길면 붉게", 10.5, SUB)
    s.rect(1180, 232, 386, 150, fill=PANEL, stroke=LINE, rx=6)
    s.text(1196, 258, "운반", 14, TEXT, weight=700)
    s.bar(1196, 270, 300, 14, 12 / 16, GOLD)
    s.line(1196 + 150, 266, 1196 + 150, 288, stroke=TEXT, sw=2)
    s.text(1506, 282, "12 / 16", 13, TEXT, weight=700)
    s.text(1196, 306, "절반(8)을 넘으면 느려지기 시작하는 눈금", 11, SUB)
    s.text(1196, 332, "성광석 9 · 흑철 3", 13, TEXT)
    kx = s.key(1196, 346, "G")
    s.text(kx, 361, "내려놓기 — 떨어진 자리가 지도에 남는다", 11.5, SUB)

    # C. 기술 칸
    head(440, "C", "기술 칸", "아래 왼쪽. 칸마다 입력 표시가 붙는다. 재사용은 숫자와 덮개 둘 다로 보인다")
    cs = [("준비", dict(name="내려찍기", sig="서리", runes=2)),
          ("재사용 중", dict(name="내려찍기", sig="서리", cd="4.2", runes=2)),
          ("인장 없음", dict(name="왕의 방패", runes=1)),
          ("누르는 중", dict(name="왕의 방패", runes=1)),
          ("곶에서 못 쓴다", dict(name="부동")),
          ("예약됨", dict(name="부동")),
          ("잠김", dict(name="", locked="3장"))]
    for i, (label, kw) in enumerate(cs):
        x = 34 + i * 118
        slot(s, x, 460, "Q" if i < 4 else "F", size=66, **kw)
        s.text(x + 33, 562, label, 11.5, SUB, "middle")
    s.rect(34 + 3 * 118, 460, 66, 66, fill="none", stroke=GOLD, rx=6, sw=3)
    s.bar(34 + 3 * 118 + 6, 512, 54, 6, 0.6, GOLD)
    s.line(34 + 4 * 118 + 8, 468, 34 + 4 * 118 + 58, 518, stroke=RED, sw=2)
    s.rect(34 + 5 * 118, 460, 66, 66, fill="none", stroke=CYAN, rx=6, sw=2, dash="5 4")
    s.text(34 + 5 * 118 + 33, 516, "돌아가면", 10, CYAN, "middle")
    # 인장 범례
    s.rect(880, 452, 686, 122, fill=PANEL, stroke=LINE, rx=6)
    s.text(896, 476, "인장과 상태이상 — 색만으로 구분하지 않는다", 13, TEXT, weight=700)
    for i, (k, st) in enumerate((("화염", "점화"), ("서리", "냉기 → 빙결"), ("뇌전", "감전"), ("성광", "낙인"), ("공허", "침식"))):
        x = 904 + i * 132
        sigil(s, x + 8, 506, k, 10)
        s.text(x + 26, 511, k, 13, TEXT, weight=700)
        s.text(x, 536, st, 11.5, SUB)
    s.text(896, 562, "룬 소켓은 칸 아래 왼쪽의 점. 찬 소켓은 채워서, 빈 소켓은 테두리만.", 11, SUB)

    # D. 적 표시
    head(628, "D", "적 표시", "높이가 먼저 읽혀야 한다. 막대는 필요해진 순간에만 나타난다")
    items = [("기본", "표시 없음.\n위치가 정보다", dict()),
             ("턱밑 띠", "흰 윤곽선.\n근접이 닿는다", dict(band=True)),
             ("맞은 뒤", "체력과 붙잡기.\n3초 뒤 사라진다", dict(band=True, hp=0.6, grip=0.4)),
             ("빙결", "육각 테두리.\n붙잡기 절반", dict(frozen=True, grip=0.5)),
             ("올라서는 중", "2초. 고리가 준다.\n붙잡기 절반", dict(band=True)),
             ("대형", "막대가 늘 보인다.\n약점은 따로 표시", dict(kind="짐꾼 거인", hp=0.8, grip=0.9)),
             ("엘리트", "금색 관.\n마정을 떨어뜨린다", dict(kind="방패병"))]
    for i, (label, desc, kw) in enumerate(items):
        x = 34 + i * 220
        s.rect(x, 646, 208, 100, fill=PANEL, stroke=LINE, rx=6)
        enemy(s, x + 44, 686, **kw)
        if label == "올라서는 중":
            s.path(f"M {x + 44} 668 A 18 18 0 1 1 {x + 26} 686", stroke="#ffffff", sw=3)
        if label == "엘리트":
            s.poly([(x + 32, 668), (x + 36, 658), (x + 44, 666), (x + 52, 658), (x + 56, 668)], fill=GOLD, stroke=BG)
        s.text(x + 86, 672, label, 13, TEXT, weight=700)
        for j, ln in enumerate(desc.split("\n")):
            s.text(x + 86, 692 + j * 16, ln, 11, SUB)
    s.save("ui_hud_states.svg")


# --------------------------------------------------------------------------
# 2. 각성 트리
TIER_R = {1: 96, 2: 146, 3: 196, 4: 246, 5: 294}
BRANCH_DEG = {"철벽": 0, "파멸": 120, "섬광": 240}
TIER_OPEN = {1: "프롤로그", 2: "1장 대파편", 3: "2장 대파편", 4: "3장 대파편", 5: "4장 대파편"}
CROSS_NEED = {2: 5, 4: 9}


def tree_layout(nodes, cx, cy):
    """노드 id → (x, y). 가지는 120° 간격의 부채, 단계는 고리."""
    pos = {}
    stem = [n for n in nodes if n["branch"] == "줄기"]
    ring = {"S1": 0, "S3": 60, "S5": 120, "S6": 180, "S4": 240, "S2": 300}     # 선행 조건이 이웃하도록
    for n in stem:
        pos[n["id"]] = polar(cx, cy, 50, ring[n["id"]])
    for br, base in BRANCH_DEG.items():
        ns = [n for n in nodes if n["branch"] == br]
        skills = sorted((n for n in ns if n["kind"] == "기술"), key=lambda n: n["tier"])
        off = {}
        chain = {skills[0]["id"]: {0: -12, 1: -27, 2: -34}, skills[1]["id"]: {0: -9, 1: -17, 2: -30},
                 skills[2]["id"]: {0: 17, 1: 15, 2: 17}}
        for sk in skills:
            off[sk["id"]] = chain[sk["id"]][0]
        for n in ns:
            if n["kind"] == "변형":
                off[n["id"]] = chain[n["of"]][n["stage"]]
            elif n["kind"] in ("진명", "궁극", "진화"):
                off[n["id"]] = 0
        passive = {1: [12], 2: [9, 27], 3: [34], 4: [-15, 30], 5: [-17]}
        for tier, slots in passive.items():
            ps = [n for n in ns if n["kind"] == "수동" and n["tier"] == tier]
            for n, o in zip(ps, slots):
                off[n["id"]] = o
        for n in ns:
            pos[n["id"]] = polar(cx, cy, TIER_R[n["tier"]], base + off[n["id"]])
    for n in nodes:
        if n["kind"] == "교차":
            a, b = (BRANCH_DEG[p] for p in n["pair"])
            mid = (a + b) / 2 if abs(a - b) <= 180 else ((a + b + 360) / 2) % 360
            pos[n["id"]] = polar(cx, cy, TIER_R[n["tier"]], mid)
    return pos


def tree_state(data, awake, virtual, max_tier):
    """노드 id → 깨움 / 가상 / 가능 / 멀다 / 잠김."""
    nodes = data["nodes"]
    have = set(awake) | set(virtual)
    count = lambda br, tier=None: sum(1 for n in nodes if n["id"] in have and n["branch"] == br
                                      and (tier is None or n["tier"] == tier))
    st = {}
    for n in nodes:
        i = n["id"]
        if i in awake:
            st[i] = "깨움"
        elif i in virtual:
            st[i] = "가상"
        elif n["tier"] > max_tier:
            st[i] = "잠김"
        else:
            ok = all(r in have for r in n["requires"])
            if n["kind"] == "교차":
                ok = ok and all(count(p) >= n["need"] for p in n["pair"])
            elif n["branch"] != "줄기" and n["tier"] > 1:
                ok = ok and count(n["branch"], n["tier"] - 1) >= 2
            st[i] = "가능" if ok else "멀다"
    return st


def draw_node(s, n, x, y, state, tag_on=False, selected=False, equipped=None):
    col = {"깨움": GOLD, "가상": CYAN, "가능": TEXT, "멀다": LINE, "잠김": DIM}[state]
    fill = {"깨움": GOLD, "가상": BG, "가능": BG, "멀다": BG, "잠김": BG}[state]
    sw = 2 if state in ("깨움", "가능", "가상") else 1.2
    dash = "3 3" if state == "가상" else None
    k = n["kind"]
    if tag_on and state != "잠김":
        s.circle(x, y, 19, stroke=GOLD, sw=1, dash="1 3")
    if selected:
        s.circle(x, y, 23, stroke="#ffffff", sw=1.5)
    if k == "기술":
        s.poly(diamond(x, y, 13), fill=fill, stroke=col, sw=sw, dash=dash)
    elif k == "변형":
        s.poly(diamond(x, y, 9), fill=BG, stroke=col, sw=sw, dash=dash)
        if state == "깨움":                                   # 고른 쪽(A=왼쪽, B=오른쪽)만 찬다
            s.poly([(x, y - 9), (x, y + 9), (x - 9, y)], fill=GOLD, stroke="none")
        s.line(x, y - 9, x, y + 9, stroke=col, sw=1)
    elif k == "수동":
        s.circle(x, y, 7, fill=fill, stroke=col, sw=sw, dash=dash)
    elif k == "줄기":
        s.rect(x - 7, y - 7, 14, 14, fill=fill, stroke=col, rx=2, sw=sw, dash=dash)
    elif k == "교차":
        s.poly(star4(x, y, 13), fill=fill, stroke=col, sw=sw, dash=dash)
    elif k == "진명":
        s.poly(ngon(x, y, 14, 6), fill=BG, stroke=col, sw=sw, dash=dash)
        s.poly(ngon(x, y, 8, 6), fill=fill, stroke=col, sw=1)
    elif k == "궁극":
        s.circle(x, y, 16, fill=BG, stroke=col, sw=sw, dash=dash)
        s.poly(diamond(x, y, 10), fill=fill, stroke=col, sw=1)
    elif k == "진화":
        s.circle(x, y, 11, fill=BG, stroke=col, sw=sw, dash=dash)
        s.circle(x, y, 5, fill=fill, stroke=col, sw=1)
    if equipped:
        s.rect(x - 8, y - 5.5, 16, 13, fill=BG, stroke="none", rx=3)
        s.text(x, y + 4.5, equipped, 11, GOLD, "middle", 700)


def tree():
    data = json.loads((ROOT / "data" / "trees" / "sword.json").read_text(encoding="utf-8"))
    nodes = data["nodes"]
    by = {n["id"]: n for n in nodes}
    build = next(b for b in data["builds"] if b["name"] == "추락 전문가")
    order = [i for i in build["nodes"] if by[i]["tier"] <= 3]
    awake, virtual = order[:22], ["P10", "P11"]
    assert all(v in order and v not in awake for v in virtual), "예시 상태가 데이터와 어긋난다"
    st = tree_state(data, awake, virtual, 3)
    sel = by["P10"]
    equip = {"P1": "Q", "P3": "E", "I1": "R"}
    tag_filter = "추락"

    s = Svg(title="각성 트리 화면 — 검, 3장 진행 중 (3단계까지 열림)")
    spent = sum(by[v]["cost"] for v in virtual)
    topbar(s, "레갈리아", pending=(spent, 0, 0, 0))

    cx, cy = 548, 446
    # 단계 고리
    for t, r in TIER_R.items():
        locked = t > 3
        s.circle(cx, cy, r, stroke=DIM if locked else LINE, sw=1, dash="2 7" if locked else "1 5")
    s.circle(cx, cy, 74, fill=PANEL, stroke=LINE)
    s.text(cx, cy - 2, "검", 22, GOLD, "middle", 700)
    s.text(cx, cy + 16, f"{len(awake)} / {len(nodes)}", 11, SUB, "middle")
    # 가지의 경계와 이름
    for br, deg in BRANCH_DEG.items():
        for d in (-48, 48):
            x1, y1 = polar(cx, cy, 82, deg + d)
            x2, y2 = polar(cx, cy, 310, deg + d)
            s.line(x1, y1, x2, y2, stroke=DIM, dash="1 6")
    for br, (lx, ly, anchor) in {"철벽": (cx + 128, 134, "start"), "파멸": (940, 716, "end"), "섬광": (156, 716, "start")}.items():
        n_aw = sum(1 for i in awake if by[i]["branch"] == br)
        s.text(lx, ly, br, 18, TEXT, anchor, 700)
        s.text(lx, ly + 18, f"깨운 별 {n_aw} / 19", 11.5, SUB, anchor)
    # 단계 번호 (가지의 경계선 위)
    for t, r in TIER_R.items():
        x, y = polar(cx, cy, r, 48)
        s.circle(x, y, 9, fill=BG, stroke="none")
        s.text(x, y + 4, t, 11, SUB if t <= 3 else LINE, "middle", 700)
    s.rect(880, 398, 204, 66, fill=BG, stroke=DIM, rx=6)
    s.text(894, 420, "잠긴 고리", 12, TEXT, weight=700)
    for i, t in enumerate((4, 5)):
        s.text(894, 438 + i * 16, f"{t}단계 — {TIER_OPEN[t]}에 열린다", 11, SUB)
    # 선: 선행 조건
    pos = tree_layout(nodes, cx, cy)
    for n in nodes:
        for r in n["requires"]:
            on = st[n["id"]] in ("깨움", "가상") and st[r] in ("깨움", "가상")
            s.line(*pos[r], *pos[n["id"]], stroke=GOLD if on else (LINE if st[n["id"]] != "잠김" else DIM),
                   sw=2 if on else 1, dash=None if on else "3 4")
    for br, deg in BRANCH_DEG.items():                           # 줄기에서 가지로
        for n in nodes:
            if n["branch"] == br and n["tier"] == 1:
                x0, y0 = polar(cx, cy, 74, deg)
                on = st[n["id"]] == "깨움"
                s.line(x0, y0, *pos[n["id"]], stroke=GOLD if on else LINE, sw=2 if on else 1)
    for n in nodes:
        x, y = pos[n["id"]]
        draw_node(s, n, x, y, st[n["id"]], tag_on=tag_filter in n["tags"], selected=n is sel,
                  equipped=equip.get(n["id"]))
    # 이름표: 기술, 진명, 궁극
    for n in nodes:
        if n["kind"] in ("기술", "진명", "궁극"):
            x, y = pos[n["id"]]
            dx, dy = x - cx, y - cy
            d = math.hypot(dx, dy)
            ux, uy = dx / d, dy / d
            col = {"깨움": TEXT, "가상": CYAN, "가능": TEXT, "멀다": SUB, "잠김": LINE}[st[n["id"]]]
            anchor = "start" if ux > 0.35 else ("end" if ux < -0.35 else "middle")
            lx, ly = x + ux * 21, y + uy * 23 + 4
            s.rect(lx - (0 if anchor == "start" else tw(n["name"], 11.5) / (2 if anchor == "middle" else 1)) - 2,
                   ly - 11, tw(n["name"], 11.5) + 4, 14, fill=BG, stroke="none", rx=2, op=0.8)
            s.text(lx, ly, n["name"], 11.5, col, anchor, 500)
    # 교차 노드의 조건
    for n in nodes:
        if n["kind"] == "교차" and n["tier"] == 2 and st[n["id"]] != "깨움":
            x, y = pos[n["id"]]
            have = set(awake) | set(virtual)
            a, b = n["pair"]
            ca, cb = (sum(1 for i in have if by[i]["branch"] == p) for p in (a, b))
            s.text(x, y + 28, f"{a} {min(ca, 5)}/5 · {b} {min(cb, 5)}/5", 10, SUB, "middle")

    # 위 왼쪽: 태그 검색
    s.text(24, 92, "태그", 12, SUB)
    x = 58
    for t in TAGS:
        on = t == tag_filter
        x = s.chip(x, 76, t, 12, fill=GOLD if on else PANEL2, stroke=GOLD if on else LINE,
                   color=BG if on else TEXT, weight=700 if on else 400)
    s.text(x + 6, 92, "누르면 그 태그의 별·룬·시설이 빛난다", 11, SUB)
    # 위 오른쪽: 빌드 보관
    s.text(884, 92, "빌드", 12, SUB)
    x = 916
    for i, name in enumerate(("1 추락", "2 —", "3 —")):
        x = s.chip(x, 76, name, 12, stroke=GOLD if i == 0 else LINE, color=TEXT if i == 0 else SUB)
    # 범례
    lg = [("기술", "기술"), ("변형", "변형 A | B"), ("수동", "수동"), ("줄기", "줄기"), ("교차", "교차"),
          ("진명", "진명"), ("궁극", "궁극"), ("진화", "진화")]
    s.rect(24, 112, 108, 250, fill=BG, stroke=DIM, rx=6)
    for i, (k, label) in enumerate(lg):
        draw_node(s, {"kind": k, "tags": []}, 46, 134 + i * 22, "가능")
        s.text(68, 138 + i * 22, label, 11, SUB)
    for i, (state, label) in enumerate((("깨움", "깨운 별"), ("가능", "깨울 수 있다"), ("가상", "가상 투자"))):
        draw_node(s, {"kind": "수동", "tags": []}, 46, 310 + i * 20 - 2, state)
        s.text(68, 314 + i * 20 - 2, label, 11, SUB)
    s.rect(966, 112, 112, 62, fill=BG, stroke=DIM, rx=6)
    draw_node(s, {"kind": "수동", "tags": []}, 984, 132, "멀다")
    s.text(1000, 136, "아직 멀다", 11, SUB)
    draw_node(s, {"kind": "수동", "tags": []}, 984, 156, "잠김")
    s.text(1000, 160, "단계 잠김", 11, SUB)

    # 오른쪽: 고른 별
    px, pw = 1100, 476
    s.rect(px, 76, pw, 424, fill=PANEL, stroke=LINE, rx=6)
    draw_node(s, sel, px + 30, 108, "가상")
    s.text(px + 54, 104, sel["name"], 18, TEXT, weight=700)
    s.text(px + 54, 122, f"파멸 · {sel['tier']}단계 · 변형 분기", 11.5, SUB)
    s.chip(px + pw - 110, 92, f"성광석 {sel['cost']}", 13, fill=BG, stroke=GOLD, color=GOLD, weight=700)
    s.text(px + 20, 152, "둘 중 하나를 고른다. 왕좌에서 언제든 바꾼다.", 11.5, SUB)
    for i, key in enumerate(("a", "b")):
        v = sel[key]
        bx = px + 20 + i * 222
        on = i == 0
        s.rect(bx, 164, 214, 122, fill=PANEL2 if on else BG, stroke=CYAN if on else LINE, rx=6, sw=2 if on else 1)
        s.text(bx + 14, 188, f"{'A' if i == 0 else 'B'}  {v['name']}", 14, TEXT, weight=700)
        if on:
            s.text(bx + 200, 188, "고름", 11, CYAN, "end", 700)
        s.para(bx + 14, 210, v["text"], 188, 11.5, TEXT if on else SUB, 1.5)
    s.text(px + 20, 312, "대지 가르기가 이렇게 바뀐다", 12, SUB)
    rows = [("닿는 레인", "1줄", "3줄 (양옆은 60%)"), ("피해 · 밀어냄", "40 · 8", "40 · 8  /  24 · 4.8"),
            ("재사용", "12초", "12초")]
    for i, (k, a, b) in enumerate(rows):
        y = 334 + i * 22
        s.text(px + 20, y, k, 12, SUB)
        s.text(px + 150, y, a, 12, TEXT)
        s.text(px + 216, y, "→", 12, SUB)
        s.text(px + 240, y, b, 12, CYAN if a != b else TEXT, weight=700 if a != b else 400)
    s.text(px + 20, 410, "태그", 12, SUB)
    s.chip(px + 56, 394, "연쇄", 12, stroke=GOLD)
    s.text(px + 20, 440, "조건", 12, SUB)
    s.text(px + 56, 440, "✓ 대지 가르기를 깨웠다     ✓ 3단계 열림", 12, GREEN)
    s.button(px + 20, 456, 214, 32, "가상 투자 취소", size=12)
    s.button(px + 242, 456, 214, 32, "A ↔ B 바꿔 보기", size=12)

    # 오른쪽 아래: 공명
    s.rect(px, 512, pw, 190, fill=PANEL, stroke=LINE, rx=6)
    s.text(px + 20, 538, "공명", 15, TEXT, weight=700)
    s.text(px + 62, 538, "같은 태그 3 / 6 / 9개. 트리 + 룬 + 성물 + 시설", 11, SUB)
    have, after = set(awake), set(awake) | set(virtual)
    extra = {"수호": 1, "표식": 1}                                # 가시 난간, 발리스타
    for i, t in enumerate(TAGS):
        c0 = sum(1 for j in have if t in by[j]["tags"]) + extra.get(t, 0)
        c1 = sum(1 for j in after if t in by[j]["tags"]) + extra.get(t, 0)
        col, row = i % 2, i // 2
        x, y = px + 20 + col * 228, 556 + row * 34
        s.text(x, y + 14, t, 12.5, TEXT if c1 else SUB, weight=700 if t == tag_filter else 400)
        for k in range(9):
            filled, new = k < c0, c0 <= k < c1
            s.rect(x + 40 + k * 14 + (k // 3) * 5, y + 3, 11, 13, fill=GOLD if filled else (CYAN if new else BG),
                   stroke=CYAN if new else (GOLD if filled else DIM), rx=2)
        s.text(x + 186, y + 14, f"{c0}" if c0 == c1 else f"{c0} → {c1}", 12, CYAN if c1 != c0 else SUB, weight=700)
    # 공명 변화 알림
    s.rect(px, 712, pw, 50, fill=BG, stroke=CYAN, rx=6)
    s.text(px + 16, 733, "추락 6 공명이 켜진다", 13, CYAN, weight=700)
    s.text(px + 16, 751, "떨어진 적이 아래 적을 함께 떨어뜨린다", 12, TEXT)

    # 아래: 장착
    y0 = 772
    s.line(0, y0 - 8, 1088, y0 - 8, stroke=DIM)
    s.text(24, y0 + 12, "장착", 13, TEXT, weight=700)
    s.text(24, y0 + 30, "깨운 기술 4", 11, SUB)
    s.text(24, y0 + 46, "칸은 3", 11, SUB)
    for i, (k, name, sg, rn) in enumerate((("Q", "내려찍기", "서리", 2), ("E", "대지 가르기", "화염", 1),
                                           ("R", "왕의 방패", None, 1))):
        slot(s, 108 + i * 78, y0, k, name, sig=sg, size=62, runes=rn)
    slot(s, 108 + 3 * 78 + 16, y0, "F", "", locked="3장 대파편", size=62)
    s.text(450, y0 + 12, "진명", 13, TEXT, weight=700)
    s.text(450, y0 + 30, "슬롯 1 / 3", 11, SUB)
    s.rect(516, y0, 150, 62, fill=PANEL, stroke=GOLD, rx=6)
    s.text(591, y0 + 28, "성을 부수는 검", 12.5, TEXT, "middle", 700)
    s.text(591, y0 + 46, "연타를 칠 수 없다", 10.5, RED, "middle")
    for i, when in enumerate(("4장 대파편", "5장 대파편")):
        s.rect(676 + i * 108, y0, 98, 62, fill=BG, stroke=DIM, rx=6, dash="4 3")
        s.text(725 + i * 108, y0 + 28, "잠김", 12, SUB, "middle")
        s.text(725 + i * 108, y0 + 46, when, 10, SUB, "middle")
    s.text(906, y0 + 12, "인장", 13, TEXT, weight=700)
    sigil(s, 916, y0 + 36, "서리", 9)
    sigil(s, 946, y0 + 36, "화염", 9)
    s.text(906, y0 + 62, "서리 + 화염 = 파열", 10.5, SUB)
    # 확정
    s.button(px, 772, 300, 62, f"깨우기 확정 — 별 {len(virtual)}개 · 성광석 {spent}", primary=True, size=14)
    s.button(px + 310, 772, 166, 28, "가상 투자 모두 취소", size=11.5)
    s.button(px + 310, 806, 166, 28, "처음부터 재배치 (무료)", size=11.5)

    hints(s, [("좌클릭", "가상 투자"), ("우클릭", "취소"), ("Enter", "확정"), ("1 2 3", "기술 칸에 장착"),
              ("휠", "확대"), ("F5", "빌드 저장"), ("Tab", "닫기")])
    for n, x, y in ((1, 44, 70), (2, 906, 70), (3, 618, 392), (4, 584, 584), (5, 1084, 110), (6, 1094, 70),
                    (7, 1094, 506), (8, 1094, 706), (9, 92, 766), (10, 510, 766), (11, 1094, 766),
                    (12, 1076, 30), (13, 874, 398)):
        s.badge(n, x, y)
    s.save("ui_tree.svg")
    return dict(awake=len(awake), virtual=virtual, spent=spent)


# --------------------------------------------------------------------------
# 3. 시설 건설
FACILITIES = [  # 이름, 태그, 열리는 장, 한 줄
    ("망루", "", 1, "경보를 앞당기고 다음 습격을 보여 준다"),
    ("발리스타", "표식", 1, "느리고 강한 자동 사격"),
    ("가시 난간", "수호", 1, "턱에 닿은 적에게 피해, 등반 지연"),
    ("수리 작업대", "수호", 1, "고요 중 성벽을 자동 수리"),
    ("기름 솥", "지대", 2, "구역 아래로 쏟는다. 화염이 닿으면 불탄다"),
    ("성문 방책", "수호", 2, "성문길의 적을 막고 늦춘다"),
    ("광차 선로", "", 2, "선로 위 이동 절반, 운반 2배"),
    ("성광 등대", "", 3, "공중 적을 늦추고 낙인"),
    ("피뢰탑", "연쇄", 3, "주기적 낙뢰, 연쇄의 중계점"),
    ("낙석 장치", "추락", 4, "수동 발동. 한 경로 전체에 추락"),
    ("증폭 제단", "", 4, "양옆 시설의 위력을 올린다"),
]


def castle():
    s = Svg(title="시설 건설 화면 — 왕좌 메뉴의 '성' 탭 (2장 기준)")
    topbar(s, "성", pending=(0, 10, 0, 0))

    # --- 성의 도면 ---
    mx, my, mw, mh = 24, 80, 1060, 388
    s.rect(mx, my, mw, mh, fill="#141a22", stroke=LINE, rx=6)
    s.text(mx + 16, my + 24, "성의 도면", 15, TEXT, weight=700)
    s.text(mx + 92, my + 24, "칸을 고르고 → 시설을 고른다. 위가 곶, 아래가 벼랑", 11.5, SUB)
    wx, wy, ww = mx + 40, my + 96, 820                       # 성벽 위 통로
    s.rect(wx, wy, ww, 70, fill="#2a3340", stroke=LINE, rx=2)
    s.rect(wx + ww, wy - 26, 160, 96, fill="#343e4d", stroke=LINE, rx=2)
    s.text(wx + ww + 80, wy - 6, "성문탑", 12, TEXT, "middle")
    s.line(wx, wy + 70, wx + ww, wy + 70, stroke=TEXT, sw=4)
    for i in range(1, 3):
        s.line(wx + ww * i / 3, wy, wx + ww * i / 3, wy + 70, stroke=LINE, dash="6 5")
    for i in range(3):
        s.text(wx + ww * (i + 0.5) / 3, wy - 10, f"구역 {i + 1}", 12, SUB, "middle")
    s.text(wx + 410, my + 56, "뒷문 ↑", 11.5, SUB, "middle")
    # 절벽과 경로
    cy0 = wy + 74
    for i, (name, col) in enumerate((("3단", "#3a3f47"), ("2단", "#2e3239"), ("1단", "#23262c"))):
        s.rect(wx, cy0 + i * 44, ww, 44, fill=col, stroke="none", rx=0)
        s.text(wx - 8, cy0 + i * 44 + 27, name, 11, SUB, "end")
    s.rect(wx, cy0, ww, 132, fill="none", stroke=LINE, rx=0)
    lanes = [wx + 250 + i * 64 for i in range(6)]
    for x in lanes:
        s.line(x, cy0, x, cy0 + 132, stroke="#ffffff", sw=1, dash="2 8", op=0.3)
    s.text(wx + 410, cy0 + 150, "중앙 절벽면 — 레인 6줄", 11.5, TEXT, "middle")
    s.rect(wx + 14, cy0 + 8, 190, 116, fill=BG, stroke=DIM, rx=3, dash="5 4", op=0.9)
    s.text(wx + 109, cy0 + 62, "좌 절벽면", 12, SUB, "middle")
    s.text(wx + 109, cy0 + 80, "3장에 열린다", 11, SUB, "middle")
    for x in (wx + 222, wx + 646):
        s.rect(x, cy0 + 44, 22, 12, fill="#4a4f58", stroke=LINE, rx=1)
    s.text(wx + 700, cy0 + 56, "바위턱", 10.5, SUB)
    s.rect(wx + ww + 30, wy + 70, 100, 136, fill="#1d232c", stroke=LINE, rx=0)
    s.text(wx + ww + 80, cy0 + 60, "성문길", 12, TEXT, "middle")
    s.text(wx + ww + 80, cy0 + 78, "중장갑 · 공성", 10.5, SUB, "middle")
    # 슬롯 8칸
    built = {1: ("망루", "Ⅰ"), 2: ("발리스타", "Ⅱ"), 3: ("가시 난간", "Ⅰ")}
    opens = {5: "3장", 6: "3장", 7: "4장", 8: "5장"}
    sw_, gap = 84, 16
    sx0 = wx + (ww - (8 * sw_ + 7 * gap)) / 2
    for i in range(1, 9):
        x = sx0 + (i - 1) * (sw_ + gap)
        y = wy + 8
        if i in built:
            s.rect(x, y, sw_, 54, fill=PANEL, stroke=LINE, rx=4)
            s.text(x + sw_ / 2, y + 24, built[i][0], 12, TEXT, "middle", 500)
            lv = {"Ⅰ": 1, "Ⅱ": 2}[built[i][1]]
            for k in range(3):                                # 셋째 칸은 2장의 상한 밖
                s.rect(x + 22 + k * 14, y + 34, 11, 8, fill=GOLD if k < lv else BG,
                       stroke=GOLD if k < 2 else DIM, rx=1, dash=None if k < 2 else "2 2")
        elif i == 4:
            s.rect(x - 3, y - 3, sw_ + 6, 60, fill="none", stroke="#ffffff", rx=6, sw=1.5)
            s.rect(x, y, sw_, 54, fill=PANEL2, stroke=CYAN, rx=4, sw=2, dash="5 4")
            s.text(x + sw_ / 2, y + 24, "기름 솥", 12, CYAN, "middle", 700)
            s.text(x + sw_ / 2, y + 42, "미리 보기", 10, CYAN, "middle")
        else:
            s.rect(x, y, sw_, 54, fill=BG, stroke=DIM, rx=4, dash="3 4")
            s.text(x + sw_ / 2, y + 24, "무너짐", 11, SUB, "middle")
            s.text(x + sw_ / 2, y + 41, f"{opens[i]}에 복구", 10, SUB, "middle")
        s.text(x + 4, y - 4, i, 10, SUB)
    # 미리 보기: 기름 솥이 덮는 범위 (4번 칸 아래)
    x4 = sx0 + 3 * (sw_ + gap)
    s.rect(x4 - 70, cy0, sw_ + 140, 88, fill=CYAN, stroke=CYAN, rx=0, op=0.22)
    s.text(x4 + sw_ / 2, cy0 + 106, "덮는 범위: 레인 2~5, 3단과 2단", 10.5, CYAN, "middle", 700)
    # 지어진 시설의 범위 (옅게)
    x3 = sx0 + 2 * (sw_ + gap)
    s.rect(x3 - 60, cy0 - 3, sw_ + 120, 7, fill=GREEN, stroke="none", rx=0, op=0.6)
    # 오른쪽 띠: 층 보기
    s.text(mx + mw - 262, my + 25, "보기", 11.5, SUB)
    vx = mx + mw - 232
    for i, v in enumerate(("범위", "태그", "다음 습격")):
        vx = s.chip(vx, my + 10, v, 11.5, stroke=GOLD if i == 0 else LINE, color=TEXT if i == 0 else SUB)

    # --- 건설 목록 ---
    ly = 482
    s.text(46, ly + 14, "4번 칸에 지을 시설", 15, TEXT, weight=700)
    s.text(198, ly + 14, "11종 가운데 7종이 열려 있다 · 같은 시설을 여러 칸에 지을 수 있다", 11.5, SUB)
    cw, ch = 168, 86
    for i, (name, tag, chap, desc) in enumerate(FACILITIES):
        col, row = i % 6, i // 6
        x, y = 24 + col * (cw + 10), ly + 28 + row * (ch + 10)
        locked = chap > 2
        on = name == "기름 솥"
        s.rect(x, y, cw, ch, fill=PANEL2 if on else (PANEL if not locked else BG),
               stroke=CYAN if on else (LINE if not locked else DIM), rx=6, sw=2 if on else 1,
               dash="4 3" if locked else None)
        s.rect(x + 10, y + 10, 30, 30, fill=BG, stroke=LINE if not locked else DIM, rx=4)
        s.text(x + 48, y + 24, name, 13.5, TEXT if not locked else SUB, weight=700)
        if locked:
            s.text(x + 48, y + 41, f"{chap}장에 열린다", 11, SUB)
        else:
            s.text(x + 48, y + 41, "흑철 10", 11.5, GOLD, weight=700)
            if tag:
                s.chip(x + cw - tw(tag, 10.5) - 26, y + 10, tag, 10.5, h=18)
        for j, ln in enumerate(wrap(desc, cw - 20, 10.5)[:2]):
            s.text(x + 10, y + 58 + j * 14, ln, 10.5, SUB)
    # 성벽 (목록 오른쪽 아래 빈 칸)
    wx2 = 24 + 5 * (cw + 10)
    s.rect(wx2, ly + 28 + ch + 10, cw, ch, fill=BG, stroke=DIM, rx=6)
    s.text(wx2 + 12, ly + 28 + ch + 34, "철거와 특화 변경", 12, TEXT, weight=700)
    s.text(wx2 + 12, ly + 28 + ch + 52, "전액 환급", 11, SUB)
    s.text(wx2 + 12, ly + 28 + ch + 70, "되돌리기 2 / 3 남음", 11.5, GOLD, weight=700)

    # --- 성벽 띠 (아래) ---
    by_ = 712
    s.rect(24, by_, 1060, 130, fill=PANEL, stroke=LINE, rx=6)
    s.text(40, by_ + 26, "성벽", 15, TEXT, weight=700)
    s.text(84, by_ + 26, "742 / 1,150", 14, TEXT, weight=700)
    s.bar(40, by_ + 38, 420, 16, 742 / 1150, GOLD)
    s.rect(40 + 420 * 742 / 1150, by_ + 39, 420 * 50 / 1150, 14, fill=CYAN, stroke="none", rx=0, op=0.7)
    s.button(40, by_ + 68, 204, 36, "수리 +50 — 흑철 1")
    s.button(254, by_ + 68, 206, 36, "가득 수리 — 흑철 9")
    s.text(40, by_ + 120, "장이 끝나면 백성이 가득 고친다", 10.5, SUB)
    s.text(500, by_ + 26, "성벽 강화", 13, TEXT, weight=700)
    s.text(568, by_ + 26, "최대 내구도와 피해 경감. 영구", 11, SUB)
    costs = [12, 20, 30, 44, 60]
    for i, c in enumerate(costs):
        x = 500 + i * 112
        done, nxt = i < 1, i == 1
        s.rect(x, by_ + 40, 100, 50, fill=GOLD if done else (PANEL2 if nxt else BG),
               stroke=GOLD if (done or nxt) else DIM, rx=5, dash=None if (done or nxt) else "4 3")
        s.text(x + 50, by_ + 61, "ⅠⅡⅢⅣⅤ"[i], 15, BG if done else (TEXT if nxt else SUB), "middle", 700)
        s.text(x + 50, by_ + 80, "완료" if done else f"흑철 {c}", 11, BG if done else (GOLD if nxt else SUB), "middle",
               700)
    s.text(500, by_ + 112, "Ⅱ: 최대 내구도 1,150 → 1,300, 받는 피해 −5%", 11.5, SUB)

    # --- 오른쪽: 고른 시설 ---
    px, pw = 1100, 476
    s.rect(px, 80, pw, 762, fill=PANEL, stroke=LINE, rx=6)
    s.rect(px + 20, 100, 56, 56, fill=BG, stroke=LINE, rx=6)
    s.text(px + 90, 124, "기름 솥", 20, TEXT, weight=700)
    s.text(px + 90, 146, "4번 칸 · 구역 2 · 새로 짓는다", 12, SUB)
    s.chip(px + pw - 76, 106, "지대", 12, stroke=GOLD)
    y = s.para(px + 20, 186, "구역 아래로 끓는 기름을 쏟는다. 8초마다 한 번, 덮는 범위의 적에게 피해 20. "
               "화염 인장이 닿으면 4초 동안 불타는 지대가 된다.", pw - 40, 13, TEXT, 1.55)
    # 단계
    y += 12
    s.text(px + 20, y, "단계", 13, TEXT, weight=700)
    s.text(px + 58, y, "지은 것은 다음 장에도 그 자리에 그 단계로 남는다", 11, SUB)
    y += 14
    steps = [("Ⅰ", "흑철 10", "지금 짓는다", "now"), ("Ⅱ", "흑철 18", "살 수 있다", "open"),
             ("Ⅲ", "흑철 30", "3장부터", "cap"), ("특화", "흑철 44", "4장부터", "cap")]
    for i, (n, c, note, kind) in enumerate(steps):
        x = px + 20 + i * 110
        s.rect(x, y, 100, 66, fill=PANEL2 if kind == "now" else BG,
               stroke=CYAN if kind == "now" else (LINE if kind == "open" else DIM), rx=5,
               sw=2 if kind == "now" else 1, dash="4 3" if kind == "cap" else None)
        s.text(x + 50, y + 24, n, 16, TEXT if kind != "cap" else SUB, "middle", 700)
        s.text(x + 50, y + 42, c, 11.5, GOLD if kind != "cap" else SUB, "middle", 700)
        s.text(x + 50, y + 58, note, 10.5, CYAN if kind == "now" else SUB, "middle")
        if i < 3:
            s.line(x + 100, y + 33, x + 110, y + 33, stroke=LINE)
    y += 84
    s.text(px + 20, y, "특화 (둘 중 하나)", 12, SUB)
    s.text(px + 136, y, "끓는 납 — 대형 적에게 큰 피해  /  넓은 솥 — 범위와 지속", 11.5, SUB)
    # 빌드와의 맞물림
    y += 26
    s.line(px + 20, y - 12, px + pw - 20, y - 12, stroke=DIM)
    s.text(px + 20, y + 4, "지금 빌드와", 13, TEXT, weight=700)
    y += 24
    sigil(s, px + 28, y - 4, "화염", 7)
    y = s.para(px + 44, y, "E 대지 가르기에 화염 인장이 찍혀 있다. 균열이 기름을 지나면 불이 붙는다.", pw - 64, 12, TEXT)
    y += 6
    s.text(px + 20, y, "지대", 12.5, TEXT, weight=700)
    for k in range(9):
        s.rect(px + 60 + k * 14 + (k // 3) * 5, y - 11, 11, 13, fill=GOLD if k < 1 else (CYAN if k < 2 else BG),
               stroke=CYAN if k == 1 else (GOLD if k < 1 else DIM), rx=2)
    s.text(px + 206, y, "1 → 2", 12, CYAN, weight=700)
    s.text(px + 256, y, "하나 더 모으면 지대 범위 증가", 11.5, SUB)
    # 다음 습격
    y += 30
    s.line(px + 20, y - 12, px + pw - 20, y - 12, stroke=DIM)
    s.text(px + 20, y + 4, "다음 습격 (망루)", 13, TEXT, weight=700)
    y += 24
    s.text(px + 20, y, "절벽면: 등반병 7 · 방패병 2 · 그림자 칼 1", 12, TEXT)
    s.text(px + 20, y + 18, "성문길: 공성병 1", 12, TEXT)
    s.text(px + 20, y + 38, "기름 솥은 성문길에 닿지 않는다", 11.5, RED)
    # 비용과 확정
    y = 716
    s.line(px + 20, y - 14, px + pw - 20, y - 14, stroke=DIM)
    s.text(px + 20, y + 6, "흑철", 12, SUB)
    s.text(px + 56, y + 8, "18 → 8", 18, TEXT, weight=700)
    s.text(px + 140, y + 6, "남는 8개로 성벽을 400 고칠 수 있다", 11.5, SUB)
    s.button(px + 20, y + 26, pw - 40, 46, "4번 칸에 짓는다 — 흑철 10", primary=True, size=15)
    s.text(px + pw / 2, y + 92, "다음 습격부터 움직인다 · 철거하면 전액 돌려받는다 (이번 장 2회 남음)", 11, SUB, "middle")

    hints(s, [("← →", "칸 이동"), ("↑ ↓", "시설 고르기"), ("Enter", "짓기 · 강화"), ("X", "철거"), ("V", "보기 바꾸기"),
              ("R", "수리"), ("Esc", "왕좌로")])
    for n, x, y in ((1, 30, 74), (2, 412, 166), (3, 608, 166), (4, 424, 340), (5, 790, 100), (6, 30, 491),
                    (7, 894, 586), (8, 30, 706), (9, 494, 706), (10, 1094, 74), (11, 1094, 258), (12, 1094, 396),
                    (13, 1094, 506), (14, 1094, 742), (15, 76, 252)):
        s.badge(n, x, y)
    s.save("ui_castle.svg")


# --------------------------------------------------------------------------
# 4. 왕좌 메뉴
def throne():
    s = Svg(title="왕좌 메뉴 — 첫 화면 (2장 기준)")
    topbar(s, "왕좌")

    def card(x, y, w, h, title, sub=None):
        s.rect(x, y, w, h, fill=PANEL, stroke=LINE, rx=6)
        s.text(x + 18, y + 28, title, 16, TEXT, weight=700)
        if sub:
            s.text(x + 18 + tw(title, 16) + 12, y + 28, sub, 11.5, SUB)

    # 이번 고요에 할 수 있는 일
    s.rect(24, 80, 1552, 46, fill=BG, stroke=GOLD, rx=6)
    s.text(42, 109, "이번 고요에", 13, GOLD, weight=700)
    x = 124
    for t in ("깨울 수 있는 별 4", "왕 강화 2", "빈 시설 칸 1", "성벽 수리 −408"):
        x = s.chip(x, 91, t, 12.5, h=24)
    s.text(1558, 109, "2장 · 습격 6 / 12 다음 · 사건이 가깝다", 12, SUB, "end")

    # --- 레갈리아 ---
    card(24, 140, 500, 300, "레갈리아", "검 · 깨운 별 17 / 69")
    cx, cy = 104, 268
    for r in (30, 46, 62):
        s.circle(cx, cy, r, stroke=LINE, dash="1 5")
    for deg, n_on, col in ((0, 6, GOLD), (120, 7, GOLD), (240, 2, GOLD)):
        for k in range(5):
            x1, y1 = polar(cx, cy, 22 + k * 10, deg + (k % 2) * 16 - 8)
            s.circle(x1, y1, 3.2, fill=col if k < n_on / 1.5 else BG, stroke=col if k < n_on / 1.5 else LINE)
    s.text(cx, cy + 4, "검", 13, GOLD, "middle", 700)
    for i, (br, n) in enumerate((("철벽", 6), ("파멸", 7), ("섬광", 2))):
        s.text(190, 204 + i * 24, br, 12.5, TEXT)
        s.bar(232, 194 + i * 24, 150, 12, n / 19, GOLD)
        s.text(392, 204 + i * 24, f"{n} / 19", 11.5, SUB)
    s.text(190, 282, "깨울 수 있는 별 4 — 성광석 14부터", 12, CYAN, weight=700)
    s.text(190, 302, "2단계까지 열림 · 3단계는 2장 대파편", 11.5, SUB)
    for i, (k, name, sg) in enumerate((("Q", "내려찍기", "서리"), ("E", "대지 가르기", None), ("R", "왕의 방패", None))):
        slot(s, 44 + i * 70, 346, k, name, sig=sg, size=56, runes=1)
    slot(s, 44 + 3 * 70 + 10, 346, "F", "", locked="3장", size=56)
    s.button(354, 356, 152, 40, "트리 열기", size=13)
    s.key(486, 348, "Tab")

    # --- 성 ---
    card(24, 454, 500, 388, "성", "시설 3 / 4칸 · 단계 상한 2")
    s.text(42, 508, "성벽", 13, TEXT, weight=700)
    s.text(82, 508, "742 / 1,150", 13, TEXT, weight=700)
    s.bar(42, 518, 464, 14, 742 / 1150, GOLD)
    s.button(42, 544, 226, 36, "수리 +50 — 흑철 1")
    s.button(280, 544, 226, 36, "가득 수리 — 흑철 9")
    s.key(248, 540, "R")
    s.text(42, 612, "시설", 13, TEXT, weight=700)
    names = [("망루", "Ⅰ"), ("발리스타", "Ⅱ"), ("가시 난간", "Ⅰ"), ("빈 칸", "+"), (None, "3장"), (None, "3장"),
             (None, "4장"), (None, "5장")]
    for i, (n, t) in enumerate(names):
        x = 42 + i * 58.5
        if n and n != "빈 칸":
            s.rect(x, 624, 52, 60, fill=PANEL2, stroke=LINE, rx=4)
            for j, ln in enumerate(wrap(n, 44, 11)[:2]):
                s.text(x + 26, 644 + j * 13, ln, 11, TEXT, "middle")
            s.text(x + 26, 678, t, 11, GOLD, "middle", 700)
        elif n:
            s.rect(x, 624, 52, 60, fill=BG, stroke=CYAN, rx=4, dash="4 3")
            s.text(x + 26, 660, "+", 20, CYAN, "middle", 700)
        else:
            s.rect(x, 624, 52, 60, fill=BG, stroke=DIM, rx=4, dash="2 4")
            s.text(x + 26, 659, t, 10.5, SUB, "middle")
    s.text(42, 712, "빈 칸 1 · 강화할 수 있는 시설 2 (망루, 가시 난간)", 12, CYAN, weight=700)
    s.text(42, 732, "성벽 강화 Ⅱ — 흑철 20", 12, TEXT)
    s.text(42, 760, "성의 상처", 12, SUB)
    s.text(108, 760, "부서진 성문 (성문길이 늘 열려 있다)", 12, TEXT)
    s.button(354, 786, 152, 40, "성 열기", size=13)

    # --- 왕 강화 ---
    card(540, 140, 560, 446, "왕", "성광석으로 올린다. 영구 · 2장 상한 3단계")
    axes = [("다리", "이동 속도", 2, "+20%", "+30%", 36), ("등", "운반 한도", 1, "23", "30", 22),
            ("눈", "빛의 반경 · 광맥 탐지", 1, "+2m", "+4m, 광맥이 비친다", 22),
            ("심장", "체력 · 재기 · 왕의 귀환", 0, "100", "120", 12)]
    for i, (name, what, lv, cur, nxt, cost) in enumerate(axes):
        y = 190 + i * 92
        s.rect(558, y, 524, 80, fill=PANEL2 if i == 1 else BG, stroke="#ffffff" if i == 1 else DIM, rx=6)
        s.text(574, y + 30, name, 17, TEXT, weight=700)
        s.text(574, y + 52, what, 11, SUB)
        for k in range(5):
            cap = k >= 3
            s.rect(720 + k * 30, y + 16, 24, 16, fill=GOLD if k < lv else BG,
                   stroke=GOLD if k < lv else (DIM if cap else LINE), rx=3, dash="2 3" if cap else None)
        s.text(720, y + 56, cur, 13, TEXT, weight=700)
        s.text(720 + tw(cur, 13) + 8, y + 56, "→", 12, SUB)
        s.text(720 + tw(cur, 13) + 28, y + 56, nxt, 13, CYAN, weight=700)
        afford = cost <= 124
        s.button(940, y + 20, 126, 40, f"성광석 {cost}", primary=(i == 1), size=13, disabled=not afford)
    s.text(558, 570, "되돌리기: 전액 환급 · 이번 장 2 / 3 남음", 11.5, SUB)
    s.key(1010, 554, "X")
    s.text(1036, 570, "되돌리기", 11.5, SUB)

    # --- 성물 ---
    card(540, 600, 560, 242, "성물", "가진 것 4 · 가동 3 / 5")
    relics = [("길잡이 실타래", "뒷문으로 즉시 귀환. 주기당 1회"), ("광부의 눈", "광맥이 암반 너머로 보인다"),
              ("운반 석상", "떨어진 자원을 뒷문까지 나른다")]
    for i in range(5):
        x = 558 + i * 106
        if i < 3:
            s.rect(x, 648, 96, 96, fill=PANEL2, stroke=GOLD, rx=6)
            for j, ln in enumerate(wrap(relics[i][0], 84, 12)[:2]):
                s.text(x + 48, 690 + j * 16, ln, 12, TEXT, "middle", 500)
        else:
            s.rect(x, 648, 96, 96, fill=BG, stroke=LINE, rx=6, dash="4 3")
            s.text(x + 48, 701, "비어 있음", 11, SUB, "middle")
    s.text(558, 770, "쉬는 성물", 12, SUB)
    s.chip(624, 754, "깨진 모래시계", 12)
    s.text(558, 800, "깨진 모래시계 — 고요가 길어지고, 그만큼 습격이 거세진다", 11.5, SUB)
    s.text(558, 822, "칸을 누르면 바꿔 끼운다. 고요 중에는 값 없이 바꾼다", 11.5, SUB)

    # --- 다음 습격 ---
    card(1116, 140, 460, 250, "다음 습격", "7 / 12 · 망루가 보여 준다")
    comp = [("등반병", 7, "등반병"), ("방패병", 2, "방패병"), ("그림자 칼", 1, "그림자 칼"), ("공성병", 1, "공성병")]
    for i, (n, c, kind) in enumerate(comp):
        x = 1150 + i * 108
        enemy(s, x + 16, 214, kind)
        s.text(x + 16, 258, n, 11.5, TEXT, "middle")
        s.text(x + 16, 276, f"× {c}", 13, TEXT, "middle", 700)
    s.text(1134, 310, "경로", 12, SUB)
    s.text(1172, 310, "중앙 절벽면 75% · 성문길 25%", 12, TEXT)
    s.text(1134, 332, "위협", 12, SUB)
    s.bar(1172, 322, 200, 12, 0.56, RED)
    s.text(1382, 332, "지난 습격보다 +9%", 11.5, SUB)
    s.text(1134, 360, "처음 보는 적: 그림자 칼 — 빠르다. 왕을 노린다", 12, RED, weight=700)
    s.text(1134, 378, "망루가 없으면 이 칸은 '망루를 지으면 보인다'로 비어 있다", 10.5, SUB)

    # --- 이번 장 ---
    card(1116, 404, 460, 196, "이번 장", "2장 · 왕릉 언덕")
    for i in range(12):
        x = 1134 + i * 33
        done = i < 6
        s.rect(x, 448, 27, 20, fill=GOLD if done else BG, stroke=GOLD if done else LINE, rx=3)
        if i in (5, 11):
            s.poly([(x + 8, 444), (x + 13.5, 437), (x + 19, 444)], fill=RED, stroke="none")
    s.poly(diamond(1134 + 12 * 33 + 14, 458, 12), fill=BG, stroke=GOLD, sw=2)
    s.text(1134, 488, "습격 6 / 12 · ▲ 엘리트 · ◇ 대파편과 총공세", 11.5, SUB)
    s.text(1134, 516, "대파편", 12, SUB)
    s.text(1184, 516, "낙성지를 아직 찾지 못했다 (셋째 구간)", 12, TEXT)
    s.text(1134, 540, "곶", 12, SUB)
    s.text(1184, 540, "성광석 약 60% 남음 · 봉인실 1곳 안 열림", 12, TEXT)
    s.text(1134, 564, "결의", 12, SUB)
    s.text(1184, 564, "없음 (함락될 때마다 최대 내구도 +5%)", 12, SUB)
    s.button(1424, 548, 134, 36, "지도", size=13)
    s.key(1540, 540, "M")

    # --- 봉헌과 체계 ---
    card(1116, 614, 460, 228, "그 밖에")
    rows = [("봉헌", "성광석 1개로 부채 1%. 공허 인장을 받은 뒤에 나타난다", True),
            ("빌드 보관", "1 추락 · 2 비어 있음 · 3 비어 있음", False),
            ("난이도", "표준 — 고요 중 언제든 바꾼다", False),
            ("저장하고 나가기", "고요가 시작될 때 저장되어 있다", False)]
    for i, (k, v, off) in enumerate(rows):
        y = 660 + i * 44
        s.rect(1134, y, 424, 36, fill=BG, stroke=DIM if off else LINE, rx=5, dash="4 3" if off else None)
        s.text(1148, y + 23, k, 13, SUB if off else TEXT, weight=700)
        s.text(1270, y + 23, v, 11.5, SUB)

    hints(s, [("Q E", "탭"), ("방향키", "고르기"), ("Enter", "사기 · 열기"), ("X", "되돌리기"), ("R", "수리"),
              ("Tab", "트리"), ("M", "지도"), ("Esc", "일어선다 — 고요가 다시 흐른다")])
    for n, x, y in ((1, 18, 12), (2, 748, 12), (3, 1082, 12), (4, 18, 80), (5, 18, 140), (6, 18, 454),
                    (7, 534, 140), (8, 1088, 230), (9, 534, 600), (10, 1110, 140), (11, 1110, 404),
                    (12, 1110, 614), (13, 852, 870)):
        s.badge(n, x, y)
    s.save("ui_throne.svg")


def main():
    print("와이어프레임을 그린다 → docs/img/")
    battle()
    hud_states()
    info = tree()
    castle()
    throne()
    print(f"각성 트리의 예시 상태: 깨운 별 {info['awake']}, 가상 투자 {info['virtual']} (성광석 {info['spent']})")


if __name__ == "__main__":
    main()
