using System;
using System.Net.Http;

namespace VoiceToTextPro.Services
{
    public static class HttpService
    {
        private static readonly Lazy<HttpClient> _lazyClient = new(() =>
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                MaxConnectionsPerServer = 64,
                EnableMultipleHttp2Connections = true,
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(60)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) VoiceToTextPro/3.0");
            return client;
        });

        public static HttpClient Client => _lazyClient.Value;
    }
}
