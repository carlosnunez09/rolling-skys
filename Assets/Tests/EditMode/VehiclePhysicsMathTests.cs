using System;
using System.Reflection;
using NUnit.Framework;

public class VehiclePhysicsMathTests {
    static readonly Type MathType = GameCode.Type("VehiclePhysicsMath");
    static readonly MethodInfo GripMethod = GameCode.Method(MathType, "GripFraction", typeof(float), typeof(float));
    static readonly MethodInfo BrakeMethod = GameCode.Method(MathType, "BrakingAcceleration", typeof(float), typeof(float), typeof(float));
    const float Eps = 1e-5f;

    static float Grip (float gripAt50Hz, float dt) => (float)GripMethod.Invoke(null, new object[] { gripAt50Hz, dt });
    static float Brake (float speed, float decel, float dt) => (float)BrakeMethod.Invoke(null, new object[] { speed, decel, dt });

    [TestCase(0f)]
    [TestCase(0.25f)]
    [TestCase(0.8f)]
    [TestCase(1f)]
    public void Grip_At50Hz_MatchesTuning (float grip) {
        Assert.AreEqual(grip, Grip(grip, 0.02f), Eps);
    }

    [TestCase(0.3f, 0.01f)]
    [TestCase(0.6f, 1f / 120f)]
    [TestCase(0.9f, 1f / 30f)]
    public void Grip_IsTickRateIndependent (float grip, float dt) {
        // Applying the fraction twice over dt must equal applying it once over 2*dt.
        float twoSteps = 1f - (1f - Grip(grip, dt)) * (1f - Grip(grip, dt));
        Assert.AreEqual(Grip(grip, 2f * dt), twoSteps, Eps);
    }

    [Test]
    public void Grip_StaysInUnitRange () {
        Assert.AreEqual(1f, Grip(1.5f, 0.02f), Eps, "grip above 1 clamps");
        Assert.AreEqual(0f, Grip(-0.5f, 0.02f), Eps, "negative grip clamps");
        Assert.AreEqual(0f, Grip(0.5f, 0f), Eps, "no time, no grip");
        Assert.AreEqual(0f, Grip(0.5f, -0.02f), Eps, "negative dt treated as zero");
        Assert.That(Grip(0.99f, 1f), Is.InRange(0f, 1f), "large dt never overshoots");
    }

    [TestCase(20f)]
    [TestCase(-20f)]
    public void Brake_OpposesMotion (float speed) {
        float a = Brake(speed, 10f, 0.02f);
        Assert.AreEqual(-Math.Sign(speed) * 10f, a, Eps);
    }

    [TestCase(0.1f, 50f, 0.02f)]
    [TestCase(-0.1f, 50f, 0.02f)]
    [TestCase(3f, 1000f, 1f / 60f)]
    public void Brake_NeverReversesDirection (float speed, float decel, float dt) {
        float next = speed + Brake(speed, decel, dt) * dt;
        Assert.AreEqual(0f, next, Eps, "braking should stop the car exactly, not push it backwards");
    }

    [Test]
    public void Brake_EdgeCases () {
        Assert.AreEqual(0f, Brake(0f, 10f, 0.02f), Eps, "stationary car gets no braking force");
        Assert.AreEqual(0f, Brake(5f, -10f, 0.02f), Eps, "negative deceleration request is ignored");
        Assert.False(float.IsNaN(Brake(5f, 10f, 0f)) || float.IsInfinity(Brake(5f, 10f, 0f)), "dt = 0 must not divide by zero");
    }
}
