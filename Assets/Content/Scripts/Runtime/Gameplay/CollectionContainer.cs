using System.Collections.Generic;
using Screw.Levels;
using UnityEngine;

namespace Screw.Gameplay
{
    public sealed class CollectionContainer : MonoBehaviour
    {
        [SerializeField] private Transform _panel;
        [SerializeField] private Transform[] _slots;
        [SerializeField] private SpriteRenderer _body;

        public Transform Panel
        {
            get { return _panel; }
        }

        public IReadOnlyList<Transform> Slots
        {
            get { return _slots; }
        }

        public int? GroupId { get; private set; }
        public int Count { get; private set; }

        public void Configure(int? groupId, LevelContentCatalog catalog, IReadOnlyList<int> palette)
        {
            GroupId = groupId;
            Count = 0;
            _panel.gameObject.SetActive(groupId.HasValue);
            if (groupId.HasValue)
            {
                _body.color = catalog.GetColor(palette[groupId.Value]).Primary.color;
            }
        }

        public void RecordArrival()
        {
            Count++;
        }
    }
}
