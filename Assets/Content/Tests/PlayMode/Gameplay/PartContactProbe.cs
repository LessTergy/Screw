using System.Collections.Generic;
using UnityEngine;

namespace Screw.Tests.Gameplay
{
    public sealed class PartContactProbe : MonoBehaviour
    {
        public HashSet<Collider> Contacts { get; } = new HashSet<Collider>();

        private void OnCollisionEnter(Collision collision)
        {
            Contacts.Add(collision.collider);
        }
    }
}
