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
| [곶 생성 규칙](docs/07_cape_generation.md) | 땅을 만드는 순서, 타일과 광맥, 자리와 위험, 검증, 만들어 본 땅의 측정값 |
| [HUD·인터페이스 설계서](docs/09_ui_hud.md) | 화면 배치, 종 게이지와 귀환선, 성벽 띠, 왕좌 메뉴, 글 규칙, 입력과 행동 유도, 단계별 범위 |
| [기술 설계서](docs/08_technical_design.md) | 시뮬레이션과 화면의 분리, 틱과 결정성, 저장과 되감기, Unity 쪽 구조, 데이터, 테스트 |

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

곶 생성기 (땅을 만들고, 검증하고, 재 본다):

```bash
python3 tools/cape_gen.py stats       # 문서의 표 갱신
python3 tools/cape_gen.py render 7    # 시드 7의 다섯 땅을 그림으로 (docs/img/)
```

`data/cape/lands.json`이 규칙의 원본이다. 이 생성기가 Unity 생성기의 기준 구현이다.

HUD·인터페이스 목업:

```bash
python3 tools/hud_mock.py
```

`docs/img/ui_*.svg` 다섯 장을 다시 그린다. `docs/09_ui_hud.md`의 그림 원본이다.

## 시뮬레이션 코어

`sim/`은 Unity를 모르는 순수 C# 코드다. Unity에서는 로컬 패키지로 읽고, 여기서는 `dotnet`으로 컴파일하고 테스트한다.

```bash
dotnet run --project sim/Tests~/Tests -c Release
```

규칙 데이터를 고쳤다면 `python3 tools/combat_doc.py`를 먼저 돌려 기대값(`data/expect/`)을 갱신한다.
