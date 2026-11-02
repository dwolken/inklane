package dev.inklane.android;

import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.ClipData;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Insets;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Process;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebMessage;
import android.webkit.WebMessagePort;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.view.WindowInsets;
import android.widget.Toast;

import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Locale;

public final class WorkspaceActivity extends Activity {
    private static final String HOST = "appassets.androidplatform.net";
    private static final String ORIGIN = "https://" + HOST;
    private static final String PAGE = ORIGIN + "/editor/index.html";
    private static final int OPEN = 20, SAVE = 21;
    private WebView web;
    private NativeExport exporter;
    private ValueCallback<Uri[]> chooser;
    private WebMessagePort port;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        getWindow().setDecorFitsSystemWindows(false);
        web = new WebView(this);
        setContentView(web);
        web.setOnApplyWindowInsetsListener((view, insets) -> {
            Insets bars = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.ime() | WindowInsets.Type.displayCutout());
            view.setPadding(bars.left, bars.top, bars.right, bars.bottom);
            return WindowInsets.CONSUMED;
        });
        web.requestApplyInsets();
        exporter = new NativeExport(this);
        WebView.setWebContentsDebuggingEnabled(BuildConfig.DEBUG);
        web.getSettings().setJavaScriptEnabled(true);
        web.getSettings().setDomStorageEnabled(true);
        web.getSettings().setAllowFileAccess(false);
        web.getSettings().setAllowContentAccess(false);
        web.getSettings().setAllowFileAccessFromFileURLs(false);
        web.getSettings().setAllowUniversalAccessFromFileURLs(false);
        web.getSettings().setMixedContentMode(android.webkit.WebSettings.MIXED_CONTENT_NEVER_ALLOW);
        web.setWebViewClient(new WebViewClient() {
            @Override public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                // The app has a single document host. File input goes through the system picker.
                return true;
            }
            @Override public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
                return bundled(request);
            }
            @Override public void onPageStarted(WebView view, String url, android.graphics.Bitmap icon) {
                if (port != null) { port.close(); port = null; }
            }
            @Override public void onPageFinished(WebView view, String url) {
                if (!PAGE.equals(url) || port != null) return;
                WebMessagePort[] channels = view.createWebMessageChannel();
                port = channels[0];
                port.setWebMessageCallback(new WebMessagePort.WebMessageCallback() {
                    @Override public void onMessage(WebMessagePort channel, WebMessage message) {
                        exporter.receive(message.getData());
                    }
                });
                view.postWebMessage(new WebMessage("inklane:files", new WebMessagePort[]{channels[1]}), Uri.parse(ORIGIN));
            }
        });
        web.setWebChromeClient(new WebChromeClient() {
            @Override public boolean onShowFileChooser(WebView view, ValueCallback<Uri[]> callback, FileChooserParams params) {
                if (chooser != null) chooser.onReceiveValue(null);
                chooser = callback;
                Intent open = new Intent(Intent.ACTION_OPEN_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("*/*");
                open.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, params.getMode() == FileChooserParams.MODE_OPEN_MULTIPLE);
                try { startActivityForResult(open, OPEN); }
                catch (ActivityNotFoundException ex) {
                    finishChooser(null);
                    Toast.makeText(WorkspaceActivity.this, R.string.open_error, Toast.LENGTH_LONG).show();
                }
                return true;
            }
        });
        if (Build.VERSION.SDK_INT >= 33) {
            getOnBackInvokedDispatcher().registerOnBackInvokedCallback(
                android.window.OnBackInvokedDispatcher.PRIORITY_DEFAULT, this::back);
        }
        web.loadUrl(PAGE);
    }

    private WebResourceResponse bundled(WebResourceRequest req) {
        Uri url = req.getUrl();
        String path = url.getPath();
        if (!"GET".equals(req.getMethod()) || !"https".equals(url.getScheme()) || !HOST.equals(url.getAuthority())
                || path == null || !(path.startsWith("/editor/") || path.startsWith("/assets/"))
                || path.contains("..") || path.contains("\\") || path.indexOf(0) >= 0) return missing();
        if (path.endsWith("/")) path += "index.html";
        try {
            return new WebResourceResponse(mime(path), null, 200, "OK",
                Collections.singletonMap("X-Content-Type-Options","nosniff"), getAssets().open(path.substring(1)));
        } catch (IOException ex) { return missing(); }
    }

    private static WebResourceResponse missing() {
        return new WebResourceResponse("text/plain","UTF-8",404,"Not Found", Collections.emptyMap(), new ByteArrayInputStream(new byte[0]));
    }

    private static String mime(String path) {
        String ext = path.substring(path.lastIndexOf('.') + 1).toLowerCase(Locale.ROOT);
        switch (ext) {
            case "js": case "mjs": return "text/javascript";
            case "html": return "text/html";
            case "css": return "text/css";
            case "png": return "image/png";
            case "svg": return "image/svg+xml";
            case "wasm": return "application/wasm";
            case "json": case "webmanifest": return "application/json";
            case "woff2": return "font/woff2";
            case "ttf": return "font/ttf";
            default: return "application/octet-stream";
        }
    }

    private void finishChooser(Uri[] files) {
        ValueCallback<Uri[]> callback = chooser;
        chooser = null;
        if (callback != null) callback.onReceiveValue(files);
    }

    boolean canAccess(Uri uri, int mode) {
        return uri != null && "content".equals(uri.getScheme()) && uri.getAuthority() != null
            && checkUriPermission(uri, Process.myPid(), Process.myUid(), mode) == PackageManager.PERMISSION_GRANTED;
    }

    @Override protected void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request, result, data);
        if (request == SAVE) {
            exporter.destination(result == RESULT_OK && data != null ? data.getData() : null);
        } else if (request == OPEN) {
            ArrayList<Uri> items = new ArrayList<>();
            if (result == RESULT_OK && data != null) {
                ClipData clip = data.getClipData();
                if (clip != null && clip.getItemCount() <= 32) {
                    for (int i=0;i<clip.getItemCount();i++) items.add(clip.getItemAt(i).getUri());
                } else if (clip == null && data.getData() != null) items.add(data.getData());
            }
            boolean readable = !items.isEmpty();
            for (Uri uri : items) readable &= canAccess(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION);
            finishChooser(readable ? items.toArray(new Uri[0]) : null);
        }
    }

    void pickDestination(String name) {
        String type = name.endsWith(".json") ? "application/json" : "application/octet-stream";
        Intent intent = new Intent(Intent.ACTION_CREATE_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType(type);
        intent.putExtra(Intent.EXTRA_TITLE, name);
        startActivityForResult(intent, SAVE);
    }

    void reply(String json) {
        if (port != null && !isFinishing() && !isDestroyed()) port.postMessage(new WebMessage(json));
    }

    private void back() {
        // Keep a live unsaved document when returning to the Android launcher.
        web.evaluateJavascript("(()=>{const d=document.querySelector('dialog[open]');if(d){d.dispatchEvent(new Event('cancel',{cancelable:true}));if(d.open)d.close();return true}const m=document.querySelector('details[open]');if(m){m.open=false;return true}return false})()", handled -> {
            if (!"true".equals(handled)) moveTaskToBack(true);
        });
    }
    @Override public void onBackPressed() { back(); }

    @Override protected void onDestroy() {
        finishChooser(null);
        if (port != null) { port.close(); port = null; }
        exporter.close();
        web.destroy();
        super.onDestroy();
    }
}
