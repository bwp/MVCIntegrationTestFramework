using System;
using System.IO;
using System.Web;
using System.Web.Hosting;
using MvcIntegrationTestFramework.Browsing;

namespace MvcIntegrationTestFramework.Hosting
{
    /// <summary>
    /// Simply provides a remoting gateway to execute code within the ASP.NET-hosting appdomain
    /// </summary>
    internal class AppDomainProxy : MarshalByRefObject
    {
        public void InitASPNET()
        {
            HttpRuntime.ProcessRequest(new SimpleWorkerRequest("/default.aspx", "", new StringWriter()));
        }

        public void RunCodeInAppDomain(Action codeToRun)
        {
            codeToRun();
        }

        public void RunBrowsingSessionInAppDomain(SerializableDelegate<Action<BrowsingSession>> script)
        {
            BrowsingSession browsingSession = BrowsingSession.Instance;

            if (browsingSession != null)
            {
                //if (browsingSession.Session != null)
                //{
                if (script != null)
                {
                    script.Delegate(browsingSession);
                }
                else
                {
                    throw new Exception("Script Delegate is null");
                }
                //}else
                //{
                //    throw new Exception("BrowsingSession Session is null");
                //}
            }
            else
            {
                throw new Exception("BrowsingSession is null");
            }
        }

        public FuncExecutionResult<TResult> RunBrowsingSessionInAppDomain<TResult>(SerializableDelegate<Func<BrowsingSession, TResult>> script)
        {
            //            var browsingSession = new BrowsingSession();
            var browsingSession = BrowsingSession.Instance;

            TResult local;

            if (browsingSession != null)
            {
                local = script.Delegate(browsingSession);
            }
            else
            {
                throw new Exception("BrowsingSession is null");
            }

            FuncExecutionResult<TResult> result = new FuncExecutionResult<TResult>();

            result.DelegateCalled = script;
            result.DelegateCallResult = local;

            return result;
        }

        public override object InitializeLifetimeService()
        {
            // This tells the CLR not to surreptitiously 
            // destroy this object -- it's a singleton
            // and will live for the life of the appdomain

            return null; // Tells .NET not to expire this remoting object
        }
    }
}