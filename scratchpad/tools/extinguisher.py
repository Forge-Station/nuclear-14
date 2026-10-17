"""Переводит самотушитель на апстримный SharedChargesSystem: заряды больше не
лежат в LimitedChargesComponent.Charges, а вычисляются, а SharedActionsSystem
больше не умеет SetCharges/SetMaxCharges - этим занимается система зарядов."""
import io

PATH = 'Content.Shared/SelfExtinguisher/SharedSelfExtinguisherSystem.cs'

EDITS = [
    # MapInit
    ('        _actions.SetUseDelay(component.ActionEntity, component.Cooldown);\n'
     '        if (TryComp<LimitedChargesComponent>(uid, out var charges))\n'
     '        {\n'
     '            _actions.SetCharges(component.ActionEntity, charges.Charges);\n'
     '            _actions.SetMaxCharges(component.ActionEntity, charges.MaxCharges);\n'
     '        }\n',
     '        _actions.SetUseDelay(component.ActionEntity, component.Cooldown);\n'
     '        if (TryComp<LimitedChargesComponent>(uid, out var charges))\n'
     '            MirrorCharges(component.ActionEntity, (uid, charges));\n'),

    # SetCharges
    ('        _charges.SetCharges((uid, chargeComp), charges, maxCharges);\n'
     '        _actions.SetCharges(component.ActionEntity, chargeComp.Charges);\n'
     '        _actions.SetMaxCharges(component.ActionEntity, chargeComp.MaxCharges);\n'
     '\n'
     '        _actions.SetEnabled(component.ActionEntity, chargeComp.Charges != 0);\n'
     '    }\n',
     '        if (maxCharges != null)\n'
     '            _charges.SetMaxCharges((uid, chargeComp), maxCharges.Value);\n'
     '\n'
     '        if (charges != null)\n'
     '            _charges.SetCharges((uid, chargeComp), charges.Value);\n'
     '\n'
     '        MirrorCharges(component.ActionEntity, (uid, chargeComp));\n'
     '    }\n'
     '\n'
     '    /// <summary>\n'
     '    /// Действие только отражает заряды самого предмета: источник истины -\n'
     '    /// его LimitedCharges, как и было до переезда на апстримную систему зарядов.\n'
     '    /// </summary>\n'
     '    private void MirrorCharges(EntityUid? action, Entity<LimitedChargesComponent> source)\n'
     '    {\n'
     '        if (action == null)\n'
     '            return;\n'
     '\n'
     '        var current = _charges.GetCurrentCharges((source.Owner, source.Comp));\n'
     '\n'
     '        if (TryComp<LimitedChargesComponent>(action, out var actionCharges))\n'
     '        {\n'
     '            _charges.SetMaxCharges((action.Value, actionCharges), source.Comp.MaxCharges);\n'
     '            _charges.SetCharges((action.Value, actionCharges), current);\n'
     '        }\n'
     '\n'
     '        _actions.SetEnabled(action, current != 0);\n'
     '    }\n'),

    # InteractUsing
    ('        if (charges.Charges >= charges.MaxCharges)\n',
     '        if (_charges.GetCurrentCharges((uid, charges)) >= charges.MaxCharges)\n'),
    ('        // Add charges\n'
     '        _charges.AddCharges(uid, refill.RefillAmount, charges);\n'
     '        _actions.SetCharges(component.ActionEntity, charges.Charges);\n'
     '\n'
     '        // Reenable action\n'
     '        _actions.SetEnabled(component.ActionEntity, charges.Charges != 0);\n'
     '\n',
     '        // Add charges\n'
     '        _charges.AddCharges(uid, refill.RefillAmount);\n'
     '\n'
     '        // Mirror the new charges onto the action and reenable it\n'
     '        MirrorCharges(component.ActionEntity, (uid, charges));\n'
     '\n'),

    # Examine
    ('        if (TryComp<LimitedChargesComponent>(uid, out var charges) && charges.Charges == 0)\n',
     '        if (TryComp<LimitedChargesComponent>(uid, out var charges) && _charges.IsEmpty((uid, charges)))\n'),
]

text = io.open(PATH, encoding='utf-8-sig').read()
newline = '\r\n' if '\r\n' in text else '\n'
body = text.replace('\r\n', '\n')
for old, new in EDITS:
    if old not in body:
        print('!!', old.strip().split('\n')[0][:72])
        continue
    body = body.replace(old, new, 1)
io.open(PATH, 'w', encoding='utf-8', newline=newline).write(body)
print('ok', PATH)
