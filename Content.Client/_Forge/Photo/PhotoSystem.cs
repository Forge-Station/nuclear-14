using Content.Client._Forge.Photo.UI;
using Content.Shared._Forge.Photo;

namespace Content.Client._Forge.Photo;

public sealed partial class PhotoSystem : SharedPhotoSystem
{
    private readonly Dictionary<EntityUid, PhotoCameraBoundUserInterface> _activeCameras = new();

    public override void Initialize()
    {
        base.Initialize();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_activeCameras.Count == 0)
            return;

        List<EntityUid>? toRemove = null;

        foreach (var (uid, window) in _activeCameras)
        {
            if (!TryComp<PhotoCameraComponent>(uid, out var component))
            {
                toRemove ??= new List<EntityUid>();
                toRemove.Add(uid);
                continue;
            }

            window.UpdateControl(component, frameTime);
        }

        if (toRemove != null)
        {
            foreach (var uid in toRemove)
                _activeCameras.Remove(uid);
        }
    }

    public void OpenCameraUi(EntityUid uid, PhotoCameraBoundUserInterface window)
    {
        _activeCameras.TryAdd(uid, window);
    }

    public void CloseCameraUi(EntityUid uid)
    {
        _activeCameras.Remove(uid);
    }
}
