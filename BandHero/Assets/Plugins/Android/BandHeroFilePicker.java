package com.bandhero.picker;

import android.app.Activity;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.provider.OpenableColumns;
import android.util.Log;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;

/**
 * Bridges Unity to the Android Storage Access Framework so the player can pick an audio file
 * from their own device.
 *
 * Unity cannot read a content:// URI directly, so the picked file is copied into the app's
 * private storage and the resulting absolute path is handed back to C#.
 */
public class BandHeroFilePicker
{
    private static final String TAG = "BandHeroFilePicker";
    private static final int PICK_REQUEST = 4711;

    private static Activity activity;
    private static String lastPickedPath;
    private static String lastError;
    private static boolean pending;

    /** Called by Unity with the current activity. */
    public static void initialize(Activity currentActivity)
    {
        activity = currentActivity;
        lastPickedPath = null;
        lastError = null;
        pending = false;
    }

    /** Opens the system audio picker. Results arrive via {@link #getPickedPath()}. */
    public static void pickAudioFile()
    {
        if (activity == null)
        {
            lastError = "No activity available.";
            return;
        }

        pending = true;
        lastPickedPath = null;
        lastError = null;

        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType("audio/*");
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);

        try
        {
            activity.startActivityForResult(intent, PICK_REQUEST);
        }
        catch (Exception e)
        {
            pending = false;
            lastError = "Could not open the file picker: " + e.getMessage();
            Log.e(TAG, "pickAudioFile failed", e);
        }
    }

    /** True while the picker is open. */
    public static boolean isPending()
    {
        return pending;
    }

    /** Absolute path of the copied file, or null when nothing has been picked yet. */
    public static String getPickedPath()
    {
        return lastPickedPath;
    }

    public static String getLastError()
    {
        return lastError;
    }

    /** Called from the Unity activity's onActivityResult. */
    public static void onActivityResult(int requestCode, int resultCode, Intent data)
    {
        if (requestCode != PICK_REQUEST) return;

        pending = false;

        if (resultCode != Activity.RESULT_OK || data == null || data.getData() == null)
        {
            lastError = "No file selected.";
            return;
        }

        Uri uri = data.getData();

        try
        {
            String displayName = queryDisplayName(uri);
            String path = copyToPrivateStorage(uri, displayName);
            if (path != null)
            {
                lastPickedPath = path;
                lastError = null;
            }
        }
        catch (Exception e)
        {
            lastError = "Could not read the selected file: " + e.getMessage();
            Log.e(TAG, "onActivityResult failed", e);
        }
    }

    private static String queryDisplayName(Uri uri)
    {
        try (Cursor cursor = activity.getContentResolver().query(uri, null, null, null, null))
        {
            if (cursor != null && cursor.moveToFirst())
            {
                int index = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                if (index >= 0)
                {
                    String name = cursor.getString(index);
                    if (name != null && !name.isEmpty()) return name;
                }
            }
        }
        catch (Exception e)
        {
            Log.w(TAG, "Could not read display name", e);
        }

        return "picked_audio";
    }

    private static String copyToPrivateStorage(Uri uri, String displayName) throws Exception
    {
        File directory = new File(activity.getExternalFilesDir(null), "LocalAudio");
        if (!directory.exists() && !directory.mkdirs())
        {
            lastError = "Could not create the local audio folder.";
            return null;
        }

        File target = new File(directory, sanitize(displayName));

        try (InputStream input = activity.getContentResolver().openInputStream(uri);
             FileOutputStream output = new FileOutputStream(target))
        {
            if (input == null)
            {
                lastError = "Could not open the selected file.";
                return null;
            }

            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = input.read(buffer)) > 0)
            {
                output.write(buffer, 0, read);
            }
            output.flush();
        }

        return target.getAbsolutePath();
    }

    private static String sanitize(String name)
    {
        StringBuilder builder = new StringBuilder();
        for (char c : name.toCharArray())
        {
            builder.append(Character.isLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_');
        }

        String result = builder.toString();
        return result.isEmpty() ? "picked_audio" : result;
    }
}
