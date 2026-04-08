using Content.Shared._Forge.Photo;
using Robust.Client.Audio;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Audio.Sources;
using Robust.Shared.Map;
using System.Numerics;

namespace Content.Client._Forge.Photo.UI;

public sealed class PhotoCameraBoundUserInterface : BoundUserInterface
{
    private readonly EyeSystem _eyeSystem;
    private readonly PhotoSystem _photoSystem;
    private readonly TransformSystem _transform;

    [Dependency] private readonly IResourceCache _cache = default!;
    [Dependency] private readonly IAudioManager _audioManager = default!;

    [ViewVariables]
    private PhotoCameraWindow? _window;

    [ViewVariables]
    private EntityUid? _cameraEntity;

    private Vector2 _zoomPos = Vector2.Zero;
    private float _zoomValue = 1f;
    private bool _viewInitialized;

    private float _controlVolume;
    private IAudioSource? _controlSound;

    public PhotoCameraBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _eyeSystem = EntMan.System<EyeSystem>();
        _photoSystem = EntMan.System<PhotoSystem>();
        _transform = EntMan.System<TransformSystem>();
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<PhotoCameraWindow>();
        _viewInitialized = false;

        _window.OnTakeImageAttempt += AttemptTakeImage;

        if (!_cache.TryGetResource("/Audio/_Forge/Effects/servo_effect.ogg", out AudioResource? resource))
            return;

        var source = _audioManager.CreateAudioSource(resource);
        if (source == null)
            return;

        source.Global = true;
        source.Looping = true;
        source.Volume = float.NegativeInfinity;
        source.Restart();

        _controlSound = source;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (_window == null || state is not PhotoCameraUiState cast)
            return;

        _cameraEntity = EntMan.GetEntity(cast.CameraEntity);

        if (EntMan.TryGetComponent<PhotoCameraComponent>(_cameraEntity, out var component))
        {
            if (!_viewInitialized)
            {
                // Start slightly forward so the holder is not centered in every shot by default.
                _zoomPos = new Vector2(0f, component.ViewBox.Y * 0.35f);
                _zoomValue = Math.Clamp(0.8f, component.MinZoom, component.MaxZoom);
                _viewInitialized = true;
            }

            _photoSystem.OpenCameraUi(_cameraEntity.Value, this);
            UpdateControl(component, 1);
        }

        if (EntMan.TryGetComponent<EyeComponent>(_cameraEntity, out var eye))
        {
            _window.UpdateState(eye.Eye, cast.HasPaper);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (_cameraEntity != null)
        {
            _photoSystem.CloseCameraUi(_cameraEntity.Value);
            _cameraEntity = null;
        }

        _controlSound?.Dispose();
        _window?.OnDispose();
        _viewInitialized = false;
    }

    public void UpdateControl(PhotoCameraComponent component, float frameTime)
    {
        //This looks so bad
        if (_cameraEntity == null || _window == null)
            return;

        Vector2 pos = _zoomPos + _window.MoveInput * _zoomValue * frameTime;

        float zoom = Math.Clamp(_zoomValue + _window.ZoomInput * frameTime * (component.MaxZoom - component.MinZoom), component.MinZoom, component.MaxZoom);
        float zoomRatio = (zoom - component.MinZoom) / (component.MaxZoom - component.MinZoom);

        float xClamp = component.ViewBox.X * 0.5f * (1 - zoomRatio);
        float yClamp = component.ViewBox.Y * 0.5f * (1 - zoomRatio);
        pos.X = Math.Clamp(pos.X, -xClamp, xClamp);
        pos.Y = Math.Clamp(pos.Y, -yClamp, yClamp);

        var angle = _transform.GetWorldRotation(_cameraEntity.Value);
        var grid = _transform.GetGrid(_cameraEntity.Value);
        Angle localAngle = 0;
        if (grid != null)
            localAngle = angle - _transform.GetWorldRotation(grid.Value);

        var delta = new System.Numerics.Vector3(_zoomPos - pos, _zoomValue - zoom);
        _zoomPos = pos;
        _zoomValue = zoom;

        _window.ZoomInput = 0;

        var rotateAngle = angle.Opposite() - (localAngle - localAngle.RoundToCardinalAngle());

        _eyeSystem.SetOffset(_cameraEntity.Value, rotateAngle.RotateVec(pos));
        _eyeSystem.SetZoom(_cameraEntity.Value, new Vector2(zoom));
        _eyeSystem.SetRotation(_cameraEntity.Value, -rotateAngle);

        if (_controlSound == null)
            return;

        var targetVolume = delta != System.Numerics.Vector3.Zero ? 2f : -20f;
        _controlVolume = delta.Z != 0 ? 2f : _controlVolume;
        _controlVolume = Math.Clamp(_controlVolume + (targetVolume - _controlVolume) * frameTime, -20f, 2f);

        _controlSound.Volume = _controlVolume > -20f ? _controlVolume : float.NegativeInfinity;
    }

    private void AttemptTakeImage()
    {
        if (_window == null || _cameraEntity == null)
            return;

        _window.RenderImage(bytes =>
        {
            if (!EntMan.TryGetComponent<TransformComponent>(_cameraEntity, out var transform))
                return;

            // Compute actual world position: camera world pos + rotated offset
            var worldPos = _transform.GetWorldPosition(_cameraEntity.Value);
            var angle = _transform.GetWorldRotation(_cameraEntity.Value);
            var photoWorldPos = worldPos + angle.RotateVec(_zoomPos);

            var message = new PhotoCameraTakeImageMessage(bytes, new MapCoordinates(photoWorldPos, transform.MapID), _zoomValue);
            SendMessage(message);
        });
    }
}
