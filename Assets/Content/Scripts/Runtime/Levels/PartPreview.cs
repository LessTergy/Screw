using System;
using System.Collections.Generic;
using UnityEngine;

namespace Screw.Levels
{
    internal sealed class PartPreview
    {
        private static Material _material;
        private readonly List<(MeshRenderer Renderer, Material[] Materials)> _originalMaterials
            = new List<(MeshRenderer Renderer, Material[] Materials)>();

        public void Show(Transform root)
        {
            if (_material == null)
            {
                _material = Resources.Load<Material>("PartPreview");
                if (_material == null)
                {
                    throw new InvalidOperationException("Missing PartPreview material in Resources.");
                }
            }

            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                var original = renderer.sharedMaterials;
                var preview = new Material[original.Length];
                for (var index = 0; index < original.Length; index++)
                {
                    if (original[index] != null)
                    {
                        preview[index] = _material;
                    }
                }

                _originalMaterials.Add((renderer, original));
                renderer.sharedMaterials = preview;
            }
        }

        public void Clear()
        {
            foreach (var (renderer, materials) in _originalMaterials)
            {
                if (renderer != null)
                {
                    renderer.sharedMaterials = materials;
                }
            }

            _originalMaterials.Clear();
        }
    }
}
