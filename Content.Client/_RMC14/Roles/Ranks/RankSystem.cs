// #Forge-Change
using Content.Shared._RMC14.Roles.Ranks;
using Robust.Client;

namespace Content.Client._RMC14.Roles.Ranks;

public sealed class RankSystem : SharedRankSystem
{
    [Dependency] private readonly IBaseClient _client = default!;
    private readonly Dictionary<string, string> _preferences = new();
    private bool _requested;

    public event Action? PreferencesUpdated;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<RankPreferencesEvent>(OnPreferences);
        _client.RunLevelChanged += OnRunLevelChanged;
    }

    public override void Shutdown()
    {
        _client.RunLevelChanged -= OnRunLevelChanged;
        base.Shutdown();
    }

    public void RequestPreferences()
    {
        if (_requested || _client.RunLevel < ClientRunLevel.Connected)
            return;

        _requested = true;
        RaiseNetworkEvent(new RequestRankPreferencesEvent());
    }

    public string? GetPreference(string job) => _preferences.GetValueOrDefault(job);

    public void SetPreference(string job, string? rank)
    {
        RaiseNetworkEvent(new SetRankPreferenceEvent(job, rank));
    }

    private void OnPreferences(RankPreferencesEvent args)
    {
        _preferences.Clear();
        foreach (var (job, rank) in args.Preferences)
            _preferences[job] = rank;
        PreferencesUpdated?.Invoke();
    }

    private void OnRunLevelChanged(object? sender, RunLevelChangedEventArgs args)
    {
        if (args.NewLevel != ClientRunLevel.Initialize)
            return;

        _preferences.Clear();
        _requested = false;
        PreferencesUpdated?.Invoke();
    }
}
