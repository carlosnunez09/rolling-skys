using System;
using System.Reflection;
using NUnit.Framework;

public class EndpointParsingTests {
    const ushort DefaultPort = 7777;
    static readonly MethodInfo Parse = GameCode.Method(GameCode.Type("GameNetworkManager"), "TryParseEndpoint",
        typeof(string), typeof(ushort), typeof(string).MakeByRefType(), typeof(ushort).MakeByRefType());

    static bool TryParse (string endpoint, out string address, out ushort port) {
        object[] args = { endpoint, DefaultPort, null, (ushort)0 };
        bool ok = (bool)Parse.Invoke(null, args);
        address = (string)args[2];
        port = (ushort)args[3];
        return ok;
    }

    [TestCase("127.0.0.1", "127.0.0.1", DefaultPort)]
    [TestCase("192.168.1.20:9000", "192.168.1.20", (ushort)9000)]
    [TestCase("  10.0.0.5:7778  ", "10.0.0.5", (ushort)7778)]
    [TestCase("10.0.0.5;7778", "10.0.0.5", (ushort)7778)]
    [TestCase("udp://10.0.0.5:7778", "10.0.0.5", (ushort)7778)]
    [TestCase("udp://10.0.0.5:7778/", "10.0.0.5", (ushort)7778)]
    [TestCase("localhost", "127.0.0.1", DefaultPort)]
    [TestCase("LocalHost:8000", "127.0.0.1", (ushort)8000)]
    [TestCase("localhost;8000", "127.0.0.1", (ushort)8000)]
    [TestCase("[::1]", "::1", DefaultPort)]
    [TestCase("[::1]:9000", "::1", (ushort)9000)]
    [TestCase("::1", "::1", DefaultPort)]
    [TestCase("game.example.com:7000", "game.example.com", (ushort)7000)]
    public void ValidEndpoints_Parse (string endpoint, string expectedAddress, ushort expectedPort) {
        Assert.True(TryParse(endpoint, out string address, out ushort port), $"'{endpoint}' should parse");
        Assert.AreEqual(expectedAddress, address);
        Assert.AreEqual(expectedPort, port);
    }

    [TestCase((string)null)]
    [TestCase("")]
    [TestCase("   ")]
    public void EmptyEndpoint_DefaultsToLoopback (string endpoint) {
        Assert.True(TryParse(endpoint, out string address, out ushort port));
        Assert.AreEqual("127.0.0.1", address);
        Assert.AreEqual(DefaultPort, port);
    }

    [TestCase(":7777")]
    [TestCase("10.0.0.5:")]
    [TestCase("10.0.0.5:port")]
    [TestCase("10.0.0.5:70000")]
    [TestCase("10.0.0.5:-1")]
    [TestCase("[]:7777")]
    [TestCase("[::1]7777")]
    [TestCase("[::1]:abc")]
    public void InvalidEndpoints_AreRejected (string endpoint) {
        Assert.False(TryParse(endpoint, out _, out _), $"'{endpoint}' should be rejected");
    }
}
