using System;
using System.Collections.Generic;
using Unity.Cinemachine;

namespace EasyMovement.FirstPerson
{
    public partial class FirstPersonCharacterController
    {
        /// <summary>Clears and fills a caller-owned list with distinct configured gait views, including inactive custom views.</summary>
        public void GetViewCameras(List<CinemachineCamera> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            AddView(destination, defaultCamera);
            AddView(destination, crouchedCamera);
            AddView(destination, sprintCamera);
            if (customStates == null) return;
            for (int i = 0; i < customStates.Length; ++i)
                if (customStates[i] != null) AddView(destination, customStates[i].view);
        }

        private static void AddView(List<CinemachineCamera> destination, CinemachineCamera view)
        {
            if (view && !destination.Contains(view)) destination.Add(view);
        }
    }
}
