using System;
using System.Linq;
using System.Reflection;

namespace DVRAT
{
    /// <summary>Calls into other mods the tablet has no reference to.</summary>
    internal static class ModReflection
    {
        /// <summary>Public static method as a delegate, much cheaper per call than Invoke. Null when type or method is missing.</summary>
        public static T Method<T>(Type type, string name) where T : Delegate
        {
            var parameters = typeof(T).GetMethod("Invoke").GetParameters().Select(p => p.ParameterType).ToArray();
            var method = type?.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, parameters, null);
            return method == null ? null : (T)Delegate.CreateDelegate(typeof(T), method, false);
        }
    }
}
