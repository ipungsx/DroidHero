using System.Text;

namespace DroidHero.Core
{
    /// <summary>
    /// Editable search text.
    ///
    /// IMGUI's text field is the only widget that drives the platform keyboard, and that path is
    /// what took the enchor screen down on a device. The editing rules live here, free of
    /// UnityEngine, so they can be tested and the screen can draw the field with a plain label.
    /// </summary>
    public sealed class SearchTextEditor
    {
        public const int MaxLength = 64;

        readonly StringBuilder _text = new StringBuilder(MaxLength);

        public string Text => _text.ToString();

        public bool IsEmpty => _text.Length == 0;

        /// <summary>Replaces the whole value, as a platform keyboard reports it.</summary>
        public void Set(string value)
        {
            _text.Length = 0;
            Type(value);
        }

        public void Clear() => _text.Length = 0;

        /// <summary>
        /// Applies typed characters: backspace deletes, other control keys are dropped, and the
        /// length limit stops accepting characters without discarding the ones already there.
        /// </summary>
        public void Type(string typed)
        {
            if (string.IsNullOrEmpty(typed)) return;

            foreach (char c in typed)
            {
                if (c == '\b')
                {
                    Backspace();
                    continue;
                }

                if (char.IsControl(c)) continue;
                if (_text.Length >= MaxLength) continue;

                _text.Append(c);
            }
        }

        public void Backspace()
        {
            if (_text.Length > 0) _text.Length--;
        }
    }
}
