using System;
using System.Collections.Concurrent;

namespace ComicReader.Core.Services
{
    // Servicio temporal de registro simple (se reemplazará por Host DI en fases posteriores)
    public static class ServiceLocator
    {
        private static readonly ConcurrentDictionary<Type, object> _singletons = new();

        public static void RegisterSingleton<T>(T instance) where T : class
        {
            _singletons[typeof(T)] = instance;
        }

        public static T Get<T>() where T : class
        {
            if (_singletons.TryGetValue(typeof(T), out var obj))
                return (T)obj;
            throw new InvalidOperationException($"No service registered for type {typeof(T).FullName}");
        }

        public static T TryGet<T>() where T : class
        {
            if (_singletons.TryGetValue(typeof(T), out var obj))
                return (T)obj;
            return null;
        }
    }
}
