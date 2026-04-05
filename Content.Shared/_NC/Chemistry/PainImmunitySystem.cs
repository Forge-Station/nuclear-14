using System;
using Content.Shared.Clothing;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.StatusEffect;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server.PainImmunity
{
    public sealed class PainImmunitySystem : EntitySystem
    {
        [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;
        [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;

        private const string StatusEffectKey = "PainImmunity";

        public override void Initialize()
        {
            base.Initialize();

            // Перехватываем событие изменения скорости от урона
            SubscribeLocalEvent<PainImmunityComponent, ModifySlowOnDamageSpeedEvent>(
                OnModifySlowOnDamageSpeed,
                after: new[] { typeof(ClothingSlowOnDamageModifierComponent) } // выполняется после модификаторов одежды
            );
        }

        private void OnModifySlowOnDamageSpeed(
            EntityUid uid,
            PainImmunityComponent _,
            ref ModifySlowOnDamageSpeedEvent args)
        {
            // Полностью убираем замедление от боли
            args.Speed = 1f;
        }

        /// <summary>
        /// Накладывает временный иммунитет к боли на указанную сущность.
        /// </summary>
        /// <param name="uid">Цель.</param>
        /// <param name="duration">Длительность в секундах.</param>
        public void ApplyPainImmunity(EntityUid uid, TimeSpan duration)
        {
            // Добавляем статус-эффект, который автоматически добавит компонент PainImmunityComponent
            // и удалит его по окончании времени.
            if (!_statusEffects.TryAddStatusEffect<PainImmunityComponent>(
                    uid, StatusEffectKey, duration, refresh: true))
            {
                // Если не получилось добавить (например, нет компонента StatusEffectsComponent),
                // можно залогировать или просто проигнорировать.
                return;
            }

            // Принудительно обновляем скорость, чтобы иммунитет применился немедленно.
            _movementSpeed.RefreshMovementSpeedModifiers(uid);
        }

        /// <summary>
        /// Снимает иммунитет к боли досрочно.
        /// </summary>
        public void RemovePainImmunity(EntityUid uid)
        {
            if (_statusEffects.HasStatusEffect(uid, StatusEffectKey))
                _statusEffects.TryRemoveStatusEffect(uid, StatusEffectKey);
        }

        /// <summary>
        /// Проверяет, есть ли у сущности иммунитет к боли.
        /// </summary>
        public bool HasPainImmunity(EntityUid uid)
        {
            return _statusEffects.HasStatusEffect(uid, StatusEffectKey);
        }
    }
}