using UnityEngine;
using System;

namespace Crystal
{
    public class SafeAreaDemo : MonoBehaviour
    {
        [SerializeField] KeyCode KeySafeArea = KeyCode.A;
        [SerializeField] SafeArea.SimDevice StartupSim = SafeArea.SimDevice.None;
        SafeArea.SimDevice[] Sims;
        int SimIdx;

        void Awake ()
        {
            if (!Application.isEditor)
                Destroy (this);

            Sims = (SafeArea.SimDevice[])Enum.GetValues (typeof (SafeArea.SimDevice));

            if (StartupSim != SafeArea.SimDevice.None)
            {
                Debug.Log ("Setting startup safe area sim device");
                SetSafeArea (StartupSim);
            }
        }

        void Update ()
        {
            if (Input.GetKeyDown (KeySafeArea))
                ToggleSafeArea ();
        }

        /// <summary>
        /// Toggle the safe area simulation device.
        /// </summary>
        public void ToggleSafeArea ()
        {
            SimIdx++;

            if (SimIdx >= Sims.Length)
                SimIdx = 0;

            SafeArea.Sim = Sims[SimIdx];
            Debug.LogFormat ("Switched to sim device {0} with debug key '{1}'", Sims[SimIdx], KeySafeArea);
        }

        /// <summary>
        /// Set a specific safe area simulation device.
        /// </summary>
        public void SetSafeArea (SafeArea.SimDevice sim)
        {
            SafeArea.Sim = sim;
            Debug.LogFormat ("Switched to sim device {0}", sim);
        }
    }
}
