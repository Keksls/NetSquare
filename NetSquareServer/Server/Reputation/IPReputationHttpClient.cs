using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace NetSquare.Server
{
    /// <summary>
    /// Provides bounded HTTP downloads for reputation providers.
    /// </summary>
    internal static class IPReputationHttpClient
    {
        private static readonly HttpClient Client = CreateClient();

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

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            using (CancellationTokenSource timeout = new CancellationTokenSource(Math.Max(250, timeoutMilliseconds)))
            {
                request.Headers.TryAddWithoutValidation("Accept", "application/json,text/plain,text/html;q=0.8");
                request.Headers.TryAddWithoutValidation("User-Agent", "NetSquare-Server-IP-Reputation");

                if (headers != null)
                {
                    foreach (KeyValuePair<string, string> header in headers)
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                // Buffer the complete response under the per-request timeout before reading its text.
                using (HttpResponseMessage response = Client
                           .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token)
                           .ConfigureAwait(false)
                           .GetAwaiter()
                           .GetResult())
                {
                    response.EnsureSuccessStatusCode();
                    return response.Content
                        .ReadAsStringAsync()
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult();
                }
            }
        }

        /// <summary>
        /// Creates the shared HTTP client used by every reputation provider.
        /// </summary>
        /// <returns>A reusable client with response decompression enabled.</returns>
        private static HttpClient CreateClient()
        {
            // Keep one handler alive so repeated reputation checks reuse their underlying connections.
            HttpClientHandler handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            return new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }
    }
}
