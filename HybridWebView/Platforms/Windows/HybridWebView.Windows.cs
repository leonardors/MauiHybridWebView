﻿using Microsoft.Web.WebView2.Core;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Storage.Streams;

namespace HybridWebView
{
    partial class HybridWebView
    {
        // Using an IP address means that WebView2 doesn't wait for any DNS resolution,
        // making it substantially faster. Note that this isn't real HTTP traffic, since
        // we intercept all the requests within this origin.
        private static readonly string AppHostAddress = "0.0.0.0";

        /// <summary>
        /// Gets the application's base URI. Defaults to <c>https://0.0.0.0/</c>
        /// </summary>
        private static readonly string AppOrigin = $"https://{AppHostAddress}/";

        private static readonly Uri AppOriginUri = new(AppOrigin);

        private CoreWebView2Environment? _coreWebView2Environment;

        private Microsoft.UI.Xaml.Controls.WebView2 PlatformWebView => (Microsoft.UI.Xaml.Controls.WebView2)Handler!.PlatformView!;

        private partial async Task InitializeHybridWebView()
        {
            PlatformWebView.WebMessageReceived += Wv2_WebMessageReceived;

            _coreWebView2Environment = await CoreWebView2Environment.CreateAsync();

            await PlatformWebView.EnsureCoreWebView2Async();

            PlatformWebView.CoreWebView2.Settings.AreDevToolsEnabled = EnableWebDevTools;
            PlatformWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            PlatformWebView.CoreWebView2.AddWebResourceRequestedFilter($"{AppOrigin}*", CoreWebView2WebResourceContext.All);
            PlatformWebView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;
        }

        private partial void NavigateCore(string url)
        {
            PlatformWebView.Source = new Uri(new Uri(AppOriginUri, url).ToString());
        }

        private async void CoreWebView2_WebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs eventArgs)
        {
            // Get a deferral object so that WebView2 knows there's some async stuff going on. We call Complete() at the end of this method.
            using var deferral = eventArgs.GetDeferral();

            var requestUri = QueryStringHelper.RemovePossibleQueryString(eventArgs.Request.Uri);
            var method = eventArgs.Request.Method;
            var headers = eventArgs.Request.Headers.ToDictionary(p => p.Key, p => p.Value);

            if (new Uri(requestUri) is Uri uri && AppOriginUri.IsBaseOf(uri))
            {
                var relativePath = AppOriginUri.MakeRelativeUri(uri).ToString().Replace('/', '\\');

                string contentType;
                if (string.IsNullOrEmpty(relativePath))
                {
                    relativePath = MainFile;
                    contentType = "text/html";
                }
                else
                {
                    var requestExtension = Path.GetExtension(relativePath);
                    contentType = requestExtension switch
                    {
                        ".htm" or ".html" => "text/html",
                        ".js" => "application/javascript",
                        ".css" => "text/css",
                        _ => "text/plain",
                    };
                }

                Stream? contentStream = null;
                IDictionary<string, string>? responseHeaders = null;

                // Check to see if the request is a proxy request
                if (relativePath == ProxyRequestPath || relativePath?.StartsWith($"{HybridWebView.ProxyRequestPath}\\") == true)
                {
                    var fullUrl = eventArgs.Request.Uri;

                    var args = new HybridWebViewProxyEventArgs(fullUrl, method, headers);
                    await OnProxyRequestMessage(args);

                    if (args.ResponseStream != null)
                    {
                        contentType = args.ResponseContentType ?? "text/plain";
                        contentStream = args.ResponseStream;
                        responseHeaders = args.ResponseHeaders;
                    }
                }

                if (contentStream is null)
                {
                    contentStream = KnownStaticFileProvider.GetKnownResourceStream(relativePath!);
                }

                if (contentStream is null)
                {
                    var assetPath = Path.Combine(HybridAssetRoot!, relativePath!);
                    contentStream = await GetAssetStreamAsync(assetPath);
                }

                if (contentStream is null)
                {
                    var notFoundContent = "Resource not found (404)";
                    eventArgs.Response = _coreWebView2Environment!.CreateWebResourceResponse(
                        Content: null,
                        StatusCode: 404,
                        ReasonPhrase: "Not Found",
                        Headers: GetHeaderString("text/plain", notFoundContent.Length, responseHeaders)
                    );
                }
                else
                {
                    var randomStream = await CopyContentToRandomAccessStreamAsync(contentStream);

                    eventArgs.Response = _coreWebView2Environment!.CreateWebResourceResponse(
                        Content: randomStream,
                        StatusCode: 200,
                        ReasonPhrase: "OK",
                        Headers: GetHeaderString(contentType, (int)randomStream.Size, headers)
                    );

                    randomStream = null;
                }

                contentStream?.Dispose();
            }

            // Notify WebView2 that the deferred (async) operation is complete and we set a response.
            deferral.Complete();

            async Task<IRandomAccessStream> CopyContentToRandomAccessStreamAsync(Stream content)
            {
                using var memStream = new MemoryStream();
                await content.CopyToAsync(memStream);
                var randomAccessStream = new InMemoryRandomAccessStream();
                await randomAccessStream.WriteAsync(memStream.GetWindowsRuntimeBuffer());
                return randomAccessStream;
            }
        }

        private protected static string GetHeaderString(string contentType, int contentLength, IDictionary<string, string>? baseHeaders)
        {
            if (baseHeaders == null) baseHeaders = new Dictionary<string, string>();

            if (baseHeaders.ContainsKey("Content-Type") == false)
            {
                baseHeaders["Content-Type"] = contentType;
            }

            if (baseHeaders.ContainsKey("Content-Length") == false)
            {
                baseHeaders["Content-Length"] = contentLength.ToString();
            }

            var valuesFormated = baseHeaders.Select(h => $"{h.Key}: {h.Value}");
            var result = $@"{string.Join("\n", valuesFormated)}";

            return result;
        }

        private void Wv2_WebMessageReceived(Microsoft.UI.Xaml.Controls.WebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            OnMessageReceived(args.TryGetWebMessageAsString());
        }
    }
}
