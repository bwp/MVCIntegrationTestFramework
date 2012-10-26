﻿using System;
using System.Collections.Specialized;
using System.IO;
using System.Web;
using System.Web.Mvc;
using System.Web.SessionState;
using MvcIntegrationTestFramework.Interception;

namespace MvcIntegrationTestFramework.Browsing
{
    public class BrowsingSession
    {
        public HttpSessionState Session { get; private set; }
        public HttpCookieCollection Cookies { get; private set; }

        private static BrowsingSession _instance;
        private static object __lock = new object();

        private BrowsingSession()
        {
            Cookies = new HttpCookieCollection();
        }

        public static BrowsingSession Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (__lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new BrowsingSession();
                        }
                    }
                }
                return _instance;
            }
        }

        public RequestResult Get(string url)
        {
            return ProcessRequest(url, HttpVerbs.Get, new NameValueCollection());
        }

        /// <summary>
        /// Sends a post to your url. Url should NOT start with a /
        /// </summary>
        /// <param name="url"></param>
        /// <param name="formData"></param>
        /// <example>
        /// <code>
        /// var result = Post("registration/create", new
        /// {
        ///     Form = new
        ///     {
        ///         InvoiceNumber = "10000",
        ///         AmountDue = "10.00",
        ///         Email = "chriso@innovsys.com",
        ///         Password = "welcome",
        ///         ConfirmPassword = "welcome"
        ///     }
        /// });
        /// </code>
        /// </example>
        public RequestResult Post(string url, object formData)
        {
            var formNameValueCollection = NameValueCollectionConversions.ConvertFromObject(formData);
            return ProcessRequest(url, HttpVerbs.Post, formNameValueCollection);
        }

        private RequestResult ProcessRequest(string url, HttpVerbs httpVerb = HttpVerbs.Get, NameValueCollection formValues = null)
        {
            return ProcessRequest(url, httpVerb, formValues, null);
        }

        private RequestResult ProcessRequest(string url, HttpVerbs httpVerb, NameValueCollection formValues, NameValueCollection headers)
        {
            if (url == null) throw new ArgumentNullException("url");

            // Fix up URLs that incorrectly start with / or ~/
            if (url.StartsWith("~/"))
                url = url.Substring(2);
            else if (url.StartsWith("/"))
                url = url.Substring(1);

            // Parse out the querystring if provided
            string query = "";
            int querySeparatorIndex = url.IndexOf("?", StringComparison.Ordinal);
            if (querySeparatorIndex >= 0)
            {
                query = url.Substring(querySeparatorIndex + 1);
                url = url.Substring(0, querySeparatorIndex);
            }

            // Perform the request
            LastRequestData.Reset();
            var output = new StringWriter();
            string httpVerbName = httpVerb.ToString().ToLower();
            var workerRequest = new SimulatedWorkerRequest(url, query, output, Cookies, httpVerbName, formValues, headers);
            HttpRuntime.ProcessRequest(workerRequest);

            // Capture the output
            AddAnyNewCookiesToCookieCollection();
            Session = LastRequestData.HttpSessionState;
            return new RequestResult
            {
                ResponseText = output.ToString(),
                ActionExecutedContext = LastRequestData.ActionExecutedContext,
                ResultExecutedContext = LastRequestData.ResultExecutedContext,
                Response = LastRequestData.Response,
            };
        }

        private void AddAnyNewCookiesToCookieCollection()
        {
            if (LastRequestData.Response == null)
                return;

            HttpCookieCollection lastResponseCookies = LastRequestData.Response.Cookies;
            if (lastResponseCookies == null)
                return;

            foreach (string cookieName in lastResponseCookies)
            {
                HttpCookie cookie = lastResponseCookies[cookieName];
                if (Cookies[cookieName] != null)
                    Cookies.Remove(cookieName);
                if ((cookie.Expires == default(DateTime)) || (cookie.Expires > DateTime.Now))
                    Cookies.Add(cookie);
            }
        }
    }
}