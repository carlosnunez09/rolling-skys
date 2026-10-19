using System;
using System.Collections.Generic;

/// Assigns each connected client a stable spawn slot: the lowest slot that is
/// neither owned by another client nor physically blocked. Slots are released on
/// disconnect. When every slot is taken, assignment cycles through the slots.
public sealed class SpawnSlotAllocator {

    readonly Dictionary<ulong, int> _slotsByClient = new Dictionary<ulong, int>();
    int _acquireCount;

    public int Count => _slotsByClient.Count;

    public bool TryGetSlot (ulong clientId, out int slot) => _slotsByClient.TryGetValue(clientId, out slot);

    /// Returns the client's slot in [0, slotCount), or -1 when there are no slots.
    /// `isBlocked` lets the caller skip free slots that are currently occupied in the world.
    public int Acquire (ulong clientId, int slotCount, Func<int, bool> isBlocked = null) {
        if (slotCount <= 0) return -1;

        if (_slotsByClient.TryGetValue(clientId, out int existing) && existing < slotCount)
            return existing;

        var used = new HashSet<int>(_slotsByClient.Values);
        int chosen = -1;
        for (int i = 0; i < slotCount; i++) {
            if (used.Contains(i) || (isBlocked != null && isBlocked(i))) continue;
            chosen = i;
            break;
        }
        if (chosen < 0) chosen = _acquireCount % slotCount;

        _acquireCount++;
        _slotsByClient[clientId] = chosen;
        return chosen;
    }

    public bool Release (ulong clientId) => _slotsByClient.Remove(clientId);

    public void Clear () {
        _slotsByClient.Clear();
        _acquireCount = 0;
    }
}
