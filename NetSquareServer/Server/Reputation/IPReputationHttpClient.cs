using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace NetSquare.Server
{
    /// <summary>
    /// Provides bounded HTTP downloads for reputation providers.
    /// </summary>
    internal static class IPReputationHttpClient
    {
        /// <summary>
        /// Downloads a text resource with an explicit timeout and optional headers.
        /// </summary>
        /// <param name="url">HTTPS resource URL.</param>
        /// <param name="timeoutMilliseconds">Request timeout in milliseconds.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <returns>Response body text.</returns>
        public static string DownloadString(string url, int timeoutMilliseconds, IDictionary<string, string> headers = null)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("A reputation URL is required.", nameof(url));

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Accept = "application/json,text/plain,text/html;q=0.8";
            request.UserAgent = "NetSquare-Server-IP-Reputation";
            request.Timeout = Math.Max(250, timeoutMilliseconds);
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            if (headers != null)
            {
                foreach (KeyValuePair<string, string> header in headers)
                    request.Headers[header.Key] = header.Value;
            }

            // Dispose the response and stream immediately so background checks cannot leak sockets.
            using (WebResponse response = request.GetResponse())
            using (Stream responseStream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(responseStream))
                return reader.ReadToEnd();
        }
    }
}
