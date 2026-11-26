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
    private const float BluntDamage = 20f;
    private const float StaminaDamage = 50f;

    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly StaminaSystem _stamina = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<N14ChampionPunchActionEvent>(OnPunch);
    }

    private void OnPunch(N14ChampionPunchActionEvent args)
    {
        if (args.Handled || !Exists(args.Target))
            return;

        var target = args.Target;
        var damage = new DamageSpecifier();
        damage.DamageDict["Blunt"] = FixedPoint2.New(BluntDamage);
        _damageable.TryChangeDamage(target, damage, interruptsDoAfters: false, origin: args.Performer, canSever: false);
        _stamina.TakeStaminaDamage(target, StaminaDamage, source: args.Performer);

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Weapons/punch1.ogg"), target);

        var direction = Transform(target).WorldPosition - Transform(args.Performer).WorldPosition;
        if (direction.LengthSquared() > 0.001f)
            _physics.ApplyLinearImpulse(target, Vector2.Normalize(direction) * 3f);

        _chat.TrySendInGameICMessage(args.Performer, Loc.GetString("n14-champion-punch-emote"), InGameICChatType.Emote, false);

        args.Handled = true;
    }
}
