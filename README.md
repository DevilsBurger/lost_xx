# LOST REGALIA

성 뒤편 곶을 파 나아가 부서진 무기를 깨우고, 종이 울리면 성벽으로 돌아와 절벽을 기어오르는 군세를 직접 베어 떨어뜨리는 3D 탑다운 채굴·방어 액션.

- 플랫폼: PC (Steam)
- 엔진: Unity 6
- 제작: 1인. 기간 제한 없음, 재미가 기준
- 모드: 캠페인(영구 누적) + 농성(한 판짜리). 1인 플레이 전용

## 문서

| 문서 | 내용 |
|---|---|
| [시나리오](docs/01_scenario.md) | 배경, 인물, 프롤로그 + 5장, 결말 |
| [게임 기획서 (GDD)](docs/02_gdd.md) | 핵심 루프, 레갈리아와 각성 트리, 성·자원·곶 탐사, 적과 보스, 챕터와 모드, 범위와 만드는 순서 |
| [프로토타입 사양서](docs/03_prototype_spec.md) | 첫 4주에 만들 것, 수치, 작업 순서, 통과 기준 |
| [경제·밸런스 시트](docs/04_balance.md) | 매장량과 비용, 시간 예산, 진행 시뮬레이션, 위협 예산 |
| [검 각성 트리 상세](docs/05_sword_tree.md) | 검의 노드 69개, 기술과 변형, 진명과 궁극, 빌드 경로 예시 |
| [전투 규칙](docs/06_combat_rules.md) | 피해와 추락의 규칙, 인장과 반응, 적 15종의 행동, 습격 편성, 보스 5종 |

## 도구

밸런스 시뮬레이션:

```bash
python3 balance/balance_sim.py
```

`balance/balance_sim.py` 위쪽의 값을 고치고 다시 돌리면 `balance/out/`의 표가 갱신된다.

각성 트리 문서 생성:

```bash
python3 tools/tree_doc.py sword
```

`data/trees/sword.json`이 원본이다. 노드를 고치고 다시 돌리면 검증을 거쳐 `docs/05_sword_tree.md`가 갱신된다. 비용이 밸런스 시뮬레이션과 어긋나거나 선행 조건이 깨지면 문서가 만들어지지 않는다.

전투 규칙 문서의 계산 표 갱신:

```bash
python3 tools/combat_doc.py
```

`data/combat/`와 `data/waves/`가 원본이다. 처치 시간, 떨어뜨리는 데 필요한 횟수, 낙하 결과, 습격의 부하를 계산해 `docs/06_combat_rules.md`의 표를 채운다.
