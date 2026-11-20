using System;
using System.Collections;
using UnityEngine;

namespace DroidHero.Core
{
    /// <summary>
    /// Opens the device's audio picker and returns the copied file path.
    ///
    /// Android goes through the Java plugin; in the editor a native file dialog is used so the
    /// feature can be tested without a device.
    /// </summary>
    public class LocalAudioPicker
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        const string PickerClass = "com.droidhero.picker.DroidHeroFilePicker";
#endif

        public bool IsSupported =>
#if UNITY_ANDROID && !UNITY_EDITOR
            true;
#else
            Application.isEditor || Application.platform == RuntimePlatform.WindowsPlayer ||
            Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.OSXPlayer;
#endif

        /// <summary>Opens the picker. Yields until a file is chosen or the picker is cancelled.</summary>
        public IEnumerator PickRoutine(Action<string> onPicked, Action<string> onError)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            yield return PickAndroid(onPicked, onError);
#else
            yield return PickDesktop(onPicked, onError);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        IEnumerator PickAndroid(Action<string> onPicked, Action<string> onError)
        {
            using (var picker = new AndroidJavaClass(PickerClass))
            using (var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                       .GetStatic<AndroidJavaObject>("currentActivity"))
            {
                picker.CallStatic("initialize", activity);
                picker.CallStatic("pickAudioFile");

                while (picker.CallStatic<bool>("isPending"))
                {
                    yield return null;
                }

                string path = picker.CallStatic<string>("getPickedPath");
                if (!string.IsNullOrEmpty(path))
                {
                    onPicked?.Invoke(path);
                    yield break;
                }

                string error = picker.CallStatic<string>("getLastError");
                onError?.Invoke(string.IsNullOrEmpty(error) ? "No file selected." : error);
            }
        }
#else
        IEnumerator PickDesktop(Action<string> onPicked, Action<string> onError)
        {
            string path = null;

            // Unity's editor file panel is blocking, so it runs on the main thread as usual.
            string startDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (!System.IO.Directory.Exists(startDirectory)) startDirectory = null;

#if UNITY_EDITOR
            path = UnityEditor.EditorUtility.OpenFilePanel("Pick an audio file", startDirectory ?? "",
                                                           "wav,ogg");
#else
            path = null;
            onError?.Invoke("Picking local audio needs the Android build or the Unity editor.");
#endif

            yield return null;

            if (!string.IsNullOrEmpty(path))
            {
                onPicked?.Invoke(path);
            }
            else
            {
                onError?.Invoke("No file selected.");
            }
        }
#endif
    }
}
