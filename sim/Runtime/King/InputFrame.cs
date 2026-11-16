using System;

namespace LostRegalia.Sim
{
    [Flags]
    public enum Buttons : ushort
    {
        None = 0,
        Attack = 1,     // 기본 공격. 누르고 있으면 모으고, 떼면 나간다
        Guard = 2,      // 고유 동작. 검은 받아치기
        Dash = 4,
        Skill1 = 8,
        Skill2 = 16,
        Skill3 = 32,
        Ultimate = 64,
        Interact = 128,
        Drop = 256
    }

    /// <summary>
    /// 틱 하나의 입력. 시뮬레이션은 장치를 모른다. 화면 쪽이 키보드든 패드든 이 모양으로 바꿔 넘긴다.
    /// 좌표는 성벽 기준이다. x는 성벽을 따라(왼쪽 끝이 0), y는 벼랑 턱에서 성 안쪽으로.
    /// </summary>
    public struct InputFrame
    {
        public float MoveX, MoveY;      // 길이 1 이하. 넘으면 1로 줄인다
        public float AimX, AimY;        // 길이 0이면 직전의 조준을 유지한다
        public Buttons Held;            // 이 틱에 눌려 있는 버튼. 눌린 순간과 뗀 순간은 왕이 직전 틱과 견주어 안다
    }
}
