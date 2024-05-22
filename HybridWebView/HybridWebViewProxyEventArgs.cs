namespace HybridWebView
{
    /// <summary>
    /// Event arg object for a proxy request from the <see cref="HybridWebView"/>.
    /// </summary>
    public class HybridWebViewProxyEventArgs
    {
        /// <summary>
        /// Creates a new instance of <see cref="HybridWebViewProxyEventArgs"/>.
        /// </summary>
        /// <param name="fullUrl">The full request URL.</param>
        public HybridWebViewProxyEventArgs(string fullUrl, string? method, IDictionary<string, string>? headers, Stream? requestStream)
        {
            Url = fullUrl;
            QueryParams = QueryStringHelper.GetKeyValuePairs(fullUrl);
            RequestHeaders = headers ?? new Dictionary<string, string>();
            RequestStream = requestStream;

            if (Enum.TryParse(method ?? "GET", true, out HttpMethod methodResult))
            {
                Method = methodResult;
            }
        }

        /// <summary>
        /// The full request URL.
        /// </summary>
        public string Url { get; }

        /// <summary>
        /// The request method.
        /// </summary>
        public HttpMethod Method { get; }

        /// <summary>
        /// Query string values extracted from the request URL.
        /// </summary>
        public IDictionary<string, string> QueryParams { get; }

        /// <summary>
        /// Header strings values extracted from the request.
        /// </summary>
        public IDictionary<string, string> RequestHeaders { get; }

        /// <summary>
        /// Stream values extracted from the request.
        /// </summary>
        public Stream? RequestStream { get; }

        /// <summary>
        /// The response headers to be used to respond to the request.
        /// </summary>
        public IDictionary<string, string>? ResponseHeaders { get; set; }

        /// <summary>
        /// The response content type.
        /// </summary>
        public string? ResponseContentType { get; set; } = "text/plain";

        /// <summary>
        /// The response stream to be used to respond to the request.
        /// </summary>
        public Stream? ResponseStream { get; set; } = null;

        public int? ResponseStatusCode { get; set; } 

    }

    public enum HttpMethod
    {
        GET,
        POST,
        PUT,
        DELETE,
        OPTIONS
    }
}
