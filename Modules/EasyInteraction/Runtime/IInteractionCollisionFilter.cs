using System;
using UnityEngine;

namespace EasyInteraction
{
    /// <summary>Additional character queries that must ignore a held body. The caller owns the returned lease.</summary>
    public interface IInteractionCollisionFilter
    {
        IDisposable AcquireCollisionExclusion(Rigidbody body);
    }
}
