using System;

namespace LostRegalia.Sim
{
    public enum CollideOutcome { Dies, Falls, FallsWithPassive, FallsIfFrozen, Holds }

    /// <summary>
    /// 추락의 규칙. 상태를 갖지 않는 순수한 계산이다.
    /// 전투 규칙 문서(docs/06) 3장의 표는 이 함수들과 같은 값을 내야 한다.
    /// </summary>
    public static class FallRules
    {
        /// <summary>낙하 피해 = 계수 × 높이².</summary>
        public static int FallDamage(KingDef k, float height)
        {
            return (int)Math.Round(k.FallCoef * height * height);
        }

        /// <summary>강공격을 몇 번 연달아 맞혀야 떨어지는가.</summary>
        public static int ChargedHitsToDrop(KingDef k, float grip, bool frozen)
        {
            float g = frozen ? grip * k.FrozenGrip : grip;
            float knock = frozen ? k.ChargeKnock * k.FrozenKnockTaken : k.ChargeKnock;
            return (int)Math.Ceiling(g / knock);
        }

        /// <summary>떨어지는 적이 바로 아래의 적을 덮쳤을 때의 결과.</summary>
        public static CollideOutcome Collide(KingDef k, Size falling, EnemyDef below)
        {
            float damage = k.CollideDamage[(int)falling];
            float knock = k.CollideKnock[(int)falling];
            if (damage >= below.Hp) return CollideOutcome.Dies;
            if (knock >= below.Grip) return CollideOutcome.Falls;
            if (knock * 1.5f >= below.Grip) return CollideOutcome.FallsWithPassive;
            if (knock >= below.Grip * k.FrozenGrip) return CollideOutcome.FallsIfFrozen;
            return CollideOutcome.Holds;
        }
    }
}
