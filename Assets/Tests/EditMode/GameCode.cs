using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

/// Game code lives in Assembly-CSharp (no asmdef), which test assemblies can't reference,
/// so game types and members are resolved by name.
static class GameCode {
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    public static Type Type (string name) {
        Type type = System.Type.GetType($"{name}, Assembly-CSharp");
        Assert.NotNull(type, $"{name} not found in Assembly-CSharp");
        return type;
    }

    public static MethodInfo Method (Type type, string name, params Type[] parameters) {
        MethodInfo method = type.GetMethods(Any).FirstOrDefault(m =>
            m.Name == name && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters));
        Assert.NotNull(method, $"{type.Name}.{name}({string.Join(", ", parameters.Select(p => p.Name))}) not found");
        return method;
    }
}
