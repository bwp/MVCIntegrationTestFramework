using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Web;
using System.Web.Hosting;
using System.Web.Mvc;
using MvcIntegrationTestFramework.Browsing;
using MvcIntegrationTestFramework.Interception;

namespace MvcIntegrationTestFramework.Hosting
{
    /// <summary>
    /// Hosts an ASP.NET application within an ASP.NET-enabled .NET appdomain
    /// and provides methods for executing test code within that appdomain
    /// </summary>
    public class AppHost
    {
        private readonly AppDomainProxy _appDomainProxy; // The gateway to the ASP.NET-enabled .NET appdomain
        private static Hashtable _appEventHandlers;

        //
        // This is invoked by HttpRuntime.Dispose, when we unload an AppDomain
        // To reproduce this in action, touch "global.asax" while XSP is running.
        //
        public void Dispose()
        {
            HttpApplication app = GetApplicationInstance();

            FireEvent("Application_End", app, new object[] { new object(), EventArgs.Empty });
            app.Dispose();
        }

        public static Hashtable GetApplicationTypeEvents(Type type)
        {

            if (_appEventHandlers != null)
                return _appEventHandlers;

            _appEventHandlers = new Hashtable();
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static;

            MethodInfo[] methods = type.GetMethods(flags);
            foreach (MethodInfo m in methods)
            {
                if (m.DeclaringType != typeof(HttpApplication) && IsEventHandler(m))
                    AddEvent(m, _appEventHandlers);
            }

            return _appEventHandlers;
        }

        public static Hashtable GetApplicationTypeEvents(HttpApplication app)
        {

            if (_appEventHandlers != null)
                return _appEventHandlers;

            _appEventHandlers = new Hashtable();
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static;

            Type type = app.GetType();

            MethodInfo[] methods = type.GetMethods(flags);
            foreach (MethodInfo m in methods)
            {
                if (m.DeclaringType != typeof(HttpApplication) && IsEventHandler(m))
                    AddEvent(m, _appEventHandlers);
            }

            return _appEventHandlers;
        }

        public static void AddEvent(MethodInfo method, Hashtable appTypeEventHandlers)
        {
            string name = method.Name.Replace("_On", "_");
            if (appTypeEventHandlers[name] == null)
            {
                appTypeEventHandlers[name] = method;
                return;
            }

            MethodInfo oldMethod = appTypeEventHandlers[name] as MethodInfo;
            ArrayList list;
            if (oldMethod != null)
            {
                list = new ArrayList(4);
                list.Add(oldMethod);
                appTypeEventHandlers[name] = list;
            }
            else
                list = appTypeEventHandlers[name] as ArrayList;

            list.Add(method);
        }

        public static bool FireEvent(string methodName, object target, object[] args)
        {
            Hashtable possibleEvents = GetApplicationTypeEvents((HttpApplication)target);
            MethodInfo method = possibleEvents[methodName] as MethodInfo;
            if (method == null)
                return false;

            if (method.GetParameters().Length == 0)
                args = null;

            method.Invoke(target, args);

            return true;
        }

        public static bool IsEventHandler(MethodInfo m)
        {
            int pos = m.Name.IndexOf('_');
            if (pos == -1 || (m.Name.Length - 1) <= pos)
                return false;

            if (m.ReturnType != typeof(void))
                return false;

            ParameterInfo[] pi = m.GetParameters();
            int length = pi.Length;
            if (length == 0)
                return true;

            if (length != 2)
                return false;

            if (pi[0].ParameterType != typeof(object) ||
                pi[1].ParameterType != typeof(EventArgs))
                return false;

            return true;
        }

        private AppHost(string appPhysicalDirectory, string virtualDirectory = "/")
        {
            _appDomainProxy = (AppDomainProxy)ApplicationHost.CreateApplicationHost(typeof(AppDomainProxy), virtualDirectory, appPhysicalDirectory);
            //AppDomain.CurrentDomain.SetupInformation.ConfigurationFile
             // Run a bogus request through the pipeline to wake up ASP.NET and initialize everything
            _appDomainProxy.InitASPNET();
            
            _appDomainProxy.RunCodeInAppDomain(() =>
            {
                InitializeApplication();
                FilterProviders.Providers.Add(new InterceptionFilterProvider());
                LastRequestData.Reset();
            });
        }

        public void Start(Action<BrowsingSession> testScript)
        {
            var serializableDelegate = new SerializableDelegate<Action<BrowsingSession>>(testScript);
            _appDomainProxy.RunBrowsingSessionInAppDomain(serializableDelegate);
        }

        public TResult Start<TResult>(Func<BrowsingSession, TResult> testScript)
        {
            var serializableDelegate = new SerializableDelegate<Func<BrowsingSession, TResult>>(testScript);
            FuncExecutionResult<TResult> result = _appDomainProxy.RunBrowsingSessionInAppDomain(serializableDelegate);
            CopyFields<object>(result.DelegateCalled.Delegate.Target, testScript.Target);
            return result.DelegateCallResult;
        }

        private static void CopyFields<T>(T from, T to) where T : class
        {
            if ((from != null) && (to != null))
            {
                foreach (FieldInfo info in from.GetType().GetFields())
                {
                    info.SetValue(to, info.GetValue(from));
                }
            }
        }

        #region Initializing app & interceptors
        private static void InitializeApplication()
        {
            var appInstance = GetApplicationInstance();
            appInstance.PostRequestHandlerExecute += delegate
            {
                // Collect references to context objects that would otherwise be lost
                // when the request is completed
                if (LastRequestData.HttpSessionState == null)
                    LastRequestData.HttpSessionState = HttpContext.Current.Session;
                if (LastRequestData.Response == null)
                    LastRequestData.Response = HttpContext.Current.Response;
            };
            RefreshEventsList(appInstance);

            RecycleApplicationInstance(appInstance);
        }
        #endregion

        #region Reflection hacks
        private static readonly MethodInfo GetApplicationInstanceMethod;
        private static readonly MethodInfo RecycleApplicationInstanceMethod;

        static AppHost()
        {
            // Get references to some MethodInfos we'll need to use later to bypass nonpublic access restrictions
            var httpApplicationFactory = typeof(HttpContext).Assembly.GetType("System.Web.HttpApplicationFactory", true);
            GetApplicationInstanceMethod = httpApplicationFactory.GetMethod("GetApplicationInstance", BindingFlags.Static | BindingFlags.NonPublic);
            RecycleApplicationInstanceMethod = httpApplicationFactory.GetMethod("RecycleApplicationInstance", BindingFlags.Static | BindingFlags.NonPublic);
        }

        private static HttpApplication GetApplicationInstance()
        {
            var writer = new StringWriter();
            var workerRequest = new SimpleWorkerRequest("", "", writer);
            var httpContext = new HttpContext(workerRequest);
            return (HttpApplication)GetApplicationInstanceMethod.Invoke(null, new object[] { httpContext });
        }

        private static void RecycleApplicationInstance(HttpApplication appInstance)
        {
            RecycleApplicationInstanceMethod.Invoke(null, new object[] { appInstance });
        }

        private static void RefreshEventsList(HttpApplication appInstance)
        {
            object stepManager = typeof(HttpApplication).GetField("_stepManager", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(appInstance);
            object resumeStepsWaitCallback = typeof(HttpApplication).GetField("_resumeStepsWaitCallback", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(appInstance);
            var buildStepsMethod = stepManager.GetType().GetMethod("BuildSteps", BindingFlags.NonPublic | BindingFlags.Instance);
            buildStepsMethod.Invoke(stepManager, new[] { resumeStepsWaitCallback });
        }

        #endregion

        /// <summary>
        /// Creates an instance of the AppHost so it can be used to simulate a browsing session.
        /// </summary>
        /// <returns></returns>
        public static AppHost Simulate(string mvcProjectDirectory)
        {
          
            var mvcProjectPath = GetMvcProjectPath(mvcProjectDirectory);
            if (mvcProjectPath == null)
            {
                throw new ArgumentException(string.Format("Mvc Project Directory {0} not found", mvcProjectDirectory));
            }
            CopyDllFiles(mvcProjectPath);
            return new AppHost(mvcProjectPath);
        }

        public static AppHost Simulate(string mvcProjectDirectory, string altProjectDirectory)
        {

            var mvcProjectPath = GetMvcProjectPath(mvcProjectDirectory, altProjectDirectory);
            if (mvcProjectPath == null)
            {
                throw new ArgumentException(string.Format("Mvc Project Directory {0} not found", mvcProjectDirectory));
            }
            CopyDllFiles(mvcProjectPath);
            return new AppHost(mvcProjectPath);
        }

        private static void CopyDllFiles(string mvcProjectPath)
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            foreach (var file in Directory.GetFiles(baseDirectory, "*.dll"))
            {
                var destFile = Path.Combine(mvcProjectPath, "bin", Path.GetFileName(file));
                if (!File.Exists(destFile) || File.GetCreationTimeUtc(destFile) != File.GetCreationTimeUtc(file))
                {
                    File.Copy(file, destFile, true);
                }
            }
        }

        private static string GetMvcProjectPath(string mvcProjectName)
        {
            var mvcPath = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory + mvcProjectName);
            if (Directory.Exists(mvcPath))
            {
                return mvcPath;
            }

            throw new Exception("MVC WebSite path: " + mvcPath + " not found");

            //while (baseDirectory.Contains("\\"))
            //{
            //    baseDirectory = baseDirectory.Substring(0, baseDirectory.LastIndexOf("\\"));
            //    var mvcPath = Path.GetFullPath(baseDirectory + mvcProjectName);
            //    //var mvcPath = Path.Combine(baseDirectory, mvcProjectName);
            //    if (Directory.Exists(mvcPath))
            //    {
            //        return mvcPath;
            //    }
            //}
            //return null;
        }

        private static string GetMvcProjectPath(string mvcProjectName, string altProjectName)
        {
            string mvcPath = (Environment.MachineName.ToLower() == "pd-it-tfs05") ||
                             AppDomain.CurrentDomain.BaseDirectory.ToLower().Contains("teamcity")
                                 ? altProjectName
                                 : Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory + mvcProjectName);


            if (Directory.Exists(mvcPath))
            {
                return mvcPath;
            }

            throw new Exception("MVC WebSite path: " + mvcPath + " BaseDirectory " + AppDomain.CurrentDomain.BaseDirectory + " Machine " + Environment.MachineName);

        }
    }
}