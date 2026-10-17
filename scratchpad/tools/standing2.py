"""Добирает остатки кластера Standing: зависимости на StandingStateSystem,
последние потребители tri-state и прототипы."""
import io
import pathlib
import re


def patch(path, pairs):
    text = io.open(path, encoding='utf-8-sig').read()
    bom = io.open(path, 'rb').read(3) == b'\xef\xbb\xbf'
    newline = '\r\n' if '\r\n' in text else '\n'
    body = text.replace('\r\n', '\n')
    for old, new in pairs:
        if old not in body:
            print('!!', path, '->', old.strip().split('\n')[0][:70])
            continue
        body = body.replace(old, new, 1)
    io.open(path, 'w', encoding='utf-8-sig' if bom else 'utf-8', newline=newline).write(body)
    print('ok', path)


patch('Content.Shared/Movement/Systems/SharedMobCollisionSystem.cs', [
    ('    [Dependency] protected EntityQuery<StandingStateComponent> StandingQuery = default!; // Corvax-Change',
     '    [Dependency] protected readonly StandingStateSystem Standing = default!; // Forge-Change\n'
     '    protected EntityQuery<StandingStateComponent> StandingQuery; // Corvax-Change'),
])

patch('Content.Server/FootPrint/FootPrintsSystem.cs', [
    ('    private EntityQuery<StandingStateComponent> _standingStateQuery;',
     '    [Dependency] private readonly StandingStateSystem _standing = default!; // Forge-Change\n'
     '\n'
     '    private EntityQuery<StandingStateComponent> _standingStateQuery;'),
])

patch('Content.Server/_Forge/Cross/CrossServerSystem.cs', [
    ('        else if (TryComp<StandingStateComponent>(target, out var standing))\n'
     '        {\n'
     '            _appearance.SetData(\n'
     '                target,\n'
     '                RotationVisuals.RotationState,\n'
     '                _standing.IsDown(uid) ? RotationState.Horizontal : RotationState.Vertical);\n',
     '        else if (HasComp<StandingStateComponent>(target))\n'
     '        {\n'
     '            _appearance.SetData(\n'
     '                target,\n'
     '                RotationVisuals.RotationState,\n'
     '                _standing.IsDown(target) ? RotationState.Horizontal : RotationState.Vertical);\n'),
])

patch('Content.Server/InteractionVerbs/Actions/ChangeStandingStateAction.cs', [
    ('        if (!deps.EntMan.TryGetComponent<StandingStateComponent>(args.Target, out var state))\n'
     '            return false;\n'
     '\n'
     '        if (isBefore)\n'
     '            args.Blackboard["standing"] = state.CurrentState;\n'
     '\n'
     '        return state.CurrentState == StandingState.Standing && MakeLaying\n'
     '               || state.CurrentState == StandingState.Lying && MakeStanding;\n',
     '        if (!deps.EntMan.HasComponent<StandingStateComponent>(args.Target))\n'
     '            return false;\n'
     '\n'
     '        // Апстрим свёл состояние к Standing/нет, промежуточного GettingUp больше нет.\n'
     '        var down = deps.EntMan.System<StandingStateSystem>().IsDown(args.Target);\n'
     '\n'
     '        if (isBefore)\n'
     '            args.Blackboard["standing"] = !down;\n'
     '\n'
     '        return down ? MakeStanding : MakeLaying;\n'),
    ('        if (!deps.EntMan.TryGetComponent<StandingStateComponent>(args.Target, out var state)\n'
     '            || args.TryGetBlackboard("standing", out StandingState oldStanding) && oldStanding != state.CurrentState)\n'
     '            return false;\n'
     '\n'
     '        // Note: these will get cancelled if the target is forced to stand/lay, e.g. due to a buckle or stun or something else.\n'
     '        if (state.CurrentState == StandingState.Lying && MakeStanding)\n'
     '            return stateSystem.Stand(args.Target);\n'
     '        else if (state.CurrentState == StandingState.Standing && MakeLaying)\n'
     '            return stateSystem.Down(args.Target);\n',
     '        if (!deps.EntMan.HasComponent<StandingStateComponent>(args.Target))\n'
     '            return false;\n'
     '\n'
     '        var down = stateSystem.IsDown(args.Target);\n'
     '\n'
     '        if (args.TryGetBlackboard("standing", out bool oldStanding) && oldStanding == down)\n'
     '            return false;\n'
     '\n'
     '        // Note: these will get cancelled if the target is forced to stand/lay, e.g. due to a buckle or stun or something else.\n'
     '        if (down && MakeStanding)\n'
     '            return stateSystem.Stand(args.Target);\n'
     '        else if (!down && MakeLaying)\n'
     '            return stateSystem.Down(args.Target);\n'),
])

patch('Content.Server/Stunnable/Systems/KnockdownOnHitSystem.cs', [
    ('        foreach (var target in args.HitEntities)\n'
     '        {\n'
     '            if (!TryComp(target, out StatusEffectsComponent? statusEffects))\n'
     '                continue;\n'
     '\n'
     '            _stun.TryKnockdown(target,\n'
     '                entity.Comp.Duration,\n'
     '                entity.Comp.RefreshDuration,\n'
     '                entity.Comp.DropHeldItemsBehavior,\n'
     '                statusEffects);\n'
     '        }\n',
     '        foreach (var target in args.HitEntities)\n'
     '        {\n'
     '            _stun.TryKnockdown(target,\n'
     '                entity.Comp.Duration,\n'
     '                entity.Comp.RefreshDuration,\n'
     '                drop: entity.Comp.DropHeldItems);\n'
     '        }\n'),
])

# --- прототипы ---
for path in pathlib.Path('Resources/Prototypes').rglob('*.yml'):
    raw = io.open(path, encoding='utf-8-sig').read()
    if 'type: LayingDown' not in raw and 'dropHeldItemsBehavior' not in raw:
        continue
    bom = io.open(path, 'rb').read(3) == b'\xef\xbb\xbf'
    newline = '\r\n' if '\r\n' in raw else '\n'
    body = raw.replace('\r\n', '\n')
    body = body.replace('type: LayingDown', 'type: CrawlUnder')
    # Форковый enum стал апстримным bool: NoDrop -> false, остальное -> true.
    body = re.sub(r'(\s*)dropHeldItemsBehavior:\s*NoDrop', r'\1dropHeldItems: false', body)
    body = re.sub(r'(\s*)dropHeldItemsBehavior:\s*\w+', r'\1dropHeldItems: true', body)
    io.open(path, 'w', encoding='utf-8-sig' if bom else 'utf-8', newline=newline).write(body)
    print('ok', path.as_posix())
