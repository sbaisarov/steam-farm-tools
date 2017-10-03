﻿using System.IO;
using System.Net;
using System.Text;

namespace Shatulsky_Farm {
    static class Request {
        public static string FilePath { get; private set; }

        public static string getResponse(string uri, string cookies = "") {
            System.Net.WebClient web = new System.Net.WebClient();
            web.Encoding = UTF8Encoding.UTF8;
            web.Headers.Add(HttpRequestHeader.UserAgent, "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/60.0.3112.113 Safari/537.36");
            if (cookies != "")
                web.Headers.Add(HttpRequestHeader.Cookie, cookies);
            string html = web.DownloadString(uri);
            return html;
        }
        public static bool DownloadFile(string url, string cookies, string filename) {
            try {
                // Construct HTTP request to get the file

                var client = new WebClient();

                client.Headers.Add(HttpRequestHeader.Accept, "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8");
                client.Headers.Add(HttpRequestHeader.UserAgent, "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/60.0.3112.113 Safari/537.36");
                client.Headers.Add(HttpRequestHeader.Cookie, cookies);

                client.DownloadFile(url, $"{filename}");

                return true;
            } catch {
                return false;
            }
        }

        public static string POST(string Url, string postData, out string[] setCookies) {
            var request = (HttpWebRequest)WebRequest.Create(Url);

            var data = Encoding.ASCII.GetBytes(postData);

            request.Method = "POST";
            request.UserAgent = "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/60.0.3112.113 Safari/537.36";
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = data.Length;

            using (var stream = request.GetRequestStream()) {
                stream.Write(data, 0, data.Length);
            }

            var response1 = (HttpWebResponse)request.GetResponse();

            var returnValue = new StreamReader(response1.GetResponseStream()).ReadToEnd();
            setCookies = response1.Headers.GetValues("Set-Cookie");


            return returnValue;
        }
    }
}
