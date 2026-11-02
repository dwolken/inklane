package dev.inklane.android;

import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.provider.OpenableColumns;
import android.util.Base64;

import org.json.JSONObject;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.OutputStream;
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** Stages one complete workspace copy privately before opening the system destination picker. */
final class NativeExport {
    private static final long LIMIT = 100L * 1024 * 1024;
    private final WorkspaceActivity activity;
    private final ExecutorService io = Executors.newSingleThreadExecutor();
    private File staged;
    private FileOutputStream output;
    private String name;
    private long declared, received;
    private boolean choosing;

    NativeExport(WorkspaceActivity activity) {
        this.activity = activity;
        // A process killed during a picker can leave its private staging copy.
        io.execute(() -> {
            File[] abandoned = activity.getCacheDir().listFiles((dir, n) -> n.startsWith("inklane-copy-") && n.endsWith(".tmp"));
            if (abandoned != null) for (File file : abandoned) file.delete();
        });
    }

    void receive(String message) {
        io.execute(() -> {
            try {
                if (message == null || message.length() > 67000 || choosing) throw new IOException();
                JSONObject request = new JSONObject(message);
                switch (request.getString("op")) {
                    case "begin":
                        reset();
                        name = request.getString("name");
                        declared = request.getLong("size");
                        if (!allowed(name) || declared <= 0 || declared > LIMIT) throw new IOException();
                        staged = File.createTempFile("inklane-copy-", ".tmp", activity.getCacheDir());
                        output = new FileOutputStream(staged);
                        reply("ok");
                        break;
                    case "chunk":
                        if (output == null) throw new IOException();
                        byte[] bytes = Base64.decode(request.getString("data"), Base64.NO_WRAP);
                        if (bytes.length == 0 || bytes.length > 49152 || bytes.length > declared - received) throw new IOException();
                        output.write(bytes);
                        received += bytes.length;
                        reply("ok");
                        break;
                    case "save":
                        if (output == null || received != declared) throw new IOException();
                        output.close();
                        output = null;
                        choosing = true;
                        String title = name;
                        activity.runOnUiThread(() -> {
                            try { activity.pickDestination(title); }
                            catch (RuntimeException e) { fail(); }
                        });
                        break;
                    case "abort":
                        reset();
                        reply("ok");
                        break;
                    default: throw new IOException();
                }
            } catch (Exception e) {
                reset();
                reply("error");
            }
        });
    }

    private static boolean allowed(String title) {
        if (title.length() > 255 || title.trim().isEmpty() || title.startsWith(".") || title.matches(".*[\\p{Cntrl}/\\\\].*")) return false;
        String lower = title.toLowerCase(Locale.ROOT);
        return lower.endsWith(".inklane") || lower.endsWith(".inklane.json") || lower.endsWith(".feedback.json");
    }

    void destination(Uri uri) {
        io.execute(() -> {
            if (!choosing) return;
            try {
                if (uri == null) { reply("cancel"); return; }
                if (!activity.canAccess(uri, Intent.FLAG_GRANT_WRITE_URI_PERMISSION)) throw new IOException();
                // Capture a new copy only. Providers can return an existing user-selected
                // item or change the filename. Inspect it before opening any writable stream.
                String extension = name.endsWith(".inklane") ? ".inklane" : name.endsWith(".feedback.json") ? ".feedback.json" : ".inklane.json";
                try (Cursor row = activity.getContentResolver().query(uri,
                        new String[]{OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE}, null, null, null)) {
                    if (row == null || !row.moveToFirst() || row.isNull(0) || !row.getString(0).toLowerCase(Locale.ROOT).endsWith(extension)) throw new IOException();
                    if (!row.isNull(1) && row.getLong(1) > 0) throw new IOException();
                }
                // A document provider may report no size. Verify the actual destination is empty.
                try (java.io.InputStream check = activity.getContentResolver().openInputStream(uri)) {
                    if (check == null || check.read() != -1) throw new IOException();
                }
                try (FileInputStream source = new FileInputStream(staged);
                     OutputStream target = activity.getContentResolver().openOutputStream(uri, "wt")) {
                    if (target == null) throw new IOException();
                    byte[] buffer = new byte[65536];
                    int length;
                    while ((length = source.read(buffer)) > 0) target.write(buffer, 0, length);
                    target.flush();
                }
                reply("ok");
            } catch (Exception e) { reply("error"); }
            finally { reset(); }
        });
    }

    private void reply(String status) {
        activity.runOnUiThread(() -> activity.reply("{\"status\":" + JSONObject.quote(status) + "}"));
    }
    private void fail() { io.execute(() -> { reset(); reply("error"); }); }
    private void reset() {
        if (output != null) try { output.close(); } catch (IOException ignored) { }
        output = null;
        if (staged != null) staged.delete();
        staged = null;
        choosing = false;
        received = 0;
    }
    void close() {
        io.execute(this::reset);
        io.shutdown();
    }
}
