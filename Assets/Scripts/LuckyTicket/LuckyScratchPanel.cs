using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One scratchable window on the ticket: a ScratchSurface (the foil) sitting over a reward
/// icon/text that's already there, just covered - scratching doesn't create the content, it
/// uncovers it. LuckyScratchTicketManager assigns the reward via Setup() right before the ticket
/// becomes interactive, then listens for OnRevealed to apply it and check win/lose.
/// </summary>
[RequireComponent(typeof(ScratchSurface))]
public class LuckyScratchPanel : MonoBehaviour
{
    [Header("Reward Display (sits underneath the foil)")]
    [SerializeField] private Image rewardIcon;
    [SerializeField] private TextMeshProUGUI rewardText;
    [Tooltip("Optional - a small grow/pop tween plays on this once revealed. Leave unassigned to skip.")]
    [SerializeField] private Transform popTarget;

    private ScratchSurface scratchSurface;

    public ScratchRewardDefinition AssignedReward { get; private set; }
    public bool IsRevealed => scratchSurface != null && scratchSurface.IsRevealed;

    /// <summary>Raised once this panel is fully scratched - passes itself so the manager can read AssignedReward.</summary>
    public event Action<LuckyScratchPanel> OnRevealed;

    private void Awake()
    {
        scratchSurface = GetComponent<ScratchSurface>();
        scratchSurface.OnFullyRevealed += HandleFullyRevealed;
    }

    private void OnDestroy()
    {
        if (scratchSurface != null) scratchSurface.OnFullyRevealed -= HandleFullyRevealed;
    }

    /// <summary>Called by LuckyScratchTicketManager right after rolling this panel's reward,
    /// before the player can scratch anything. Also resets the foil, so panels can be reused
    /// across multiple ticket instances instead of being instantiated fresh every time.</summary>
    public void Setup(ScratchRewardDefinition reward)
    {
        AssignedReward = reward;
        scratchSurface.ResetSurface();

        if (rewardIcon != null)
        {
            rewardIcon.sprite = reward != null ? reward.icon : null;
            rewardIcon.enabled = reward != null && reward.icon != null;
        }
        if (rewardText != null)
            rewardText.text = reward != null ? reward.BuildDisplayText() : string.Empty;
    }

    /// <summary>Skip button / timeout path - see LuckyScratchTicketManager.RevealAllRemaining.</summary>
    public void ForceReveal() => scratchSurface.RevealFully();

    private void HandleFullyRevealed()
    {
        if (popTarget != null)
        {
            popTarget.localScale = Vector3.one * 0.85f;
            DOTween.Sequence()
                .Append(popTarget.DOScale(1.15f, 0.12f).SetEase(Ease.OutBack))
                .Append(popTarget.DOScale(1f, 0.08f));
        }

        OnRevealed?.Invoke(this);
    }
}
