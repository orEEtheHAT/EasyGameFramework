using System;
using UnityEngine;

namespace EasyNarrative
{
    [CreateAssetMenu(menuName = "EasyGameFramework/Narrative/Speaker Profile")]
    public sealed class SpeakerProfile : ScriptableObject
    {
        public string displayName = "Speaker", nameKey;
        public Sprite portrait;
        public Color subtitleColor = Color.white;
    }

    [Serializable]
    public sealed class DialogueVoice
    {
        [Tooltip("Locale code, for example en or ru. An empty locale is the fallback clip.")]
        public string locale;
        public AudioClip clip;

        public static AudioClip Find(DialogueVoice[] clips, string locale)
        {
            if (clips == null) return null;
            AudioClip fallback = null, language = null;
            int region = string.IsNullOrEmpty(locale) ? -1 : locale.IndexOf('-');
            foreach (var entry in clips)
            {
                if (entry == null || !entry.clip) continue;
                if (string.Equals(entry.locale, locale, StringComparison.OrdinalIgnoreCase)) return entry.clip;
                if (string.IsNullOrEmpty(entry.locale)) fallback = entry.clip;
                else if (region == entry.locale.Length && region > 0 &&
                         string.Compare(locale, 0, entry.locale, 0, region, StringComparison.OrdinalIgnoreCase) == 0)
                    language = entry.clip;
            }
            return language ? language : fallback;
        }
    }
}
