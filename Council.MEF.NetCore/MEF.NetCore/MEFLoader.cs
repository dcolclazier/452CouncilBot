using System;
using System.Collections.Generic;
using System.Composition;
using System.Composition.Convention;
using System.Composition.Hosting;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MEF.NetCore
{
    /// <summary>
    /// Set this to true for unit testing
    /// </summary>
    public class MEFSkip
    {
        public static bool Skip { get; set; } = false;
    }
    /// <summary>
    /// https://dotnetthoughts.net/using-mef-in-dotnet-core
    /// </summary>
    public static class MEFLoader
    {
        //private static ServiceLoggerFactory _loggerFactory = new ServiceLoggerFactory();
        private static object _sync = new object();
        private static CompositionHost _container = null;
        private static Dictionary<string, object> _mockedObjects = new Dictionary<string, object>();

        private static readonly string _assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        public static bool IsInitialized
        {
            get
            {
                lock (_sync)
                {
                    return _container != null;
                }

            }
        }

        public static void Init()
        {
            lock (_sync)
            {
                var configuration = new ContainerConfiguration();
                if (IsInitialized)
                {
                    Dispose();
                }
                if (!MEFSkip.Skip)
                {
                    var startTime = DateTime.UtcNow;
                    var assemblyList = Directory.GetFiles(_assemblyPath, "DiscordBot*.dll").ToList();
                    assemblyList.AddRange(Directory.GetFiles(_assemblyPath, "AWS.Logging.dll"));
                    //assemblyList.AddRange(Directory.GetFiles(_assemblyPath, "MEF.NetCore.dll"));
                    var rules = new ConventionBuilder();
                    var currentAssembly = Assembly.GetExecutingAssembly().GetName();
                    foreach (var a in assemblyList)
                    {
                        try
                        {
                            Console.WriteLine("Trying to find exports from " + a);
                            var assembly = Assembly.LoadFrom(a);

                            var sharedExports = assembly
                                .GetTypes()
                                .Where(type => type.GetCustomAttribute<SharedAttribute>(true) is SharedAttribute)
                                .ToList();
                            foreach (var export in sharedExports)
                            {
                                rules.ForTypesDerivedFrom(export).Shared();
                            }

                            configuration.WithAssembly(assembly);
                        }
                        catch (Exception ex)
                        {
                            throw ex;
                        }

                    }
                    _container = configuration.CreateContainer();
                }
            }
        }

        private static IEnumerable<PropertyInfo> GetPropertyInfo(Type type, List<PropertyInfo> list)
        {
            list.AddRange(type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));

            if (type.BaseType == null) return list;
            return GetPropertyInfo(type.BaseType, list);
        }

        /// <summary>
        /// Import all dependencies for properties that have an [Import] attribute.
        /// </summary>
        /// <param name="obj"></param>
        public static void SatisfyImportsOnce(object obj)
        {
            lock (_sync)
            {
                if (!IsInitialized)
                {
                    Init();
                }

                foreach (var property in GetPropertyInfo(obj.GetType(), new List<PropertyInfo>()).Where(prop => Attribute.IsDefined(prop, typeof(ImportAttribute))))
                {
                    //check to see if we have a mocked object for this property
                    if (_mockedObjects.ContainsKey(property.PropertyType.ToString()))
                    {
                        property.SetValue(obj, _mockedObjects[property.PropertyType.ToString()]);
                    }
                    else
                    {
                        try
                        {
                            property.SetValue(obj, _container.GetExport(property.PropertyType));
                        }
                        catch (Exception)
                        {
                            var exports = string.Join(", ", _container.GetExports(property.PropertyType).Select(i => i.ToString()).ToList());
                            //logger.LogCritical($"Export not found... All exports: {exports}");
                        }
                    }
                }
            }
        }
        /// <summary>
        /// Use this to add mocked object into the MEFLoader. (Unit tests)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        public static void ComposeExportedValue<T>(object obj)
        {
            lock (_sync)
            {
                if (!IsInitialized)
                {
                    Init();
                }
                _mockedObjects[typeof(T).ToString()] = obj;
            }
        }

        private static void Dispose()
        {
            if (!IsInitialized) return;
            _container.Dispose();
            _container = null;
            _mockedObjects = new Dictionary<string, object>();
        }
    }

}
