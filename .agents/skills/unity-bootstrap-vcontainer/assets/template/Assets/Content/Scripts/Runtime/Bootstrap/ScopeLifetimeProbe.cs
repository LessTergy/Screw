using System;
using UnityEngine;

namespace ProjectBootstrap
{
    internal sealed class ScopeLifetimeProbe : IDisposable
    {
        private readonly string _scopeName;

        public ScopeLifetimeProbe(string scopeName)
        {
            _scopeName = scopeName;
            Debug.Log($"[VContainer] {_scopeName} created.");
        }

        public void Dispose()
        {
            Debug.Log($"[VContainer] {_scopeName} disposed.");
        }
    }
}
