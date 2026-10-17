"""Доводит кластер Standing: восстанавливает потерянные мерджем форковые
кейбинды, переводит потребителей tri-state StandingState на публичный IsDown
и заменяет форковый DropHeldItemsBehavior апстримным bool."""
import io

TARGETS = [
    ('TargetHead', 'target-head', 'Target head', 'Целиться в голову'),
    ('TargetTorso', 'target-torso', 'Target torso', 'Целиться в торс'),
    ('TargetLeftArm', 'target-left-arm', 'Target left arm', 'Целиться в левую руку'),
    ('TargetLeftHand', 'target-left-hand', 'Target left hand', 'Целиться в левую кисть'),
    ('TargetRightArm', 'target-right-arm', 'Target right arm', 'Целиться в правую руку'),
    ('TargetRightHand', 'target-right-hand', 'Target right hand', 'Целиться в правую кисть'),
    ('TargetLeftLeg', 'target-left-leg', 'Target left leg', 'Целиться в левую ногу'),
    ('TargetLeftFoot', 'target-left-foot', 'Target left foot', 'Целиться в левую ступню'),
    ('TargetRightLeg', 'target-right-leg', 'Target right leg', 'Целиться в правую ногу'),
    ('TargetRightFoot', 'target-right-foot', 'Target right foot', 'Целиться в правую ступню'),
]


def patch(path, pairs, must=True):
    text = io.open(path, encoding='utf-8-sig').read()
    bom = io.open(path, 'rb').read(3) == b'\xef\xbb\xbf'
    newline = '\r\n' if '\r\n' in text else '\n'
    body = text.replace('\r\n', '\n')
    for old, new in pairs:
        if old not in body:
            if must:
                print('!!', path, '->', old.strip().split('\n')[0][:70])
            continue
        body = body.replace(old, new, 1)
    io.open(path, 'w', encoding='utf-8-sig' if bom else 'utf-8', newline=newline).write(body)
    print('ok', path)


# --- кейбинды: объявления ---
keys = ['OfferItem', 'LookUp', 'OpenLanguageMenu', 'ToggleCrawlingUnder'] + [t[0] for t in TARGETS]
block = ['', '        // Forge-Change-Start: кейбинды форка, которых нет у апстрима']
block += [f'        public static readonly BoundKeyFunction {k} = "{k}";' for k in keys]
block += ['        // Forge-Change-End']
patch('Content.Shared/Input/ContentKeyFunctions.cs', [
    ('        public static readonly BoundKeyFunction ResetZoom = "ResetZoom";\n',
     '        public static readonly BoundKeyFunction ResetZoom = "ResetZoom";\n' + '\n'.join(block) + '\n'),
])

# --- кейбинды: контекст "human" ---
ctx = ['            human.AddFunction(ContentKeyFunctions.OpenEmotesMenu);',
       '            // Forge-Change-Start']
ctx += [f'            human.AddFunction(ContentKeyFunctions.{k});' for k in keys]
ctx += ['            // Forge-Change-End']
patch('Content.Client/Input/ContentContexts.cs', [
    ('            human.AddFunction(ContentKeyFunctions.OpenEmotesMenu);',
     '\n'.join(ctx)),
])

# --- кейбинды: привязки по умолчанию ---
binds = """
# Forge-Change-Start
- function: OfferItem
  type: State
  key: F
- function: ToggleCrawlingUnder
  type: State
  mod1: Shift
  key: R
- function: OpenLanguageMenu
  type: State
  key: L
- function: LookUp
  type: State
  key: Space
# Forge-Change-End
"""
text = io.open('Resources/keybinds.yml', encoding='utf-8-sig').read()
if 'function: ToggleCrawlingUnder' not in text:
    newline = '\r\n' if '\r\n' in text else '\n'
    body = text.replace('\r\n', '\n').rstrip('\n') + '\n' + binds
    io.open('Resources/keybinds.yml', 'w', encoding='utf-8', newline=newline).write(body)
    print('ok Resources/keybinds.yml')

# --- окно переназначения клавиш ---
rebind = ['            AddButton(ContentKeyFunctions.ToggleKnockdown);',
          '            // Forge-Change-Start',
          '            AddButton(ContentKeyFunctions.ToggleCrawlingUnder);',
          '            AddButton(ContentKeyFunctions.LookUp);',
          '            AddCheckBox("ui-options-function-hold-look-up", _cfg.GetCVar(CCVars.HoldLookUp), HandleHoldLookUp);',
          '            // Forge-Change-End']
targeting = ['            // Forge-Change-Start',
             '            AddHeader("ui-options-header-targeting");']
targeting += [f'            AddButton(ContentKeyFunctions.{t[0]});' for t in TARGETS]
targeting += ['            // Forge-Change-End', '']
patch('Content.Client/Options/UI/Tabs/KeyRebindTab.xaml.cs', [
    ('            AddButton(ContentKeyFunctions.ToggleKnockdown);', '\n'.join(rebind)),
    ('            AddButton(ContentKeyFunctions.RotateStoredItem);\n',
     '            AddButton(ContentKeyFunctions.RotateStoredItem);\n'
     '            AddButton(ContentKeyFunctions.OfferItem); // Forge-Change\n'),
    ('            AddHeader("ui-options-header-misc");',
     '\n'.join(targeting) + '            AddHeader("ui-options-header-misc");'),
    ('        private void HandleToggleWalk(BaseButton.ButtonToggledEventArgs args)',
     '        // Forge-Change-Start\n'
     '        private void HandleHoldLookUp(BaseButton.ButtonToggledEventArgs args)\n'
     '        {\n'
     '            _cfg.SetCVar(CCVars.HoldLookUp, args.Pressed);\n'
     '        }\n'
     '        // Forge-Change-End\n'
     '\n'
     '        private void HandleToggleWalk(BaseButton.ButtonToggledEventArgs args)'),
])

# --- локали ---
loc_en = ['', '# Forge-Change-Start',
          'ui-options-function-offer-item = Offer something',
          'ui-options-function-toggle-crawling-under = Toggle crawling under furniture',
          'ui-options-function-look-up = Look up/Take aim',
          'ui-options-function-hold-look-up = Hold down the key to aim',
          'ui-options-header-targeting = Targeting']
loc_en += [f'ui-options-function-{t[1]} = {t[2]}' for t in TARGETS]
loc_en += ['# Forge-Change-End', '']

loc_ru = ['', '# Forge-Change-Start',
          'ui-options-function-offer-item = Предложить что-нибудь',
          'ui-options-function-toggle-crawling-under = Вкл/выкл ползание под мебелью',
          'ui-options-function-look-up = Смотреть вверх/Целиться',
          'ui-options-function-hold-look-up = Удерживать клавишу для прицеливания',
          'ui-options-header-targeting = Прицеливание']
loc_ru += [f'ui-options-function-{t[1]} = {t[3]}' for t in TARGETS]
loc_ru += ['# Forge-Change-End', '']

for path, lines in (('Resources/Locale/en-US/escape-menu/ui/options-menu.ftl', loc_en),
                    ('Resources/Locale/ru-RU/escape-menu/ui/options-menu.ftl', loc_ru)):
    text = io.open(path, encoding='utf-8-sig').read()
    if 'ui-options-header-targeting' in text:
        print('уже есть:', path)
        continue
    newline = '\r\n' if '\r\n' in text else '\n'
    body = text.replace('\r\n', '\n').rstrip('\n') + '\n' + '\n'.join(lines)
    io.open(path, 'w', encoding='utf-8', newline=newline).write(body)
    print('ok', path)

# --- CVar автоподъёма: апстримный knockdown встаёт сам, настройка осиротела ---
patch('Content.Shared/CCVar/CCVars.LayingDown.cs', [
    ('    public static readonly CVarDef<bool> AutoGetUp =\n'
     '        CVarDef.Create("rest.auto_get_up", true, CVar.CLIENT | CVar.ARCHIVE | CVar.REPLICATED);\n'
     '\n', ''),
])

# --- DropHeldItemsBehavior -> апстримный bool drop ---
patch('Content.Shared/Repulsor/RepulseSystem.cs', [
    ('        _stunSystem.TryKnockdown(user, repulsor.Comp.KnockdownDuration, true, DropHeldItemsBehavior.DropIfStanding);',
     '        _stunSystem.TryKnockdown(user, repulsor.Comp.KnockdownDuration, true, drop: true);'),
])
patch('Content.Server/Stunnable/Components/KnockdownOnHitComponent.cs', [
    ('using Content.Shared.Standing;\n\n', ''),
    ('    [DataField]\n'
     '    public DropHeldItemsBehavior DropHeldItemsBehavior = DropHeldItemsBehavior.NoDrop;\n',
     '    /// <summary>\n'
     '    /// Выбивать ли предметы из рук. Раньше это был форковый DropHeldItemsBehavior,\n'
     '    /// апстримный TryKnockdown принимает bool.\n'
     '    /// </summary>\n'
     '    [DataField]\n'
     '    public bool DropHeldItems;\n'),
])

# --- tri-state StandingState -> публичный IsDown ---
patch('Content.Shared/InteractionVerbs/Requirements/AssortedRequirements.cs', [
    ('        if (!deps.EntMan.TryGetComponent<StandingStateComponent>(args.Target, out var state))\n'
     '            return false;\n'
     '\n'
     '        return state.CurrentState == StandingState.Standing && AllowStanding\n'
     '            || state.CurrentState == StandingState.Lying && AllowLaying;\n',
     '        if (!deps.EntMan.HasComponent<StandingStateComponent>(args.Target))\n'
     '            return false;\n'
     '\n'
     '        // Апстрим свёл состояние к Standing/нет, промежуточного GettingUp больше нет.\n'
     '        return deps.EntMan.System<StandingStateSystem>().IsDown(args.Target)\n'
     '            ? AllowLaying\n'
     '            : AllowStanding;\n'),
])
patch('Content.Shared/Movement/Systems/SharedMobCollisionSystem.cs', [
    ('        if (StandingQuery.TryComp(entity.Owner, out var standing) && standing.CurrentState == StandingState.Lying)',
     '        if (StandingQuery.HasComp(entity.Owner) && Standing.IsDown(entity.Owner))'),
    ('            if (StandingQuery.TryComp(other, out var otherStanding) && otherStanding.CurrentState == StandingState.Lying)',
     '            if (StandingQuery.HasComp(other) && Standing.IsDown(other))'),
])
patch('Content.Server/FootPrint/FootPrintsSystem.cs', [
    ('        var dragging = _standingStateQuery.TryComp(uid, out var standing) && standing.CurrentState == StandingState.Lying;',
     '        var dragging = _standingStateQuery.HasComp(uid) && _standing.IsDown(uid);'),
])
patch('Content.Server/_Forge/Cross/CrossServerSystem.cs', [
    ('                standing.CurrentState == StandingState.Standing ? RotationState.Vertical : RotationState.Horizontal);',
     '                _standing.IsDown(uid) ? RotationState.Horizontal : RotationState.Vertical);'),
])
