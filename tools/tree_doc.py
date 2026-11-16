#!/usr/bin/env python3
"""각성 트리 데이터를 검증하고 문서를 만든다.

데이터(data/trees/<형태>.json)가 원본이다. 문서는 여기서 생성하므로 직접 고치지 않는다.

실행:  python3 tools/tree_doc.py sword
출력:  docs/05_sword_tree.md
"""
from __future__ import annotations

import collections
import importlib.util
import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
TAGS = ["연쇄", "설치", "추락", "수호", "표식", "지대", "소환", "처형"]
BRANCH_SHAPE = {1: 2, 2: 4, 3: 5, 4: 5, 5: 3}       # 가지 하나의 단계별 노드 수
STEM_SHAPE = {1: 2, 2: 1, 3: 1, 4: 1, 5: 1}
CROSS_SHAPE = {2: 3, 4: 3}
OUT = {"sword": ("05_sword_tree.md", "검")}


def load_balance_tree():
    spec = importlib.util.spec_from_file_location("balance_sim", ROOT / "balance" / "balance_sim.py")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod.TREE


# --------------------------------------------------------------------------
def validate(data):
    errors = []
    nodes = data["nodes"]
    by_id = {}
    for n in nodes:
        if n["id"] in by_id:
            errors.append(f"중복 id {n['id']}")
        by_id[n["id"]] = n
    branches = [b["name"] for b in data["branches"] if b["name"] != "줄기"]

    if len(nodes) != 69:
        errors.append(f"노드 수 {len(nodes)} (69여야 한다)")

    # 단계별 모양
    for b in branches:
        shape = collections.Counter(n["tier"] for n in nodes if n["branch"] == b)
        if dict(shape) != BRANCH_SHAPE:
            errors.append(f"{b} 가지의 단계별 노드 수 {dict(shape)}")
    if dict(collections.Counter(n["tier"] for n in nodes if n["branch"] == "줄기")) != STEM_SHAPE:
        errors.append("줄기의 단계별 노드 수가 다르다")
    if dict(collections.Counter(n["tier"] for n in nodes if n["branch"] == "교차")) != CROSS_SHAPE:
        errors.append("교차의 단계별 노드 수가 다르다")

    # 선행 조건
    for n in nodes:
        for r in n["requires"]:
            if r not in by_id:
                errors.append(f"{n['id']}의 선행 {r}가 없다")
            elif by_id[r]["tier"] > n["tier"]:
                errors.append(f"{n['id']}의 선행 {r}가 더 높은 단계다")
        for t in n["tags"]:
            if t not in TAGS:
                errors.append(f"{n['id']}의 태그 {t}는 정의되지 않았다")

    # 기술마다 변형 분기 둘
    for s in (n for n in nodes if n["kind"] == "기술"):
        forks = sorted((n for n in nodes if n.get("of") == s["id"]), key=lambda n: n["stage"])
        if [f["stage"] for f in forks] != [1, 2]:
            errors.append(f"{s['name']}의 변형 분기가 둘이 아니다")
            continue
        if forks[0]["tier"] != s["tier"] + 1 or forks[1]["tier"] != s["tier"] + 2:
            errors.append(f"{s['name']}의 변형 분기 단계가 어긋난다")
        if s["id"] not in forks[0]["requires"] or forks[0]["id"] not in forks[1]["requires"]:
            errors.append(f"{s['name']}의 변형 분기 선행 조건이 빠졌다")
    for e in (n for n in nodes if n["kind"] == "진화"):
        ult = [n for n in nodes if n["kind"] == "궁극" and n["branch"] == e["branch"]]
        if not ult or ult[0]["id"] not in e["requires"]:
            errors.append(f"{e['name']}이 궁극을 선행으로 갖지 않는다")

    # 가지마다 기술 3, 변형 6, 수동 7, 진명 1, 궁극 1, 진화 1
    for b in branches:
        kinds = collections.Counter(n["kind"] for n in nodes if n["branch"] == b)
        want = {"기술": 3, "변형": 6, "수동": 7, "진명": 1, "궁극": 1, "진화": 1}
        if dict(kinds) != want:
            errors.append(f"{b} 가지의 구성 {dict(kinds)}")

    # 밸런스 시뮬레이션의 비용표와 일치하는가
    mine = collections.Counter((n["kind"], n["tier"], n["cost"], n.get("rune", 0)) for n in nodes)
    sim = collections.Counter(load_balance_tree())
    if mine != sim:
        diff = (mine - sim) + (sim - mine)
        errors.append(f"밸런스 시뮬레이션의 비용표와 다르다: {dict(diff)}")

    # 빌드 예시
    reports = []
    for bd in data["builds"]:
        have = set(bd["nodes"])
        if len(have) != len(bd["nodes"]):
            errors.append(f"빌드 '{bd['name']}'에 중복 노드")
        for i in bd["nodes"]:
            if i not in by_id:
                errors.append(f"빌드 '{bd['name']}'의 {i}가 없다")
                continue
            n = by_id[i]
            for r in n["requires"]:
                if r not in have:
                    errors.append(f"빌드 '{bd['name']}': {n['name']}의 선행 {by_id[r]['name']}가 없다")
            if n["branch"] in branches and n["tier"] >= 2:
                below = sum(1 for j in have if by_id[j]["branch"] == n["branch"] and by_id[j]["tier"] == n["tier"] - 1)
                if below < 2:
                    errors.append(f"빌드 '{bd['name']}': {n['name']} 아래 단계 노드가 {below}개뿐이다")
            if n["kind"] == "교차":
                for p in n["pair"]:
                    cnt = sum(1 for j in have if by_id[j]["branch"] == p)
                    if cnt < n["need"]:
                        errors.append(f"빌드 '{bd['name']}': {n['name']}에 {p} 노드 {cnt}/{n['need']}")
        for kid in bd["keystones"]:
            if kid not in have or by_id[kid]["kind"] != "진명":
                errors.append(f"빌드 '{bd['name']}': 진명 {kid}")
        eq = [by_id[i]["kind"] for i in bd["equip"] if i in have]
        if sorted(eq) != ["궁극", "기술", "기술", "기술"] or len(eq) != len(bd["equip"]):
            errors.append(f"빌드 '{bd['name']}': 장착은 기술 셋과 궁극 하나여야 한다")
        got = [by_id[i] for i in bd["nodes"] if i in by_id]
        tags = collections.Counter(t for n in got for t in n["tags"])
        reports.append({
            "build": bd, "count": len(got), "cost": sum(n["cost"] for n in got),
            "rune": sum(n.get("rune", 0) for n in got), "tags": tags,
            "by_tier": {t: [n for n in got if n["tier"] == t] for t in range(1, 6)},
        })
    return errors, by_id, branches, reports


# --------------------------------------------------------------------------
def table(head, rows):
    out = ["| " + " | ".join(head) + " |", "|" + "---|" * len(head)]
    out += ["| " + " | ".join(str(c) for c in r) + " |" for r in rows]
    return "\n".join(out)


def tag_str(n):
    return " ".join(n["tags"]) if n["tags"] else "—"


def render(data, by_id, branches, reports):
    nodes = data["nodes"]
    form = data["form"]
    total = sum(n["cost"] for n in nodes)
    L = []
    L += [f"# LOST REGALIA — {form}: 각성 트리 상세", "",
          f"> 데이터 버전 {data['version']} · 원본: [`data/trees/sword.json`](../data/trees/sword.json)",
          "> 이 문서는 `python3 tools/tree_doc.py sword`로 생성한다. **직접 고치지 않는다.** 데이터를 고치고 다시 생성한다.",
          "> 짝 문서: [GDD 8장](./02_gdd.md#8-각성-시스템--스킬-트리와-조합) · [경제·밸런스 시트](./04_balance.md)",
          "> 수치는 모두 출발값이다. 인장과 상태이상의 수치는 전투 규칙 문서에서 함께 맞춘다.", "",
          f"**{data['pitch']}**", "", "---", ""]

    # 1
    kinds = collections.Counter(n["kind"] for n in nodes)
    L += ["## 1. 한눈에", "",
          table(["구분", "수", "성광석"],
                [[k, kinds[k], sum(n["cost"] for n in nodes if n["kind"] == k)]
                 for k in ["줄기", "기술", "변형", "수동", "진명", "궁극", "진화", "교차"]]
                + [["**합계**", f"**{len(nodes)}**", f"**{total:,}**"]]), "",
          "단계별 비용: 1단계 8 · 2단계 14 · 3단계 22 · 4단계 34 · 5단계 50. "
          "진명 32(+ 룬 수정 2) · 궁극 48 · 궁극 진화 60 · 교차 18 / 40.", "",
          "표준 진행에서는 이 가운데 약 38개, 성광석 900 안팎을 깨운다. 전부 깨울 수는 없다.", "",
          "### 규칙", ""]
    L += [f"{i}. {r}" for i, r in enumerate(data["rules"], 1)]
    L += ["", "### 단계가 열리는 때", "",
          table(["단계", "열리는 때", "새로 오는 것"],
                [[1, "프롤로그", "가지의 첫 기술"],
                 [2, "1장 대파편", "둘째 기술, 첫 변형, 2단계 교차"],
                 [3, "2장 대파편", "진명, 셋째 기술"],
                 [4, "3장 대파편", "궁극기, 4단계 교차"],
                 [5, "4장 대파편", "궁극 진화"]]), ""]

    # 2
    L += ["---", "", "## 2. 기본기", "",
          "깨운 노드가 하나도 없어도 쓸 수 있는 것들이다. 트리의 모든 수치는 이 값을 고친다.", "",
          table(["동작", "내용"], [[b["name"], b["text"]] for b in data["base"]]), ""]

    # 3
    stem = [n for n in nodes if n["branch"] == "줄기"]
    pitch = {b["name"]: b["pitch"] for b in data["branches"]}
    L += ["---", "", "## 3. 줄기", "", pitch["줄기"], "",
          table(["단계", "노드", "성광석", "효과"], [[n["tier"], f"**{n['name']}**", n["cost"], n["text"]] for n in stem]), ""]

    # 4~
    sec = 4
    for b in branches:
        bn = [n for n in nodes if n["branch"] == b]
        skills = [n for n in bn if n["kind"] == "기술"]
        L += ["---", "", f"## {sec}. {b}", "", f"**{pitch[b]}**", ""]
        L += ["### 기술", "",
              table(["단계", "기술", "성광석", "효과", "피해", "재사용", "밀어냄", "곶에서", "태그"],
                    [[n["tier"], f"**{n['name']}**", n["cost"], n["text"],
                      n["stats"]["피해"] or "—", f"{n['stats']['재사용']}초", n["stats"]["밀어냄"] or "—",
                      n["mine"], tag_str(n)] for n in skills]), ""]
        forks = sorted((n for n in bn if n["kind"] == "변형"), key=lambda n: (by_id[n["of"]]["tier"], n["stage"]))
        L += ["### 변형 분기", "", "분기마다 A와 B 중 하나를 고른다. 기술 하나가 네 가지 형태 중 하나가 된다.", "",
              table(["기술", "차수", "단계", "성광석", "A", "B", "태그"],
                    [[by_id[n["of"]]["name"], f"{n['stage']}차", n["tier"], n["cost"],
                      f"**{n['a']['name']}** — {n['a']['text']}", f"**{n['b']['name']}** — {n['b']['text']}", tag_str(n)]
                     for n in forks]), ""]
        L += ["### 수동", "",
              table(["단계", "노드", "성광석", "효과", "태그"],
                    [[n["tier"], f"**{n['name']}**", n["cost"], n["text"], tag_str(n)]
                     for n in bn if n["kind"] == "수동"]), ""]
        ks = next(n for n in bn if n["kind"] == "진명")
        ult = next(n for n in bn if n["kind"] == "궁극")
        evo = next(n for n in bn if n["kind"] == "진화")
        L += ["### 진명", "",
              table(["진명", "비용", "얻는 것", "치르는 것"],
                    [[f"**{ks['name']}**", f"성광석 {ks['cost']} + 룬 수정 {ks['rune']}", ks["text"], ks["price"]]]), "",
              "### 궁극과 진화", "",
              table(["단계", "노드", "성광석", "효과", "재사용", "곶에서"],
                    [[ult["tier"], f"**{ult['name']}**", ult["cost"], ult["text"], f"{ult['stats']['재사용']}초", ult["mine"]],
                     [evo["tier"], f"**{evo['name']}**", evo["cost"], evo["text"], "—", "—"]]), ""]
        sec += 1

    # 교차
    cross = [n for n in nodes if n["kind"] == "교차"]
    L += ["---", "", f"## {sec}. 교차 노드", "",
          "두 가지를 함께 키운 빌드의 보상이다. 두 가지에 각각 정해진 수 이상의 노드를 깨워야 열린다.", "",
          table(["단계", "가지", "노드", "성광석", "조건", "효과", "태그"],
                [[n["tier"], " × ".join(n["pair"]), f"**{n['name']}**", n["cost"], f"각 {n['need']}개", n["text"], tag_str(n)]
                 for n in cross]), ""]
    sec += 1

    # 태그
    tally = collections.Counter(t for n in nodes for t in n["tags"])
    L += ["---", "", f"## {sec}. 태그와 공명", "",
          "같은 태그를 3 / 6 / 9개 모으면 공명이 켜진다. 아래는 이 트리 안에 있는 태그의 수다. "
          "룬, 성물, 시설의 태그가 여기에 더해진다.", "",
          table(["태그", "트리 안의 노드 수", "노드", "트리만으로 닿는 공명"],
                [[t, tally[t], ", ".join(n["name"] for n in nodes if t in n["tags"]) or "—",
                  "9" if tally[t] >= 9 else "6" if tally[t] >= 6 else "3" if tally[t] >= 3 else "닿지 않는다"]
                 for t in TAGS]), "",
          "검은 추락과 수호가 두텁고, 설치는 룬 없이는 없다. 설치 빌드를 하려면 '설치' 룬이나 부형태가 필요하다. 의도한 빈칸이다.", ""]
    sec += 1

    # 빌드
    L += ["---", "", f"## {sec}. 빌드 경로 예시", "",
          "표준 진행(노드 약 38개, 성광석 900 안팎)으로 닿을 수 있는 세 가지 길이다. "
          "선행 조건과 교차 조건을 모두 만족하는지 스크립트로 확인했다.", ""]
    for r in reports:
        bd = r["build"]
        reach = [f"{t} {c}" + (" → 공명 9" if c >= 9 else " → 공명 6" if c >= 6 else " → 공명 3" if c >= 3 else "")
                 for t, c in sorted(r["tags"].items(), key=lambda x: -x[1])]
        L += [f"### {bd['name']}", "", bd["pitch"], "",
              table(["항목", "내용"],
                    [["노드", f"{r['count']}개"],
                     ["비용", f"성광석 {r['cost']:,} · 룬 수정 {r['rune']}"],
                     ["장착", " · ".join(by_id[i]["name"] for i in bd["equip"])],
                     ["진명", " · ".join(by_id[i]["name"] for i in bd["keystones"])],
                     ["어울리는 인장", bd["sigil"]],
                     ["트리 안의 태그", ", ".join(reach)]]), "",
              table(["단계", "깨우는 노드", "성광석"],
                    [[t, ", ".join(n["name"] for n in ns), sum(n["cost"] for n in ns)] for t, ns in r["by_tier"].items()]), ""]
    sec += 1

    # 검증
    L += ["---", "", f"## {sec}. 검증", "",
          "문서를 생성할 때마다 아래를 확인한다. 하나라도 어긋나면 문서가 만들어지지 않는다.", "",
          table(["확인", "결과"],
                [["노드 수", f"{len(nodes)}"],
                 ["가지의 구성 (기술 3, 변형 6, 수동 7, 진명 1, 궁극 1, 진화 1)", "세 가지 모두 일치"],
                 ["단계별 노드 수", "일치"],
                 ["기술마다 변형 분기 둘, 단계와 선행 조건", "일치"],
                 ["밸런스 시뮬레이션의 비용표", f"일치 (합계 성광석 {total:,})"],
                 ["빌드 예시의 선행·교차·장착 조건", f"{len(reports)}개 모두 만족"]]), ""]
    sec += 1

    L += ["---", "", f"## {sec}. 플레이로 확인할 것", "",
          table(["지점", "걱정"],
                [["검기의 관통과 폭발", "한쪽이 모든 상황의 답이 되지 않는가. 관통은 석궁수와 주술사, 폭발은 턱밑의 무리로 갈려야 한다"],
                 ["왕의 방패", "누르고만 있으면 안전해지지 않는가. 최대 2초와 재사용 5초로 막았다"],
                 ["성을 부수는 검의 채굴", "깎는 강도 3배가 채굴 우선 진행에서 지나치지 않은가"],
                 ["궁극의 재사용 90~100초", "습격 한 번에 한 번 쓰는 간격이다. 습격 길이(55~75초)에 맞는가"],
                 ["처형 '벼랑 끝'과 추락 공명", "붙잡기만 깎으면 무엇이든 떨어지는 조합이다. 대형 적과 보스에서 지나치지 않은가"],
                 ["일섬과 왕의 귀환", "둘이 겹쳐 '일부러 늦게 돌아오기'가 이득이 되지 않는가"],
                 ["레갈리아의 맥동", "기본 공격이 인장을 띠면 반응이 너무 쉬워지지 않는가"]]), ""]
    return "\n".join(L)


def main():
    form = sys.argv[1] if len(sys.argv) > 1 else "sword"
    data = json.loads((ROOT / "data" / "trees" / f"{form}.json").read_text(encoding="utf-8"))
    errors, by_id, branches, reports = validate(data)
    if errors:
        print("검증 실패:")
        for e in errors:
            print(" -", e)
        sys.exit(1)
    out = ROOT / "docs" / OUT[form][0]
    out.write_text(render(data, by_id, branches, reports), encoding="utf-8")
    print(f"검증 통과. {out.relative_to(ROOT)} 생성")
    for r in reports:
        print(f"  {r['build']['name']}: 노드 {r['count']}, 성광석 {r['cost']}, 태그 {dict(r['tags'])}")


if __name__ == "__main__":
    main()
