#!/usr/bin/env python3
"""전투 데이터를 검증하고, 전투 규칙 문서의 계산 표를 채운다.

데이터(data/combat/*.json, data/waves/*.json)가 원본이다.
문서(docs/06_combat_rules.md)의 글은 손으로 쓰고, <!-- AUTO:이름 --> 블록만 이 스크립트가 채운다.

실행:  python3 tools/combat_doc.py          (문서의 표를 갱신)
       python3 tools/combat_doc.py --print  (표만 출력)
"""
from __future__ import annotations

import importlib.util
import json
import math
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
DOC = ROOT / "docs" / "06_combat_rules.md"


def load(path):
    return json.loads((ROOT / path).read_text(encoding="utf-8"))


def balance():
    spec = importlib.util.spec_from_file_location("balance_sim", ROOT / "balance" / "balance_sim.py")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def table(head, rows):
    out = ["| " + " | ".join(head) + " |", "|" + "---|" * len(head)]
    out += ["| " + " | ".join(str(c) for c in r) + " |" for r in rows]
    return "\n".join(out)


def num(x):
    return f"{x:g}"


# --------------------------------------------------------------------------
# 계산
# --------------------------------------------------------------------------
def ttk_basic(k, hp):
    """기본 3연타만으로 처치하는 데 걸리는 시간."""
    t, i = 0.0, 0
    while hp > 0:
        t += k["basic"]["times"][i % 3]
        hp -= k["basic"]["damage"][i % 3]
        i += 1
    return round(t, 2), i


def charged_hits(k, amount, per_hit):
    n = math.ceil(amount / per_hit)
    cyc = k["charged"]["charge"] + k["charged"]["recover"]
    return n, round(k["charged"]["charge"] + (n - 1) * cyc, 1)


def fall_damage(k, height):
    """낙하 피해는 높이의 제곱에 비례한다."""
    return round(k["fall"]["coef"] * height * height)


def validate(k, enemies, sig, waves):
    errors = []
    ids = [e["id"] for e in enemies]
    if len(set(ids)) != len(ids):
        errors.append("적 id 중복")
    by = {e["id"]: e for e in enemies}
    for e in enemies:
        if e["size"] not in k["fall"]["collide_knock"]:
            errors.append(f"{e['name']}: 크기 {e['size']}")
        if (e["climb"] is None) != (e["grip"] is None):
            errors.append(f"{e['name']}: 등반 속도와 붙잡기는 함께 있거나 함께 없어야 한다")
    # 밸런스 시트의 적 표와 일치하는가
    text = (ROOT / "docs" / "04_balance.md").read_text(encoding="utf-8")
    for e in enemies:
        m = re.search(rf"^\| {re.escape(e['name'])} \| ([\d.]+) \| (\d+)", text, re.M)
        if not m:
            errors.append(f"{e['name']}: 밸런스 시트 표에 없다")
        elif float(m.group(1)) != e["cost"] or int(m.group(2)) != e["hp"]:
            errors.append(f"{e['name']}: 밸런스 시트와 비용·체력이 다르다 ({m.group(1)}, {m.group(2)})")
    # 인장 5종, 반응은 모든 쌍
    names = [s["name"] for s in sig["sigils"]]
    pairs = {frozenset((r["a"], r["b"])) for r in sig["reactions"]}
    want = {frozenset((a, b)) for i, a in enumerate(names) for b in names[i + 1:]}
    if pairs != want:
        errors.append("반응이 모든 인장 쌍을 덮지 않는다")
    for s in sig["sigils"]:
        if len(s["levels"]) != 3:
            errors.append(f"{s['name']}: 단계는 셋이어야 한다")
    # 습격 예산
    threat = balance().threat_table()[waves["chapter"] - 1]
    for w in waves["waves"]:
        for u in w["units"]:
            if u not in by:
                errors.append(f"습격 {w['n']}: {u}가 없다")
        cost = sum(by[u]["cost"] * c for u, c in w["units"].items() if u in by)
        budget = threat["총공세"] if w["n"] == "총공세" else threat[str(w["n"])]
        if abs(cost - budget) > 1.0:
            errors.append(f"습격 {w['n']}: 편성 비용 {cost} / 예산 {budget}")
    return errors, by


def blocks(k, enemies, sig, waves, by):
    B = {}
    H = k["cliff"]["height"]
    climbers = [e for e in enemies if e["grip"] is not None]

    # 적 능력치
    B["enemies"] = table(
        ["세력", "적", "등장", "경로", "비용", "체력", "등반 속도", "바닥에서 성벽까지", "붙잡기", "성벽 피해", "왕 피해"],
        [[e["faction"], f"**{e['name']}**", "프롤로그" if e["chapter"] == 0 else f"{e['chapter']}장", e["route"],
          num(e["cost"]), e["hp"],
          f"{e['climb']} m/s" if e["climb"] else "—",
          f"{H / e['climb']:.0f}초" if e["climb"] and e["route"] == "절벽면" else
          (f"{k['cliff']['ledge_height'] / e['climb']:.0f}초 (바위턱)" if e["climb"] else "—"),
          e["grip"] if e["grip"] is not None else "—",
          f"초당 {e['wall_dps']}" if e["wall_dps"] else "—",
          e["king_damage"] or "—"] for e in enemies])

    # 처치 시간
    rows = []
    for e in enemies:
        t, hits = ttk_basic(k, e["hp"])
        n, ct = charged_hits(k, e["hp"], k["charged"]["damage"])
        rows.append([f"**{e['name']}**", e["hp"], f"{hits}타, {t}초", f"{n}번, {ct}초"])
    B["ttk"] = table(["적", "체력", "기본 연타로", "강공격으로"], rows)

    # 떨어뜨리기
    rows = []
    kb, kc, ks = k["basic"]["knock"][2], k["charged"]["knock"], k["slam"]["knock"]
    combo = sum(k["basic"]["times"])
    for e in climbers:
        g = e["grip"]
        n, ct = charged_hits(k, g, kc)
        fn, fct = charged_hits(k, g * k["fall"]["frozen_grip"], kc * k["fall"]["frozen_knock_taken"])
        after_slam = max(0, g - ks)
        rows.append([f"**{e['name']}**", g,
                     f"{n}번, {ct}초",
                     "한 번에" if after_slam == 0 else f"내려찍기 + 강공격 {math.ceil(after_slam / kc)}번",
                     f"연타 {math.ceil(g / kb)}세트, {math.ceil(g / kb) * combo:.1f}초",
                     f"{fn}번, {fct}초"])
    B["knock"] = table(["적", "붙잡기", "강공격으로", "내려찍기를 섞으면", "기본 연타의 3타째로", "빙결된 적을 강공격으로"], rows)

    # 낙하
    heights = [(H, "턱밑 (18m)"), (12, "2단 끝 (12m)"), (6, "1단 끝 (6m)")]
    rows = []
    for e in climbers:
        row = [f"**{e['name']}**", e["hp"]]
        for h, _ in heights:
            d = fall_damage(k, h)
            row.append("죽는다" if d >= e["hp"] else f"체력 {e['hp'] - d} 남는다")
        rows.append(row)
    B["fall"] = table(["적", "체력"] + [f"{lab}에서 — 피해 {fall_damage(k, h)}" for h, lab in heights], rows)

    # 덮치기 (떨어지는 적이 바로 아래의 적 하나와 부딪힌다)
    cd, ck = k["fall"]["collide_damage"], k["fall"]["collide_knock"]
    rows = []
    for e in climbers:
        cells = []
        for size in ("소", "중", "대"):
            if cd[size] >= e["hp"]:
                cells.append("죽는다")
            elif ck[size] >= e["grip"]:
                cells.append("함께 떨어진다")
            elif ck[size] * 1.5 >= e["grip"]:
                cells.append("'굴러떨어지는 것들'이 있으면 떨어진다")
            elif ck[size] >= e["grip"] * k["fall"]["frozen_grip"]:
                cells.append("빙결 상태면 떨어진다")
            else:
                cells.append("버틴다")
        rows.append([f"**{e['name']}**", f"{e['hp']} / {e['grip']}"] + cells)
    B["collide"] = table(["아래에 있던 적", "체력 / 붙잡기"] +
                         [f"{s}형이 덮치면 — 피해 {cd[s]}, 밀어냄 {ck[s]}" for s in ("소", "중", "대")], rows)

    # 1장 습격의 부하
    bal = balance().threat_table()[waves["chapter"] - 1]
    win, mv = k["assault"]["spawn_window"], k["assault"]["move_between_kills"]
    rows = []
    for w in waves["waves"]:
        units = [(by[u], c) for u, c in w["units"].items()]
        wall = [(e, c) for e, c in units if e["route"] == "절벽면"]
        other = [(e, c) for e, c in units if e["route"] != "절벽면"]
        t = sum((ttk_basic(k, e["hp"])[0] + mv) * c for e, c in wall)
        n = sum(c for _, c in wall)
        cost = sum(e["cost"] * c for e, c in units)
        load = t / win
        label = "여유" if load < 0.75 else "빠듯" if load <= 1.0 else "넘친다"
        budget = bal["총공세"] if w["n"] == "총공세" else bal[str(w["n"])]
        rows.append([f"{w['n']}" + (" (엘리트)" if w.get("elite") else ""), budget,
                     ", ".join(f"{e['name']} {c}" for e, c in units),
                     f"{n}" + (f" + 바위턱 {sum(c for _, c in other)}" if other else ""),
                     f"{t:.0f}초", f"{load:.2f} · {label}", w.get("intro", "")])
    B["waves"] = table(["습격", "예산", "편성", "성벽에 닿는 수", "기본기로 처리하는 시간", "부하", "처음 나오는 적"], rows)

    # 장마다 요구되는 처리 능력
    base = by["climber"]["cost"] / (ttk_basic(k, by["climber"]["hp"])[0] + mv)
    rows = []
    for c, row in enumerate(balance().threat_table()):
        w = k["assault"]["spawn_window_by_chapter"][c]
        last = max(v for key, v in row.items() if key not in ("장", "총공세"))
        rows.append([f"{c + 1}장", row["1"], last, f"{w}초", f"{last / w:.2f}", f"{last / w / base:.1f}배"])
    B["capacity"] = (f"기본기의 처리 능력은 초당 위협 {base:.2f}이다(등반병 한 기를 처치하고 다음 적으로 옮기는 데 "
                     f"{ttk_basic(k, by['climber']['hp'])[0] + mv:.2f}초).\n\n"
                     + table(["장", "첫 습격의 예산", "마지막 습격의 예산", "스폰 창", "마지막 습격의 초당 위협", "기본기의 몇 배"], rows))

    # 인장
    B["sigils"] = table(["인장", "상태이상", "규칙", "1단계", "2단계", "3단계"],
                        [[f"**{s['name']}**", s["status"], s["rule"]] + s["levels"] for s in sig["sigils"]])
    B["reactions"] = table(["반응", "재료", "효과", "상태를 소모하는가"],
                           [[f"**{r['name']}**", f"{r['a']} + {r['b']}", r["effect"], "소모한다" if r["consumes"] else "소모하지 않는다 (지속 규칙)"]
                            for r in sig["reactions"]])
    B["affixes"] = table(["수식어", "효과", "대응"], [[f"**{a['name']}**", a["effect"], a["counter"]] for a in sig["elite_affixes"]])
    return B


def main():
    k = load("data/combat/king.json")
    enemies = load("data/combat/enemies.json")
    sig = load("data/combat/sigils.json")
    waves = load("data/waves/ch1.json")
    errors, by = validate(k, enemies, sig, waves)
    if errors:
        print("검증 실패:")
        for e in errors:
            print(" -", e)
        sys.exit(1)
    B = blocks(k, enemies, sig, waves, by)
    if "--print" in sys.argv or not DOC.exists():
        for name, text in B.items():
            print(f"\n### {name}\n{text}")
        return
    doc = DOC.read_text(encoding="utf-8")
    for name, text in B.items():
        pat = re.compile(rf"(<!-- AUTO:{name} -->\n).*?(<!-- /AUTO:{name} -->)", re.S)
        if not pat.search(doc):
            print(f"문서에 AUTO:{name} 블록이 없다")
            sys.exit(1)
        doc = pat.sub(lambda m: m.group(1) + text + "\n" + m.group(2), doc)
    DOC.write_text(doc, encoding="utf-8")
    print(f"검증 통과. {DOC.relative_to(ROOT)}의 표 {len(B)}개 갱신")


if __name__ == "__main__":
    main()
