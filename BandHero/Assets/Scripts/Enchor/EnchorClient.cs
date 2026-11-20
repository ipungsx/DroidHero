using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace BandHero.Enchor
{
    [Serializable]
    public class EnchorChart
    {
        public string name;
        public string artist;
        public string album;
        public string genre;
        public int year;
        public string charter;
        public string md5;
        public string chartHash;
        public string albumArtMd5;
        public int song_length;
        public int diff_guitar;
        public int diff_bass;
        public int diff_drums;
        public int diff_vocals;
        public int diff_keys;

        public float LengthSeconds => song_length / 1000f;

        public string DisplayLength
        {
            get
            {
                int total = Mathf.Max(0, song_length / 1000);
                return (total / 60) + ":" + (total % 60).ToString("00");
            }
        }
    }

    [Serializable]
    public class EnchorSearchResult
    {
        public int found;
        public int out_of;
        public int page;
        public EnchorChart[] data;
        public float search_time_ms;
    }

    /// <summary>
    /// Client for the enchor.us chart search API and its .sng download endpoint.
    ///
    /// The search endpoint accepts a JSON body and returns chart metadata; charts themselves are
    /// static files addressed by their MD5.
    /// </summary>
    public class EnchorClient
    {
        public const string ApiBase = "https://api.enchor.us";
        public const string FilesBase = "https://files.enchor.us";
        public const int PageSize = 10;

        public const string InstrumentGuitar = "guitar";
        public const string InstrumentBass = "bass";
        public const string InstrumentDrums = "drums";
        public const string InstrumentKeys = "keys";

        public string SongUrl(string md5) => FilesBase + "/" + md5 + ".sng";

        [Serializable]
        class SearchRequest
        {
            public string search;
            public int page;
            public string instrument;

            // Null (omitted) asks the API for every difficulty, which is what the song screen
            // wants now that difficulty is chosen per chart.
            public string difficulty;

            // The API validates this against an enum; "4" is rejected with a 400.
            public string drumType = "fourLane";
        }

        /// <summary>Searches charts. Yields the parsed result for use inside a coroutine.</summary>
        public IEnumerator Search(string query, int page, string instrument, string difficulty,
                                  Action<EnchorSearchResult> onSuccess, Action<string> onError)
        {
            // The API only accepts lowercase enum values (expert/hard/medium/easy); an empty
            // value is rejected, so an unfiltered search omits the field entirely.
            var body = new SearchRequest
            {
                search = query ?? "",
                page = Mathf.Max(1, page),
                instrument = (instrument ?? InstrumentGuitar).ToLowerInvariant(),
                difficulty = string.IsNullOrEmpty(difficulty) ? null : difficulty.ToLowerInvariant()
            };

            byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));

            using (var request = new UnityWebRequest(ApiBase + "/search", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(payload);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 20;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke("Search failed: " + request.error);
                    yield break;
                }

                EnchorSearchResult result;
                try
                {
                    result = JsonUtility.FromJson<EnchorSearchResult>(request.downloadHandler.text);
                }
                catch (Exception ex)
                {
                    onError?.Invoke("Could not read search results: " + ex.Message);
                    yield break;
                }

                if (result?.data == null)
                {
                    onError?.Invoke("Search returned no data.");
                    yield break;
                }

                onSuccess?.Invoke(result);
            }
        }

        /// <summary>Downloads a chart .sng by MD5, reporting progress from 0 to 1.</summary>
        public IEnumerator DownloadChart(string md5, Action<byte[]> onSuccess, Action<string> onError,
                                         Action<float> onProgress = null)
        {
            using (var request = UnityWebRequest.Get(SongUrl(md5)))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 60;

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    onProgress?.Invoke(request.downloadProgress);
                    yield return null;
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke("Download failed: " + request.error);
                    yield break;
                }

                onSuccess?.Invoke(request.downloadHandler.data);
            }
        }
    }
}
