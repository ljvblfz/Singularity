//------------------------------------------------------------------------------
// <copyright company='Microsoft Corporation'>
//   Copyright (c) Microsoft Corporation.  All rights reserved.
//   Information Contained Herein is Proprietary and Confidential.
// </copyright>
//------------------------------------------------------------------------------

namespace Microsoft.VisualStudio.WebHost {
    using System;
    using System.Collections;
    using System.Globalization;
    using System.Text;
    using System.Web;

    internal class Messages {

        private const String _httpErrorFormat1 =
@"<html>
    <head>
        <title>{0}</title>
";

        public static String VersionString = "1.0"; //typeof(Server).Assembly.GetName().Version.ToString();

        private const String _httpStyle =
@"        <style>
            body {font-family:""Verdana"";font-weight:normal;font-size: 8pt;color:black;}
            p {font-family:""Verdana"";font-weight:normal;color:black;margin-top: -5px}
            b {font-family:""Verdana"";font-weight:bold;color:black;margin-top: -5px}
            h1 { font-family:""Verdana"";font-weight:normal;font-size:18pt;color:red }
            h2 { font-family:""Verdana"";font-weight:normal;font-size:14pt;color:maroon }
            pre {font-family:""Lucida Console"";font-size: 8pt}
            .marker {font-weight: bold; color: black;text-decoration: none;}
            .version {color: gray;}
            .error {margin-bottom: 10px;}
            .expandable { text-decoration:underline; font-weight:bold; color:navy; cursor:hand; }
        </style>
";

        private static String _httpErrorFormat2 =
@"    </head>
    <body bgcolor=""white"">

            <span><h1>Server Error in '{0}' Application.<hr width=100% size=1 color=silver></h1>

            <h2> <i>HTTP Error {1} - {2}.</i> </h2></span>

            <hr width=100% size=1 color=silver>

            <b>Version Information:</b>&nbsp;Visual Web Developer Web Server " + VersionString + @"

            </font>

    </body>
</html>
";

        public static String FormatErrorMessageBody(int statusCode, String appName) {
            string desc = HttpWorkerRequest.GetStatusDescription(statusCode);

            return String.Format(_httpErrorFormat1, new object[] {desc}) +
                   _httpStyle +
                   String.Format(_httpErrorFormat2, new object[] {appName, statusCode, desc});
        }
    }
}
