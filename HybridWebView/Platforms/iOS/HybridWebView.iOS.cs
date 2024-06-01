using System.Diagnostics;
using Foundation;
using ObjCRuntime;
using WebKit;

namespace HybridWebView
{
    partial class HybridWebView
    {
        internal const string AppHostAddress = "0.0.0.0";

        internal const string AppOrigin = "app://" + AppHostAddress + "/";

        internal static readonly Uri AppOriginUri = new(AppOrigin);

        private WKWebView PlatformWebView => (WKWebView)Handler!.PlatformView!;

        private partial Task InitializeHybridWebView()
        {
            return Task.CompletedTask;
        }

        private partial void NavigateCore(string url)
        {
            using var nsUrl = new NSUrl(new Uri(AppOriginUri, url).ToString());
            using var request = new NSUrlRequest(nsUrl);

            PlatformWebView.LoadRequest(request);
            PlatformWebView.NavigationDelegate = new CustomNavigationDelegate();
            PlatformWebView.UIDelegate = new CustomUiDelegate();

            PlatformWebView.Configuration.Preferences.SetValueForKey((NSString)"TRUE",  (NSString)"allowFileAccessFromFileURLs");
            // PlatformWebView.SetValueForKey((NSString)"TRUE",  (NSString)"allowUniversalAccessFromFileURLs");
        }
    }

    public class CustomUiDelegate : WKUIDelegate
    {
         
    }


    public class CustomNavigationDelegate : WKNavigationDelegate
    {
        public override void DecidePolicy(WKWebView webView, WKNavigationAction navigationAction, Action<WKNavigationActionPolicy> decisionHandler)
        {
            Debug.WriteLine($"DecidePolicy: {navigationAction.Request.Url.AbsoluteString}");

            if (navigationAction.Request.Url.Scheme == "app")
            {
                // Allow the app scheme
                decisionHandler(WKNavigationActionPolicy.Allow);
            }
            else if (navigationAction.Request.Url.Scheme == "https")
            {
                // Allow the https scheme
                decisionHandler(WKNavigationActionPolicy.Allow);
            }
            else
            {
                // Cancel for other schemes
                decisionHandler(WKNavigationActionPolicy.Cancel);
            }
        }

        // public override void DidFailNavigation(WKWebView webView, WKNavigation navigation, NSError error)
        // {
        //     base.DidFailNavigation(webView, navigation, error);
        // }

        // public override void DidFailProvisionalNavigation(WKWebView webView, WKNavigation navigation, NSError error)
        // {
        //     base.DidFailProvisionalNavigation(webView, navigation, error);
        // }

        public override void DidFailProvisionalNavigation(WKWebView webView, WKNavigation navigation, NSError error)
        {
            Console.WriteLine($"DidFailProvisionalNavigation: {error.LocalizedDescription}");
            // Adicione aqui o código para tratar o erro
        }

        public override void DidFailNavigation(WKWebView webView, WKNavigation navigation, NSError error)
        {
            Console.WriteLine($"DidFailNavigation: {error.LocalizedDescription}");
            // Adicione aqui o código para tratar o erro
        }

        public override void DidFinishNavigation(WKWebView webView, WKNavigation navigation)
        {
            Console.WriteLine("DidFinishNavigation");
        }

        public override void DidStartProvisionalNavigation(WKWebView webView, WKNavigation navigation)
        {
            Console.WriteLine("DidStartProvisionalNavigation");
        }

        public override void DidReceiveServerRedirectForProvisionalNavigation(WKWebView webView, WKNavigation navigation)
        {
            Console.WriteLine("DidReceiveServerRedirectForProvisionalNavigation");
            base.DidReceiveServerRedirectForProvisionalNavigation(webView, navigation);
        }

        public override void ContentProcessDidTerminate(WKWebView webView)
        {
            Debug.WriteLine("Web content process terminated.");
        }

        public override void NavigationActionDidBecomeDownload(WKWebView webView, WKNavigationAction navigationAction, WKDownload download)
        {
            Debug.WriteLine("NavigationActionDidBecomeDownload.");
        }

        public override void ShouldAllowDeprecatedTls(WKWebView webView, NSUrlAuthenticationChallenge challenge,  Action<bool> decisionHandler)
        {
            base.ShouldAllowDeprecatedTls(webView, challenge, decisionHandler);
        }

        // public override void DecidePolicy(WKWebView webView, WKNavigationAction navigationAction, Action<WKNavigationActionPolicy> decisionHandler)
        // {
        //     Console.WriteLine($"DecidePolicy: {navigationAction.Request.Url.AbsoluteString}");
        //     decisionHandler(WKNavigationActionPolicy.Allow);
        // }
    }
}
