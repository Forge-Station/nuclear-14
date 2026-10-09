using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.Client.Corvax.Forge;

/// <summary>
///     Window hosting the armor-assembly minigame: drag the 4 limb pieces onto the torso.
/// </summary>
public sealed class N14ForgeMiniGameWindow : DefaultWindow
{
    private readonly N14ForgeMiniGameBoard _board;

    public event Action? Completed;

    public N14ForgeMiniGameWindow()
    {
        Title = Loc.GetString("n14-forge-minigame-title");
        SetSize = new Vector2(1020, 840);
        Resizable = false;

        _board = new N14ForgeMiniGameBoard
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };
        _board.Completed += OnBoardCompleted;
        Contents.AddChild(_board);
    }

    private void OnBoardCompleted()
    {
        Completed?.Invoke();
    }

    public void Setup(string torsoProto, IReadOnlyList<string> partProtos)
    {
        _board.Setup(torsoProto, partProtos);
    }

    public void CloseGame()
    {
        Close();
    }
}

/// <summary>
///     The play field: a torso target in the center and 4 limb pieces dragged with the mouse.
///     A line connects each piece to the torso and shortens as the piece is brought closer.
/// </summary>
public sealed class N14ForgeMiniGameBoard : Control
{
    private const float IconHalf = 44f;
    private const float AttachRadius = 112f;
    private const float AttachRestRadius = AttachRadius * 0.9f;
    private const float TorsoHalf = 48f;
    private const float HazardRadius = 15f;
    private const byte HazardCount = 60;

    private readonly SpriteSystem _sprite;

    private static readonly Color[] FallbackPalette =
    {
        Color.FromHex("#EF5350"),
        Color.FromHex("#42A5F5"),
        Color.FromHex("#66BB6A"),
        Color.FromHex("#FFA726"),
    };

    private Texture? _torsoTexture;
    private readonly List<Piece> _pieces = new();
    private readonly List<Vector2> _hazards = new();

    private bool _pendingScatter = true;
    private bool _pendingHazards = true;
    private int _dragging = -1;
    private Vector2 _dragOffset;
    private bool _completed;

    public event Action? Completed;

    public N14ForgeMiniGameBoard()
    {
        MouseFilter = MouseFilterMode.Stop;
        MinSize = new Vector2(600, 480);

        var entityManager = IoCManager.Resolve<IEntityManager>();
        _sprite = entityManager.System<SpriteSystem>();
    }

    public void Setup(string torsoProto, IReadOnlyList<string> partProtos)
    {
        _pieces.Clear();
        _hazards.Clear();
        _dragging = -1;
        _completed = false;
        _pendingScatter = true;
        _pendingHazards = true;

        _torsoTexture = GetIcon(torsoProto);
        var paletteIndex = 0;
        foreach (var proto in partProtos)
        {
            if (string.IsNullOrEmpty(proto))
                continue;

            var tex = GetIcon(proto);
            _pieces.Add(new Piece
            {
                Texture = tex,
                Proto = proto,
                FallbackColor = FallbackPalette[paletteIndex % FallbackPalette.Length],
            });
            paletteIndex++;
        }

        if (Size != Vector2.Zero)
        {
            PlaceHazards();
            _pendingHazards = false;
            Scatter();
        }

        UpdateDraw();
    }

    private Texture? GetIcon(string proto)
    {
        if (string.IsNullOrEmpty(proto))
            return null;

        try
        {
            return _sprite.GetPrototypeIcon(proto).Default;
        }
        catch
        {
            return null;
        }
    }

    private void Scatter()
    {
        if (Size == Vector2.Zero)
        {
            _pendingScatter = true;
            return;
        }

        var center = Size / 2;
        var maxRadius = Math.Max(220f, Math.Min(center.X, center.Y) - 90f);
        var random = new Random();
        var fallbackIndex = 0;

        foreach (var piece in _pieces)
        {
            var placed = false;
            for (var attempts = 0; attempts < 80; attempts++)
            {
                var angle = random.NextDouble() * Math.Tau;
                var radius = 270f + (float)random.NextDouble() * (maxRadius - 270f);
                var pos = center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
                var overlaps = false;
                foreach (var other in _pieces)
                {
                    if (other.Position != Vector2.Zero && (other.Position - pos).Length() < 130f)
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps)
                {
                    foreach (var hazard in _hazards)
                    {
                        if ((hazard - pos).Length() < HazardRadius + IconHalf)
                        {
                            overlaps = true;
                            break;
                        }
                    }
                }

                if (!overlaps)
                {
                    piece.Position = pos;
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                // Guaranteed spread around the ring even if random placement failed.
                var step = (float)(Math.Tau / _pieces.Count) * fallbackIndex + (float)(random.NextDouble() * 0.4);
                var radius = 285f + 24f * (fallbackIndex % 3);
                piece.Position = center + new Vector2((float)Math.Cos(step), (float)Math.Sin(step)) * radius;
                fallbackIndex++;
            }
        }

        _pendingScatter = false;
    }

    private void PlaceHazards()
    {
        _hazards.Clear();

        if (Size == Vector2.Zero)
            return;

        var center = Size / 2;
        var minRadius = AttachRadius + HazardRadius + 30f;
        var maxRadius = Math.Max(minRadius + 40f, Math.Min(center.X, center.Y) - 70f);
        var random = new Random();
        var golden = Math.PI * (3.0 - Math.Sqrt(5.0));

        for (var h = 0; h < HazardCount; h++)
        {
            var placed = false;
            for (var attempts = 0; attempts < 600; attempts++)
            {
                var angle = random.NextDouble() * Math.Tau;
                var radius = minRadius + (float)random.NextDouble() * (maxRadius - minRadius);
                var pos = center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;

                var conflict = false;
                foreach (var existing in _hazards)
                {
                    if ((existing - pos).Length() < 2f * HazardRadius + 16f)
                    {
                        conflict = true;
                        break;
                    }
                }

                if (!conflict)
                {
                    _hazards.Add(pos);
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                // Deterministic aerial spread so the minimum count is always reached.
                var a = h * golden + random.NextDouble() * 0.2;
                var r = minRadius + (maxRadius - minRadius) * (h / (float)HazardCount) * 0.95f;
                _hazards.Add(center + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * r);
            }
        }
    }

    private void ResetGame()
    {
        foreach (var piece in _pieces)
        {
            piece.Attached = false;
            piece.Position = Vector2.Zero;
        }

        _dragging = -1;
        _pendingScatter = true;
        _pendingHazards = false;
        PlaceHazards();
        UpdateDraw();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        handle.DrawRect(new UIBox2(0f, 0f, Size.X, Size.Y), Color.FromHex("#25214A"));

        if (Size != Vector2.Zero)
        {
            if (_pendingHazards)
            {
                PlaceHazards();
                _pendingHazards = false;
            }

            if (_pendingScatter)
            {
                Scatter();
                UpdateDraw();
            }
        }

        var center = Size / 2;

        // Torso (target) in the centre with a glint ring.
        if (_torsoTexture != null)
        {
            handle.DrawCircle(center, TorsoHalf + 12f, Color.FromHex("#FFD700").WithAlpha(0.18f));
            handle.DrawTextureRect(_torsoTexture,
                new UIBox2(center.X - TorsoHalf, center.Y - TorsoHalf, center.X + TorsoHalf, center.Y + TorsoHalf));
        }
        else
        {
            handle.DrawCircle(center, TorsoHalf, Color.FromHex("#B8860B").WithAlpha(0.5f));
        }

        // Hazard zones - dragging a piece into them resets the whole mini-game.
        foreach (var hazard in _hazards)
        {
            handle.DrawCircle(hazard, HazardRadius, Color.FromHex("#B71C1C").WithAlpha(0.32f));
            handle.DrawCircle(hazard, HazardRadius, Color.FromHex("#FF5252").WithAlpha(0.85f), filled: false);
            handle.DrawCircle(hazard, 5f, Color.FromHex("#FF5252").WithAlpha(0.9f));
        }

        // Connecting lines - they melt together with the torso as a piece is dragged closer.
        foreach (var piece in _pieces)
        {
            var dir = piece.Position - center;
            var normal = dir.LengthSquared() < 0.001f ? new Vector2(1f, 0f) : dir.Normalized();
            var from = center + normal * (TorsoHalf + 4f);
            var to = piece.Position - normal * IconHalf;

            var color = piece.Attached
                ? Color.FromHex("#43A047").WithAlpha(0.95f)
                : Color.FromHex("#9E9E9E").WithAlpha(0.75f);

            handle.DrawLine(from, to, color);
        }

        // Pieces on top.
        foreach (var piece in _pieces)
        {
            if (piece.Texture != null)
            {
                handle.DrawTextureRect(piece.Texture,
                    new UIBox2(piece.Position.X - IconHalf, piece.Position.Y - IconHalf,
                        piece.Position.X + IconHalf, piece.Position.Y + IconHalf));
            }
            else
            {
                // Fallback figure if the prototype has no icon.
                handle.DrawCircle(piece.Position, IconHalf, piece.FallbackColor.WithAlpha(0.9f));
                handle.DrawCircle(piece.Position, IconHalf, Color.Black.WithAlpha(0.6f), filled: false);
            }

            if (piece.Attached)
                handle.DrawCircle(piece.Position, IconHalf + 5f, Color.FromHex("#43A047").WithAlpha(0.28f));
        }
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIClick || _completed)
            return;

        var local = args.RelativePixelPosition;
        for (var i = 0; i < _pieces.Count; i++)
        {
            var piece = _pieces[i];
            if (piece.Attached)
                continue;

            if ((piece.Position - local).Length() <= IconHalf + 6f)
            {
                _dragging = i;
                _dragOffset = piece.Position - local;
                args.Handle();
                return;
            }
        }
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (_dragging == -1 || _completed)
            return;

        var piece = _pieces[_dragging];
        var pos = args.RelativePixelPosition + _dragOffset;
        pos = new Vector2(
            Math.Clamp(pos.X, IconHalf, Size.X - IconHalf),
            Math.Clamp(pos.Y, IconHalf, Size.Y - IconHalf));
        piece.Position = pos;

        var center = Size / 2;
        if (!piece.Attached && (piece.Position - center).Length() <= AttachRadius)
        {
            // Snapped onto the torso - lock in place.
            piece.Attached = true;
            var dir = piece.Position - center;
            piece.Position = center + (dir.LengthSquared() < 0.001f ? new Vector2(1f, 0f) : dir.Normalized()) * AttachRestRadius;
            _dragging = -1;
        }
        else if (!piece.Attached)
        {
            // Touched a hazard zone - the mini-game resets.
            foreach (var hazard in _hazards)
            {
                if ((hazard - piece.Position).Length() <= HazardRadius)
                {
                    ResetGame();
                    return;
                }
            }
        }

        UpdateDraw();

        if (!_completed && _pieces.TrueForAll(x => x.Attached))
        {
            _completed = true;
            Completed?.Invoke();
        }
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        _dragging = -1;
    }

    private sealed class Piece
    {
        public Texture? Texture;
        public Vector2 Position;
        public bool Attached;
        public string Proto = string.Empty;
        public Color FallbackColor;
    }
}
