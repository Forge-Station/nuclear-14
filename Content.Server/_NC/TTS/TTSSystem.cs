using System.Threading.Tasks;
using Content.Server.Chat.Systems;
using Content.Server.Language;
using Content.Server.Radio.Components;
using Content.Shared._NC.CorvaxVars;
using Content.Shared._NC.TTS;
using Content.Shared.GameTicking;
using Content.Shared.Language;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Players.RateLimiting;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;


namespace Content.Server._NC.TTS;

// ReSharper disable once InconsistentNaming
public sealed partial class TTSSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly INetConfigurationManager _netCfg = default!;
    [Dependency] private readonly LanguageSystem _language = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly TTSManager _ttsManager = default!;
    [Dependency] private readonly SharedTransformSystem _xforms = default!;
    [Dependency] private readonly IRobustRandom _rng = default!;

    private readonly List<string> _sampleText =
        new()
        {
            "Война... Война никогда не меняется.",
            "Падаль! Убирайся с моего участка!",
            "Ещё один поселенец нуждается в твоей помощи. Отмечу его на твоей карте.",
            "Дверная ловушка — лучший друг одинокого сталкера.",
            "Пахнет жареным... и не супчиком.",
            "Кто тут стреляет? У меня патронов на всех хватит!",
            "Найдёшь крышечку — считай, день прошёл не зря.",
            "Этот робот явно видел лучшие дни... и более целые шестерёнки.",
            "Радиация — это просто неприятное покалывание на коже.",
            "Говорят, в пустошах водятся гигантские муравьи... Надеюсь, это просто слухи.",
            "Мой Пси-нож нуждается в заточке... и свежих мозгах.",
            "Видишь суслика? А он есть! Или это просто галлюцинация от радиации...",
            "Эй, гладкокожий! Есть что обменять на крышечки?",
            "Мой брамин сегодня какой-то задумчивый... Наверное, обед проглотил не тот.",
            "Эти супермутанты совсем распоясались! Кто-то должен навести порядок.",
            "Странник, приветствую тебя. Не хочешь сыграть в караван?",
            "Мой энергетический пистолет требует перезарядки... и пару свежих батарей.",
            "Говорят, в этих развалинах водятся призраки... Наверное, просто гули шутят.",
            "Ещё один день в пустошах — ещё одна история для таверны.",
            "Эти роботы-охранники становятся всё настырнее... Прямо как моя бывшая.",
            "Нашёл новый ствол? Покажи! Только не направляй в мою сторону.",
            "Пустоши прекрасны... если забыть про радиацию, мутантов и бандитов.",
            "Мой счётчик гейгера трещит веселее, чем радио Даймонд-сити!",
            "Видел того типа в синем комбинезоне? Говорит, что из убежища... Странный тип.",
            "Эй, сталкер! Не хочешь присоединиться к нашему каравану? Делиться будем честно... Наверное."
        };

    private const int MaxMessageChars = 100 * 2; // same as SingleBubbleCharLimit * 2
    private bool _isEnabled = false;

    public override void Initialize()
    {
        _cfg.OnValueChanged(CorvaxVars.TTSEnabled, v => _isEnabled = v, true);

        SubscribeLocalEvent<TransformSpeechEvent>(OnTransformSpeech);
        SubscribeLocalEvent<TTSComponent, EntitySpokeEvent>(OnEntitySpoke);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);

        SubscribeNetworkEvent<RequestPreviewTTSEvent>(OnRequestPreviewTTS);

        RegisterRateLimits();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _ttsManager.ResetCache();
    }

    private async void OnRequestPreviewTTS(RequestPreviewTTSEvent ev, EntitySessionEventArgs args)
    {
        if (!_isEnabled ||
            !_prototypeManager.TryIndex<TTSVoicePrototype>(ev.VoiceId, out var protoVoice))
            return;

        if (HandleRateLimit(args.SenderSession) != RateLimitStatus.Allowed)
            return;

        var previewText = _rng.Pick(_sampleText);
        var soundData = await GenerateTTS(previewText, protoVoice.Speaker);
        if (soundData is null)
            return;

        RaiseNetworkEvent(new PlayTTSEvent(soundData), Filter.SinglePlayer(args.SenderSession));
    }

    private async void OnEntitySpoke(EntityUid uid, TTSComponent component, EntitySpokeEvent args) 
    { 
        if (TryComp<MindContainerComponent>(uid, out var mindCon) 
            && TryComp<MindComponent>(mindCon.Mind, out var mind) && mind.Session != null) 
        { 
            var channel = mind.Session.Channel; 
            if (!_netCfg.GetClientCVar(channel, CorvaxVars.LocalTTSEnabled)) 
                return; 
        }

        if (HasComp<ActiveRadioComponent>(uid))
            await Task.Delay(1000);
        
        var voiceId = component.VoicePrototypeId; 
        if (!_isEnabled || 
            args.Message.Length > MaxMessageChars || 
            voiceId == null) 
            return;
        
        var voiceEv = new TransformSpeakerVoiceEvent(uid, voiceId); 
        RaiseLocalEvent(uid, voiceEv); 
        voiceId = voiceEv.VoiceId;
        
        if (!_prototypeManager.TryIndex<TTSVoicePrototype>(voiceId, out var protoVoice)) 
            return;
        
        var obfuscatedMessage = _language.ObfuscateSpeech(args.Message, args.Language);
        
        await Handle(uid, args.Message, protoVoice.Speaker, args.IsWhisper, obfuscatedMessage, args.Language);
    }
    
    private async Task Handle(
        EntityUid uid,
        string message,
        string speaker,
        bool isWhisper,
        string obfuscatedMessage,
        LanguagePrototype language
        )
    { 
        var fullSoundData = await GenerateTTS(message, speaker, isWhisper); 
        if (fullSoundData is null) return;
        await Task.Delay(70);
        
        var obfSoundData = await GenerateTTS(obfuscatedMessage, speaker, isWhisper); 
        if (obfSoundData is null) return;
        
        var fullTtsEvent = new PlayTTSEvent(fullSoundData, GetNetEntity(uid), isWhisper);
        var obfTtsEvent = new PlayTTSEvent(obfSoundData, GetNetEntity(uid), isWhisper);
        
        var xformQuery = GetEntityQuery<TransformComponent>(); 
        var sourcePos = _xforms.GetWorldPosition(xformQuery.GetComponent(uid), xformQuery); 
        var recipients = Filter.Pvs(uid).Recipients;
        
        foreach (var session in recipients) 
        { 
            if (!session.AttachedEntity.HasValue) continue;
            
            var listener = session.AttachedEntity.Value; 
            var xform = xformQuery.GetComponent(listener); 
            var distance = (sourcePos - _xforms.GetWorldPosition(xform, xformQuery)).Length();
            
            if (distance > ChatSystem.VoiceRange * ChatSystem.VoiceRange) continue;
            var canUnderstand = _language.CanUnderstand(listener, language);
            
            RaiseNetworkEvent(canUnderstand ? fullTtsEvent : obfTtsEvent, session); 
        } 
    }
    
    // ReSharper disable once InconsistentNaming
    private async Task<byte[]?> GenerateTTS(string text, string speaker, bool isWhisper = false)
    {
        var textSanitized = Sanitize(text);
        if (textSanitized == "") return null;
        if (char.IsLetter(textSanitized[^1]))
            textSanitized += ".";

        var ssmlTraits = SoundTraits.RateFast;
        if (isWhisper)
            ssmlTraits = SoundTraits.PitchVerylow;
        var textSsml = ToSsmlText(textSanitized, ssmlTraits);

        return await _ttsManager.ConvertTextToSpeech(speaker, textSsml);
    }
}
