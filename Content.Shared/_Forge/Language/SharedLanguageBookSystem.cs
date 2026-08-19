using Content.Shared.DoAfter;
using Content.Shared.Interaction.Events;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Language;

/// <summary>
///     Общая логика изучения языков по книгам. Фактическая выдача знаний и проверка
///     кулдаунов выполняется в серверной части.
/// </summary>
public abstract class SharedLanguageBookSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<N14LanguageBookComponent, UseInHandEvent>(OnUseInHand);
    }

    private void OnUseInHand(Entity<N14LanguageBookComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        if (TryStartStudy(ent, args.User))
            args.Handled = true;
    }

    /// <summary>
    ///     Запускает этап изучения (DoAfter) для книги. Реализуется на сервере.
    /// </summary>
    protected abstract bool TryStartStudy(Entity<N14LanguageBookComponent> book, EntityUid user);
}

/// <summary>
///     DoAfter-событие, срабатывающее по завершении одного этапа изучения языка.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class N14LanguageStudyDoAfterEvent : SimpleDoAfterEvent;