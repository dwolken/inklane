#import <WebKit/WebKit.h>

NS_ASSUME_NONNULL_BEGIN

// Xcode 16.4's iOS 18.5 simulator lacks libswiftWebKit for the Swift-refined
// JavaScript methods. Exercise the same WebKit APIs through their Objective-C entry points.
static inline void InklaneTestJavaScript(WKWebView *view, NSString *script,
                                         NSDictionary<NSString *, id> * _Nullable arguments,
                                         void (^completion)(id _Nullable, NSError * _Nullable)) {
    if (arguments) {
        [view callAsyncJavaScript:script arguments:arguments inFrame:nil
                   inContentWorld:WKContentWorld.pageWorld completionHandler:completion];
    } else {
        [view evaluateJavaScript:script completionHandler:completion];
    }
}

NS_ASSUME_NONNULL_END
