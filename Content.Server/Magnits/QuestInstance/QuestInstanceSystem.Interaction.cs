using Content.Shared.Interaction;
using Content.Shared.Magnits.QuestInstance;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Shared.Map;
using Robust.Shared.Player;


namespace Content.Server.Magnits.QuestInstance;

public sealed partial class QuestInstanceSystem
{
    private void TeleportWithPulledEntity(EntityUid user, EntityCoordinates destination)
    {
        if (TryComp<PullerComponent>(user, out var puller)
            && puller.Pulling is { } pulled
            && TryComp<PullableComponent>(pulled, out var pullable))
        {
            _transform.SetCoordinates(pulled, destination);
            _transform.AttachToGridOrMap(pulled);
            _transform.SetCoordinates(user, destination);
            _transform.AttachToGridOrMap(user);

            if (pullable.Puller != user)
                _pulling.TryStartPull(user, pulled, puller, pullable);

            return;
        }

        _transform.SetCoordinates(user, destination);
        _transform.AttachToGridOrMap(user);
    }

    private void SendWarningPopups(QuestBoardComponent board, TimeSpan remaining)
    {
        PrunePresentPlayers(board);
        if (board.PresentPlayers.Count == 0)
            return;

        List<EntityUid>? players = null;

        foreach (var threshold in board.WarningThresholdsSeconds)
        {
            if (remaining.TotalSeconds > threshold)
                continue;

            if (!board.SentWarnings.Add(threshold))
                continue;

            var msg = Loc.GetString("quest-instance-warning", ("seconds", threshold));
            players ??= [..board.PresentPlayers,];

            foreach (var uid in players)
            {
                if (!TryComp<ActorComponent>(uid, out var actor))
                    continue;

                _popup.PopupEntity(msg, uid, actor.PlayerSession, PopupType.LargeCaution);
            }
        }
    }

    private void OnSignpostUsed(EntityUid uid, QuestSignpostComponent comp, InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<ActorComponent>(args.User, out var actor))
            return;

        if (!TryComp<QuestBoardComponent>(comp.BoardUid, out var board) || !board.HasActiveInstance)
            return;

        args.Handled = true;

        ExitPlayer(comp.BoardUid, actor.PlayerSession, board);
        TryCleanupIfEmpty(comp.BoardUid, board);
        SendBoardState(comp.BoardUid, board);
    }

    private void OnBoardTerminating(EntityUid uid, QuestBoardComponent comp, EntityTerminatingEvent args)
    {
        if (!comp.HasActiveInstance)
            return;

        ForceCloseInstance(uid, comp);
    }

    private void OnBoardUiOpened(EntityUid uid, QuestBoardComponent comp, AfterActivatableUIOpenEvent args) =>
        SendBoardState(uid, comp);

    private void OnBoardSelectDifficulty(EntityUid uid, QuestBoardComponent comp, QuestBoardSelectDifficultyMessage msg)
    {
        if (!TryComp<ActorComponent>(msg.Actor, out var actor))
            return;

        StartOrJoinInstance(uid, actor.PlayerSession, msg.Difficulty);
        SendBoardState(uid, comp);
    }

    private void SendBoardState(EntityUid boardUid, QuestBoardComponent? board = null)
    {
        if (!Resolve(boardUid, ref board, false))
            return;

        QuestBoardBoundUserInterfaceState state;

        if (board.HasActiveInstance)
        {
            PrunePresentPlayers(board);
            var remaining = board.EndAt - _timing.CurTime;
            state = new(
                true,
                Math.Max(0, (int) remaining.TotalSeconds),
                board.PresentPlayers.Count);
        }
        else
            state = new(false, 0, 0);

        _ui.SetUiState(boardUid, QuestBoardUiKey.Key, state);
    }
}
