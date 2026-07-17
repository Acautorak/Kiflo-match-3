using UnityEngine;

/// <summary>
/// Awards score that grows the longer this symbol survives unmatched before being cleared.
/// Intended for onClearedEffects - the payout is a one-time bonus added on top of the cell's
/// normal match score (via Board.AddBonusScore), not a replacement for it.
/// </summary>
[CreateAssetMenu(fileName = "MadnessScoreGrowth", menuName = "Match3/Madness/Effects/Score Growth")]
public class MadnessScoreGrowthEffect : MadnessEffect
{
    [Min(0)] public int baseBonusScore = 10;
    [Min(0)] public int bonusScorePerMoveSurvived = 5;

    public override void Execute(MadnessContext ctx)
    {
        int bonus = baseBonusScore + bonusScorePerMoveSurvived * ctx.MovesSurvived;
        if (bonus <= 0) return;
        ctx.Board?.AddBonusScore(bonus);
    }
}
