using CoreFoundation;
using Foundation;
using Microsoft.Maui.Platform;
using ObjCRuntime;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Net.Mime;
using System.Reflection.Metadata;
using System.Runtime.Versioning;
using System.Text.Json.Serialization;
using SystemConfiguration;
using WebKit;

namespace HybridWebView
{
    partial class HybridWebViewHandler
    {

        const string Rules = "[{\"trigger\":{\"url-filter\":\".*\"},\"action\":{\"type\":\"make-https\"}}]";

        protected override WKWebView CreatePlatformView()
        {
            var config = new WKWebViewConfiguration();
            config.UserContentController.AddScriptMessageHandler(new WebViewScriptMessageHandler(MessageReceived), "webwindowinterop");
            config.SetUrlSchemeHandler(new SchemeHandler(this), urlScheme: "app");
            config.LimitsNavigationsToAppBoundDomains = false;
            config.AllowsInlineMediaPlayback = true;
            config.WebsiteDataStore = WKWebsiteDataStore.DefaultDataStore;


            WKContentRuleListStore.DefaultStore.CompileContentRuleList("MWWKWebViewContentRules", Rules, (r, e)=>
            {
                if (e == null)
                {
                    config.UserContentController.AddContentRuleList(r);
                }
            });

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

            private List<IWKUrlSchemeTask> Pending {get;}

            public SchemeHandler(HybridWebViewHandler webViewHandler)
            {
                _webViewHandler = webViewHandler;
                Pending = new List<IWKUrlSchemeTask>();
            }

            [Export("webView:startURLSchemeTask:")]
            [SupportedOSPlatform("ios11.0")]
            public async void StartUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
            {
                Pending.Add(urlSchemeTask);

                try
                {

                    var responseData = await GetResponseBytes(urlSchemeTask);
                    var locationKey = (NSString)"Location";

                    var keys = responseData.headers?.Keys?.Select(p => new NSString(p)) ?? Array.Empty<NSString>();
                    var values = responseData.headers?.Values?.Select(p => new NSString(p)) ?? Array.Empty<NSString>();

                    var dic = new NSMutableDictionary<NSString, NSString>(keys.ToArray(), values.ToArray());

                    if (responseData.StatusCode >= 300 && responseData.StatusCode < 400 && dic.ContainsKey(locationKey))
                    {
                        var requestUrl = urlSchemeTask.Request.Url;
                        var redirectResponse = new NSHttpUrlResponse(requestUrl, responseData.StatusCode, "HTTP/1.1", dic);

                        urlSchemeTask.DidReceiveResponse(redirectResponse);
                        urlSchemeTask.DidFinish();
                        return;
                    }

                    if (dic.ContainsKey((NSString)"Content-Length") == false && responseData.ResponseStream != null)
                    {
                        dic.Add((NSString)"Content-Length", (NSString)(responseData.ResponseStream.Length.ToString(CultureInfo.InvariantCulture)));
                    }

                    if (dic.ContainsKey((NSString)"Cache-Control") == false)
                    {
                        dic.Add((NSString)"Cache-Control", (NSString)"no-cache, max-age=0, must-revalidate, no-store");
                    }

                    if (dic.ContainsKey((NSString)"Content-Type") == false)
                    {
                        dic.Add((NSString)"Content-Type", (NSString)responseData.ContentType);
                    }

                    if (dic.ContainsKey((NSString)"Access-Control-Allow-Origin"))
                    {
                        dic.Remove((NSString)"Access-Control-Allow-Origin");
                    }

                    dic.Add((NSString)"Access-Control-Expose-Headers", (NSString)string.Join(",", dic.Keys.ToList()));
                    dic.Add((NSString)"Access-Control-Allow-Origin", (NSString)"*");
                    dic.Add((NSString)"Access-Control-Allow-Credentials", (NSString)"true");
                    //dic.Add((NSString)"Accept-Ranges", (NSString)"bytes");

                    if (Pending.Contains(urlSchemeTask) == false) return;

                    var response = new NSHttpUrlResponse(urlSchemeTask.Request.Url, responseData.StatusCode, "HTTP/1.1", dic);
                    urlSchemeTask.DidReceiveResponse(response);

                    if (responseData.ResponseStream != null)
                    {
                        byte[] buffer = new byte[1024*10];
                        int bytesRead;

                        var stream = responseData.ResponseStream;

                        while ((bytesRead = responseData.ResponseStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            var dataChunk = NSData.FromArray(buffer.Take(bytesRead).ToArray());
                            urlSchemeTask.DidReceiveData(dataChunk);
                        }
                    }

                    urlSchemeTask.DidFinish();
                    responseData.ResponseStream = null;
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"StartUrlSchemeTask {e.Message}");
                }
                finally
                {
                    Pending.Remove(urlSchemeTask);
                }
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
                   
                    if (statusCode == null)
                    {
                        contentStream = KnownStaticFileProvider.GetKnownResourceStream(relativePath!);

                        if (contentStream != null)
                        {
                            statusCode = 200;

                            if (responseHeaders == null) responseHeaders = new Dictionary<string, string>();

                            responseHeaders["Cache-Control"] = $"public, max-age={TimeSpan.FromDays(1).TotalSeconds}, immutable";
                        }
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
                Debug.WriteLine($"cancelando request: {urlSchemeTask.Request.Url.AbsoluteString}");
                Pending.Remove(urlSchemeTask);
            }
        }

        
    }

    public class TestTask :  IWKUrlSchemeTask
    {
        public NativeHandle Handle => throw new NotImplementedException();

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
