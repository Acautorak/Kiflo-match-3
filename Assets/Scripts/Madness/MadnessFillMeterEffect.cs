using UnityEngine;

/// <summary>Adds to the run's Madness meter. What happens when it fills isn't implemented yet - see MadnessMeter.</summary>
[CreateAssetMenu(fileName = "MadnessFillMeter", menuName = "Match3/Madness/Effects/Fill Meter")]
public class MadnessFillMeterEffect : MadnessEffect
{
    public float amount = 10f;

    public override void Execute(MadnessContext ctx) => ctx.Meter?.Add(amount);
}
