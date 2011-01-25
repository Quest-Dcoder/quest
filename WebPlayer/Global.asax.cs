﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Security;
using System.Web.SessionState;

namespace WebPlayer
{
    public class Global : System.Web.HttpApplication
    {

        protected void Application_Start(object sender, EventArgs e)
        {

        }

        protected void Session_Start(object sender, EventArgs e)
        {

        }

        protected void Application_BeginRequest(object sender, EventArgs e)
        {

        }

        protected void Application_AuthenticateRequest(object sender, EventArgs e)
        {

        }

        protected void Application_Error(object sender, EventArgs e)
        {

        }

        protected void Session_End(object sender, EventArgs e)
        {
            // If the session ends, we want to terminate any threads that may be waiting
            // for e.g. a menu reponse

            var gamesInSession = (Dictionary<string, PlayerHandler>)Session["Games"];
            if (gamesInSession != null)
            {
                foreach (PlayerHandler handler in gamesInSession.Values)
                {
                    handler.EndGame();
                }
            }
        }

        protected void Application_End(object sender, EventArgs e)
        {

        }
    }
}