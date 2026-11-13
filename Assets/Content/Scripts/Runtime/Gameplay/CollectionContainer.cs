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
        [SerializeField] private SpriteRenderer _lid;

        public Transform Panel
        {
            get { return _panel; }
        }

        public SpriteRenderer Lid
        {
            get { return _lid; }
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
            _lid.gameObject.SetActive(false);
            _panel.gameObject.SetActive(groupId.HasValue);
            if (groupId.HasValue)
            {
                _body.color = catalog.GetColor(palette[groupId.Value]).Primary.color;
                _lid.color = _body.color;
            }
        }

        public void RecordArrival()
        {
            Count++;
        }
    }
}
