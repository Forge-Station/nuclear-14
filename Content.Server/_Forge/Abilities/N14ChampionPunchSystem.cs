using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Shared._Forge.Abilities;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Forge.Abilities;

public sealed partial class N14ChampionPunchSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<N14ChampionPunchActionEvent>(OnPunch);
    }

    private void OnPunch(N14ChampionPunchActionEvent args)
    {
        if (args.Handled)
            return;

        var target = args.Target;

        var damage = new DamageSpecifier();
        damage.DamageDict["Blunt"] = FixedPoint2.New(20);
        _damageable.TryChangeDamage(target, damage, interruptsDoAfters: false);

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Weapons/punch1.ogg"), target);

        var direction = Transform(target).WorldPosition - Transform(args.Performer).WorldPosition;
        if (direction.LengthSquared() > 0.001f)
            _physics.ApplyLinearImpulse(target, Vector2.Normalize(direction) * 3f);

        _chat.TrySendInGameICMessage(target, Loc.GetString("n14-champion-punch-emote"), InGameICChatType.Emote, false);

        args.Handled = true;
    }
}
