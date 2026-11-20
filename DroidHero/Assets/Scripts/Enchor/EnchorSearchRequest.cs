using System.Text;

namespace DroidHero.Enchor
{
    /// <summary>Builds the JSON body sent to the enchor.us chart search endpoint.</summary>
    /// <remarks>
    /// The body is assembled by hand instead of with JsonUtility. The endpoint validates every
    /// enum field it receives and answers 400 for an empty value, so a filter that is not in use
    /// has to be absent from the body. JsonUtility cannot express that: it always writes every
    /// public field and serialises a null string as "", which is exactly the rejected payload.
    /// </remarks>
    public static class EnchorSearchRequest
    {
        public const string DefaultInstrument = "guitar";
        public const string DefaultDrumType = "fourLane";

        public static string Build(string search, int page, string instrument, string difficulty)
        {
            var json = new StringBuilder(160);

            json.Append("{\"search\":");
            AppendString(json, search ?? "");

            json.Append(",\"page\":").Append(page < 1 ? 1 : page);

            json.Append(",\"instrument\":");
            AppendString(json, Lower(instrument, DefaultInstrument));

            if (!string.IsNullOrEmpty(difficulty))
            {
                json.Append(",\"difficulty\":");
                AppendString(json, difficulty.ToLowerInvariant());
            }

            json.Append(",\"drumType\":");
            AppendString(json, DefaultDrumType);

            return json.Append('}').ToString();
        }

        static string Lower(string value, string fallback)
        {
            return string.IsNullOrEmpty(value) ? fallback : value.ToLowerInvariant();
        }

        static void AppendString(StringBuilder json, string value)
        {
            json.Append('"');

            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    case '\n': json.Append("\\n"); break;
                    case '\r': json.Append("\\r"); break;
                    case '\t': json.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            json.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            json.Append(c);
                        break;
                }
            }

            json.Append('"');
        }
    }
}
