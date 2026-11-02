using System;
using System.Collections.Generic;
using UnityEngine;

namespace MainProjeto.Scripts.Protoboard_Interactor
{
    // Guarda as celulas da entrada; a saida independe da posicao atual da peca.
    public sealed class ProtoboardOccupancyMap<T> where T : class
    {
        private readonly Dictionary<Vector3Int, T> owners = new Dictionary<Vector3Int, T>();
        private readonly Dictionary<T, List<Vector3Int>> footprints = new Dictionary<T, List<Vector3Int>>();

        public void Reserve(T owner, IEnumerable<Vector3Int> cells)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            var snapshot = new List<Vector3Int>(cells);
            Release(owner);
            footprints[owner] = snapshot;
            foreach (var cell in snapshot) owners[cell] = owner;
        }

        public bool IsFree(Vector3Int cell, T candidate, Predicate<T> isDestroyed = null)
        {
            if (!owners.TryGetValue(cell, out var owner)) return true;
            if (isDestroyed != null && isDestroyed(owner)) { Release(owner); return true; }
            return ReferenceEquals(owner, candidate);
        }

        public void Release(T owner)
        {
            if (owner == null || !footprints.TryGetValue(owner, out var cells)) return;
            foreach (var cell in cells)
                if (owners.TryGetValue(cell, out var existing) && ReferenceEquals(existing, owner)) owners.Remove(cell);
            footprints.Remove(owner);
        }

        public void Clear() { owners.Clear(); footprints.Clear(); }
    }
}
