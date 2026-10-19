using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

public class SpawnSlotAllocatorTests {
    static readonly Type AllocatorType = GameCode.Type("SpawnSlotAllocator");
    static readonly MethodInfo AcquireMethod = GameCode.Method(AllocatorType, "Acquire", typeof(ulong), typeof(int), typeof(Func<int, bool>));
    static readonly MethodInfo ReleaseMethod = GameCode.Method(AllocatorType, "Release", typeof(ulong));
    static readonly MethodInfo ClearMethod = GameCode.Method(AllocatorType, "Clear");

    const int Slots = 8;
    object _allocator;

    [SetUp]
    public void SetUp () => _allocator = Activator.CreateInstance(AllocatorType);

    int Acquire (ulong client, int slotCount = Slots, Func<int, bool> isBlocked = null) =>
        (int)AcquireMethod.Invoke(_allocator, new object[] { client, slotCount, isBlocked });

    bool Release (ulong client) => (bool)ReleaseMethod.Invoke(_allocator, new object[] { client });

    [Test]
    public void ClientsGetLowestFreeSlotsInOrder () {
        for (ulong client = 0; client < Slots; client++)
            Assert.AreEqual((int)client, Acquire(client));
    }

    [Test]
    public void SameClient_KeepsItsSlot () {
        Acquire(0);
        int slot = Acquire(1);
        Acquire(2);
        Assert.AreEqual(slot, Acquire(1));
        Assert.AreEqual(slot, Acquire(1));
    }

    [Test]
    public void ReleasedSlot_IsReusedByNextClient () {
        Acquire(10); Acquire(11); Acquire(12);
        Assert.True(Release(11));
        Assert.AreEqual(1, Acquire(13), "lowest free slot should be the released one");
        Assert.AreEqual(3, Acquire(14));
    }

    [Test]
    public void ReleasingUnknownClient_ReturnsFalse () {
        Assert.False(Release(42));
    }

    [Test]
    public void BlockedSlots_AreSkipped () {
        var blocked = new HashSet<int> { 0, 2 };
        Assert.AreEqual(1, Acquire(0, Slots, blocked.Contains));
        Assert.AreEqual(3, Acquire(1, Slots, blocked.Contains));
        Assert.AreEqual(4, Acquire(2, Slots, blocked.Contains));
    }

    [Test]
    public void NoSlots_ReturnsMinusOne () {
        Assert.AreEqual(-1, Acquire(0, 0));
    }

    [Test]
    public void AllSlotsTaken_CyclesInsteadOfFailing () {
        for (ulong client = 0; client < Slots; client++) Acquire(client);
        Assert.AreEqual(0, Acquire(100));
        Assert.AreEqual(1, Acquire(101));
    }

    [Test]
    public void AllSlotsBlocked_StillReturnsAValidSlot () {
        int slot = Acquire(0, 3, _ => true);
        Assert.That(slot, Is.InRange(0, 2));
    }

    [Test]
    public void SlotOutsideShrunkRange_IsReassigned () {
        for (ulong client = 0; client < 5; client++) Acquire(client);
        Release(0);
        Assert.AreEqual(0, Acquire(4, 3), "client 4 held slot 4, which no longer exists with 3 slots");
    }

    [Test]
    public void Clear_ForgetsAllClients () {
        Acquire(0); Acquire(1);
        ClearMethod.Invoke(_allocator, null);
        Assert.AreEqual(0, Acquire(1));
        Assert.AreEqual(1, Acquire(0));
    }
}
