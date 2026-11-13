using System;
using UnityEngine;

namespace EasySettings
{
    /// <summary>Optional owner of a player's base vertical FOV, independent of the output-camera lens.
    /// Read excludes gameplay offsets. All methods run on Unity's main thread.</summary>
    public interface ISettingsFieldOfViewProvider
    {
        bool TryRead(out float degrees, out string reason);
        ISettingsFieldOfViewOverride Acquire(UnityEngine.Object owner, float degrees);
    }

    /// <summary>Releasing this handle removes only this settings request, preserving other owners/offsets.</summary>
    public interface ISettingsFieldOfViewOverride : IDisposable
    {
        bool IsActive { get; }
        bool TrySet(float degrees);
    }
}
