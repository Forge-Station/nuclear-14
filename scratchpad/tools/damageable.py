"""Переводит обращения к закрытым полям DamageableComponent на публичные
геттеры DamageableSystem: апстрим выставил им Other = AccessPermissions.None."""
import io

DEP = '    [Dependency] private readonly DamageableSystem _damageable = default!;'

EDITS = {
    'Content.Shared/_Shitmed/Surgery/SharedSurgerySystem.cs': [
        ('            || damageable.TotalDamage <= 0\n'
         '            && partDamageable.TotalDamage <= 0\n',
         '            || _damageable.GetTotalDamage((args.Body, damageable)) <= 0\n'
         '            && _damageable.GetTotalDamage((args.Part, partDamageable)) <= 0\n'),
    ],
    'Content.Shared/_Shitmed/Surgery/SharedSurgerySystem.Steps.cs': [
        ('    private bool HasDamageGroup(EntityUid entity, string[] group, out DamageableComponent? damageable)\n'
         '    {\n'
         '        if (!TryComp<DamageableComponent>(entity, out var damageableComp))\n'
         '        {\n'
         '            damageable = null;\n'
         '            return false;\n'
         '        }\n'
         '\n'
         '        damageable = damageableComp;\n'
         '        return group.Any(damageType => damageableComp.Damage.DamageDict.TryGetValue(damageType, out var value) && value > 0);\n',
         '    private bool HasDamageGroup(EntityUid entity, string[] group, out Entity<DamageableComponent>? damageable)\n'
         '    {\n'
         '        if (!TryComp<DamageableComponent>(entity, out var damageableComp))\n'
         '        {\n'
         '            damageable = null;\n'
         '            return false;\n'
         '        }\n'
         '\n'
         '        damageable = (entity, damageableComp);\n'
         '        var dict = _damageable.GetAllDamage(damageable.Value).DamageDict;\n'
         '        return group.Any(damageType => dict.TryGetValue(damageType, out var value) && value > 0);\n'),
        ('        var bonus = ent.Comp.HealMultiplier * damageable.DamagePerGroup[ent.Comp.MainGroup];\n',
         '        var bonus = ent.Comp.HealMultiplier\n'
         '                    * _damageable.GetDamagePerGroup(damageable.Value)[ent.Comp.MainGroup];\n'),
    ],
    'Content.Shared/Chat/SharedSuicideSystem.cs': [
        ('        var lethalAmountOfDamage = mobThresholds.Thresholds.Keys.Last() - target.Comp.TotalDamage;\n',
         '        var lethalAmountOfDamage = mobThresholds.Thresholds.Keys.Last() - _damageableSystem.GetTotalDamage(target.Owner);\n'),
        ('        var lethalAmountOfDamage = mobThresholds.Thresholds.Keys.Last() - target.Comp.Damage.GetTotal();\n',
         '        var lethalAmountOfDamage = mobThresholds.Thresholds.Keys.Last() - _damageableSystem.GetTotalDamage(target.Owner);\n'),
        ('        _damageableSystem.TryChangeDamage(target, appliedDamageSpecifier, true, origin: target);\n',
         '        _damageableSystem.TryChangeDamage(target.Owner, appliedDamageSpecifier, true, origin: target);\n'),
        ('        _damageableSystem.TryChangeDamage(target, damage, true, origin: target);\n',
         '        _damageableSystem.TryChangeDamage(target.Owner, damage, true, origin: target);\n'),
    ],
    'Content.Shared/_N14/Radiation/Systems/RadiationHealingSystem.cs': [
        ('        if (!TryComp(uid, out DamageableComponent? damage) || damage.TotalDamage >= 90f)\n'
         '            return;\n'
         '\n'
         '        var healable = GetHealableDamage(damage);\n',
         '        if (!TryComp(uid, out DamageableComponent? damage)\n'
         '            || _damageable.GetTotalDamage((uid, damage)) >= 90f)\n'
         '            return;\n'
         '\n'
         '        var healable = GetHealableDamage((uid, damage));\n'),
        ('    private static float GetHealableDamage(DamageableComponent damage)\n'
         '    {\n'
         '        float amount = 0f;\n'
         '        foreach (var type in HealableTypes)\n'
         '        {\n'
         '            if (damage.Damage.DamageDict.TryGetValue(type, out var val) && val > FixedPoint2.Zero)\n',
         '    private float GetHealableDamage(Entity<DamageableComponent> damage)\n'
         '    {\n'
         '        var dict = _damageable.GetAllDamage(damage).DamageDict;\n'
         '        float amount = 0f;\n'
         '        foreach (var type in HealableTypes)\n'
         '        {\n'
         '            if (dict.TryGetValue(type, out var val) && val > FixedPoint2.Zero)\n'),
    ],
    'Content.Shared/Contests/ContestsSystem.cs': [
        ('            ? 1 - damage.TotalDamage.Float() / threshold.Value.Float()\n'
         '            : 1 - Math.Clamp(damage.TotalDamage.Float() / threshold.Value.Float(), 0, 0.25f * rangeFactor));\n',
         '            ? 1 - _damageable.GetTotalDamage((performer, damage)).Float() / threshold.Value.Float()\n'
         '            : 1 - Math.Clamp(_damageable.GetTotalDamage((performer, damage)).Float() / threshold.Value.Float(), 0, 0.25f * rangeFactor));\n'),
        ('            ? (1 - perfDamage.TotalDamage.Float() / perfThreshold.Value.Float())\n'
         '                / (1 - targetDamage.TotalDamage.Float() / targetThreshold.Value.Float())\n'
         '            : (1 - Math.Clamp(perfDamage.TotalDamage.Float() / perfThreshold.Value.Float(), 0, 0.25f * rangeFactor))\n'
         '                / (1 - Math.Clamp(targetDamage.TotalDamage.Float() / targetThreshold.Value.Float(), 0, 0.25f * rangeFactor)));\n',
         '            ? (1 - _damageable.GetTotalDamage((performer, perfDamage)).Float() / perfThreshold.Value.Float())\n'
         '                / (1 - _damageable.GetTotalDamage((target, targetDamage)).Float() / targetThreshold.Value.Float())\n'
         '            : (1 - Math.Clamp(_damageable.GetTotalDamage((performer, perfDamage)).Float() / perfThreshold.Value.Float(), 0, 0.25f * rangeFactor))\n'
         '                / (1 - Math.Clamp(_damageable.GetTotalDamage((target, targetDamage)).Float() / targetThreshold.Value.Float(), 0, 0.25f * rangeFactor)));\n'),
    ],
    'Content.Shared/_Shitmed/Body/Systems/SharedBodySystem.Targeting.cs': [
        ('        var damage = damageable.TotalDamage;\n',
         '        var damage = _damageable.GetTotalDamage((entity.Owner, damageable));\n'),
        ('            && damageable.TotalDamage >= partEnt.Comp.SeverIntegrity\n',
         '            && _damageable.GetTotalDamage((partEnt.Owner, damageable)) >= partEnt.Comp.SeverIntegrity\n'),
        ('        var integrity = damageable.TotalDamage;\n',
         '        var integrity = _damageable.GetTotalDamage((partEnt.Owner, damageable));\n'),
        ('                result[targetBodyPart.Value] = GetIntegrityThreshold(partComponent.Component, damageable.TotalDamage.Float(), false);\n',
         '                result[targetBodyPart.Value] = GetIntegrityThreshold(partComponent.Component,\n'
         '                    _damageable.GetTotalDamage((partComponent.Id, damageable)).Float(), false);\n'),
    ],
}

# Файлы, которым нужна сама зависимость на систему.
NEED_DEP = [
    'Content.Shared/_N14/Radiation/Systems/RadiationHealingSystem.cs',
    'Content.Shared/Contests/ContestsSystem.cs',
]

for path, pairs in EDITS.items():
    text = io.open(path, encoding='utf-8-sig').read()
    newline = '\r\n' if '\r\n' in text else '\n'
    body = text.replace('\r\n', '\n')
    for old, new in pairs:
        if old not in body:
            print('!! не найдено в', path, '->', old.strip().split('\n')[0][:70])
            continue
        body = body.replace(old, new, 1)

    if path in NEED_DEP and '_damageable = default!' not in body:
        lines = body.split('\n')
        at = next((i for i, line in enumerate(lines) if line.strip().startswith('[Dependency]')), None)
        if at is None:
            # В классе ещё нет зависимостей - ставим сразу за открывающей скобкой.
            start = next(i for i, line in enumerate(lines) if ' class ' in line)
            at = next(i for i in range(start, len(lines)) if lines[i].strip() == '{') + 1
            lines.insert(at, '')
        lines.insert(at, DEP)
        body = '\n'.join(lines)

    io.open(path, 'w', encoding='utf-8', newline=newline).write(body)
    print('ok', path)
