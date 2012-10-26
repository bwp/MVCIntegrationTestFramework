MVCIntegrationTestFramework
===========================

Host an ASP.NET MVC website in process instead of in IIS. This eliminates the need to deploy. It current supports MVC 4. 

         SAMPLES

        [TestMethod]
        public void HomeIndex_DemoTests()
        {
            AppHost.Start(browsingSession =>
            {
                RequestResult result = browsingSession.ProcessRequest("home/index");

                // Routing config should match "home" controller
                Assert.AreEqual("home", result.ActionExecutedContext.RouteData.Values["controller"]);

                // Should have rendered the "index" view
                ActionResult actionResult = result.ActionExecutedContext.Result;
                Assert.IsInstanceOfType(actionResult, typeof(ViewResult));
                Assert.AreEqual("index", ((ViewResult)actionResult).ViewName);

                // App should not have had an unhandled exception
                Assert.IsNull(result.ResultExecutedContext.Exception);
            });
        }

        [TestMethod]
        public void Root_Url_Renders_Index_View()
        {
            AppHost.Start(browsingSession =>
            {
                // Request the root URL
                RequestResult result = browsingSession.Get("");

                // Can make assertions about the ActionResult...
                var viewResult = (ViewResult)result.ActionExecutedContext.Result;
                Assert.AreEqual("Index", viewResult.ViewName);
                Assert.AreEqual("Welcome to ASP.NET MVC!", viewResult.ViewData["Message"]);

                // ... or can make assertions about the rendered HTML
                Assert.IsTrue(result.ResponseText.Contains("<!DOCTYPE html"));
            });
        }

        [TestMethod]
        public void WorkWithCookiesAndSession()
        {
            AppHost.Start(browsingSession =>
            {
                string url = "home/DoStuffWithSessionAndCookies";
                browsingSession.Get(url);

                // Can make assertions about cookies
                Assert.AreEqual("myval", browsingSession.Cookies["mycookie"].Value);

                // Can read Session as long as you've already made at least one request
                // (you can also write to Session from your test if you want)
                Assert.AreEqual(1, browsingSession.Session["myIncrementingSessionItem"]);

                // Session values persist within a browsingSession
                browsingSession.Get(url);
                Assert.AreEqual(2, browsingSession.Session["myIncrementingSessionItem"]);
                browsingSession.Get(url);
                Assert.AreEqual(3, browsingSession.Session["myIncrementingSessionItem"]);
            });
        }

        [TestMethod]
        public void LogInProcess()
        {
            string securedActionUrl = "/home/SecretAction";

            AppHost.Start(browsingSession =>
            {
                // First try to request a secured page without being logged in                
                RequestResult initialRequestResult = browsingSession.Get(securedActionUrl);
                string loginRedirectUrl = initialRequestResult.Response.RedirectLocation;
                Assert.IsTrue(loginRedirectUrl.StartsWith("/Account/LogOn"), "Didn't redirect to logon page");

                // Now follow redirection to logon page
                string loginFormResponseText = browsingSession.Get(loginRedirectUrl).ResponseText;
                string suppliedAntiForgeryToken = MvcUtils.ExtractAntiForgeryToken(loginFormResponseText);

                // Now post the login form, including the verification token
                RequestResult loginResult = browsingSession.Post(loginRedirectUrl, new
                {
                    UserName = "steve",
                    Password = "secret",
                    __RequestVerificationToken = suppliedAntiForgeryToken
                });
                string afterLoginRedirectUrl = loginResult.Response.RedirectLocation;
                Assert.AreEqual(securedActionUrl, afterLoginRedirectUrl, "Didn't redirect back to SecretAction");

                // Check that we can now follow the redirection back to the protected action, and are let in
                RequestResult afterLoginResult = browsingSession.Get(securedActionUrl);
                Assert.AreEqual("Hello, you're logged in as steve", afterLoginResult.ResponseText);
            });
        }