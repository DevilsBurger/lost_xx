#!/usr/bin/env python3
"""LOST REGALIA 경제 시뮬레이션.

곶의 매장량(공급)과 강화 비용(수요)이 맞물리는지, 표준 진행에서 장마다
무엇을 얼마나 살 수 있는지 계산한다. 모든 값은 프로토타입 이전의 가설이다.

실행:  python3 balance/balance_sim.py
출력:  balance/out/*.csv, balance/out/tables.md
"""
from __future__ import annotations

import csv
import pathlib

OUT = pathlib.Path(__file__).parent / "out"

# --------------------------------------------------------------------------
# 1. 시간
# --------------------------------------------------------------------------
CHAPTERS = 5
WAVES = 10                                  # 장당 정규 습격 수 (총공세 제외)
CALM = [100, 100, 100, 85, 85]              # 고요 길이(초). 4장부터 균열 때문에 짧아진다
ASSAULT = [50, 55, 60, 65, 70]              # 습격 평균 길이(초)
FINAL_ASSAULT = [240, 270, 300, 330, 360]   # 총공세 + 보스(초)
MENU = 45                                   # 주기당 왕좌 메뉴 시간(초, 게임은 멈춘다)
BUFFER = 10                                 # 귀환 후 성벽에 자리 잡는 여유(초)

# --------------------------------------------------------------------------
# 2. 이동 (기본 속도 6 m/s 기준의 편도 시간, 초)
# --------------------------------------------------------------------------
GATE_TIME = 5.0                             # 성벽 -> 뒷문 -> 첫 땅 어귀
ROAD_TIME = 4.0                             # 지나온 땅 하나 (닦인 길, 3배속)
INLAND = [2, 2, 2, 6, 6, 6, 6, 10, 10, 10]       # 주기별로 현재 땅 안에서 나아간 거리
LEG_SPEED = 0.10                            # 다리 강화 1단계당 이동 속도
RAIL_COVER = {0: 0, 1: 2, 2: 3, 3: 4}       # 광차 선로 단계 -> 선로가 닿는 '지나온 땅' 수

# --------------------------------------------------------------------------
# 3. 매장량과 채집
# --------------------------------------------------------------------------
DEPOSIT = {
    "star": [80, 110, 140, 170, 200],       # 성광석
    "iron": [30, 50, 70, 80, 90],           # 흑철
    "rune": [0, 3, 4, 5, 6],                # 룬 수정
}
RATE = {                                    # 채굴 1초당 채집량 (그 장의 기준 채굴 위력일 때)
    "star": [0.110, 0.158, 0.186, 0.300, 0.370],
    "iron": [0.060, 0.092, 0.112, 0.168, 0.200],
    "rune": [0.000, 0.010, 0.012, 0.019, 0.022],
}
PAR_DIG = [1, 2, 2, 3, 3]                   # 장별 기준 채굴 위력(줄기 노드 수)
CARRY_BASE, CARRY_PER_BACK = 12, 6          # 운반 한도(조각). 룬 수정은 2조각으로 센다
EYE_YIELD = 0.05                            # 눈 강화 1단계당 채집 효율

# --------------------------------------------------------------------------
# 4. 비용
# --------------------------------------------------------------------------
NODE_COST = {1: 8, 2: 14, 3: 22, 4: 34, 5: 50}
KEYSTONE_COST, KEYSTONE_RUNE = 32, 2        # 진명 (3단계)
ULT_COST = 70                               # 궁극 (5단계)
# 형태 하나의 트리: (이름, 단계, 성광석, 룬 수정)
TREE = (
    [("줄기", 1, 8, 0)] * 2 + [("기술A", 1, 8, 0)] * 3 + [("수동", 1, 8, 0)] * 3
    + [("줄기", 2, 14, 0)] + [("변형A", 2, 14, 0)] * 3 + [("수동", 2, 14, 0)] * 3
    + [("줄기", 3, 22, 0)] + [("진명", 3, KEYSTONE_COST, KEYSTONE_RUNE)] * 3
    + [("줄기", 4, 34, 0)] + [("기술B", 4, 34, 0)] * 3 + [("변형B", 4, 34, 0)] * 3
    + [("수동", 5, 50, 0)] * 3 + [("궁극", 5, ULT_COST, 0)]
)
KING_COST = [12, 22, 36, 54]                # 왕 강화 1~4단계 (축 4개)
KING_CAP = [2, 3, 4, 4, 4]                  # 장별 단계 상한
FACILITY_COST = [10, 18, 30]                # 시설 건설 / 2단계 / 3단계
FACILITY_CAP = [1, 2, 3, 3, 3]
SLOTS = [3, 4, 5, 6, 6]
WALL_UP_COST = [12, 20, 30, 44]             # 성벽 강화 1~4단계
SOCKET_RUNE, RUNE_UP_RUNE = 2, 2            # 둘째 소켓 개방 / 룬 강화 (룬 수정)
REPAIR_PER_CYCLE = [0.6, 0.6, 0.8, 1.4, 2.0]   # 표준 진행의 주기당 수리 흑철

# --------------------------------------------------------------------------
# 5. 위협 예산
# --------------------------------------------------------------------------
THREAT_START = 10.0                         # 1장 첫 습격
THREAT_GROWTH = 0.10                        # 장 안에서 습격마다
THREAT_DIP = 0.80                           # 장이 바뀔 때 직전 습격 대비
FINAL_MULT = 1.5                            # 총공세 = 마지막 정규 습격 x 1.5 + 보스

# --------------------------------------------------------------------------
# 6. 구매 순서 (장별). (자원, 종류, 이름)
#    종류: node=각성 노드, king=왕 강화, fac=시설, wall=성벽 강화,
#          socket=둘째 소켓, runeup=룬 강화
# --------------------------------------------------------------------------
def n(name, tier):
    return ("node", name, tier)


STANDARD = {
    "star": [
        [n("줄기", 1), n("기술A", 1), ("king", "다리"), ("king", "등"), n("수동", 1), n("줄기", 1), n("기술A", 1)],
        [n("줄기", 2), ("king", "등"), n("변형A", 2), n("수동", 1), ("king", "다리"), n("변형A", 2)],
        [n("수동", 2), n("줄기", 3), n("진명", 3), ("king", "눈"), ("king", "심장"), ("king", "다리")],
        [n("줄기", 4), n("기술B", 4), n("변형B", 4), ("king", "등")],
        [n("수동", 5), n("궁극", 5), ("king", "심장"), ("king", "눈")],
    ],
    "iron": [
        [("fac", "발리스타"), ("fac", "망루")],
        [("fac", "가시 난간"), ("fac", "성문 방책"), ("fac", "발리스타")],
        [("fac", "광차 선로"), ("fac", "가시 난간"), ("fac", "망루"), ("wall", "성벽 강화")],
        [("fac", "광차 선로"), ("fac", "발리스타"), ("fac", "기름 솥")],
        [("wall", "성벽 강화"), ("fac", "성문 방책"), ("fac", "기름 솥")],
    ],
    "rune": [
        [], [("socket", "둘째 소켓")], [("socket", "둘째 소켓")],
        [("runeup", "룬 강화"), ("socket", "둘째 소켓")], [("runeup", "룬 강화"), ("runeup", "룬 강화")],
    ],
}

# 왕 강화와 채굴 위력을 먼저 올리는 채굴 중심 진행
KING_FIRST = {
    **STANDARD,
    "star": [
        [n("줄기", 1), ("king", "다리"), ("king", "등"), n("기술A", 1), ("king", "눈"), ("king", "다리")],
        [n("줄기", 2), ("king", "등"), ("king", "눈"), ("king", "다리"), n("수동", 1), n("변형A", 2)],
        [("king", "등"), ("king", "눈"), n("줄기", 3), n("진명", 3), n("기술A", 1), n("수동", 2)],
        [n("줄기", 4), ("king", "다리"), ("king", "등"), n("기술B", 4), ("king", "눈")],
        [n("변형B", 4), n("수동", 5), n("궁극", 5), ("king", "심장"), ("king", "심장")],
    ],
}

# 왕 강화를 거의 하지 않고 무기에 몰아 쓰는 진행
WEAPON_FIRST = {
    **STANDARD,
    "star": [
        [n("줄기", 1), n("기술A", 1), n("수동", 1), n("줄기", 1), n("기술A", 1), n("수동", 1), n("기술A", 1)],
        [n("줄기", 2), n("변형A", 2), n("변형A", 2), n("수동", 2), n("수동", 1), n("수동", 2)],
        [n("줄기", 3), n("진명", 3), n("변형A", 2), n("수동", 2), ("king", "다리")],
        [n("줄기", 4), n("기술B", 4), n("변형B", 4), n("기술B", 4), ("king", "등")],
        [n("수동", 5), n("궁극", 5), n("변형B", 4), n("수동", 5)],
    ],
}


# --------------------------------------------------------------------------
def simulate(plan):
    stock = {"star": 0.0, "iron": 0.0, "rune": 0.0}
    left = {k: list(v) for k, v in DEPOSIT.items()}
    king = {"다리": 0, "등": 0, "눈": 0, "심장": 0}
    fac, wall, nodes, dig = {}, 0, [], 0
    sockets = runeups = keystones = 0
    queue = {k: [] for k in stock}
    rows, chapters = [], []

    for c in range(CHAPTERS):
        mined = {k: 0.0 for k in stock}
        spent = {k: 0.0 for k in stock}
        for k in stock:
            queue[k] += plan[k][c]
        for i in range(WAVES):
            # --- 이동과 채굴 시간
            covered = min(c, RAIL_COVER[fac.get("광차 선로", 0)])
            road = ROAD_TIME * (c - covered) + ROAD_TIME * 0.5 * covered
            oneway = (GATE_TIME + road + INLAND[i]) / (1 + LEG_SPEED * king["다리"])
            mining = max(0.0, CALM[c] - 2 * oneway - BUFFER)
            # --- 채집
            gap = PAR_DIG[c] - dig
            digf = 1.0 + 0.15 * (-gap) if gap <= 0 else (0.7 if gap == 1 else 0.45)
            eff = digf * (1 + EYE_YIELD * king["눈"])
            want = {
                "star": mining * RATE["star"][c] * eff,
                "iron": mining * RATE["iron"][c] * eff * (1.0 if i >= 2 else 0.25),
                "rune": mining * RATE["rune"][c] * eff * (1.0 if i >= 4 else 0.0),
            }
            got = {k: min(want[k], left[k][c]) for k in want}
            cap = (CARRY_BASE + CARRY_PER_BACK * king["등"]) * (2 if fac.get("광차 선로") else 1)
            pieces = got["star"] + got["iron"] + 2 * got["rune"]
            if pieces > cap:
                got = {k: v * cap / pieces for k, v in got.items()}
            for k in got:
                left[k][c] -= got[k]
                stock[k] += got[k]
                mined[k] += got[k]
            # --- 수리
            repair = min(stock["iron"], REPAIR_PER_CYCLE[c])
            stock["iron"] -= repair
            spent["iron"] += repair
            # --- 구매 (순서대로, 살 수 있는 만큼)
            for res in stock:
                while queue[res]:
                    kind, name, *rest = queue[res][0]
                    rune_cost = 0
                    if kind == "node":
                        tier = rest[0]
                        cost = next(x for x in TREE if x[0] == name and x[1] == tier)
                        price, rune_cost = cost[2], cost[3]
                        allowed = tier <= c + 1
                    elif kind == "king":
                        price = KING_COST[king[name]]
                        allowed = king[name] < KING_CAP[c]
                    elif kind == "fac":
                        lv = fac.get(name, 0)
                        price = FACILITY_COST[lv]
                        allowed = lv < FACILITY_CAP[c] and (lv > 0 or len(fac) < SLOTS[c])
                    elif kind == "wall":
                        price, allowed = WALL_UP_COST[wall], True
                    else:
                        price, allowed = (SOCKET_RUNE if kind == "socket" else RUNE_UP_RUNE), True
                    if not allowed or stock[res] < price or stock["rune"] < rune_cost:
                        break
                    stock[res] -= price
                    spent[res] += price
                    stock["rune"] -= rune_cost
                    spent["rune"] += rune_cost
                    queue[res].pop(0)
                    if kind == "node":
                        nodes.append((name, tier))
                        dig += name == "줄기" and tier in (1, 2, 4) and (name, tier) not in nodes[:-1]
                        keystones += name == "진명"
                    elif kind == "king":
                        king[name] += 1
                    elif kind == "fac":
                        fac[name] = fac.get(name, 0) + 1
                    elif kind == "wall":
                        wall += 1
                    elif kind == "socket":
                        sockets += 1
                    else:
                        runeups += 1
            rows.append({
                "장": c + 1, "주기": i + 1, "편도(초)": round(oneway, 1), "채굴(초)": round(mining, 1),
                "성광석": round(got["star"], 1), "흑철": round(got["iron"], 1), "룬 수정": round(got["rune"], 2),
                "운반 한도": cap, "보유 성광석": round(stock["star"], 1), "보유 흑철": round(stock["iron"], 1),
            })
        chapters.append({
            "장": c + 1,
            "성광석 채집": round(mined["star"]), "성광석 회수율": f"{mined['star'] / DEPOSIT['star'][c]:.0%}",
            "흑철 채집": round(mined["iron"]), "룬 수정 채집": round(mined["rune"], 1),
            "깨운 노드(누적)": len(nodes), "왕 강화(누적 단계)": sum(king.values()),
            "시설(누적 단계)": sum(fac.values()), "성벽 강화": wall,
            "남은 성광석": round(stock["star"]), "남은 흑철": round(stock["iron"]),
        })
    total = {
        "성광석 채집": round(sum(DEPOSIT["star"]) - sum(left["star"])),
        "흑철 채집": round(sum(DEPOSIT["iron"]) - sum(left["iron"])),
        "룬 수정 채집": round(sum(DEPOSIT["rune"]) - sum(left["rune"]), 1),
        "노드": len(nodes), "왕 강화": sum(king.values()), "시설 단계": sum(fac.values()),
        "성벽 강화": wall, "둘째 소켓": sockets, "룬 강화": runeups, "진명": keystones,
        "못 산 항목": sum(len(q) for q in queue.values()),
    }
    return rows, chapters, total


def threat_table():
    rows, start = [], THREAT_START
    for c in range(CHAPTERS):
        waves = [start * (1 + THREAT_GROWTH * i) for i in range(WAVES)]
        rows.append({"장": c + 1, **{f"{i + 1}": round(w, 1) for i, w in enumerate(waves)},
                     "총공세": round(waves[-1] * FINAL_MULT, 1)})
        start = waves[-1] * THREAT_DIP
    return rows


def time_table():
    rows = []
    for c in range(CHAPTERS):
        play = WAVES * (CALM[c] + ASSAULT[c]) + FINAL_ASSAULT[c]
        rows.append({"장": c + 1, "고요(초)": CALM[c], "습격(초)": ASSAULT[c],
                     "전투·채굴(분)": round(play / 60, 1), "메뉴 포함(분)": round((play + WAVES * MENU) / 60, 1)})
    return rows


def demand_supply():
    tree_star = sum(x[2] for x in TREE)
    tree_rune = sum(x[3] for x in TREE)
    king_star = sum(KING_COST) * 4
    fac_iron = sum(FACILITY_COST) * SLOTS[-1]
    wall_iron = sum(WALL_UP_COST)
    rune_need = tree_rune + 6 * SOCKET_RUNE + 8 * RUNE_UP_RUNE
    return [
        {"자원": "성광석", "전부 사는 비용": tree_star + king_star, "내역": f"각성 {tree_star} + 왕 강화 {king_star}",
         "매장량": sum(DEPOSIT["star"]), "매장량/비용": f"{sum(DEPOSIT['star']) / (tree_star + king_star):.0%}"},
        {"자원": "흑철", "전부 사는 비용": fac_iron + wall_iron, "내역": f"시설 {fac_iron} + 성벽 강화 {wall_iron} (수리 별도)",
         "매장량": sum(DEPOSIT["iron"]), "매장량/비용": f"{sum(DEPOSIT['iron']) / (fac_iron + wall_iron):.0%}"},
        {"자원": "룬 수정", "전부 사는 비용": rune_need, "내역": f"진명 {tree_rune} + 둘째 소켓 {6 * SOCKET_RUNE} + 룬 강화 {8 * RUNE_UP_RUNE}",
         "매장량": sum(DEPOSIT["rune"]), "매장량/비용": f"{sum(DEPOSIT['rune']) / rune_need:.0%}"},
    ]


def md(rows):
    keys = list(rows[0])
    out = ["| " + " | ".join(keys) + " |", "|" + "---|" * len(keys)]
    out += ["| " + " | ".join(str(r[k]) for k in keys) + " |" for r in rows]
    return "\n".join(out)


def write_csv(name, rows):
    with open(OUT / name, "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0]))
        w.writeheader()
        w.writerows(rows)


def main():
    OUT.mkdir(exist_ok=True)
    parts = []
    parts += ["## 시간 예산", md(time_table())]
    tt = time_table()
    parts += [f"\n캠페인 합계(메뉴 포함): {sum(r['메뉴 포함(분)'] for r in tt):.0f}분"]
    parts += ["\n## 수요와 공급", md(demand_supply())]
    parts += [f"\n형태 하나의 노드 수: {len(TREE)}"]
    summary = []
    for label, slug, plan in (("표준", "standard", STANDARD), ("채굴 우선", "mining", KING_FIRST),
                              ("무기 우선", "weapon", WEAPON_FIRST)):
        rows, chapters, total = simulate(plan)
        write_csv(f"cycles_{slug}.csv", rows)
        parts += [f"\n## 진행: {label}", md(chapters)]
        summary.append({"진행": label, **total})
        if label == "표준":
            write_csv("chapters_standard.csv", chapters)
            parts += ["\n### 표준 진행의 주기별 이동·채굴 시간",
                      md([{k: r[k] for k in ("장", "주기", "편도(초)", "채굴(초)", "성광석", "흑철", "운반 한도")}
                          for r in rows if r["주기"] in (1, 5, 10)])]
    parts += ["\n## 진행 비교", md(summary)]
    parts += ["\n## 위협 예산", md(threat_table())]
    write_csv("threat.csv", threat_table())
    write_csv("time.csv", tt)
    write_csv("demand_supply.csv", demand_supply())
    text = "\n".join(parts) + "\n"
    (OUT / "tables.md").write_text(text, encoding="utf-8")
    print(text)


if __name__ == "__main__":
    main()
