using System;
using UnityEngine;
using UnityEngine.UI;

namespace DVRAT
{
    /// <summary>Sets the label text every frame, for values that change faster than a data refresh.</summary>
    public class LiveText : MonoBehaviour
    {
        private Func<string> _text;
        private Text _label;

        public static LiveText Attach(Text label, Func<string> text)
        {
            var live = label.gameObject.AddComponent<LiveText>();
            live._label = label;
            live._text = text;
            live.Update();
            return live;
        }

        private void Update()
        {
            _label.text = _text();
        }
    }
}
