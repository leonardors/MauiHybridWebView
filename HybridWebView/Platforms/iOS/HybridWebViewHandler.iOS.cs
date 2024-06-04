﻿using Foundation;
using Microsoft.Maui.Platform;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Reflection.Metadata;
using System.Runtime.Versioning;
using WebKit;

namespace HybridWebView
{
    partial class HybridWebViewHandler
    {
        protected override WKWebView CreatePlatformView()
        {
            var config = new WKWebViewConfiguration();
            config.UserContentController.AddScriptMessageHandler(new WebViewScriptMessageHandler(MessageReceived), "webwindowinterop");
            config.SetUrlSchemeHandler(new SchemeHandler(this), urlScheme: "app");
            config.LimitsNavigationsToAppBoundDomains = false;

            // Legacy Developer Extras setting.
            var enableWebDevTools = ((HybridWebView)VirtualView).EnableWebDevTools;
            config.Preferences.SetValueForKey(NSObject.FromObject(enableWebDevTools), new NSString("developerExtrasEnabled"));

            var platformView = new MauiWKWebView(RectangleF.Empty, this, config);

            if (OperatingSystem.IsMacCatalystVersionAtLeast(major: 13, minor: 3) ||
                OperatingSystem.IsIOSVersionAtLeast(major: 16, minor: 4))
            {
                // Enable Developer Extras for Catalyst/iOS builds for 16.4+
                platformView.SetValueForKey(NSObject.FromObject(enableWebDevTools), new NSString("inspectable"));
            }

            return platformView;
        }

        private void MessageReceived(Uri uri, string message)
        {
            ((HybridWebView)VirtualView).OnMessageReceived(message);
        }

        private sealed class WebViewScriptMessageHandler : NSObject, IWKScriptMessageHandler
        {
            private Action<Uri, string> _messageReceivedAction;

            public WebViewScriptMessageHandler(Action<Uri, string> messageReceivedAction)
            {
                _messageReceivedAction = messageReceivedAction ?? throw new ArgumentNullException(nameof(messageReceivedAction));
            }

            public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
            {
                if (message is null)
                {
                    throw new ArgumentNullException(nameof(message));
                }
                _messageReceivedAction(HybridWebView.AppOriginUri, ((NSString)message.Body).ToString());
            }
        }

        private class SchemeHandler : NSObject, IWKUrlSchemeHandler
        {
            private readonly HybridWebViewHandler _webViewHandler;

            public SchemeHandler(HybridWebViewHandler webViewHandler)
            {
                _webViewHandler = webViewHandler;
            }

            [Export("webView:startURLSchemeTask:")]
            [SupportedOSPlatform("ios11.0")]
            public async void StartUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
            {

                var responseData = await GetResponseBytes(urlSchemeTask);
                var locationKey = (NSString)"Location";

                var keys = responseData.headers?.Keys?.Select(p => new NSString(p)) ?? Array.Empty<NSString>();
                var values = responseData.headers?.Values?.Select(p => new NSString(p)) ?? Array.Empty<NSString>();

                using (var dic = new NSMutableDictionary<NSString, NSString>(keys.ToArray(), values.ToArray()))
                {
                    dic.Add((NSString)"Access-Control-Allow-Origin", (NSString)("*"));
                    dic.Add((NSString)"Access-Control-Allow-Methods", (NSString)("GET, POST, PUT, DELETE, OPTIONS"));
                    dic.Add((NSString)"Access-Control-Allow-Credentials", (NSString)("true"));
                    // dic.Add((NSString)"Access-Control-Allow-Headers", (NSString)("*"));

                    // Handle redirection if necessary
                    if (responseData.StatusCode >= 300 && responseData.StatusCode < 400 && dic.ContainsKey(locationKey))
                    {
                        // var location = (NSString)"https://digitalpages.com.br";// dic[locationKey];
                        var location = dic[locationKey];
                        var newUrl = new NSUrl(location);
                        var redirectResponse = new NSUrlResponse(urlSchemeTask.Request.Url, "text/html", 0, string.Empty);

                        urlSchemeTask.DidReceiveResponse(redirectResponse);
                        urlSchemeTask.DidFinish();
                        return;
                    }

                    // Disable local caching. This will prevent user scripts from executing correctly.
                    dic.Add((NSString)"Cache-Control", (NSString)"no-cache, max-age=0, must-revalidate, no-store");
                    // dic.Add((NSString)"Content-Security-Policy", (NSString)"frame-src 'self' https://* app://* app://*");

                    if (dic.ContainsKey((NSString)"Content-Length") == false && responseData.ResponseStream != null)
                    {
                        dic.Add((NSString)"Content-Length", (NSString)(responseData.ResponseStream.Length.ToString(CultureInfo.InvariantCulture)));
                    }

                    if (dic.ContainsKey((NSString)"Content-Type") == false)
                    {
                        dic.Add((NSString)"Content-Type", (NSString)responseData.ContentType);
                    }

                    if (urlSchemeTask.Request.Url != null)
                    {
                        using var response = new NSHttpUrlResponse(urlSchemeTask.Request.Url, responseData.StatusCode, "HTTP/1.1", dic);
                        urlSchemeTask.DidReceiveResponse(response);
                    }
                }

                if (responseData.ResponseStream != null) {
                    
                    Console.WriteLine($"response: {urlSchemeTask.Request?.Url?.AbsoluteString}  size: {responseData.ResponseStream.Length} position: {responseData.ResponseStream.Position}");
                    var data = NSData.FromStream(responseData.ResponseStream);
                    if (data != null) urlSchemeTask.DidReceiveData(data);
                }

                urlSchemeTask.DidFinish();
            }

            private async Task<(Stream? ResponseStream, string ContentType, int StatusCode, IDictionary<string, string>? headers)> GetResponseBytes(IWKUrlSchemeTask urlSchemeTask)
            {
                var url = urlSchemeTask.Request.Url?.AbsoluteString ?? "";
                int? statusCode = null;
                
                string contentType;

                string fullUrl = url;
                url = QueryStringHelper.RemovePossibleQueryString(url);

                if (new Uri(url) is Uri uri && HybridWebView.AppOriginUri.IsBaseOf(uri))
                {
                    var relativePath = HybridWebView.AppOriginUri.MakeRelativeUri(uri).ToString().Replace('\\', '/');

                    var hwv = (HybridWebView)_webViewHandler.VirtualView;

                    var bundleRootDir = Path.Combine(NSBundle.MainBundle.ResourcePath, hwv.HybridAssetRoot!);

                    Debug.WriteLine($"Relative path: {relativePath}");

                    if (string.IsNullOrEmpty(relativePath))
                    {
                        relativePath = hwv.MainFile!.Replace('\\', '/');
                        contentType = "text/html";
                    }
                    else
                    {
                        contentType = relativePath.MimeType();
                    }

                    Stream? contentStream = null;
                    IDictionary<string, string>? responseHeaders = null;

                    // Check to see if the request is a proxy request.
                    if (relativePath == HybridWebView.ProxyRequestPath || relativePath?.StartsWith($"{HybridWebView.ProxyRequestPath}/") == true)
                    {
                        var method = urlSchemeTask.Request.HttpMethod;
                        var requestHeaders = urlSchemeTask.Request.Headers?.ToDictionary(p => p.Key.ToString(), p => p.Value.ToString());
                        MemoryStream? requestData = null;
                        
                        if (urlSchemeTask.Request?.Body != null)
                        {
                            requestData = new MemoryStream(urlSchemeTask.Request.Body.ToArray());
                        }
                        
                        var args = new HybridWebViewProxyEventArgs(fullUrl, method, requestHeaders, requestData);

                        await hwv.OnProxyRequestMessage(args);

                        if (args.ResponseStatusCode != null)
                        {
                            contentType = args.ResponseContentType ?? "text/plain";
                            contentStream = args.ResponseStream;
                            responseHeaders = args.ResponseHeaders;
                            statusCode = args.ResponseStatusCode ?? statusCode;
                        }
                    }
                   
                   if (relativePath?.Contains("/default/target") == true)
                   {
                    var s = 1;;;
                   }

                    if (statusCode == null)
                    {
                        contentStream = KnownStaticFileProvider.GetKnownResourceStream(relativePath!);
                        if (contentStream != null) statusCode = 200;
                    }

                    if (statusCode != null)
                    {
                        return (contentStream, contentType, StatusCode: statusCode.Value, responseHeaders);
                    }

                    var assetPath = Path.Combine(bundleRootDir, relativePath);

                    if (File.Exists(assetPath))
                    {
                        return (File.OpenRead(assetPath), contentType, StatusCode: 200, responseHeaders);
                    }
                }

                return (new MemoryStream(), ContentType: string.Empty, StatusCode: 404, null);
            }
            

            [Export("webView:stopURLSchemeTask:")]
            public void StopUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
            {
            }
        }

        
    }
}
