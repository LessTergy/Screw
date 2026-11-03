using System.Collections.Generic;
using Screw.Levels;
using TMPro;
using UnityEngine;

namespace Screw.Gameplay
{
    public sealed class CollectionContainer : MonoBehaviour
    {
        [SerializeField] private Transform _panel;
        [SerializeField] private Transform[] _slots;
        [SerializeField] private Renderer _body;
        [SerializeField] private TMP_Text _counter;

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
            _counter.text = "0/3";
            _panel.gameObject.SetActive(groupId.HasValue);
            if (groupId.HasValue)
            {
                _body.sharedMaterial = catalog.GetColor(palette[groupId.Value]).Primary;
            }
        }

        public void RecordArrival()
        {
            Count++;
            _counter.text = Count + "/3";
        }
    }
}
