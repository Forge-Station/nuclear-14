using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._N14.Casino;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SlotMachineComponent : Component
{
    #region Sounds

    [DataField]
    public SoundSpecifier SpinSound = new SoundPathSpecifier("/Audio/_Nuclear14/Effects/Slotmachine/sfx_slots_pulllever.wav");

    [DataField]
    public SoundSpecifier LoseSound = new SoundPathSpecifier("/Audio/_Nuclear14/Effects/Slotmachine/sfx_slots_lose.wav");

    [DataField]
    public SoundSpecifier SmallWinSound = new SoundPathSpecifier("/Audio/_Nuclear14/Effects/Slotmachine/sfx_slots_win_small.wav");

    [DataField]
    public SoundSpecifier MediumWinSound = new SoundPathSpecifier("/Audio/_Nuclear14/Effects/Slotmachine/sfx_slots_win_med.wav");

    [DataField]
    public SoundSpecifier BigWinSound = new SoundPathSpecifier("/Audio/_Nuclear14/Effects/Slotmachine/sfx_slots_win_jackpot.wav");

    [DataField]
    public SoundSpecifier JackPotWinSound = new SoundPathSpecifier("/Audio/_Nuclear14/Effects/Slotmachine/sfx_slots_win_jackpot.wav");

    #endregion

    #region Chances

    [DataField, AutoNetworkedField]
    public float SmallWinChance = .20f;

    [DataField, AutoNetworkedField]
    public float MediumWinChance = .04f;

    [DataField, AutoNetworkedField]
    public float BigWinChance = .0167f;

    [DataField, AutoNetworkedField]
    public float JackPotWinChance = .0067f;

    #endregion

    #region Prize Amounts

    [DataField, AutoNetworkedField]
    public int SpinCost = 50;

    [DataField, AutoNetworkedField]
    public int SmallPrizeAmount = 100;

    [DataField, AutoNetworkedField]
    public int MediumPrizeAmount = 250;

    [DataField, AutoNetworkedField]
    public int BigPrizeAmount = 500;

    [DataField, AutoNetworkedField]
    public int JackPotPrizeAmount = 1000;

    #endregion

    #region DoAfter

    [DataField, AutoNetworkedField]
    public float DoAfterTime = 3.8f;

    [DataField, AutoNetworkedField]
    public bool IsSpinning;

    #endregion
}

[Serializable, NetSerializable]
public enum SlotMachineVisuals : byte
{
    Spinning
}