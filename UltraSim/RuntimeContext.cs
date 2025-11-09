
#nullable enable

using System;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UltraSim;

namespace UltraSim
{
    /// <summary>
    /// Holds runtime and build-level metadata for the active UltraSim instance.
    /// </summary>
    public sealed class RuntimeContext
    {
        public string ContextName { get; init; } = "Server";
        public DateTime StartTimeUtc { get; } = DateTime.UtcNow;
        public Version EngineVersion { get; init; } = new(1, 0, 0, 0);

        public HostEnvironment Environment { get; }
        public Assembly[] Assemblies { get; private set; }
        private readonly List<MethodInfo> _Scratch = new();

        public BuildInfo Build { get; init; }

        public RuntimeContext(HostEnvironment env, string context)
        {
            Environment = env;
            ContextName = context;
            Assemblies = AppDomain.CurrentDomain.GetAssemblies();
            Build = BuildInfo.Current;
        }

        public void RefreshAssemblies() =>
            Assemblies = AppDomain.CurrentDomain.GetAssemblies();


        // --- Reflection Invocation ---


        private readonly string[] _NamespaceWhitelist =
        {
            "UltraSim",
            "Server",
            "Client"
        };

        public void InvokeMethod(string name) { InvokeMethod(name, ContextName); }

        public void InvokeMethod(string name, string context)
        {
            _Scratch.Clear();

            foreach (var asm in Assemblies.AsSpan())
            {
                foreach (var type in asm.GetTypes().AsSpan())
                {
                    if (!IsNamespaceAllowed(type))
                        continue;

                    foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).AsSpan())
                    {
                        if (!method.Name.Equals(name, StringComparison.Ordinal))
                            continue;

                        var ctxAttr = method.GetCustomAttribute<ContextAttribute>();
                        if (ctxAttr != null && !ctxAttr.Context.Equals(context, StringComparison.OrdinalIgnoreCase))
                            continue;

                        _Scratch.Add(method);
                    }
                }
            }

            _Scratch.Sort(new CallPriorityComparer());

            foreach (var method in CollectionsMarshal.AsSpan(_Scratch))
            {
                try { method.Invoke(null, null); }
                catch (Exception ex)
                {
                    Logging.Log($"[UltraSim.RuntimeContext] Error invoking {method.DeclaringType?.FullName}.{method.Name}: {ex}");
                }
            }
        }

        public void InvokeAttribute<T>() where T : Attribute
        {
            _Scratch.Clear();

            foreach (var asm in Assemblies.AsSpan())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types!;
                }

                foreach (var type in types.AsSpan())
                {
                    if (type == null || !IsNamespaceAllowed(type))
                        continue;

                    foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).AsSpan())
                    {
                        if (method.GetCustomAttribute<T>() == null)
                            continue;

                        var ctxAttr = method.GetCustomAttribute<ContextAttribute>();
                        if (ctxAttr != null && !ctxAttr.Context.Equals(ContextName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        _Scratch.Add(method);
                    }
                }
            }

            _Scratch.Sort(new CallPriorityComparer());

            foreach (var method in CollectionsMarshal.AsSpan(_Scratch))
            {
                try { method.Invoke(null, null); }
                catch (Exception ex)
                {
                    Logging.Log($"[UltraSim.RuntimeContext] Error invoking {method.DeclaringType?.FullName}.{method.Name}: {ex}");
                }
            }
        }

        private bool IsNamespaceAllowed(Type t)
        {
            if (t.Namespace == null)
                return false;

            foreach (var prefix in _NamespaceWhitelist.AsSpan())
            {
                if (t.Namespace.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Optional static build metadata — typically injected at compile time.
    /// </summary>
    public sealed class BuildInfo
    {
        public string Branch { get; init; } = "main";
        public string Commit { get; init; } = "unknown";
        public string BuildTimestamp { get; init; } = DateTime.UtcNow.ToString("u");

        public static BuildInfo Current => new(); // Could read from file or generated consts
    }

    // --- Sorting Helpers & Attributes ---

    public sealed class CallPriorityComparer : IComparer<MethodInfo>
    {
        public int Compare(MethodInfo? x, MethodInfo? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return 1;
            if (y == null) return -1;

            var xPriority = GetPriority(x);
            var yPriority = GetPriority(y);
            return xPriority.CompareTo(yPriority);
        }

        private static int GetPriority(MethodInfo mi)
        {
            var attr = mi.GetCustomAttribute<CallPriorityAttribute>();
            return attr?.Priority ?? 0;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class CallPriorityAttribute : Attribute
    {
        public int Priority { get; }
        public CallPriorityAttribute(int priority) => Priority = priority;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AutoConfigureAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AutoInitializeAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AutoEventSubscribeAttribute : Attribute
    {
        public string EventName { get; }
        public AutoEventSubscribeAttribute(string eventName) => EventName = eventName;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RequireSystemAttribute : Attribute
    {
        public string RequiredSystem { get; }
        public RequireSystemAttribute(string fullName) => RequiredSystem = fullName;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ContextAttribute : Attribute
    {
        public string Context { get; }
        public ContextAttribute(string context) => Context = context;
    }
}
