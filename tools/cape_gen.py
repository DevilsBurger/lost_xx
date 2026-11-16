#!/usr/bin/env python3
"""곶의 땅을 만들고, 검증하고, 재 본다.

규칙의 값은 data/cape/lands.json에 있다. 이 스크립트는 그 규칙의 기준 구현이다.
Unity의 생성기는 같은 규칙을 따르고, 같은 검증을 통과해야 한다.

실행:
  python3 tools/cape_gen.py stats            생성 → 검증 → 측정. 문서(docs/07)의 표를 갱신
  python3 tools/cape_gen.py render 7         시드 7의 다섯 땅을 그림(SVG)으로
  python3 tools/cape_gen.py ascii 1 7        1번 땅, 시드 7을 글자로 출력
"""
from __future__ import annotations

import heapq
import importlib.util
import json
import math
import pathlib
import random
import re
import statistics
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
CFG = json.loads((ROOT / "data" / "cape" / "lands.json").read_text(encoding="utf-8"))
DOC = ROOT / "docs" / "07_cape_generation.md"
W, H = CFG["grid"]["width"], CFG["grid"]["length"]
BAND = H // CFG["grid"]["bands"]
SEA, RIDGE, OPEN, RAW = "바다", "바위산", "빈터", "?"
ROCKS = ["흙", "연암", "경암", "흑암"]
HARD = CFG["hardness"]
N4 = ((1, 0), (-1, 0), (0, 1), (0, -1))


def band_of(y):
    return min(y // BAND, CFG["grid"]["bands"] - 1)


class Land:
    def __init__(self, cfg, seed):
        self.cfg, self.seed = cfg, seed
        self.rock = [[RAW] * W for _ in range(H)]
        self.ore = {}          # (x, y) -> [종류, 양, 썰물에만 드러나는가]
        self.feat = {}         # (x, y) -> 자리
        self.low = set()       # 밀물에 잠기는 갯골
        self.reserved = set()  # 자리의 발자국
        self.centers = []      # 자리의 중심
        self.boulders, self.crystals, self.corrupt = [], set(), set()
        self.entrance = (W // 2 - 1, 0)
        self.crater = None
        self.passes = 0        # 이어지게 하려고 뚫은 고개의 타일 수

    def at(self, x, y):
        return self.rock[y][x] if 0 <= x < W and 0 <= y < H else SEA

    def passable(self, x, y):
        return self.at(x, y) not in (SEA, RIDGE)


# --------------------------------------------------------------------------
# 잡음
# --------------------------------------------------------------------------
def value_noise(rng, scale):
    gw, gh = W // scale + 3, H // scale + 3
    g = [[rng.random() for _ in range(gw)] for _ in range(gh)]

    def f(x, y):
        fx, fy = x / scale, y / scale
        x0, y0 = int(fx), int(fy)
        tx, ty = fx - x0, fy - y0
        tx, ty = tx * tx * (3 - 2 * tx), ty * ty * (3 - 2 * ty)
        a = g[y0][x0] * (1 - tx) + g[y0][x0 + 1] * tx
        b = g[y0 + 1][x0] * (1 - tx) + g[y0 + 1][x0 + 1] * tx
        return a * (1 - ty) + b * ty
    return f


def fbm(rng, s1, s2):
    n1, n2 = value_noise(rng, s1), value_noise(rng, s2)
    return lambda x, y: 0.65 * n1(x, y) + 0.35 * n2(x, y)


# --------------------------------------------------------------------------
# 생성
# --------------------------------------------------------------------------
def generate(cfg, seed, attempt=0):
    rng = random.Random(seed * 1000 + cfg["id"] + attempt * 7919)
    L = Land(cfg, seed)
    S = CFG["structures"]

    # 1. 해안선: 좌우는 바다. 땅끝은 뒤쪽도 바다
    lm = rm = 1
    for y in range(H):
        lm = max(0, min(2, lm + rng.choice((-1, 0, 0, 1))))
        rm = max(0, min(2, rm + rng.choice((-1, 0, 0, 1))))
        for x in range(W):
            if x < lm or x >= W - rm:
                L.rock[y][x] = SEA
    if cfg["sea_end"]:
        end = 2
        for x in range(W):
            end = max(1, min(3, end + rng.choice((-1, 0, 0, 1))))
            for y in range(H - end, H):
                L.rock[y][x] = SEA

    # 2. 바위산: 잡음의 높은 쪽부터 정해진 비율만큼
    ex0, ex1 = S["entrance"]["cols"]
    noise = fbm(rng, 6, 3)
    cand = [(x, y) for y in range(H) for x in range(W)
            if L.rock[y][x] == RAW and not (y < 4 and ex0 - 2 <= x <= ex1 + 2)]
    cand.sort(key=lambda p: -noise(*p))
    for x, y in cand[:int(len(cand) * cfg["ridge"])]:
        L.rock[y][x] = RIDGE

    # 3. 갯골: 땅을 가로지르는 낮은 띠. 둑 하나는 언제나 말라 있다
    for r0 in cfg["channels"]:
        cy, xd = r0, rng.randint(4, W - 6)
        for x in range(W):
            if rng.random() < 0.35:
                cy = max(r0 - S["channel_wander"], min(r0 + S["channel_wander"], cy + rng.choice((-1, 1))))
            for y in (cy, cy + 1):
                if L.rock[y][x] != SEA:
                    L.rock[y][x] = OPEN
                    if x in (xd, xd + 1):
                        L.feat[(x, y)] = "둑"
                    else:
                        L.low.add((x, y))

    # 4. 입구의 빈터
    for y in range(S["entrance"]["rows"]):
        for x in range(ex0, ex1 + 1):
            L.rock[y][x] = OPEN
    L.feat[L.entrance] = "입구"

    # 5. 자리: 낙성지, 봉인실, 석관
    def place(size, y0, y1):
        for _ in range(400):
            x, y = rng.randint(2, W - 2 - size), rng.randint(y0, y1)
            c = (x + size // 2, y + size // 2)
            cells = [(x + i, y + j) for i in range(size) for j in range(size)]
            if any(L.at(*p) == SEA or p in L.low or p in L.reserved or p in L.feat for p in cells):
                continue
            if any(math.dist(c, o) < S["min_gap"] for o in L.centers):
                continue
            L.reserved.update(cells)
            L.centers.append(c)
            return x, y, c
        return None

    r = place(S["crater"]["size"], S["crater"]["rows"][0] - 2, S["crater"]["rows"][1] - 2)
    if r:
        x, y, c = r
        for i in range(5):
            for j in range(5):
                L.rock[y + j][x + i] = OPEN
        L.feat[c], L.crater = "낙성지", c
    for b, count in enumerate(cfg["vaults"]):
        for _ in range(count):
            r = (place(S["vault"]["size"], max(3, b * BAND), b * BAND + BAND - S["vault"]["size"])
                 or place(S["vault"]["size"], 3, H - 7))      # 구간 안에 자리가 없으면 어디든
            if r:
                x, y, c = r
                for i in range(5):
                    for j in range(5):
                        L.rock[y + j][x + i] = OPEN if 1 <= i <= 3 and 1 <= j <= 3 else cfg["vault_wall"]
                L.feat[c] = "봉인실"
    r = place(S["coffin"]["size"], BAND, H - 8)
    if r:
        x, y, c = r
        for i in range(3):
            for j in range(3):
                L.rock[y + j][x + i] = "경암"
        L.rock[c[1]][c[0]] = OPEN
        L.feat[c] = "석관"

    # 6. 끊긴 곳 잇기: 바위산에 막힌 땅이 있으면 가장 얇은 곳에 고개를 뚫는다
    while True:
        seen = flood(L, L.entrance)
        lost = [(x, y) for y in range(H) for x in range(W) if L.passable(x, y) and (x, y) not in seen]
        if not lost:
            break
        dist, prev = {p: 0 for p in seen}, {}
        dq = [(0, p) for p in seen]
        heapq.heapify(dq)
        target = None
        while dq:
            d, (x, y) = heapq.heappop(dq)
            if d > dist[(x, y)]:
                continue
            if L.passable(x, y) and (x, y) not in seen:
                target = (x, y)
                break
            for dx, dy in N4:
                q = (x + dx, y + dy)
                if L.at(*q) == SEA:
                    continue
                nd = d + (1 if L.at(*q) == RIDGE else 0)
                if nd < dist.get(q, 1e9):
                    dist[q], prev[q] = nd, (x, y)
                    heapq.heappush(dq, (nd, q))
        if target is None:      # 바다로 완전히 끊긴 조각은 바다로 돌린다
            for x, y in lost:
                L.rock[y][x] = SEA
            break
        p = target
        while p in prev:
            if L.at(*p) == RIDGE:
                L.rock[p[1]][p[0]] = RAW
                L.passes += 1
            p = prev[p]

    # 7. 암반: 구간마다 정해진 비율로, 잡음을 따라 덩어리지게
    noise = fbm(rng, 4, 2)
    for b in range(CFG["grid"]["bands"]):
        cells = [(x, y) for y in range(b * BAND, (b + 1) * BAND) for x in range(W) if L.rock[y][x] == RAW]
        cells.sort(key=lambda p: noise(*p))
        mix, i = cfg["mix"][b], 0
        for k, rock in enumerate(ROCKS):
            n = len(cells) - i if k == len(ROCKS) - 1 else round(len(cells) * mix.get(rock, 0))
            for x, y in cells[i:i + n]:
                L.rock[y][x] = rock
            i += n
        for x, y in cells[i:]:
            L.rock[y][x] = [r for r in ROCKS if mix.get(r)][-1]

    def free_rock(x, y):
        return L.at(x, y) in ROCKS and (x, y) not in L.reserved and (x, y) not in L.ore and (x, y) not in L.feat

    # 8. 빈터와 샘
    pockets = []
    for _ in range(cfg["pockets"]):
        cells = [(x, y) for y in range(4, H) for x in range(W) if free_rock(x, y)]
        cx, cy = rng.choice(cells)
        rad = rng.choice((1.0, 1.4, 2.0))
        for y in range(cy - 2, cy + 3):
            for x in range(cx - 2, cx + 3):
                if math.dist((x, y), (cx, cy)) <= rad and free_rock(x, y):
                    L.rock[y][x] = OPEN
        pockets.append((cx, cy))
    for cx, cy in pockets[:cfg["springs"]]:
        L.feat[(cx, cy)] = "샘"

    # 9. 옛 길: 구간을 가로지르는 지름길. 바위산도 뚫고 지나간다
    x, y = rng.randint(4, W - 5), rng.randint(8, 14)
    for _ in range(rng.randint(*S["old_road"]["length"])):
        p = (x, y)
        if L.at(*p) == SEA or p in L.reserved or p in L.low or y >= H - 1:
            break
        L.rock[y][x] = OPEN
        L.feat.setdefault(p, "옛 길")
        if rng.random() < S["old_road"]["turn"]:
            x = max(2, min(W - 3, x + rng.choice((-1, 1))))
        else:
            y += 1

    # 10. 광맥: 덩어리로. 총량은 밸런스 시트의 매장량과 정확히 같다
    for kind in ("star", "iron", "rune"):
        spec, rule = cfg[kind], CFG["veins"][kind]
        if not spec["total"]:
            continue
        n_all = max(1, round(spec["total"] / spec["per_tile"]))
        placed = []
        if kind == "star" and cfg["tidal_share"]:
            beds = [p for p in sorted(L.low) if p not in L.ore]
            for p in rng.sample(beds, min(len(beds), round(n_all * cfg["tidal_share"]))):
                L.ore[p] = [kind, spec["per_tile"], True]
                placed.append(p)
        rest = n_all - len(placed)
        per_band = [round(rest * s) for s in spec["split"]]
        last = max(i for i, s in enumerate(spec["split"]) if s)
        per_band[last] += rest - sum(per_band)
        for b, n in enumerate(per_band):
            done = 0
            while done < n:
                cells = [(x, y) for y in range(b * BAND, (b + 1) * BAND) for x in range(W)
                         if free_rock(x, y) and rule["host_weight"][L.rock[y][x]] > 0]
                if not cells:
                    cells = [(x, y) for y in range(H) for x in range(W) if free_rock(x, y)]
                p = rng.choices(cells, [rule["host_weight"][L.rock[y][x]] or 1 for x, y in cells])[0]
                size, cluster = min(n - done, rng.randint(*rule["cluster"])), [p]
                L.ore[p] = [kind, spec["per_tile"], False]
                while len(cluster) < size:
                    near = [(x + dx, y + dy) for x, y in cluster for dx, dy in N4
                            if free_rock(x + dx, y + dy) and band_of(y + dy) == b]
                    if not near:
                        break
                    q = rng.choice(near)
                    L.ore[q] = [kind, spec["per_tile"], False]
                    cluster.append(q)
                placed += cluster
                done += len(cluster)
        diff = spec["total"] - sum(L.ore[p][1] for p in placed)
        for p in rng.sample(placed, abs(diff)) if abs(diff) <= len(placed) else []:
            L.ore[p][1] += 1 if diff > 0 else -1

    # 11. 위험: 걸린 바위, 불안정한 결정, 침식의 씨앗
    edges = [(x, y, x - rx, y - ry) for y in range(4, H) for x in range(W) if free_rock(x, y)
             for rx, ry in ((x + dx, y + dy) for dx, dy in N4) if L.at(rx, ry) == RIDGE]
    rng.shuffle(edges)
    taken = set()
    for x, y, dx, dy in edges:
        if len(L.boulders) >= cfg["boulders"]:
            break
        if (x, y) not in taken:
            L.boulders.append((x, y, dx, dy))
            taken.add((x, y))
    deep = [(x, y) for y in range(BAND, H) for x in range(W) if free_rock(x, y) and (x, y) not in taken]
    while len(L.crystals) < cfg["crystals"] and deep:
        x, y = rng.choice(deep)
        L.crystals.add((x, y))
        near = [(x + dx, y + dy) for dx, dy in N4 if (x + dx, y + dy) in deep and (x + dx, y + dy) not in L.crystals]
        if near and len(L.crystals) < cfg["crystals"]:
            L.crystals.add(rng.choice(near))
    far = [(x, y) for y in range(2 * BAND, H) for x in range(W) if free_rock(x, y) and (x, y) not in L.crystals]
    L.corrupt = set(rng.sample(far, min(len(far), cfg["corruption"])))
    return L


def generate_valid(cfg, seed):
    """만들고, 검증하고, 실패하면 다시 만든다. (땅, 다시 만든 횟수)를 돌려준다."""
    for attempt in range(CFG["retry_limit"]):
        L = generate(cfg, seed, attempt)
        if not validate(L):
            return L, attempt
    return L, CFG["retry_limit"]


def flood(L, start):
    seen, stack = {start}, [start]
    while stack:
        x, y = stack.pop()
        for dx, dy in N4:
            q = (x + dx, y + dy)
            if q not in seen and L.passable(*q):
                seen.add(q)
                stack.append(q)
    return seen


# --------------------------------------------------------------------------
# 검증
# --------------------------------------------------------------------------
def validate(L):
    cfg, errors = L.cfg, []
    seen = flood(L, L.entrance)
    lost = sum(1 for y in range(H) for x in range(W) if L.passable(x, y) and (x, y) not in seen)
    if lost:
        errors.append(f"닿을 수 없는 타일 {lost}개")
    for kind in ("star", "iron", "rune"):
        total = sum(a for k, a, _ in L.ore.values() if k == kind)
        if total != cfg[kind]["total"]:
            errors.append(f"{kind} 매장량 {total} / {cfg[kind]['total']}")
    for p in L.ore:
        if p not in seen:
            errors.append(f"닿을 수 없는 광맥 {p}")
    feats = list(L.feat.values())
    if feats.count("낙성지") != 1 or L.crater is None:
        errors.append("낙성지가 없다")
    elif band_of(L.crater[1]) != CFG["grid"]["bands"] - 1:
        errors.append("낙성지가 가장 먼 구간에 있지 않다")
    if feats.count("봉인실") != sum(cfg["vaults"]):
        errors.append(f"봉인실 {feats.count('봉인실')} / {sum(cfg['vaults'])}")
    if feats.count("석관") != 1:
        errors.append("석관이 없다")
    for r0 in cfg["channels"]:
        if not any(v == "둑" and r0 - 2 <= p[1] <= r0 + 3 for p, v in L.feat.items()):
            errors.append(f"갯골 {r0}에 둑이 없다")
    return errors


# --------------------------------------------------------------------------
# 재기: 파는 시간, 캐는 속도
# --------------------------------------------------------------------------
def host_hardness(L, p):
    r = L.at(*p)
    return HARD.get(r, 0) if p not in L.crystals else HARD["연암"]


def step_cost(L, p, power, dug):
    walk = CFG["dig"]["walk_per_tile"]
    if p in dug or L.at(*p) == OPEN:
        return walk
    return walk + math.ceil(host_hardness(L, p) / power) * CFG["dig"]["swing"]


def dijkstra(L, start, power, dug, goal):
    dist, prev, pq = {start: 0.0}, {}, [(0.0, start)]
    while pq:
        d, p = heapq.heappop(pq)
        if d > dist[p]:
            continue
        if p != start and goal(p):
            path = [p]
            while path[-1] in prev:
                path.append(prev[path[-1]])
            return d, path[::-1][1:]
        for dx, dy in N4:
            q = (p[0] + dx, p[1] + dy)
            if not L.passable(*q):
                continue
            nd = d + step_cost(L, q, power, dug)
            if nd < dist.get(q, 1e9):
                dist[q], prev[q] = nd, p
                heapq.heappush(pq, (nd, q))
    return None, []


def light_tiles(L, y, bonus_m=0.0):
    lt = CFG["light"]
    return (lt["base_m"] - lt["fog_loss_m"] * L.cfg["fog"][band_of(y)] + bonus_m) / CFG["grid"]["tile_m"]


def miner(L, power, fog):
    """가장 싼 광맥부터 캐 나가는 광부. fog=True면 빛의 반경 안에서 본 것만 안다."""
    targets = dict(L.ore)
    dug, pos, t = set(), L.entrance, 0.0
    got = {"star": 0, "iron": 0, "rune": 0}
    curve, seen = [], set()

    def reveal(p):
        r = light_tiles(L, p[1])
        ri = int(r) + 1
        for y in range(p[1] - ri, p[1] + ri + 1):
            for x in range(p[0] - ri, p[0] + ri + 1):
                if 0 <= x < W and 0 <= y < H and (x - p[0]) ** 2 + (y - p[1]) ** 2 <= r * r:
                    seen.add((x, y))

    reveal(pos)
    while targets:
        if fog:
            known = lambda p: p in targets and p in seen
            d, path = dijkstra(L, pos, power, dug, known)
            if d is None:       # 아는 광맥이 없다. 안개의 가장자리로 간다
                edge = lambda p: p in seen and any(
                    (p[0] + dx, p[1] + dy) not in seen and L.passable(p[0] + dx, p[1] + dy) for dx, dy in N4)
                d, path = dijkstra(L, pos, power, dug, edge)
                if d is None:
                    break
        else:
            d, path = dijkstra(L, pos, power, dug, lambda p: p in targets)
            if d is None:
                break
        for p in path:
            was_rock = p not in dug and L.at(*p) != OPEN
            t += step_cost(L, p, power, dug)
            hit = [p]
            if was_rock:        # 검의 채굴 호: 양옆 타일도 같은 타격을 받는다. 더 단단하면 남는다
                dx, dy = p[0] - pos[0], p[1] - pos[1]
                for q in ((p[0] + dy, p[1] + dx), (p[0] - dy, p[1] - dx)):
                    if q not in dug and L.at(*q) in ROCKS and host_hardness(L, q) <= host_hardness(L, p):
                        hit.append(q)
            for q in hit:
                dug.add(q)
                if q in targets:
                    kind, amount, _ = targets.pop(q)
                    got[kind] += amount
                    curve.append((t, dict(got)))
            if fog:
                reveal(p)
            pos = p
    return curve, len(dug)


def time_to(curve, kind, amount):
    for t, got in curve:
        if got[kind] >= amount:
            return t
    return None


def measure(L, with_miners):
    cfg = L.cfg
    power = CFG["dig"]["power_by_nodes"][cfg["dig_nodes"]]
    weak = CFG["dig"]["power_by_nodes"][cfg["dig_nodes"] - 1]
    land = [(x, y) for y in range(H) for x in range(W) if L.rock[y][x] != SEA]
    m = {"land": len(land), "passes": L.passes,
         "ridge": sum(1 for x, y in land if L.rock[y][x] == RIDGE) / len(land),
         "open": sum(1 for x, y in land if L.rock[y][x] == OPEN) / len(land)}
    for b in range(CFG["grid"]["bands"]):
        rocks = [L.rock[y][x] for x, y in land if band_of(y) == b and L.rock[y][x] in ROCKS and (x, y) not in L.reserved]
        for r in ROCKS:
            m[f"mix{b}{r}"] = rocks.count(r) / max(1, len(rocks))
    for kind in ("star", "iron", "rune"):
        m[f"{kind}_tiles"] = sum(1 for k, _, _ in L.ore.values() if k == kind)
    crater = L.crater
    m["tunnel"] = dijkstra(L, L.entrance, power, set(), lambda p: p == crater)[0]
    m["tunnel_weak"] = dijkstra(L, L.entrance, weak, set(), lambda p: p == crater)[0]
    if with_miners:
        for name, fog in (("all", False), ("fog", True)):
            curve, dug = miner(L, power, fog)
            total = cfg["star"]["total"]
            t90 = time_to(curve, "star", math.ceil(total * 0.9))
            m[f"{name}_t90"] = t90
            m[f"{name}_rate"] = total * 0.9 / t90 if t90 else 0
            m[f"{name}_found"] = (curve[-1][1]["star"] / total) if curve else 0
            m[f"{name}_dug"] = dug
            ti = time_to(curve, "iron", math.ceil(cfg["iron"]["total"] * 0.9))
            m[f"{name}_iron_rate"] = cfg["iron"]["total"] * 0.9 / ti if ti else 0
    return m


# --------------------------------------------------------------------------
# 표
# --------------------------------------------------------------------------
def table(head, rows):
    out = ["| " + " | ".join(head) + " |", "|" + "---|" * len(head)]
    out += ["| " + " | ".join(str(c) for c in r) + " |" for r in rows]
    return "\n".join(out)


def balance_rates():
    spec = importlib.util.spec_from_file_location("balance_sim", ROOT / "balance" / "balance_sim.py")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod.RATE, mod.DEPOSIT


def stats(n_seeds=200, n_miners=16):
    rate, deposit = balance_rates()
    B, failures, retries = {}, [], []
    rows_mix, rows_shape, rows_dig, rows_rate, rows_cfg = [], [], [], [], []
    for i, cfg in enumerate(CFG["lands"]):
        for kind in ("star", "iron", "rune"):
            if cfg[kind]["total"] != deposit[kind][i]:
                failures.append(f"{cfg['name']}: {kind} 매장량이 밸런스 시트와 다르다")
        ms = []
        for seed in range(1, n_seeds + 1):
            L, tries = generate_valid(cfg, seed)
            retries.append(tries)
            if tries >= CFG["retry_limit"]:
                failures.append(f"{cfg['name']} 시드 {seed}: {validate(L)}")
                continue
            ms.append(measure(L, seed <= n_miners))
        avg = lambda k: statistics.mean(m[k] for m in ms if m.get(k) is not None)
        power = CFG["dig"]["power_by_nodes"][cfg["dig_nodes"]]
        for b in range(3):
            rows_mix.append([cfg["name"] if b == 0 else "", f"구간 {b + 1}"] +
                            [f"{cfg['mix'][b].get(r, 0):.0%} → {avg(f'mix{b}{r}'):.0%}" if cfg["mix"][b].get(r) else "—"
                             for r in ROCKS])
        rows_shape.append([cfg["name"], f"{avg('land'):.0f}", f"{avg('ridge'):.0%}", f"{avg('open'):.0%}",
                           f"{avg('star_tiles'):.0f}", f"{avg('iron_tiles'):.0f}", f"{avg('rune_tiles'):.0f}",
                           f"{avg('passes'):.1f}"])
        tun = sorted(m["tunnel"] for m in ms)
        rows_dig.append([cfg["name"], power, f"{tun[len(tun) // 2]:.0f}초", f"{tun[0]:.0f}~{tun[-1]:.0f}초",
                         f"{avg('tunnel_weak'):.0f}초"])
        mm = [m for m in ms if "all_rate" in m]
        a = lambda k: statistics.mean(m[k] for m in mm)
        hf = CFG["human_factor"]
        rows_rate.append([cfg["name"], f"{a('all_rate'):.3f}", f"{a('fog_rate'):.3f}",
                          f"{1 - a('fog_rate') / a('all_rate'):.0%}",
                          f"{a('fog_rate') * hf:.3f}", f"{rate['star'][i]:.3f}",
                          f"{a('fog_rate') * hf / rate['star'][i]:.2f}배",
                          f"{a('fog_iron_rate') * hf:.3f}", f"{rate['iron'][i]:.3f}",
                          f"{a('fog_iron_rate') * hf / rate['iron'][i]:.2f}배"])
        rows_cfg.append([cfg["name"], f"{cfg['star']['total']} ({cfg['star']['per_tile']}개씩)",
                         f"{cfg['iron']['total']} ({cfg['iron']['per_tile']}개씩)", cfg["rune"]["total"] or "—",
                         sum(cfg["vaults"]), cfg["springs"], cfg["boulders"] or "—", cfg["crystals"] or "—",
                         cfg["corruption"] or "—", len(cfg["channels"]) or "—"])
    B["config"] = table(["땅", "성광석", "흑철", "룬 수정", "봉인실", "샘", "걸린 바위", "불안정한 결정", "침식의 씨앗", "갯골"], rows_cfg)
    B["mix"] = table(["땅", "구간"] + [f"{r} (목표 → 실제)" for r in ROCKS], rows_mix)
    B["shape"] = table(["땅", "땅 타일", "바위산", "빈터", "성광석 광맥", "흑철 광맥", "룬 수정", "뚫은 고개"], rows_shape)
    B["tunnel"] = table(["땅", "기준 채굴 위력", "입구에서 낙성지까지 (중앙값)", "범위", "위력이 한 단계 모자라면"], rows_dig)
    B["rate"] = table(["땅", "전부 아는 광부", "안개 속의 광부", "안개로 잃는 효율",
                       f"사람 보정 (×{CFG['human_factor']})", "밸런스 시트의 가정", "가정 대비",
                       "흑철: 사람 보정", "흑철: 가정", "흑철: 가정 대비"], rows_rate)
    again = sum(1 for r in retries if r)
    B["verify"] = (f"땅마다 시드 {n_seeds}개, 모두 {len(retries)}장을 만들어 검증했다. "
                   + (f"**실패 {len(failures)}건.**" if failures else
                      "**모두 첫 생성에서 통과했다.**" if not again else
                      f"**모두 통과했다.** 그중 {again}장({again / len(retries):.1%})은 첫 생성이 검증에 걸려 다시 만들었고, "
                      f"가장 많이 다시 만든 것이 {max(retries)}번이다.")
                   + f" 캐는 속도는 땅마다 시드 {n_miners}개로 쟀다.")
    return B, failures


# --------------------------------------------------------------------------
# 그림
# --------------------------------------------------------------------------
COLORS = {SEA: "#5d8fb0", RIDGE: "#26262b", OPEN: "#e9e1cf", "흙": "#c8b596", "연암": "#9c9080",
          "경암": "#6c6661", "흑암": "#3d3a44", RAW: "#ff00ff"}
ORE = {"star": "#f6c443", "iron": "#3f6aa8", "rune": "#b262d8"}


def svg_land(L, ts, ox, oy):
    out = []
    Y = lambda y: oy + (H - 1 - y) * ts      # 성이 아래, 바다가 위
    for y in range(H):
        for x in range(W):
            p, c = (x, y), COLORS[L.rock[y][x]]
            if p in L.low:
                c = "#8fc9c2"
            if L.feat.get(p) == "둑":
                c = "#b99c66"
            if L.feat.get(p) == "옛 길":
                c = "#f3e9b5"
            out.append(f'<rect x="{ox + x * ts}" y="{Y(y)}" width="{ts}" height="{ts}" fill="{c}"/>')
    h = ts / 2
    for (x, y), (kind, _, tidal) in L.ore.items():
        cx, cy = ox + x * ts + h, Y(y) + h
        if kind == "rune":
            out.append(f'<rect x="{cx - ts * .25}" y="{cy - ts * .25}" width="{ts * .5}" height="{ts * .5}" fill="{ORE[kind]}" '
                       f'transform="rotate(45 {cx} {cy})"/>')
        else:
            out.append(f'<circle cx="{cx}" cy="{cy}" r="{ts * .3}" fill="{ORE[kind]}"'
                       + (' stroke="#fff" stroke-width="1"' if tidal else "") + "/>")
    for x, y in L.crystals:
        cx, cy = ox + x * ts + h, Y(y) + h
        out.append(f'<polygon points="{cx},{cy - ts * .4} {cx + ts * .35},{cy + ts * .3} {cx - ts * .35},{cy + ts * .3}" fill="#58e0e6"/>')
    for x, y in L.corrupt:
        out.append(f'<circle cx="{ox + x * ts + h}" cy="{Y(y) + h}" r="{ts * .35}" fill="#d0338f"/>')
    for x, y, dx, dy in L.boulders:
        cx, cy = ox + x * ts + h, Y(y) + h
        out.append(f'<circle cx="{cx}" cy="{cy}" r="{ts * .33}" fill="#111" stroke="#e8e8e8" stroke-width="1"/>'
                   f'<line x1="{cx}" y1="{cy}" x2="{cx + dx * ts * .8}" y2="{cy - dy * ts * .8}" stroke="#e8e8e8" stroke-width="1.2"/>')
    marks = {"봉인실": ("#ffffff", "▣"), "낙성지": ("#ff5a36", "◆"), "석관": ("#ffffff", "✚"), "샘": ("#2b7fd9", "●"), "입구": ("#1c8a3b", "▲")}
    for (x, y), f in L.feat.items():
        if f in marks:
            col, ch = marks[f]
            out.append(f'<text x="{ox + x * ts + h}" y="{Y(y) + h + ts * .38}" font-size="{ts * 1.25}" text-anchor="middle" '
                       f'fill="{col}" stroke="#000" stroke-width="0.4">{ch}</text>')
    for b in (1, 2):
        yy = Y(b * BAND) + ts
        out.append(f'<line x1="{ox}" y1="{yy}" x2="{ox + W * ts}" y2="{yy}" stroke="#fff" stroke-width="1" stroke-dasharray="4 3" opacity="0.7"/>')
    out.append(f'<rect x="{ox}" y="{oy}" width="{W * ts}" height="{H * ts}" fill="none" stroke="#222" stroke-width="1"/>')
    return "\n".join(out)


LEGEND = [("#c8b596", "흙·자갈"), ("#9c9080", "연암"), ("#6c6661", "경암"), ("#3d3a44", "흑암"), ("#26262b", "바위산 (파괴 불가)"),
          ("#e9e1cf", "빈터"), ("#f3e9b5", "옛 길"), ("#8fc9c2", "갯골 (밀물에 잠긴다)"), ("#b99c66", "둑"), ("#5d8fb0", "바다"),
          ("#f6c443", "성광석 광맥"), ("#3f6aa8", "흑철 광맥"), ("#b262d8", "룬 수정"), ("#58e0e6", "불안정한 결정"),
          ("#d0338f", "침식의 씨앗"), ("#111111", "걸린 바위")]


def render(seed, lands, path, ts=10):
    gap, top, n = 26, 44, len(lands)
    width = n * W * ts + (n + 1) * gap
    cols = 4 if n > 1 else 2
    lh = math.ceil((len(LEGEND) + 1) / cols) * 20 + 16
    height = top + H * ts + 34 + lh
    S = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" '
         f'font-family="\'Noto Sans KR\', \'Malgun Gothic\', \'Apple SD Gothic Neo\', sans-serif">',
         f'<rect width="{width}" height="{height}" fill="#f7f5f0"/>']
    for i, L in enumerate(lands):
        ox = gap + i * (W * ts + gap)
        S.append(f'<text x="{ox}" y="{top - 22}" font-size="15" font-weight="700" fill="#222">{L.cfg["id"]}. {L.cfg["name"]}</text>')
        S.append(f'<text x="{ox}" y="{top - 6}" font-size="11" fill="#555">위: 바다 쪽 · 아래: 성 쪽 · 시드 {seed}</text>')
        S.append(svg_land(L, ts, ox, top))
        S.append(f'<text x="{ox + W * ts / 2}" y="{top + H * ts + 16}" font-size="11" text-anchor="middle" fill="#555">▲ 입구 (뒷문 쪽)</text>')
    x0, y0 = gap, top + H * ts + 40
    cw = (width - 2 * gap) / cols
    items = LEGEND + [("mark", "▣ 봉인실  ◆ 낙성지  ✚ 석관  ● 샘")]
    for k, (c, label) in enumerate(items):
        x, y = x0 + (k % cols) * cw, y0 + (k // cols) * 20
        if c != "mark":
            S.append(f'<rect x="{x}" y="{y}" width="14" height="14" fill="{c}" stroke="#444" stroke-width="0.5"/>')
            S.append(f'<text x="{x + 20}" y="{y + 12}" font-size="12" fill="#222">{label}</text>')
        else:
            S.append(f'<text x="{x}" y="{y + 12}" font-size="12" fill="#222">{label}</text>')
    S.append("</svg>")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(S), encoding="utf-8")


def ascii_map(L):
    sym = {SEA: "~", RIDGE: "#", OPEN: " ", "흙": ".", "연암": ":", "경암": "x", "흑암": "X"}
    feat = {"봉인실": "V", "낙성지": "@", "석관": "+", "샘": "o", "입구": "^", "둑": "=", "옛 길": "|"}
    for y in range(H - 1, -1, -1):
        row = ""
        for x in range(W):
            p = (x, y)
            row += (feat[L.feat[p]] if p in L.feat else {"star": "*", "iron": "i", "rune": "r"}[L.ore[p][0]] if p in L.ore
                    else "c" if p in L.crystals else "_" if p in L.low else sym[L.rock[y][x]])
        print(f"{y:2d} {row}")


def main():
    cmd = sys.argv[1] if len(sys.argv) > 1 else "stats"
    if cmd == "ascii":
        L = generate_valid(CFG["lands"][int(sys.argv[2]) - 1], int(sys.argv[3]))[0]
        ascii_map(L)
        print(validate(L) or "검증 통과", measure(L, True))
    elif cmd == "render":
        seed = int(sys.argv[2])
        lands = [generate_valid(c, seed)[0] for c in CFG["lands"]]
        render(seed, lands, ROOT / "docs" / "img" / f"cape_seed{seed}.svg")
        render(seed, lands[:1], ROOT / "docs" / "img" / f"land1_seed{seed}.svg", ts=16)
        print("그림 생성")
    else:
        quick = "--quick" in sys.argv
        B, failures = stats(20 if quick else 200, 4 if quick else 16)
        for f in failures[:20]:
            print("실패:", f)
        if DOC.exists() and not quick:
            doc = DOC.read_text(encoding="utf-8")
            for name, text in B.items():
                pat = re.compile(rf"(<!-- AUTO:{name} -->\n).*?(<!-- /AUTO:{name} -->)", re.S)
                if not pat.search(doc):
                    print(f"문서에 AUTO:{name} 블록이 없다")
                    sys.exit(1)
                doc = pat.sub(lambda m: m.group(1) + text + "\n" + m.group(2), doc)
            DOC.write_text(doc, encoding="utf-8")
            print(f"{DOC.relative_to(ROOT)}의 표 {len(B)}개 갱신")
        else:
            for name, text in B.items():
                print(f"\n### {name}\n{text}")
        if failures:
            sys.exit(1)


if __name__ == "__main__":
    main()
