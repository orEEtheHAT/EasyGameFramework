using System;
using UnityEngine;

namespace EasyNarrative.UI
{
    public enum DialogueInterruptMode { Close, Resume, Redirect, KeepTalking }
    [Serializable]
    public sealed class DialogueInterruption
    {
        [Tooltip("Allow the listener to walk while reading the window.")] public bool allowMovement;
        [Min(0), Tooltip("Interrupt beyond this separation in world units. Zero disables the distance check.")] public float distance;
        public DialogueInterruptMode whenFar = DialogueInterruptMode.Close;
        public bool interruptOnDamage;
        public DialogueInterruptMode whenDamaged = DialogueInterruptMode.Close;
        public DialogueInterruptMode whenSpeakerLeaves = DialogueInterruptMode.Close;
        [Tooltip("Dialogue line ID for Redirect. A missing line closes the conversation.")] public string interruptLine;
    }
}
